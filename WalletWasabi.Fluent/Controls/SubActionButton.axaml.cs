using System.Collections;
using Avalonia;
using Avalonia.Controls;

namespace WalletWasabi.Fluent.Controls;

public class SubActionButton : SplitButton
{
	public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
		AvaloniaProperty.Register<SubActionButton, IEnumerable?>(nameof(ItemsSource));

	public IEnumerable? ItemsSource
	{
		get => GetValue(ItemsSourceProperty);
		set => SetValue(ItemsSourceProperty, value);
	}
}
