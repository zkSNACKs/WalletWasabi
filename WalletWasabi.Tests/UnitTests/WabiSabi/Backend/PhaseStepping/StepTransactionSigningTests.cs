using Moq;
using NBitcoin;
using NBitcoin.RPC;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.WabiSabi;
using WalletWasabi.WabiSabi.Backend;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.WabiSabi.Models.MultipartyTransaction;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Backend.PhaseStepping
{
	public class StepTransactionSigningTests
	{
		[Fact]
		public async Task EveryoneSignedAsync()
		{
			WabiSabiConfig cfg = new()
			{
				MaxInputCountByRound = 2,
				MinInputCountByRoundMultiplier = 0.5
			};
			using Key key1 = new();
			using Key key2 = new();
			var coin1 = WabiSabiFactory.CreateCoin(key1);
			var coin2 = WabiSabiFactory.CreateCoin(key2);

			var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1, coin2);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, mockRpc);
			var (round, aliceClient1, aliceClient2) = await CreateRoundWithRegisteredOutputsAsync(arena, key1, coin1, key2, coin2).ConfigureAwait(false);

			await aliceClient1.ReadyToSignAsync(CancellationToken.None);
			await aliceClient2.ReadyToSignAsync(CancellationToken.None);

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(Phase.TransactionSigning, round.Phase);

			var signedCoinJoin = round.Assert<SigningState>().CreateTransaction();

			await aliceClient1.SignTransactionAsync(signedCoinJoin, CancellationToken.None);
			await aliceClient2.SignTransactionAsync(signedCoinJoin, CancellationToken.None);
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.DoesNotContain(round, arena.ActiveRounds);
			Assert.Equal(Phase.Ended, round.Phase);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task FailsBroadcastAsync()
		{
			WabiSabiConfig cfg = new()
			{
				MaxInputCountByRound = 2,
				MinInputCountByRoundMultiplier = 0.5
			};
			using Key key1 = new();
			using Key key2 = new();
			var coin1 = WabiSabiFactory.CreateCoin(key1);
			var coin2 = WabiSabiFactory.CreateCoin(key2);

			var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1, coin2);
			mockRpc.Setup(rpc => rpc.SendRawTransactionAsync(It.IsAny<Transaction>()))
				.ThrowsAsync(new RPCException(RPCErrorCode.RPC_TRANSACTION_REJECTED, "", null));

			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, mockRpc);
			var (round, aliceClient1, aliceClient2) = await CreateRoundWithRegisteredOutputsAsync(arena, key1, coin1, key2, coin2).ConfigureAwait(false);

			await aliceClient1.ReadyToSignAsync(CancellationToken.None);
			await aliceClient2.ReadyToSignAsync(CancellationToken.None);

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(Phase.TransactionSigning, round.Phase);

			var signedCoinJoin = round.Assert<SigningState>().CreateTransaction();

			await aliceClient1.SignTransactionAsync(signedCoinJoin, CancellationToken.None);
			await aliceClient2.SignTransactionAsync(signedCoinJoin, CancellationToken.None);
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.DoesNotContain(round, arena.ActiveRounds);
			Assert.Equal(Phase.Ended, round.Phase);
			Assert.False(round.WasTransactionBroadcast);
			Assert.Empty(arena.Prison.GetInmates());

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task AlicesSpentAsync()
		{
			WabiSabiConfig cfg = new()
			{
				MaxInputCountByRound = 2,
				MinInputCountByRoundMultiplier = 0.5
			};

			using Key key1 = new();
			using Key key2 = new();
			var coin1 = WabiSabiFactory.CreateCoin(key1);
			var coin2 = WabiSabiFactory.CreateCoin(key2);

			var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1, coin2);
			mockRpc.Setup(rpc => rpc.SendRawTransactionAsync(It.IsAny<Transaction>()))
				.ThrowsAsync(new RPCException(RPCErrorCode.RPC_TRANSACTION_REJECTED, "", null));

			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, mockRpc);
			var (round, aliceClient1, aliceClient2) = await CreateRoundWithRegisteredOutputsAsync(arena, key1, coin1, key2, coin2).ConfigureAwait(false);

			await aliceClient1.ReadyToSignAsync(CancellationToken.None);
			await aliceClient2.ReadyToSignAsync(CancellationToken.None);

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(Phase.TransactionSigning, round.Phase);

			var signedCoinJoin = round.Assert<SigningState>().CreateTransaction();

			await aliceClient1.SignTransactionAsync(signedCoinJoin, CancellationToken.None);
			await aliceClient2.SignTransactionAsync(signedCoinJoin, CancellationToken.None);
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.DoesNotContain(round, arena.ActiveRounds);
			Assert.Equal(Phase.Ended, round.Phase);
			Assert.False(round.WasTransactionBroadcast);

			// There should be no inmate, because we aren't punishing spenders with banning
			// as there's no reason to ban already spent UTXOs,
			// the cost of spending the UTXO is the punishment instead.
			Assert.Empty(arena.Prison.GetInmates());

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task TimeoutInsufficientPeersAsync()
		{
			WabiSabiConfig cfg = new()
			{
				MaxInputCountByRound = 2,
				MinInputCountByRoundMultiplier = 1,
				TransactionSigningTimeout = TimeSpan.FromSeconds(10)
			};

			using Key key1 = new();
			using Key key2 = new();
			var coin1 = WabiSabiFactory.CreateCoin(key1);
			var coin2 = WabiSabiFactory.CreateCoin(key2);

			var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1, coin2);
			mockRpc.Setup(rpc => rpc.SendRawTransactionAsync(It.IsAny<Transaction>()))
				.ThrowsAsync(new RPCException(RPCErrorCode.RPC_TRANSACTION_REJECTED, "", null));

			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, mockRpc);
			var (round, aliceClient1, aliceClient2) = await CreateRoundWithRegisteredOutputsAsync(arena, key1, coin1, key2, coin2).ConfigureAwait(false);

			await aliceClient1.ReadyToSignAsync(CancellationToken.None);
			await aliceClient2.ReadyToSignAsync(CancellationToken.None);

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(Phase.TransactionSigning, round.Phase);

			var signedCoinJoin = round.Assert<SigningState>().CreateTransaction();
			await aliceClient1.SignTransactionAsync(signedCoinJoin, CancellationToken.None);
			await Task.Delay(TimeSpan.FromSeconds(10));
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.DoesNotContain(round, arena.ActiveRounds);
			Assert.Equal(Phase.Ended, round.Phase);
			Assert.False(round.WasTransactionBroadcast);
			Assert.Empty(arena.Rounds.Where(x => x.IsBlameRound));
			Assert.Contains(aliceClient2.Coin.Outpoint, arena.Prison.GetInmates().Select(x => x.Utxo));

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task TimeoutSufficientPeersAsync()
		{
			WabiSabiConfig cfg = new()
			{
				MaxInputCountByRound = 2,
				MinInputCountByRoundMultiplier = 1,
				OutputRegistrationTimeout = TimeSpan.FromSeconds(10),
				TransactionSigningTimeout = TimeSpan.FromSeconds(10),
			};

			using Key key1 = new();
			using Key key2 = new();
			var coin1 = WabiSabiFactory.CreateCoin(key1);
			var coin2 = WabiSabiFactory.CreateCoin(key2);

			var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1, coin2);
			mockRpc.Setup(rpc => rpc.SendRawTransactionAsync(It.IsAny<Transaction>()))
				.ThrowsAsync(new RPCException(RPCErrorCode.RPC_TRANSACTION_REJECTED, "", null));

			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, mockRpc);
			var (round, aliceClient1, aliceClient2) = await CreateRoundWithRegisteredOutputsAsync(arena, key1, coin1, key2, coin2).ConfigureAwait(false);

			// Make sure not all alices signed.
			var alice3 = WabiSabiFactory.CreateAlice(round);
			alice3.ConfirmedConnection = true;
			alice3.ReadyToSign = true;
			round.Alices.Add(alice3);
			round.CoinjoinState = round.Assert<ConstructionState>().AddInput(alice3.Coin);

			await aliceClient1.ReadyToSignAsync(CancellationToken.None);
			await aliceClient2.ReadyToSignAsync(CancellationToken.None);
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(Phase.TransactionSigning, round.Phase);

			var signedCoinJoin = round.Assert<SigningState>().CreateTransaction();
			await aliceClient1.SignTransactionAsync(signedCoinJoin, CancellationToken.None);
			await aliceClient2.SignTransactionAsync(signedCoinJoin, CancellationToken.None);
			await Task.Delay(TimeSpan.FromSeconds(10));
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(Phase.Ended, round.Phase);
			Assert.DoesNotContain(round, arena.ActiveRounds);
			Assert.Single(arena.Rounds.Where(x => x.IsBlameRound));
			var badOutpoint = alice3.Coin.Outpoint;
			Assert.Contains(badOutpoint, arena.Prison.GetInmates().Select(x => x.Utxo));

			var blameRound = arena.Rounds.Single(x => x.IsBlameRound);
			Assert.True(blameRound.IsBlameRound);
			Assert.NotNull(blameRound.BlameOf);
			Assert.Equal(round.Id, blameRound.BlameOf?.Id);

			var whitelist = blameRound.BlameWhitelist;
			Assert.Contains(aliceClient1.Coin.Outpoint, whitelist);
			Assert.Contains(aliceClient2.Coin.Outpoint, whitelist);
			Assert.DoesNotContain(badOutpoint, whitelist);

			await arena.StopAsync(CancellationToken.None);
		}

		private async Task<(Round Round, AliceClient AliceClient1, AliceClient AliceClient2)>
			CreateRoundWithRegisteredOutputsAsync(Arena arena, Key key1, Coin coin1, Key key2, Coin coin2)
		{
			// Create the round.
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));

			var round = Assert.Single(arena.Rounds);
			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);

			// Register Alices.
			var aliceClient1 = new AliceClient(round.Id, arenaClient, coin1, round.FeeRate, key1.GetBitcoinSecret(round.Network));
			var aliceClient2 = new AliceClient(round.Id, arenaClient, coin2, round.FeeRate, key2.GetBitcoinSecret(round.Network));

			await aliceClient1.RegisterInputAsync(CancellationToken.None).ConfigureAwait(false);
			await aliceClient2.RegisterInputAsync(CancellationToken.None).ConfigureAwait(false);

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(Phase.ConnectionConfirmation, round.Phase);

			// Confirm connections.
			await aliceClient1.ConfirmConnectionAsync(
				TimeSpan.FromMilliseconds(100),
				new long[] { coin1.EffectiveValue(round.FeeRate) },
				new long[] { round.MaxVsizeAllocationPerAlice - coin1.ScriptPubKey.EstimateInputVsize() },
				CancellationToken.None).ConfigureAwait(false);

			await aliceClient2.ConfirmConnectionAsync(
				TimeSpan.FromMilliseconds(100),
				new long[] { coin2.EffectiveValue(round.FeeRate) },
				new long[] { round.MaxVsizeAllocationPerAlice - coin2.ScriptPubKey.EstimateInputVsize() },
				CancellationToken.None).ConfigureAwait(false);

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(Phase.OutputRegistration, round.Phase);

			// Register outputs.
			var bobClient = new BobClient(round.Id, arenaClient);
			using var destKey1 = new Key();
			using var destKey2 = new Key();
			await bobClient.RegisterOutputAsync(
				coin1.Amount - round.FeeRate.GetFee(coin1.ScriptPubKey.EstimateInputVsize()),
				destKey1.PubKey.WitHash.ScriptPubKey,
				aliceClient1.IssuedAmountCredentials.Take(ProtocolConstants.CredentialNumber),
				aliceClient1.IssuedVsizeCredentials.Take(ProtocolConstants.CredentialNumber),
				CancellationToken.None).ConfigureAwait(false);

			await bobClient.RegisterOutputAsync(
				coin2.Amount - round.FeeRate.GetFee(coin2.ScriptPubKey.EstimateInputVsize()),
				destKey2.PubKey.WitHash.ScriptPubKey,
				aliceClient2.IssuedAmountCredentials.Take(ProtocolConstants.CredentialNumber),
				aliceClient2.IssuedVsizeCredentials.Take(ProtocolConstants.CredentialNumber),
				CancellationToken.None).ConfigureAwait(false);

			return (round, aliceClient1, aliceClient2);
		}
	}
}
