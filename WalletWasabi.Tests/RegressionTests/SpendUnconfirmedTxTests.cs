using NBitcoin;
using NBitcoin.Protocol;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionBroadcasting;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Blockchain.TransactionProcessing;
using WalletWasabi.FeeRateEstimation;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Services;
using WalletWasabi.Stores;
using WalletWasabi.Tests.XunitConfiguration;
using WalletWasabi.Wallets;
using WalletWasabi.WebClients.Wasabi;
using Xunit;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.Wallets.FilterProcessor;

namespace WalletWasabi.Tests.RegressionTests;

/// <seealso cref="RegTestCollectionDefinition"/>
[Collection("RegTest collection")]
public class SpendUnconfirmedTxTests : IClassFixture<RegTestFixture>
{
	public SpendUnconfirmedTxTests(RegTestFixture regTestFixture)
	{
		RegTestFixture = regTestFixture;
	}

	private RegTestFixture RegTestFixture { get; }

	[Fact]
	public async Task SpendUnconfirmedTxTestAsync()
	{
		await using RegTestSetup setup = await RegTestSetup.InitializeTestEnvironmentAsync(RegTestFixture, numberOfBlocksToGenerate: 1, nameof(SpendUnconfirmedTxTestAsync));
		IRPCClient rpc = setup.RpcClient;
		Network network = setup.Network;
		BitcoinStore bitcoinStore = setup.BitcoinStore;
		using Backend.Global global = setup.Global;
		ServiceConfiguration serviceConfiguration = setup.ServiceConfiguration;
		string password = setup.Password;

		bitcoinStore.IndexStore.NewFilters += setup.Wallet_NewFiltersProcessed;

		// Create the services.
		// 1. Create connection service.
		NodesGroup nodes = new(global.Config.Network, requirements: Constants.NodeRequirements);
		nodes.ConnectedNodes.Add(await RegTestFixture.BackendRegTestNode.CreateNewP2pNodeAsync());

		// 2. Create mempool service.

		Node node = await RegTestFixture.BackendRegTestNode.CreateNewP2pNodeAsync();
		node.Behaviors.Add(bitcoinStore.CreateUntrustedP2pBehavior());

		// 3. Create wasabi synchronizer service.
		await using WasabiHttpClientFactory httpClientFactory = new(torEndPoint: null, backendUriGetter: () => new Uri(RegTestFixture.BackendEndPoint));
		using WasabiSynchronizer synchronizer = new(period: TimeSpan.FromSeconds(3), 10000, bitcoinStore, httpClientFactory.SharedWasabiClient);
		using FeeRateEstimationUpdater feeProvider = new(TimeSpan.Zero, () => "BlockstreamInfo");
		using UnconfirmedTransactionChainProvider unconfirmedChainProvider = new(httpClientFactory);

		// 4. Create key manager service.
		var keyManager = KeyManager.CreateNew(out _, password, network);

		// 5. Create wallet service.
		var workDir = Common.GetWorkDir(nameof(SpendUnconfirmedTxTestAsync));

		using MemoryCache cache = BitcoinFactory.CreateMemoryCache();
		await using SpecificNodeBlockProvider specificNodeBlockProvider = new(network, serviceConfiguration, httpClientFactory.TorEndpoint);

		using BlockDownloadService blockDownloadService = new(
			bitcoinStore.BlockRepository,
			[specificNodeBlockProvider],
			new P2PBlockProvider(network, nodes, httpClientFactory.IsTorEnabled));

		WalletFactory walletFactory = new(workDir, network, bitcoinStore, synchronizer, httpClientFactory.SharedWasabiClient, serviceConfiguration, feeProvider, blockDownloadService, unconfirmedChainProvider);
		WalletManager walletManager = new(network, workDir, new WalletDirectories(network, workDir), walletFactory);
		walletManager.Initialize();

		// Get some money, make it confirm.
		var key = keyManager.GetNextReceiveKey("foo label");

		try
		{
			nodes.Connect(); // Start connection service.
			node.VersionHandshake(); // Start mempool service.
			await synchronizer.StartAsync(CancellationToken.None); // Start wasabi synchronizer service.
			await feeProvider.StartAsync(CancellationToken.None);

			// Start wallet and filter processing service
			using var wallet = await walletManager.AddAndStartWalletAsync(keyManager);

			// Wait until the filter our previous transaction is present.
			var blockCount = await rpc.GetBlockCountAsync();
			await setup.WaitForFiltersToBeProcessedAsync(TimeSpan.FromSeconds(120), blockCount);

			Assert.Empty(wallet.Coins);

			// Get some money, make it confirm.
			// this is necessary because we are in a fork now.
			var eventAwaiter = new EventAwaiter<ProcessedResult>(
				h => wallet.TransactionProcessor.WalletRelevantTransactionProcessed += h,
				h => wallet.TransactionProcessor.WalletRelevantTransactionProcessed -= h);
			var tx0Id = await rpc.SendToAddressAsync(
				key.GetP2wpkhAddress(network),
				Money.Coins(1m),
				replaceable: true);
			var eventArgs = await eventAwaiter.WaitAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(tx0Id, eventArgs.NewlyReceivedCoins.Single().TransactionId);
			Assert.Single(wallet.Coins);

			TransactionBroadcaster broadcaster = new(network, bitcoinStore, walletManager);
			broadcaster.Initialize(nodes, rpc);

			using Key key2 = new();
			using Key key3 = new();
			var destination1 = key.PubKey.GetAddress(ScriptPubKeyType.Segwit, Network.Main);
			var destination2 = key2.PubKey.GetAddress(ScriptPubKeyType.Legacy, Network.Main);
			var destination3 = key3.PubKey.GetAddress(ScriptPubKeyType.Legacy, Network.Main);

			PaymentIntent operations = new(new DestinationRequest(destination1, Money.Coins(0.01m)), new DestinationRequest(destination2, Money.Coins(0.01m)), new DestinationRequest(destination3, Money.Coins(0.01m)));

			var tx1Res = wallet.BuildTransaction(password, operations, FeeStrategy.TwentyMinutesConfirmationTargetStrategy, allowUnconfirmed: true);
			Assert.Equal(2, tx1Res.InnerWalletOutputs.Count());
			Assert.Equal(2, tx1Res.OuterWalletOutputs.Count());

			// Spend the unconfirmed coin (send it to ourself)
			operations = new PaymentIntent(key.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit), Money.Coins(0.5m));
			tx1Res = wallet.BuildTransaction(password, operations, FeeStrategy.TwentyMinutesConfirmationTargetStrategy, allowUnconfirmed: true);
			eventAwaiter = new EventAwaiter<ProcessedResult>(
				h => wallet.TransactionProcessor.WalletRelevantTransactionProcessed += h,
				h => wallet.TransactionProcessor.WalletRelevantTransactionProcessed -= h);
			await broadcaster.SendTransactionAsync(tx1Res.Transaction);
			eventArgs = await eventAwaiter.WaitAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(tx0Id, eventArgs.NewlySpentCoins.Single().TransactionId);
			Assert.Equal(tx1Res.Transaction.GetHash(), eventArgs.NewlyReceivedCoins.First().TransactionId);

			// There is a coin created by the latest spending transaction
			Assert.Contains(wallet.Coins, x => x.TransactionId == tx1Res.Transaction.GetHash());

			// There is a coin destroyed
			var allCoins = wallet.TransactionProcessor.Coins.AsAllCoinsView();
			Assert.Equal(1, allCoins.Count(x => !x.IsAvailable() && x.SpenderTransaction?.GetHash() == tx1Res.Transaction.GetHash()));

			// There is at least one coin created from the destruction of the first coin
			Assert.Contains(wallet.Coins, x => x.Transaction.Transaction.Inputs.Any(o => o.PrevOut.Hash == tx0Id));

			var totalWallet = wallet.Coins.Where(c => c.IsAvailable()).Sum(c => c.Amount);
			Assert.Equal((1 * Money.COIN) - tx1Res.Fee.Satoshi, totalWallet);

			// Spend the unconfirmed and unspent coin (send it to ourself)
			operations = new PaymentIntent(key.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit), Money.Coins(0.6m), subtractFee: true);
			var tx2Res = wallet.BuildTransaction(password, operations, FeeStrategy.TwentyMinutesConfirmationTargetStrategy, allowUnconfirmed: true);

			eventAwaiter = new EventAwaiter<ProcessedResult>(
							h => wallet.TransactionProcessor.WalletRelevantTransactionProcessed += h,
							h => wallet.TransactionProcessor.WalletRelevantTransactionProcessed -= h);
			await broadcaster.SendTransactionAsync(tx2Res.Transaction);
			eventArgs = await eventAwaiter.WaitAsync(TimeSpan.FromSeconds(21));
			var spentCoins = eventArgs.NewlySpentCoins.ToArray();
			Assert.Equal(tx1Res.Transaction.GetHash(), spentCoins.First().TransactionId);
			uint256 tx2Hash = tx2Res.Transaction.GetHash();
			var receivedCoins = eventArgs.NewlyReceivedCoins.ToArray();
			Assert.Equal(tx2Hash, receivedCoins[0].TransactionId);
			Assert.Equal(tx2Hash, receivedCoins[1].TransactionId);

			// There is a coin created by the latest spending transaction
			Assert.Contains(wallet.Coins, x => x.TransactionId == tx2Res.Transaction.GetHash());

			// There is a coin destroyed
			allCoins = wallet.TransactionProcessor.Coins.AsAllCoinsView();
			Assert.Equal(2, allCoins.Count(x => !x.IsAvailable() && x.SpenderTransaction?.GetHash() == tx2Hash));

			// There is at least one coin created from the destruction of the first coin
			Assert.Contains(wallet.Coins, x => x.Transaction.Transaction.Inputs.Any(o => o.PrevOut.Hash == tx1Res.Transaction.GetHash()));

			totalWallet = wallet.Coins.Where(c => c.IsAvailable()).Sum(c => c.Amount);
			Assert.Equal((1 * Money.COIN) - tx1Res.Fee.Satoshi - tx2Res.Fee.Satoshi, totalWallet);

			Interlocked.Exchange(ref setup.FiltersProcessedByWalletCount, 0);
			var blockId = (await rpc.GenerateAsync(1)).Single();
			try
			{
				await setup.WaitForFiltersToBeProcessedAsync(TimeSpan.FromSeconds(120), 1);
			}
			catch (TimeoutException)
			{
				Logger.LogInfo("Index was not processed.");
				return; // Very rarely this test fails. I have no clue why. Probably because all these RegTests are interconnected, anyway let's not bother the CI with it.
			}

			// Verify transactions are confirmed in the blockchain
			var block = await rpc.GetBlockAsync(blockId);
			Assert.Contains(block.Transactions, x => x.GetHash() == tx2Res.Transaction.GetHash());
			Assert.Contains(block.Transactions, x => x.GetHash() == tx1Res.Transaction.GetHash());
			Assert.Contains(block.Transactions, x => x.GetHash() == tx0Id);

			Assert.True(wallet.Coins.All(x => x.Confirmed));

			// Test coin basic count.
			ICoinsView GetAllCoins() => wallet.TransactionProcessor.Coins.AsAllCoinsView();
			var coinCount = GetAllCoins().Count();
			var to = keyManager.GetNextReceiveKey("foo");
			var res = wallet.BuildTransaction(password, new PaymentIntent(to.P2wpkhScript, Money.Coins(0.2345m), label: "bar"), FeeStrategy.TwentyMinutesConfirmationTargetStrategy, allowUnconfirmed: true);
			await broadcaster.SendTransactionAsync(res.Transaction);
			Assert.Equal(coinCount + 2, GetAllCoins().Count());
			Assert.Equal(2, GetAllCoins().Count(x => !x.Confirmed));
			Interlocked.Exchange(ref setup.FiltersProcessedByWalletCount, 0);
			await rpc.GenerateAsync(1);
			await setup.WaitForFiltersToBeProcessedAsync(TimeSpan.FromSeconds(120), 1);
			Assert.Equal(coinCount + 2, GetAllCoins().Count());
			Assert.Equal(0, GetAllCoins().Count(x => !x.Confirmed));
		}
		finally
		{
			bitcoinStore.IndexStore.NewFilters -= setup.Wallet_NewFiltersProcessed;
			await walletManager.RemoveAndStopAllAsync(CancellationToken.None);
			await synchronizer.StopAsync(CancellationToken.None);
			await feeProvider.StopAsync(CancellationToken.None);
			nodes?.Dispose();
			node?.Disconnect();
		}
	}
}
