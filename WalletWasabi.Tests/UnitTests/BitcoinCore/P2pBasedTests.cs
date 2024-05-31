using NBitcoin;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WalletWasabi.BitcoinCore;
using WalletWasabi.Blockchain.Blocks;
using WalletWasabi.Blockchain.Mempool;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Helpers;
using WalletWasabi.Services;
using WalletWasabi.Stores;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.Wallets;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.BitcoinCore;

/// <seealso cref="XunitConfiguration.SerialCollectionDefinition"/>
[Collection("Serial unit tests collection")]
public class P2pBasedTests
{
	[Fact]
	public async Task MempoolNotifiesAsync()
	{
		CoreNode coreNode = await TestNodeBuilder.CreateAsync(nameof(MempoolNotifiesAsync));

		using var node = await coreNode.CreateNewP2pNodeAsync();

		try
		{
			string dir = Common.GetWorkDir(nameof(MempoolNotifiesAsync));
			var network = coreNode.Network;
			var rpc = coreNode.RpcClient;

			var walletName = "wallet";
			await rpc.CreateWalletAsync(walletName);

			SmartHeaderChain smartHeaderChain = new();
			await using IndexStore indexStore = new(Path.Combine(dir, "indexStore"), network, smartHeaderChain);
			await using AllTransactionStore transactionStore = new(Path.Combine(dir, "transactionStore"), network);
			MempoolService mempoolService = coreNode.MempoolService;
			FileSystemBlockRepository blocks = new(Path.Combine(dir, "blocks"), network);

			// Construct BitcoinStore.
			BitcoinStore bitcoinStore = new(indexStore, transactionStore, mempoolService, smartHeaderChain, blocks);
			await bitcoinStore.InitializeAsync();

			await rpc.GenerateAsync(blockCount: 101);

			node.Behaviors.Add(bitcoinStore.CreateUntrustedP2pBehavior());
			node.VersionHandshake();

			using Key k = new();
			var address = k.PubKey.GetAddress(ScriptPubKeyType.Segwit, network);

			// Number of transactions to send.
			const int TransactionsCount = 3;

			EventsAwaiter<SmartTransaction> eventAwaiter = new(
				subscribe: h => mempoolService.TransactionReceived += h,
				unsubscribe: h => mempoolService.TransactionReceived -= h,
				count: TransactionsCount);

			Task<uint256>[] txHashesTasks = new Task<uint256>[TransactionsCount];

			// Add to the batch 10 RPC commands: Send 1 coin to the same address.
			for (int i = 0; i < TransactionsCount; i++)
			{
				Task<uint256> txidTask = rpc.SendToAddressAsync(address, Money.Coins(1));
				txHashesTasks[i] = txidTask;
			}

			uint256[] txHashes = await Task.WhenAll(txHashesTasks);

			// Wait until the mempool service receives all the sent transactions.
			IEnumerable<SmartTransaction> mempoolSmartTxs = await eventAwaiter.WaitAsync(TimeSpan.FromMinutes(4));

			// Check that all the received transaction hashes are in the set of sent transaction hashes.
			foreach (SmartTransaction tx in mempoolSmartTxs)
			{
				Assert.Contains(tx.GetHash(), txHashes);
			}
		}
		finally
		{
			node.Disconnect();
			await coreNode.TryStopAsync();
		}
	}

	[Fact]
	public async Task TrustedNotifierNotifiesTxAsync()
	{
		var coreNode = await TestNodeBuilder.CreateAsync(nameof(TrustedNotifierNotifiesTxAsync));
		try
		{
			var rpc = coreNode.RpcClient;
			var walletName = "wallet";
			await rpc.CreateWalletAsync(walletName);

			await rpc.GenerateAsync(101);
			var network = rpc.Network;

			var dir = Common.GetWorkDir(nameof(TrustedNotifierNotifiesTxAsync));

			using Key k = new();
			var addr = k.PubKey.GetAddress(ScriptPubKeyType.Segwit, network);
			var notifier = coreNode.MempoolService;

			var txNum = 10;
			EventsAwaiter<SmartTransaction> txEventAwaiter = new(
				h => notifier.TransactionReceived += h,
				h => notifier.TransactionReceived -= h,
				txNum);

			List<Task<uint256>> txTasks = new();
			var batch = rpc.PrepareBatch();
			for (int i = 0; i < txNum; i++)
			{
				txTasks.Add(batch.SendToAddressAsync(addr, Money.Coins(1)));
			}
			await batch.SendBatchAsync();

			var arrivedTxs = await txEventAwaiter.WaitAsync(TimeSpan.FromSeconds(21));

			var hashes = await Task.WhenAll(txTasks);
			foreach (var hash in arrivedTxs.Select(x => x.GetHash()))
			{
				Assert.Contains(hash, hashes);
			}
		}
		finally
		{
			await coreNode.TryStopAsync();
		}
	}

	[Fact]
	public async Task BlockNotifierTestsAsync()
	{
		var coreNode = await TestNodeBuilder.CreateAsync(nameof(BlockNotifierTestsAsync));
		using HostedServices services = new();
		services.Register<BlockNotifier>(() => new BlockNotifier(coreNode.RpcClient, coreNode.P2pNode,TimeSpan.FromSeconds(7) ), "Block Notifier");

		await services.StartAllAsync();
		try
		{
			var rpc = coreNode.RpcClient;
			var walletName = "wallet";
			await rpc.CreateWalletAsync(walletName);

			BlockNotifier notifier = services.Get<BlockNotifier>();

			// Make sure we get notification for one block.
			EventAwaiter<Block> blockEventAwaiter = new(h => notifier.OnBlock += h, h => notifier.OnBlock -= h);

			var hash = (await rpc.GenerateAsync(1)).First();
			var block = await blockEventAwaiter.WaitAsync(TimeSpan.FromSeconds(21));
			Assert.Equal(hash, block.GetHash());

			// Make sure we get notifications about 10 blocks created at the same time.
			var blockNum = 10;
			EventsAwaiter<Block> blockEventsAwaiter = new(h => notifier.OnBlock += h, h => notifier.OnBlock -= h, blockNum);

			var hashes = (await rpc.GenerateAsync(blockNum)).ToArray();

			var arrivedBlocks = (await blockEventsAwaiter.WaitAsync(TimeSpan.FromSeconds(21))).ToArray();

			for (int i = 0; i < hashes.Length; i++)
			{
				var expected = hashes[i];
				var actual = arrivedBlocks[i].GetHash();
				Assert.Equal(expected, actual);
			}

			// Make sure we get reorg notifications.
			var reorgNum = 3;
			var newBlockNum = reorgNum + 1;
			EventsAwaiter<uint256> reorgEventsAwaiter = new(h => notifier.OnReorg += h, h => notifier.OnReorg -= h, reorgNum);
			blockEventsAwaiter = new(
				h => notifier.OnBlock += h,
				h => notifier.OnBlock -= h,
				newBlockNum);

			var reorgedHashes = hashes.TakeLast(reorgNum).ToArray();
			await rpc.InvalidateBlockAsync(reorgedHashes[0]);
			var newHashes = (await rpc.GenerateAsync(newBlockNum)).ToArray();

			var reorgedHeaders = (await reorgEventsAwaiter.WaitAsync(TimeSpan.FromSeconds(21))).ToArray();
			var newBlocks = (await blockEventsAwaiter.WaitAsync(TimeSpan.FromSeconds(21))).ToArray();

			reorgedHashes = reorgedHashes.Reverse().ToArray();
			for (int i = 0; i < reorgedHashes.Length; i++)
			{
				var expected = reorgedHashes[i];
				var actual = reorgedHeaders[i];
				Assert.Equal(expected, actual);
			}

			for (int i = 0; i < newHashes.Length; i++)
			{
				var expected = newHashes[i];
				var actual = newBlocks[i].GetHash();
				Assert.Equal(expected, actual);
			}
		}
		finally
		{
			await services.StopAllAsync();
			await coreNode.TryStopAsync();
		}
	}
}
