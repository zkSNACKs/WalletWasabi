namespace WalletWasabi.BuyAnything;

public enum OrderStatus
{
	Open,
	Done,
	Cancelled,
	InProgress,
};

public enum ConversationStatus
{
	Started,
	OfferReceived,
	OfferAccepted,
	InvoiceReceived,
	PaymentDone,
	PaymentConfirmed
};

public record ConversationId(string WalletId, string EmailAddress, string Password, string OrderId)
{
	public static readonly ConversationId Empty = new("", "", "", "");
}
public record Conversation(ConversationId Id, ChatMessage[] Messages, OrderStatus OrderStatus, ConversationStatus ConversationStatus, object Metadata);
