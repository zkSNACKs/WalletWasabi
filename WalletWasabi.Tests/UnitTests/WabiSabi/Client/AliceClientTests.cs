using Moq;
using NBitcoin;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Backend.Controllers;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Crypto.Randomness;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.WabiSabi.Backend;
using WalletWasabi.WabiSabi.Backend.Banning;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.WabiSabi.Crypto;
using WalletWasabi.WabiSabi.Models;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Client
{
	public class AliceClientTests
	{
		[Fact]
		public async Task CreateNewAsync()
		{
			var config = new WabiSabiConfig { MaxInputCountByRound = 1 };
			var round = WabiSabiFactory.CreateRound(config);
			var km = ServiceFactory.CreateKeyManager("");
			var key = BitcoinFactory.CreateHdPubKey(km);
			SmartCoin coin1 = BitcoinFactory.CreateSmartCoin(key, Money.Coins(1m));

			var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1.Coin);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(config, mockRpc, round);
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromMinutes(1));

			await using var coordinator = new ArenaRequestHandler(config, new Prison(), arena, mockRpc.Object);
			var wabiSabiApi = new WabiSabiController(coordinator);

			var insecureRandom = new InsecureRandom();
			var roundState = RoundState.FromRound(round);
			var arenaClient = new ArenaClient(
				roundState.CreateAmountCredentialClient(insecureRandom),
				roundState.CreateVsizeCredentialClient(insecureRandom),
				wabiSabiApi);
			Assert.Equal(Phase.InputRegistration, arena.Rounds.Single().Phase);

			var bitcoinSecret = km.GetSecrets("", coin1.ScriptPubKey).Single().PrivateKey.GetBitcoinSecret(Network.Main);

			var aliceClient = new AliceClient(round.Id, arenaClient, coin1.Coin, round.FeeRate, bitcoinSecret);
			await aliceClient.RegisterInputAsync(CancellationToken.None);

			Assert.Equal(Phase.InputRegistration, arena.Rounds.Single().Phase);

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromMinutes(1));

			Assert.Equal(Phase.ConnectionConfirmation, round.Phase);
			Assert.Equal(1, arena.Rounds.Count(r => r.Phase == Phase.ConnectionConfirmation));

			// Another round is also created
			Assert.Equal(1, arena.Rounds.Count(r => r.Phase == Phase.InputRegistration));
			Assert.Equal(2, arena.Rounds.Count);

			Task confirmationTask = aliceClient.ConfirmConnectionAsync(
				TimeSpan.FromSeconds(1),
				new long[] { coin1.EffectiveValue(round.FeeRate) },
				new long[] { roundState.MaxVsizeAllocationPerAlice - coin1.ScriptPubKey.EstimateInputVsize() },
				CancellationToken.None);

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromMinutes(1));
			await confirmationTask;

			Assert.Equal(Phase.OutputRegistration, round.Phase);
		}
	}
}
