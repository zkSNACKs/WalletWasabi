using System.Linq;
using System.Reactive.Concurrency;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ChatGPT;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Fluent.Models;
using WalletWasabi.Fluent.Models.ClientConfig;
using WalletWasabi.Fluent.Models.FileSystem;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.ViewModels;
using WalletWasabi.Fluent.ViewModels.SearchBar.Sources;
using WalletWasabi.Services;

namespace WalletWasabi.Fluent;

public class App : Application
{
	private readonly bool _startInBg;
	private readonly Func<Task>? _backendInitialiseAsync;
	private ApplicationStateManager? _applicationStateManager;

	static App()
	{
		// TODO:
		Defaults.ConfigureDefaultServices();
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

	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	public override void OnFrameworkInitializationCompleted()
	{
		if (!Design.IsDesignMode)
		{
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			{
				var uiContext = CreateUiContext();
				UiContext.Default = uiContext;
				_applicationStateManager =
					new ApplicationStateManager(desktop, uiContext, _startInBg);

				DataContext = _applicationStateManager.ApplicationViewModel;

				desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
				desktop.Exit += (sender, args) =>
				{
					MainViewModel.Instance.ClearStacks();
					uiContext.HealthMonitor.Dispose();
				};

				RxApp.MainThreadScheduler.Schedule(
					async () =>
					{
						await _backendInitialiseAsync!(); // Guaranteed not to be null when not in designer.

						MainViewModel.Instance.Initialize();
					});

				InitializeTrayIcons();
			}
		}

		base.OnFrameworkInitializationCompleted();
#if DEBUG
		this.AttachDevTools();
#endif
	}

	private void InitializeTrayIcons()
	{
		// TODO: This is temporary workaround until https://github.com/zkSNACKs/WalletWasabi/issues/8151 is fixed.
		var trayIcon = TrayIcon.GetIcons(this).FirstOrDefault();
		if (trayIcon is not null)
		{
			if (this.TryFindResource("DefaultNativeMenu", out var nativeMenu))
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
		return new AmountProvider(Services.HostedServices.Get<WasabiSynchronizer>());
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
