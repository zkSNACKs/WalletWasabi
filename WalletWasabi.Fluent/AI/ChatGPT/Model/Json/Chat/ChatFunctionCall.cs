using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace AI.Model.Json.Chat;

[DataContract]
public class ChatFunctionCall
{
    [DataMember(Name = "name")]
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
