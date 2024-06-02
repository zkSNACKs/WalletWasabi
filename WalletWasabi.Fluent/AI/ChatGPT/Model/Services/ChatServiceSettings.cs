using AI.Model.Json.Chat;

namespace AI.Model.Services;

public class ChatServiceSettings
{
    public string? ApiUrl { get; set; }
    public string? Model { get; set; }
    public ChatMessage[]? Messages { get; set; }
    public object? Functions { get; set; }
    public object? FunctionCall { get; set; }
    public string? Suffix { get; set; }
    public decimal Temperature { get; set; }
    public int MaxTokens { get; set; }
    public decimal TopP { get; set; }
    public decimal PresencePenalty { get; set; }
    public decimal FrequencyPenalty { get; set; }
    public string? Stop { get; set; }
    public bool Debug { get; set; }
    public bool RequireApiKey { get; set; }
}
