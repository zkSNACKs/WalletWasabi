using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace AI.Model.Json.Chat;

[DataContract]
public class ChatUsage
{
    [DataMember(Name = "prompt_tokens")]
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [DataMember(Name = "completion_tokens")]
    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [DataMember(Name = "total_tokens")]
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
