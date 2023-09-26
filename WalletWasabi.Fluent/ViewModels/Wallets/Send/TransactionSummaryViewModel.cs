using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Send;

public partial class TransactionSummaryViewModel : ViewModelBase
{
	private readonly Wallet _wallet;
	private BuildTransactionResult? _transaction;
	[AutoNotify] private bool _transactionHasChange;
	[AutoNotify] private TimeSpan? _confirmationTime;
	[AutoNotify] private string _feeText = "";
	[AutoNotify] private bool _isCustomFeeUsed;
	[AutoNotify] private bool _isOtherPocketSelectionPossible;
	[AutoNotify] private LabelsArray _labels = LabelsArray.Empty;
	[AutoNotify] private LabelsArray _recipient = LabelsArray.Empty;
	[AutoNotify] private BtcAmount? _fee;
	[AutoNotify] private BtcAmount? _amount;

	public TransactionSummaryViewModel(TransactionPreviewViewModel parent, Wallet wallet, TransactionInfo info, bool isPreview = false)
	{
		Parent = parent;
		_wallet = wallet;
		IsPreview = isPreview;
		AddressText = info.Destination.ToString();
		PayJoinUrl = info.PayJoinClient?.PaymentUrl.AbsoluteUri;
		IsPayJoin = PayJoinUrl is not null;
	}

	public TransactionPreviewViewModel Parent { get; }

	public bool IsPreview { get; }

	public string AddressText { get; }

	public string? PayJoinUrl { get; }

	public bool IsPayJoin { get; }

	public void UpdateTransaction(BuildTransactionResult transactionResult, TransactionInfo info)
	{
		_transaction = transactionResult;

		TransactionFeeHelper.TryEstimateConfirmationTime(_wallet, info.FeeRate, out var estimate);
		ConfirmationTime = estimate;

		var destinationAmount = _transaction.CalculateDestinationAmount(info.Destination);

		Amount = UiContext.CreateAmount(destinationAmount);
		Fee = UiContext.CreateAmount(_transaction.Fee);

		Recipient = info.Recipient;
		IsCustomFeeUsed = info.IsCustomFeeUsed;
		IsOtherPocketSelectionPossible = info.IsOtherPocketSelectionPossible;
	}
}
