using System.Windows.Input;

namespace WalletWasabi.Fluent.Controls.Sorting;

public partial class SortableItem : ISortableItem
{
	[AutoNotify] private bool _isDescendingActive;
	[AutoNotify] private bool _isAscendingActive;

	public SortableItem(string name)
	{
		Name = name;
	}

	public required ICommand SortByDescendingCommand { get; init; }
	public required ICommand SortByAscendingCommand { get; init; }
	public string Name { get; init; }
}
