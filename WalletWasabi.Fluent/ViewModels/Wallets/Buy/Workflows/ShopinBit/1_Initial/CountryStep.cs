using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using ReactiveUI;
using WalletWasabi.BuyAnything;
using WalletWasabi.Extensions;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Buy.Workflows;

/// <summary>
/// ShopinBit Step #2: Select Country
/// </summary>
public class CountryStep : WorkflowStep<Country>
{
	public CountryStep(Conversation conversation, IReadOnlyList<Country> countries, CancellationToken token) : base(conversation, token)
	{
		Watermark = "Type in a country...";
		Countries = new ObservableCollection<string>(countries.Select(x => x.Name));

		// TODO: TagsBox provide a list as result, so it cannot be directly bound to Value. Fede, better idea?
		this.WhenAnyValue(x => x.SelectedCountries.Count)
			.Select(_ => SelectedCountries.FirstOrDefault())
			.Select(countryString => countries.FirstOrDefault(x => x.Name == countryString))
			.BindTo(this, x => x.Value);
	}

	public ObservableCollection<string> Countries { get; }

	public ObservableCollection<string> SelectedCountries { get; } = new();

	protected override IEnumerable<string> BotMessages(Conversation conversation)
	{
		// Assistant greeting, min order limit
		yield return $"Hello, I am your {GetAssistantName(conversation)}.\nFor now, the MINIMUM ORDER VALUE is USD 1,000 and we only accept requests for LEGAL goods or services.";

		// Ask for Location
		yield return "If your order involves shipping, provide the destination country. For non-shipping orders, specify your nationality.";
	}

	protected override Conversation PutValue(Conversation conversation, Country value) =>
		conversation.UpdateMetadata(x => x with { Country = value });

	protected override Country? RetrieveValue(Conversation conversation) =>
		conversation.MetaData.Country;

	protected override string StringValue(Country value) =>
		value.Name;

	private string GetAssistantName(Conversation conversation) =>
		conversation.MetaData.Product?.GetDescription() ?? "Assistant";
}
