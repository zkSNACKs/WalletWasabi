using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Avalonia.Controls;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Fluent.ViewModels.AddWallet;
using WalletWasabi.Fluent.ViewModels.Dialogs.Base;
using WalletWasabi.Fluent.ViewModels.HelpAndSupport;
using WalletWasabi.Fluent.ViewModels.NavBar;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Fluent.ViewModels.OpenDirectory;
using WalletWasabi.Fluent.ViewModels.SearchBar;
using WalletWasabi.Fluent.ViewModels.SearchBar.Sources;
using WalletWasabi.Fluent.ViewModels.Settings;
using WalletWasabi.Fluent.ViewModels.StatusIcon;
using WalletWasabi.Fluent.ViewModels.TransactionBroadcasting;
using WalletWasabi.Fluent.ViewModels.Wallets;
using WalletWasabi.Logging;

namespace WalletWasabi.Fluent.ViewModels;

public partial class MainViewModel : ViewModelBase
{
	private readonly SettingsPageViewModel _settingsPage;
	private readonly PrivacyModeViewModel _privacyMode;
	private readonly AddWalletPageViewModel _addWalletPage;
	[AutoNotify] private DialogScreenViewModel _dialogScreen;
	[AutoNotify] private DialogScreenViewModel _fullScreen;
	[AutoNotify] private DialogScreenViewModel _compactDialogScreen;
	[AutoNotify] private NavBarViewModel _navBar;
	[AutoNotify] private StatusIconViewModel _statusIcon;
	[AutoNotify] private string _title = "Wasabi Wallet";
	[AutoNotify] private WindowState _windowState;
	[AutoNotify] private bool _isOobeBackgroundVisible;
	[AutoNotify] private bool _isCoinJoinActive;

	public MainViewModel()
	{
		ApplyUiConfigWindowSate();

		_dialogScreen = new DialogScreenViewModel();
		_fullScreen = new DialogScreenViewModel(NavigationTarget.FullScreen);
		_compactDialogScreen = new DialogScreenViewModel(NavigationTarget.CompactDialogScreen);
		MainScreen = new TargettedNavigationStack(NavigationTarget.HomeScreen);
		NavigationState.Register(MainScreen, DialogScreen, FullScreen, CompactDialogScreen);

		_statusIcon = new StatusIconViewModel();

		UiServices.Initialize();

		_addWalletPage = new AddWalletPageViewModel();
		_settingsPage = new SettingsPageViewModel();
		_privacyMode = new PrivacyModeViewModel();
		_navBar = new NavBarViewModel(MainScreen);

		NavigationManager.RegisterType(_navBar);
		RegisterViewModels();

		RxApp.MainThreadScheduler.Schedule(async () => await _navBar.InitialiseAsync());

		this.WhenAnyValue(x => x.WindowState)
			.Where(state => state != WindowState.Minimized)
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(state => Services.UiConfig.WindowState = state.ToString());

		IsMainContentEnabled = this.WhenAnyValue(
				x => x.DialogScreen.IsDialogOpen,
				x => x.FullScreen.IsDialogOpen,
				x => x.CompactDialogScreen.IsDialogOpen,
				(dialogIsOpen, fullScreenIsOpen, compactIsOpen) => !(dialogIsOpen || fullScreenIsOpen || compactIsOpen))
			.ObserveOn(RxApp.MainThreadScheduler);

		this.WhenAnyValue(
				x => x.DialogScreen.CurrentPage,
				x => x.CompactDialogScreen.CurrentPage,
				x => x.FullScreen.CurrentPage,
				x => x.MainScreen.CurrentPage,
				(dialog, compactDialog, fullScreenDialog, mainScreen) => compactDialog ?? dialog ?? fullScreenDialog ?? mainScreen)
			.WhereNotNull()
			.ObserveOn(RxApp.MainThreadScheduler)
			.Do(page => page.SetActive())
			.Subscribe();

		CurrentWallet =
			this.WhenAnyValue(x => x.MainScreen.CurrentPage)
			.WhereNotNull()
			.OfType<WalletViewModel>();

		IsOobeBackgroundVisible = Services.UiConfig.Oobe;

		RxApp.MainThreadScheduler.Schedule(async () =>
		{
			if (!Services.WalletManager.HasWallet() || Services.UiConfig.Oobe)
			{
				IsOobeBackgroundVisible = true;

				await _dialogScreen.NavigateDialogAsync(new WelcomePageViewModel(_addWalletPage));

				if (Services.WalletManager.HasWallet())
				{
					Services.UiConfig.Oobe = false;
					IsOobeBackgroundVisible = false;
				}
			}
		});

		var source = new CompositeSearchItemsSource(new ActionsSource(), new SettingsSource(_settingsPage));
		SearchBar = new SearchBarViewModel(source.Changes);
	}

	public IObservable<bool> IsMainContentEnabled { get; }

	public IObservable<WalletViewModel> CurrentWallet { get; }

	public TargettedNavigationStack MainScreen { get; }

	public SearchBarViewModel SearchBar { get; }

	public static MainViewModel Instance { get; } = new();

	public void ClearStacks()
	{
		MainScreen.Clear();
		DialogScreen.Clear();
		FullScreen.Clear();
		CompactDialogScreen.Clear();
	}

	public void InvalidateIsCoinJoinActive()
	{
		IsCoinJoinActive = UiServices.WalletManager.Wallets.OfType<WalletViewModel>().Any(x => x.IsCoinJoining);
	}

	public void Initialize()
	{
		StatusIcon.Initialize();

		if (Services.Config.Network != Network.Main)
		{
			Title += $" - {Services.Config.Network}";
		}
	}

	private void RegisterViewModels()
	{
		PrivacyModeViewModel.Register(_privacyMode);
		AddWalletPageViewModel.Register(_addWalletPage);
		SettingsPageViewModel.Register(_settingsPage);

		GeneralSettingsTabViewModel.RegisterLazy(() =>
		{
			_settingsPage.SelectedTab = 0;
			return _settingsPage;
		});

		BitcoinTabSettingsViewModel.RegisterLazy(() =>
		{
			_settingsPage.SelectedTab = 1;
			return _settingsPage;
		});

		RxApp.MainThreadScheduler.Schedule(async () =>
		{
			try
			{
				await Services.LegalChecker.WaitAndGetLatestDocumentAsync();

				LegalDocumentsViewModel.RegisterAsyncLazy(async () =>
				{
					var document = await Services.LegalChecker.WaitAndGetLatestDocumentAsync();
					return new LegalDocumentsViewModel(document.Content);
				});
			}
			catch (Exception ex)
			{
				if (ex is not OperationCanceledException)
				{
					Logger.LogError("Failed to get Legal documents.", ex);
				}
			}
		});

		AboutViewModel.RegisterLazy(() => new AboutViewModel());
		BroadcasterViewModel.RegisterLazy(() => new BroadcasterViewModel());
		UserSupportViewModel.RegisterLazy(() => new UserSupportViewModel());
		BugReportLinkViewModel.RegisterLazy(() => new BugReportLinkViewModel());
		DocsLinkViewModel.RegisterLazy(() => new DocsLinkViewModel());
		OpenDataFolderViewModel.RegisterLazy(() => new OpenDataFolderViewModel());
		OpenWalletsFolderViewModel.RegisterLazy(() => new OpenWalletsFolderViewModel());
		OpenLogsViewModel.RegisterLazy(() => new OpenLogsViewModel());
		OpenTorLogsViewModel.RegisterLazy(() => new OpenTorLogsViewModel());
		OpenConfigFileViewModel.RegisterLazy(() => new OpenConfigFileViewModel());
	}

	public void ApplyUiConfigWindowSate()
	{
		WindowState = (WindowState)Enum.Parse(typeof(WindowState), Services.UiConfig.WindowState);
	}
}
