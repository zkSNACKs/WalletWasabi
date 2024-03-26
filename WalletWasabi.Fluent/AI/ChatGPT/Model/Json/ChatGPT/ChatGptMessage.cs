using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace AI.Model.Json.ChatGPT;

[DataContract]
public class ChatGptMessage
{
    [DataMember(Name = "role")]
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [DataMember(Name = "content")]
    [JsonPropertyName("content")]
    public string[]? Content { get; set; }

    [DataMember(Name = "create_time")]
    [JsonPropertyName("create_time")]
    public double CreateTime { get; set; }
}
