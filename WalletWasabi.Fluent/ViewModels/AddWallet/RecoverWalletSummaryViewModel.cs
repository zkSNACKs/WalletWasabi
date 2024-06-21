using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using DynamicData;
using DynamicData.Binding;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Fluent.Validation;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Fluent.Models;

namespace WalletWasabi.Fluent.ViewModels.AddWallet;

public class RecoverWalletState(WalletCreationOptions.RecoverWallet options)
{
	public WalletCreationOptions.RecoverWallet Options { get; set; } = options;
}

[NavigationMetaData(Title = "Recovery Words")]
public partial class RecoverWalletSummaryViewModel : RoutableViewModel
{
	[AutoNotify] private IEnumerable<string>? _suggestions;
	[AutoNotify] private Mnemonic? _currentMnemonics;
	[AutoNotify] private bool _isMnemonicsValid;
	[AutoNotify] private string? _passphrase;
	[AutoNotify] private string? _minGapLimit;

	private RecoverWalletSummaryViewModel(RecoverWalletState state)
	{
		Passphrase = state.Options.Passphrase;

		MinGapLimit = state.Options.MinGapLimit.ToString();

		Suggestions = new Mnemonic(Wordlist.English, WordCount.Twelve).WordList.GetWords();

		if (state.Options.Mnemonic is not null)
		{
			Mnemonics.AddRange(state.Options.Mnemonic.Words);
		}

		Mnemonics.ToObservableChangeSet().ToCollection()
			.Select(x => x.Count is 12 or 15 or 18 or 21 or 24 ? new Mnemonic(GetTagsAsConcatString().ToLowerInvariant()) : null)
			.Subscribe(x =>
			{
				CurrentMnemonics = x;
				IsMnemonicsValid = x is { IsValidChecksum: true };
				this.RaisePropertyChanged(nameof(Mnemonics));
			});

		this.ValidateProperty(x => x.Mnemonics, ValidateMnemonics);
		this.ValidateProperty(x => x.MinGapLimit, ValidateMinGapLimit);

		EnableBack = true;

		var canExecuteNext = this.WhenAnyValue(
			x => x.MinGapLimit,
			x => x.IsMnemonicsValid)
			.Select(x =>
			{
				var (_, isMnemonicsValid) = x;
				return !Validations.Any && isMnemonicsValid;
			});

		NextCommand = ReactiveCommand.CreateFromTask(
			async () => await OnNextAsync(state),
			canExecute: canExecuteNext);

		var canExecuteBack = this.WhenAnyValue(
				model => model.IsBusy,
				model => model.Mnemonics.Count)
			.Select(x => x is { Item1: false, Item2: <= 12 });

		BackCommand = ReactiveCommand.Create(() =>
		{
			state.Options = state.Options with
			{
				Passphrase = Passphrase,
				Mnemonic = new Mnemonic(GetTagsAsConcatString().ToLowerInvariant()),
				MinGapLimit = int.Parse(MinGapLimit)
			};

			Navigate().Back();
		}, canExecuteBack);
	}

	public ObservableCollection<string> Mnemonics { get; } = new();

	private async Task OnNextAsync(RecoverWalletState state)
	{
		var (walletName, _, _, _, _) = state.Options;
		ArgumentException.ThrowIfNullOrEmpty(walletName);

		IsBusy = true;

		try
		{
			state.Options = state.Options with
			{
				Passphrase = Passphrase,
				Mnemonic = _currentMnemonics,
				MinGapLimit = int.Parse(MinGapLimit)
			};

			var wallet = await UiContext.WalletRepository.NewWalletAsync(state.Options);
			await Navigate().To().CoinJoinProfiles(wallet, state.Options).GetResultAsync();
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);
			await ShowErrorAsync(Title, ex.ToUserFriendlyString(), "Wasabi was unable to recover the wallet.");
		}

		IsBusy = false;
	}

	private void ValidateMnemonics(IValidationErrors errors)
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

		if (!Mnemonics.Any())
		{
			return;
		}

		errors.Add(ErrorSeverity.Error, "Invalid set. Make sure you typed all your recovery words in the correct order.");
	}

	private void ValidateMinGapLimit(IValidationErrors errors)
	{
		if (!int.TryParse(MinGapLimit, out var minGapLimit) ||
		    minGapLimit is < KeyManager.AbsoluteMinGapLimit or > KeyManager.MaxGapLimit)
		{
			errors.Add(
				ErrorSeverity.Error,
				$"Must be a number between {KeyManager.AbsoluteMinGapLimit} and {KeyManager.MaxGapLimit}.");
		}
	}

	private string GetTagsAsConcatString()
	{
		return string.Join(' ', Mnemonics);
	}

	protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
	{
		base.OnNavigatedTo(isInHistory, disposables);

		var enableCancel = UiContext.WalletRepository.HasWallet;
		SetupCancel(enableCancel: enableCancel, enableCancelOnEscape: enableCancel, enableCancelOnPressed: false);
	}
}
