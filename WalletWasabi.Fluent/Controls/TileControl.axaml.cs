using Avalonia;
using Avalonia.Controls;

namespace WalletWasabi.Fluent.Controls;

public class TileControl : ContentControl
{
	public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<TileControl, string>(nameof(Title));

	public static readonly StyledProperty<object> BottomContentProperty = AvaloniaProperty.Register<TileControl, object>(nameof(BottomContent));

	public static readonly StyledProperty<bool> IsBottomContentVisibleProperty = AvaloniaProperty.Register<TileControl, bool>(nameof(IsBottomContentVisible), true);

	public static readonly StyledProperty<double> BottomPartHeightProperty = AvaloniaProperty.Register<TileControl, double>(nameof(BottomPartHeight));

	public string Title
	{
		get => GetValue(TitleProperty);
		set => SetValue(TitleProperty, value);
	}

	public object BottomContent
	{
		get => GetValue(BottomContentProperty);
		set => SetValue(BottomContentProperty, value);
	}

	public bool IsBottomContentVisible
	{
		get => GetValue(IsBottomContentVisibleProperty);
		set => SetValue(IsBottomContentVisibleProperty, value);
	}

	public double BottomPartHeight
	{
		get => GetValue(BottomPartHeightProperty);
		set => SetValue(BottomPartHeightProperty, value);
	}
}
