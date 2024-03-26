using ReactiveUI;

namespace WalletWasabi.Fluent.ViewModels.AI.Messages;

public abstract partial class MessageViewModel(string message) : ReactiveObject
{
	[AutoNotify] private string _message = message;
}
