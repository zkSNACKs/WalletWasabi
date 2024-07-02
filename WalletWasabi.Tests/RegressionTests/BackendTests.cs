using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBitcoin.Protocol;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Backend.Controllers;
using WalletWasabi.Backend.Models;
using WalletWasabi.Backend.Models.Responses;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Blockchain.BlockFilters;
using WalletWasabi.Blockchain.Blocks;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionBroadcasting;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Services;
using WalletWasabi.Stores;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.Tests.XunitConfiguration;
using WalletWasabi.Tor.Http;
using WalletWasabi.Tor.Http.Extensions;
using WalletWasabi.Wallets;
using WalletWasabi.Wallets.FilterProcessor;
using WalletWasabi.WebClients.Wasabi;
using Xunit;

namespace WalletWasabi.Tests.RegressionTests;

/// <seealso cref="RegTestCollectionDefinition"/>
[Collection("RegTest collection")]
public class BackendTests : IClassFixture<RegTestFixture>
{
	public BackendTests(RegTestFixture regTestFixture)
	{
		RegTestFixture = regTestFixture;
		BackendHttpClient = regTestFixture.BackendHttpClient;
		BackendApiHttpClient = new ClearnetHttpClient(regTestFixture.HttpClient, () => RegTestFixture.BackendEndPointApiUri);
	}

	private RegTestFixture RegTestFixture { get; }

	/// <summary>Clearnet HTTP client with predefined base URI for Wasabi Backend (note: <c>/api</c> is not part of base URI).</summary>
	public IHttpClient BackendHttpClient { get; }

	/// <summary>Clearnet HTTP client with predefined base URI for Wasabi Backend API queries.</summary>
	private IHttpClient BackendApiHttpClient { get; }

	#region BackendTests

	[Fact]
	public async Task GetExchangeRatesAsync()
	{
		using var response = await BackendApiHttpClient.SendAsync(HttpMethod.Get, "btc/offchain/exchange-rates");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);

		var exchangeRates = await response.Content.ReadAsJsonAsync<List<ExchangeRate>>();
		Assert.Single(exchangeRates);

		var rate = exchangeRates[0];
		Assert.Equal("USD", rate.Ticker);
		Assert.True(rate.Rate > 0);
	}

	[Fact]
	public async Task GetClientVersionAsync()
	{
		WasabiClient client = new(BackendHttpClient);
		var backendCompatible = await client.CheckUpdatesAsync(CancellationToken.None);
		Assert.True(backendCompatible);
	}

	[Fact]
	public async Task BroadcastReplayTxAsync()
	{
		await using RegTestSetup setup = await RegTestSetup.InitializeTestEnvironmentAsync(RegTestFixture, numberOfBlocksToGenerate: 1);
		IRPCClient rpc = setup.RpcClient;

		var utxos = await rpc.ListUnspentAsync();
		var utxo = utxos[0];
		var tx = await rpc.GetRawTransactionAsync(utxo.OutPoint.Hash);
		using StringContent content = new($"'{tx.ToHex()}'", Encoding.UTF8, "application/json");

		Logger.TurnOff();

		using var response = await BackendApiHttpClient.SendAsync(HttpMethod.Post, "btc/blockchain/broadcast", content);
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("Transaction is already in the blockchain.", await response.Content.ReadAsJsonAsync<string>());

		Logger.TurnOn();
	}

	[Fact]
	public async Task BroadcastInvalidTxAsync()
	{
		await using RegTestSetup setup = await RegTestSetup.InitializeTestEnvironmentAsync(RegTestFixture, numberOfBlocksToGenerate: 1);

		using StringContent content = new($"''", Encoding.UTF8, "application/json");

		Logger.TurnOff();

		using var response = await BackendApiHttpClient.SendAsync(HttpMethod.Post, "btc/blockchain/broadcast", content);

		Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.Contains("The hex field is required.", await response.Content.ReadAsStringAsync());

		Logger.TurnOn();
	}

	[Fact]
	public async Task GetUnconfirmedTxChainAsync()
	{
		#region Initialize

		await using RegTestSetup setup = await RegTestSetup.InitializeTestEnvironmentAsync(RegTestFixture, numberOfBlocksToGenerate: 1);
		IRPCClient rpc = setup.RpcClient;
		Network network = setup.Network;
		BitcoinStore bitcoinStore = setup.BitcoinStore;
		using Backend.Global global = setup.Global;
		ServiceConfiguration serviceConfiguration = setup.ServiceConfiguration;
		string password = setup.Password;

		bitcoinStore.IndexStore.NewFilters += setup.Wallet_NewFiltersProcessed;

		// Create the services.
		// 1. Create connection service.
		using NodesGroup nodes = new(global.Config.Network, requirements: WalletWasabi.Helpers.Constants.NodeRequirements);
		nodes.ConnectedNodes.Add(await RegTestFixture.BackendRegTestNode.CreateNewP2pNodeAsync());

		// 2. Create mempool service.

		Node node = await RegTestFixture.BackendRegTestNode.CreateNewP2pNodeAsync();
		node.Behaviors.Add(bitcoinStore.CreateUntrustedP2pBehavior());

		// 3. Create wasabi synchronizer service.
		await using WasabiHttpClientFactory httpClientFactory = new(torEndPoint: null, backendUriGetter: () => new Uri(RegTestFixture.BackendEndPoint));
		using WasabiSynchronizer synchronizer = new(period: TimeSpan.FromSeconds(3), 10000, bitcoinStore, httpClientFactory);
		HybridFeeProvider feeProvider = new(synchronizer, null);

		// 4. Create key manager service.
		var keyManager = KeyManager.CreateNew(out _, password, network);

		// 5. Create wallet service.
		var workDir = Helpers.Common.GetWorkDir();

		using MemoryCache cache = BitcoinFactory.CreateMemoryCache();
		await using SpecificNodeBlockProvider specificNodeBlockProvider = new(network, serviceConfiguration, httpClientFactory.TorEndpoint);

		using BlockDownloadService blockDownloadService = new(
			bitcoinStore.BlockRepository,
			[specificNodeBlockProvider],
			new P2PBlockProvider(network, nodes, httpClientFactory.IsTorEnabled));

		using UnconfirmedTransactionChainProvider unconfirmedChainProvider = new(httpClientFactory);

		WalletManager walletManager = new(network, workDir, new WalletDirectories(network, workDir), new WalletFactory(workDir, network, bitcoinStore, synchronizer, serviceConfiguration, feeProvider, blockDownloadService, unconfirmedChainProvider));
		walletManager.Initialize();

		nodes.Connect(); // Start connection service.
		node.VersionHandshake(); // Start mempool service.
		await synchronizer.StartAsync(CancellationToken.None); // Start wasabi synchronizer service.
		await feeProvider.StartAsync(CancellationToken.None);

		using var wallet = await walletManager.AddAndStartWalletAsync(keyManager);

		// Wait until the filter our previous transaction is present.
		var blockCount = await rpc.GetBlockCountAsync();
		await setup.WaitForFiltersToBeProcessedAsync(TimeSpan.FromSeconds(120), blockCount);

		TransactionBroadcaster broadcaster = new(network, bitcoinStore, httpClientFactory, walletManager);
		broadcaster.Initialize(nodes, rpc);

		#endregion Initialize

		// Generate transaction.
		var key = keyManager.GetNextReceiveKey("foo label");
		var txId = await rpc.SendToAddressAsync(key.GetP2wpkhAddress(network), Money.Coins(1m));
		Assert.NotNull(txId);

		await Task.Delay(1000);

		using var response = await BackendApiHttpClient.SendAsync(HttpMethod.Get, $"btc/blockchain/unconfirmed-transaction-chain?transactionId={txId}");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);

		var unconfirmedChain = await response.Content.ReadAsJsonAsync<UnconfirmedTransactionChainItemLegacy[]>();

		Assert.Equal(txId, unconfirmedChain.First().TxId);
		Assert.Empty(unconfirmedChain.First().Parents);
		Assert.Empty(unconfirmedChain.First().Children);
		Assert.Single(unconfirmedChain);

		var tx1 = await rpc.GetRawTransactionAsync(txId);

		// Get the outpoints of TX1, so we can spend it.
		var outpoints = tx1.Outputs.Select((txout, i) => new OutPoint(tx1, i));

		using Key randomKey = new();
		var randomReceiveScript = randomKey.GetScriptPubKey(ScriptPubKeyType.Segwit);

		// Build a transaction on top of TX1.
		var buildTransactionResult = wallet.BuildTransaction(password, new PaymentIntent(randomReceiveScript, Money.Coins(0.05m), label: "foo"), FeeStrategy.CreateFromConfirmationTarget(5), allowUnconfirmed: true, allowedInputs: outpoints);

		await broadcaster.SendTransactionAsync(buildTransactionResult.Transaction);

		// Wait for more than 10 seconds, so the backend cache expires.
		await Task.Delay(11000);

		var txId2 = buildTransactionResult.Transaction.GetHash();

		using var response2 = await BackendApiHttpClient.SendAsync(HttpMethod.Get, $"btc/blockchain/unconfirmed-transaction-chain?transactionId={txId2}", cancellationToken: CancellationToken.None);

		Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

		unconfirmedChain = await response2.Content.ReadAsJsonAsync<UnconfirmedTransactionChainItemLegacy[]>();

		Assert.Equal(2, unconfirmedChain.Length);
		Assert.Contains(txId2, unconfirmedChain.Select(x => x.TxId));
		Assert.Contains(txId, unconfirmedChain.Select(x => x.TxId));
		Assert.Contains(txId, unconfirmedChain.First(tx => tx.TxId == txId2).Parents);
		Assert.Contains(txId2, unconfirmedChain.First(tx => tx.TxId == txId).Children);

		bitcoinStore.IndexStore.NewFilters -= setup.Wallet_NewFiltersProcessed;
		await walletManager.RemoveAndStopAllAsync(CancellationToken.None);
		await synchronizer.StopAsync(CancellationToken.None);
		await feeProvider.StopAsync(CancellationToken.None);
		nodes?.Dispose();
		node?.Disconnect();
	}

	[Fact]
	public async Task FilterBuilderTestAsync()
	{
		await using RegTestSetup setup = await RegTestSetup.InitializeTestEnvironmentAsync(RegTestFixture, numberOfBlocksToGenerate: 1);
		IRPCClient rpc = setup.RpcClient;
		using Backend.Global global = setup.Global;

		var indexBuilderServiceDir = Helpers.Common.GetWorkDir();
		var indexFilePath = Path.Combine(indexBuilderServiceDir, $"Index{rpc.Network}.dat");

		IndexBuilderService indexBuilderService = new(IndexType.SegwitTaproot, rpc, global.HostedServices.Get<BlockNotifier>(), indexFilePath);
		try
		{
			indexBuilderService.Synchronize();

			// Test initial synchronization.
			var times = 0;
			uint256 firstHash = await rpc.GetBlockHashAsync(0);
			while (indexBuilderService.GetFilterLinesExcluding(firstHash, 101, out _).filters.Count() != 101)
			{
				if (times > 500) // 30 sec
				{
					throw new TimeoutException($"{nameof(IndexBuilderService)} test timed out.");
				}
				await Task.Delay(100);
				times++;
			}

			// Test later synchronization.
			await rpc.GenerateAsync(10);
			times = 0;
			while (indexBuilderService.GetFilterLinesExcluding(firstHash, 111, out bool found5).filters.Count() != 111)
			{
				Assert.True(found5);
				if (times > 500) // 30 sec
				{
					throw new TimeoutException($"{nameof(IndexBuilderService)} test timed out.");
				}
				await Task.Delay(100);
				times++;
			}

			// Test correct number of filters is received.
			var hundredthHash = await rpc.GetBlockHashAsync(100);
			Assert.Equal(11, indexBuilderService.GetFilterLinesExcluding(hundredthHash, 11, out bool found).filters.Count());
			Assert.True(found);
			var bestHash = await rpc.GetBestBlockHashAsync();
			Assert.Empty(indexBuilderService.GetFilterLinesExcluding(bestHash, 1, out bool found2).filters);
			Assert.Empty(indexBuilderService.GetFilterLinesExcluding(uint256.Zero, 1, out bool found3).filters);
			Assert.False(found3);

			// Test filter block hashes are correct.
			var filters = indexBuilderService.GetFilterLinesExcluding(firstHash, 111, out bool found4).filters.ToArray();
			Assert.True(found4);
			for (int i = 0; i < 111; i++)
			{
				var expectedHash = await rpc.GetBlockHashAsync(i + 1);
				var filterModel = filters[i];
				Assert.Equal(expectedHash, filterModel.Header.BlockHash);
			}
		}
		finally
		{
			if (indexBuilderService is { })
			{
				await indexBuilderService.StopAsync();
			}
		}
	}

	[Fact]
	public async Task StatusRequestTestAsync()
	{
		await using RegTestSetup setup = await RegTestSetup.InitializeTestEnvironmentAsync(RegTestFixture, numberOfBlocksToGenerate: 1);
		IRPCClient rpc = setup.RpcClient;
		using Backend.Global global = setup.Global;

		var requestUri = "btc/Blockchain/status";

		try
		{
			global.IndexBuilderService.Synchronize();

			// Test initial synchronization.
			var times = 0;
			uint256 firstHash = await rpc.GetBlockHashAsync(0);
			while (global.IndexBuilderService.GetFilterLinesExcluding(firstHash, 101, out _).filters.Count() != 101)
			{
				if (times > 500) // 30 sec
				{
					throw new TimeoutException($"{nameof(IndexBuilderService)} test timed out.");
				}
				await Task.Delay(100);
				times++;
			}

			// First request.
			using (HttpResponseMessage response = await BackendApiHttpClient.SendAsync(HttpMethod.Get, requestUri))
			{
				Assert.NotNull(response);

				var resp = await response.Content.ReadAsJsonAsync<StatusResponse>();
				Assert.True(resp.FilterCreationActive);

				// Simulate an unintended stop
				await global.IndexBuilderService.StopAsync();
				await rpc.GenerateAsync(1);
			}

			// Second request.
			using (HttpResponseMessage response = await BackendApiHttpClient.SendAsync(HttpMethod.Get, requestUri))
			{
				Assert.NotNull(response);

				var resp = await response.Content.ReadAsJsonAsync<StatusResponse>();
				Assert.True(resp.FilterCreationActive);

				await rpc.GenerateAsync(1);

				var blockchainController = RegTestFixture.BackendHost.Services.GetRequiredService<BlockchainController>();
				blockchainController.Cache.Remove($"{nameof(BlockchainController.GetStatusAsync)}");

				// Set back the time to trigger timeout in BlockchainController.GetStatusAsync.
				global.IndexBuilderService.LastFilterBuildTime = DateTimeOffset.UtcNow - BlockchainController.FilterTimeout;
			}

			// Third request.
			using (HttpResponseMessage response = await BackendApiHttpClient.SendAsync(HttpMethod.Get, requestUri))
			{
				Assert.NotNull(response);

				var resp = await response.Content.ReadAsJsonAsync<StatusResponse>();
				Assert.False(resp.FilterCreationActive);
			}
		}
		finally
		{
			await global.IndexBuilderService.StopAsync();
		}
	}

	#endregion BackendTests
}
