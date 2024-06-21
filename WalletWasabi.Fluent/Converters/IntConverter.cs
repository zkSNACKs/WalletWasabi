using Avalonia.Data.Converters;

namespace WalletWasabi.Fluent.Converters;

public static class IntConverter
{
	public static readonly IValueConverter ToOrdinalString =
		new FuncValueConverter<int, string>(x => $"{x}.");

	public static readonly IValueConverter IsZero =
		new FuncValueConverter<int, bool>(x => x == 0);
}
