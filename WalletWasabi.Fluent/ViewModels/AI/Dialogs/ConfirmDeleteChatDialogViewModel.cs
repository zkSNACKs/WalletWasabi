using ReactiveUI;
using WalletWasabi.Fluent.ViewModels.Dialogs.Base;

namespace WalletWasabi.Fluent.ViewModels.AI.Dialogs;

[NavigationMetaData(Title = "Delete Chat", NavigationTarget = NavigationTarget.CompactDialogScreen)]
public partial class ConfirmDeleteChatDialogViewModel : DialogViewModelBase<bool>
{
	public ConfirmDeleteChatDialogViewModel(ChatViewModel chat)
	{
		Chat = chat;

		NextCommand = ReactiveCommand.Create(() => Close(result: true));
		CancelCommand = ReactiveCommand.Create(() => Close(DialogResultKind.Cancel));

		SetupCancel(enableCancel: false, enableCancelOnEscape: true, enableCancelOnPressed: true);
	}

	public ChatViewModel Chat { get; }
}
