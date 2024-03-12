using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.Templates;
using ReactiveUI;

namespace WalletWasabi.Fluent.Controls;

public class DummyRowsControl : TemplatedControl
{
	private IEnumerable? _items;
	public static readonly StyledProperty<double> RowHeightProperty = AvaloniaProperty.Register<DummyRowsControl, double>(nameof(RowHeight));

	public static readonly DirectProperty<DummyRowsControl, IEnumerable?> ItemsProperty = AvaloniaProperty.RegisterDirect<DummyRowsControl, IEnumerable?>(nameof(Items), o => o.Items, (o, v) => o.Items = v);

	public static readonly StyledProperty<ControlTemplate> RowTemplateProperty = AvaloniaProperty.Register<DummyRowsControl, ControlTemplate>(nameof(RowTemplate));

	public DummyRowsControl()
	{
		this.WhenAnyValue(x => x.RowHeight, x => x.Bounds)
			.Where(tuple => tuple is { Item1: > 0, Item2: { Width: > 0, Height: > 0 } })
			.Select(a => GenerateItems(a.Item1, a.Item2))
			.BindTo(this, x => x.Items);
	}

	public IEnumerable? Items
	{
		get => _items;
		private set => SetAndRaise(ItemsProperty, ref _items, value);
	}

	public double RowHeight
	{
		get => GetValue(RowHeightProperty);
		set => SetValue(RowHeightProperty, value);
	}

	public ControlTemplate RowTemplate
	{
		get => GetValue(RowTemplateProperty);
		set => SetValue(RowTemplateProperty, value);
	}

	private static IEnumerable<int> GenerateItems(double rowHeight, Rect bounds) => Enumerable.Range(0, (int) Math.Ceiling(bounds.Height / rowHeight));
}
