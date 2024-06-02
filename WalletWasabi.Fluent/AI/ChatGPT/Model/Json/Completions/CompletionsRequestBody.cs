using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace AI.Model.Json.Completions;

[DataContract]
public class CompletionsRequestBody
{
    [DataMember(Name = "model")]
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [DataMember(Name = "prompt")]
    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    [DataMember(Name = "suffix")]
    [JsonPropertyName("suffix")]
    public string? Suffix { get; set; }

    [DataMember(Name = "max_tokens")]
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 16;

    [DataMember(Name = "temperature")]
    [JsonPropertyName("temperature")]
    public decimal Temperature { get; set; } = 1;

    [DataMember(Name = "top_p")]
    [JsonPropertyName("top_p")]
    public decimal TopP { get; set; } = 1;

    [DataMember(Name = "n")]
    [JsonPropertyName("n")]
    public int N { get; set; } = 1;

    [DataMember(Name = "stream")]
    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [DataMember(Name = "logprobs")]
    [JsonPropertyName("logprobs")]
    public int? Logprobs { get; set; }

    [DataMember(Name = "echo")]
    [JsonPropertyName("echo")]
    public bool Echo { get; set; }

    [DataMember(Name = "stop")]
    [JsonPropertyName("stop")]
    public string? Stop { get; set; }

    [DataMember(Name = "presence_penalty")]
    [JsonPropertyName("presence_penalty")]
    public decimal PresencePenalty { get; set; }

    [DataMember(Name = "frequency_penalty")]
    [JsonPropertyName("frequency_penalty")]
    public decimal FrequencyPenalty { get; set; }

    [DataMember(Name = "best_of")]
    [JsonPropertyName("best_of")]
    public int BestOf { get; set; } = 1;

    [DataMember(Name = "logit_bias")]
    [JsonPropertyName("logit_bias")]
    public Dictionary<string, decimal>? LogitBias { get; set; }

    [DataMember(Name = "user")]
    [JsonPropertyName("user")]
    public string? User { get; set; }
}
