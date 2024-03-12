namespace WalletWasabi.Fluent.TreeDataGrid;

public interface ITreeDataGridExpanderItem
{
	bool IsExpanded { get; set; }

	bool IsChild { get; set; }

	bool IsLastChild { get; set; }

	bool IsParentPointerOver { get; set; }

	bool IsParentSelected { get; set; }
}
