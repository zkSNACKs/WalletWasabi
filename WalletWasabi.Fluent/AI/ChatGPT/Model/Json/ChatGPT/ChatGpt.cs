using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace AI.Model.Json.ChatGPT;

[DataContract]
public class ChatGpt
{
    [DataMember(Name = "messages")]
    [JsonPropertyName("messages")]
    public ChatGptMessage[]? Messages { get; set; }

    [DataMember(Name = "create_time")]
    [JsonPropertyName("create_time")]
    public double? CreateTime { get; set; }

    [DataMember(Name = "title")]
    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
