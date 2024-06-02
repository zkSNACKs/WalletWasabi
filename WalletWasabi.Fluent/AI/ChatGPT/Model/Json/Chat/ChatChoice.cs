using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace AI.Model.Json.Chat;

[DataContract]
public class ChatChoice
{
    [DataMember(Name = "message")]
    [JsonPropertyName("message")]
    public ChatMessage? Message { get; set; }

    [DataMember(Name = "index")]
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [DataMember(Name = "logprobs")]
    [JsonPropertyName("logprobs")]
    public object? Logprobs { get; set; }

    [DataMember(Name = "finish_reason")]
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}
