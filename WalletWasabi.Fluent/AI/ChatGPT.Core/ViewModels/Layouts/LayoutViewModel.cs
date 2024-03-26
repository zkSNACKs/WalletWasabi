using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChatGPT.ViewModels.Layouts;

[JsonPolymorphic]
[JsonDerivedType(typeof(MobileLayoutViewModel), typeDiscriminator: "mobile")]
[JsonDerivedType(typeof(DesktopLayoutViewModel), typeDiscriminator: "desktop")]
public abstract partial class LayoutViewModel : ObservableObject
{
    private string? _name;
    private bool _showSettings;
    private bool _showChats;
    private bool _showPrompts;

    [JsonConstructor]
    protected LayoutViewModel()
    {
        ShowSettingsCommand = new RelayCommand(ShowSettingsAction);

        ShowChatsCommand = new RelayCommand(ShowChatsAction);

        ShowPromptsCommand = new RelayCommand(ShowPromptsAction);
    }

    [JsonPropertyName("name")]
    public string? Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    [JsonPropertyName("showSettings")]
    public bool ShowSettings
    {
        get => _showSettings;
        set => SetProperty(ref _showSettings, value);
    }

    [JsonPropertyName("showChats")]
    public bool ShowChats
    {
        get => _showChats;
        set => SetProperty(ref _showChats, value);
    }

    [JsonPropertyName("showPrompts")]
    public bool ShowPrompts
    {
        get => _showPrompts;
        set => SetProperty(ref _showPrompts, value);
    }

    [JsonIgnore]
    public IRelayCommand ShowSettingsCommand { get; }

    [JsonIgnore]
    public IRelayCommand ShowChatsCommand { get; }

    [JsonIgnore]
    public IRelayCommand ShowPromptsCommand { get; }

    public abstract Task BackAsync();

    protected abstract void ShowSettingsAction();

    protected abstract void ShowChatsAction();

    protected abstract void ShowPromptsAction();

    public abstract LayoutViewModel Copy();
}
