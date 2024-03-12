using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;

namespace WalletWasabi.Fluent.TreeDataGrid;

internal class PrivacyTextColumn<T> : ColumnBase<T>
{
	private readonly Func<T, string?> _getter;
	private readonly PrivacyCellType _type;
	private readonly Comparison<T?>? _sortAscending;
	private readonly Comparison<T?>? _sortDescending;
	private readonly int _numberOfPrivacyChars;

	public PrivacyTextColumn(
		object? header,
		Func<T, string?> getter,
		GridLength? width,
		ColumnOptions<T>? options,
		PrivacyCellType type,
		int numberOfPrivacyChars = 0)
		: base(header, width, options)
	{
		_sortAscending = options?.CompareAscending;
		_sortDescending = options?.CompareDescending;
		_getter = getter;
		_type = type;
		_numberOfPrivacyChars = numberOfPrivacyChars;
	}

	public override ICell CreateCell(IRow<T> row)
	{
		return new PrivacyTextCell(_getter(row.Model), _type, _numberOfPrivacyChars);
	}

	public override Comparison<T?>? GetComparison(ListSortDirection direction)
	{
		return direction switch
		{
			ListSortDirection.Ascending => _sortAscending,
			ListSortDirection.Descending => _sortDescending,
			_ => null,
		};
	}
}
