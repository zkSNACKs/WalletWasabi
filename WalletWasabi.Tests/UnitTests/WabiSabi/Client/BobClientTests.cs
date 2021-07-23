using NBitcoin;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Backend.Controllers;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Crypto.Randomness;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.WabiSabi;
using WalletWasabi.WabiSabi.Backend;
using WalletWasabi.WabiSabi.Backend.Banning;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.WabiSabi.Models;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Client
{
	public class BobClientTests
	{
		[Fact]
		public async Task RegisterOutputTestAsync()
		{
			var config = new WabiSabiConfig { MaxInputCountByRound = 1 };
			var round = WabiSabiFactory.CreateRound(config);
			var km = ServiceFactory.CreateKeyManager("");
			var key = BitcoinFactory.CreateHdPubKey(km);
			SmartCoin coin1 = BitcoinFactory.CreateSmartCoin(key, Money.Coins(2m));

			var mockRpc = WabiSabiFactory.CreatePreconfiguredRpcClient(coin1.Coin);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(config, mockRpc, round);
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromMinutes(1));

			await using var coordinator = new ArenaRequestHandler(config, new Prison(), arena, mockRpc.Object);
			var insecureRandom = new InsecureRandom();
			var wabiSabiApi = new WabiSabiController(coordinator);
			var roundState = RoundState.FromRound(round);
			var aliceArenaClient = new ArenaClient(
				roundState.CreateAmountCredentialClient(insecureRandom),
				roundState.CreateVsizeCredentialClient(insecureRandom),
				wabiSabiApi);
			var bobArenaClient = new ArenaClient(
				roundState.CreateAmountCredentialClient(insecureRandom),
				roundState.CreateVsizeCredentialClient(insecureRandom),
				wabiSabiApi);
			Assert.Equal(Phase.InputRegistration, round.Phase);

			var bitcoinSecret = km.GetSecrets("", coin1.ScriptPubKey).Single().PrivateKey.GetBitcoinSecret(Network.Main);

			var aliceClient = new AliceClient(round.Id, aliceArenaClient, coin1.Coin, round.FeeRate, bitcoinSecret);
			await aliceClient.RegisterInputAsync(CancellationToken.None);

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromMinutes(1));

			Task confirmationTask = aliceClient.ConfirmConnectionAsync(
				TimeSpan.FromSeconds(1),
				new long[] { coin1.EffectiveValue(round.FeeRate) },
				new long[] { roundState.MaxVsizeAllocationPerAlice - coin1.ScriptPubKey.EstimateInputVsize() },
				CancellationToken.None);

			await confirmationTask;

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromMinutes(1));
			Assert.Equal(Phase.OutputRegistration, round.Phase);

			using var destinationKey = new Key();
			var destination = destinationKey.PubKey.WitHash.ScriptPubKey;

			var bobClient = new BobClient(round.Id, bobArenaClient);

			await bobClient.RegisterOutputAsync(
				Money.Coins(0.25m),
				destination,
				aliceClient.IssuedAmountCredentials.Take(ProtocolConstants.CredentialNumber),
				aliceClient.IssuedVsizeCredentials.Take(ProtocolConstants.CredentialNumber),
				CancellationToken.None);

			var bob = Assert.Single(round.Bobs);
			Assert.Equal(destination, bob.Script);
			Assert.Equal(25_000_000, bob.CredentialAmount);
		}
	}
}
