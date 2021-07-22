using NBitcoin;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Helpers;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.WabiSabi;
using WalletWasabi.WabiSabi.Backend;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.WabiSabi.Models.MultipartyTransaction;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Backend.PhaseStepping
{
	public class StepOutputRegistrationTests
	{
		[Fact]
		public async Task AllBobsRegisteredAsync()
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
			var (round, arenaClient, alices) = await CreateRoundWithTwoConfirmedConnectionsAsync(arena, key1, coin1, key2, coin2);
			var (amountCredentials1, vsizeCredentials1) = (alices[0].IssuedAmountCredentials, alices[0].IssuedVsizeCredentials);
			var (amountCredentials2, vsizeCredentials2) = (alices[1].IssuedAmountCredentials, alices[1].IssuedVsizeCredentials);

			// Register outputs.
			var bobClient = new BobClient(round.Id, arenaClient);
			using var destKey1 = new Key();
			await bobClient.RegisterOutputAsync(
				coin1.Amount - round.FeeRate.GetFee(coin1.ScriptPubKey.EstimateInputVsize()),
				destKey1.PubKey.WitHash.ScriptPubKey,
				amountCredentials1.Take(ProtocolConstants.CredentialNumber),
				vsizeCredentials1.Take(ProtocolConstants.CredentialNumber),
				CancellationToken.None);

			using var destKey2 = new Key();
			await bobClient.RegisterOutputAsync(
				coin2.Amount - round.FeeRate.GetFee(coin2.ScriptPubKey.EstimateInputVsize()),
				destKey2.PubKey.WitHash.ScriptPubKey,
				amountCredentials2.Take(ProtocolConstants.CredentialNumber),
				vsizeCredentials2.Take(ProtocolConstants.CredentialNumber),
				CancellationToken.None);

			foreach (var alice in alices)
			{
				await alice.ReadyToSignAsync(CancellationToken.None);
			}

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(Phase.TransactionSigning, round.Phase);
			var tx = round.Assert<SigningState>().CreateTransaction();
			Assert.Equal(2, tx.Inputs.Count);
			Assert.Equal(2, tx.Outputs.Count);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task SomeBobsRegisteredTimeoutAsync()
		{
			WabiSabiConfig cfg = new()
			{
				MaxInputCountByRound = 2,
				MinInputCountByRoundMultiplier = 0.5,
				OutputRegistrationTimeout = TimeSpan.Zero
			};
			using Key key1 = new();
			using Key key2 = new();
			var coin1 = WabiSabiFactory.CreateCoin(key1);
			var coin2 = WabiSabiFactory.CreateCoin(key2);

			var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1, coin2);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, mockRpc);
			var (round, arenaClient, alices) = await CreateRoundWithTwoConfirmedConnectionsAsync(arena, key1, coin1, key2, coin2);
			var (amountCredentials1, vsizeCredentials1) = (alices[0].IssuedAmountCredentials, alices[0].IssuedVsizeCredentials);
			var (amountCredentials2, vsizeCredentials2) = (alices[1].IssuedAmountCredentials, alices[1].IssuedVsizeCredentials);

			// Register outputs.
			var bobClient = new BobClient(round.Id, arenaClient);
			using var destKey = new Key();
			await bobClient.RegisterOutputAsync(
				coin1.Amount - round.FeeRate.GetFee(coin1.ScriptPubKey.EstimateInputVsize()),
				destKey.PubKey.WitHash.ScriptPubKey,
				amountCredentials1.Take(ProtocolConstants.CredentialNumber),
				vsizeCredentials1.Take(ProtocolConstants.CredentialNumber),
				CancellationToken.None);

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(Phase.TransactionSigning, round.Phase);
			var tx = round.Assert<SigningState>().CreateTransaction();
			Assert.Equal(2, tx.Inputs.Count);
			Assert.Equal(2, tx.Outputs.Count);
			Assert.Contains(cfg.BlameScript, tx.Outputs.Select(x => x.ScriptPubKey));

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task DiffTooSmallToBlameAsync()
		{
			WabiSabiConfig cfg = new()
			{
				MaxInputCountByRound = 2,
				MinInputCountByRoundMultiplier = 0.5,
				OutputRegistrationTimeout = TimeSpan.Zero
			};
			using Key key1 = new();
			using Key key2 = new();
			var coin1 = WabiSabiFactory.CreateCoin(key1);
			var coin2 = WabiSabiFactory.CreateCoin(key2);

			var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1, coin2);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, mockRpc);
			var (round, arenaClient, alices) = await CreateRoundWithTwoConfirmedConnectionsAsync(arena, key1, coin1, key2, coin2);
			var (amountCredentials1, vsizeCredentials1) = (alices[0].IssuedAmountCredentials, alices[0].IssuedVsizeCredentials);
			var (amountCredentials2, vsizeCredentials2) = (alices[1].IssuedAmountCredentials, alices[1].IssuedVsizeCredentials);

			// Register outputs.
			var bobClient = new BobClient(round.Id, arenaClient);
			using var destKey1 = new Key();
			using var destKey2 = new Key();
			await bobClient.RegisterOutputAsync(
				coin1.Amount - round.FeeRate.GetFee(coin1.ScriptPubKey.EstimateInputVsize()),
				destKey1.PubKey.WitHash.ScriptPubKey,
				amountCredentials1.Take(ProtocolConstants.CredentialNumber),
				vsizeCredentials1.Take(ProtocolConstants.CredentialNumber),
				CancellationToken.None);

			await bobClient.RegisterOutputAsync(
				coin2.Amount - round.FeeRate.GetFee(coin2.ScriptPubKey.EstimateInputVsize()),
				destKey2.PubKey.WitHash.ScriptPubKey,
				amountCredentials2.Take(ProtocolConstants.CredentialNumber),
				vsizeCredentials2.Take(ProtocolConstants.CredentialNumber),
				CancellationToken.None);

			// Add another input. The input must be able to pay for itself, but
			// the remaining amount after deducting the fees needs to be less
			// than the minimum.
			var txParams = round.Assert<ConstructionState>().Parameters;
			var extraAlice = WabiSabiFactory.CreateAlice(txParams.FeeRate.GetFee(Constants.P2wpkhInputVirtualSize) + txParams.AllowedOutputAmounts.Min - new Money(1L), round);
			round.Alices.Add(extraAlice);
			round.CoinjoinState = round.Assert<ConstructionState>().AddInput(extraAlice.Coin);

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(Phase.TransactionSigning, round.Phase);
			var tx = round.Assert<SigningState>().CreateTransaction();
			Assert.Equal(3, tx.Inputs.Count);
			Assert.Equal(2, tx.Outputs.Count);
			Assert.DoesNotContain(cfg.BlameScript, tx.Outputs.Select(x => x.ScriptPubKey));

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task DoesntSwitchImmaturelyAsync()
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
			var (round, arenaClient, alices) = await CreateRoundWithTwoConfirmedConnectionsAsync(arena, key1, coin1, key2, coin2);
			var (amountCredentials1, vsizeCredentials1) = (alices[0].IssuedAmountCredentials, alices[0].IssuedVsizeCredentials);
			var (amountCredentials2, vsizeCredentials2) = (alices[1].IssuedAmountCredentials, alices[1].IssuedVsizeCredentials);

			// Register outputs.
			var bobClient = new BobClient(round.Id, arenaClient);
			using var destKey = new Key();
			await bobClient.RegisterOutputAsync(
				coin1.Amount - round.FeeRate.GetFee(coin1.ScriptPubKey.EstimateInputVsize()),
				destKey.PubKey.WitHash.ScriptPubKey,
				amountCredentials1.Take(ProtocolConstants.CredentialNumber),
				vsizeCredentials1.Take(ProtocolConstants.CredentialNumber),
				CancellationToken.None);

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(Phase.OutputRegistration, round.Phase);

			await arena.StopAsync(CancellationToken.None);
		}

		private async Task<(Round Round, ArenaClient ArenaClient, AliceClient[] alices)>
			CreateRoundWithTwoConfirmedConnectionsAsync(Arena arena, Key key1, Coin coin1, Key key2, Coin coin2)
		{
			// Create the round.
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);
			var round = Assert.Single(arena.Rounds);

			// Register Alices.
			var aliceClient1 = new AliceClient(round.Id, arenaClient, coin1, round.FeeRate, key1.GetBitcoinSecret(round.Network));
			var aliceClient2 = new AliceClient(round.Id, arenaClient, coin2, round.FeeRate, key2.GetBitcoinSecret(round.Network));

			await aliceClient1.RegisterInputAsync(CancellationToken.None).ConfigureAwait(false);
			await aliceClient2.RegisterInputAsync(CancellationToken.None).ConfigureAwait(false);

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(Phase.ConnectionConfirmation, round.Phase);

			// Confirm connections.
			await aliceClient1.ConfirmConnectionAsync(
				TimeSpan.FromSeconds(1),
				new long[] { coin1.EffectiveValue(round.FeeRate) },
				new long[] { round.MaxVsizeAllocationPerAlice - coin1.ScriptPubKey.EstimateInputVsize() },
				CancellationToken.None).ConfigureAwait(false);

			await aliceClient2.ConfirmConnectionAsync(
				TimeSpan.FromSeconds(1),
				new long[] { coin2.EffectiveValue(round.FeeRate) },
				new long[] { round.MaxVsizeAllocationPerAlice - coin2.ScriptPubKey.EstimateInputVsize() },
				CancellationToken.None).ConfigureAwait(false);

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(Phase.OutputRegistration, round.Phase);

			return (round,
					arenaClient,
					new[]
					{
						aliceClient1,
						aliceClient2
					});
		}
	}
}
