using System.Windows.Input;

namespace WalletWasabi.Fluent.Controls.Sorting;

public class SortableItemDesign : ISortableItem
{
	public ICommand SortByDescendingCommand { get; set; }
	public ICommand SortByAscendingCommand { get; set; }
	public string Name { get; set; }
	public bool IsDescendingActive { get; set; }
	public bool IsAscendingActive { get; set; }
}
