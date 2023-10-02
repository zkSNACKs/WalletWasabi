using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using DynamicData;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Blockchain.TransactionProcessing;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.ViewModels.Wallets.Send;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.Models.Wallets;

[AutoInterface]
public partial class WalletTransactionsModel : ReactiveObject
{
	private readonly Wallet _wallet;

	public WalletTransactionsModel(Wallet wallet)
	{
		_wallet = wallet;

		TransactionProcessed =
			Observable.FromEventPattern<ProcessedResult?>(wallet, nameof(wallet.WalletRelevantTransactionProcessed)).ToSignal()
					  .Merge(Observable.FromEventPattern(wallet, nameof(wallet.NewFiltersProcessed)).ToSignal())
					  .Sample(TimeSpan.FromSeconds(1))
					  .ObserveOn(RxApp.MainThreadScheduler)
					  .StartWith(Unit.Default);

		var transactionChanges =
			Observable.Defer(() => BuildSummary().ToObservable())
					  .Concat(TransactionProcessed.SelectMany(_ => BuildSummary()))
					  .ToObservableChangeSet(x => x.GetHash());

		transactionChanges.Bind(out var collection).Subscribe();
		Transactions = collection;
	}

	public ReadOnlyObservableCollection<TransactionSummary> Transactions { get; }

	public IObservable<Unit> TransactionProcessed { get; }

	public bool AreEnoughToCreateTransaction(TransactionInfo transactionInfo, IEnumerable<SmartCoin> coins)
	{
		return TransactionHelpers.TryBuildTransactionWithoutPrevTx(_wallet.KeyManager, transactionInfo, _wallet.Coins, coins, _wallet.Kitchen.SaltSoup(), out _);
	}

	public TransactionSummary? GetById(uint256 transactionId)
	{
		return Transactions.FirstOrDefault(x => x.GetHash() == transactionId);
	}

	public TimeSpan? TryEstimateConfirmationTime(TransactionSummary transactionSummary)
	{
		return
			TransactionFeeHelper.TryEstimateConfirmationTime(_wallet, transactionSummary.Transaction, out var estimate)
			? estimate
			: null;
	}

	private IEnumerable<TransactionSummary> BuildSummary()
	{
		return TransactionHistoryBuilder.BuildHistorySummary(_wallet);
	}
}
