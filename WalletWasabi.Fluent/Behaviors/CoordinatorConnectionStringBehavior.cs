using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Xaml.Interactions.Custom;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.ViewModels.Dialogs;
using WalletWasabi.Helpers;

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
			.Do(_ =>
			{
				if (uiContext.Navigate().DialogScreen.CurrentPage is NewCoordinatorConfirmationDialogViewModel currentDialog)
				{
					currentDialog.CancelCommand.ExecuteIfCan();
				}
			})
			.DoAsync(async _ =>
			{
				var clipboardValue = await ApplicationHelper.GetTextAsync();
				if (CoordinatorConfigStringHelper.Parse(clipboardValue) is not { } coordinatorConfigString)
				{
					return;
				}

				if (CoordinatorConfigStringHelper.DoesntChangeAnything(coordinatorConfigString))
				{
					return;
				}
				var accepted = await uiContext.Navigate().To().NewCoordinatorConfirmationDialog(coordinatorConfigString).GetResultAsync();
				if (!accepted)
				{
					return;
				}

				if (!uiContext.ApplicationSettings.TryProcessCoordinatorConfigString(coordinatorConfigString))
				{
					uiContext.Navigate().To().ShowErrorDialog(
						message: "Some of the values were incorrect. See logs for more details.",
						title: "Coordinator detected",
						caption: "");

					return;
				}
			})
			.Subscribe()
			.DisposeWith(disposables);
	}
}
