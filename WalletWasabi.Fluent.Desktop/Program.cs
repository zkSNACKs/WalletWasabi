using System.Diagnostics;
using Avalonia;
using Avalonia.ReactiveUI;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ReactiveUI;
using System.Linq;
using WalletWasabi.Fluent.CrashReport;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using System.Diagnostics.CodeAnalysis;
using WalletWasabi.Fluent.Desktop.Extensions;
using System.Net.Sockets;
using System.Collections.ObjectModel;
using WalletWasabi.Daemon;
using LogLevel = WalletWasabi.Logging.LogLevel;
using System.Threading;

namespace WalletWasabi.Fluent.Desktop;

public class Program
{
	// Initialization code. Don't use any Avalonia, third-party APIs or any
	// SynchronizationContext-reliant code before AppMain is called: things aren't initialized
	// yet and stuff might break.
	public static async Task<int> Main(string[] args)
	{
		// TDOO: For testing only
		// await Repl.RunAsync();

		// Crash reporting must be before the "single instance checking".
		Logger.InitializeDefaults(Path.Combine(Config.DataDir, "Logs.txt"), LogLevel.Info);
		try
		{
			if (CrashReporter.TryGetExceptionFromCliArgs(args, out var exceptionToShow))
			{
				// Show the exception.
				BuildCrashReporterApp(exceptionToShow).StartWithClassicDesktopLifetime(args);
				return 1;
			}
		}
		catch (Exception ex)
		{
			// If anything happens here just log it and exit.
			Logger.LogCritical(ex);
			return 1;
		}

		try
		{
			var app = WasabiAppBuilder
				.Create("Wasabi GUI", args)
				.EnsureSingleInstance()
				.OnUnhandledExceptions(LogUnhandledException)
				.OnUnobservedTaskExceptions(LogUnobservedTaskException)
				.OnTermination(TerminateApplication)
				.Build();

			var exitCode = await app.RunAsGuiAsync();

			if (app.TerminateService.GracefulCrashException is not null)
			{
				throw app.TerminateService.GracefulCrashException;
			}

			if (exitCode == ExitCode.Ok && Services.UpdateManager.DoUpdateOnClose)
			{
				Services.UpdateManager.StartInstallingNewVersion();
			}

			return (int)exitCode;
		}
		catch (Exception ex)
		{
			CrashReporter.Invoke(ex);
			Logger.LogCritical(ex);
			return 1;
		}
	}

	/// <summary>
	/// Do not call this method it should only be called by TerminateService.
	/// </summary>
	private static void TerminateApplication()
	{
		Dispatcher.UIThread.Post(() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Close());
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

	[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Required to bootstrap Avalonia's Visual Previewer")]
	private static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure(() => new App()).UseReactiveUI().SetupAppBuilder();

	/// <summary>
	/// Sets up and initializes the crash reporting UI.
	/// </summary>
	/// <param name="serializableException">The serializable exception</param>
	private static AppBuilder BuildCrashReporterApp(SerializableException serializableException)
	{
		var result = AppBuilder
			.Configure(() => new CrashReportApp(serializableException))
			.UseReactiveUI();

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			result
				.UseWin32()
				.UseSkia();
		}
		else
		{
			result.UsePlatformDetect();
		}

		return result
			.With(new Win32PlatformOptions { RenderingMode = new[] { Win32RenderingMode.Software } })
			.With(new X11PlatformOptions { RenderingMode = new[] { X11RenderingMode.Software }, WmClass = "Wasabi Wallet Crash Report" })
			.With(new AvaloniaNativePlatformOptions { RenderingMode = new[] { AvaloniaNativeRenderingMode.Software } })
			.With(new MacOSPlatformOptions { ShowInDock = true })
			.AfterSetup(_ => ThemeHelper.ApplyTheme(Theme.Dark));
	}
}

public static class WasabiAppExtensions
{
	public static async Task<ExitCode> RunAsGuiAsync(this WasabiApplication app)
	{
		return await app.RunAsync(
			afterStarting: () =>
			{
				RxApp.DefaultExceptionHandler = Observer.Create<Exception>(ex =>
				{
					if (Debugger.IsAttached)
					{
						Debugger.Break();
					}

					Logger.LogError(ex);

					RxApp.MainThreadScheduler.Schedule(() => throw new ApplicationException("Exception has been thrown in unobserved ThrownExceptions", ex));
				});

				Logger.LogInfo("Wasabi GUI started.");
				bool runGuiInBackground = app.AppConfig.Arguments.Any(arg => arg.Contains(StartupHelper.SilentArgument));
				UiConfig uiConfig = LoadOrCreateUiConfig(Config.DataDir);
				Services.Initialize(app.Global!, uiConfig, app.SingleInstanceChecker, app.TerminateService);

				using CancellationTokenSource stopLoadingCts = new();

				AppBuilder appBuilder = AppBuilder
					.Configure(() => new App(
						backendInitialiseAsync: async () =>
						{
							// macOS require that Avalonia is started with the UI thread. Hence this call must be delayed to this point.
							await app.Global!.InitializeNoWalletAsync(initializeSleepInhibitor: true, app.TerminateService, stopLoadingCts.Token).ConfigureAwait(false);

							// Make sure that wallet startup set correctly regarding RunOnSystemStartup
							await StartupHelper.ModifyStartupSettingAsync(uiConfig.RunOnSystemStartup).ConfigureAwait(false);
						}, startInBg: runGuiInBackground))
					.UseReactiveUI()
					.SetupAppBuilder()
					.AfterSetup(_ => ThemeHelper.ApplyTheme(uiConfig.DarkModeEnabled ? Theme.Dark : Theme.Light));

				if (app.TerminateService.CancellationToken.IsCancellationRequested)
				{
					Logger.LogDebug("Skip starting Avalonia UI as requested the application to stop.");
					stopLoadingCts.Cancel();
				}
				else
				{
					appBuilder.StartWithClassicDesktopLifetime(app.AppConfig.Arguments);
				}

				return Task.CompletedTask;
			});
	}

	private static UiConfig LoadOrCreateUiConfig(string dataDir)
	{
		Directory.CreateDirectory(dataDir);

		UiConfig uiConfig = new(Path.Combine(dataDir, "UiConfig.json"));
		uiConfig.LoadFile(createIfMissing: true);

		return uiConfig;
	}
}
