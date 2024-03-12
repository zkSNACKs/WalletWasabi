using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace WalletWasabi.Fluent.TreeDataGrid;

internal class PrivacyElementFactory : TreeDataGridElementFactory
{
	protected override Control CreateElement(object? data)
	{
		if (data is PrivacyTextCell cell)
		{
			return cell.Type switch
			{
				PrivacyCellType.Amount => new TreeDataGridAmountPrivacyTextCell(),
				PrivacyCellType.Date => new TreeDataGridDatePrivacyTextCell(),
				_ => new TreeDataGridPrivacyTextCell()
			};
		}

		return base.CreateElement(data);
	}

	protected override string GetDataRecycleKey(object? data)
	{
		if (data is PrivacyTextCell cell)
		{
			return cell.Type switch
			{
				PrivacyCellType.Amount => typeof(TreeDataGridAmountPrivacyTextCell).FullName!,
				PrivacyCellType.Date => typeof(TreeDataGridDatePrivacyTextCell).FullName!,
				_ => typeof(TreeDataGridPrivacyTextCell).FullName!
			};
		}

		return base.GetDataRecycleKey(data);
	}
}
