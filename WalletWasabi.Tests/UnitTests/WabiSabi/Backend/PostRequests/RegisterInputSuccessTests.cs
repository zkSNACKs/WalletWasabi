using NBitcoin;
using System;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.WabiSabi.Backend;
using WalletWasabi.WabiSabi.Backend.Models;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Client;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Backend.PostRequests
{
	public class RegisterInputSuccessTests
	{
		private static void AssertSingleAliceSuccessfullyRegistered(Round round, DateTimeOffset minAliceDeadline, ArenaResponse<uint256> resp)
		{
			var alice = Assert.Single(round.Alices);
			Assert.NotNull(resp);
			Assert.NotNull(resp.IssuedAmountCredentials);
			Assert.NotNull(resp.IssuedVsizeCredentials);
			Assert.True(minAliceDeadline <= alice.Deadline);
		}

		[Fact]
		public async Task SuccessAsync()
		{
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);

			using Key key = new();
			var coin = WabiSabiFactory.CreateCoin(key);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, WabiSabiFactory.CreatePreconfiguredRpcClient(coin), round);

			var minAliceDeadline = DateTimeOffset.UtcNow + cfg.ConnectionConfirmationTimeout * 0.9;
			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);
			var resp = await arenaClient.RegisterInputAsync(round.Id, coin.Outpoint, key, CancellationToken.None);
			AssertSingleAliceSuccessfullyRegistered(round, minAliceDeadline, resp);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task SuccessWithAliceUpdateIntraRoundAsync()
		{
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);

			using Key key = new();
			var coin = WabiSabiFactory.CreateCoin(key);

			// Make sure an Alice have already been registered with the same input.
			var preAlice = WabiSabiFactory.CreateAlice(coin, WabiSabiFactory.CreateOwnershipProof(key), round);
			round.Alices.Add(preAlice);

			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, WabiSabiFactory.CreatePreconfiguredRpcClient(coin), round);

			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);
			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(async () => await arenaClient.RegisterInputAsync(round.Id, coin.Outpoint, key, CancellationToken.None).ConfigureAwait(false));
			Assert.Equal(WabiSabiProtocolErrorCode.AliceAlreadyRegistered, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}
	}
}
