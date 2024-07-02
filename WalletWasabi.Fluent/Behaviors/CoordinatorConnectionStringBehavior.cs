using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Xaml.Interactions.Custom;
using ReactiveUI;
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
				var clipboardValue = await uiContext.Clipboard.GetTextAsync();

				if (!CoordinatorConnectionString.TryParse(clipboardValue, out var coordinatorConnectionString))
				{
					return null;
				}

				await uiContext.Clipboard.ClearAsync();

				if (uiContext.Navigate().DialogScreen.CurrentPage is NewCoordinatorConfirmationDialogViewModel currentDialog)
				{
					if (currentDialog.CoordinatorConnection.ToString() == coordinatorConnectionString.ToString())
					{
						return null;
					}

					currentDialog.CancelCommand.ExecuteIfCan();
				}

				return coordinatorConnectionString;
			})
			.WhereNotNull()
			.DoAsync(async coordinatorConnectionString =>
			{
				var accepted = await uiContext.Navigate().To().NewCoordinatorConfirmationDialog(coordinatorConnectionString).GetResultAsync();
				if (!accepted)
				{
					return;
				}

				if (!uiContext.ApplicationSettings.TryProcessCoordinatorConnectionString(coordinatorConnectionString))
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
