using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChatGPT.ViewModels.Layouts;

public partial class WindowLayoutViewModel : ObservableObject
{
    private int _x;
    private int _y;
    private double _width;
    private double _height;
    private string? _windowState;
    private string? _windowStartupLocation;
    private bool _topmost;

    [JsonConstructor]
    public WindowLayoutViewModel()
    {
    }

    [JsonPropertyName("x")]
    public int X
    {
        get => _x;
        set => SetProperty(ref _x, value);
    }

    [JsonPropertyName("y")]
    public int Y
    {
        get => _y;
        set => SetProperty(ref _y, value);
    }

    [JsonPropertyName("width")]
    public double Width
    {
        get => _width;
        set => SetProperty(ref _width, value);
    }

    [JsonPropertyName("height")]
    public double Height
    {
        get => _height;
        set => SetProperty(ref _height, value);
    }

    [JsonPropertyName("windowState")]
    public string? WindowState
    {
        get => _windowState;
        set => SetProperty(ref _windowState, value);
    }

    [JsonPropertyName("windowStartupLocation")]
    public string? WindowStartupLocation
    {
        get => _windowStartupLocation;
        set => SetProperty(ref _windowStartupLocation, value);
    }

    [JsonPropertyName("topmost")]
    public bool Topmost
    {
        get => _topmost;
        set => SetProperty(ref _topmost, value);
    }
    
    public WindowLayoutViewModel Copy()
    {
        return new WindowLayoutViewModel
        {
            X = _x,
            Y = _y,
            Width = _width,
            Height = _height,
            WindowState = _windowState,
            WindowStartupLocation = _windowStartupLocation,
            Topmost = _topmost,
        };
    }
}
