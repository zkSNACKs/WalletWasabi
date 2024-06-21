using System.Reactive.Disposables;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Xaml.Interactions.Custom;

namespace WalletWasabi.Fluent.Behaviors;

public class ExecuteCommandOnTappedBehavior : ExecuteCommandBaseBehavior
{
	protected override void OnAttached(CompositeDisposable disposables)
	{
		Gestures.TappedEvent.AddClassHandler<InputElement>(
				(x, _) =>
				{
					if (Equals(x, AssociatedObject) && IsEnabled)
					{
						var parameter = CommandParameter;
						if (Command is { } cmd && cmd.CanExecute(parameter))
						{
							cmd.Execute(parameter);
						}
					}
				},
				RoutingStrategies.Tunnel | RoutingStrategies.Bubble)
			.DisposeWith(disposables);
	}
}
