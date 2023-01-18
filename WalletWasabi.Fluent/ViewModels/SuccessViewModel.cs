using CommunityToolkit.Mvvm.Input;
using ReactiveUI;
using WalletWasabi.Fluent.ViewModels.Navigation;

namespace WalletWasabi.Fluent.ViewModels;

[NavigationMetaData(Title = "Success")]
public partial class SuccessViewModel : RoutableViewModel
{
	public SuccessViewModel(string successText)
	{
		SuccessText = successText;
		NextCommand = new RelayCommand(() => Navigate().Clear());

		SetupCancel(enableCancel: false, enableCancelOnEscape: true, enableCancelOnPressed: true);
	}

	public string SuccessText { get; }
}
