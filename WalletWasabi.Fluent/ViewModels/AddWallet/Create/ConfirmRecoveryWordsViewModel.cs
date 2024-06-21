using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using WalletWasabi.Fluent.Models;
using WalletWasabi.Fluent.Validation;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Models;
using WalletWasabi.Userfacing;

namespace WalletWasabi.Fluent.ViewModels.AddWallet.Create;

[NavigationMetaData(Title = "Confirm Recovery Words")]
public partial class ConfirmRecoveryWordsViewModel : RoutableViewModel
{
	private readonly List<RecoveryWordViewModel> _words;
	private readonly WalletCreationOptions.AddNewWallet _options;

	[AutoNotify] private bool _isSkipEnabled;
	[AutoNotify] private RecoveryWordViewModel _currentWord;
	[AutoNotify] private List<RecoveryWordViewModel> _availableWords;
	[AutoNotify(SetterModifier = AccessModifier.Private)] private string? _passphrase;
	[AutoNotify] private string? _confirmPassphrase;
	[AutoNotify(SetterModifier = AccessModifier.Private)] private bool _allWordsConfirmed;
	[AutoNotify(SetterModifier = AccessModifier.Private)] private bool _passphraseConfirmed;

	private ConfirmRecoveryWordsViewModel(WalletCreationOptions.AddNewWallet options, List<RecoveryWordViewModel> words)
	{
		_options = options;
		_availableWords = new List<RecoveryWordViewModel>();
		_words = words.OrderBy(x => x.Index).ToList();
		_currentWord = words.First();

		this.ValidateProperty(x => x.ConfirmPassphrase, ValidateConfirmPassphrase);
	}

	public ObservableCollectionExtended<RecoveryWordViewModel> ConfirmationWords { get; } = new();

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Uses DisposeWith()")]
	protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
	{
		base.OnNavigatedTo(isInHistory, disposables);

		ConfirmationWords.Clear();

		var confirmationWordsSourceList = new SourceList<RecoveryWordViewModel>();

		confirmationWordsSourceList
			.DisposeWith(disposables)
			.Connect()
			.ObserveOn(RxApp.MainThreadScheduler)
			.Bind(ConfirmationWords)
			.OnItemAdded(x => x.Reset())
			.Subscribe()
			.DisposeWith(disposables);

		EnableBack = true;

		CancelCommand = ReactiveCommand.Create(OnCancel);

		confirmationWordsSourceList
			.Connect()
			.WhenValueChanged(x => x.IsConfirmed)
			.Subscribe(_ => AllWordsConfirmed = confirmationWordsSourceList.Items.All(x => x.IsConfirmed))
			.DisposeWith(disposables);

		if (string.IsNullOrEmpty(_options.Passphrase))
		{
			PassphraseConfirmed = true;
		}
		else
		{
			this.WhenAnyValue(x => x.ConfirmPassphrase)
				.Subscribe(confirmPassphrase =>
				{
					PassphraseConfirmed = confirmPassphrase == _options.Passphrase;
				})
				.DisposeWith(disposables);
		}

		var nextCommandCanExecute = this.WhenAnyValue(
				x => x.AllWordsConfirmed,
				x => x.PassphraseConfirmed)
			.Select(x => x.Item1 && x.Item2);

		NextCommand = ReactiveCommand.CreateFromTask(OnNextAsync, nextCommandCanExecute);

		SetSkip();

		confirmationWordsSourceList.AddRange(_words);

		AvailableWords = confirmationWordsSourceList.Items
			.Select(x => new RecoveryWordViewModel(x.Index, x.Word))
			.OrderBy(x => x.Word)
			.ToList();

		var availableWordsSourceList = new SourceList<RecoveryWordViewModel>()
			.DisposeWith(disposables);

		availableWordsSourceList
			.Connect()
			.WhenPropertyChanged(x => x.IsSelected)
			.Subscribe(x => OnWordSelectionChanged(x.Sender))
			.DisposeWith(disposables);

		availableWordsSourceList.AddRange(AvailableWords);

		SetNextWord();

		Passphrase = _options.Passphrase;

		var enableCancel = UiContext.WalletRepository.HasWallet;
		SetupCancel(enableCancel: false, enableCancelOnEscape: enableCancel, enableCancelOnPressed: false);
	}

	private void ValidateConfirmPassphrase(IValidationErrors errors)
	{
		if (!string.IsNullOrEmpty(ConfirmPassphrase) && Passphrase != ConfirmPassphrase)
		{
			errors.Add(ErrorSeverity.Error, PasswordHelper.MatchingMessage);
		}
	}

	private void SetNextWord()
	{
		if (ConfirmationWords.FirstOrDefault(x => !x.IsConfirmed) is { } nextWord)
		{
			CurrentWord = nextWord;
		}

		EnableAvailableWords(true);
	}

	private void OnWordSelectionChanged(RecoveryWordViewModel selectedWord)
	{
		if (selectedWord.IsSelected)
		{
			CurrentWord.SelectedWord = selectedWord.Word;
		}
		else
		{
			CurrentWord.SelectedWord = null;
		}

		if (CurrentWord.IsConfirmed)
		{
			selectedWord.IsConfirmed = true;
			SetNextWord();
		}
		else if (!selectedWord.IsSelected)
		{
			EnableAvailableWords(true);
		}
		else
		{
			EnableAvailableWords(false);
			selectedWord.IsEnabled = true;
		}
	}

	private void EnableAvailableWords(bool enable)
	{
		foreach (var w in AvailableWords)
		{
			w.IsEnabled = enable;
		}
	}

	private async Task OnNextAsync()
	{
		IsBusy = true;

		var walletSettings = await UiContext.WalletRepository.NewWalletAsync(_options);

		IsBusy = false;

		await Navigate().To().CoinJoinProfiles(walletSettings, _options).GetResultAsync();
	}

	private void OnCancel()
	{
		Navigate().Clear();
	}

	private void SetSkip()
	{
#if RELEASE
		IsSkipEnabled = Services.WalletManager.Network != NBitcoin.Network.Main || System.Diagnostics.Debugger.IsAttached;
#else
		IsSkipEnabled = true;
#endif

		if (IsSkipEnabled)
		{
			SkipCommand = ReactiveCommand.CreateFromTask(OnNextAsync);
		}
	}
}
