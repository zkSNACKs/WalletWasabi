using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace WalletWasabi.Fluent.Controls.Sorting;

public class SortControl : TemplatedControl
{
	public static readonly StyledProperty<IEnumerable<ISortableItem>> SortablesProperty = AvaloniaProperty.Register<SortControl, IEnumerable<ISortableItem>>(nameof(Sortables));

	public IEnumerable<ISortableItem> Sortables
	{
		get => GetValue(SortablesProperty);
		set => SetValue(SortablesProperty, value);
	}
}
