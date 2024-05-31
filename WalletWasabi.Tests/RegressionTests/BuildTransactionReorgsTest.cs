using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.RPC;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionBroadcasting;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Blockchain.TransactionProcessing;
using WalletWasabi.FeeRateEstimation;
using WalletWasabi.Helpers;
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
public class BuildTransactionReorgsTest : IClassFixture<RegTestFixture>
{
	public BuildTransactionReorgsTest(RegTestFixture regTestFixture)
	{
		RegTestFixture = regTestFixture;
	}

	private RegTestFixture RegTestFixture { get; }

	[Fact]
	public async Task BuildTransactionReorgsTestAsync()
	{
		using CancellationTokenSource testDeadlineCts = new(TimeSpan.FromMinutes(5));

		await using RegTestSetup setup = await RegTestSetup.InitializeTestEnvironmentAsync(RegTestFixture, numberOfBlocksToGenerate: 1, nameof(BuildTransactionReorgsTestAsync));
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
		var workDir = Helpers.Common.GetWorkDir(nameof(BuildTransactionReorgsTestAsync));

		using MemoryCache cache = CreateMemoryCache();
		await using SpecificNodeBlockProvider specificNodeBlockProvider = new(network, serviceConfiguration, httpClientFactory.TorEndpoint);

		using BlockDownloadService blockDownloadService = new(
			bitcoinStore.BlockRepository,
			[specificNodeBlockProvider],
			new P2PBlockProvider(network, nodes, httpClientFactory.IsTorEnabled));

		WalletFactory walletFactory = new(workDir, network, bitcoinStore, synchronizer, httpClientFactory.SharedWasabiClient, serviceConfiguration, feeProvider, blockDownloadService, unconfirmedChainProvider);
		WalletManager walletManager = new(network, workDir, new WalletDirectories(network, workDir), walletFactory);
		walletManager.Initialize();

		var baseTip = await rpc.GetBestBlockHashAsync();

		// Generate script
		using var k = new Key();
		var scp = k.PubKey.GetAddress(ScriptPubKeyType.Legacy, Network.Main);

		// Get some money, make it confirm.
		var key = keyManager.GetNextReceiveKey("foo label");
		var fundingTxId = await rpc.SendToAddressAsync(key.GetP2wpkhAddress(network), Money.Coins(0.1m));

		// Generate some coins
		await rpc.GenerateAsync(2);

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

			var coin = Assert.Single(wallet.Coins);
			Assert.True(coin.Confirmed);
			TransactionBroadcaster broadcaster = new(network, bitcoinStore, walletManager);
			broadcaster.Initialize(nodes, rpc);

			// Send money before reorg.
			PaymentIntent operations = new(scp, Money.Coins(0.011m));
			var btx1 = wallet.BuildTransaction(password, operations, FeeStrategy.TwentyMinutesConfirmationTargetStrategy);
			await broadcaster.SendTransactionAsync(btx1.Transaction);
			var coin2 = Assert.Single(wallet.Coins);
			Assert.NotEqual(coin, coin2);
			Assert.False(coin2.Confirmed);

			operations = new PaymentIntent(scp, Money.Coins(0.012m));
			var btx2 = wallet.BuildTransaction(password, operations, FeeStrategy.TwentyMinutesConfirmationTargetStrategy, allowUnconfirmed: true);
			await broadcaster.SendTransactionAsync(btx2.Transaction);
			var coin3 = Assert.Single(wallet.Coins);
			Assert.NotEqual(coin2, coin3);
			Assert.False(coin3.Confirmed);

			// Test synchronization after fork.
			// Invalidate the blocks containing the funding transaction
			var tip = await rpc.GetBestBlockHashAsync();
			await rpc.InvalidateBlockAsync(tip); // Reorg 1
			tip = await rpc.GetBestBlockHashAsync();
			await rpc.InvalidateBlockAsync(tip); // Reorg 2

			// Generate three new blocks (replace the previous invalidated ones)
			Interlocked.Exchange(ref setup.FiltersProcessedByWalletCount, 0);
			await rpc.GenerateAsync(3);
			await setup.WaitForFiltersToBeProcessedAsync(TimeSpan.FromSeconds(120), 3);
			await Task.Delay(100); // Wait for tx processing.

			var coin4 = Assert.Single(wallet.Coins);
			Assert.Equal(coin3, coin4);
			Assert.True(coin.Confirmed);
			Assert.True(coin2.Confirmed);
			Assert.True(coin3.Confirmed);
			Assert.True(coin4.Confirmed);

			// Send money after reorg.
			// When we invalidate a block, the transactions set in the invalidated block
			// are reintroduced when we generate a new block through the rpc call
			operations = new PaymentIntent(scp, Money.Coins(0.013m));
			var btx3 = wallet.BuildTransaction(password, operations, FeeStrategy.TwentyMinutesConfirmationTargetStrategy);
			await broadcaster.SendTransactionAsync(btx3.Transaction);
			var coin5 = Assert.Single(wallet.Coins);
			Assert.NotEqual(coin4, coin5);
			Assert.False(coin5.Confirmed);

			operations = new PaymentIntent(scp, Money.Coins(0.014m));
			var btx4 = wallet.BuildTransaction(password, operations, FeeStrategy.TwentyMinutesConfirmationTargetStrategy, allowUnconfirmed: true);
			await broadcaster.SendTransactionAsync(btx4.Transaction);
			var coin6 = Assert.Single(wallet.Coins);
			Assert.NotEqual(coin5, coin6);
			Assert.False(coin6.Confirmed);

			// Test synchronization after fork with different transactions.
			// Create a fork that invalidates the blocks containing the funding transaction
			Interlocked.Exchange(ref setup.FiltersProcessedByWalletCount, 0);
			await rpc.InvalidateBlockAsync(baseTip);
			try
			{
				await rpc.AbandonTransactionAsync(fundingTxId);
			}
			catch
			{
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					throw;
				}

				return; // Occasionally this fails on Linux or OSX, I have no idea why.
			}

			// Spend the inputs of the tx so we know
			var success = bitcoinStore.TransactionStore.TryGetTransaction(fundingTxId, out var invalidSmartTransaction);
			Assert.True(success);
			var invalidCoin = Assert.Single(wallet.GetAllCoins().CreatedBy(invalidSmartTransaction!.GetHash()));
			Assert.NotNull(invalidCoin.SpenderTransaction);
			Assert.True(invalidCoin.Confirmed);

			var overwriteTx = Transaction.Create(network);
			overwriteTx.Inputs.AddRange(invalidSmartTransaction.Transaction.Inputs);
			var walletAddress = keyManager.GetNextReceiveKey("foo").GetP2wpkhAddress(network);
			bool onAddress = false;
			foreach (var invalidOutput in invalidSmartTransaction.Transaction.Outputs)
			{
				if (onAddress)
				{
					using Key newKey = new();
					overwriteTx.Outputs.Add(new TxOut(invalidOutput.Value, newKey.GetAddress(ScriptPubKeyType.Segwit, network)));
				}
				else
				{
					overwriteTx.Outputs.Add(new TxOut(invalidOutput.Value, walletAddress));
					onAddress = true;
				}
			}

			var srtxwwreq = new SignRawTransactionRequest()
			{
				Transaction = overwriteTx
			};

			var srtxwwres = await rpc.SignRawTransactionWithWalletAsync(srtxwwreq);

			var eventAwaiter = new EventAwaiter<ProcessedResult>(
				h => wallet.TransactionProcessor.WalletRelevantTransactionProcessed += h,
				h => wallet.TransactionProcessor.WalletRelevantTransactionProcessed -= h);
			await rpc.SendRawTransactionAsync(srtxwwres.SignedTransaction);
			await rpc.GenerateAsync(10);
			await setup.WaitForFiltersToBeProcessedAsync(TimeSpan.FromSeconds(120), 10);
			var eventArgs = await eventAwaiter.WaitAsync(TimeSpan.FromSeconds(21));
			var doubleSpend = Assert.Single(eventArgs.SuccessfullyDoubleSpentCoins);
			Assert.Equal(invalidCoin.TransactionId, doubleSpend.TransactionId);

			var curBlockHash = await rpc.GetBestBlockHashAsync();
			blockCount = await rpc.GetBlockCountAsync();
			Assert.Equal(bitcoinStore.SmartHeaderChain.TipHash, curBlockHash);
			Assert.Equal((int)bitcoinStore.SmartHeaderChain.TipHeight, blockCount);

			// Make sure the funding transaction is not in any block of the chain
			while (curBlockHash != rpc.Network.GenesisHash)
			{
				var block = await rpc.GetBlockAsync(curBlockHash);

				if (block.Transactions.Any(tx => tx.GetHash() == fundingTxId))
				{
					throw new InvalidOperationException($"Transaction found in block at height {blockCount} hash: {block.GetHash()}");
				}

				curBlockHash = block.Header.HashPrevBlock;
				blockCount--;
			}

			// Get some money, make it confirm.
			// this is necessary because we are in a fork now.
			eventAwaiter = new EventAwaiter<ProcessedResult>(
							h => wallet.TransactionProcessor.WalletRelevantTransactionProcessed += h,
							h => wallet.TransactionProcessor.WalletRelevantTransactionProcessed -= h);
			fundingTxId = await rpc.SendToAddressAsync(key.GetP2wpkhAddress(network), Money.Coins(1m), replaceable: true);
			eventArgs = await eventAwaiter.WaitAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(fundingTxId, eventArgs.NewlyReceivedCoins.Single().TransactionId);
			Assert.Contains(fundingTxId, wallet.Coins.Select(x => x.TransactionId));

			var fundingBumpTxId = await rpc.BumpFeeAsync(fundingTxId);
			await Task.Delay(2000); // Waits for the funding transaction get to the mempool.
			Assert.Contains(fundingBumpTxId.TransactionId, wallet.Coins.Select(x => x.TransactionId));
			Assert.DoesNotContain(fundingTxId, wallet.Coins.Select(x => x.TransactionId));
			Assert.Single(wallet.Coins.Where(x => x.TransactionId == fundingBumpTxId.TransactionId));

			// Confirm the coin
			Interlocked.Exchange(ref setup.FiltersProcessedByWalletCount, 0);
			await rpc.GenerateAsync(1);
			await setup.WaitForFiltersToBeProcessedAsync(TimeSpan.FromSeconds(120), 1);

			Assert.Single(wallet.Coins.Where(x => x.Confirmed && x.TransactionId == fundingBumpTxId.TransactionId));
		}
		finally
		{
			bitcoinStore.IndexStore.NewFilters -= setup.Wallet_NewFiltersProcessed;
			await walletManager.RemoveAndStopAllAsync(testDeadlineCts.Token);
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
