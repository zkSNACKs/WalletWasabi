using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.ViewModels.Navigation;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Home.History.Details;

[NavigationMetaData(Title = "Transaction Details")]
public partial class TransactionDetailsViewModel : RoutableViewModel
{
	private readonly IWalletModel _wallet;

	[AutoNotify] private bool _isConfirmed;
	[AutoNotify] private string? _amountText = "";
	[AutoNotify] private string? _blockHash;
	[AutoNotify] private int _blockHeight;
	[AutoNotify] private int _confirmations;
	[AutoNotify] private TimeSpan? _confirmationTime;
	[AutoNotify] private string? _dateString;
	[AutoNotify] private bool _isConfirmationTimeVisible;
	[AutoNotify] private bool _isLabelsVisible;
	[AutoNotify] private LabelsArray? _labels;
	[AutoNotify] private Amount? _amount;

	public TransactionDetailsViewModel(UiContext uiContext, IWalletModel wallet, TransactionModel model)
	{
		UiContext = uiContext;
		_wallet = wallet;

		NextCommand = ReactiveCommand.Create(OnNext);
		Fee = wallet.AmountProvider.Create(model.Fee);
		FeeRate = model.FeeRate;
		IsFeeVisible = model.Fee != null;
		TransactionId = model.Id;
		DestinationAddresses = wallet.Transactions.GetDestinationAddresses(model.Id).ToArray();
		SingleAddress = DestinationAddresses.Count == 1 ? DestinationAddresses.First() : null;

		SetupCancel(enableCancel: false, enableCancelOnEscape: true, enableCancelOnPressed: true);

		UpdateValues(model);
	}

	public BitcoinAddress? SingleAddress { get; set; }

	public FeeRate? FeeRate { get; set; }

	public uint256 TransactionId { get; }

	public Amount? Fee { get; }

	public ICollection<BitcoinAddress> DestinationAddresses { get; }

	public bool IsFeeVisible { get; }

	private void UpdateValues(TransactionModel model)
	{
		DateString = model.DateToolTipString;
		Labels = model.Labels;
		BlockHeight = model.BlockHeight;
		Confirmations = model.Confirmations;

		var confirmationTime = _wallet.Transactions.TryEstimateConfirmationTime(model);
		if (confirmationTime is { })
		{
			ConfirmationTime = confirmationTime;
		}

		IsConfirmed = Confirmations > 0;

		if (model.Amount < Money.Zero)
		{
			Amount = _wallet.AmountProvider.Create(-model.Amount - (model.Fee ?? Money.Zero));
			AmountText = "Amount sent";
		}
		else
		{
			Amount = _wallet.AmountProvider.Create(model.Amount);
			AmountText = "Amount received";
		}

		BlockHash = model.BlockHash?.ToString();

		IsConfirmationTimeVisible = ConfirmationTime.HasValue && ConfirmationTime != TimeSpan.Zero;
		IsLabelsVisible = Labels.HasValue && Labels.Value.Any();
	}

	private void OnNext()
	{
		Navigate().Clear();
	}

	protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
	{
		base.OnNavigatedTo(isInHistory, disposables);

		_wallet.Transactions.Cache
			                .Connect()
							.Do(_ => UpdateCurrentTransaction())
							.Subscribe()
							.DisposeWith(disposables);
	}

	private void UpdateCurrentTransaction()
	{
		if (_wallet.Transactions.TryGetById(TransactionId, false, out var transaction))
		{
			UpdateValues(transaction);
		}
	}
}
