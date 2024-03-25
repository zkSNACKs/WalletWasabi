using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using ReactiveUI;
using WalletWasabi.Fluent.Infrastructure;
using WalletWasabi.Fluent.ViewModels.Navigation;

namespace WalletWasabi.Fluent.ViewModels.Dialogs.Base;

[AppLifetime]
public partial class DialogScreenViewModel : TargettedNavigationStack
{
	[AutoNotify] private bool _isDialogOpen;
	[AutoNotify] private bool _showAlert;

	public DialogScreenViewModel(NavigationTarget navigationTarget = NavigationTarget.DialogScreen) : base(navigationTarget)
	{
		this.WhenAnyValue(x => x.IsDialogOpen)
			.Skip(1) // Skip the initial value change (which is false).
			.DistinctUntilChanged()
			.Subscribe(
				x =>
				{
					if (!x)
					{
						CloseScreen();
					}
				});
	}

	protected override void OnNavigated(RoutableViewModel? oldPage, bool oldInStack, RoutableViewModel? newPage, bool newInStack)
	{
		base.OnNavigated(oldPage, oldInStack, newPage, newInStack);

		IsDialogOpen = CurrentPage is not null;
	}

	private static void CloseDialogs(IEnumerable<RoutableViewModel> navigationStack)
	{
		// Close all dialogs so the awaited tasks can complete.
		// - DialogViewModelBase.ShowDialogAsync()
		// - DialogViewModelBase.GetDialogResultAsync()

		foreach (var routable in navigationStack)
		{
			if (routable is DialogViewModelBase dialog)
			{
				dialog.IsDialogOpen = false;
			}
		}
	}

	private void CloseScreen()
	{
		var navStack = Stack.ToList();
		Clear();

		CloseDialogs(navStack);
	}
}
