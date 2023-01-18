using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using ReactiveUI;
using WalletWasabi.Helpers;

namespace WalletWasabi.Fluent.ViewModels.OpenDirectory;

[NavigationMetaData(
	Title = "Data Folder",
	Caption = "",
	Order = 0,
	Category = "Open",
	Keywords = new[]
	{
			"Browse", "Open", "Data", "Folder"
	},
	IconName = "folder_regular")]
public partial class OpenDataFolderViewModel : TriggerCommandViewModel
{
	public OpenDataFolderViewModel()
	{
		TargetCommand = new RelayCommand(() => IoHelpers.OpenFolderInFileExplorer(Services.DataDir));
	}

	public override ICommand TargetCommand { get; }
}
