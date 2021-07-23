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
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Backend.PostRequests
{
	public class RegisterOutputTests
	{
		[Fact]
		public async Task SuccessAsync()
		{
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);
			round.SetPhase(Phase.OutputRegistration);
			round.Alices.Add(WabiSabiFactory.CreateAlice(round));
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, round);
			await using ArenaRequestHandler handler = new(cfg, new Prison(), arena, new MockRpcClient());

			var req = WabiSabiFactory.CreateOutputRegistrationRequest(round);
			var resp = await handler.RegisterOutputAsync(req, CancellationToken.None);
			Assert.NotEmpty(round.Bobs);
			Assert.NotNull(resp);
			Assert.NotNull(resp.AmountCredentials);
			Assert.NotNull(resp.VsizeCredentials);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task RoundNotFoundAsync()
		{
			var cfg = new WabiSabiConfig();
			var nonExistingRound = WabiSabiFactory.CreateRound(cfg);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync();
			await using ArenaRequestHandler handler = new(cfg, new Prison(), arena, new MockRpcClient());
			var req = WabiSabiFactory.CreateOutputRegistrationRequest(nonExistingRound);
			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(async () => await handler.RegisterOutputAsync(req, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.RoundNotFound, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task ScriptNotAllowedAsync()
		{
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, round);
			using Key key = new();

			round.SetPhase(Phase.OutputRegistration);
			round.Alices.Add(WabiSabiFactory.CreateAlice(Money.Coins(1), round));

			var req = WabiSabiFactory.CreateOutputRegistrationRequest(round, key.PubKey.GetAddress(ScriptPubKeyType.Legacy, Network.Main).ScriptPubKey);
			await using ArenaRequestHandler handler = new(cfg, new Prison(), arena, new MockRpcClient());
			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(async () => await handler.RegisterOutputAsync(req, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.ScriptNotAllowed, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task NonStandardOutputAsync()
		{
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, round);

			round.SetPhase(Phase.OutputRegistration);
			round.Alices.Add(WabiSabiFactory.CreateAlice(Money.Coins(1), round));

			var sha256Bounty = Script.FromHex("aa20000000000019d6689c085ae165831e934ff763ae46a2a6c172b3f1b60a8ce26f87");
			var req = WabiSabiFactory.CreateOutputRegistrationRequest(round, sha256Bounty);
			await using ArenaRequestHandler handler = new(cfg, new Prison(), arena, new MockRpcClient());
			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(async () => await handler.RegisterOutputAsync(req, CancellationToken.None));

			// The following assertion requires standardness to be checked before allowed script types
			Assert.Equal(WabiSabiProtocolErrorCode.NonStandardOutput, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task NotEnoughFundsAsync()
		{
			WabiSabiConfig cfg = new() { MinRegistrableAmount = Money.Coins(2) };
			var round = WabiSabiFactory.CreateRound(cfg);
			round.SetPhase(Phase.OutputRegistration);
			round.Alices.Add(WabiSabiFactory.CreateAlice(Money.Coins(1), round));
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, round);
			await using ArenaRequestHandler handler = new(cfg, new Prison(), arena, new MockRpcClient());

			var req = WabiSabiFactory.CreateOutputRegistrationRequest(round);

			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(async () => await handler.RegisterOutputAsync(req, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.NotEnoughFunds, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task TooMuchFundsAsync()
		{
			WabiSabiConfig cfg = new() { MaxRegistrableAmount = Money.Coins(1.999m) }; // TODO migrate to MultipartyTransactionParameters
			var round = WabiSabiFactory.CreateRound(cfg);
			round.SetPhase(Phase.OutputRegistration);
			round.Alices.Add(WabiSabiFactory.CreateAlice(Money.Coins(2), round));
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, round);
			await using ArenaRequestHandler handler = new(cfg, new Prison(), arena, new MockRpcClient());

			var req = WabiSabiFactory.CreateOutputRegistrationRequest(round);

			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(async () => await handler.RegisterOutputAsync(req, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.TooMuchFunds, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task IncorrectRequestedVsizeCredentialsAsync()
		{
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);
			round.SetPhase(Phase.OutputRegistration);
			round.Alices.Add(WabiSabiFactory.CreateAlice(round));
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, round);
			await using ArenaRequestHandler handler = new(cfg, new Prison(), arena, new MockRpcClient());

			var req = WabiSabiFactory.CreateOutputRegistrationRequest(round, vsize: 30);

			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(async () => await handler.RegisterOutputAsync(req, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.IncorrectRequestedVsizeCredentials, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task WrongPhaseAsync()
		{
			WabiSabiConfig cfg = new();
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg);
			await using ArenaRequestHandler handler = new(cfg, new Prison(), arena, new MockRpcClient());
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			var round = arena.Rounds.First();
			round.Alices.Add(WabiSabiFactory.CreateAlice(round));

			foreach (Phase phase in Enum.GetValues(typeof(Phase)))
			{
				if (phase != Phase.OutputRegistration)
				{
					var req = WabiSabiFactory.CreateOutputRegistrationRequest(round);
					round.SetPhase(phase);
					var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(async () => await handler.RegisterOutputAsync(req, CancellationToken.None));
					Assert.Equal(WabiSabiProtocolErrorCode.WrongPhase, ex.ErrorCode);
				}
			}

			await arena.StopAsync(CancellationToken.None);
		}
	}
}
