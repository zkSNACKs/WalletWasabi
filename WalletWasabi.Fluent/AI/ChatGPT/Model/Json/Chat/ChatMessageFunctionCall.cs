using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace AI.Model.Json.Chat;

[DataContract]
public class ChatMessageFunctionCall
{
    [DataMember(Name = "name")]
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [DataMember(Name = "arguments")]
    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}
