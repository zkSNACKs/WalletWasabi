using ReactiveUI;
using WalletWasabi.BuyAnything;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Buy.Messages;

public abstract partial class MessageViewModel : ReactiveObject
{
	[AutoNotify(SetterModifier = AccessModifier.Protected)] private ChatMessage _message;
	[AutoNotify] private string? _id;
	[AutoNotify] private string? _uiMessage;
	[AutoNotify] private bool _isUnread;

	protected MessageViewModel(ChatMessage message)
	{
		_message = message;
		IsUnread = message.IsUnread;
		OriginalText = message.Text;
		UiMessage = ParseRawMessage(message);
	}

	public string? OriginalText { get; set; }

	public string ParseRawMessage(ChatMessage message)
	{
		string raw = message.Text;

		// Check if the string starts with '@'
		if (raw.StartsWith('@'))
		{
			// Find the index of the second '@'
			int secondAt = raw.IndexOf('@', 1);

			if (secondAt != -1 && long.TryParse(raw[1..secondAt], out _))
			{
				// Take the substring starting after the second '@'
				raw = raw[(secondAt + 1)..];
			}
		}

		return raw;
	}
}
