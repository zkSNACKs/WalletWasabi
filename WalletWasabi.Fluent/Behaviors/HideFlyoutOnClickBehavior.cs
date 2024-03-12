using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;

namespace WalletWasabi.Fluent.Behaviors;

public class HideFlyoutOnClickBehavior : Behavior<RadioButton>
{
	private IDisposable? _subscription;

	protected override void OnAttachedToVisualTree()
	{
		base.OnAttachedToVisualTree();

		if (AssociatedObject == null)
		{
			return;
		}
        
		var fp = AssociatedObject.FindAncestorOfType<FlyoutPresenter>();

		if (fp?.Parent is not Popup popup)
		{
			return;
		}
        
		_subscription = Observable
			.FromEventPattern<RoutedEventArgs>(handler => AssociatedObject.Click += handler, handler => AssociatedObject.Click -= handler)
			.Do(_ =>
			{
				// Execute Command if any before closing. Otherwise, it won't execute because Close will destroy the associated object before Click can execute it.
				if (AssociatedObject.Command != null && AssociatedObject.IsEnabled)
				{
					AssociatedObject.Command.Execute(AssociatedObject.CommandParameter);
				}
				popup.Close();
			})
			.Subscribe();
	}

	protected override void OnDetachedFromVisualTree()
	{
		base.OnDetachedFromVisualTree();
		_subscription?.Dispose();
	}
}
