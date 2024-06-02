using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChatGPT.ViewModels.Chat;

public partial class ChatSettingsViewModel : ObservableObject
{
    private object? _functions;
    private object? _functionCall;
    private decimal _temperature;
    private decimal _topP;
    private decimal _presencePenalty;
    private decimal _frequencyPenalty;
    private int _maxTokens;
    private string? _apiKey;
    private string? _model;
    private string? _directions;
    private string? _format;
    private string? _apiUrl;

    [JsonConstructor]
    public ChatSettingsViewModel()
    {
        _functions = null;
        _functionCall = null;
        _temperature = 0.7m;
        _topP = 1m;
        _presencePenalty = 0m;
        _frequencyPenalty = 0m;
        _maxTokens = 2000;
        _apiKey = null;
        _model = "gpt-3.5-turbo";
        _directions = "You are a helpful assistant.";
        _apiUrl = null;
    }

    /// <summary>
    /// A list of functions the model may generate JSON inputs for.
    /// </summary>
    [JsonPropertyName("functions")]
    public object? Functions
    {
        get => _functions;
        set => SetProperty(ref _functions, value);
    }

    /// <summary>
    /// Controls how the model responds to function calls. "none" means the model does not call a function, and responds to the end-user. "auto" means the model can pick between an end-user or calling a function. Specifying a particular function via {"name":\ "my_function"} forces the model to call that function. "none" is the default when no functions are present. "auto" is the default if functions are present.
    /// </summary>
    [JsonPropertyName("function_call")]
    public object?  FunctionCall
    {
        get => _functionCall;
        set => SetProperty(ref _functionCall, value);
    }

    /// <summary>
    /// What sampling temperature to use, between 0 and 2. Higher values like 0.8 will make the output more random, while lower values like 0.2 will make it more focused and deterministic.
    /// </summary>
    /// <remarks>
    /// We generally recommend altering this or top_p but not both.
    /// </remarks>
    [JsonPropertyName("temperature")]
    public decimal Temperature
    {
        get => _temperature;
        set => SetProperty(ref _temperature, value);
    }

    /// <summary>
    /// An alternative to sampling with temperature, called nucleus sampling, where the model considers the results of the tokens with top_p probability mass. So 0.1 means only the tokens comprising the top 10% probability mass are considered.
    /// </summary>
    /// <remarks>
    /// We generally recommend altering this or temperature but not both.
    /// </remarks>
    [JsonPropertyName("top_p")]
    public decimal TopP
    {
        get => _topP;
        set => SetProperty(ref _topP, value);
    }

    /// <summary>
    /// Number between -2.0 and 2.0. Positive values penalize new tokens based on whether they appear in the text so far, increasing the model's likelihood to talk about new topics.
    /// </summary>
    [JsonPropertyName("presence_penalty")]
    public decimal PresencePenalty
    {
        get => _presencePenalty;
        set => SetProperty(ref _presencePenalty, value);
    }

    /// <summary>
    /// Number between -2.0 and 2.0. Positive values penalize new tokens based on their existing frequency in the text so far, decreasing the model's likelihood to repeat the same line verbatim.
    /// </summary>
    [JsonPropertyName("frequency_penalty")]
    public decimal FrequencyPenalty
    {
        get => _frequencyPenalty;
        set => SetProperty(ref _frequencyPenalty, value);
    }

    [JsonPropertyName("maxTokens")]
    public int MaxTokens
    {
        get => _maxTokens;
        set => SetProperty(ref _maxTokens, value);
    }

    [JsonPropertyName("apiKey")]
    public string? ApiKey
    {
        get => _apiKey;
        set => SetProperty(ref _apiKey, value);
    }

    [JsonPropertyName("model")]
    public string? Model
    {
        get => _model;
        set => SetProperty(ref _model, value);
    }

    [JsonPropertyName("directions")]
    public string? Directions
    {
        get => _directions;
        set => SetProperty(ref _directions, value);
    }

    [JsonPropertyName("format")]
    public string? Format
    {
        get => _format;
        set => SetProperty(ref _format, value);
    }

    [JsonPropertyName("apiUrl")]
    public string? ApiUrl
    {
        get => _apiUrl;
        set => SetProperty(ref _apiUrl, value);
    }

    public ChatSettingsViewModel Copy()
    {
        return new ChatSettingsViewModel
        {
            // TODO: Copy Functions object.
            Functions = _functions,
            // TODO: Copy FunctionCall object.
            FunctionCall = _functionCall is ChatFunctionCallViewModel functionCall 
                ? functionCall.Copy() 
                : _functionCall,
            Temperature = _temperature,
            TopP = _topP,
            PresencePenalty = _presencePenalty,
            FrequencyPenalty = _frequencyPenalty,
            MaxTokens = _maxTokens,
            ApiKey = _apiKey,
            Model = _model,
            Directions = _directions,
            Format = _format,
            ApiUrl = _apiUrl,
        };
    }
}
