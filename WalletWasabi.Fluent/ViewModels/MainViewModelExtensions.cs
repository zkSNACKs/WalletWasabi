using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.ViewModels.AddWallet;
using WalletWasabi.Fluent.ViewModels.AI;
using WalletWasabi.Fluent.ViewModels.HelpAndSupport;
using WalletWasabi.Fluent.ViewModels.OpenDirectory;
using WalletWasabi.Fluent.ViewModels.Settings;
using WalletWasabi.Fluent.ViewModels.TransactionBroadcasting;

namespace WalletWasabi.Fluent.ViewModels;

public static class MainViewModelExtensions
{
	public static void RegisterAllViewModels(this MainViewModel mainViewModel, UiContext uiContext)
	{
		PrivacyModeViewModel.Register(mainViewModel.PrivacyMode);
		AddWalletPageViewModel.RegisterLazy(() => new AddWalletPageViewModel(uiContext));
		SettingsPageViewModel.Register(mainViewModel.SettingsPage);

		GeneralSettingsTabViewModel.RegisterLazy(() =>
		{
			mainViewModel.SettingsPage.SelectedTab = 0;
			return mainViewModel.SettingsPage;
		});

		BitcoinTabSettingsViewModel.RegisterLazy(() =>
		{
			mainViewModel.SettingsPage.SelectedTab = 1;
			return mainViewModel.SettingsPage;
		});

		AdvancedSettingsTabViewModel.RegisterLazy(() =>
		{
			mainViewModel.SettingsPage.SelectedTab = 2;
			return mainViewModel.SettingsPage;
		});

		// TODO:
		ChatsViewModel.RegisterLazy(() => new ChatsViewModel(uiContext));

		AboutViewModel.RegisterLazy(() => new AboutViewModel(uiContext));
		BroadcasterViewModel.RegisterLazy(() => new BroadcasterViewModel(uiContext));
		UserSupportViewModel.RegisterLazy(() => new UserSupportViewModel(uiContext));
		BugReportLinkViewModel.RegisterLazy(() => new BugReportLinkViewModel(uiContext));
		DocsLinkViewModel.RegisterLazy(() => new DocsLinkViewModel(uiContext));
		OpenDataFolderViewModel.RegisterLazy(() => new OpenDataFolderViewModel(uiContext));
		OpenWalletsFolderViewModel.RegisterLazy(() => new OpenWalletsFolderViewModel(uiContext));
		OpenLogsViewModel.RegisterLazy(() => new OpenLogsViewModel(uiContext));
		OpenTorLogsViewModel.RegisterLazy(() => new OpenTorLogsViewModel(uiContext));
		OpenConfigFileViewModel.RegisterLazy(() => new OpenConfigFileViewModel(uiContext));
	}
}
