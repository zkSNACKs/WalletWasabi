using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChatGPT.ViewModels.Chat;

public class ChatFunctionViewModel : ObservableObject
{
    private string? _name;
    private string? _description;
    private object? _parameters;

    [JsonConstructor]
    public ChatFunctionViewModel()
    {
    }

    public ChatFunctionViewModel(string name, string description) 
        : this()
    {
        _name = name;
        _description = description;
    }

    public ChatFunctionViewModel(string name, string description, object parameters) 
        : this()
    {
        _name = name;
        _description = description;
        _parameters = parameters;
    }

    [JsonPropertyName("name")]
    public string? Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    [JsonPropertyName("description")]
    public string? Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    [JsonPropertyName("parameters")]
    public object? Parameters
    {
        get => _parameters;
        set => SetProperty(ref _parameters, value);
    }

    public ChatFunctionViewModel Copy()
    {
        return new ChatFunctionViewModel
        {
            Name = _name,
            Description = _description,
            // TODO: Copy Parameters if type is reference.
            Parameters = _parameters
        };
    }
}
