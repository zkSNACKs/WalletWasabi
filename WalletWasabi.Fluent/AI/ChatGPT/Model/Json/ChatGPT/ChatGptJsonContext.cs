using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI.Model.Json.ChatGPT;

[JsonSerializable(typeof(ChatGpt))]
[JsonSerializable(typeof(ChatGptMessage))]
[JsonSerializable(typeof(ChatGpt[]))]
public partial class ChatGptJsonContext : JsonSerializerContext
{
    public static readonly ChatGptJsonContext s_instance = new(
        new JsonSerializerOptions
        {
            WriteIndented = true,
            ReferenceHandler = ReferenceHandler.Preserve,
            IncludeFields = false,
            IgnoreReadOnlyFields = true,
            IgnoreReadOnlyProperties = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        });
}
