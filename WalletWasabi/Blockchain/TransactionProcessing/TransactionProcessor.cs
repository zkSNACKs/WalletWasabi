using NBitcoin;
using System.Collections.Generic;
using System.Linq;
using WalletWasabi.Blockchain.Analysis;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.Mempool;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Extensions;
using WalletWasabi.Models;

namespace WalletWasabi.Blockchain.TransactionProcessing;

public class TransactionProcessor
{
	public TransactionProcessor(
		AllTransactionStore transactionStore,
		MempoolService? mempoolService,
		KeyManager keyManager,
		Money dustThreshold)
	{
		TransactionStore = transactionStore;
		MempoolService = mempoolService;
		KeyManager = keyManager;
		DustThreshold = dustThreshold;
		Coins = new();
		BlockchainAnalyzer = new();
	}

	public event EventHandler<ProcessedResult>? WalletRelevantTransactionProcessed;

	/// <remarks>Intentionally, <c>static</c> to avoid modifying smart transactions from multiple threads.</remarks>
	public static object Lock { get; } = new();
	public AllTransactionStore TransactionStore { get; }
	private HashSet<uint256> Aware { get; } = new();

	public KeyManager KeyManager { get; }

	public CoinsRegistry Coins { get; }
	private BlockchainAnalyzer BlockchainAnalyzer { get; }
	public Money DustThreshold { get; }
	private MempoolService? MempoolService { get; }

	public IEnumerable<ProcessedResult> Process(IEnumerable<SmartTransaction> txs)
	{
		var rets = new List<ProcessedResult>();

		lock (Lock)
		{
			foreach (var tx in txs)
			{
				rets.Add(ProcessNoLock(tx));
			}
		}

		foreach (var ret in rets.Where(x => x.IsNews))
		{
			WalletRelevantTransactionProcessed?.Invoke(this, ret);
		}

		return rets;
	}

	public IEnumerable<ProcessedResult> Process(params SmartTransaction[] txs)
		=> Process(txs as IEnumerable<SmartTransaction>);

	/// <summary>
	/// Was the transaction already processed by the transaction processor?
	/// </summary>
	public bool IsAware(uint256 tx)
	{
		lock (Lock)
		{
			return Aware.Contains(tx);
		}
	}

	public ProcessedResult Process(SmartTransaction tx)
	{
		ProcessedResult ret;
		lock (Lock)
		{
			Aware.Add(tx.GetHash());
			ret = ProcessNoLock(tx);
		}
		if (ret.IsNews)
		{
			WalletRelevantTransactionProcessed?.Invoke(this, ret);
		}
		return ret;
	}

	private ProcessedResult ProcessNoLock(SmartTransaction tx)
	{
		var result = new ProcessedResult(tx);

		// We do not care about non-witness transactions for other than mempool cleanup.
		if (!tx.Transaction.SegWitInvolved())
		{
			return result;
		}

		uint256 txId = tx.GetHash();

		// If we already have the transaction, then let's work on that.
		if (MempoolService?.TryGetFromBroadcastStore(txId, out var foundEntry) is true)
		{
			// If we already have the transaction in the broadcast store, then let's work on that.
			foundEntry.Transaction.TryUpdate(tx);
			tx = foundEntry.Transaction;
			result = new ProcessedResult(tx);
		}

		if (TransactionStore.TryGetTransaction(txId, out var foundTx))
		{
			foundTx.TryUpdate(tx);
			tx = foundTx;
			result = new ProcessedResult(tx);
		}

		if (!tx.Transaction.IsCoinBase && !Coins.IsKnown(txId)) // Transactions we already have and processed would be "double spends" but they shouldn't.
		{
			var doubleSpentSpenders = new List<SmartCoin>();
			var doubleSpentCoins = new List<SmartCoin>();
			foreach (var txIn in tx.Transaction.Inputs)
			{
				if (Coins.TryGetCoinsByInputPrevOut(txIn.PrevOut, out var coins))
				{
					doubleSpentSpenders.AddRange(coins);
				}

				if (Coins.TryGetByOutPoint(txIn.PrevOut, out var coin) && coin.IsSpent())
				{
					doubleSpentCoins.Add(coin);
				}
			}

			var doubleSpentTransactions = doubleSpentCoins.Select(x => x.SpenderTransaction!).Concat(doubleSpentSpenders.Select(x => x.Transaction)).ToHashSet();

			if (doubleSpentTransactions.Count > 0)
			{
				tx.SetReplacement();
			}

			if (tx.Height == Height.Mempool)
			{
				// if the received transaction is spending at least one input already
				// spent by a previous unconfirmed transaction then it is not considered a double
				// spending transaction but a replacement transaction.
				var isReplacementTx = doubleSpentSpenders.Any();
				if (isReplacementTx)
				{
					// Undo the replaced transaction by removing the coins it created (if other coin
					// spends it, remove that too and so on) and restoring those that it replaced.
					// After undoing the replaced transaction it will process the replacement transaction.
					var replacedTxId = doubleSpentSpenders.First().TransactionId;
					var (replaced, restored) = Coins.Undo(replacedTxId);

					result.ReplacedCoins.AddRange(replaced);
					result.RestoredCoins.AddRange(restored);
				}
			}
			else // new confirmation always enjoys priority
			{
				foreach (var doubleSpentTx in doubleSpentTransactions)
				{
					var unconfirmedDoubleSpentTxId = doubleSpentTx.GetHash();
					if (TransactionStore.MempoolStore.TryGetTransaction(unconfirmedDoubleSpentTxId, out var replacedTx) && replacedTx.IsReplacement)
					{
						var (replaced, restored) = Coins.Undo(unconfirmedDoubleSpentTxId);

						result.ReplacedCoins.AddRange(replaced);
						result.RestoredCoins.AddRange(restored);
					}
					else
					{
						// remove double spent spenders recursively (if other coin spends it, remove that too and so on), will add later if they came to our keys
						foreach (var doubleSpentTxid in doubleSpentSpenders.Select(x => x.TransactionId).Distinct())
						{
							Coins.Undo(doubleSpentTxid);
						}

						result.SuccessfullyDoubleSpentCoins.AddRange(doubleSpentSpenders);
					}
				}
			}

			// Recursively double spent transactions could be here.
			foreach (var doubleSpentTx in result.ReplacedCoins.Select(coin => coin.Transaction))
			{
				doubleSpentTransactions.Add(doubleSpentTx);
			}

			foreach (var replacedTransactionId in doubleSpentTransactions.Select(x => x.GetHash()))
			{
				TransactionStore.MempoolStore.TryRemove(replacedTransactionId, out _);
			}
		}

		IReadOnlyList<SmartCoin> myInputs = Coins.GetMyInputs(tx);

		for (var i = 0U; i < tx.Transaction.Outputs.Count; i++)
		{
			// If transaction received to any of the wallet keys:
			var output = tx.Transaction.Outputs[i];
			if (KeyManager.TryGetKeyForScriptPubKey(output.ScriptPubKey, out HdPubKey? foundKey))
			{
				if (!foundKey.IsInternal)
				{
					tx.Labels = LabelsArray.Merge(tx.Labels, foundKey.Labels);
				}

				var couldBeDustAttack = CanBeConsideredDustAttack(output, foundKey, myInputs.Any());
				KeyManager.SetKeyState(KeyState.Used, foundKey);

				if (tx.Confirmed && foundKey.FirstReceivingHeight is null)
				{
					foundKey.FirstReceivingHeight = tx.Height;
					KeyManager.InvalidateFirstReceivingHeight = true;
				}

				if (couldBeDustAttack)
				{
					result.ReceivedDusts.Add(output);
					continue;
				}

				SmartCoin newCoin = new(tx, i, foundKey);

				result.ReceivedCoins.Add(newCoin);

				// If we did not have it.
				if (Coins.TryAdd(newCoin))
				{
					result.NewlyReceivedCoins.Add(newCoin);
				}
				else // If we had this coin already.
				{
					if (newCoin.Height != Height.Mempool) // Update the height of this old coin we already had.
					{
						if (Coins.AsAllCoinsView().TryGetByOutPoint(new OutPoint(txId, i), out var oldCoin)) // Just to be sure, it is a concurrent collection.
						{
							result.NewlyConfirmedReceivedCoins.Add(newCoin);
							oldCoin.Height = newCoin.Height;
						}
					}
				}
			}
		}

		// If spends any of our coin
		foreach (var coin in myInputs)
		{
			var alreadyKnown = coin.SpenderTransaction == tx;
			result.SpentCoins.Add(coin);
			Coins.Spend(coin, tx);
			MempoolService?.TrySpend(coin, tx);
			result.RestoredCoins.Remove(coin);

			if (!alreadyKnown)
			{
				result.NewlySpentCoins.Add(coin);
			}

			if (tx.Confirmed)
			{
				result.NewlyConfirmedSpentCoins.Add(coin);
			}
		}

		if (tx.Confirmed)
		{
			// Update for TurboSync - save spending height for internal keys if there is a spender tx and no more coins left on the key.
			SaveInternalKeysLatestSpendingHeight(tx.Height, myInputs.Select(x => x.HdPubKey).Where(x => x.IsInternal).Distinct());
		}

		if (tx.WalletInputs.Count != 0 || tx.WalletOutputs.Count != 0)
		{
			TransactionStore.AddOrUpdate(tx);
		}

		BlockchainAnalyzer.Analyze(result.Transaction);

		if (result.SuccessfullyDoubleSpentCoins.Count > 0 || result.ReplacedCoins.Count > 0)
		{
			KeyManager.InvalidateFirstReceivingHeight = true;
		}
		return result;
	}

	private bool CanBeConsideredDustAttack(TxOut output, HdPubKey hdPubKey, bool weAreAmongTheSender) =>
		output.Value <= DustThreshold // the value received is under the dust threshold
		&& !weAreAmongTheSender // we are not one of the senders (it is not a self-spending tx or coinjoin)
		&& Coins.IsUsed(hdPubKey); // the destination address has already been used (address reuse)

	private void SaveInternalKeysLatestSpendingHeight(Height txHeight, IEnumerable<HdPubKey> internalKeys)
	{
		foreach (var spenderKey in internalKeys)
		{
			if (Coins.HasUnspentCoin(spenderKey))
			{
				// The key still has unspent coins.
				continue;
			}

			// All the coins on this key were spent. Mark it as retired and store the block height.
			if (spenderKey.LatestSpendingHeight is null)
			{
				spenderKey.LatestSpendingHeight = txHeight;
			}
			else if ((Height)spenderKey.LatestSpendingHeight < txHeight)
			{
				// Key spent its coins earlier in history but was reused and spent again.
				spenderKey.LatestSpendingHeight = txHeight;
			}
		}
	}

	public void UndoBlock(Height blockHeight)
	{
		Coins.SwitchToUnconfirmFromBlock(blockHeight);
	}
}
