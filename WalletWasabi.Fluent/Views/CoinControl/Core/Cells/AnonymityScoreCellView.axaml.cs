using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace WalletWasabi.Fluent.Views.CoinControl.Core.Cells;

public class AnonymityScoreCellView : UserControl
{
	public AnonymityScoreCellView()
	{
		InitializeComponent();
	}

	private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);
	}
}
