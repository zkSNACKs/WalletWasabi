using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ReactiveUI;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.Providers;
using WalletWasabi.Fluent.State;
using WalletWasabi.Fluent.ViewModels;
using WalletWasabi.Fluent.Views;
using WalletWasabi.Fluent.Views.Shell;
using WalletWasabi.Logging;
using WalletWasabi.Services;
using WalletWasabi.Services.Terminate;

namespace WalletWasabi.Fluent;

public class ApplicationStateManager : IMainWindowService
{
	private readonly StateMachine<State, Trigger> _stateMachine;
	private readonly IApplicationLifetime _lifetime;
	private CompositeDisposable? _compositeDisposable;
	private bool _hideRequest;
	private bool _isShuttingDown;
	private bool _restartRequest;

	internal ApplicationStateManager(IApplicationLifetime lifetime, UiContext uiContext, bool startInBg)
	{
		_lifetime = lifetime;
		_stateMachine = new StateMachine<State, Trigger>(State.InitialState);

		UiContext = uiContext;
		ApplicationViewModel = new ApplicationViewModel(UiContext, this);
		State initTransitionState = startInBg ? State.Closed : State.Open;

		Observable
			.FromEventPattern(Services.SingleInstanceChecker, nameof(SingleInstanceChecker.OtherInstanceStarted))
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(_ => _stateMachine.Fire(Trigger.Show));

		_stateMachine.Configure(State.InitialState)
			.InitialTransition(initTransitionState)
			.OnTrigger(
				Trigger.ShutdownRequested,
				() =>
				{
					if (_restartRequest)
					{
						AppLifetimeHelper.StartAppWithArgs();
					}

					switch (_lifetime)
					{
						case IClassicDesktopStyleApplicationLifetime desktop:
							desktop.Shutdown();
							break;
						case ISingleViewApplicationLifetime single:
							// TODO:
							break;
					}
				})
			.OnTrigger(
				Trigger.ShutdownPrevented,
				() =>
				{
					switch (_lifetime)
					{
						case IClassicDesktopStyleApplicationLifetime desktop:
							desktop.MainWindow.BringToFront();
							break;
						case ISingleViewApplicationLifetime single:
							// TODO:
							break;
					}
					ApplicationViewModel.OnShutdownPrevented(_restartRequest);
					_restartRequest = false; // reset the value.
				});

		_stateMachine.Configure(State.Closed)
			.SubstateOf(State.InitialState)
			.OnEntry(() =>
			{
				switch (_lifetime)
				{
					case IClassicDesktopStyleApplicationLifetime desktop:
						desktop.MainWindow?.Close();
						desktop.MainWindow = null;
						break;
					case ISingleViewApplicationLifetime single:
						// TODO:
						break;
				}
				ApplicationViewModel.IsMainWindowShown = false;
			})
			.Permit(Trigger.Show, State.Open)
			.Permit(Trigger.ShutdownPrevented, State.Open);

		switch (_lifetime)
		{
			case IClassicDesktopStyleApplicationLifetime:
			{
				_stateMachine.Configure(State.Open)
					.SubstateOf(State.InitialState)
					.OnEntry(CreateAndShowMainWindow)
					.Permit(Trigger.Hide, State.Closed)
					.Permit(Trigger.MainWindowClosed, State.Closed)
					.OnTrigger(Trigger.Show, MainViewModel.Instance.ApplyUiConfigWindowState);
				break;
			}
			case ISingleViewApplicationLifetime single:
			{
				_stateMachine.Configure(State.Open)
					.SubstateOf(State.InitialState)
					.OnEntry(CreateAndShowMainView)
					.Permit(Trigger.Hide, State.Closed)
					.Permit(Trigger.MainWindowClosed, State.Closed)
					.OnTrigger(Trigger.Show, MainViewModel.Instance.ApplyUiConfigWindowState);
				break;
			}
		}

		switch (_lifetime)
		{
			case IClassicDesktopStyleApplicationLifetime desktop:
				desktop.ShutdownRequested += LifetimeOnShutdownRequested;
				break;
			case ISingleViewApplicationLifetime single:
				// TODO:
				break;
		}

		_stateMachine.Start();
	}

	private enum Trigger
	{
		Invalid = 0,
		Hide,
		Show,
		ShutdownPrevented,
		ShutdownRequested,
		MainWindowClosed,
	}

	private enum State
	{
		Invalid = 0,
		InitialState,
		Closed,
		Open,
	}

	internal UiContext UiContext { get; }
	internal ApplicationViewModel ApplicationViewModel { get; }

	private void LifetimeOnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
	{
		// Shutdown prevention will only work if you directly run the executable.
		bool shouldShutdown = ApplicationViewModel.CanShutdown(_restartRequest, out bool isShutdownEnforced) || isShutdownEnforced;

		e.Cancel = !shouldShutdown;
		Logger.LogDebug($"Cancellation of the shutdown set to: {e.Cancel}.");

		_stateMachine.Fire(shouldShutdown ? Trigger.ShutdownRequested : Trigger.ShutdownPrevented);
	}

	private void CreateAndShowMainWindow()
	{
		if (_lifetime is not IClassicDesktopStyleApplicationLifetime desktop)
		{
			return;
		}

		if (desktop.MainWindow is { })
		{
			return;
		}

		var result = new MainWindow
		{
			DataContext = MainViewModel.Instance
		};

		_compositeDisposable?.Dispose();
		_compositeDisposable = new();

		Observable.FromEventPattern<CancelEventArgs>(result, nameof(result.Closing))
			.Select(args => (args.EventArgs, !ApplicationViewModel.CanShutdown(false, out bool isShutdownEnforced), isShutdownEnforced))
			.TakeWhile(_ => !_isShuttingDown) // Prevents stack overflow.
			.Subscribe(tup =>
			{
				var (e, preventShutdown, isShutdownEnforced) = tup;

				// Check if Ctrl-C was used to forcefully terminate the app.
				if (isShutdownEnforced)
				{
					_isShuttingDown = true;
					tup.EventArgs.Cancel = false;
					_stateMachine.Fire(Trigger.ShutdownRequested);
				}

				// _hideRequest flag is used to distinguish what is the user's intent.
				// It is only true when the request comes from the Tray.
				if ((Services.UiConfig.HideOnClose || _hideRequest) && App.EnableFeatureHide)
				{
					_hideRequest = false; // request processed, set it back to the default.
					return;
				}

				_isShuttingDown = !preventShutdown;
				e.Cancel = preventShutdown;

				_stateMachine.Fire(preventShutdown ? Trigger.ShutdownPrevented : Trigger.ShutdownRequested);
			})
			.DisposeWith(_compositeDisposable);

		Observable.FromEventPattern(result, nameof(result.Closed))
			.Take(1)
			.Subscribe(_ =>
			{
				_compositeDisposable?.Dispose();
				_compositeDisposable = null;
				_stateMachine.Fire(Trigger.MainWindowClosed);
			})
			.DisposeWith(_compositeDisposable);

		desktop.MainWindow = result;

		if (result.WindowState != WindowState.Maximized)
		{
			SetWindowSize(result);
		}

		ObserveWindowSize(result, _compositeDisposable);

		result.Show();

		ApplicationViewModel.IsMainWindowShown = true;
	}

	private void CreateAndShowMainView()
	{
		if (_lifetime is not ISingleViewApplicationLifetime single)
		{
			return;
		}

		if (single.MainView is { })
		{
			return;
		}

		var result = new Shell
		{
			DataContext = MainViewModel.Instance
		};

		// TODO: Handle Closing event from Android/iOS.

		// TODO: Handle Close event from Android/iOS.

		// This needs to be somehow wired up in mobile projects
		// as avalonia single lifetime and main view does not support this

		single.MainView = result;

		ApplicationViewModel.IsMainWindowShown = true;
	}

	private void SetWindowSize(Window window)
	{
		var configWidth = Services.UiConfig.WindowWidth;
		var configHeight = Services.UiConfig.WindowHeight;
		var currentScreen = window.Screens.ScreenFromPoint(window.Position);

		if (configWidth is null || configHeight is null || currentScreen is null)
		{
			return;
		}

		var isValidWidth = configWidth <= currentScreen.WorkingArea.Width && configWidth >= window.MinWidth;
		var isValidHeight = configHeight <= currentScreen.WorkingArea.Height && configHeight >= window.MinHeight;

		if (isValidWidth && isValidHeight)
		{
			window.Width = configWidth.Value;
			window.Height = configHeight.Value;
		}
	}

	private void ObserveWindowSize(Window window, CompositeDisposable disposables)
	{
		window
			.WhenAnyValue(x => x.Bounds)
			.Skip(1)
			.Where(b => b != default && window.WindowState == WindowState.Normal)
			.Subscribe(b =>
			{
				Services.UiConfig.WindowWidth = b.Width;
				Services.UiConfig.WindowHeight = b.Height;
			})
			.DisposeWith(disposables);
	}

	void IMainWindowService.Show()
	{
		_stateMachine.Fire(Trigger.Show);
	}

	void IMainWindowService.Hide()
	{
		_hideRequest = true;
		_stateMachine.Fire(Trigger.Hide);
	}

	void IMainWindowService.Shutdown(bool restart)
	{
		_restartRequest = restart;

		bool shouldShutdown = ApplicationViewModel.CanShutdown(_restartRequest, out bool isShutdownEnforced) || isShutdownEnforced;
		_stateMachine.Fire(shouldShutdown ? Trigger.ShutdownRequested : Trigger.ShutdownPrevented);
	}
}
