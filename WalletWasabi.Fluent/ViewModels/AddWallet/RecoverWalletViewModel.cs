using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using DynamicData;
using DynamicData.Binding;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Fluent.Validation;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Fluent.Models;

namespace WalletWasabi.Fluent.ViewModels.AddWallet;

[NavigationMetaData(Title = "Recovery Words")]
public partial class RecoverWalletViewModel : RoutableViewModel
{
	private readonly RecoverWalletState _state;
	private readonly List<RecoverWordViewModel> _words;
	[AutoNotify] private RecoverWordViewModel _currentWord;
	[AutoNotify] private Mnemonic? _currentMnemonics;
	[AutoNotify] private bool _isMnemonicsValid;
	[AutoNotify] private string? _passphrase;
	[AutoNotify] private bool _focusPassphrase;
	[AutoNotify(SetterModifier = AccessModifier.Private)] private bool _allWordsConfirmed;
	[AutoNotify(SetterModifier = AccessModifier.Private)] private bool _passphraseConfirmed;

	private RecoverWalletViewModel(RecoverWalletState state)
	{
		_state = state;

		var suggestions = new Mnemonic(Wordlist.English, WordCount.Twelve).WordList.GetWords();
		var words = Enumerable
			.Range(1, 12)
			.Select(x => new RecoverWordViewModel(x, "", suggestions));
		_words = words.OrderBy(x => x.Index).ToList();
		_currentWord = _words.First();
		_currentWord.IsSelected = true;

		_passphrase = "";

		this.ValidateProperty(x => x.CurrentMnemonics, ValidateCurrentMnemonics);

		EnableBack = true;

		var nextCanExecute = this.WhenAnyValue(x => x.IsMnemonicsValid);

		NextCommand = ReactiveCommand.CreateFromTask(
			async () => await OnNextAsync(state),
			canExecute: nextCanExecute);

		AdvancedRecoveryOptionsDialogCommand = ReactiveCommand.CreateFromTask(
			async () => await OnAdvancedRecoveryOptionsDialogAsync(state));

		var canExecuteNextWord = this.WhenAnyValue(x => x.CurrentWord.IsValid);

		NextWordCommand = ReactiveCommand.Create(NextWord, canExecuteNextWord);

		PreviousWordCommand = ReactiveCommand.Create(PreviousWord);

		SelectWordCommand = ReactiveCommand.Create<RecoverWordViewModel>(SelectWord);

		DeselectWordCommand = ReactiveCommand.Create<RecoverWordViewModel>(DeselectWord);
	}

	public ObservableCollectionExtended<RecoverWordViewModel> RecoveryWords { get; } = new();

	public ICommand AdvancedRecoveryOptionsDialogCommand { get; }

	public ICommand NextWordCommand { get; }

	public ICommand PreviousWordCommand { get; }

	public ICommand SelectWordCommand { get; }

	public ICommand DeselectWordCommand { get; }

	private int MinGapLimit { get; set; } = 114;

	private async Task OnNextAsync(RecoverWalletState state)
	{
		var (walletName, _, _, _, _) = state.Options;
		ArgumentException.ThrowIfNullOrEmpty(walletName);

		var passphrase = Passphrase;
		if (passphrase is not { } || CurrentMnemonics is not { IsValidChecksum: true } currentMnemonics)
		{
			return;
		}

		IsBusy = true;

		try
		{
			state.Options = state.Options with { Passphrase = passphrase, Mnemonic = currentMnemonics, MinGapLimit = MinGapLimit };
			Navigate().To().RecoverWalletSummary(state);
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);
			await ShowErrorAsync(Title, ex.ToUserFriendlyString(), "Wasabi was unable to recover the wallet.");
		}

		IsBusy = false;
	}

	private async Task OnAdvancedRecoveryOptionsDialogAsync(RecoverWalletState state)
	{
		var (walletName, _, _, _, _) = state.Options;
		ArgumentException.ThrowIfNullOrEmpty(walletName);

		IsBusy = true;

		try
		{
			state.Options = state.Options with { Passphrase = Passphrase, Mnemonic = CurrentMnemonics, MinGapLimit = MinGapLimit };
			Navigate().To().RecoverWalletSummary(state);
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);
			await ShowErrorAsync(Title, ex.ToUserFriendlyString(), "Wasabi was unable to recover the wallet.");
		}

		IsBusy = false;
	}

	private void NextWord()
	{
		var currentIndex = _words.IndexOf(_currentWord);
		if (currentIndex >= _words.Count - 1)
		{
			_currentWord.IsSelected = false;
			FocusPassphrase = true;
			FocusPassphrase = false;
		}
		else
		{
			_currentWord.IsSelected = false;
			_currentWord = _words[currentIndex + 1];
			_currentWord.IsSelected = true;
		}
	}

	private void PreviousWord()
	{
		var currentIndex = _words.IndexOf(_currentWord);
		if (currentIndex <= 0 || !_currentWord.IsSelected)
		{
			_currentWord.IsSelected = false;
			_currentWord = _words[^1];
			_currentWord.IsSelected = true;
		}
		else
		{
			_currentWord.IsSelected = false;
			_currentWord = _words[currentIndex - 1];
			_currentWord.IsSelected = true;
		}
	}

	private void SelectWord(RecoverWordViewModel word)
	{
		_currentWord.IsSelected = false;
		_currentWord = word;
		_currentWord.IsSelected = true;
	}

	private void DeselectWord(RecoverWordViewModel word)
	{
		word.IsSelected = false;
	}

	private void ValidateCurrentMnemonics(IValidationErrors errors)
	{
		if (CurrentMnemonics is null)
		{
			ClearValidations();
			return;
		}

		if (IsMnemonicsValid)
		{
			return;
		}

		errors.Add(ErrorSeverity.Error, "Invalid set. Make sure you typed all your recovery words in the correct order.");
	}

	private string GetTagsAsConcatString()
	{
		return string.Join(' ', _words.Select(x => x.Word));
	}

	private void RestoreState()
	{
		if (_words.Count == 12 && _state.Options.Mnemonic is not null)
		{
			for (var i = 0; i < _words.Count; i++)
			{
				var word = _words[i];

				word.Word = _state.Options.Mnemonic.Words.Length >= i + 1
					? _state.Options.Mnemonic.Words[i]
					: "";
			}
		}

		if (_state.Options.Passphrase is not null)
		{
			Passphrase = _state.Options.Passphrase;
		}
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Uses DisposeWith()")]
	protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
	{
		base.OnNavigatedTo(isInHistory, disposables);

		RecoveryWords.Clear();

		var recoveryWordsSourceList = new SourceList<RecoverWordViewModel>();

		recoveryWordsSourceList
			.DisposeWith(disposables)
			.Connect()
			.ObserveOn(RxApp.MainThreadScheduler)
			.Bind(RecoveryWords)
			.Subscribe()
			.DisposeWith(disposables);

		RestoreState();

		recoveryWordsSourceList.AddRange(_words);

		foreach (var word in _words)
		{
			word.WhenAnyValue(x => x.Word)
				.Subscribe(_ =>
				{
					var count = _words.Count(x => !string.IsNullOrEmpty(x.Word));
					try
					{
						var mnemonic = count is 12 or 15 or 18 or 21 or 24
							? new Mnemonic(GetTagsAsConcatString().ToLowerInvariant())
							: null;
						CurrentMnemonics = mnemonic;
						IsMnemonicsValid = mnemonic is { IsValidChecksum: true };
					}
					catch (Exception)
					{
						CurrentMnemonics = null;
						IsMnemonicsValid = false;
					}
					this.RaisePropertyChanged(nameof(CurrentMnemonics));
				})
				.DisposeWith(disposables);
		}

		var enableCancel = UiContext.WalletRepository.HasWallet;
		SetupCancel(enableCancel: enableCancel, enableCancelOnEscape: enableCancel, enableCancelOnPressed: false);
	}
}
