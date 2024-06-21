using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace WalletWasabi.Fluent.Behaviors;

public class ExecuteCommandOnActivatedBehavior : ExecuteCommandBaseBehavior
{
	protected override void OnAttached(CompositeDisposable disposables)
	{
		if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
		{
			var mainWindow = lifetime.MainWindow;

			Observable
				.FromEventPattern(mainWindow, nameof(mainWindow.Activated))
				.Subscribe(_ =>
				{
					if (!IsEnabled)
					{
						return;
					}

					var parameter = CommandParameter;
					if (Command is { } cmd && cmd.CanExecute(parameter))
					{
						cmd.Execute(parameter);
					}
				})
				.DisposeWith(disposables);
		}
	}
}
