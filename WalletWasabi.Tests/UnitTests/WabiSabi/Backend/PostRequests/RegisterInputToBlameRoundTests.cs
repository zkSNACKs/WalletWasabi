using NBitcoin;
using System;
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
	public class RegisterInputToBlameRoundTests
	{
		[Fact]
		public async Task InputNotWhitelistedAsync()
		{
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);
			round.Alices.Add(WabiSabiFactory.CreateAlice(round));
			Round blameRound = WabiSabiFactory.CreateBlameRound(round, cfg);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, round, blameRound);
			using Key key = new();
			var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient();

			await using ArenaRequestHandler handler = new(cfg, new(), arena, mockRpc.Object);
			var req = WabiSabiFactory.CreateInputRegistrationRequest(round: blameRound);
			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(async () => await handler.RegisterInputAsync(req, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.InputNotWhitelisted, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task InputWhitelistedAsync()
		{
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);
			var alice = WabiSabiFactory.CreateAlice(round);
			round.Alices.Add(alice);
			Round blameRound = WabiSabiFactory.CreateBlameRound(round, cfg);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, round, blameRound);

			await using ArenaRequestHandler handler = new(cfg, new(), arena, WabiSabiFactory.CreatePreconfiguredRpcClient().Object);
			var req = WabiSabiFactory.CreateInputRegistrationRequest(prevout: alice.Coin.Outpoint, round: blameRound);

			var ex = await Assert.ThrowsAnyAsync<Exception>(async () => await handler.RegisterInputAsync(req, CancellationToken.None));
			if (ex is WabiSabiProtocolException wspex)
			{
				Assert.NotEqual(WabiSabiProtocolErrorCode.InputNotWhitelisted, wspex.ErrorCode);
			}

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task InputWhitelistedButBannedAsync()
		{
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);

			using Key key = new();
			var alice = WabiSabiFactory.CreateAlice(key, round);
			var bannedCoin = alice.Coin.Outpoint;

			round.Alices.Add(alice);
			Round blameRound = WabiSabiFactory.CreateBlameRound(round, cfg);
			var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(alice.Coin);

			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, mockRpc, round, blameRound);
			arena.Prison.Punish(bannedCoin, Punishment.Banned, uint256.Zero);
			await using ArenaRequestHandler handler = new(cfg, arena.Prison, arena, mockRpc.Object);

			var req = WabiSabiFactory.CreateInputRegistrationRequest(key: key, round: blameRound, prevout: bannedCoin);
			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(async () => await handler.RegisterInputAsync(req, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.InputBanned, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}
	}
}
