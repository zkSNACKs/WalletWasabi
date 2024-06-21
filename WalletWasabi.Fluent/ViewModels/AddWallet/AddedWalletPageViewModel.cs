using ReactiveUI;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Wallets;
using System.Reactive.Disposables;
using WalletWasabi.Fluent.Models;
using System.Threading.Tasks;

namespace WalletWasabi.Fluent.ViewModels.AddWallet;

[NavigationMetaData(Title = "Success")]
public partial class AddedWalletPageViewModel : RoutableViewModel
{
	private readonly IWalletSettingsModel _walletSettings;
	private IWalletModel? _wallet;

	private AddedWalletPageViewModel(IWalletSettingsModel walletSettings, WalletCreationOptions options)
	{
		_walletSettings = walletSettings;

		WalletName = options.WalletName!;
		WalletType = walletSettings.WalletType;

		SetupCancel(enableCancel: false, enableCancelOnEscape: false, enableCancelOnPressed: false);
		EnableBack = false;

		NextCommand = ReactiveCommand.CreateFromTask(() => OnNextAsync(options));
	}

	public WalletType WalletType { get; }

	public string WalletName { get; }

	private async Task OnNextAsync(WalletCreationOptions options)
	{
		if (_wallet is not { })
		{
			return;
		}

		IsBusy = true;

		await AutoLoginAsync(options);

		IsBusy = false;

		await Task.Delay(UiConstants.CloseSuccessDialogMillisecondsDelay);

		Navigate().Clear();

		UiContext.Navigate().To(_wallet);
	}

	protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
	{
		base.OnNavigatedTo(isInHistory, disposables);

		_wallet = UiContext.WalletRepository.SaveWallet(_walletSettings);

		if (NextCommand is not null && NextCommand.CanExecute(default))
		{
			NextCommand.Execute(default);
		}
	}

	private async Task AutoLoginAsync(WalletCreationOptions? options)
	{
		if (_wallet is not { })
		{
			return;
		}

		var password =
			options switch
			{
				WalletCreationOptions.AddNewWallet add => add.Passphrase,
				WalletCreationOptions.RecoverWallet rec => rec.Passphrase,
				WalletCreationOptions.ConnectToHardwareWallet => "",
				_ => null
			};

		if (password is { })
		{
			await _wallet.Auth.LoginAsync(password);
		}
	}
}
