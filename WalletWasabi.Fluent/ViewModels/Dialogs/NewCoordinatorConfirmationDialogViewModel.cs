using System.Windows.Input;
using ReactiveUI;
using WalletWasabi.Fluent.ViewModels.Dialogs.Base;
using WalletWasabi.Models;

namespace WalletWasabi.Fluent.ViewModels.Dialogs;

[NavigationMetaData(Title = "Coordinator detected")]
public partial class NewCoordinatorConfirmationDialogViewModel : DialogViewModelBase<bool>
{
	private NewCoordinatorConfirmationDialogViewModel(CoordinatorConnectionString coordinatorConnection)
	{
		CoordinatorConnection = coordinatorConnection;
		EnableBack = false;
		SetupCancel(enableCancel: false, enableCancelOnEscape: true, enableCancelOnPressed: true);

		NextCommand = ReactiveCommand.Create(() => Close(result: true));
		OpenReadMoreCommand = ReactiveCommand.CreateFromTask(async () => await UiContext.FileSystem.OpenBrowserAsync(coordinatorConnection.ReadMore.ToString()));
	}

	public CoordinatorConnectionString CoordinatorConnection { get; }

	public ICommand OpenReadMoreCommand { get; }
}
