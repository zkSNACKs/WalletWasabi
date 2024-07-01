using System.Reactive.Concurrency;
using ReactiveUI;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.ViewModels.Dialogs.Base;
using WalletWasabi.Models;

namespace WalletWasabi.Fluent.ViewModels.Dialogs;

[NavigationMetaData(Title = "Coordinator detected")]
public partial class NewCoordinatorConfirmationDialogViewModel : DialogViewModelBase<bool>
{
	public NewCoordinatorConfirmationDialogViewModel(CoordinatorConfigString coordinatorConfig)
	{
		CoordinatorConfig = coordinatorConfig;
		EnableBack = false;
		SetupCancel(enableCancel: false, enableCancelOnEscape: true, enableCancelOnPressed: true);

		NextCommand = ReactiveCommand.Create(() => Close(result: true));

		RxApp.MainThreadScheduler.Schedule(ClearClipboardAsync);
	}

	private async void ClearClipboardAsync()
	{
		await ApplicationHelper.SetTextAsync("");
	}

	public CoordinatorConfigString CoordinatorConfig { get; }
}
