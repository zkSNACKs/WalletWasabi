using NBitcoin;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Helpers;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.WabiSabi;
using WalletWasabi.WabiSabi.Backend;
using WalletWasabi.WabiSabi.Backend.Models;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.WabiSabi.Client.CoinJoin.Client;
using WalletWasabi.WabiSabi.Client.RoundStateAwaiters;
using WalletWasabi.WabiSabi.Models;
using WalletWasabi.WabiSabi.Models.MultipartyTransaction;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Backend.PhaseStepping;

public class StepOutputRegistrationTests
{
	private TimeSpan TestTimeout { get; } = TimeSpan.FromMinutes(3);

	[Fact]
	public async Task AllBobsRegisteredAsync()
	{
		using CancellationTokenSource cancellationTokenSource = new(TestTimeout);
		var token = cancellationTokenSource.Token;

		WabiSabiConfig cfg = new()
		{
			MaxInputCountByRound = 2,
			MinInputCountByRoundMultiplier = 0.5,
		};
		var (keyChain, coin1, coin2) = WabiSabiFactory.CreateCoinKeyPairs();

		var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1.Coin, coin2.Coin);
		using Arena arena = await ArenaBuilder.From(cfg).With(mockRpc).CreateAndStartAsync();
		var (round, arenaClient, alices) = await CreateRoundWithTwoConfirmedConnectionsAsync(arena, keyChain, coin1, coin2);
		var (amountCredentials1, vsizeCredentials1) = (alices[0].IssuedAmountCredentials, alices[0].IssuedVsizeCredentials);
		var (amountCredentials2, vsizeCredentials2) = (alices[1].IssuedAmountCredentials, alices[1].IssuedVsizeCredentials);

		// Register outputs.
		var bobClient = new BobClient(round.Id, arenaClient);
		using var destinationKey1 = new Key();
		await bobClient.RegisterOutputAsync(
			destinationKey1.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
			amountCredentials1.Take(ProtocolConstants.CredentialNumber),
			vsizeCredentials1.Take(ProtocolConstants.CredentialNumber),
			token);

		using var destinationKey2 = new Key();
		await bobClient.RegisterOutputAsync(
			destinationKey2.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
			amountCredentials2.Take(ProtocolConstants.CredentialNumber),
			vsizeCredentials2.Take(ProtocolConstants.CredentialNumber),
			token);

		foreach (var alice in alices)
		{
			await alice.ReadyToSignAsync(token);
		}

		await arena.TriggerAndWaitRoundAsync(token);
		Assert.Equal(Phase.TransactionSigning, round.Phase);
		var tx = round.Assert<SigningState>().CreateTransaction();
		Assert.Equal(2, tx.Inputs.Count);
		Assert.Equal(2, tx.Outputs.Count);

		await arena.StopAsync(token);
	}

	[Fact]
	public async Task SomeBobsRegisteredTimeoutAsync()
	{
		using CancellationTokenSource cancellationTokenSource = new(TestTimeout);
		var token = cancellationTokenSource.Token;

		WabiSabiConfig cfg = new()
		{
			MaxInputCountByRound = 2,
			MinInputCountByRoundMultiplier = 0.5,
			OutputRegistrationTimeout = TimeSpan.Zero,
			CoordinationFeeRate = CoordinationFeeRate.Zero
		};
		var (keyChain, coin1, coin2) = WabiSabiFactory.CreateCoinKeyPairs();

		var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1.Coin, coin2.Coin);
		using Arena arena = await ArenaBuilder.From(cfg).With(mockRpc).CreateAndStartAsync();
		var (round, arenaClient, alices) = await CreateRoundWithTwoConfirmedConnectionsAsync(arena, keyChain, coin1, coin2);
		var (amountCredentials1, vsizeCredentials1) = (alices[0].IssuedAmountCredentials, alices[0].IssuedVsizeCredentials);
		var (amountCredentials2, vsizeCredentials2) = (alices[1].IssuedAmountCredentials, alices[1].IssuedVsizeCredentials);

		// Register outputs.
		var bobClient = new BobClient(round.Id, arenaClient);
		using var destinationKey = new Key();
		await bobClient.RegisterOutputAsync(
			destinationKey.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
			amountCredentials1.Take(ProtocolConstants.CredentialNumber),
			vsizeCredentials1.Take(ProtocolConstants.CredentialNumber),
			token);
		await alices[0].ReadyToSignAsync(token);
		await alices[1].ReadyToSignAsync(token);

		await arena.TriggerAndWaitRoundAsync(token);
		Assert.Equal(Phase.TransactionSigning, round.Phase);
		var tx = round.Assert<SigningState>().CreateTransaction();
		Assert.Equal(2, tx.Inputs.Count);
		Assert.Equal(2, tx.Outputs.Count);
		Assert.Contains(round.CoordinatorScript, tx.Outputs.Select(x => x.ScriptPubKey));

		await arena.StopAsync(token);
	}

	[Fact]
	public async Task DiffTooSmallToBlameAsync()
	{
		using CancellationTokenSource cancellationTokenSource = new(TestTimeout);
		var token = cancellationTokenSource.Token;

		WabiSabiConfig cfg = new()
		{
			MaxInputCountByRound = 2,
			MinInputCountByRoundMultiplier = 0.5,
			OutputRegistrationTimeout = TimeSpan.Zero,
			CoordinationFeeRate = CoordinationFeeRate.Zero
		};
		var (keyChain, coin1, coin2) = WabiSabiFactory.CreateCoinKeyPairs();

		var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1.Coin, coin2.Coin);
		using Arena arena = await ArenaBuilder.From(cfg).With(mockRpc).CreateAndStartAsync();
		var (round, arenaClient, alices) = await CreateRoundWithTwoConfirmedConnectionsAsync(arena, keyChain, coin1, coin2);
		var (amountCredentials1, vsizeCredentials1) = (alices[0].IssuedAmountCredentials, alices[0].IssuedVsizeCredentials);
		var (amountCredentials2, vsizeCredentials2) = (alices[1].IssuedAmountCredentials, alices[1].IssuedVsizeCredentials);

		// Register outputs.
		var bobClient = new BobClient(round.Id, arenaClient);
		using var destinationKey1 = new Key();
		using var destinationKey2 = new Key();
		await bobClient.RegisterOutputAsync(
			destinationKey1.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
			amountCredentials1.Take(ProtocolConstants.CredentialNumber),
			vsizeCredentials1.Take(ProtocolConstants.CredentialNumber),
			token);

		await bobClient.RegisterOutputAsync(
			destinationKey2.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
			amountCredentials2.Take(ProtocolConstants.CredentialNumber),
			vsizeCredentials2.Take(ProtocolConstants.CredentialNumber),
			token);
		await alices[0].ReadyToSignAsync(token);
		await alices[1].ReadyToSignAsync(token);

		// Add another input. The input must be able to pay for itself, but
		// the remaining amount after deducting the fees needs to be less
		// than the minimum.
		var txParameters = round.Parameters;
		var extraAlice = WabiSabiFactory.CreateAlice(round.Parameters.MiningFeeRate.GetFee(Constants.P2wpkhInputVirtualSize) + txParameters.AllowedOutputAmounts.Min - new Money(1L), round);
		extraAlice.ReadyToSign = true;
		round.Alices.Add(extraAlice);
		round.CoinjoinState = round.Assert<ConstructionState>().AddInput(extraAlice.Coin, extraAlice.OwnershipProof, WabiSabiFactory.CreateCommitmentData(round.Id));

		await arena.TriggerAndWaitRoundAsync(token);
		Assert.Equal(Phase.TransactionSigning, round.Phase);
		var tx = round.Assert<SigningState>().CreateTransaction();
		Assert.Equal(3, tx.Inputs.Count);
		Assert.Equal(2, tx.Outputs.Count);
		Assert.DoesNotContain(round.CoordinatorScript, tx.Outputs.Select(x => x.ScriptPubKey));

		await arena.StopAsync(token);
	}

	[Fact]
	public async Task DoesntSwitchImmaturelyAsync()
	{
		using CancellationTokenSource cancellationTokenSource = new(TestTimeout);
		var token = cancellationTokenSource.Token;

		WabiSabiConfig cfg = new()
		{
			MaxInputCountByRound = 2,
			MinInputCountByRoundMultiplier = 0.5
		};
		var (keyChain, coin1, coin2) = WabiSabiFactory.CreateCoinKeyPairs();

		var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1.Coin, coin2.Coin);
		using Arena arena = await ArenaBuilder.From(cfg).With(mockRpc).CreateAndStartAsync();
		var (round, arenaClient, alices) = await CreateRoundWithTwoConfirmedConnectionsAsync(arena, keyChain, coin1, coin2);
		var (amountCredentials1, vsizeCredentials1) = (alices[0].IssuedAmountCredentials, alices[0].IssuedVsizeCredentials);
		var (amountCredentials2, vsizeCredentials2) = (alices[1].IssuedAmountCredentials, alices[1].IssuedVsizeCredentials);

		// Register outputs.
		var bobClient = new BobClient(round.Id, arenaClient);
		using var destinationKey = new Key();
		await bobClient.RegisterOutputAsync(
			destinationKey.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
			amountCredentials1.Take(ProtocolConstants.CredentialNumber),
			vsizeCredentials1.Take(ProtocolConstants.CredentialNumber),
			token);

		await arena.TriggerAndWaitRoundAsync(token);
		Assert.Equal(Phase.OutputRegistration, round.Phase);

		await arena.StopAsync(token);
	}

	private async Task<(Round Round, ArenaClient ArenaClient, AliceClient[] alices)>
			CreateRoundWithTwoConfirmedConnectionsAsync(Arena arena, IKeyChain keyChain, SmartCoin coin1, SmartCoin coin2)
	{
		using CancellationTokenSource cancellationTokenSource = new(TestTimeout);
		var token = cancellationTokenSource.Token;

		// Get the round.
		await arena.TriggerAndWaitRoundAsync(token);
		var arenaClient = WabiSabiFactory.CreateArenaClient(arena);
		var round = Assert.Single(arena.Rounds);

		// Refresh the Arena States because of vsize manipulation.
		await arena.TriggerAndWaitRoundAsync(token);

		using RoundStateUpdater roundStateUpdater = new(TimeSpan.FromSeconds(2), arena);
		await roundStateUpdater.StartAsync(token);
		var task1 = AliceClient.CreateRegisterAndConfirmInputAsync(RoundState.FromRound(round), arenaClient, coin1, keyChain, roundStateUpdater, token, token, token);
		var task2 = AliceClient.CreateRegisterAndConfirmInputAsync(RoundState.FromRound(round), arenaClient, coin2, keyChain, roundStateUpdater, token, token, token);

		while (Phase.ConnectionConfirmation != round.Phase)
		{
			await arena.TriggerAndWaitRoundAsync(token);
		}

		await Task.WhenAll(task1, task2);
		var aliceClient1 = await task1;
		var aliceClient2 = await task2;

		await arena.TriggerAndWaitRoundAsync(token);
		Assert.Equal(Phase.OutputRegistration, round.Phase);

		await roundStateUpdater.StopAsync(token);

		return (round,
				arenaClient,
				new[]
				{
						aliceClient1,
						aliceClient2
				});
	}

	[Fact]
	public async Task SomeBobsReusingAddressAsync()
	{
		using CancellationTokenSource cancellationTokenSource = new(TestTimeout);
		var token = cancellationTokenSource.Token;

		WabiSabiConfig cfg = new()
		{
			MaxInputCountByRound = 2,
			MinInputCountByRoundMultiplier = 0.5,
			CoordinationFeeRate = CoordinationFeeRate.Zero
		};

		var keyManager1 = ServiceFactory.CreateKeyManager("");
		var keyManager2 = ServiceFactory.CreateKeyManager("");

		var (keyChain1, coin1a, coin1b) = WabiSabiFactory.CreateCoinKeyPairs(keyManager1);
		var (keyChain2, coin2a, coin2b) = WabiSabiFactory.CreateCoinKeyPairs(keyManager2);

		var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1a.Coin, coin1b.Coin, coin2a.Coin, coin2b.Coin);
		using Arena arena = await ArenaBuilder.From(cfg).With(mockRpc).CreateAndStartAsync();

		// Get the round.
		await arena.TriggerAndWaitRoundAsync(token);
		var round1 = Assert.Single(arena.Rounds);
		var arenaClient1 = WabiSabiFactory.CreateArenaClient(arena);
		var round2 = WabiSabiFactory.CreateRound(WabiSabiFactory.CreateRoundParameters(cfg));

		arena.Rounds.Add(round2);

		// Refresh the Arena States because of vsize manipulation.
		await arena.TriggerAndWaitRoundAsync(token);

		using RoundStateUpdater roundStateUpdater = new(TimeSpan.FromSeconds(2), arena);
		await roundStateUpdater.StartAsync(token);
		var task1a = AliceClient.CreateRegisterAndConfirmInputAsync(RoundState.FromRound(round1), arenaClient1, coin1a, keyChain1, roundStateUpdater, token, token, token);
		var task1b = AliceClient.CreateRegisterAndConfirmInputAsync(RoundState.FromRound(round1), arenaClient1, coin1b, keyChain1, roundStateUpdater, token, token, token);

		while (Phase.ConnectionConfirmation != round1.Phase)
		{
			await arena.TriggerAndWaitRoundAsync(token);
		}

		var aliceClient1a = await task1a;
		var aliceClient1b = await task1b;

		// Arena will create another round - to have at least one in input reg.
		await arena.TriggerAndWaitRoundAsync(token);

		var arenaClient2 = WabiSabiFactory.CreateArenaClient(arena, round2);

		var task2a = AliceClient.CreateRegisterAndConfirmInputAsync(RoundState.FromRound(round2), arenaClient2, coin2a, keyChain2, roundStateUpdater, token, token, token);
		var task2b = AliceClient.CreateRegisterAndConfirmInputAsync(RoundState.FromRound(round2), arenaClient2, coin2b, keyChain2, roundStateUpdater, token, token, token);

		while (Phase.ConnectionConfirmation != round2.Phase)
		{
			await arena.TriggerAndWaitRoundAsync(token);
		}

		var aliceClient2a = await task2a;
		var aliceClient2b = await task2b;

		while (Phase.OutputRegistration != round1.Phase || Phase.OutputRegistration != round2.Phase)
		{
			await arena.TriggerAndWaitRoundAsync(token);
		}

		Assert.Equal(Phase.OutputRegistration, round1.Phase);
		Assert.Equal(Phase.OutputRegistration, round2.Phase);

		var (amountCredentials1a, vsizeCredentials1a) = (aliceClient1a.IssuedAmountCredentials, aliceClient1a.IssuedVsizeCredentials);
		var (amountCredentials1b, vsizeCredentials1b) = (aliceClient1b.IssuedAmountCredentials, aliceClient1b.IssuedVsizeCredentials);

		var (amountCredentials2a, vsizeCredentials2a) = (aliceClient2a.IssuedAmountCredentials, aliceClient2a.IssuedVsizeCredentials);
		var (amountCredentials2b, vsizeCredentials2b) = (aliceClient2b.IssuedAmountCredentials, aliceClient2b.IssuedVsizeCredentials);

		// Register outputs.
		var bobClient1 = new BobClient(round1.Id, arenaClient1);
		var bobClient2 = new BobClient(round2.Id, arenaClient2);

		var out1a = keyManager1.GetNextReceiveKey("o1a");
		var out1b = keyManager1.GetNextReceiveKey("o1b");
		var out2a = keyManager2.GetNextReceiveKey("o2a");
		var out2b = keyManager2.GetNextReceiveKey("o2b");

		var bob1a = bobClient1.RegisterOutputAsync(
			out1a.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
			amountCredentials1a.Take(ProtocolConstants.CredentialNumber),
			vsizeCredentials1a.Take(ProtocolConstants.CredentialNumber),
			token);

		var bob1b = bobClient1.RegisterOutputAsync(
			out1b.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
			amountCredentials1b.Take(ProtocolConstants.CredentialNumber),
			vsizeCredentials1b.Take(ProtocolConstants.CredentialNumber),
			token);

		await bob1a;

		using var bob2aCts = new CancellationTokenSource();
		var bob2a = Task.Run(async () =>
		{
			using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(token, bob2aCts.Token);
			do
			{
				try
				{
					// Trying to register the same script again and again.
					await bobClient2.RegisterOutputAsync(
						out1a.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
						amountCredentials2a.Take(ProtocolConstants.CredentialNumber),
						vsizeCredentials2a.Take(ProtocolConstants.CredentialNumber),
						combinedCts.Token);
					throw new InvalidOperationException("This output should never be able to register.");
				}
				catch (WabiSabiProtocolException)
				{
				}
				catch (OperationCanceledException)
				{
				}
			}
			while (!combinedCts.Token.IsCancellationRequested);
		});

		await bob1b;

		await Task.Delay(100);

		await aliceClient1a.ReadyToSignAsync(token);
		await aliceClient1b.ReadyToSignAsync(token);

		while (Phase.TransactionSigning != round1.Phase)
		{
			await arena.TriggerAndWaitRoundAsync(token);
		}

		await Task.Delay(100);

		bob2aCts.Cancel();

		// We should never get an exception here. Otherwise it would indicate that output was registered twice.
		await bob2a;

		await roundStateUpdater.StopAsync(token);

		await arena.StopAsync(token);
	}

	[Fact]
	public async Task AliceIsNotReadyAsync()
	{
		using CancellationTokenSource cancellationTokenSource = new(TestTimeout);
		var token = cancellationTokenSource.Token;

		WabiSabiConfig cfg = new()
		{
			MaxInputCountByRound = 2,
			MinInputCountByRoundMultiplier = 0.5,
			OutputRegistrationTimeout = TimeSpan.Zero,
		};
		var (keyChain, coin1, coin2) = WabiSabiFactory.CreateCoinKeyPairs();

		var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1.Coin, coin2.Coin);
		using Arena arena = await ArenaBuilder.From(cfg).With(mockRpc).CreateAndStartAsync();
		var (round, arenaClient, alices) = await CreateRoundWithTwoConfirmedConnectionsAsync(arena, keyChain, coin1, coin2);

		await alices[0].ReadyToSignAsync(token);

		await arena.TriggerAndWaitRoundAsync(token);
		Assert.Equal(Phase.TransactionSigning, round.Phase);
	}
}
