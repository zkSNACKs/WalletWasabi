using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalonia.Xaml.Interactions.Custom;

namespace WalletWasabi.Fluent.Behaviors;

internal class FocusBehavior : DisposingBehavior<Control>
{
	public static readonly StyledProperty<bool> IsFocusedProperty =
		AvaloniaProperty.Register<FocusBehavior, bool>(nameof(IsFocused), defaultBindingMode: BindingMode.TwoWay);

	public bool IsFocused
	{
		get => GetValue(IsFocusedProperty);
		set => SetValue(IsFocusedProperty, value);
	}

	protected override void OnAttached(CompositeDisposable disposables)
	{
		if (AssociatedObject is not null)
		{
			disposables.Add(AssociatedObject.GetObservable(Avalonia.Input.InputElement.IsFocusedProperty)
				.Subscribe(new AnonymousObserver<bool>(
					focused =>
					{
						if (!focused)
						{
							SetCurrentValue(IsFocusedProperty, false);
						}
					})));

			disposables.Add(this.GetObservable(IsFocusedProperty)
				.Subscribe(new AnonymousObserver<bool>(
					focused =>
					{
						if (focused)
						{
							Dispatcher.UIThread.Post(() => AssociatedObject?.Focus());
						}
					})));
		}
	}
}
