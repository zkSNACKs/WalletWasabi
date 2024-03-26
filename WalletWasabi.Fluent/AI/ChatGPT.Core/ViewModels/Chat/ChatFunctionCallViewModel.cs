using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChatGPT.ViewModels.Chat;

public class ChatFunctionCallViewModel : ObservableObject
{
    private string? _name;

    [JsonConstructor]
    public ChatFunctionCallViewModel()
    {
    }

    public ChatFunctionCallViewModel(string name) 
        : this()
    {
        _name = name;
    }

    [JsonPropertyName("name")]
    public string? Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public ChatFunctionCallViewModel Copy()
    {
        return new ChatFunctionCallViewModel
        {
            Name = _name,
        };
    }
}
