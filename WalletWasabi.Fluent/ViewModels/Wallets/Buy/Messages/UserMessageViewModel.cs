using ReactiveUI;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using WalletWasabi.BuyAnything;
using WalletWasabi.Fluent.ViewModels.Wallets.Buy.Workflows;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Buy.Messages;

public partial class UserMessageViewModel : MessageViewModel
{
	public UserMessageViewModel(Workflow workflow, ChatMessage message) : base(message)
	{
		Workflow = workflow;

		CanEditObservable =
			this.WhenAnyValue(x => x.Workflow.Conversation).Select(_ => Workflow.MessageEditor.IsEditable(message))
				.CombineLatest(this.WhenAnyValue(x => x.Workflow.CurrentStep).WhereNotNull().Select(x => x.IsEditing))
				.Select(tup => tup.First && !tup.Second);

		EditCommand = ReactiveCommand.CreateFromTask(() => EditAsync(message), CanEditObservable);
	}

	public IObservable<bool> CanEditObservable { get; }

	public ICommand EditCommand { get; }

	// used just to be able to use this.WhenAnyValue()
	private Workflow Workflow { get; }

	private async Task EditAsync(ChatMessage message)
	{
		var editor = Workflow.MessageEditor.Get(message);

		if (editor is null)
		{
			return;
		}

		var currentStep = Workflow.CurrentStep;
		Workflow.CurrentStep = editor;

		var newMessage = await editor.EditMessageAsync(message);

		Message = newMessage;
		IsUnread = newMessage.IsUnread;
		OriginalText = newMessage.Text;
		UiMessage = ParseRawMessage(newMessage);

		Workflow.CurrentStep = currentStep;
		Workflow.Conversation = editor.Conversation;
	}
}
