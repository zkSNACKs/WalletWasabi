using System.Reactive.Disposables;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactions.Custom;

namespace WalletWasabi.Fluent.Behaviors;

public class ExecuteCommandOnKeyDownBehavior : AttachedToVisualTreeBehavior<Control>
{
	public static readonly StyledProperty<KeyGesture?> KeyGestureProperty =
		AvaloniaProperty.Register<ExecuteCommandOnKeyDownBehavior, KeyGesture?>(nameof(KeyGesture));

	public static readonly StyledProperty<Key?> KeyProperty =
		AvaloniaProperty.Register<ExecuteCommandOnKeyDownBehavior, Key?>(nameof(Key));

	public static readonly StyledProperty<bool> IsEnabledProperty =
		AvaloniaProperty.Register<ExecuteCommandOnKeyDownBehavior, bool>(nameof(IsEnabled), true);

	public static readonly StyledProperty<ICommand> CommandProperty =
		AvaloniaProperty.Register<ExecuteCommandOnKeyDownBehavior, ICommand>(nameof(Command));

	public static readonly StyledProperty<object> CommandParameterProperty =
		AvaloniaProperty.Register<ExecuteCommandOnKeyDownBehavior, object>(nameof(CommandParameter));

	public static readonly StyledProperty<RoutingStrategies> EventRoutingStrategyProperty =
		AvaloniaProperty.Register<ExecuteCommandOnKeyDownBehavior, RoutingStrategies>(nameof(EventRoutingStrategy), RoutingStrategies.Bubble);

	/// <summary>If specified, the Command will be executed whenever the Key matches, regardless of KeyModifiers </summary>
	public Key? Key
	{
		get => GetValue(KeyProperty);
		set => SetValue(KeyProperty, value);
	}

	/// <summary>If specified, the Command will be executed only if the gesture matches exactly (including KeyModifiers) </summary>
	public KeyGesture? KeyGesture
	{
		get => GetValue(KeyGestureProperty);
		set => SetValue(KeyGestureProperty, value);
	}

	public bool IsEnabled
	{
		get => GetValue(IsEnabledProperty);
		set => SetValue(IsEnabledProperty, value);
	}

	public ICommand Command
	{
		get => GetValue(CommandProperty);
		set => SetValue(CommandProperty, value);
	}

	public object CommandParameter
	{
		get => GetValue(CommandParameterProperty);
		set => SetValue(CommandParameterProperty, value);
	}

	public RoutingStrategies EventRoutingStrategy
	{
		get => GetValue(EventRoutingStrategyProperty);
		set => SetValue(EventRoutingStrategyProperty, value);
	}

	protected override void OnAttachedToVisualTree(CompositeDisposable disposable)
	{
		var control = AssociatedObject;
		if (control is null)
		{
			return;
		}

		if (control.GetVisualRoot() is InputElement inputRoot)
		{
			inputRoot
				.AddDisposableHandler(InputElement.KeyDownEvent, RootDefaultKeyDown, EventRoutingStrategy)
				.DisposeWith(disposable);
		}
	}

	private void RootDefaultKeyDown(object? sender, KeyEventArgs e)
	{
		var control = AssociatedObject;
		if (control is null)
		{
			return;
		}

		var isMatch =
			(Key is { } k && k == e.Key) ||
			(KeyGesture is { } kg && kg.Matches(e));

		if (isMatch && control.IsVisible && control.IsEnabled && IsEnabled)
		{
			if (!e.Handled)
			{
				e.Handled = true;
				if (Command?.CanExecute(CommandParameter) == true)
				{
					Command.Execute(CommandParameter);
				}
			}
		}
	}
}
