using Avalonia;
using Avalonia.Controls;

namespace WalletWasabi.Fluent.Controls;

public class ThemeImage: Image
{
	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		ActualThemeVariantChanged += OnThemeVariantChanged;
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		ActualThemeVariantChanged -= OnThemeVariantChanged;	
	}

	private void OnThemeVariantChanged(object? sender, EventArgs e)
	{
		InvalidateVisual();
	}	
}
