using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace AI.Model.Json.Chat;

[DataContract]
public class ChatResponseError : ChatResponse
{
    [DataMember(Name = "error")]
    [JsonPropertyName("error")]
    public ChatError? Error { get; set; }
}
