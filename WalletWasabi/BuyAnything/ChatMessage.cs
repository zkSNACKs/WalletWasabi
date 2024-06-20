using System;
using System.Collections.Generic;
using System.Text;
using WalletWasabi.WebClients.ShopWare.Models;

namespace WalletWasabi.BuyAnything;

public enum MessageSource
{
	User = 1,
	Agent = 2,
	Bot = 3
}

public record Invoice(string Bip21Link, decimal Amount, string BitcoinAddress, bool IsPaid) : DataCarrier;

public record OfferItem(float Quantity, string Description, float UnitPrice, float TotalPrice);

public record OfferCarrier(IEnumerable<OfferItem> Items, ShippingCosts ShippingCost) : DataCarrier;

public record TrackingCodes(IEnumerable<string> Codes) : DataCarrier;

public record AttachmentLinks(IEnumerable<string> Links) : DataCarrier;

public record NoData : DataCarrier;

public abstract record DataCarrier
{
	public static readonly DataCarrier NoData = new NoData();
}

public record ChatMessage
{
	public ChatMessage(MessageSource source, string text, bool isUnread, string? stepName, DataCarrier? data = null)
	{
		Source = source;
		Text = AddUnixTimestampIfMissing(text);
		IsUnread = isUnread;
		StepName = stepName;
		Data = data;
		CreatedAt = ReadUnixTimestamp();
	}

	public bool IsMyMessage => Source == MessageSource.User;
	public MessageSource Source { get; }
	public string Text { get; set; }
	public bool IsUnread { get; set; }
	public string? StepName { get; }
	public DataCarrier? Data { get; set; }
	public DateTimeOffset CreatedAt { get; }

	private DateTimeOffset ReadUnixTimestamp()
	{
		int secondAt = Text.IndexOf('@', 1);

		if (long.TryParse(Text[1..secondAt], out long unixTimestamp))
		{
			DateTimeOffset dateTime = DateTimeOffset.FromUnixTimeMilliseconds(unixTimestamp).ToLocalTime();
			return dateTime;
		}

		return DateTimeOffset.UtcNow;
	}

	private string AddUnixTimestampIfMissing(string text)
	{
		if (!text.StartsWith('@'))
		{
			var now = DateTimeOffset.UtcNow;
			StringBuilder sb = new();
			sb.Append($"@{now.ToUnixTimeMilliseconds()}@");
			sb.Append(text);
			return sb.ToString();
		}
		return text;
	}
}
