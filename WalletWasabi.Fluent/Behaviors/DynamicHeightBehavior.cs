using System;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using ReactiveUI;

namespace WalletWasabi.Fluent.Behaviors
{
	public class DynamicHeightBehavior : DisposingBehavior<Control>
	{
		public static readonly StyledProperty<double> HeightMultiplierProperty =
			AvaloniaProperty.Register<DynamicHeightBehavior, double>(nameof(HeightMultiplier));

		public static readonly StyledProperty<double> HideThresholdHeightProperty =
			AvaloniaProperty.Register<DynamicHeightBehavior, double>(nameof(HideThresholdHeight));

		public double HeightMultiplier
		{
			get => GetValue(HeightMultiplierProperty);
			set => SetValue(HeightMultiplierProperty, value);
		}

		public double HideThresholdHeight
		{
			get => GetValue(HideThresholdHeightProperty);
			set => SetValue(HideThresholdHeightProperty, value);
		}

		protected override void OnAttached(CompositeDisposable disposables)
		{
			AssociatedObject?.Parent?.WhenAnyValue(x => x.Bounds)
				.Subscribe(bounds =>
				{
					var newHeight = bounds.Height * HeightMultiplier;

					if (newHeight < HideThresholdHeight)
					{
						AssociatedObject.IsVisible = false;
					}
					else
					{
						AssociatedObject.IsVisible = true;
						AssociatedObject.Height = newHeight;
					}
				})
				.DisposeWith(disposables);
		}
	}
}
