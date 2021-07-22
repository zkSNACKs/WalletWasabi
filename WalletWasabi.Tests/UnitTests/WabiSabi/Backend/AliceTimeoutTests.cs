using NBitcoin;
using System;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.WabiSabi.Backend;
using WalletWasabi.WabiSabi.Backend.Banning;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Client;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Backend
{
	public class AliceTimeoutTests
	{
		[Fact]
		public async Task AliceTimesoutAsync()
		{
			// Alice times out when its deadline is reached.
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);
			using Key key = new();
			var coin = WabiSabiFactory.CreateCoin(key);
			var rpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin);

			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, rpc, round);
			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);

			// Register Alices.
			var minAliceDeadline = DateTimeOffset.UtcNow + cfg.ConnectionConfirmationTimeout * 0.9;
			var aliceClient = new AliceClient(round.Id, arenaClient, coin, round.FeeRate, key.GetBitcoinSecret(round.Network));
			await aliceClient.RegisterInputAsync(CancellationToken.None);

			var alice = Assert.Single(round.Alices);
			alice.Deadline = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(1);
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Empty(round.Alices);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task AliceDoesntTimeoutInConnectionConfirmationAsync()
		{
			// Alice does not time out when it's not input registration anymore,
			// even though the deadline is reached.
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);
			round.SetPhase(Phase.ConnectionConfirmation);
			var alice = WabiSabiFactory.CreateAlice(round);
			round.Alices.Add(alice);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, round);

			var req = WabiSabiFactory.CreateConnectionConfirmationRequest(round);
			await using ArenaRequestHandler handler = new(cfg, new Prison(), arena, new MockRpcClient());

			Assert.Single(round.Alices);
			DateTimeOffset preDeadline = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(1);
			alice.Deadline = preDeadline;
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Single(round.Alices);
			Assert.Equal(preDeadline, alice.Deadline);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task AliceDoesntTimeoutIfInputRegistrationTimedoutAsync()
		{
			// Alice does not time out if input registration timed out,
			// even though the deadline is reached and still in input reg.
			WabiSabiConfig cfg = new() { StandardInputRegistrationTimeout = TimeSpan.Zero };
			var round = WabiSabiFactory.CreateRound(cfg);
			var alice = WabiSabiFactory.CreateAlice(round);
			round.Alices.Add(alice);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, round);

			var req = WabiSabiFactory.CreateConnectionConfirmationRequest(round);
			await using ArenaRequestHandler handler = new(cfg, new Prison(), arena, new MockRpcClient());

			Assert.Single(round.Alices);
			DateTimeOffset preDeadline = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(1);
			alice.Deadline = preDeadline;
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Single(round.Alices);
			Assert.Equal(preDeadline, alice.Deadline);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task AliceDoesntTimeoutIfMaxInputCountReachedAsync()
		{
			// Alice does not time out if input reg is full with alices,
			// even though the deadline is reached and still in input reg.
			WabiSabiConfig cfg = new() { MaxInputCountByRound = 3 };
			var round = WabiSabiFactory.CreateRound(cfg);
			var alice = WabiSabiFactory.CreateAlice(round);
			round.Alices.Add(alice);
			round.Alices.Add(WabiSabiFactory.CreateAlice(round));
			round.Alices.Add(WabiSabiFactory.CreateAlice(round));
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, round);

			var req = WabiSabiFactory.CreateConnectionConfirmationRequest(round);
			await using ArenaRequestHandler handler = new(cfg, new Prison(), arena, new MockRpcClient());

			Assert.Equal(3, round.Alices.Count);
			DateTimeOffset preDeadline = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(1);
			alice.Deadline = preDeadline;
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(3, round.Alices.Count);
			Assert.Equal(preDeadline, alice.Deadline);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task AliceDeadlineUpdatedAsync()
		{
			// Alice's deadline is updated by connection confirmation.
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);
			var alice = WabiSabiFactory.CreateAlice(round);
			round.Alices.Add(alice);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, round);

			var req = WabiSabiFactory.CreateConnectionConfirmationRequest(round);
			await using ArenaRequestHandler handler = new(cfg, new Prison(), arena, new MockRpcClient());

			Assert.Single(round.Alices);
			DateTimeOffset preDeadline = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(1);
			alice.Deadline = preDeadline;
			await handler.ConfirmConnectionAsync(req, CancellationToken.None);
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Single(round.Alices);
			Assert.NotEqual(preDeadline, alice.Deadline);

			await arena.StopAsync(CancellationToken.None);
		}
	}
}
