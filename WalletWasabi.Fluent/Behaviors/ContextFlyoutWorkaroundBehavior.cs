using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using ReactiveUI;

namespace WalletWasabi.Fluent.Behaviors;

/// <summary>That behavior workarounds an Avalonia bug with not auto closing context flyouts.</summary>
public class ContextFlyoutWorkaroundBehavior : DisposingBehavior<Window>
{
	private readonly List<FlyoutBase> _openFlyouts = new();

	protected override void OnAttached(CompositeDisposable disposables)
	{
		if (AssociatedObject is { })
		{
			FlyoutBase.IsOpenProperty.Changed
				.Subscribe(FlyoutOpenChanged)
				.DisposeWith(disposables);

			AssociatedObject
				.WhenAnyValue(x => x.IsActive, x => x.IsPointerOver, (isActive, isPointerOver) => !isActive && !isPointerOver)
				.Where(x => x)
				.Subscribe(_ => CloseFlyouts())
				.DisposeWith(disposables);

			AssociatedObject
				.GetObservable(Visual.BoundsProperty)
				.Subscribe(_ => CloseFlyouts())
				.DisposeWith(disposables);

			Observable
				.FromEventPattern<PixelPointEventArgs>(
					handler => AssociatedObject!.PositionChanged += handler,
					handler => AssociatedObject!.PositionChanged -= handler)
				.Subscribe(_ => CloseFlyouts())
				.DisposeWith(disposables);
		}
	}

	protected void CloseFlyouts()
	{
		for (var index = _openFlyouts.Count; index > 0; )
		{
			_openFlyouts[--index].Hide();
		}
	}

	private void FlyoutOpenChanged(AvaloniaPropertyChangedEventArgs<bool> e)
	{
		if (e.Sender is FlyoutBase flyout &&
			flyout.Target is { } target)
		{
			if (e.OldValue.Value)
			{
				_openFlyouts.Remove(flyout);

				return;
			}

			if (target.FindAncestorOfType<Window>() == AssociatedObject)
			{
				_openFlyouts.Add(flyout);
			}
		}
	}
}
