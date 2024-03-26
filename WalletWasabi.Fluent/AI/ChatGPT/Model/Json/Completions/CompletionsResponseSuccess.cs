using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace AI.Model.Json.Completions;

[DataContract]
public class CompletionsResponseSuccess : CompletionsResponse
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
    public CompletionsChoice[]? Choices { get; set; }

    [DataMember(Name = "usage")]
    [JsonPropertyName("usage")]
    public CompletionsUsage? Usage { get; set; }
}
