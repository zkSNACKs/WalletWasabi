using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using NBitcoin;
using NBitcoin.Policy;
using ReactiveUI;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Exceptions;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Models;
using WalletWasabi.Fluent.Models.Transactions;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.ViewModels.Dialogs.Base;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Logging;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Send;

[NavigationMetaData(Title = "Transaction Preview")]
public partial class TransactionPreviewViewModel : RoutableViewModel
{
	private readonly Stack<(BuildTransactionResult, TransactionInfo)> _undoHistory;
	private readonly Wallet _wallet;
	private readonly WalletViewModel _walletViewModel;
	private TransactionInfo _info;
	private TransactionInfo _currentTransactionInfo;
	private CancellationTokenSource _cancellationTokenSource;
	[AutoNotify] private BuildTransactionResult? _transaction;
	[AutoNotify] private string _nextButtonText;
	[AutoNotify] private TransactionSummaryViewModel? _displayedTransactionSummary;
	[AutoNotify] private bool _canUndo;
	[AutoNotify] private bool _isCoinControlVisible;

	public TransactionPreviewViewModel(UiContext uiContext, WalletViewModel walletViewModel, TransactionInfo info)
	{
		_undoHistory = new();
		_wallet = walletViewModel.Wallet;
		_walletViewModel = walletViewModel;
		_info = info;
		_currentTransactionInfo = info.Clone();
		_cancellationTokenSource = new CancellationTokenSource();

		PrivacySuggestions = new PrivacySuggestionsFlyoutViewModel(_wallet);
		CurrentTransactionSummary = new TransactionSummaryViewModel(uiContext, this, _wallet, _info);
		PreviewTransactionSummary = new TransactionSummaryViewModel(uiContext, this, _wallet, _info, true);

		TransactionSummaries = new List<TransactionSummaryViewModel>
		{
			CurrentTransactionSummary,
			PreviewTransactionSummary
		};

		DisplayedTransactionSummary = CurrentTransactionSummary;
		
		SetupCancel(enableCancel: true, enableCancelOnEscape: true, enableCancelOnPressed: false);
		EnableBack = true;

		if (PreferPsbtWorkflow)
		{
			SkipCommand = ReactiveCommand.CreateFromTask(OnConfirmAsync);
			NextCommand = ReactiveCommand.CreateFromTask(OnExportPsbtAsync);

			_nextButtonText = "Save PSBT file";
		}
		else
		{
			NextCommand = ReactiveCommand.CreateFromTask(OnConfirmAsync);

			_nextButtonText = "Confirm";
		}

		AdjustFeeCommand = ReactiveCommand.CreateFromTask(OnAdjustFeeAsync);

		UndoCommand = ReactiveCommand.Create(
				() =>
				{
					if (_undoHistory.TryPop(out var previous))
					{
						_info = previous.Item2;
						UpdateTransaction(CurrentTransactionSummary, previous.Item1, false);
						CanUndo = _undoHistory.Count != 0;
					}
				});

		ChangeCoinsCommand = ReactiveCommand.CreateFromTask(OnChangeCoinsAsync);
	}

	public TransactionSummaryViewModel CurrentTransactionSummary { get; }

	public TransactionSummaryViewModel PreviewTransactionSummary { get; }

	public List<TransactionSummaryViewModel> TransactionSummaries { get; }

	public PrivacySuggestionsFlyoutViewModel PrivacySuggestions { get; }

	public bool PreferPsbtWorkflow => _wallet.KeyManager.PreferPsbtWorkflow;

	public ICommand AdjustFeeCommand { get; }

	public ICommand ChangeCoinsCommand { get; }

	public ICommand UndoCommand { get; }

	private async Task OnExportPsbtAsync()
	{
		if (Transaction is { })
		{
			bool saved = false;
			try
			{
				saved = await TransactionHelpers.ExportTransactionToBinaryAsync(Transaction);
			}
			catch (Exception ex)
			{
				Logger.LogError(ex);
				await ShowErrorAsync("Transaction Export", ex.ToUserFriendlyString(), "Wasabi was unable to export the PSBT.");
			}

			if (saved)
			{
				Navigate().To().Success();
			}
		}
	}

	private void UpdateTransaction(TransactionSummaryViewModel summary, BuildTransactionResult transaction, bool addToUndoHistory = true)
	{
		if (!summary.IsPreview)
		{
			if (addToUndoHistory)
			{
				AddToUndoHistory();
			}

			Transaction = transaction;
			_currentTransactionInfo = _info.Clone();
		}

		summary.UpdateTransaction(transaction, _info);

		DisplayedTransactionSummary = summary;
	}

	private async Task OnAdjustFeeAsync()
	{
		DialogViewModelBase<FeeRate> feeDialog = _info.IsCustomFeeUsed
			? new CustomFeeRateDialogViewModel(_info)
			: new SendFeeViewModel(UiContext, _wallet, _info, false);

		var feeDialogResult = await NavigateDialogAsync(feeDialog, feeDialog.DefaultTarget);

		if (feeDialogResult.Kind == DialogResultKind.Normal &&
			feeDialogResult.Result is { } feeRate &&
			feeRate != _info.FeeRate) // Prevent rebuild if the selected fee did not change.
		{
			_info.FeeRate = feeRate;
			await BuildAndUpdateAsync();
		}
	}

	private async Task BuildAndUpdateAsync()
	{
		var newTransaction = await BuildTransactionAsync();

		if (newTransaction is { })
		{
			UpdateTransaction(CurrentTransactionSummary, newTransaction);
		}
	}

	private async Task OnChangeCoinsAsync()
	{
		var currentCoins = _walletViewModel.WalletModel.Coins.GetSpentCoins(Transaction);

		var selectedCoins = await Navigate().To().SelectCoinsDialog(_walletViewModel.WalletModel, currentCoins, _info).GetResultAsync();

		if (selectedCoins is { })
		{
			if (currentCoins.GetSmartCoins().ToHashSet().SetEquals(selectedCoins))
			{
				return;
			}

			_info.Coins = selectedCoins;
			await BuildAndUpdateAsync();
		}
	}

	private async Task<bool> InitialiseTransactionAsync()
	{
		if (_info.FeeRate == FeeRate.Zero)
		{
			var feeDialogResult = await NavigateDialogAsync(new SendFeeViewModel(UiContext, _wallet, _info, true));
			if (feeDialogResult.Kind == DialogResultKind.Normal && feeDialogResult.Result is { } newFeeRate)
			{
				_info.FeeRate = newFeeRate;
			}
			else
			{
				return false;
			}
		}

		if (!_info.Coins.Any())
		{
			var privacyControlDialogResult =
				await NavigateDialogAsync(new PrivacyControlViewModel(_wallet, _info, Transaction?.SpentCoins, isSilent: true));
			if (privacyControlDialogResult.Kind == DialogResultKind.Normal &&
				privacyControlDialogResult.Result is { } coins)
			{
				_info.Coins = coins;
			}
			else if (privacyControlDialogResult.Kind != DialogResultKind.Normal)
			{
				return false;
			}
		}

		return true;
	}

	private async Task<BuildTransactionResult?> BuildTransactionAsync()
	{
		if (!await InitialiseTransactionAsync())
		{
			return null;
		}

		try
		{
			IsBusy = true;

			return await Task.Run(() => TransactionHelpers.BuildTransaction(_wallet, _info, tryToSign: false));
		}
		catch (Exception ex) when (ex is NotEnoughFundsException or TransactionFeeOverpaymentException || (ex is InvalidTxException itx && itx.Errors.OfType<FeeTooHighPolicyError>().Any()))
		{
			if (await TransactionFeeHelper.TrySetMaxFeeRateAsync(_wallet, _info))
			{
				return await BuildTransactionAsync();
			}

			await ShowErrorAsync(
				"Transaction Building",
				"The transaction cannot be sent because its fee is more than the payment amount.",
				"Wasabi was unable to create your transaction.");

			return null;
		}
		catch (InsufficientBalanceException)
		{
			var canSelectMoreCoins = _wallet.Coins.Any(coin => !_info.Coins.Contains(coin));

			if (canSelectMoreCoins)
			{
				var selectPocketsDialog =
					await NavigateDialogAsync(new PrivacyControlViewModel(_wallet, _info, usedCoins: Transaction?.SpentCoins, isSilent: true));

				if (selectPocketsDialog.Result is { } newCoins)
				{
					_info.Coins = newCoins;
					return await BuildTransactionAsync();
				}
			}
			else if (await TransactionFeeHelper.TrySetMaxFeeRateAsync(_wallet, _info))
			{
				return await BuildTransactionAsync();
			}

			await ShowErrorAsync(
				"Transaction Building",
				"There are not enough funds to cover the transaction fee.",
				"Wasabi was unable to create your transaction.");

			return null;
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);

			await ShowErrorAsync(
				"Transaction Building",
				ex.ToUserFriendlyString(),
				"Wasabi was unable to create your transaction.");

			return null;
		}
		finally
		{
			IsBusy = false;
		}
	}

	private async Task InitialiseViewModelAsync()
	{
		if (await BuildTransactionAsync() is { } initialTransaction)
		{
			UpdateTransaction(CurrentTransactionSummary, initialTransaction);
		}
		else
		{
			Navigate().Back();
		}
	}

	protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
	{
		base.OnNavigatedTo(isInHistory, disposables);

		PrivacySuggestions.WhenAnyValue(x => x.PreviewSuggestion)
			.DoAsync(
				async x =>
				{
					if (x?.Transaction is { } transaction)
					{
						UpdateTransaction(PreviewTransactionSummary, transaction);
						await PrivacySuggestions.UpdatePreviewWarningsAsync(_info, transaction, _cancellationTokenSource.Token);
					}
					else
					{
						DisplayedTransactionSummary = CurrentTransactionSummary;
						PrivacySuggestions.ClearPreviewWarnings();
					}
				})
			.Subscribe()
			.DisposeWith(disposables);

		PrivacySuggestions.WhenAnyValue(x => x.SelectedSuggestion)
			.SubscribeAsync(
				async suggestion =>
				{
					PrivacySuggestions.SelectedSuggestion = null;

					if (suggestion is { })
					{
						await ApplyPrivacySuggestionAsync(suggestion);
					}
				})
			.DisposeWith(disposables);

		this.WhenAnyValue(x => x.Transaction)
			.WhereNotNull()
			.Throttle(TimeSpan.FromMilliseconds(100))
			.ObserveOn(RxApp.MainThreadScheduler)
			.Do(
				_ =>
				{
					_cancellationTokenSource.Cancel();
					_cancellationTokenSource = new();
				})
			.DoAsync(
				async transaction =>
				{
					await CheckChangePocketAvailableAsync(transaction);
					await PrivacySuggestions.BuildPrivacySuggestionsAsync(_info, transaction, _cancellationTokenSource.Token);
				})
			.Subscribe()
			.DisposeWith(disposables);

		if (!isInHistory)
		{
			RxApp.MainThreadScheduler.Schedule(async () => await InitialiseViewModelAsync());
		}
	}

	protected override void OnNavigatedFrom(bool isInHistory)
	{
		if (!isInHistory)
		{
			_cancellationTokenSource.Cancel();
			_cancellationTokenSource.Dispose();
		}

		base.OnNavigatedFrom(isInHistory);

		DisplayedTransactionSummary = null;
	}

	private async Task OnConfirmAsync()
	{
		try
		{
			var transaction = await Task.Run(() => TransactionHelpers.BuildTransaction(_wallet, _info));
			var transactionAuthorizationInfo = new TransactionAuthorizationInfo(transaction);
			var authResult = await AuthorizeAsync(transactionAuthorizationInfo);
			if (authResult)
			{
				IsBusy = true;

				var finalTransaction =
					await GetFinalTransactionAsync(transactionAuthorizationInfo.Transaction, _info);
				await SendTransactionAsync(finalTransaction);
				_wallet.UpdateUsedHdPubKeysLabels(transaction.HdPubKeysWithNewLabels);
				_cancellationTokenSource.Cancel();
				Navigate().To().SendSuccess(finalTransaction);
			}
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);
			await ShowErrorAsync(
				"Transaction",
				ex.ToUserFriendlyString(),
				"Wasabi was unable to send your transaction.");
		}
		finally
		{
			IsBusy = false;
		}
	}

	private async Task<bool> AuthorizeAsync(TransactionAuthorizationInfo transactionAuthorizationInfo)
	{
		if (!_wallet.KeyManager.IsHardwareWallet &&
			string.IsNullOrEmpty(_wallet.Kitchen.SaltSoup())) // Do not show authentication dialog when password is empty
		{
			return true;
		}

		var authDialog = AuthorizationHelpers.GetAuthorizationDialog(_walletViewModel.WalletModel, transactionAuthorizationInfo);
		var authDialogResult = await NavigateDialogAsync(authDialog, authDialog.DefaultTarget, NavigationMode.Clear);

		return authDialogResult.Result;
	}

	private async Task SendTransactionAsync(SmartTransaction transaction)
	{
		await Services.TransactionBroadcaster.SendTransactionAsync(transaction);
	}

	private async Task<SmartTransaction> GetFinalTransactionAsync(SmartTransaction transaction, TransactionInfo transactionInfo)
	{
		if (transactionInfo.PayJoinClient is { })
		{
			try
			{
				var payJoinTransaction = await Task.Run(() =>
					TransactionHelpers.BuildTransaction(_wallet, transactionInfo, isPayJoin: true));
				return payJoinTransaction.Transaction;
			}
			catch (Exception ex)
			{
				Logger.LogError(ex);
			}
		}

		return transaction;
	}

	private void AddToUndoHistory()
	{
		if (Transaction is { })
		{
			_undoHistory.Push((Transaction, _currentTransactionInfo));
			CanUndo = true;
		}
	}

	private async Task CheckChangePocketAvailableAsync(BuildTransactionResult transaction)
	{
		if (!_info.IsSelectedCoinModificationEnabled)
		{
			_info.IsOtherPocketSelectionPossible = false;
			return;
		}

		var cjManager = Services.HostedServices.Get<CoinJoinManager>();

		var usedCoins = transaction.SpentCoins;
		var pockets = _wallet.GetPockets().ToArray();
		var labelSelection = new LabelSelectionViewModel(_wallet.KeyManager, _wallet.Kitchen.SaltSoup(), _info, isSilent: true);
		await labelSelection.ResetAsync(pockets, coinsToExclude: cjManager.CoinsInCriticalPhase[_wallet.WalletId].ToList());

		_info.IsOtherPocketSelectionPossible = labelSelection.IsOtherSelectionPossible(usedCoins, _info.Recipient);
	}

	private async Task ApplyPrivacySuggestionAsync(PrivacySuggestion suggestion)
	{
		switch (suggestion)
		{
			case LabelManagementSuggestion:
				{
					var selectPocketsDialog =
						await NavigateDialogAsync(new PrivacyControlViewModel(_wallet, _info, Transaction?.SpentCoins, false));

					if (selectPocketsDialog.Kind == DialogResultKind.Normal && selectPocketsDialog.Result is { })
					{
						_info.Coins = selectPocketsDialog.Result;
						await BuildAndUpdateAsync();
					}

					break;
				}

			case ChangeAvoidanceSuggestion { Transaction: { } txn }:
				_info.ChangelessCoins = txn.SpentCoins;
				break;

			case FullPrivacySuggestion fullPrivacySuggestion:
				{
					if (fullPrivacySuggestion.IsChangeless)
					{
						_info.ChangelessCoins = fullPrivacySuggestion.Coins;
					}
					else
					{
						_info.Coins = fullPrivacySuggestion.Coins;
					}

					break;
				}

			case BetterPrivacySuggestion betterPrivacySuggestion:
				{
					if (betterPrivacySuggestion.IsChangeless)
					{
						_info.ChangelessCoins = betterPrivacySuggestion.Coins;
					}
					else
					{
						_info.Coins = betterPrivacySuggestion.Coins;
					}

					break;
				}
		}

		if (suggestion.Transaction is { } transaction)
		{
			UpdateTransaction(CurrentTransactionSummary, transaction);
		}
	}
}
