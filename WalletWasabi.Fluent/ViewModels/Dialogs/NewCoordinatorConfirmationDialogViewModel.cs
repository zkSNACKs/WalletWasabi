using ReactiveUI;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.ViewModels.Dialogs.Base;

namespace WalletWasabi.Fluent.ViewModels.Dialogs;

[NavigationMetaData(Title = "Coordinator detected")]
public partial class NewCoordinatorConfirmationDialogViewModel : DialogViewModelBase<bool>
{
	public NewCoordinatorConfirmationDialogViewModel(CoordinatorConfigStringHelper.CoordinatorConfigString coordinatorConfig)
	{
		CoordinatorConfig = coordinatorConfig;
		EnableBack = false;
		SetupCancel(enableCancel: false, enableCancelOnEscape: true, enableCancelOnPressed: true);

		NextCommand = ReactiveCommand.Create(() => Close(result: true));
	}

	public CoordinatorConfigStringHelper.CoordinatorConfigString CoordinatorConfig { get; }
}
