using NBitcoin;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.WabiSabi.Backend;
using WalletWasabi.WabiSabi.Backend.Banning;
using WalletWasabi.WabiSabi.Backend.Models;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Models;
using WalletWasabi.WabiSabi.Models.MultipartyTransaction;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Backend.PostRequests
{
	public class SignTransactionTests
	{
		[Fact]
		public async Task SuccessAsync()
		{
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);
			using Key key = new();
			Alice alice = WabiSabiFactory.CreateAlice(key: key, round: round);
			round.Alices.Add(alice);
			round.CoinjoinState = round.AddInput(alice.Coin).Finalize();
			round.SetPhase(Phase.TransactionSigning);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, round);

			var aliceSignedCoinJoin = round.Assert<SigningState>().CreateUnsignedTransaction();
			aliceSignedCoinJoin.Sign(key.GetBitcoinSecret(Network.Main), alice.Coin);

			var req = new TransactionSignaturesRequest(round.Id, new[] { new InputWitnessPair(0, aliceSignedCoinJoin.Inputs[0].WitScript) });
			await using ArenaRequestHandler handler = new(cfg, new Prison(), arena, new MockRpcClient());
			await handler.SignTransactionAsync(req, CancellationToken.None);
			Assert.True(round.Assert<SigningState>().IsFullySigned);
			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task RoundNotFoundAsync()
		{
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync();
			await using ArenaRequestHandler handler = new(new WabiSabiConfig(), new Prison(), arena, new MockRpcClient());
			var req = new TransactionSignaturesRequest(uint256.Zero, null!);
			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(async () => await handler.SignTransactionAsync(req, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.RoundNotFound, ex.ErrorCode);
			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task WrongPhaseAsync()
		{
			WabiSabiConfig cfg = new();
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg);
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			var round = arena.Rounds.First();

			var req = new TransactionSignaturesRequest(round.Id, null!);
			foreach (Phase phase in Enum.GetValues(typeof(Phase)))
			{
				if (phase != Phase.TransactionSigning)
				{
					round.SetPhase(phase);
					await using ArenaRequestHandler handler = new(cfg, new Prison(), arena, new MockRpcClient());
					var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(async () =>
						await handler.SignTransactionAsync(req, CancellationToken.None));
					Assert.Equal(WabiSabiProtocolErrorCode.WrongPhase, ex.ErrorCode);
				}
			}
			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task WrongCoinjoinSignatureAsync()
		{
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);
			using Key key1 = new();
			Alice alice1 = WabiSabiFactory.CreateAlice(key: key1, round: round);
			using Key key2 = new();
			Alice alice2 = WabiSabiFactory.CreateAlice(key: key2, round: round);
			round.Alices.Add(alice1);
			round.Alices.Add(alice2);
			round.CoinjoinState = round.Assert<ConstructionState>().AddInput(alice1.Coin).AddInput(alice2.Coin).Finalize();
			round.SetPhase(Phase.TransactionSigning);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, round);

			// Submit the signature for the second alice to the first alice's input.
			var alice2signedCoinJoin = round.Assert<SigningState>().CreateUnsignedTransaction();
			alice2signedCoinJoin.Sign(key2.GetBitcoinSecret(Network.Main), alice2.Coin);

			var req = new TransactionSignaturesRequest(round.Id, new[] { new InputWitnessPair(0, alice2signedCoinJoin.Inputs[0].WitScript) });
			await using ArenaRequestHandler handler = new(cfg, new Prison(), arena, new MockRpcClient());
			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(async () => await handler.SignTransactionAsync(req, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.WrongCoinjoinSignature, ex.ErrorCode);
			await arena.StopAsync(CancellationToken.None);
		}
	}
}
