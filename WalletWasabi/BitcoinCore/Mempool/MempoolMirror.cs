using NBitcoin;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Bases;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Logging;

namespace WalletWasabi.BitcoinCore.Mempool;

public class MempoolMirror : PeriodicRunner
{
	/// <param name="period">How often to mirror the mempool.</param>
	public MempoolMirror( IRPCClient rpc, P2pNode node, TimeSpan? period = null) : base(period ?? TimeSpan.FromSeconds(21))
	{
		Rpc = rpc;
		Node = node;
	}

	private IRPCClient Rpc { get; }
	private P2pNode Node { get; }
	private Mempool Mempool { get; set; } = new();
	private object MempoolLock { get; } = new();

	public override Task StartAsync(CancellationToken cancellationToken)
	{
		Node.MempoolService.TransactionReceived += MempoolService_TransactionReceived;
		return base.StartAsync(cancellationToken);
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var sw = Stopwatch.StartNew();
		int added = await MirrorMempoolAsync(stoppingToken).ConfigureAwait(false);
		Logger.LogInfo($"{added} transactions were copied from the full node to the in-memory mempool within {sw.Elapsed.TotalSeconds} seconds.");

		await base.ExecuteAsync(stoppingToken).ConfigureAwait(false);
	}

	protected override async Task ActionAsync(CancellationToken cancel)
	{
		await MirrorMempoolAsync(cancel).ConfigureAwait(false);
	}

	public override Task StopAsync(CancellationToken cancellationToken)
	{
		Node.MempoolService.TransactionReceived -= MempoolService_TransactionReceived;
		return base.StopAsync(cancellationToken);
	}

	private void MempoolService_TransactionReceived(object? sender, SmartTransaction stx)
	{
		AddTransactions(stx.Transaction);
	}

	internal int AddTransactions(params Transaction[] txs)
	{
		lock (MempoolLock)
		{
			return Mempool.AddMissingTransactions(txs);
		}
	}

	private async Task<int> MirrorMempoolAsync(CancellationToken cancel)
	{
		// Get all TXIDs in the up-to-date mempool.
		uint256[]? newTxids = await Rpc.GetRawMempoolAsync(cancel).ConfigureAwait(false);

		Mempool newMempool;

		lock (MempoolLock)
		{
			newMempool = Mempool.Clone();
		}

		ISet<uint256> oldTxids = newMempool.GetMempoolTxids();

		// Those TXIDs that are in the new mempool snapshot but not in the old one, are the ones
		// for which we want to download the corresponding transactions via RPC.
		IEnumerable<uint256> missingTxids = newTxids.Except(oldTxids);

		// Remove those transactions that are not present in the new mempool snapshot.
		foreach (uint256 txid in oldTxids.Except(newTxids).ToHashSet())
		{
			if (!newMempool.TryRemoveTransaction(txid))
			{
				Logger.LogError($"Failed to remove transaction '{txid}'.");
			}
		}

		IEnumerable<Transaction> missingTxs = await Rpc.GetRawTransactionsAsync(missingTxids, cancel).ConfigureAwait(false);
		int added = newMempool.AddMissingTransactions(missingTxs);

		lock (MempoolLock)
		{
			Mempool = newMempool;
		}

		return added;
	}

	public IReadOnlySet<Transaction> GetSpenderTransactions(IEnumerable<OutPoint> txOuts)
	{
		lock (MempoolLock)
		{
			return Mempool.GetSpenderTransactions(txOuts);
		}
	}

	public ISet<uint256> GetMempoolHashes()
	{
		lock (MempoolLock)
		{
			return Mempool.GetMempoolTxids();
		}
	}
}
