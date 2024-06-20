using System.Linq;

namespace WalletWasabi.BuyAnything;

public static class ConversationExtensions
{
	public static bool IsCompleted(this Conversation conversation)
	{
		return conversation.OrderStatus == OrderStatus.Done;
	}

	public static bool IsUpdatable(this Conversation conversation) => conversation.ConversationStatus != ConversationStatus.Deleted;

	public static Conversation AddSystemChatLine(this Conversation conversation, string message, DataCarrier data,
		ConversationStatus newStatus) =>
		conversation with
		{
			ChatMessages = new(conversation.ChatMessages.Append(new ChatMessage(MessageSource.Bot, message, isUnread: true, null, data))),
			ConversationStatus = newStatus
		};

	public static Conversation AddUserMessage(this Conversation conversation, string msg, string? stepName = null) =>
		conversation with { ChatMessages = new(conversation.ChatMessages.Append(new ChatMessage(MessageSource.User, msg, isUnread: false, stepName))) };

	public static Conversation AddBotMessage(this Conversation conversation, string msg, string? stepName = null, bool isUnread = true) =>
		conversation with { ChatMessages = new(conversation.ChatMessages.Append(new ChatMessage(MessageSource.Bot, msg, isUnread: isUnread, stepName))) };

	public static Conversation AddAgentMessage(this Conversation conversation, string msg) =>
		conversation with { ChatMessages = new(conversation.ChatMessages.Append(new ChatMessage(MessageSource.Agent, msg, isUnread: true, null))) };

	public static Conversation UpdateMetadata(this Conversation conversation, Func<ConversationMetaData, ConversationMetaData> updateMetadata)
	{
		return conversation with { MetaData = updateMetadata(conversation.MetaData) };
	}

	public static Conversation UpdateStatus(this Conversation conversation, ConversationStatus newStatus) =>
		conversation with { ConversationStatus = newStatus };

	public static Conversation MarkAsRead(this Conversation conversation)
	{
		return conversation with
		{
			ChatMessages = new(conversation.ChatMessages.Select(m => m with { IsUnread = false }))
		};
	}

	public static Conversation ReplaceMessage(this Conversation conversation, ChatMessage message, ChatMessage newMessage)
	{
		var messageList = conversation.ChatMessages.ToList();

		var index = messageList.IndexOf(message);
		messageList[index] = newMessage;

		return conversation with { ChatMessages = new(messageList) };
	}
}
