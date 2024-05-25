using System.Collections.Generic;
using System.Linq;
using WalletWasabi.BuyAnything;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Buy.Messages;

public class OfferMessageViewModel : AssistantMessageViewModel
{
	public OfferMessageViewModel(ChatMessage message) : base(message)
	{
		if (message.Data is not OfferCarrier carrier)
		{
			throw new InvalidOperationException($"Invalid Data Type: {message.Data?.GetType().Name}");
		}

		OfferCarrier = carrier;

		Items = new List<OfferItem>(OfferCarrier.Items);

		var shippingCost = float.Parse(carrier.ShippingCost.TotalPrice);
		if (shippingCost > 0)
		{
			Items.Add(new OfferItem(1, "Shipping Cost", shippingCost, shippingCost));
		}

		var total = Items.Sum(x => x.TotalPrice);

		TotalMessage = $"For a total price of USD {total}.";
		UiMessage = "Our offer includes:";
	}

	public OfferCarrier OfferCarrier { get; }

	public List<OfferItem> Items { get; }

	public string TotalMessage { get; }
}
