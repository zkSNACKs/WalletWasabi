using System.Reactive.Disposables;
using NBitcoin;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Home.History.Details;

[NavigationMetaData(Title = "Coinjoin Details", NavigationTarget = NavigationTarget.DialogScreen)]
public partial class CoinJoinDetailsViewModel : RoutableViewModel
{
	private readonly IWalletModel _wallet;
	private readonly TransactionModel _transaction;

	[AutoNotify] private string _date = "";
	[AutoNotify] private Amount? _coinJoinFeeAmount;
	[AutoNotify] private uint256? _transactionId;
	[AutoNotify] private bool _isConfirmed;
	[AutoNotify] private int _confirmations;

	public CoinJoinDetailsViewModel(UiContext uiContext, IWalletModel wallet, TransactionModel transaction)
	{
		_wallet = wallet;
		_transaction = transaction;

		UiContext = uiContext;

		SetupCancel(enableCancel: false, enableCancelOnEscape: true, enableCancelOnPressed: true);
		NextCommand = CancelCommand;

		Money feeInSats = Services.HostedServices.Get<TransactionFeeProvider>().GetFee(_transaction.Id);
		var network = Services.WalletManager.Network;
		var vSize = _transaction.TransactionSummary.Transaction.Transaction.GetVirtualSize();
		TransactionFeeHelper.TryEstimateConfirmationTime(Services.HostedServices.Get<HybridFeeProvider>(), network, feeInSats, vSize, out var estimate);

		ConfirmationTime = estimate;

		IsConfirmationTimeVisible = ConfirmationTime.HasValue && ConfirmationTime != TimeSpan.Zero;
	}

	public TimeSpan? ConfirmationTime { get; set; }

	public bool IsConfirmationTimeVisible { get; set; }

	protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
	{
		base.OnNavigatedTo(isInHistory, disposables);

		_wallet.Transactions.TransactionProcessed
							.Subscribe(_ => Update())
							.DisposeWith(disposables);
	}

	private void Update()
	{
		if (_wallet.Transactions.TryGetById(_transaction.Id, _transaction.IsChild, out var transaction))
		{
			Date = transaction.DateString;
			CoinJoinFeeAmount = UiContext.AmountProvider.Create(transaction.OutgoingAmount);
			Confirmations = transaction.Confirmations;
			IsConfirmed = Confirmations > 0;
			TransactionId = transaction.Id;
		}
	}
}
