using Moq;
using NBitcoin;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.WabiSabi.Backend;
using WalletWasabi.WabiSabi.Backend.Banning;
using WalletWasabi.WabiSabi.Backend.Models;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Models;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Backend.PostRequests
{
	public class RegisterInputFailureTests
	{
		private static async Task RegisterAndAssertWrongPhaseAsync(InputRegistrationRequest req, ArenaRequestHandler handler)
		{
			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(async () => await handler.RegisterInputAsync(req, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.WrongPhase, ex.ErrorCode);
		}

		[Fact]
		public async Task RoundNotFoundAsync()
		{
			using Key key = new();
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(new(), WabiSabiFactory.CreatePreconfiguredRpcClient());

			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);
			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(
				async () => await arenaClient.RegisterInputAsync(uint256.Zero, BitcoinFactory.CreateOutPoint(), key, CancellationToken.None));
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
			using Key key = new();
			var req = WabiSabiFactory.CreateInputRegistrationRequest(round, key: key);
			await using ArenaRequestHandler handler = new(cfg, new(), arena, WabiSabiFactory.CreatePreconfiguredRpcClient().Object);

			foreach (Phase phase in Enum.GetValues(typeof(Phase)))
			{
				if (phase != Phase.InputRegistration)
				{
					round.SetPhase(phase);
					await RegisterAndAssertWrongPhaseAsync(req, handler);
				}
			}

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task InputRegistrationFullAsync()
		{
			WabiSabiConfig cfg = new() { MaxInputCountByRound = 3 };
			var round = WabiSabiFactory.CreateRound(cfg);
			using Key key = new();
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, WabiSabiFactory.CreatePreconfiguredRpcClient(), round);

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));
			round.Alices.Add(WabiSabiFactory.CreateAlice(round));
			round.Alices.Add(WabiSabiFactory.CreateAlice(round));
			round.Alices.Add(WabiSabiFactory.CreateAlice(round));

			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);
			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(
				async () => await arenaClient.RegisterInputAsync(round.Id, BitcoinFactory.CreateOutPoint(), key, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.WrongPhase, ex.ErrorCode);
			Assert.Equal(Phase.InputRegistration, round.Phase);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task InputRegistrationTimedoutAsync()
		{
			WabiSabiConfig cfg = new() { StandardInputRegistrationTimeout = TimeSpan.Zero };
			var round = WabiSabiFactory.CreateRound(cfg);
			using Key key = new();
			var coin = WabiSabiFactory.CreateCoin(key);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, WabiSabiFactory.CreatePreconfiguredRpcClient(coin));

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));

			arena.Rounds.Add(round);

			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);
			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(
				async () => await arenaClient.RegisterInputAsync(round.Id, coin.Outpoint, key, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.WrongPhase, ex.ErrorCode);
			Assert.Equal(Phase.InputRegistration, round.Phase);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task InputRegistrationTimeoutCanBeModifiedRuntimeAsync()
		{
			WabiSabiConfig cfg = new() { StandardInputRegistrationTimeout = TimeSpan.FromHours(1) };
			using Key key = new();
			var coin = WabiSabiFactory.CreateCoin(key);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, WabiSabiFactory.CreatePreconfiguredRpcClient(coin));

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(21));

			var round = arena.Rounds.First();

			cfg.StandardInputRegistrationTimeout = TimeSpan.Zero;

			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);
			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(
				async () => await arenaClient.RegisterInputAsync(round.Id, coin.Outpoint, key, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.WrongPhase, ex.ErrorCode);
			Assert.Equal(Phase.InputRegistration, round.Phase);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task InputBannedAsync()
		{
			using Key key = new();
			var outpoint = BitcoinFactory.CreateOutPoint();

			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, round);
			arena.Prison.Punish(outpoint, Punishment.Banned, uint256.One);

			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);

			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(
				async () => await arenaClient.RegisterInputAsync(round.Id, outpoint, key, CancellationToken.None));

			Assert.Equal(WabiSabiProtocolErrorCode.InputBanned, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task InputCanBeNotedAsync()
		{
			using Key key = new();
			var outpoint = BitcoinFactory.CreateOutPoint();

			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, round);
			arena.Prison.Punish(outpoint, Punishment.Noted, uint256.One);

			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);

			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(
				async () => await arenaClient.RegisterInputAsync(round.Id, outpoint, key, CancellationToken.None));
			Assert.NotEqual(WabiSabiProtocolErrorCode.InputBanned, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task InputCantBeNotedAsync()
		{
			using Key key = new();
			var outpoint = BitcoinFactory.CreateOutPoint();

			WabiSabiConfig cfg = new() { AllowNotedInputRegistration = false };
			var round = WabiSabiFactory.CreateRound(cfg);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, round);
			arena.Prison.Punish(outpoint, Punishment.Noted, uint256.One);

			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);

			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(
				async () => await arenaClient.RegisterInputAsync(round.Id, outpoint, key, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.InputBanned, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task InputSpentAsync()
		{
			using Key key = new();
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);

			var mockRpc = new Mock<IRPCClient>();
			mockRpc.Setup(rpc => rpc.GetTxOutAsync(It.IsAny<uint256>(), It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync((NBitcoin.RPC.GetTxOutResponse?)null);

			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, mockRpc, round);
			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);

			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(
				async () => await arenaClient.RegisterInputAsync(round.Id, BitcoinFactory.CreateOutPoint(), key, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.InputSpent, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task InputUnconfirmedAsync()
		{
			using Key key = new();
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);

			var mockRpc = new Mock<IRPCClient>();
			mockRpc.Setup(rpc => rpc.GetTxOutAsync(It.IsAny<uint256>(), It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync(new NBitcoin.RPC.GetTxOutResponse { Confirmations = 0 });

			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, mockRpc, round);
			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);

			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(
				async () => await arenaClient.RegisterInputAsync(round.Id, BitcoinFactory.CreateOutPoint(), key, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.InputUnconfirmed, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task InputImmatureAsync()
		{
			using Key key = new();
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);
			var rpc = WabiSabiFactory.CreatePreconfiguredRpcClient();
			var rpcCfg = rpc.SetupSequence(rpc => rpc.GetTxOutAsync(It.IsAny<uint256>(), It.IsAny<int>(), It.IsAny<bool>()));
			foreach (var i in Enumerable.Range(1, 100))
			{
				rpcCfg = rpcCfg.ReturnsAsync(new NBitcoin.RPC.GetTxOutResponse { Confirmations = i, IsCoinBase = true });
			}
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, rpc, round);
			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);

			var req = WabiSabiFactory.CreateInputRegistrationRequest(round: round);
			foreach (var i in Enumerable.Range(1, 100))
			{
				var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(
					async () => await arenaClient.RegisterInputAsync(round.Id, BitcoinFactory.CreateOutPoint(), key, CancellationToken.None));

				Assert.Equal(WabiSabiProtocolErrorCode.InputImmature, ex.ErrorCode);
			}

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task ScriptNotAllowedAsync()
		{
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);
			using Key key = new();

			var mockRpc = new Mock<IRPCClient>();
			mockRpc.Setup(rpc => rpc.GetTxOutAsync(It.IsAny<uint256>(), It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync(new NBitcoin.RPC.GetTxOutResponse
				{
					Confirmations = 1,
					TxOut = new(Money.Coins(1), key.PubKey.GetScriptAddress(Network.Main))
				});

			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, mockRpc, round);

			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);

			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(
				async () => await arenaClient.RegisterInputAsync(round.Id, BitcoinFactory.CreateOutPoint(), key, CancellationToken.None));

			Assert.Equal(WabiSabiProtocolErrorCode.ScriptNotAllowed, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task WrongOwnershipProofAsync()
		{
			using Key key = new();
			var coin = WabiSabiFactory.CreateCoin(key);
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, WabiSabiFactory.CreatePreconfiguredRpcClient(coin), round);

			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);
			using Key nonOwnerKey = new();
			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(
				async () => await arenaClient.RegisterInputAsync(round.Id, coin.Outpoint, nonOwnerKey, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.WrongOwnershipProof, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task NotEnoughFundsAsync()
		{
			using Key key = new();
			WabiSabiConfig cfg = new() { MinRegistrableAmount = Money.Coins(2) };
			var round = WabiSabiFactory.CreateRound(cfg);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, round);

			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);
			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(
				async () => await arenaClient.RegisterInputAsync(round.Id, BitcoinFactory.CreateOutPoint(), key, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.NotEnoughFunds, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task TooMuchFundsAsync()
		{
			using Key key = new();
			WabiSabiConfig cfg = new() { MaxRegistrableAmount = Money.Coins(0.9m) };
			var round = WabiSabiFactory.CreateRound(cfg);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, WabiSabiFactory.CreatePreconfiguredRpcClient(), round);

			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);
			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(
				async () => await arenaClient.RegisterInputAsync(round.Id, BitcoinFactory.CreateOutPoint(), key, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.TooMuchFunds, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task TooMuchVsizeAsync()
		{
			// Configures a round that allows so many inputs (Alices) that
			// the virtual size each of they have available is not enought
			// to register anything.
			using Key key = new();
			var coin = WabiSabiFactory.CreateCoin(key);
			WabiSabiConfig cfg = new() { MaxInputCountByRound = 100_000 };
			var round = WabiSabiFactory.CreateRound(cfg);
			Assert.Equal(0, round.MaxVsizeAllocationPerAlice);

			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, WabiSabiFactory.CreatePreconfiguredRpcClient(coin), round);

			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);
			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(
				async () => await arenaClient.RegisterInputAsync(round.Id, coin.Outpoint, key, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.TooMuchVsize, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task AliceAlreadyRegisteredIntraRoundAsync()
		{
			using Key key = new();
			var coin = WabiSabiFactory.CreateCoin(key);
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);

			// Make sure an Alice have already been registered with the same input.
			var anotherAlice = WabiSabiFactory.CreateAlice(coin, WabiSabiFactory.CreateOwnershipProof(key), round);
			round.Alices.Add(anotherAlice);
			round.SetPhase(Phase.ConnectionConfirmation);

			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, WabiSabiFactory.CreatePreconfiguredRpcClient(coin), round);

			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);
			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(
				async () => await arenaClient.RegisterInputAsync(round.Id, coin.Outpoint, key, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.AliceAlreadyRegistered, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}

		[Fact]
		public async Task AliceAlreadyRegisteredCrossRoundAsync()
		{
			using Key key = new();
			var coin = WabiSabiFactory.CreateCoin(key);
			WabiSabiConfig cfg = new();
			var round = WabiSabiFactory.CreateRound(cfg);
			var anotherRound = WabiSabiFactory.CreateRound(cfg);

			// Make sure an Alice have already been registered with the same input.
			var preAlice = WabiSabiFactory.CreateAlice(coin, WabiSabiFactory.CreateOwnershipProof(key), round);
			anotherRound.Alices.Add(preAlice);
			anotherRound.SetPhase(Phase.ConnectionConfirmation);

			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(cfg, WabiSabiFactory.CreatePreconfiguredRpcClient(coin), round, anotherRound);

			var arenaClient = WabiSabiFactory.CreateArenaClient(arena);
			var ex = await Assert.ThrowsAsync<WabiSabiProtocolException>(
				async () => await arenaClient.RegisterInputAsync(round.Id, coin.Outpoint, key, CancellationToken.None));
			Assert.Equal(WabiSabiProtocolErrorCode.AliceAlreadyRegistered, ex.ErrorCode);

			await arena.StopAsync(CancellationToken.None);
		}
	}
}
