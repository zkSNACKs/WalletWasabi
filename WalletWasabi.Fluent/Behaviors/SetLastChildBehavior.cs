using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Xaml.Interactions.Custom;
using ReactiveUI;
using WalletWasabi.Fluent.TreeDataGrid;

namespace WalletWasabi.Fluent.Behaviors;

/// <remark>
/// Items must implement <see cref="ITreeDataGridExpanderItem"/> interface.
/// </remark>
public class SetLastChildBehavior : AttachedToVisualTreeBehavior<Avalonia.Controls.TreeDataGrid>
{
	protected override void OnAttachedToVisualTree(CompositeDisposable disposable)
	{
		if (AssociatedObject is { Rows: { } rows })
		{
			Observable.FromEventPattern(rows, nameof(rows.CollectionChanged))
				.Select(_ => AssociatedObject.RowsPresenter?.Items)
				.WhereNotNull()
				.Do(items =>
				{
					var castedList = items.Select(x => x.Model as ITreeDataGridExpanderItem).ToArray();

					for (var i = 0; i < castedList.Length; i++)
					{
						var currentItem = castedList[i];
						var nextItem = i + 1 < castedList.Length ? castedList[i + 1] : null;

						if (currentItem is { IsChild: true })
						{
							currentItem.IsLastChild = nextItem is null or { IsChild: false };
						}
					}
				})
				.Subscribe()
				.DisposeWith(disposable);
		}
	}
}
