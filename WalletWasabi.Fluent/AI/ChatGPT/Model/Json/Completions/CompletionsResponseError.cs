using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace AI.Model.Json.Completions;

[DataContract]
public class CompletionsResponseError : CompletionsResponse
{
    [DataMember(Name = "error")]
    [JsonPropertyName("error")]
    public CompletionsError? Error { get; set; }
}
