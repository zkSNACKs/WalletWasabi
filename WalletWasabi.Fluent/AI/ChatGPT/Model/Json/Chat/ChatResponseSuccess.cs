using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace AI.Model.Json.Chat;

[DataContract]
public class ChatResponseSuccess : ChatResponse
{
    [DataMember(Name = "id")]
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [DataMember(Name = "object")]
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [DataMember(Name = "created")]
    [JsonPropertyName("created")]
    public int Created { get; set; }

    [DataMember(Name = "model")]
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [DataMember(Name = "choices")]
    [JsonPropertyName("choices")]
    public ChatChoice[]? Choices { get; set; }

    [DataMember(Name = "usage")]
    [JsonPropertyName("usage")]
    public ChatUsage? Usage { get; set; }
}
