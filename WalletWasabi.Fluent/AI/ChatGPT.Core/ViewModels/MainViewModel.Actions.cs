using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using ChatGPT.Model.Services;
using ChatGPT.ViewModels.Layouts;
using CommunityToolkit.Mvvm.Input;

namespace ChatGPT.ViewModels;

public partial class MainViewModel
{
    private ObservableCollection<LayoutViewModel>? _layouts;
    private LayoutViewModel? _currentLayout;
    private MobileLayoutViewModel? _mobileLayout;
    private DesktopLayoutViewModel? _desktopLayout;
    private string? _theme;
    private bool _topmost;

    [JsonPropertyName("layouts")]
    public ObservableCollection<LayoutViewModel>? Layouts
    {
        get => _layouts;
        set => SetProperty(ref _layouts, value);
    }

    [JsonPropertyName("currentLayout")]
    public LayoutViewModel? CurrentLayout
    {
        get => _currentLayout;
        set => SetProperty(ref _currentLayout, value);
    }

    [JsonPropertyName("mobileLayout")]
    public MobileLayoutViewModel? MobileLayout
    {
        get => _mobileLayout;
        set => SetProperty(ref _mobileLayout, value);
    }

    [JsonPropertyName("desktopLayout")]
    public DesktopLayoutViewModel? DesktopLayout
    {
        get => _desktopLayout;
        set => SetProperty(ref _desktopLayout, value);
    }

    [JsonPropertyName("theme")]
    public string? Theme
    {
        get => _theme;
        set => SetProperty(ref _theme, value);
    }

    [JsonPropertyName("topmost")]
    public bool Topmost
    {
        get => _topmost;
        set => SetProperty(ref _topmost, value);
    }

    [JsonIgnore]
    public IRelayCommand ExitCommand { get; }

    [JsonIgnore]
    public IAsyncRelayCommand LoadWorkspaceCommand { get; }

    [JsonIgnore]
    public IAsyncRelayCommand SaveWorkspaceCommand { get; }

    [JsonIgnore]
    public IAsyncRelayCommand ExportWorkspaceCommand { get; }

    [JsonIgnore]
    public IRelayCommand ChangeThemeCommand { get; }

    [JsonIgnore]
    public IRelayCommand ChangeDesktopMobileCommand { get; }

    [JsonIgnore]
    public IRelayCommand ChangeTopmostCommand { get; }

    private void ExitAction()
    {
        var app = Defaults.Locator.GetService<IApplicationService>();
        app?.Exit();
    }

    private void ChangeThemeAction()
    {
        var app = Defaults.Locator.GetService<IApplicationService>();
        if (app is { })
        {
            app.ToggleTheme();
        }
    }

    private void ChangeDesktopMobileAction()
    {
        CurrentLayout = CurrentLayout switch
        {
            MobileLayoutViewModel => DesktopLayout,
            DesktopLayoutViewModel => MobileLayout,
            _ => CurrentLayout
        };
    }

    private void ChangeTopmostAction()
    {
        Topmost = !Topmost;
    }
}
