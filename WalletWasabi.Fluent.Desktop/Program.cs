using System.Diagnostics;
using Avalonia;
using Avalonia.ReactiveUI;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ReactiveUI;
using System.Linq;
using Avalonia.OpenGL;
using WalletWasabi.Fluent.CrashReport;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.ViewModels;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using System.Diagnostics.CodeAnalysis;
using WalletWasabi.Fluent.Desktop.Extensions;
using System.Net.Sockets;
using System.Collections.ObjectModel;
using WalletWasabi.Daemon;
using LogLevel = WalletWasabi.Logging.LogLevel;

namespace WalletWasabi.Fluent.Desktop;

public class Program
{
	// Initialization code. Don't use any Avalonia, third-party APIs or any
	// SynchronizationContext-reliant code before AppMain is called: things aren't initialized
	// yet and stuff might break.
	public static async Task<int> Main(string[] args)
	{
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

			if (Services.UpdateManager.DoUpdateOnClose)
			{
				Services.UpdateManager.StartInstallingNewVersion();
			}

			return exitCode;
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
		MainViewModel.Instance.ClearStacks();
		MainViewModel.Instance.StatusIcon.Dispose();
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
			.With(new Win32PlatformOptions { AllowEglInitialization = false, UseDeferredRendering = true })
			.With(new X11PlatformOptions { UseGpu = false, WmClass = "Wasabi Wallet Crash Reporting" })
			.With(new AvaloniaNativePlatformOptions { UseDeferredRendering = true, UseGpu = false })
			.With(new MacOSPlatformOptions { ShowInDock = true })
			.AfterSetup(_ => ThemeHelper.ApplyTheme(Theme.Dark));
	}
}

public static class WasabiAppExtensions
{
	public static async Task<int> RunAsGuiAsync(this WasabiApplication app)
	{
		return await app.RunAsync(
			() =>
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

				Logger.LogSoftwareStarted("Wasabi GUI");
				bool runGuiInBackground = app.AppConfig.Arguments.Any(arg => arg.Contains(StartupHelper.SilentArgument));
				UiConfig uiConfig = LoadOrCreateUiConfig(Config.DataDir);
				Services.Initialize(app.Global!, uiConfig, app.SingleInstanceChecker);

				AppBuilder
					.Configure(() => new App(
						async () =>
						{
							// Make sure that wallet startup set correctly regarding RunOnSystemStartup
							await StartupHelper.ModifyStartupSettingAsync(uiConfig.RunOnSystemStartup)
								.ConfigureAwait(false);
						}, runGuiInBackground))
					.UseReactiveUI()
					.SetupAppBuilder()
					.AfterSetup(_ =>
					{
						var glInterface = AvaloniaLocator.CurrentMutable.GetService<IPlatformOpenGlInterface>();
						Logger.LogInfo(glInterface is { } ? $"Renderer: {glInterface.PrimaryContext.GlInterface.Renderer}"
							: "Renderer: Avalonia Software");

						ThemeHelper.ApplyTheme(uiConfig.DarkModeEnabled ? Theme.Dark : Theme.Light);
					})
					.StartWithClassicDesktopLifetime(app.AppConfig.Arguments);
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
