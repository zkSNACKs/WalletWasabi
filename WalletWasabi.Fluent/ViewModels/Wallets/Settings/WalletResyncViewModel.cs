using System.Reactive;
using ReactiveUI;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.Validation;
using WalletWasabi.Fluent.ViewModels.Dialogs.Base;
using WalletWasabi.Services.Terminate;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Settings;

[NavigationMetaData(Title = "Resync Wallet", NavigationTarget = NavigationTarget.CompactDialogScreen)]
public partial class WalletResyncViewModel : DialogViewModelBase<Unit>
{
	private WalletResyncViewModel(IWalletSettingsModel walletSettings)
	{
		SetupCancel(enableCancel: true, enableCancelOnEscape: true, enableCancelOnPressed: true);
		NextCommand = ReactiveCommand.Create(() => OnResync(walletSettings));
	}

	private void OnResync(IWalletSettingsModel walletSettings)
	{
		try
		{
			walletSettings.SignalResync();
			TerminateService.Instance.SignalForceTerminate();
		}
		catch
		{
			UiContext.Navigate().To().ShowErrorDialog($"Something went wrong, failed to terminate Wasabi Wallet", "Shutdown", "Cannot terminate Wasabi", NavigationTarget.CompactDialogScreen);
		}
	}
}
