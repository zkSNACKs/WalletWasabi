using ReactiveUI;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Fluent.Validation;
using System.Threading.Tasks;
using WalletWasabi.Fluent.Models;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Models;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Models.UI;

namespace WalletWasabi.Fluent.ViewModels.AddWallet;

[NavigationMetaData(Title = "Wallet Name")]
public partial class WalletNamePageViewModel : RoutableViewModel
{
	private readonly WalletCreationOptions _options;
	[AutoNotify] private string _walletName;

	public WalletNamePageViewModel(UiContext uiContext, WalletCreationOptions options)
	{
		UiContext = uiContext;

		_options = options;
		_walletName = UiContext.WalletRepository.GetNextWalletName();

		EnableBack = true;

		var nextCommandCanExecute =
			this.WhenAnyValue(x => x.WalletName)
				.Select(_ => !Validations.Any);

		NextCommand = ReactiveCommand.CreateFromTask(OnNextAsync, nextCommandCanExecute);

		this.ValidateProperty(x => x.WalletName, ValidateWalletName);
	}

	private async Task OnNextAsync()
	{
		IsBusy = true;

		// Makes sure we can create a wallet with this wallet name.
		await Task.Run(() => WalletGenerator.GetWalletFilePath(WalletName, Services.WalletManager.WalletDirectories.WalletsDir));

		IsBusy = false;

		var options = _options with { WalletName = WalletName };

		switch (options)
		{
			case WalletCreationOptions.AddNewWallet add:
				var dialogCaption = "This is needed to open and to recover your wallet.\n" +
				                    "It will be used as the 13th seed word, it can never be changed.";
				var password = await Navigate().To().CreatePasswordDialog("Add Passphrase", dialogCaption, enableEmpty: true).GetResultAsync();

				if (password is { })
				{
					var addOptions = add with { Passphrase = password };

					Navigate().To().RecoveryWords(addOptions);
				}

			 	break;

			case WalletCreationOptions.ConnectToHardwareWallet chw:
				Navigate().To().ConnectHardwareWallet(chw);
				break;

			case WalletCreationOptions.RecoverWallet rec:
				Navigate().To().RecoverWallet(new RecoverWalletState(rec));
				break;

			case WalletCreationOptions.ImportWallet imp:
				await ImportWalletAsync(imp);
				break;

			default:
				throw new InvalidOperationException($"{nameof(WalletCreationOptions)} not supported: {options?.GetType().Name}");
		}
	}

	private async Task ImportWalletAsync(WalletCreationOptions.ImportWallet options)
	{
		try
		{
			var walletSettings = await UiContext.WalletRepository.NewWalletAsync(options);
			Navigate().To().AddedWalletPage(walletSettings, options);
		}
		catch (Exception ex)
		{
			await ShowErrorAsync("Import wallet", ex.ToUserFriendlyString(), "Wasabi was unable to import your wallet.");
			BackCommand.Execute(null);
		}
	}

	private void ValidateWalletName(IValidationErrors errors)
	{
		var error = UiContext.WalletRepository.ValidateWalletName(WalletName);
		if (error is { } e)
		{
			errors.Add(e.Severity, e.Message);
		}
	}

	protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
	{
		base.OnNavigatedTo(isInHistory, disposables);

		if (isInHistory && !UiContext.WalletRepository.HasWallet)
		{
			Navigate().Back();
		}
		else if (!UiContext.WalletRepository.HasWallet && NextCommand is { } cmd && cmd.CanExecute(default))
		{
			cmd.Execute(default);
		}

		var enableCancel = UiContext.WalletRepository.HasWallet;
		SetupCancel(enableCancel: enableCancel, enableCancelOnEscape: enableCancel, enableCancelOnPressed: enableCancel);
	}
}
