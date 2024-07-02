using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.Tor.Http;
using WalletWasabi.Tor.Socks5.Pool.Circuits;
using WalletWasabi.WabiSabi;
using WalletWasabi.WabiSabi.Backend;
using WalletWasabi.WabiSabi.Backend.Models;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Backend.Rounds.CoinJoinStorage;
using WalletWasabi.WabiSabi.Backend.Statistics;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.WabiSabi.Client.CoinJoinProgressEvents;
using WalletWasabi.WabiSabi.Client.RoundStateAwaiters;
using WalletWasabi.WabiSabi.Models;
using WalletWasabi.WabiSabi.Models.MultipartyTransaction;
using Xunit;
using Xunit.Abstractions;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.WabiSabi.Client.CoinJoin.Client;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Integration;

/// <seealso cref="XunitConfiguration.SerialCollectionDefinition"/>
[Collection("Serial unit tests collection")]
public class WabiSabiHttpApiIntegrationTests : IClassFixture<WabiSabiApiApplicationFactory<Startup>>
{
	private readonly WabiSabiApiApplicationFactory<Startup> _apiApplicationFactory;
	private readonly ITestOutputHelper _output;

	public WabiSabiHttpApiIntegrationTests(WabiSabiApiApplicationFactory<Startup> apiApplicationFactory, ITestOutputHelper output)
	{
		_apiApplicationFactory = apiApplicationFactory;
		_output = output;
	}

	[Fact]
	public async Task RegisterSpentOrInNonExistentCoinAsync()
	{
		var httpClient = _apiApplicationFactory.CreateClient();

		var apiClient = await _apiApplicationFactory.CreateArenaClientAsync(httpClient);
		var rounds = (await apiClient.GetStatusAsync(RoundStateRequest.Empty, CancellationToken.None)).RoundStates;
		var round = rounds.First(x => x.CoinjoinState is ConstructionState);

		// If an output is not in the utxo dataset then it is not unspent, this
		// means that the output is spent or simply doesn't even exist.
		var nonExistingOutPoint = new OutPoint();
		using var signingKey = new Key();
		var ownershipProof = WabiSabiFactory.CreateOwnershipProof(signingKey, round.Id);

		var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(async () =>
		   await apiClient.RegisterInputAsync(round.Id, nonExistingOutPoint, ownershipProof, CancellationToken.None));

		Assert.Equal(WabiSabiProtocolErrorCode.InputSpent, ex.ErrorCode);
	}

	[Fact]
	public async Task RegisterBannedCoinAsync()
	{
		using CancellationTokenSource timeoutCts = new(TimeSpan.FromMinutes(2));

		using var signingKey = new Key();
		var coin = WabiSabiFactory.CreateCoin(signingKey);
		var bannedOutPoint = coin.Outpoint;

		var httpClient = _apiApplicationFactory.WithWebHostBuilder(builder =>
			builder.ConfigureServices(services =>
			{
				var rpc = BitcoinFactory.GetMockMinimalRpc();

				// Make the coordinator believe that the coins are real and
				// that they exist in the blockchain with many confirmations.
				rpc.OnGetTxOutAsync = (_, _, _) => new()
				{
					Confirmations = 101,
					IsCoinBase = false,
					ScriptPubKeyType = "witness_v0_keyhash",
					TxOut = coin.TxOut
				};
				services.AddScoped<IRPCClient>(s => rpc);

				var prison = WabiSabiFactory.CreatePrison();
				prison.FailedVerification(bannedOutPoint, uint256.One);
				services.AddScoped(_ => prison);
			})).CreateClient();

		var apiClient = await _apiApplicationFactory.CreateArenaClientAsync(httpClient);
		var rounds = (await apiClient.GetStatusAsync(RoundStateRequest.Empty, timeoutCts.Token)).RoundStates;
		var round = rounds.First(x => x.CoinjoinState is ConstructionState);

		// If an output is not in the utxo dataset then it is not unspent, this
		// means that the output is spent or simply doesn't even exist.
		var ownershipProof = WabiSabiFactory.CreateOwnershipProof(signingKey, round.Id);

		var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(async () =>
			await apiClient.RegisterInputAsync(round.Id, bannedOutPoint, ownershipProof, timeoutCts.Token));

		Assert.Equal(WabiSabiProtocolErrorCode.InputBanned, ex.ErrorCode);
		var inputBannedData = Assert.IsType<InputBannedExceptionData>(ex.ExceptionData);
		Assert.True(inputBannedData.BannedUntil > DateTimeOffset.UtcNow);
	}

	[Theory]
	[InlineData(new long[] { 10_000_000, 20_000_000, 30_000_000, 40_000_000, 100_000_000 })]
	public async Task SoloCoinJoinTestAsync(long[] amounts)
	{
		int inputCount = amounts.Length;

		// At the end of the test a coinjoin transaction has to be created and broadcasted.
		var transactionCompleted = new TaskCompletionSource<Transaction>();

		// Create a key manager and use it to create fake coins.
		_output.WriteLine("Creating key manager...");
		KeyManager keyManager = KeyManager.CreateNew(out _, password: "", Network.Main);

		var coins = GenerateSmartCoins(keyManager, amounts, inputCount);

		_output.WriteLine("Coins were created successfully");

		var httpClient = _apiApplicationFactory.WithWebHostBuilder(builder =>
			builder.AddMockRpcClient(
				coins,
				rpc =>

					// Make the coordinator believe that the transaction is being
					// broadcasted using the RPC interface. Once we receive this tx
					// (the `SendRawTransactionAsync` was invoked) we stop waiting
					// and finish the waiting tasks to finish the test successfully.
					rpc.OnSendRawTransactionAsync = (tx) =>
					{
						transactionCompleted.SetResult(tx);
						return tx.GetHash();
					})
			.ConfigureServices(services =>
			{
				// Instruct the coordinator DI container to use these two scoped
				// services to build everything (WabiSabi controller, arena, etc)
				services.AddScoped(s => new WabiSabiConfig
				{
					MaxInputCountByRound = inputCount - 1,  // Make sure that at least one IR fails for WrongPhase
					StandardInputRegistrationTimeout = TimeSpan.FromSeconds(60),
					ConnectionConfirmationTimeout = TimeSpan.FromSeconds(60),
					OutputRegistrationTimeout = TimeSpan.FromSeconds(60),
					TransactionSigningTimeout = TimeSpan.FromSeconds(60),
					MaxSuggestedAmountBase = Money.Satoshis(ProtocolConstants.MaxAmountPerAlice)
				});

				// Emulate that the first coin is coming from a coinjoin.
				services.AddScoped(s => new InMemoryCoinJoinIdStore(new[] { coins[0].Coin.Outpoint.Hash }));
			})).CreateClient();

		// Create the coinjoin client
		using PersonCircuit personCircuit = new();
		IHttpClient httpClientWrapper = new ClearnetHttpClient(httpClient);
		var apiClient = _apiApplicationFactory.CreateWabiSabiHttpApiClient(httpClient);

		var mockHttpClientFactory = new MockWasabiHttpClientFactory();

		mockHttpClientFactory.OnNewHttpClientWithPersonCircuit = () => (personCircuit, httpClientWrapper);
		mockHttpClientFactory.OnNewHttpClientWithCircuitPerRequest = () => httpClientWrapper;

		// Total test timeout.
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(200));
		cts.Token.Register(() => transactionCompleted.TrySetCanceled(), useSynchronizationContext: false);

		using var roundStateUpdater = new RoundStateUpdater(TimeSpan.FromSeconds(1), apiClient);

		await roundStateUpdater.StartAsync(CancellationToken.None);

		var coinJoinClient = WabiSabiFactory.CreateTestCoinJoinClient(mockHttpClientFactory, keyManager, roundStateUpdater);

		// Run the coinjoin client task.
		var coinjoinResult = await coinJoinClient.StartCoinJoinAsync(async () => await Task.FromResult(coins), true, cts.Token);
		Assert.True(coinjoinResult is SuccessfulCoinJoinResult);

		var broadcastedTx = await transactionCompleted.Task; // wait for the transaction to be broadcasted.
		Assert.NotNull(broadcastedTx);

		await roundStateUpdater.StopAsync(CancellationToken.None);
	}

	[Fact]
	public async Task FailToRegisterOutputsCoinJoinTestAsync()
	{
		long[] amounts = new long[] { 10_000_000, 20_000_000, 30_000_000 };
		int inputCount = amounts.Length;

		// At the end of the test a coinjoin transaction has to be created and broadcasted.
		var transactionCompleted = new TaskCompletionSource<Transaction>();

		// Create a key manager and use it to create fake coins.
		_output.WriteLine("Creating key manager...");
		KeyManager keyManager = KeyManager.CreateNew(out var _, password: "", Network.Main);

		var coins = GenerateSmartCoins(keyManager, amounts, inputCount);

		_output.WriteLine("Coins were created successfully");

		keyManager.AssertLockedInternalKeysIndexed(14, false);
		var outputScriptCandidates = keyManager
			.GetKeys(x => x.IsInternal && x.KeyState == KeyState.Locked)
			.Select(x => x.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit))
			.ToImmutableArray();

		var httpClient = _apiApplicationFactory.WithWebHostBuilder(builder =>
			builder
			.AddMockRpcClient(coins, rpc => { })
			.ConfigureServices(services =>
			{
				// Instruct the coordinator DI container to use this scoped
				// services to build everything (WabiSabi controller, arena, etc)
				services.AddScoped(s => new WabiSabiConfig
				{
					MaxInputCountByRound = inputCount,
					StandardInputRegistrationTimeout = TimeSpan.FromSeconds(60),
					ConnectionConfirmationTimeout = TimeSpan.FromSeconds(60),
					OutputRegistrationTimeout = TimeSpan.FromSeconds(60),
					TransactionSigningTimeout = TimeSpan.FromSeconds(60),
					MaxSuggestedAmountBase = Money.Satoshis(ProtocolConstants.MaxAmountPerAlice)
				});

				// Emulate that all our outputs had been already used in the past.
				// the server will prevent the registration and fail with a WabiSabiProtocolError.
				services.AddScoped(s => new CoinJoinScriptStore(outputScriptCandidates));
			})).CreateClient();

		// Create the coinjoin client
		using PersonCircuit personCircuit = new();
		IHttpClient httpClientWrapper = new ClearnetHttpClient(httpClient);
		var apiClient = _apiApplicationFactory.CreateWabiSabiHttpApiClient(httpClient);
		var mockHttpClientFactory = new MockWasabiHttpClientFactory();
		mockHttpClientFactory.OnNewHttpClientWithPersonCircuit = () => (personCircuit, httpClientWrapper);
		mockHttpClientFactory.OnNewHttpClientWithCircuitPerRequest = () => httpClientWrapper;

		// Total test timeout.
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(200));
		cts.Token.Register(() => transactionCompleted.TrySetCanceled(), useSynchronizationContext: false);

		using var roundStateUpdater = new RoundStateUpdater(TimeSpan.FromSeconds(1), apiClient);

		await roundStateUpdater.StartAsync(CancellationToken.None);

		var coinJoinClient = WabiSabiFactory.CreateTestCoinJoinClient(mockHttpClientFactory, keyManager, roundStateUpdater);

		bool failedBecauseNotAllAlicesSigned = false;
		void HandleCoinJoinProgress(object? sender, CoinJoinProgressEventArgs coinJoinProgress)
		{
			if (coinJoinProgress is RoundEnded roundEnded)
			{
				if (roundEnded.LastRoundState.EndRoundState is EndRoundState.NotAllAlicesSign)
				{
					failedBecauseNotAllAlicesSigned = true;
				}
				cts.Cancel(); // this is what we were waiting for so, end the test.
			}
		}

		try
		{
			coinJoinClient.CoinJoinClientProgress += HandleCoinJoinProgress;

			// Run the coinjoin client task.
			var coinjoinResult = await coinJoinClient.StartCoinJoinAsync(async () => await Task.FromResult(coins), true, cts.Token);
			if (coinjoinResult is SuccessfulCoinJoinResult)
			{
				throw new Exception("Coinjoin should have never finished successfully.");
			}
		}
		catch (OperationCanceledException)
		{
			Assert.True(failedBecauseNotAllAlicesSigned);
		}
		finally
		{
			coinJoinClient.CoinJoinClientProgress -= HandleCoinJoinProgress;
			await roundStateUpdater.StopAsync(CancellationToken.None);
		}
	}

	[Theory]
	[InlineData(new long[] { 20_000_000, 40_000_000, 60_000_000, 80_000_000 })]
	public async Task CoinJoinWithBlameRoundTestAsync(long[] amounts)
	{
		int inputCount = amounts.Length;

		// At the end of the test a coinjoin transaction has to be created and broadcasted.
		var transactionCompleted = new TaskCompletionSource<Transaction>(TaskCreationOptions.RunContinuationsAsynchronously);

		// Total test timeout.
		using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
		cts.Token.Register(() => transactionCompleted.TrySetCanceled(), useSynchronizationContext: false);

		KeyManager keyManager1 = KeyManager.CreateNew(out var _, password: "", Network.Main);
		KeyManager keyManager2 = KeyManager.CreateNew(out var _, password: "", Network.Main);

		var coins = GenerateSmartCoins(keyManager1, amounts, inputCount);
		var badCoins = GenerateSmartCoins(keyManager2, amounts, inputCount);

		var httpClient = _apiApplicationFactory.WithWebHostBuilder(builder =>
		builder.AddMockRpcClient(
			Enumerable.Concat(coins, badCoins).ToArray(),
			rpc =>
			{
				rpc.OnGetRawTransactionAsync = (txid, throwIfNotFound) =>
				{
					var tx = Transaction.Create(Network.Main);
					return Task.FromResult(tx);
				};

				// Make the coordinator believe that the transaction is being
				// broadcasted using the RPC interface. Once we receive this tx
				// (the `SendRawTransactionAsync` was invoked) we stop waiting
				// and finish the waiting tasks to finish the test successfully.
				rpc.OnSendRawTransactionAsync = (tx) =>
				{
					transactionCompleted.SetResult(tx);
					return tx.GetHash();
				};
			})
		.ConfigureServices(services =>

			// Instruct the coordinator DI container to use this scoped
			// services to build everything (WabiSabi controller, arena, etc)
			services.AddScoped(s => new WabiSabiConfig
			{
				AllowP2trInputs = true,
				AllowP2trOutputs = true,
				MaxInputCountByRound = 2 * inputCount,
				StandardInputRegistrationTimeout = TimeSpan.FromSeconds(60),
				BlameInputRegistrationTimeout = TimeSpan.FromSeconds(60),
				ConnectionConfirmationTimeout = TimeSpan.FromSeconds(60),
				OutputRegistrationTimeout = TimeSpan.FromSeconds(60),
				TransactionSigningTimeout = TimeSpan.FromSeconds(5 * inputCount),
				MaxSuggestedAmountBase = Money.Satoshis(ProtocolConstants.MaxAmountPerAlice)
			}))).CreateClient();

		// Create the coinjoin client
		using PersonCircuit personCircuit = new();
		IHttpClient httpClientWrapper = new ClearnetHttpClient(httpClient);

		var apiClient = _apiApplicationFactory.CreateWabiSabiHttpApiClient(httpClient);
		var mockHttpClientFactory = new MockWasabiHttpClientFactory();
		mockHttpClientFactory.OnNewHttpClientWithPersonCircuit = () => (personCircuit, httpClientWrapper);
		mockHttpClientFactory.OnNewHttpClientWithCircuitPerRequest = () => httpClientWrapper;

		using var roundStateUpdater = new RoundStateUpdater(TimeSpan.FromSeconds(1), apiClient);
		await roundStateUpdater.StartAsync(CancellationToken.None);

		var roundState = await roundStateUpdater.CreateRoundAwaiterAsync(roundState => roundState.Phase == Phase.InputRegistration, cts.Token);

		var coinJoinClient = WabiSabiFactory.CreateTestCoinJoinClient(mockHttpClientFactory, keyManager1, roundStateUpdater);

		// Run the coinjoin client task.
		var coinJoinTask = Task.Run(async () => await coinJoinClient.StartCoinJoinAsync(async () => await Task.FromResult(coins), true, cts.Token).ConfigureAwait(false), cts.Token);

		// Creates a IBackendHttpClientFactory that creates an HttpClient that says everything is okay
		// when a signature is sent but it doesn't really send it.
		var nonSigningHttpClientMock = new MockIHttpClient();
		nonSigningHttpClientMock.OnSendAsync = req =>
		{
			if (req.RequestUri!.AbsolutePath.Contains("transaction-signature"))
			{
				throw new HttpRequestException("Something was wrong posting the signature.");
			}

			return httpClient.SendAsync(req, CancellationToken.None);
		};

		IHttpClient nonSigningHttpClient = nonSigningHttpClientMock;
		var mockNonSigningHttpClientFactory = new MockWasabiHttpClientFactory();
		mockNonSigningHttpClientFactory.OnNewHttpClientWithPersonCircuit = () => (personCircuit, nonSigningHttpClient);
		mockNonSigningHttpClientFactory.OnNewHttpClientWithCircuitPerRequest = () => nonSigningHttpClient;

		var badCoinJoinClient = WabiSabiFactory.CreateTestCoinJoinClient(mockNonSigningHttpClientFactory, keyManager2, roundStateUpdater);

		var badCoinsTask = Task.Run(async () => await badCoinJoinClient.StartRoundAsync(badCoins, roundState, cts.Token).ConfigureAwait(false), cts.Token);

		// BadCoinsTask will throw.
		await Task.WhenAll(new Task[] { badCoinsTask, coinJoinTask });
		var resultOk = await coinJoinTask;
		var resultBad = await badCoinsTask;

		Assert.IsType<DisruptedCoinJoinResult>(resultBad);
		Assert.IsType<SuccessfulCoinJoinResult>(resultOk);

		var broadcastedTx = await transactionCompleted.Task; // wait for the transaction to be broadcasted.
		Assert.NotNull(broadcastedTx);

		Assert.Equal(
			coins.Select(x => x.Coin.Outpoint.ToString()).OrderBy(x => x),
			broadcastedTx.Inputs.Select(x => x.PrevOut.ToString()).OrderBy(x => x));

		await roundStateUpdater.StopAsync(CancellationToken.None);
	}

	[Theory]
	[InlineData(123456, 0.00, 0.00)]
	public async Task MultiClientsCoinJoinTestAsync(
		int seed,
		double faultInjectorMonkeyAggressiveness,
		double delayInjectorMonkeyAggressiveness)
	{
		const int NumberOfParticipants = 10;
		const int NumberOfCoinsPerParticipant = 2;
		const int ExpectedInputNumber = (NumberOfParticipants * NumberOfCoinsPerParticipant) / 2;

		var node = await TestNodeBuilder.CreateForHeavyConcurrencyAsync();
		try
		{
			var rpc = new TestableRpcClient((RpcClientBase)node.RpcClient);

			TaskCompletionSource<Transaction> coinJoinBroadcasted = new();
			rpc.AfterSendRawTransaction = (tx) =>
			{
				if (tx.Inputs.Count > 1)
				{
					coinJoinBroadcasted.SetResult(tx);
				}
			};

			var app = _apiApplicationFactory.WithWebHostBuilder(builder =>
				builder.ConfigureServices(services =>
				{
					// Instruct the coordinator DI container to use these two scoped
					// services to build everything (WabiSabi controller, arena, etc)
					services.AddScoped<IRPCClient>(s => rpc);
					services.AddScoped(s => new WabiSabiConfig(Path.GetTempFileName())
					{
						MaxRegistrableAmount = Money.Coins(500m),
						MaxInputCountByRound = (int)(ExpectedInputNumber / (1 + (10 * (faultInjectorMonkeyAggressiveness + delayInjectorMonkeyAggressiveness)))),
						StandardInputRegistrationTimeout = TimeSpan.FromSeconds(5 * ExpectedInputNumber),
						BlameInputRegistrationTimeout = TimeSpan.FromSeconds(2 * ExpectedInputNumber),
						ConnectionConfirmationTimeout = TimeSpan.FromSeconds(2 * ExpectedInputNumber),
						OutputRegistrationTimeout = TimeSpan.FromSeconds(5 * ExpectedInputNumber),
						TransactionSigningTimeout = TimeSpan.FromSeconds(3 * ExpectedInputNumber),
						MaxSuggestedAmountBase = Money.Satoshis(ProtocolConstants.MaxAmountPerAlice)
					});
				}));

			using PersonCircuit personCircuit = new();
			IHttpClient httpClientWrapper = new MonkeyHttpClient(
				new ClearnetHttpClient(app.CreateClient()),
				() => // This monkey injects `HttpRequestException` randomly to simulate errors in the communication.
				{
					if (Random.Shared.NextDouble() < faultInjectorMonkeyAggressiveness)
					{
						throw new HttpRequestException("Crazy monkey hates you, donkey.");
					}
					return Task.CompletedTask;
				},
				async () => // This monkey injects `Delays` randomly to simulate slow response times.
				{
					double delay = Random.Shared.NextDouble();
					await Task.Delay(TimeSpan.FromSeconds(5 * delayInjectorMonkeyAggressiveness)).ConfigureAwait(false);
				});

			var mockHttpClientFactory = new MockWasabiHttpClientFactory();
			mockHttpClientFactory.OnNewHttpClientWithPersonCircuit = () => (personCircuit, httpClientWrapper);
			mockHttpClientFactory.OnNewHttpClientWithCircuitPerRequest = () => httpClientWrapper;
			mockHttpClientFactory.OnNewHttpClientWithDefaultCircuit = () => httpClientWrapper;

			// Total test timeout.
			using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

			var participants = Enumerable
				.Range(0, NumberOfParticipants)
				.Select(i => new Participant($"participant{i}", rpc, mockHttpClientFactory))
				.ToArray();

			foreach (var participant in participants)
			{
				await participant.GenerateSourceCoinAsync(cts.Token);
			}
			var dummyWallet = new TestWallet("dummy", rpc);
			await dummyWallet.GenerateAsync(101, cts.Token);
			foreach (var participant in participants)
			{
				await participant.GenerateCoinsAsync(NumberOfCoinsPerParticipant, seed, cts.Token);
			}
			await dummyWallet.GenerateAsync(101, cts.Token);

			var tasks = participants.Select(x => x.StartParticipatingAsync(cts.Token)).ToArray();

			try
			{
				var coinjoinTransactionCompletionTask = coinJoinBroadcasted.Task.WaitAsync(cts.Token);
				var participantsFinishedTask = Task.WhenAll(tasks);
				var finishedTask = await Task.WhenAny(participantsFinishedTask, coinjoinTransactionCompletionTask);
				if (finishedTask == coinjoinTransactionCompletionTask)
				{
					var broadcastedCoinjoinTransaction = await coinjoinTransactionCompletionTask;
					var mempool = await rpc.GetRawMempoolAsync();
					var coinjoinFromMempool = await rpc.GetRawTransactionAsync(mempool.Single());

					Assert.Equal(broadcastedCoinjoinTransaction.GetHash(), coinjoinFromMempool.GetHash());
				}
				else if (finishedTask == participantsFinishedTask)
				{
					var participantsFinishedSuccessully = tasks
						.Where(t => t.IsCompletedSuccessfully)
						.Select(t => t.Result)
						.ToArray();

					// In case some participants claim to have finished successfully then wait a second for seeing
					// the coinjoin in the mempool. This seems really hard to believe but just in case.
					if (participantsFinishedSuccessully.All(x => x is SuccessfulCoinJoinResult))
					{
						await Task.Delay(TimeSpan.FromSeconds(1));
						var mempool = await rpc.GetRawMempoolAsync();
						Assert.Single(mempool);
					}
					else if (participantsFinishedSuccessully.All(x => x is FailedCoinJoinResult))
					{
						throw new Exception("All participants finished, but CoinJoin still not in the mempool (no more blame rounds).");
					}
					else if (participantsFinishedSuccessully.Length == 0 && !cts.IsCancellationRequested)
					{
						var exceptions = tasks
							.Where(x => x.IsFaulted)
							.Select(x => new Exception("Something went wrong", x.Exception))
							.ToArray();
						throw new AggregateException(exceptions);
					}
					else
					{
						throw new Exception("All participants finished, but CoinJoin still not in the mempool.");
					}
				}
				else
				{
					throw new Exception("This is not so possible.");
				}
			}
			catch (OperationCanceledException)
			{
				throw new TimeoutException("Coinjoin was not propagated.");
			}
		}
		finally
		{
			await node.TryStopAsync();
		}
	}

	[Fact]
	public async Task RegisterCoinIdempotencyAsync()
	{
		using Key signingKey = new();
		Coin coinToRegister = new(
			fromOutpoint: BitcoinFactory.CreateOutPoint(),
			fromTxOut: new TxOut(Money.Coins(1), signingKey.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit)));

		using HttpClient httpClient = _apiApplicationFactory.WithWebHostBuilder(builder =>
			builder.ConfigureServices(services =>
			{
				var rpc = BitcoinFactory.GetMockMinimalRpc();
				rpc.OnGetTxOutAsync = (_, _, _) => new()
				{
					Confirmations = 101,
					IsCoinBase = false,
					ScriptPubKeyType = "witness_v0_keyhash",
					TxOut = coinToRegister.TxOut
				};
				rpc.OnGetRawTransactionAsync = (txid, throwIfNotFound) =>
				{
					var tx = Transaction.Create(Network.Main);
					return Task.FromResult(tx);
				};
				services.AddScoped<IRPCClient>(s => rpc);
			})).CreateClient();

		ArenaClient apiClient = await _apiApplicationFactory.CreateArenaClientAsync(new StuttererHttpClient(httpClient));
		RoundState[] rounds = (await apiClient.GetStatusAsync(RoundStateRequest.Empty, CancellationToken.None)).RoundStates;
		RoundState round = rounds.First(x => x.CoinjoinState is ConstructionState);

		var ownershipProof = WabiSabiFactory.CreateOwnershipProof(signingKey, round.Id);
		var response = await apiClient.RegisterInputAsync(round.Id, coinToRegister.Outpoint, ownershipProof, CancellationToken.None);

		Assert.NotEqual(Guid.Empty, response.Value);
	}

	private SmartCoin[] GenerateSmartCoins(KeyManager keyManager, long[] amounts, int inputCount)
	{
		return keyManager.GetKeys()
			.Take(inputCount)
			.Select((x, i) => BitcoinFactory.CreateSmartCoin(x, Money.Satoshis(amounts[i])))
			.ToArray();
	}

	public class TestableRpcClient : RpcClientBase
	{
		public TestableRpcClient(RpcClientBase rpc)
			: base(rpc.Rpc)
		{
		}

		public Action<Transaction>? AfterSendRawTransaction { get; set; }

		public override async Task<uint256> SendRawTransactionAsync(Transaction transaction, CancellationToken cancellationToken = default)
		{
			var ret = await base.SendRawTransactionAsync(transaction, cancellationToken).ConfigureAwait(false);
			AfterSendRawTransaction?.Invoke(transaction);
			return ret;
		}
	}
}
