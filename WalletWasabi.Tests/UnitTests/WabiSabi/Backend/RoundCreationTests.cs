using NBitcoin;
using NBitcoin.RPC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.WabiSabi.Backend;
using WalletWasabi.WabiSabi.Backend.Banning;
using WalletWasabi.WabiSabi.Backend.Rounds;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Backend
{
	public class RoundCreationTests
	{
		[Fact]
		public async Task InitializesRoundAsync()
		{
			WabiSabiConfig cfg = new();
			var mockRpc = new MockRpcClient();
			mockRpc.OnEstimateSmartFeeAsync = async (target, _) =>
				await Task.FromResult(new EstimateSmartFeeResponse
				{
					Blocks = target,
					FeeRate = new FeeRate(10m)
				});

			using Arena arena = new(TimeSpan.FromSeconds(1), Network.Main, cfg, mockRpc, new Prison());
			Assert.Empty(arena.Rounds);
			await arena.StartAsync(CancellationToken.None);
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Single(arena.Rounds);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task CreatesRoundIfNoneInputRegistrationAsync()
		{
			WabiSabiConfig cfg = new();
			var mockRpc = new MockRpcClient();
			mockRpc.OnEstimateSmartFeeAsync = async (target, _) =>
				await Task.FromResult(new EstimateSmartFeeResponse
				{
					Blocks = target,
					FeeRate = new FeeRate(10m)
				});

			using Arena arena = new(TimeSpan.FromSeconds(1), Network.Main, cfg, mockRpc, new Prison());
			Assert.Empty(arena.Rounds);
			await arena.StartAsync(CancellationToken.None);
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			var round = Assert.Single(arena.Rounds);

			round.SetPhase(Phase.ConnectionConfirmation);
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(2, arena.Rounds.Count);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task CreatesRoundIfInBlameInputRegistrationAsync()
		{
			WabiSabiConfig cfg = new();
			var mockRpc = new MockRpcClient();
			mockRpc.OnEstimateSmartFeeAsync = async (target, _) =>
				await Task.FromResult(new EstimateSmartFeeResponse
				{
					Blocks = target,
					FeeRate = new FeeRate(10m)
				});

			using Arena arena = new(TimeSpan.FromSeconds(1), Network.Main, cfg, mockRpc, new Prison());
			Assert.Empty(arena.Rounds);
			await arena.StartAsync(CancellationToken.None);
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			var round = Assert.Single(arena.Rounds);

			round.SetPhase(Phase.ConnectionConfirmation);
			round.Alices.Add(WabiSabiFactory.CreateAlice(round));
			Round blameRound = WabiSabiFactory.CreateBlameRound(round, cfg);
			Assert.Equal(Phase.InputRegistration, blameRound.Phase);
			arena.Rounds.Add(blameRound);
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(3, arena.Rounds.Count);
			Assert.Equal(2, arena.Rounds.Where(x => x.Phase == Phase.InputRegistration).Count());

			await arena.StopAsync(CancellationToken.None);
		}
	}
}
