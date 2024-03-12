using System.Windows.Input;

namespace WalletWasabi.Fluent.Controls.Sorting;

public interface ISortableItem
{
	string Name { get; }
	bool IsDescendingActive { get; set; }
	bool IsAscendingActive { get; set; }
	ICommand SortByDescendingCommand { get; }
	ICommand SortByAscendingCommand { get; }
}
