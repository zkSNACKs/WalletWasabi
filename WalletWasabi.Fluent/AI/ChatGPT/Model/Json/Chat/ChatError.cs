using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace AI.Model.Json.Chat;

[DataContract]
public class ChatError
{
    [DataMember(Name = "message")]
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [DataMember(Name = "type")]
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [DataMember(Name = "param")]
    [JsonPropertyName("param")]
    public object? Param { get; set; }

    [DataMember(Name = "code")]
    [JsonPropertyName("code")]
    public string? Code { get; set; }
}
