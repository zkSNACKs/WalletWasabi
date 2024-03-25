using System.Reactive.Disposables;
using ReactiveUI;
using System.Reactive.Linq;
using System.Threading.Tasks;
using WalletWasabi.Fluent.Infrastructure;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.ViewModels.Dialogs.Base;
using WalletWasabi.Fluent.ViewModels.Wallets;

namespace WalletWasabi.Fluent.ViewModels.Navigation;

[AppLifetime]
public class NavigationState : ReactiveObject, INavigate
{
	private readonly IWalletNavigation _walletNavigation;

	public NavigationState(
		UiContext uiContext,
		INavigationStack<RoutableViewModel> homeScreenNavigation,
		INavigationStack<RoutableViewModel> dialogScreenNavigation,
		INavigationStack<RoutableViewModel> fullScreenNavigation,
		INavigationStack<RoutableViewModel> compactDialogScreenNavigation,
		IWalletNavigation walletNavigation)
	{
		UiContext = uiContext;
		HomeScreen = homeScreenNavigation;
		DialogScreen = dialogScreenNavigation;
		FullScreen = fullScreenNavigation;
		CompactDialogScreen = compactDialogScreenNavigation;
		_walletNavigation = walletNavigation;
		this.WhenAnyValue(
				x => x.DialogScreen.CurrentPage,
				x => x.CompactDialogScreen.CurrentPage,
				x => x.FullScreen.CurrentPage,
				x => x.HomeScreen.CurrentPage,
				(dialog, compactDialog, fullScreenDialog, mainScreen) => compactDialog ?? dialog ?? fullScreenDialog ?? mainScreen)
			.WhereNotNull()
			.ObserveOn(RxApp.MainThreadScheduler)
			.Do(OnCurrentPageChanged)
			.Subscribe();

		IsDialogOpen =
			this.WhenAnyValue(
				x => x.DialogScreen.CurrentPage,
				x => x.CompactDialogScreen.CurrentPage,
				x => x.FullScreen.CurrentPage,
				(d, c, f) => (d ?? c ?? f) != null);
	}

	public UiContext UiContext { get; }

	public INavigationStack<RoutableViewModel> HomeScreen { get; }

	public INavigationStack<RoutableViewModel> DialogScreen { get; }

	public INavigationStack<RoutableViewModel> FullScreen { get; }

	public INavigationStack<RoutableViewModel> CompactDialogScreen { get; }

	public IObservable<bool> IsDialogOpen { get; }

	public bool IsAnyPageBusy =>
		HomeScreen.CurrentPage is { IsBusy: true } ||
		DialogScreen.CurrentPage is { IsBusy: true } ||
		FullScreen.CurrentPage is { IsBusy: true } ||
		CompactDialogScreen.CurrentPage is { IsBusy: true };

	public INavigationStack<RoutableViewModel> Navigate(NavigationTarget currentTarget)
	{
		return currentTarget switch
		{
			NavigationTarget.HomeScreen => HomeScreen,
			NavigationTarget.DialogScreen => DialogScreen,
			NavigationTarget.FullScreen => FullScreen,
			NavigationTarget.CompactDialogScreen => CompactDialogScreen,
			_ => throw new NotSupportedException(),
		};
	}

	public FluentNavigate To()
	{
		return new FluentNavigate(UiContext);
	}

	public IWalletViewModel? To(IWalletModel wallet)
	{
		return _walletNavigation.To(wallet);
	}

	public async Task<DialogResult<TResult>> NavigateDialogAsync<TResult>(DialogViewModelBase<TResult> dialog, NavigationTarget target = NavigationTarget.Default, NavigationMode navigationMode = NavigationMode.Normal)
	{
		target = NavigationExtensions.GetTarget(dialog, target);
		return await Navigate(target).NavigateDialogAsync(dialog, navigationMode);
	}

	private void OnCurrentPageChanged(RoutableViewModel page)
	{
		if (HomeScreen.CurrentPage is { } homeScreen)
		{
			homeScreen.IsActive = false;
		}

		if (DialogScreen.CurrentPage is { } dialogScreen)
		{
			dialogScreen.IsActive = false;
		}

		if (FullScreen.CurrentPage is { } fullScreen)
		{
			fullScreen.IsActive = false;
		}

		if (CompactDialogScreen.CurrentPage is { } compactDialogScreen)
		{
			compactDialogScreen.IsActive = false;
		}

		page.IsActive = true;
	}
}
