using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using Avalonia;
using Avalonia.iOS;
using Avalonia.ReactiveUI;
using Foundation;
using WalletWasabi.Daemon;
using WalletWasabi.Logging;

namespace WalletWasabi.Fluent.IOS;

// The UIApplicationDelegate for the application. This class is responsible for launching the
// User Interface of the application, as well as listening (and optionally responding) to
// application events from iOS.
[Register("AppDelegate")]
public class AppDelegate : AvaloniaAppDelegate<App>
{
	private WasabiApplication? _app;

	protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
	{
		// TODO: Crash reporting

		Log.Error("WASABI", "CustomizeAppBuilder");

		try
		{
			Global.IsTorEnabled = false;

			// TODO: Do we need on iOS EnsureSingleInstance with true?
			_app = WasabiAppBuilder
				.Create("Wasabi GUI", System.Array.Empty<string>())
				.EnsureSingleInstance(false)
				.OnUnhandledExceptions(LogUnhandledException)
				.OnUnobservedTaskExceptions(LogUnobservedTaskException)
				.OnTermination(TerminateApplication)
				.Build();

			// TODO: WasabiAppExtensions.RunAsDesktopGuiAsync
			_app.RunAsyncMobile(afterStarting:
				() => App.AfterStarting(_app, AppBuilderIOSExtension.SetupAppBuilder, builder));
		}
		catch (Exception ex)
		{
			// TODO:
			// CrashReporter.Invoke(ex);

			Logger.LogCritical(ex);

			Log.Error("WASABI", $"{ex}");
		}

		return base.CustomizeAppBuilder(builder);
	}

	/// <summary>
	/// Do not call this method it should only be called by TerminateService.
	/// </summary>
	private static void TerminateApplication()
	{
		// TODO:
		// Dispatcher.UIThread.Post(() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Close());
	}

	private static void LogUnobservedTaskException(object? sender, AggregateException e)
	{
		ReadOnlyCollection<Exception> innerExceptions = e.Flatten().InnerExceptions;

		switch (innerExceptions)
		{
			case [SocketException { SocketErrorCode: SocketError.OperationAborted }]:
			// Source of this exception is NBitcoin library.
			case [OperationCanceledException { Message: "The peer has been disconnected" }]:
				// Until https://github.com/MetacoSA/NBitcoin/pull/1089 is resolved.
				Logger.LogTrace(e);
				break;

			default:
				Logger.LogDebug(e);
				break;
		}
	}

	private static void LogUnhandledException(object? sender, Exception e) =>
		Logger.LogWarning(e);

	[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members",
		Justification = "Required to bootstrap Avalonia's Visual Previewer")]
	private static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilderIOSExtension.SetupAppBuilder(AppBuilder.Configure(() => new App()).UseReactiveUI());
	}
}
