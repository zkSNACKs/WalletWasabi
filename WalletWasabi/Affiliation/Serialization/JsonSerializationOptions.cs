using System.Collections.Generic;
using Newtonsoft.Json;

namespace WalletWasabi.Affiliation.Serialization;

public static class JsonSerializationOptions
{
	public static readonly List<JsonConverter> Converters = new List<JsonConverter>() { new ByteArrayJsonConverter(), new FeeRateJsonConverter() };

	public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings() { Converters = Converters };
}
