using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Xaml.Interactions.Custom;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.ViewModels.Dialogs;
using WalletWasabi.Helpers;
using WalletWasabi.Models;

namespace WalletWasabi.Fluent.Behaviors;

public class CoordinatorConnectionStringBehavior : DisposingBehavior<Window>
{

	protected override void OnAttached(CompositeDisposable disposables)
	{
		if (AssociatedObject is null)
		{
			return;
		}

		var uiContext = UiContext.Default;

		Observable
			.FromEventPattern(AssociatedObject, nameof(AssociatedObject.Activated))
			.Where(_ => !uiContext.ApplicationSettings.Oobe)
			.SelectMany(async _ =>
			{
				var clipboardValue = await ApplicationHelper.GetTextAsync();

				if (uiContext.Navigate().DialogScreen.CurrentPage is not NewCoordinatorConfirmationDialogViewModel currentDialog)
				{
					return (clipboardValue, null);
				}

				if (!CoordinatorConnectionString.TryParse(clipboardValue, out var coordinatorConnectionString))
				{
					return (clipboardValue, null);
				}

				currentDialog.CancelCommand.ExecuteIfCan();
				return (clipboardValue, coordinatorConnectionString);

			})
			.DoAsync(async result =>
			{
				if (result.coordinatorConnectionString is null && !CoordinatorConnectionString.TryParse(result.clipboardValue, out result.coordinatorConnectionString))
				{
					return;
				}

				var accepted = await uiContext.Navigate().To().NewCoordinatorConfirmationDialog(result.coordinatorConnectionString).GetResultAsync();
				if (!accepted)
				{
					return;
				}

				if (!uiContext.ApplicationSettings.TryProcessCoordinatorConnectionString(result.coordinatorConnectionString))
				{
					uiContext.Navigate().To().ShowErrorDialog(
						message: "Some of the values were incorrect. See logs for more details.",
						title: "Coordinator detected",
						caption: "");
				}
			})
			.Subscribe()
			.DisposeWith(disposables);
	}
}
