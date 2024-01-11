using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Daemon;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Models;
using WalletWasabi.Fluent.Models.ClientConfig;
using WalletWasabi.Fluent.Models.FileSystem;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.ViewModels;
using WalletWasabi.Fluent.ViewModels.SearchBar.Sources;
using WalletWasabi.Logging;

namespace WalletWasabi.Fluent;

public class App : Application
{
	private readonly bool _startInBg;
	private readonly Func<Task>? _backendInitialiseAsync;
	private ApplicationStateManager? _applicationStateManager;

	static App()
	{
		// TODO: This is temporary workaround until https://github.com/zkSNACKs/WalletWasabi/issues/8151 is fixed.
		EnableFeatureHide = !RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
	}

	public App()
	{
		Name = "Wasabi Wallet";
	}

	public App(Func<Task> backendInitialiseAsync, bool startInBg) : this()
	{
		_startInBg = startInBg;
		_backendInitialiseAsync = backendInitialiseAsync;
	}

	public static bool EnableFeatureHide { get; private set; }

	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	// TODO: Refactor WalletWasabi.Fluent.Desktop.Program class so it can be reused in mobile projects (if needed)
	// - Program
	//   - Main
	//   - TerminateApplication
	//   - LogUnobservedTaskException
	//   - LogUnhandledException
	//   - BuildAvaloniaApp
	//   - BuildCrashReporterApp
	// - WasabiAppExtensions
	//   - RunAsGuiAsync
	//   - AfterStarting

	public override void OnFrameworkInitializationCompleted()
	{
		if (!Design.IsDesignMode && ApplicationLifetime is not null)
		{
			var uiContext = CreateUiContext();
			UiContext.Default = uiContext;
			_applicationStateManager =
				new ApplicationStateManager(ApplicationLifetime, uiContext, _startInBg);

			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			{
				DataContext = _applicationStateManager.ApplicationViewModel;

				desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
				desktop.Exit += (_, _) => OnExit(uiContext);

				RxApp.MainThreadScheduler.Schedule(
					async () =>
					{
						await _backendInitialiseAsync!(); // Guaranteed not to be null when not in designer.

						MainViewModel.Instance.Initialize();
					});

				// Not available on mobile, only supported on Desktop
				InitializeTrayIcons();
			}
			else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
			{
				DataContext = _applicationStateManager.ApplicationViewModel;

				// TODO: Handle ShutdownMode.OnExplicitShutdown in iOS/Android projects.

				// TODO: Handle Exit command in iOS/Android projects.
				// OnExit(uiContext);

				// TODO: Moved to AfterStarting method.
				/*
				RxApp.MainThreadScheduler.Schedule(
					async () =>
					{
						// TODO: Handle AppBuilder.Configure to initialize App from ctor to run _backendInitialiseAsync
						// TODO: Use WasabiAppExtensions.AfterStarting and not WasabiAppExtensions.RunAsGuiAsync
						await _backendInitialiseAsync!(); // Guaranteed not to be null when not in designer.

						MainViewModel.Instance.Initialize();
					});
				*/

				// Not available on mobile, only supported on Desktop
				// TODO: InitializeTrayIcons();
			}
		}

		base.OnFrameworkInitializationCompleted();
#if DEBUG
		if (CanRunDevTools())
		{
			this.AttachDevTools();
		}
#endif
	}

	public static Task AfterStarting(
		WasabiApplication app,
		Func<AppBuilder, AppBuilder> setupAppBuilder,
		AppBuilder? builder)
	{
		RxApp.DefaultExceptionHandler = Observer.Create<Exception>(ex =>
		{
			if (Debugger.IsAttached)
			{
				Debugger.Break();
			}

			Logger.LogError(ex);

			RxApp.MainThreadScheduler.Schedule(() =>
				throw new ApplicationException("Exception has been thrown in unobserved ThrownExceptions", ex));
		});

		Logger.LogInfo("Wasabi GUI started.");
		bool runGuiInBackground = app.AppConfig.Arguments.Any(arg => arg.Contains(StartupHelper.SilentArgument));
		UiConfig uiConfig = LoadOrCreateUiConfig(Config.DataDir);
		Services.Initialize(app.Global!, uiConfig, app.SingleInstanceChecker, app.TerminateService);

		// TOOD: Move to caller in mobile ?
		// using CancellationTokenSource stopLoadingCts = new();
		CancellationTokenSource stopLoadingCts = new();

		async Task BackendInitialise()
		{
			// macOS require that Avalonia is started with the UI thread. Hence this call must be delayed to this point.
			await app.Global!.InitializeNoWalletAsync(app.TerminateService, stopLoadingCts.Token)
				.ConfigureAwait(false);

			// Make sure that wallet startup set correctly regarding RunOnSystemStartup
			await StartupHelper.ModifyStartupSettingAsync(uiConfig.RunOnSystemStartup).ConfigureAwait(false);
		}

		void AfterSetup()
		{
			ThemeHelper.ApplyTheme(uiConfig.DarkModeEnabled ? Theme.Dark : Theme.Light);
			if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
			{
				RxApp.MainThreadScheduler.Schedule(
					async () =>
					{
						// TODO: Handle AppBuilder.Configure to initialize App from ctor to run _backendInitialiseAsync
						// TODO: Use WasabiAppExtensions.AfterStarting and not WasabiAppExtensions.RunAsGuiAsync
						await BackendInitialise(); // Guaranteed not to be null when not in designer.

						MainViewModel.Instance.Initialize();
					});
			}
		}

		AppBuilder? appBuilder;

		// TODO: Do not create appBuilder if called from CustomizeAppBuilder (Android/iOS)
		if (builder is null)
		{
			appBuilder = AppBuilder
				.Configure(() => new App(
					backendInitialiseAsync: BackendInitialise,
					startInBg: runGuiInBackground))
				.AfterSetup(_ => AfterSetup())
				.UseReactiveUI();
		}
		else
		{
			appBuilder = builder;

			appBuilder
				.AfterSetup(_ => AfterSetup())
				.UseReactiveUI();

			// TODO:
			//	backendInitialiseAsync: BackendInitialise,
			//	startInBg: runGuiInBackground))
		}

		appBuilder = setupAppBuilder(appBuilder);

		if (app.TerminateService.CancellationToken.IsCancellationRequested)
		{
			Logger.LogDebug("Skip starting Avalonia UI as requested the application to stop.");
			stopLoadingCts.Cancel();
		}
		else
		{
			// TODO: Refactor as on mobile we do not run Desktop lifetime.
			if (!OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS())
			{
				appBuilder.StartWithClassicDesktopLifetime(app.AppConfig.Arguments);
			}
		}

		return Task.CompletedTask;
	}

	private static UiConfig LoadOrCreateUiConfig(string dataDir)
	{
		Directory.CreateDirectory(dataDir);

		UiConfig uiConfig = new(Path.Combine(dataDir, "UiConfig.json"));
		uiConfig.LoadFile(createIfMissing: true);

		return uiConfig;
	}

	private static void OnExit(UiContext uiContext)
	{
		MainViewModel.Instance.ClearStacks();
		uiContext.HealthMonitor.Dispose();
	}

	private bool CanRunDevTools()
	{
		return !OperatingSystem.IsAndroid()
		       && !OperatingSystem.IsIOS();
	}

	private void InitializeTrayIcons()
	{
		// TODO: This is temporary workaround until https://github.com/zkSNACKs/WalletWasabi/issues/8151 is fixed.
		var trayIcon = TrayIcon.GetIcons(this).FirstOrDefault();
		if (trayIcon is not null)
		{
			if (this.TryFindResource(EnableFeatureHide ? "DefaultNativeMenu" : "MacOsNativeMenu", out var nativeMenu))
			{
				trayIcon.Menu = nativeMenu as NativeMenu;
			}
		}
	}

	// It begins to show that we're re-inventing DI, aren't we?
	private static IWalletRepository CreateWalletRepository(IAmountProvider amountProvider)
	{
		return new WalletRepository(amountProvider);
	}

	private static IHardwareWalletInterface CreateHardwareWalletInterface()
	{
		return new HardwareWalletInterface();
	}

	private static IFileSystem CreateFileSystem()
	{
		return new FileSystemModel();
	}

	private static IClientConfig CreateConfig()
	{
		return new ClientConfigModel();
	}

	private static IApplicationSettings CreateApplicationSettings()
	{
		return new ApplicationSettings(Services.PersistentConfigFilePath, Services.PersistentConfig, Services.Config, Services.UiConfig);
	}

	private static ITransactionBroadcasterModel CreateBroadcaster(Network network)
	{
		return new TransactionBroadcasterModel(network);
	}

	private static IAmountProvider CreateAmountProvider()
	{
		return new AmountProvider(Services.Synchronizer);
	}

	private UiContext CreateUiContext()
	{
		var amountProvider = CreateAmountProvider();

		var applicationSettings = CreateApplicationSettings();
		var torStatusChecker = new TorStatusCheckerModel();

		// This class (App) represents the actual Avalonia Application and it's sole presence means we're in the actual runtime context (as opposed to unit tests)
		// Once all ViewModels have been refactored to receive UiContext as a constructor parameter, this static singleton property can be removed.
		return new UiContext(
			new QrCodeGenerator(),
			new QrCodeReader(),
			new UiClipboard(),
			CreateWalletRepository(amountProvider),
			new CoinjoinModel(),
			CreateHardwareWalletInterface(),
			CreateFileSystem(),
			CreateConfig(),
			applicationSettings,
			CreateBroadcaster(applicationSettings.Network),
			amountProvider,
			new EditableSearchSourceSource(),
			torStatusChecker,
			new LegalDocumentsProvider(),
			new HealthMonitor(applicationSettings, torStatusChecker));
	}
}
