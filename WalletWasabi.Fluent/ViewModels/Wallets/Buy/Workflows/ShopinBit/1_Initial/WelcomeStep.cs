using ReactiveUI;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using WalletWasabi.BuyAnything;
using WalletWasabi.Extensions;
using WalletWasabi.Fluent.Models;
using WalletWasabi.WebClients.BuyAnything;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Buy.Workflows;

/// <summary>
/// ShopinBit Step #1: Welcome and Select Assistant Type
/// </summary>
public partial class WelcomeStep : WorkflowStep<BuyAnythingClient.Product?>
{
	public const string ServiceDescriptionUrl = "https://wasabiwallet.io/buy-anything.html";

	[AutoNotify] private EnumValue<BuyAnythingClient.Product>? _product;

	public WelcomeStep(Conversation conversation, CancellationToken token) : base(conversation, token)
	{
		var productsEnum = Enum.GetValues<BuyAnythingClient.Product>();

		Products = new(productsEnum.Select(x => new EnumValue<BuyAnythingClient.Product>(x, x.GetDescription() ?? "")));
		_product = Products.FirstOrDefault(x => x.Value == Value) ?? Products.FirstOrDefault();
		
		this.WhenAnyValue(x => x.Product)
			.Select(x => x?.Value)
			.BindTo(this, x => x.Value);
	}

	public ObservableCollection<EnumValue<BuyAnythingClient.Product>> Products { get; }

	protected override IEnumerable<string> BotMessages(Conversation conversation)
	{
		yield return $"Please select the assistant that best fits your needs.\nRead more about them here:\n{ServiceDescriptionUrl}";

		// All-Purpose Concierge Assistant
		yield return "All-Purpose Concierge Assistant\n\nFor a wide range of purchases, from vehicles to tech gadgets and more.";

		// Fast Travel Assistant
		yield return "Fast Travel Assistant\n\nIf you've a specific flight or hotel in mind and need quick assistance with booking.";

		// General Travel Assistant
		yield return "General Travel Assistant\n\nIf you're just starting to plan your travel and don't have any details yet.";
	}

	protected override BuyAnythingClient.Product? RetrieveValue(Conversation conversation) =>
		conversation.MetaData.Product;

	protected override Conversation PutValue(Conversation conversation, BuyAnythingClient.Product? value) =>
		conversation.UpdateMetadata(x => x with { Product = value });

	protected override string? StringValue(BuyAnythingClient.Product? value) =>
		value?.GetDescription();
}
