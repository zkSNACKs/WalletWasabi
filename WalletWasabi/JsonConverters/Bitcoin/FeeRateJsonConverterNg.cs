using NBitcoin;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WalletWasabi.JsonConverters.Bitcoin;

public class FeeRateJsonConverterNg : JsonConverter<FeeRate>
{
	/// <inheritdoc />
	public override FeeRate? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.Number)
		{
			throw new JsonException("Expected a JSON number value.");
		}

		long? serialized = reader.GetInt64();
		return serialized is null ? null : new FeeRate(Money.Satoshis(serialized.Value));
	}

	/// <inheritdoc />
	public override void Write(Utf8JsonWriter writer, FeeRate? value, JsonSerializerOptions options)
	{
		long longValue = value?.FeePerK.Satoshi
			?? throw new ArgumentNullException(nameof(value));
		writer.WriteNumberValue(longValue);
	}
}
