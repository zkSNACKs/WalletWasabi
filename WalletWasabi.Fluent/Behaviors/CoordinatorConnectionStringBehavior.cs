using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Xaml.Interactions.Custom;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.ViewModels.SearchBar.Settings;

namespace WalletWasabi.Fluent.Behaviors;

public class CoordinatorConnectionStringBehavior : DisposingBehavior<Window>
{
	protected override void OnAttached(CompositeDisposable disposables)
	{
		if (AssociatedObject is null)
		{
			return;
		}

		Observable
			.FromEventPattern(AssociatedObject, nameof(AssociatedObject.Activated))
			.DoAsync(async _ =>
			{
				var clipboardValue = await ApplicationHelper.GetTextAsync();
				if (CoordinatorConfigStringHelper.Parse(clipboardValue) is not { } coordinatorConfigString)
				{
					return;
				}

				var uiContext = UiContext.Default;

				if (uiContext.ApplicationSettings.Oobe)
				{
					return;
				}

				// TODO: Check if the detected coordinator is the same

				var accepted = await uiContext.Navigate().To().NewCoordinatorConfirmationDialog(coordinatorConfigString).GetResultAsync();

				if (accepted)
				{
					CoordinatorConfigStringHelper.Process(coordinatorConfigString, uiContext.ApplicationSettings);
					NotificationHelpers.Show(new RestartViewModel("Coordinator saved and will be applied after restarting the application."));
				}
			})
			.Subscribe()
			.DisposeWith(disposables);
	}
}
