using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia.Controls;
using DynamicData;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Affiliation.Models;
using WalletWasabi.Fluent.Infrastructure;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.ViewModels.Dialogs.Announcement;
using WalletWasabi.Fluent.ViewModels.Dialogs.Base;
using WalletWasabi.Fluent.ViewModels.NavBar;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Fluent.ViewModels.SearchBar;
using WalletWasabi.Fluent.ViewModels.SearchBar.Sources;
using WalletWasabi.Fluent.ViewModels.Settings;
using WalletWasabi.Fluent.ViewModels.StatusIcon;
using WalletWasabi.Fluent.ViewModels.Wallets;
using WalletWasabi.Fluent.ViewModels.Wallets.Notifications;

namespace WalletWasabi.Fluent.ViewModels;

[AppLifetime]
public partial class MainViewModel : ViewModelBase
{
	[AutoNotify] private string _title = "Wasabi Wallet";
	[AutoNotify] private WindowState _windowState;
	[AutoNotify] private bool _isOobeBackgroundVisible;
	[AutoNotify] private bool _isCoinJoinActive;

	public MainViewModel(UiContext uiContext)
	{
		UiContext = uiContext;
		UiContext.SetMainViewModel(this);

		ApplyUiConfigWindowState();

		DialogScreen = new DialogScreenViewModel();
		FullScreen = new DialogScreenViewModel(NavigationTarget.FullScreen);
		CompactDialogScreen = new DialogScreenViewModel(NavigationTarget.CompactDialogScreen);
		NavBar = new NavBarViewModel(UiContext);
		MainScreen = new TargettedNavigationStack(NavigationTarget.HomeScreen);
		UiContext.RegisterNavigation(new NavigationState(UiContext, MainScreen, DialogScreen, FullScreen, CompactDialogScreen, NavBar));

		NavBar.Activate();

		StatusIcon = new StatusIconViewModel(UiContext);

		SettingsPage = new SettingsPageViewModel(UiContext);
		PrivacyMode = new PrivacyModeViewModel(UiContext.ApplicationSettings);
		Notifications = new WalletNotificationsViewModel(UiContext, NavBar);

		NavigationManager.RegisterType(NavBar);

		this.RegisterAllViewModels(UiContext);

		RxApp.MainThreadScheduler.Schedule(async () => await NavBar.InitialiseAsync());

		this.WhenAnyValue(x => x.WindowState)
			.Where(state => state != WindowState.Minimized)
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(state => UiContext.ApplicationSettings.WindowState = state);

		IsMainContentEnabled =
			this.WhenAnyValue(
					x => x.DialogScreen.IsDialogOpen,
					x => x.FullScreen.IsDialogOpen,
					x => x.CompactDialogScreen.IsDialogOpen,
					(dialogIsOpen, fullScreenIsOpen, compactIsOpen) => !(dialogIsOpen || fullScreenIsOpen || compactIsOpen))
				.ObserveOn(RxApp.MainThreadScheduler);

		CurrentWallet =
			this.WhenAnyValue(x => x.MainScreen.CurrentPage)
				.WhereNotNull()
				.OfType<WalletViewModel>();

		IsOobeBackgroundVisible = UiContext.ApplicationSettings.Oobe;

		RxApp.MainThreadScheduler.Schedule(async () =>
		{
			if (!UiContext.WalletRepository.HasWallet || UiContext.ApplicationSettings.Oobe)
			{
				IsOobeBackgroundVisible = true;

				await UiContext.Navigate().To().WelcomePage().GetResultAsync();

				if (UiContext.WalletRepository.HasWallet)
				{
					UiContext.ApplicationSettings.Oobe = false;
					IsOobeBackgroundVisible = false;
				}
			}

			await Task.Delay(1000);

			foreach (var page in GetAnnouncements())
			{
				await uiContext.Navigate().NavigateDialogAsync(page, navigationMode: NavigationMode.Clear);
			}
		});

		SearchBar = CreateSearchBar();

		NetworkBadgeName =
			UiContext.ApplicationSettings.Network == Network.Main
			? ""
			: UiContext.ApplicationSettings.Network.Name;

		// TODO: the reason why this MainViewModel singleton is even needed throughout the codebase is dubious.
		// Also it causes tight coupling which damages testability.
		// We should strive to remove it altogether.
		if (Instance != null)
		{
			throw new InvalidOperationException($"MainViewModel instantiated more than once.");
		}

		Instance = this;
	}

	public IObservable<bool> IsMainContentEnabled { get; }

	public string NetworkBadgeName { get; }

	public IObservable<WalletViewModel> CurrentWallet { get; }

	public TargettedNavigationStack MainScreen { get; }

	public SearchBarViewModel SearchBar { get; }

	public DialogScreenViewModel DialogScreen { get; }
	public DialogScreenViewModel FullScreen { get; }
	public DialogScreenViewModel CompactDialogScreen { get; }
	public NavBarViewModel NavBar { get; }
	public StatusIconViewModel StatusIcon { get; }
	public SettingsPageViewModel SettingsPage { get; }
	public PrivacyModeViewModel PrivacyMode { get; }
	public WalletNotificationsViewModel Notifications { get; }

	public static MainViewModel Instance { get; private set; }

	private IEnumerable<AnnouncementBase> GetAnnouncements()
	{
		var announcements = new List<AnnouncementBase>();

		// Add Announcements here.

		return announcements;
	}

	public bool IsDialogOpen()
	{
		return DialogScreen.IsDialogOpen
			   || FullScreen.IsDialogOpen
			   || CompactDialogScreen.IsDialogOpen;
	}

	public void ShowDialogAlert()
	{
		if (CompactDialogScreen.IsDialogOpen)
		{
			CompactDialogScreen.ShowAlert = false;
			CompactDialogScreen.ShowAlert = true;
			return;
		}

		if (DialogScreen.IsDialogOpen)
		{
			DialogScreen.ShowAlert = false;
			DialogScreen.ShowAlert = true;
			return;
		}

		if (FullScreen.IsDialogOpen)
		{
			FullScreen.ShowAlert = false;
			FullScreen.ShowAlert = true;
		}
	}

	public void ClearStacks()
	{
		MainScreen.Clear();
		DialogScreen.Clear();
		FullScreen.Clear();
		CompactDialogScreen.Clear();
	}

	public void Initialize()
	{
		UiContext.WalletRepository.Wallets
			.Connect()
			.FilterOnObservable(x => x.Coinjoin.IsRunning)
			.ToCollection()
			.Select(x => x.Count != 0)
			.BindTo(this, x => x.IsCoinJoinActive);

		Notifications.StartListening();

		if (UiContext.ApplicationSettings.Network != Network.Main)
		{
			Title += $" - {UiContext.ApplicationSettings.Network}";
		}
	}

	public void ApplyUiConfigWindowState()
	{
		WindowState = UiContext.ApplicationSettings.WindowState;
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Same lifecycle as the application. Won't be disposed separately.")]
	private SearchBarViewModel CreateSearchBar()
	{
		// This subject is created to solve the circular dependency between the sources and SearchBarViewModel
		var querySubject = new Subject<string>();

		var source = new CompositeSearchSource(
			new ActionsSearchSource(UiContext, querySubject),
			new SettingsSearchSource(UiContext, querySubject),
			new TransactionsSearchSource(querySubject),
			UiContext.EditableSearchSource);

		var searchBar = new SearchBarViewModel(source);

		var queries = searchBar
			.WhenAnyValue(a => a.SearchText)
			.WhereNotNull();

		UiContext.EditableSearchSource.SetQueries(queries);

		queries
			.Subscribe(querySubject);

		return searchBar;
	}
}
