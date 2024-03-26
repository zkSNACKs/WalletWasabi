using System.Text.Json.Serialization;

namespace AI.Model.Json.Completions;

[JsonSerializable(typeof(CompletionsRequestBody))]
[JsonSerializable(typeof(CompletionsResponseSuccess))]
[JsonSerializable(typeof(CompletionsChoice))]
[JsonSerializable(typeof(CompletionsUsage))]
[JsonSerializable(typeof(CompletionsResponseError))]
[JsonSerializable(typeof(CompletionsError))]
public partial class CompletionsJsonContext : JsonSerializerContext
{
}
