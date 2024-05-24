using NBitcoin;
using NBitcoin.Protocol;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Exceptions;
using WalletWasabi.FeeRateEstimation;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Services;
using WalletWasabi.Stores;
using WalletWasabi.Tests.XunitConfiguration;
using WalletWasabi.Wallets;
using WalletWasabi.Wallets.FilterProcessor;
using WalletWasabi.WebClients.Wasabi;
using Xunit;

namespace WalletWasabi.Tests.RegressionTests;

/// <seealso cref="RegTestCollectionDefinition"/>
[Collection("RegTest collection")]
public class BuildTransactionValidationsTest : IClassFixture<RegTestFixture>
{
	public BuildTransactionValidationsTest(RegTestFixture regTestFixture)
	{
		RegTestFixture = regTestFixture;
	}

	private RegTestFixture RegTestFixture { get; }

	[Fact]
	public async Task BuildTransactionValidationsTestAsync()
	{
		using CancellationTokenSource testDeadlineCts = new(TimeSpan.FromMinutes(3));

		await using RegTestSetup setup = await RegTestSetup.InitializeTestEnvironmentAsync(RegTestFixture, numberOfBlocksToGenerate: 1, nameof(BuildTransactionValidationsTestAsync));
		IRPCClient rpc = setup.RpcClient;
		Network network = setup.Network;
		BitcoinStore bitcoinStore = setup.BitcoinStore;
		string password = setup.Password;
		ServiceConfiguration serviceConfiguration = setup.ServiceConfiguration;

		// Create the services.
		// 1. Create connection service.
		NodesGroup nodes = new(setup.Network, requirements: Constants.NodeRequirements);
		nodes.ConnectedNodes.Add(await RegTestFixture.BackendRegTestNode.CreateNewP2pNodeAsync());

		// 2. Create mempool service.

		Node node = await RegTestFixture.BackendRegTestNode.CreateNewP2pNodeAsync();
		node.Behaviors.Add(bitcoinStore.CreateUntrustedP2pBehavior());

		// 3. Create wasabi synchronizer service.
		await using WasabiHttpClientFactory httpClientFactory = new(torEndPoint: null, backendUriGetter: () => new Uri(RegTestFixture.BackendEndPoint));
		using WasabiSynchronizer synchronizer = new(period: TimeSpan.FromSeconds(3), 10000, bitcoinStore, httpClientFactory.SharedWasabiClient);
		using FeeRateEstimationUpdater feeProvider = new(TimeSpan.Zero, () => "BlockstreamInfo");

		// 4. Create key manager service.
		var keyManager = KeyManager.CreateNew(out _, password, network);

		// 5. Create wallet service.
		var workDir = Helpers.Common.GetWorkDir(nameof(BuildTransactionValidationsTestAsync));

		using MemoryCache cache = CreateMemoryCache();
		await using SpecificNodeBlockProvider specificNodeBlockProvider = new(network, serviceConfiguration, httpClientFactory.TorEndpoint);

		using BlockDownloadService blockDownloadService = new(
			bitcoinStore.BlockRepository,
			[specificNodeBlockProvider],
			new P2PBlockProvider(network, nodes, httpClientFactory.IsTorEnabled));

		using UnconfirmedTransactionChainProvider unconfirmedChainProvider = new(httpClientFactory);

		WalletFactory walletFactory = new(workDir, network, bitcoinStore, synchronizer, httpClientFactory.SharedWasabiClient, serviceConfiguration, feeProvider, blockDownloadService, unconfirmedChainProvider);
		using Wallet wallet = walletFactory.CreateAndInitialize(keyManager);
		wallet.NewFiltersProcessed += setup.Wallet_NewFiltersProcessed;

		using Key key = new();
		var scp = key.PubKey.GetAddress(ScriptPubKeyType.Legacy, Network.Main);

		PaymentIntent validIntent = new(scp, Money.Coins(1));
		PaymentIntent invalidIntent = new(new DestinationRequest(scp, Money.Coins(10 * 1000 * 1000)), new DestinationRequest(scp, Money.Coins(12 * 1000 * 1000)));

		Assert.Throws<OverflowException>(() => new PaymentIntent(
			new DestinationRequest(scp, Money.Satoshis(long.MaxValue)),
			new DestinationRequest(scp, Money.Satoshis(long.MaxValue)),
			new DestinationRequest(scp, Money.Satoshis(5))));

		Logger.TurnOff();

		// toSend cannot have a zero element
		Assert.Throws<ArgumentException>(() => wallet.BuildTransaction("", new PaymentIntent(Array.Empty<DestinationRequest>()), FeeStrategy.SevenDaysConfirmationTargetStrategy));

		// feeTarget has to be in the range 0 to 1008
		Assert.Throws<ArgumentOutOfRangeException>(() => wallet.BuildTransaction("", validIntent, FeeStrategy.CreateFromConfirmationTarget(-10)));
		Assert.Throws<ArgumentOutOfRangeException>(() => wallet.BuildTransaction("", validIntent, FeeStrategy.CreateFromConfirmationTarget(2000)));

		// toSend amount sum has to be in range 0 to 2099999997690000
		Assert.Throws<ArgumentOutOfRangeException>(() => wallet.BuildTransaction("", invalidIntent, FeeStrategy.TwentyMinutesConfirmationTargetStrategy));

		// toSend negative sum amount
		Assert.Throws<ArgumentOutOfRangeException>(() => wallet.BuildTransaction("", new PaymentIntent(scp, Money.Satoshis(-10000)), FeeStrategy.TwentyMinutesConfirmationTargetStrategy));

		// toSend negative operation amount
		Assert.Throws<ArgumentOutOfRangeException>(() => wallet.BuildTransaction(
			"",
			new PaymentIntent(
				new DestinationRequest(scp, Money.Satoshis(20000)),
				new DestinationRequest(scp, Money.Satoshis(-10000))),
			FeeStrategy.TwentyMinutesConfirmationTargetStrategy));

		// allowedInputs cannot be empty
		Assert.Throws<ArgumentException>(() => wallet.BuildTransaction("", validIntent, FeeStrategy.TwentyMinutesConfirmationTargetStrategy, allowedInputs: Array.Empty<OutPoint>()));

		// "Only one element can contain the AllRemaining flag.
		Assert.Throws<ArgumentException>(() => wallet.BuildTransaction(
			password,
			new PaymentIntent(
				new DestinationRequest(scp, MoneyRequest.CreateAllRemaining(), "zero"),
				new DestinationRequest(scp, MoneyRequest.CreateAllRemaining(), "zero")),
			FeeStrategy.SevenDaysConfirmationTargetStrategy,
			false));

		// Get some money, make it confirm.
		var txId = await rpc.SendToAddressAsync(keyManager.GetNextReceiveKey("foo").GetP2wpkhAddress(network), Money.Coins(1m));

		// Generate some coins
		await rpc.GenerateAsync(2);

		try
		{
			nodes.Connect(); // Start connection service.
			node.VersionHandshake(); // Start mempool service.
			await synchronizer.StartAsync(CancellationToken.None); // Start wasabi synchronizer service.
			await feeProvider.StartAsync(CancellationToken.None);

			using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
			{
				await wallet.StartAsync(cts.Token); // Initialize wallet service with filter processing.
			}

			// Wait until the filter our previous transaction is present.
			var blockCount = await rpc.GetBlockCountAsync();
			await setup.WaitForFiltersToBeProcessedAsync(TimeSpan.FromSeconds(120), blockCount);

			// subtract Fee from amount index with no enough money
			PaymentIntent operations = new(new DestinationRequest(scp, Money.Coins(1m), subtractFee: true), new DestinationRequest(scp, Money.Coins(0.5m)));
			Assert.Throws<InsufficientBalanceException>(() => wallet.BuildTransaction(password, operations, FeeStrategy.TwentyMinutesConfirmationTargetStrategy, false));

			// No enough money (only one confirmed coin, no unconfirmed allowed)
			operations = new PaymentIntent(scp, Money.Coins(1.5m));
			Assert.Throws<InsufficientBalanceException>(() => wallet.BuildTransaction("", operations, FeeStrategy.TwentyMinutesConfirmationTargetStrategy));

			// No enough money (only one confirmed coin, unconfirmed allowed)
			Assert.Throws<InsufficientBalanceException>(() => wallet.BuildTransaction("", operations, FeeStrategy.TwentyMinutesConfirmationTargetStrategy, true));

			// Add new money with no confirmation
			var txId2 = await rpc.SendToAddressAsync(keyManager.GetNextReceiveKey("bar").GetP2wpkhAddress(network), Money.Coins(2m));
			await Task.Delay(1000); // Wait tx to arrive and get processed.

			// Enough money (one confirmed coin and one unconfirmed coin, unconfirmed are NOT allowed)
			Assert.Throws<InsufficientBalanceException>(() => wallet.BuildTransaction("", operations, FeeStrategy.TwentyMinutesConfirmationTargetStrategy, false));

			// Enough money (one unconfirmed coin, unconfirmed are allowed)
			var btx = wallet.BuildTransaction(password, operations, FeeStrategy.TwentyMinutesConfirmationTargetStrategy, true);
			var spentCoin = Assert.Single(btx.SpentCoins);
			Assert.False(spentCoin.Confirmed);

			// Enough money (one confirmed coin and one unconfirmed coin, unconfirmed are allowed)
			operations = new PaymentIntent(scp, Money.Coins(2.5m));
			btx = wallet.BuildTransaction(password, operations, FeeStrategy.TwentyMinutesConfirmationTargetStrategy, true);
			Assert.Equal(2, btx.SpentCoins.Count());
			Assert.Equal(1, btx.SpentCoins.Count(c => c.Confirmed));
			Assert.Equal(1, btx.SpentCoins.Count(c => !c.Confirmed));

			// Only one operation with AllRemainingFlag

			Assert.Throws<ArgumentException>(() => wallet.BuildTransaction(
				"",
				new PaymentIntent(
					new DestinationRequest(scp, MoneyRequest.CreateAllRemaining()),
					new DestinationRequest(scp, MoneyRequest.CreateAllRemaining())),
				FeeStrategy.TwentyMinutesConfirmationTargetStrategy));

			Logger.TurnOn();

			operations = new PaymentIntent(scp, Money.Coins(0.5m));
			btx = wallet.BuildTransaction(password, operations, FeeStrategy.TwentyMinutesConfirmationTargetStrategy);
		}
		finally
		{
			await wallet.StopAsync(testDeadlineCts.Token);
			await synchronizer.StopAsync(testDeadlineCts.Token);
			await feeProvider.StopAsync(testDeadlineCts.Token);
			nodes?.Dispose();
			node?.Disconnect();
		}
	}

	private static MemoryCache CreateMemoryCache()
	{
		return new MemoryCache(new MemoryCacheOptions
		{
			SizeLimit = 1_000,
			ExpirationScanFrequency = TimeSpan.FromSeconds(30)
		});
	}
}
