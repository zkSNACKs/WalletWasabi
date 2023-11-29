using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Bases;
using WalletWasabi.Extensions;
using WalletWasabi.Helpers;
using WalletWasabi.Wallets;
using WalletWasabi.WebClients.BuyAnything;

namespace WalletWasabi.BuyAnything;


// Event that is raised when we detect an update in the server
public record ConversationUpdateEvent(Conversation Conversation, DateTimeOffset LastUpdate);

public record ChatMessage(bool IsMyMessage, string Message);

// Class to keep a track of the last update of a conversation
public class ConversationUpdateTrack
{
	public ConversationUpdateTrack(Conversation conversation)
	{
		Conversation = conversation;
	}

	public DateTimeOffset LastUpdate { get; set; }
	public Conversation Conversation { get; set; }
	public bool IsUpdatable =>
		Conversation.Status == ConversationStatus.WaitingForUpdates ||
		Conversation.Status == ConversationStatus.Started;
}

// Class to manage the conversation updates
public class BuyAnythingManager : PeriodicRunner
{
	public BuyAnythingManager(string dataDir, TimeSpan period, BuyAnythingClient client) : base(period)
	{
		Client = client;
		FilePath = Path.Combine(dataDir, "Conversations", "Conversations.json");
	}

	private BuyAnythingClient Client { get; }
	private List<ConversationUpdateTrack> Conversations { get; } = new();
	private string FilePath { get; }

	public event EventHandler<ConversationUpdateEvent>? ConversationUpdated;

	protected override async Task ActionAsync(CancellationToken cancel)
	{
		foreach (var track in Conversations.Where(c => c.IsUpdatable))
		{
			var orders = await Client
				.GetConversationsUpdateSinceAsync(track.Conversation.Id.ContextToken, track.LastUpdate, cancel)
				.ConfigureAwait(false);

			foreach (var order in orders.Where(o => o.UpdatedAt.HasValue && o.UpdatedAt!.Value > track.LastUpdate))
			{
				var orderLastUpdated = order.UpdatedAt!.Value;
				track.LastUpdate = orderLastUpdated;

				// Update the conversation status according to the order state
				// TODO: Verify if the state machine is values match reality
				var status = order.StateMachineState.Name switch
				{
					"Cancelled" => ConversationStatus.Cancelled,
					"Completed" => ConversationStatus.Finished,
					"InProgress" => ConversationStatus.WaitingForUpdates,
					_ => track.Conversation.Status
				};

				var newMessageFromConcierge = Parse(order.CustomerComment ?? "");

				track.Conversation = track.Conversation with
				{
					Messages = newMessageFromConcierge.ToArray(),
					Status = status != track.Conversation.Status ? status : track.Conversation.Status
				};
				ConversationUpdated.SafeInvoke(this, new ConversationUpdateEvent(track.Conversation, orderLastUpdated));
			}
		}
	}

	public IEnumerable<Conversation> GetConversations(Wallet wallet)
	{
		var walletId = GetWalletId(wallet);
		return Conversations
			.Where(c => c.Conversation.Id.WalletId == walletId)
			.Select(c => c.Conversation);
	}

	public async Task StartNewConversationAsync(string walletId, string countryId, string message, CancellationToken cancellationToken)
	{
		var ctxToken =  await Client.CreateNewConversationAsync(countryId, BuyAnythingClient.Product.ConciergeRequest, message, cancellationToken)
			.ConfigureAwait(false);

		Conversations.Add(new ConversationUpdateTrack(
			new Conversation(
				new ConversationId(walletId, ctxToken),
				new []{ new ChatMessage(true, message) },
				ConversationStatus.Started,
				new object())));

		await SaveAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task UpdateConversationAsync(ConversationId conversationId, string newMessage, object metadata, CancellationToken cancellationToken)
	{
		if (Conversations.FirstOrDefault(c => c.Conversation.Id == conversationId) is { } track)
		{
			track.Conversation = track.Conversation with
			{
				Messages = track.Conversation.Messages.Append(new ChatMessage(false, newMessage)).ToArray(),
				Metadata = metadata,
				Status = ConversationStatus.WaitingForUpdates
			};
			track.LastUpdate = DateTimeOffset.Now;

			var rawText = ConvertToCustomerComment(track.Conversation.Messages);
			await Client.UpdateConversationAsync(conversationId.ContextToken, rawText).ConfigureAwait(false);

			await SaveAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	private IEnumerable<ChatMessage> Parse(string customerComment)
	{
		var messages = customerComment.Split("||", StringSplitOptions.RemoveEmptyEntries);

		foreach (var message in messages)
		{
			var items = message.Split("#", StringSplitOptions.RemoveEmptyEntries);

			if (items.Length != 2)
			{
				yield break;
			}

			var isMine = items[0] == "WASABI";
			var text = items[1];
			yield return new ChatMessage(isMine, text);
		}
	}

	private static string ConvertToCustomerComment(IEnumerable<ChatMessage> cleanChatMessages)
	{
		StringBuilder result = new();

		foreach (var chatMessage in cleanChatMessages)
		{
			var prefix = chatMessage.IsMyMessage ? "WASABI" : "SIB";
			result.Append($"||#{prefix}#{chatMessage.Message}");
		}

		result.Append("||");

		return result.ToString();
	}

	private async Task SaveAsync(CancellationToken cancellationToken)
	{
		IoHelpers.EnsureFileExists(FilePath);
		string json = JsonConvert.SerializeObject(Conversations, Formatting.Indented);
		await File.WriteAllTextAsync(FilePath, json, cancellationToken).ConfigureAwait(false);
	}

	public static string GetWalletId (Wallet wallet) =>
		wallet.KeyManager.MasterFingerprint is { } masterFingerprint
			? masterFingerprint.ToString()
			: "readonly wallet";
}