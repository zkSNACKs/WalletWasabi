using CommunityToolkit.Mvvm.Input;
using ReactiveUI;
using WalletWasabi.Fluent.ViewModels.Dialogs.Base;

namespace WalletWasabi.Fluent.ViewModels.Dialogs;

public class ShowErrorDialogViewModel : DialogViewModelBase<bool>
{
	private string _title;

	public ShowErrorDialogViewModel(string message, string title, string caption)
	{
		Message = message;
		_title = title;
		Caption = caption;

		NextCommand = new RelayCommand(() => Close());

		SetupCancel(enableCancel: false, enableCancelOnEscape: true, enableCancelOnPressed: true);
	}

	public string Message { get; }

	public string Caption { get; }

	public override string Title
	{
		get => _title;
		protected set => SetProperty(ref _title, value);
	}
}
