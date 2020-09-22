using Avalonia.Input;
using Avalonia.Interactivity;
using System.Reactive.Disposables;

namespace WalletWasabi.Gui.Behaviors
{
	public class CommandOnClickReleaseBehavior : CommandBasedBehavior<InputElement>
	{
		private CompositeDisposable Disposables { get; set; }

		protected override void OnAttached()
		{
			Disposables = new CompositeDisposable();

			base.OnAttached();

			Disposables.Add(AssociatedObject.AddDisposableHandler(InputElement.PointerReleasedEvent, (sender, e) => e.Handled = ExecuteCommand()));
		}

		protected override void OnDetaching()
		{
			base.OnDetaching();

			Disposables?.Dispose();
		}
	}
}
