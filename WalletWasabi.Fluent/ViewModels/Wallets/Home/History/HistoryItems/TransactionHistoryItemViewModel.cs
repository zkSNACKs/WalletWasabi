using System.Reactive.Linq;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Logging;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Home.History.HistoryItems;

public partial class TransactionHistoryItemViewModel : HistoryItemViewModelBase
{
	private TransactionHistoryItemViewModel(
		int orderIndex,
		SmartTransaction transaction,
		Money amount,
		WalletViewModel walletVm,
		Money balance)
		: base(orderIndex, transaction)
	{
		Labels = transaction.Labels;
		Date = transaction.FirstSeen.ToLocalTime();
		Balance = balance;
		WalletVm = walletVm;

		IsCancellation = transaction.IsCancellation;
		IsSpeedUp = transaction.IsSpeedup;
		IsCPFP = transaction.IsCPFP;
		IsCPFPd = transaction.IsCPFPd;

		SetAmount(amount, transaction.GetFee());

		DateString = Date.ToLocalTime().ToUserFacingString();

		ShowDetailsCommand = ReactiveCommand.Create(() => UiContext.Navigate().To().TransactionDetails(transaction, amount, walletVm));
		CanCancelTransaction = transaction.IsCancellable(KeyManager);
		CanSpeedUpTransaction = transaction.IsSpeedupable(KeyManager);
		SpeedUpTransactionCommand = ReactiveCommand.Create(() => OnSpeedUpTransaction(transaction), Observable.Return(CanSpeedUpTransaction));
		CancelTransactionCommand = ReactiveCommand.Create(() => OnCancelTransaction(transaction), Observable.Return(CanCancelTransaction));
	}

	public bool CanCancelTransaction { get; }
	public bool CanSpeedUpTransaction { get; }
	public bool TransactionOperationsVisible => CanCancelTransaction || CanSpeedUpTransaction;

	public WalletViewModel WalletVm { get; }
	public Wallet Wallet => WalletVm.Wallet;
	public KeyManager KeyManager => Wallet.KeyManager;

	private void OnSpeedUpTransaction(SmartTransaction transactionToSpeedUp)
	{
		try
		{
			// If the transaction has CPFPs, then we want to speed them up instead of us.
			// Although this does happen inside the SpeedUpTransaction method, but we want to give the tx that was actually sped up to SpeedUpTransactionDialog.
			if (transactionToSpeedUp.TryGetLargestCPFP(WalletVm.Wallet.KeyManager, out var largestCpfp))
			{
				transactionToSpeedUp = largestCpfp;
			}
			var boostingTransaction = Wallet.SpeedUpTransaction(transactionToSpeedUp);
			UiContext.Navigate().To().SpeedUpTransactionDialog(WalletVm.UiTriggers, WalletVm.Wallet, transactionToSpeedUp, boostingTransaction);
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);
			UiContext.Navigate().To().ShowErrorDialog(ex.ToUserFriendlyString(), "Speed Up failed", "Wasabi could not initiate the transaction speed up process.");
		}
	}

	private void OnCancelTransaction(SmartTransaction transactionToCancel)
	{
		try
		{
			var cancellingTransaction = Wallet.CancelTransaction(transactionToCancel);
			UiContext.Navigate().To().CancelTransactionDialog(WalletVm.UiTriggers, Wallet, transactionToCancel, cancellingTransaction);
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);
			UiContext.Navigate().To().ShowErrorDialog(ex.ToUserFriendlyString(), "Cancel failed", "Wasabi could not initiate the cancelling process.");
		}
	}
}
