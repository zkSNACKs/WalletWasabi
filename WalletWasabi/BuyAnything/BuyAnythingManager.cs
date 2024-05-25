using Newtonsoft.Json;
using Nito.AsyncEx;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Bases;
using WalletWasabi.Crypto.Randomness;
using WalletWasabi.Extensions;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Wallets;
using WalletWasabi.WebClients.BuyAnything;
using WalletWasabi.WebClients.ShopWare.Models;

namespace WalletWasabi.BuyAnything;

// Event that is raised when we detect an update in the server
public record ConversationUpdateEvent(Conversation Conversation, DateTimeOffset LastUpdate);

public record Country(string Id, string Name);

[Flags]
internal enum ServerEvent
{
	None = 0, // do not remove this value
	MakeOffer = 1,
	ConfirmPayment = 2,
	InvalidateInvoice = 4,
	ReceiveInvoice = 8,
	FinishConversation = 16,
	ReceiveAttachments = 32,
	SendOrder = 64,
	ReceivePaymentAfterExpiration = 128,
	CloseOfferSuccessfully = 256,
	GenerateNewInvoice = 512,
	CloseCancelled = 1024
}

// Class to manage the conversation updates
public class BuyAnythingManager : PeriodicRunner
{
	public BuyAnythingManager(string dataDir, TimeSpan period, BuyAnythingClient client, bool useTestApi) : base(period)
	{
		Client = client;
		FilePath = Path.Combine(dataDir, "Conversations", useTestApi ? "TestConversations.json" : "Conversations.json");

		string countriesFilePath = Path.Combine(EnvironmentHelpers.GetFullBaseDirectory(), "BuyAnything", "Data", "Countries.json");
		string fileContent = File.ReadAllText(countriesFilePath);
		Country[] countries = [];
		if (!useTestApi)
		{
			countries = JsonConvert.DeserializeObject<Country[]>(fileContent)
								  ?? throw new InvalidOperationException("Couldn't read the countries list.");
		}
		Countries = countries;
	}

	private BuyAnythingClient Client { get; }
	public IReadOnlyList<Country> Countries { get; private set; }
	private ConversationTracking ConversationTracking { get; } = new();
	private bool IsConversationsLoaded { get; set; }
	private string FilePath { get; }

	public event EventHandler<ConversationUpdateEvent>? ConversationUpdated;

	private AsyncLock FileLock { get; } = new();

	protected override async Task ActionAsync(CancellationToken cancel)
	{
		// Load the conversations from the disk in case they were not loaded yet
		await EnsureConversationsAreLoadedAsync(cancel).ConfigureAwait(false);

		// Iterate over the conversations that are updatable
		foreach (var track in ConversationTracking.GetUpdatableConversations())
		{
			try
			{
				// Check if there is new info in the chat
				await CheckUpdateInChatAsync(track, cancel).ConfigureAwait(false);

				// Check if the order state has changed and update the conversation status.
				await CheckUpdateInOrderStatusAsync(track, cancel).ConfigureAwait(false);
			}
			catch (HttpRequestException ex) when (ex.Message.Contains("No matching customer for the email", StringComparison.InvariantCultureIgnoreCase))
			{
				await FinishConversationAsync(track, cancel).ConfigureAwait(false);
			}
		}
	}

	private async Task CheckUpdateInOrderStatusAsync(ConversationUpdateTrack track, CancellationToken cancel)
	{
		var orders = await Client
			.GetOrdersUpdateAsync(track.Credential, cancel)
			.ConfigureAwait(false);

		// There is only one order per customer  and that's why we request all the orders
		// but with only expect to get one. However, it could happen that the order was
		// deleted and then we can have zero orders
		if (orders.FirstOrDefault() is not { } order)
		{
			await FinishConversationAsync(track, cancel).ConfigureAwait(false);
			return;
		}

		var orderCustomFields = order.CustomFields;

		var serverEvent = GetServerEvent(order, track.Conversation.OrderStatus);
		switch (track.Conversation.ConversationStatus)
		{
			// This means that in "lineItems" we have the offer data
			case ConversationStatus.Started
				when serverEvent.HasFlag(ServerEvent.MakeOffer):

				// Update Offer Received metadata value
				track.Conversation = track.Conversation.UpdateMetadata(m => m with { OfferReceived = true });

				await SendSystemChatLinesAsync(track,
					ConvertOfferDetailToMessages(order),
					new OfferCarrier(order.LineItems.Select(x => new OfferItem(x.Quantity, x.Label, x.UnitPrice, x.TotalPrice)), GetShippingCostFromOrder(order)),
					order.UpdatedAt, ConversationStatus.OfferReceived, cancel).ConfigureAwait(false);
				break;

			// Once the user accepts the offer, the system generates a bitcoin address and amount
			case ConversationStatus.OfferAccepted
				when serverEvent.HasFlag(ServerEvent.ReceiveInvoice):
				var invoice = new Invoice(orderCustomFields.Btcpay_PaymentLink, decimal.Parse(orderCustomFields.Btcpay_Amount), orderCustomFields.Btcpay_Destination, false);
				// Remove sending this chat once the UI can handle the track.Invoice and save the track.
				await SendSystemChatLinesAsync(track,
					// $"Pay to: {orderCustomFields.Btcpay_PaymentLink}. The invoice expires in 30 minutes",
					$"To finalize your order, please pay BTC {invoice.Amount} in 30 minutes, the latest by {(DateTimeOffset.Now + TimeSpan.FromMinutes(30)).ToLocalTime():HH:mm}.",
					invoice,
					order.UpdatedAt, ConversationStatus.InvoiceReceived,
					cancel).ConfigureAwait(false);
				break;

			case ConversationStatus.WaitingForInvoice
				when serverEvent.HasFlag(ServerEvent.MakeOffer):
				await HandlePaymentAsync(track, track.Conversation, cancel).ConfigureAwait(false);
				break;

			// The status changes to "In Progress" after the user paid
			case ConversationStatus.InvoiceReceived
				or ConversationStatus.InvoicePaidAfterExpiration // if we paid a bit late but the order was sent, that means everything is alright
				when serverEvent.HasFlag(ServerEvent.ConfirmPayment):
				{
					if (track.Conversation.ChatMessages.LastOrDefault(x => x.Data is Invoice) is { Data: Invoice invoiceData } invoiceMessage)
					{
						var updatedMessageData = invoiceData with { IsPaid = true };
						var updatedMessage = invoiceMessage with { Data = updatedMessageData };

						track.Conversation = track.Conversation.ReplaceMessage(invoiceMessage, updatedMessage);
					}

					await SendSystemChatLinesAsync(track,
						"We received your payment. Thank you! I will keep you updated here on the progress of your order. If you have any questions, feel free to ask here.",
						order.UpdatedAt, ConversationStatus.PaymentConfirmed, cancel).ConfigureAwait(false);
					break;
				}
			// In case the invoice expires we communicate this fact to the chat
			case ConversationStatus.InvoiceReceived
				when serverEvent.HasFlag(ServerEvent.InvalidateInvoice):
				{
					if (track.Conversation.ChatMessages.LastOrDefault(x => x.Data is Invoice) is { Data: Invoice { IsPaid: false } } invoiceMessage)
					{
						var updatedMessage = invoiceMessage with { Data = null };
						track.Conversation = track.Conversation.ReplaceMessage(invoiceMessage, updatedMessage);
					}

					await SendSystemChatLinesAsync(track,
						"Your invoice has expired. If you've already made the payment, please share your Transaction ID with me to assist in finalizing the process.",
						order.UpdatedAt, ConversationStatus.InvoiceExpired, cancel).ConfigureAwait(false);
					break;
				}
			// In case the invoice expires we communicate this fact to the chat
			case ConversationStatus.InvoiceReceived
				when serverEvent.HasFlag(ServerEvent.ReceivePaymentAfterExpiration):
				await SendSystemChatLinesAsync(track,
					"Payment was received after the invoice had expired. If this presents any issue, I will get in contact with you.",
					order.UpdatedAt, ConversationStatus.InvoicePaidAfterExpiration, cancel).ConfigureAwait(false);
				break;

			// In case the invoice expired and a new one can be requested
			case ConversationStatus.InvoiceExpired
				when serverEvent.HasFlag(ServerEvent.GenerateNewInvoice):
				await SendSystemChatLinesAsync(track,
					"Our team is currently working on reactivating the payment process.",
					order.UpdatedAt, ConversationStatus.WaitingForInvoice, cancel).ConfigureAwait(false);
				break;

			// Payment is confirmed and status is SHIPPED the we have a tracking link to display
			case ConversationStatus.PaymentConfirmed
				when serverEvent.HasFlag(ServerEvent.SendOrder):
				{
					var trackingCodes = order.Deliveries.SelectMany(x => x.TrackingCodes).ToArray();

					// We do not step the state machine until the tracking number is added, but only in the case of ConciergeRequest.
					if (trackingCodes.Length == 0 && track.Conversation.MetaData.Product is BuyAnythingClient.Product.ConciergeRequest)
					{
						break;
					}

					// TODO: Sorry Lucas.
					await SendSystemChatLinesAsync(track, "Fantastic! Your order is now completed.", order.UpdatedAt ?? DateTimeOffset.Now, track.Conversation.ConversationStatus, cancel).ConfigureAwait(false);

					// Otherwise not having tracking number is OK.
					if (trackingCodes.Length != 0)
					{
						var newMessage = "Tracking link" + (trackingCodes.Length >= 2 ? "s" : "");
						await SendSystemChatLinesAsync(track,
							$"{newMessage}:\n {string.Join("\n", trackingCodes)}",
							new TrackingCodes(trackingCodes),
							order.UpdatedAt, ConversationStatus.Shipped, cancel).ConfigureAwait(false);
					}
					else
					{
						var updatedConversation = track.Conversation.UpdateStatus(ConversationStatus.Shipped);
						await UpdateConversationOnlyLocallyAsync(updatedConversation, cancel).ConfigureAwait(false);
						ConversationUpdated.SafeInvoke(this, new ConversationUpdateEvent(updatedConversation, order.UpdatedAt ?? DateTimeOffset.Now));
					}
				}
				break;

			// This is an special case when the order is cancelled after the payment was confirmed.
			case ConversationStatus.PaymentConfirmed
				or ConversationStatus.InvoicePaidAfterExpiration
				when serverEvent.HasFlag(ServerEvent.CloseCancelled):
				await SendSystemChatLinesAsync(track,
					"The order was cancelled. Please contact me directly to resolve this issue.",
					order.UpdatedAt, ConversationStatus.Finished, cancel).ConfigureAwait(false);
				break;

			// In case the order was paid and/or shipped and the order is closed containing attachments we send them to the ui
			case ConversationStatus.Shipped or ConversationStatus.PaymentConfirmed
				when serverEvent.HasFlag(ServerEvent.CloseOfferSuccessfully):
				await SendSystemChatLinesAsync(track,
					$"Check the attached file \n {string.Join("\n", GetLinksByLine(orderCustomFields.Concierge_Request_Attachements_Links))}",
					new AttachmentLinks(GetLinksByLine(orderCustomFields.Concierge_Request_Attachements_Links)),
					order.UpdatedAt, ConversationStatus.Finished, cancel).ConfigureAwait(false);
				break;

			//Handle unexpected finished of the conversation
			case not ConversationStatus.Finished
				when serverEvent.HasFlag(ServerEvent.FinishConversation):
				await SendSystemChatLinesAsync(track,
					"Conversation Finished.",
					order.UpdatedAt, ConversationStatus.Finished, cancel).ConfigureAwait(false);
				break;

			case not ConversationStatus.Finished
				when serverEvent.HasFlag(ServerEvent.CloseCancelled):
				await SendSystemChatLinesAsync(track,
					"Conversation Finished (Order cancelled).",
					order.UpdatedAt, ConversationStatus.Finished, cancel).ConfigureAwait(false);
				break;

			default:
				// TODO: Handle unexpected phase changes?
				break;
		}
	}

	private async Task CheckUpdateInChatAsync(ConversationUpdateTrack track, CancellationToken cancel)
	{
		// Get full customer profile to get updated messages.
		var customerProfileResponse = await Client
			.GetCustomerProfileAsync(track.Credential, cancel)
			.ConfigureAwait(false);

		var customer = customerProfileResponse;
		var fullConversation = customerProfileResponse.CustomFields.Wallet_Chat_Store;
		var messages = Chat.FromText(fullConversation, oldConversation: track.Conversation.ChatMessages);
		if (messages.Count > track.Conversation.ChatMessages.Count)
		{
			track.Conversation = track.Conversation with
			{
				ChatMessages = messages,
			};
			ConversationUpdated.SafeInvoke(this,
				new ConversationUpdateEvent(track.Conversation, customer.UpdatedAt ?? DateTimeOffset.Now));

			await SaveAsync(cancel).ConfigureAwait(false);
		}
	}

	public int GetNextConversationId(string walletId) =>
		ConversationTracking.GetNextConversationId(walletId);

	public async Task<Conversation[]> GetConversationsAsync(Wallet wallet, CancellationToken cancellationToken)
	{
		var walletId = GetWalletId(wallet);
		await EnsureConversationsAreLoadedAsync(cancellationToken).ConfigureAwait(false);
		return ConversationTracking.GetConversationsByWalletId(walletId);
	}

	public async Task<int> RemoveConversationsByIdsAsync(IEnumerable<ConversationId> toRemoveIds, CancellationToken cancellationToken)
	{
		await EnsureConversationsAreLoadedAsync(cancellationToken).ConfigureAwait(false);
		var removedCount = ConversationTracking.RemoveAll(x => toRemoveIds.Contains(x.Conversation.Id));
		if (removedCount > 0)
		{
			await SaveAsync(cancellationToken).ConfigureAwait(false);
		}

		return removedCount;
	}

	public async Task<State[]> GetStatesForCountryAsync(Country country, CancellationToken cancellationToken)
	{
		return await Client.GetStatesByCountryIdAsync(country.Id, cancellationToken).ConfigureAwait(false);
	}

	public async Task<Conversation> StartNewConversationAsync(Wallet wallet, Conversation conversation, CancellationToken cancellationToken)
	{
		await EnsureConversationsAreLoadedAsync(cancellationToken).ConfigureAwait(false);

		var country = conversation.MetaData.Country;
		var product = conversation.MetaData.Product;

		if (country is not { } || product is not { })
		{
			throw new ArgumentException("Conversation is missing Country or Product.");
		}

		var fullChat = new Chat(conversation.ChatMessages);
		var credential = GenerateRandomCredential();
		var walletId = GetWalletId(wallet);

		var (orderId, orderNumber) =
			await Client.CreateNewConversationAsync(credential.UserName, credential.Password, country.Id, product.Value, fullChat.ToText(), cancellationToken)
						.ConfigureAwait(false);

		conversation =
			conversation with
			{
				Id = new ConversationId(walletId, credential.UserName, credential.Password, orderId, orderNumber),
				OrderStatus = OrderStatus.Open,
				ConversationStatus = ConversationStatus.Started,
				MetaData = conversation.MetaData with
				{
					Title = $"Order {GetNextConversationId(walletId)}"
				},
			};

		ConversationTracking.Add(new ConversationUpdateTrack(conversation));

		await SaveAsync(cancellationToken).ConfigureAwait(false);

		return conversation;
	}

	public async Task UpdateConversationAsync(Conversation conversation, CancellationToken cancellationToken)
	{
		await EnsureConversationsAreLoadedAsync(cancellationToken).ConfigureAwait(false);
		var track = ConversationTracking.GetConversationTrackById(conversation.Id);

		if (track.Conversation == conversation)
		{
			return;
		}

		track.Conversation = conversation;

		var rawText = track.Conversation.ChatMessages.ToText();
		await Client.UpdateConversationAsync(track.Credential, rawText, cancellationToken).ConfigureAwait(false);

		await SaveAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task<Conversation> AcceptOfferAsync(Conversation conversation, CancellationToken cancellationToken)
	{
		await EnsureConversationsAreLoadedAsync(cancellationToken).ConfigureAwait(false);

		var track = ConversationTracking.GetConversationTrackById(conversation.Id);
		if (track.Conversation.ConversationStatus == ConversationStatus.InvoiceExpired)
		{
			throw new InvalidOperationException("Invoice has expired.");
		}

		var firstName = conversation.MetaData.FirstName;
		var lastName = conversation.MetaData.LastName;
		var streetName = conversation.MetaData.StreetName;
		var houseNumber = conversation.MetaData.HouseNumber;
		var postalCode = conversation.MetaData.PostalCode;
		var city = conversation.MetaData.City;
		var stateId = conversation.MetaData.State?.Id ?? "";
		var country = conversation.MetaData.Country;

		if (firstName is not { } ||
			lastName is not { } ||
			streetName is not { } ||
			houseNumber is not { } ||
			postalCode is not { } ||
			city is not { } ||
			country is not { }
		   )
		{
			throw new ArgumentException($"Conversation {conversation.Id} is missing Delivery information.");
		}

		await Client.SetBillingAddressAsync(track.Credential, firstName, lastName, streetName, houseNumber, postalCode, city, stateId, country.Id, cancellationToken).ConfigureAwait(false);
		return await HandlePaymentAsync(track, conversation, cancellationToken).ConfigureAwait(false);
	}

	private async Task<Conversation> HandlePaymentAsync(ConversationUpdateTrack track, Conversation newConversation,
		CancellationToken cancellationToken)
	{
		await Client.HandlePaymentAsync(track.Credential, track.Conversation.Id.OrderId, cancellationToken)
			.ConfigureAwait(false);
		track.Conversation = newConversation with
		{
			ConversationStatus = ConversationStatus.OfferAccepted
		};

		await SaveAsync(cancellationToken).ConfigureAwait(false);

		return track.Conversation;
	}

	// This method is used to mark conversations as read without sending requests to the web shop.
	// ChatMessage.IsUnread will arrive as false from the ViewModel, all we need to do is update the track and save to disk.
	public async Task UpdateConversationOnlyLocallyAsync(Conversation conversation, CancellationToken cancellationToken)
	{
		await EnsureConversationsAreLoadedAsync(cancellationToken).ConfigureAwait(false);
		var track = ConversationTracking.GetConversationTrackById(conversation.Id);

		if (track.Conversation == conversation)
		{
			return;
		}

		track.Conversation = track.Conversation with
		{
			ChatMessages = new(conversation.ChatMessages),
			MetaData = conversation.MetaData,
		};

		await SaveAsync(cancellationToken).ConfigureAwait(false);
	}

	private static OrderStatus GetOrderStatus(Order order)
	{
		var orderStatus = order.StateMachineState.Name switch
		{
			"Open" => OrderStatus.Open,
			"In Progress" => OrderStatus.InProgress,
			"Cancelled" => OrderStatus.Cancelled,
			"Done" => OrderStatus.Done,
			_ => throw new ArgumentException($"Unexpected {order.StateMachineState.Name} status.")
		};
		return orderStatus;
	}

	private ServerEvent GetServerEvent(Order order, OrderStatus currentOrderStatus)
	{
		ServerEvent events = 0;
		if (order.CustomFields?.Concierge_Request_Status_State == "OFFER")
		{
			events |= ServerEvent.MakeOffer;
		}

		var orderStatus = GetOrderStatus(order);
		if (orderStatus != currentOrderStatus) // the order status changed
		{
			if (orderStatus == OrderStatus.InProgress && currentOrderStatus == OrderStatus.Open)
			{
				events |= ServerEvent.ConfirmPayment;
			}

			if (orderStatus == OrderStatus.Done)
			{
				if (!string.IsNullOrWhiteSpace(order.CustomFields?.Concierge_Request_Attachements_Links))
				{
					events |= ServerEvent.CloseOfferSuccessfully;
				}
				else
				{
					events |= ServerEvent.FinishConversation;
				}
			}
			else if (orderStatus == OrderStatus.Cancelled)
			{
				events |= ServerEvent.CloseCancelled;
			}
		}

		if (order.CustomFields?.BtcpayOrderStatus == "invoiceExpired")
		{
			if (order.CustomFields.Concierge_Request_Status_State == "CLAIMED")
			{
				events |= ServerEvent.GenerateNewInvoice;
			}
			else if (order.CustomFields.PaidAfterExpiration)
			{
				events |= ServerEvent.ReceivePaymentAfterExpiration;
			}
			else
			{
				events |= ServerEvent.InvalidateInvoice;
			}
		}

		if (!string.IsNullOrWhiteSpace(order.CustomFields?.Btcpay_PaymentLink) && order.CustomFields?.BtcpayOrderStatus != "invoiceExpired")
		{
			events |= ServerEvent.ReceiveInvoice;
		}

		if (!string.IsNullOrWhiteSpace(order.CustomFields?.Concierge_Request_Attachements_Links))
		{
			events |= ServerEvent.ReceiveAttachments;
		}

		if (order.Deliveries.Any(d => d.StateMachineState.Name == "Shipped"))
		{
			events |= ServerEvent.SendOrder;
		}
		return events;
	}

	private Task SendSystemChatLinesAsync(ConversationUpdateTrack track, string message, DateTimeOffset? updatedAt,
		ConversationStatus newStatus, CancellationToken cancellationToken) =>
		SendSystemChatLinesAsync(track, message, DataCarrier.NoData, updatedAt, newStatus, cancellationToken);

	private async Task SendSystemChatLinesAsync(ConversationUpdateTrack track, string message, DataCarrier data, DateTimeOffset? updatedAt, ConversationStatus newStatus, CancellationToken cancellationToken)
	{
		var updatedConversation = track.Conversation.AddSystemChatLine(message, data, newStatus);
		await UpdateConversationAsync(updatedConversation, cancellationToken).ConfigureAwait(false);
		ConversationUpdated.SafeInvoke(this, new ConversationUpdateEvent(updatedConversation, updatedAt ?? DateTimeOffset.Now));
	}

	private async Task FinishConversationAsync(ConversationUpdateTrack track, CancellationToken cancellationToken)
	{
		await SendSystemChatLinesAsync(track,
			$"Live support for your order has now concluded. If you require any additional assistance, please don't hesitate to contact us and mention your order number: {track.Conversation.Id.OrderNumber}",
			DateTimeOffset.Now, ConversationStatus.Deleted, cancellationToken).ConfigureAwait(false);
	}

	private async Task SaveAsync(CancellationToken cancellationToken)
	{
		JsonSerializerSettings settings = new()
		{
			TypeNameHandling = TypeNameHandling.Objects
		};
		using (await FileLock.LockAsync(cancellationToken).ConfigureAwait(false))
		{
			IoHelpers.EnsureFileExists(FilePath);
			string json = JsonConvert.SerializeObject(ConversationTracking, Formatting.Indented, settings);
			await File.WriteAllTextAsync(FilePath, json, cancellationToken).ConfigureAwait(false);
		}
	}

	private static string GetWalletId(Wallet wallet) =>
		wallet.KeyManager.MasterFingerprint is { } masterFingerprint
			? masterFingerprint.ToString()
			: "readonly wallet";

	private string[] GetLinksByLine(string attachmentsLinks) =>
		attachmentsLinks.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	private static string ConvertOfferDetailToMessages(Order order)
	{
		StringBuilder sb = new();

		var shippingCost = GetShippingCostFromOrder(order);
		sb.AppendLine("Our offer includes:");
		foreach (var lineItem in order.LineItems)
		{
			if (lineItem.Quantity > 1)
			{
				sb.AppendLine($"{lineItem.Quantity} {lineItem.Label} for ${lineItem.UnitPrice}/item (${lineItem.TotalPrice} total).");
			}
			else
			{
				sb.AppendLine($"A {lineItem.Label} for ${lineItem.UnitPrice}.");
			}
		}

		sb.AppendLine($"\nFor a total price of ${order.AmountTotal}.");
		sb.AppendLine($"(Including ${shippingCost.TotalPrice} shipping cost.)");
		return sb.ToString();
	}

	private static ShippingCosts GetShippingCostFromOrder(Order order)
	{
		if (order.ShippingCosts.TotalPrice != "0")
		{
			return order.ShippingCosts;
		}

		float sum = 0;
		foreach (var delivery in order.Deliveries)
		{
			sum += float.Parse(delivery.ShippingCosts.TotalPrice);
		}
		return new ShippingCosts(sum.ToString());
	}

	public async Task EnsureConversationsAreLoadedAsync(CancellationToken cancellationToken)
	{
		if (!IsConversationsLoaded)
		{
			await LoadConversationsAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	private async Task LoadConversationsAsync(CancellationToken cancellationToken)
	{
		using (await FileLock.LockAsync(cancellationToken).ConfigureAwait(false))
		{
			IoHelpers.EnsureFileExists(FilePath);

			try
			{
				JsonSerializerSettings settings = new()
				{
					TypeNameHandling = TypeNameHandling.Objects
				};

				string json = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
				var conversations = JsonConvert.DeserializeObject<ConversationTracking>(json, settings) ?? new();
				ConversationTracking.Load(conversations);
			}
			catch (JsonException ex)
			{
				// Something happened with the file.
				var bakFilePath = $"{FilePath}.bak";
				Logger.LogError($"Wasabi was not able to load conversations file. Resetting the conversations and backup the corrupted file to: '{bakFilePath}'. Reason: '{ex}'.");
				File.Move(FilePath, bakFilePath, true);
				ConversationTracking.Load(new ConversationTracking());
				await SaveAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Logger.LogError($"Wasabi was not able to load conversations file. Reason: '{ex}'.");
			}
		}

		IsConversationsLoaded = true;
	}

	private NetworkCredential GenerateRandomCredential() =>
		new(
			userName: $"{Guid.NewGuid()}@me.com",
			password: RandomString.AlphaNumeric(25, secureRandom: true));

	public async Task EnsureCountriesAreLoadedAsync(CancellationToken cancel)
	{
		if (!Countries.Any())
		{
			try
			{
				Countries = await Client.GetCountriesAsync(cancel).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Logger.LogError($"Failed to download countries. Reason: '{ex.Message}'.");
			}
		}
	}
}
