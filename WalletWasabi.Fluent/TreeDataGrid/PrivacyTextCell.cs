using Avalonia.Controls.Models.TreeDataGrid;

namespace WalletWasabi.Fluent.TreeDataGrid;

internal class PrivacyTextCell : ICell
{
	public PrivacyTextCell(string? value, PrivacyCellType type, int numberOfPrivacyChars)
	{
		Value = value;
		Type = type;
		NumberOfPrivacyChars = numberOfPrivacyChars;
	}

	public bool CanEdit => false;

	public BeginEditGestures EditGestures => BeginEditGestures.None;

	public string? Value { get; }

	public PrivacyCellType Type { get; }

	public int NumberOfPrivacyChars { get; }

	object? ICell.Value => Value;
}
