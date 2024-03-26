using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChatGPT.ViewModels.Chat;

public class ChatResultViewModel : ObservableObject
{
    private string? _message;
    private bool _isError;
    private ChatMessageFunctionCallViewModel? _functionCall;

    [JsonPropertyName("name")]
    public string? Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    [JsonPropertyName("isError")]
    public bool IsError
    {
        get => _isError;
        set => SetProperty(ref _isError, value);
    }

    [JsonPropertyName("function_call")]
    public ChatMessageFunctionCallViewModel? FunctionCall
    {
        get => _functionCall;
        set => SetProperty(ref _functionCall, value);
    }
}
