using System.Reactive.Disposables;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Xaml.Interactions.Custom;

namespace WalletWasabi.Fluent.Behaviors;

public class ExecuteCommandOnLostFocusBehavior : ExecuteCommandBaseBehavior
{
	protected override void OnAttached(CompositeDisposable disposables)
	{
		InputElement.LostFocusEvent.AddClassHandler<InputElement>(
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
				RoutingStrategies.Bubble)
			.DisposeWith(disposables);
	}
}
