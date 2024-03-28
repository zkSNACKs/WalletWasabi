using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace WalletWasabi.Fluent.Controls;

public class InvalidatingStackPanel : StackPanel
{
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		if (change.Property == DataContextProperty)
		{
			foreach (var visualChild in VisualChildren)
			{
				if (visualChild is Layoutable layoutable)
				{
					layoutable.InvalidateMeasure();
					layoutable.InvalidateArrange();
				}
			}

			InvalidateMeasure();
			InvalidateArrange();

			if (Parent is Layoutable parentLayoutable)
			{
				parentLayoutable.InvalidateMeasure();
				parentLayoutable.InvalidateArrange();
			}
		}
	}
}
