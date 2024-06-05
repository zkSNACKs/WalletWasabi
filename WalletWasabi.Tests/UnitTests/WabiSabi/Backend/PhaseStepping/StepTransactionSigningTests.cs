using NBitcoin;
using NBitcoin.RPC;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.Tests.UnitTests.WabiSabi.Backend.Rounds.Utils;
using WalletWasabi.WabiSabi;
using WalletWasabi.WabiSabi.Backend;
using WalletWasabi.WabiSabi.Backend.DoSPrevention;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.WabiSabi.Client.CoinJoin.Client;
using WalletWasabi.WabiSabi.Client.RoundStateAwaiters;
using WalletWasabi.WabiSabi.Models;
using WalletWasabi.WabiSabi.Models.MultipartyTransaction;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Backend.PhaseStepping;

public class StepTransactionSigningTests
{
	private TimeSpan TestTimeout { get; } = TimeSpan.FromMinutes(3);

	[Fact]
	public async Task EveryoneSignedAsync()
	{
		using CancellationTokenSource cancellationTokenSource = new(TestTimeout);
		var token = cancellationTokenSource.Token;

		WabiSabiConfig cfg = new()
		{
			MaxInputCountByRound = 2,
			MinInputCountByRoundMultiplier = 0.5,
			MaxSuggestedAmountBase = Money.Satoshis(ProtocolConstants.MaxAmountPerAlice)
		};
		var (keyChain, coin1, coin2) = WabiSabiFactory.CreateCoinKeyPairs();

		var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1.Coin, coin2.Coin);
		using Arena arena = await ArenaBuilder.From(cfg).With(mockRpc).CreateAndStartAsync();
		var (round, aliceClient1, aliceClient2) = await CreateRoundWithOutputsReadyToSignAsync(arena, keyChain, coin1, coin2);

		await arena.TriggerAndWaitRoundAsync(token);
		Assert.Equal(Phase.TransactionSigning, round.Phase);

		var signedCoinJoin = round.Assert<SigningState>().CreateUnsignedTransactionWithPrecomputedData();

		await aliceClient1.SignTransactionAsync(signedCoinJoin, keyChain, token);
		await aliceClient2.SignTransactionAsync(signedCoinJoin, keyChain, token);
		await arena.TriggerAndWaitRoundAsync(token);
		Assert.DoesNotContain(round, arena.GetActiveRounds());
		Assert.Equal(Phase.Ended, round.Phase);

		await arena.StopAsync(token);
	}

	[Fact]
	public async Task FailsBroadcastAsync()
	{
		using CancellationTokenSource cancellationTokenSource = new(TestTimeout);
		var token = cancellationTokenSource.Token;

		WabiSabiConfig cfg = WabiSabiFactory.CreateWabiSabiConfig();
		var (keyChain, coin1, coin2) = WabiSabiFactory.CreateCoinKeyPairs();

		var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1.Coin, coin2.Coin);
		mockRpc.OnSendRawTransactionAsync = (_) =>
			throw new RPCException(RPCErrorCode.RPC_TRANSACTION_REJECTED, "", null);

		Prison prison = WabiSabiFactory.CreatePrison();
		using Arena arena = await ArenaBuilder.From(cfg, mockRpc, prison).CreateAndStartAsync();
		var (round, aliceClient1, aliceClient2) = await CreateRoundWithOutputsReadyToSignAsync(arena, keyChain, coin1, coin2);

		await arena.TriggerAndWaitRoundAsync(token);
		Assert.Equal(Phase.TransactionSigning, round.Phase);

		var signedCoinJoin = round.Assert<SigningState>().CreateUnsignedTransactionWithPrecomputedData();

		await aliceClient1.SignTransactionAsync(signedCoinJoin, keyChain, token);
		await aliceClient2.SignTransactionAsync(signedCoinJoin, keyChain, token);
		await arena.TriggerAndWaitRoundAsync(token);
		Assert.DoesNotContain(round, arena.Rounds.Where(x => x.Phase != Phase.Ended));
		Assert.Equal(Phase.Ended, round.Phase);
		Assert.Equal(EndRoundState.TransactionBroadcastFailed, round.EndRoundState);

		var now = DateTimeOffset.UtcNow;
		Assert.All(
			new[] { aliceClient1.SmartCoin.Outpoint, aliceClient2.SmartCoin.Outpoint },
			prevOut => Assert.False(prison.IsBanned(prevOut, cfg.GetDoSConfiguration(), now)));

		await arena.StopAsync(token);
	}

	[Fact]
	public async Task AlicesSpentAsync()
	{
		using CancellationTokenSource cancellationTokenSource = new(TestTimeout);
		var token = cancellationTokenSource.Token;

		WabiSabiConfig cfg = WabiSabiFactory.CreateWabiSabiConfig();
		var (keyChain, coin1, coin2) = WabiSabiFactory.CreateCoinKeyPairs();

		var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1.Coin, coin2.Coin);
		mockRpc.OnSendRawTransactionAsync = (_) =>
			throw new RPCException(RPCErrorCode.RPC_TRANSACTION_REJECTED, "", null);

		Prison prison = WabiSabiFactory.CreatePrison();
		using Arena arena = await ArenaBuilder.From(cfg, mockRpc, prison).CreateAndStartAsync();

		var (round, aliceClient1, aliceClient2) = await CreateRoundWithOutputsReadyToSignAsync(arena, keyChain, coin1, coin2);

		await arena.TriggerAndWaitRoundAsync(token);
		Assert.Equal(Phase.TransactionSigning, round.Phase);

		var signedCoinJoin = round.Assert<SigningState>().CreateUnsignedTransactionWithPrecomputedData();

		await aliceClient1.SignTransactionAsync(signedCoinJoin, keyChain, token);
		await aliceClient2.SignTransactionAsync(signedCoinJoin, keyChain, token);
		await arena.TriggerAndWaitRoundAsync(token);
		Assert.DoesNotContain(round, arena.Rounds.Where(x => x.Phase != Phase.Ended));
		Assert.Equal(Phase.Ended, round.Phase);
		Assert.Equal(EndRoundState.TransactionBroadcastFailed, round.EndRoundState);

		// There should be no inmate, because we aren't punishing spenders with banning
		// as there's no reason to ban already spent UTXOs,
		// the cost of spending the UTXO is the punishment instead.
		var now = DateTimeOffset.UtcNow;
		Assert.All(
			new[] { aliceClient1.SmartCoin.Outpoint, aliceClient2.SmartCoin.Outpoint },
			prevOut => Assert.False(prison.IsBanned(prevOut, cfg.GetDoSConfiguration(), now)));

		await arena.StopAsync(token);
	}

	[Fact]
	public async Task TimeoutInsufficientPeersAsync()
	{
		using CancellationTokenSource cancellationTokenSource = new(TestTimeout);
		var token = cancellationTokenSource.Token;

		WabiSabiConfig cfg = WabiSabiFactory.CreateWabiSabiConfig();
		cfg.MinInputCountByRoundMultiplier = 1;
		cfg.MinInputCountByBlameRoundMultiplier = 1;
		cfg.TransactionSigningTimeout = TimeSpan.Zero;
		var (keyChain, coin1, coin2) = WabiSabiFactory.CreateCoinKeyPairs();

		var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1.Coin, coin2.Coin);
		mockRpc.OnSendRawTransactionAsync = (_) =>
			throw new RPCException(RPCErrorCode.RPC_TRANSACTION_REJECTED, "", null);

		Prison prison = WabiSabiFactory.CreatePrison();
		using Arena arena = await ArenaBuilder.From(cfg, mockRpc, prison).CreateAndStartAsync();
		var (round, aliceClient1, aliceClient2) = await CreateRoundWithOutputsReadyToSignAsync(arena, keyChain, coin1, coin2);

		await arena.TriggerAndWaitRoundAsync(token);
		Assert.Equal(Phase.TransactionSigning, round.Phase);

		var signedCoinJoin = round.Assert<SigningState>().CreateUnsignedTransactionWithPrecomputedData();
		await aliceClient2.SignTransactionAsync(signedCoinJoin, keyChain, token);
		await arena.TriggerAndWaitRoundAsync(token);
		Assert.DoesNotContain(round, arena.Rounds.Where(x => x.Phase != Phase.Ended));
		Assert.Equal(Phase.Ended, round.Phase);
		Assert.Equal(EndRoundState.AbortedNotEnoughAlicesSigned, round.EndRoundState);
		Assert.Empty(arena.Rounds.Where(x => x is BlameRound));

		Assert.True(prison.IsBanned(aliceClient1.SmartCoin.Outpoint, cfg.GetDoSConfiguration(), DateTimeOffset.UtcNow));

		await arena.StopAsync(token);
	}

	[Fact]
	public async Task TimeoutSufficientPeersAsync()
	{
		using CancellationTokenSource cancellationTokenSource = new(TestTimeout);
		var token = cancellationTokenSource.Token;

		WabiSabiConfig cfg = WabiSabiFactory.CreateWabiSabiConfig();
		cfg.MinInputCountByRoundMultiplier = 1;
		cfg.TransactionSigningTimeout = TimeSpan.Zero;
		cfg.FailFastOutputRegistrationTimeout = TimeSpan.Zero;

		var (keyChain, coin1, coin2) = WabiSabiFactory.CreateCoinKeyPairs();

		var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1.Coin, coin2.Coin);
		mockRpc.OnSendRawTransactionAsync = (_) =>
			throw new RPCException(RPCErrorCode.RPC_TRANSACTION_REJECTED, "", null);

		Prison prison = WabiSabiFactory.CreatePrison();
		using Arena arena = await ArenaBuilder.From(cfg, mockRpc, prison).CreateAndStartAsync();
		var (round, aliceClient1, aliceClient2) = await CreateRoundWithOutputsReadyToSignAsync(arena, keyChain, coin1, coin2);

		// Make sure not all alices signed.
		var alice3 = WabiSabiFactory.CreateAlice(round);
		alice3.ConfirmedConnection = true;
		alice3.ReadyToSign = true;
		round.Alices.Add(alice3);
		round.CoinjoinState = round.Assert<ConstructionState>().AddInput(alice3.Coin, alice3.OwnershipProof, WabiSabiFactory.CreateCommitmentData(round.Id));
		await arena.TriggerAndWaitRoundAsync(token);
		Assert.Equal(Phase.TransactionSigning, round.Phase);

		var signedCoinJoin = round.Assert<SigningState>().CreateUnsignedTransactionWithPrecomputedData();
		await aliceClient1.SignTransactionAsync(signedCoinJoin, keyChain, token);
		await aliceClient2.SignTransactionAsync(signedCoinJoin, keyChain, token);
		await arena.TriggerAndWaitRoundAsync(token);
		Assert.DoesNotContain(round, arena.Rounds.Where(x => x.Phase != Phase.Ended));
		Assert.Single(arena.Rounds.Where(x => x is BlameRound));
		var badOutpoint = alice3.Coin.Outpoint;
		Assert.True(prison.IsBanned(badOutpoint, cfg.GetDoSConfiguration(), DateTimeOffset.UtcNow));

		var onlyRound = arena.Rounds.Single(x => x is BlameRound);
		var blameRound = Assert.IsType<BlameRound>(onlyRound);
		Assert.NotNull(blameRound.BlameOf);
		Assert.Equal(round.Id, blameRound.BlameOf.Id);

		var whitelist = blameRound.BlameWhitelist;
		Assert.Contains(aliceClient1.SmartCoin.Outpoint, whitelist);
		Assert.Contains(aliceClient2.SmartCoin.Outpoint, whitelist);
		Assert.DoesNotContain(badOutpoint, whitelist);

		await arena.StopAsync(token);
	}

	[Fact]
	public async Task AliceWasNotReadyAsync()
	{
		using CancellationTokenSource cancellationTokenSource = new(TestTimeout);
		var token = cancellationTokenSource.Token;

		WabiSabiConfig cfg = WabiSabiFactory.CreateWabiSabiConfig();
		cfg.TransactionSigningTimeout = TimeSpan.Zero;
		cfg.OutputRegistrationTimeout = TimeSpan.Zero;
		cfg.FailFastTransactionSigningTimeout = TimeSpan.Zero;

		var (keyChain, coin1, coin2) = WabiSabiFactory.CreateCoinKeyPairs();

		var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1.Coin, coin2.Coin);
		mockRpc.OnSendRawTransactionAsync = (tx) =>
			throw new RPCException(RPCErrorCode.RPC_TRANSACTION_REJECTED, "", null);

		Prison prison = WabiSabiFactory.CreatePrison();

		using Arena arena = await ArenaBuilder.From(cfg, mockRpc, prison).CreateAndStartAsync();
		var (round, aliceClient1, aliceClient2) = await CreateRoundWithOutputsReadyToSignAsync(arena, keyChain, coin1, coin2);

		var badOutpoint = aliceClient1.SmartCoin.Coin.Outpoint;
		var goodOutpoint = aliceClient2.SmartCoin.Coin.Outpoint;
		var badAlice = round.Alices.Single(x => badOutpoint == x.Coin.Outpoint);
		badAlice.ReadyToSign = false;

		await arena.TriggerAndWaitRoundAsync(token);
		Assert.Equal(Phase.TransactionSigning, round.Phase);

		var signedCoinJoin = round.Assert<SigningState>().CreateUnsignedTransactionWithPrecomputedData();
		await aliceClient2.SignTransactionAsync(signedCoinJoin, keyChain, token);

		await arena.TriggerAndWaitRoundAsync(token);
		Assert.Equal(Phase.Ended, round.Phase);

		Assert.True(prison.IsBanned(badOutpoint, cfg.GetDoSConfiguration(), DateTimeOffset.UtcNow));
		var onlyRound = arena.Rounds.Single(x => x is BlameRound);
		var blameRound = Assert.IsType<BlameRound>(onlyRound);
		Assert.Equal(round.Id, blameRound.BlameOf.Id);
		var whitelist = blameRound.BlameWhitelist;
		Assert.Contains(goodOutpoint, whitelist);
		Assert.DoesNotContain(badOutpoint, whitelist);

		await arena.StopAsync(token);
	}

	private async Task<(Round Round, AliceClient AliceClient1, AliceClient AliceClient2)>
			CreateRoundWithOutputsReadyToSignAsync(Arena arena, IKeyChain keyChain, SmartCoin coin1, SmartCoin coin2)
	{
		using CancellationTokenSource cancellationTokenSource = new(TestTimeout);
		var token = cancellationTokenSource.Token;
		using CancellationTokenSource coinBanCheckMode = new();
		var coinBanCheckModeToken = coinBanCheckMode.Token;

		// Create the round.
		await arena.TriggerAndWaitRoundAsync(token);

		var round = Assert.Single(arena.Rounds);
		var arenaClient = WabiSabiFactory.CreateArenaClient(arena);

		// Register Alices.
		using RoundStateUpdater roundStateUpdater = new(TimeSpan.FromSeconds(2), arena);
		await roundStateUpdater.StartAsync(token);

		var task1 = AliceClient.CreateRegisterAndConfirmInputAsync(RoundState.FromRound(round), arenaClient, coin1, keyChain, roundStateUpdater, token, token, token, coinBanCheckModeToken);
		var task2 = AliceClient.CreateRegisterAndConfirmInputAsync(RoundState.FromRound(round), arenaClient, coin2, keyChain, roundStateUpdater, token, token, token, coinBanCheckModeToken);

		while (Phase.OutputRegistration != round.Phase)
		{
			await arena.TriggerAndWaitRoundAsync(token);
		}

		await Task.WhenAll(task1, task2);

		var aliceClient1 = task1.Result;
		var aliceClient2 = task2.Result;

		// Register outputs.
		var bobClient = new BobClient(round.Id, arenaClient);
		using var destKey1 = new Key();
		using var destKey2 = new Key();
		await bobClient.RegisterOutputAsync(
			destKey1.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
			aliceClient1.IssuedAmountCredentials.Take(ProtocolConstants.CredentialNumber),
			aliceClient1.IssuedVsizeCredentials.Take(ProtocolConstants.CredentialNumber),
			token).ConfigureAwait(false);

		await bobClient.RegisterOutputAsync(
			destKey2.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
			aliceClient2.IssuedAmountCredentials.Take(ProtocolConstants.CredentialNumber),
			aliceClient2.IssuedVsizeCredentials.Take(ProtocolConstants.CredentialNumber),
			token).ConfigureAwait(false);

		await aliceClient1.ReadyToSignAsync(token);
		await aliceClient2.ReadyToSignAsync(token);

		await roundStateUpdater.StopAsync(token);
		return (round, aliceClient1, aliceClient2);
	}
}
