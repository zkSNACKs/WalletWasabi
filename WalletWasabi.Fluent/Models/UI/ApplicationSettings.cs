using System.Globalization;
using Avalonia.Controls;
using NBitcoin;
using ReactiveUI;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using WalletWasabi.Bases;
using WalletWasabi.Daemon;
using WalletWasabi.Exceptions;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Infrastructure;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Userfacing;
using Unit = System.Reactive.Unit;

namespace WalletWasabi.Fluent.Models.UI;

[AppLifetime]
[AutoInterface]
public partial class ApplicationSettings : ReactiveObject
{
	private const int ThrottleTime = 500;

	private readonly Subject<bool> _isRestartNeeded = new();
	private readonly string _persistentConfigFilePath;
	private readonly PersistentConfig _startupConfig;
	private readonly Config _config;
	private readonly UiConfig _uiConfig;

	// Advanced
	[AutoNotify] private bool _enableGpu;
	[AutoNotify] private string _backendUri;

	// Bitcoin
	[AutoNotify] private Network _network;

	[AutoNotify] private bool _startLocalBitcoinCoreOnStartup;
	[AutoNotify] private string _localBitcoinCoreDataDir;
	[AutoNotify] private bool _stopLocalBitcoinCoreOnShutdown;
	[AutoNotify] private string _bitcoinP2PEndPoint;
	[AutoNotify] private string _dustThreshold;

	// Coordinator
	[AutoNotify] private string _mainNetCoordinatorUri;
	[AutoNotify] private string _testNetCoordinatorUri;
	[AutoNotify] private string _regTestCoordinatorUri;
	[AutoNotify] private string _maxCoordinationFeeRate;
	[AutoNotify] private string _maxCoinJoinMiningFeeRate;
	[AutoNotify] private string _absoluteMinInputCount;

	// General
	[AutoNotify] private bool _darkModeEnabled;

	[AutoNotify] private bool _autoCopy;
	[AutoNotify] private bool _autoPaste;
	[AutoNotify] private bool _customChangeAddress;
	[AutoNotify] private FeeDisplayUnit _selectedFeeDisplayUnit;
	[AutoNotify] private bool _runOnSystemStartup;
	[AutoNotify] private bool _hideOnClose;
	[AutoNotify] private TorMode _useTor;
	[AutoNotify] private bool _terminateTorOnExit;
	[AutoNotify] private bool _downloadNewVersion;

	// Privacy Mode
	[AutoNotify] private bool _privacyMode;

	[AutoNotify] private bool _oobe;
	[AutoNotify] private bool _showCoordinatorAnnouncement;
	[AutoNotify] private WindowState _windowState;

	// Non-persistent
	[AutoNotify] private bool _doUpdateOnClose;

	public ApplicationSettings(string persistentConfigFilePath, PersistentConfig persistentConfig, Config config, UiConfig uiConfig)
	{
		_persistentConfigFilePath = persistentConfigFilePath;
		_startupConfig = persistentConfig;

		_config = config;
		_uiConfig = uiConfig;

		// Advanced
		_enableGpu = _startupConfig.EnableGpu;
		_backendUri = _startupConfig.GetBackendUri();

		// Bitcoin
		_network = config.Network;
		_startLocalBitcoinCoreOnStartup = _startupConfig.StartLocalBitcoinCoreOnStartup;
		_localBitcoinCoreDataDir = _startupConfig.LocalBitcoinCoreDataDir;
		_stopLocalBitcoinCoreOnShutdown = _startupConfig.StopLocalBitcoinCoreOnShutdown;
		_bitcoinP2PEndPoint = _startupConfig.GetBitcoinP2pEndPoint().ToString(defaultPort: -1);
		_dustThreshold = _startupConfig.DustThreshold.ToString();

		// Coordinator
		_mainNetCoordinatorUri = _startupConfig.MainNetCoordinatorUri;
		_testNetCoordinatorUri = _startupConfig.TestNetCoordinatorUri;
		_regTestCoordinatorUri = _startupConfig.RegTestCoordinatorUri;
		_maxCoordinationFeeRate = _startupConfig.MaxCoordinationFeeRate.ToString(CultureInfo.InvariantCulture);
		_maxCoinJoinMiningFeeRate = _startupConfig.MaxCoinJoinMiningFeeRate.ToString(CultureInfo.InvariantCulture);
		_absoluteMinInputCount = _startupConfig.AbsoluteMinInputCount.ToString(CultureInfo.InvariantCulture);

		// General
		_darkModeEnabled = _uiConfig.DarkModeEnabled;
		_autoCopy = _uiConfig.Autocopy;
		_autoPaste = _uiConfig.AutoPaste;
		_customChangeAddress = _uiConfig.IsCustomChangeAddress;
		_selectedFeeDisplayUnit = Enum.IsDefined(typeof(FeeDisplayUnit), _uiConfig.FeeDisplayUnit)
			? (FeeDisplayUnit)_uiConfig.FeeDisplayUnit
			: FeeDisplayUnit.Satoshis;
		_runOnSystemStartup = _uiConfig.RunOnSystemStartup;
		_hideOnClose = _uiConfig.HideOnClose;
		_useTor = Config.ObjectToTorMode(_config.UseTor);
		_terminateTorOnExit = _startupConfig.TerminateTorOnExit;
		_downloadNewVersion = _startupConfig.DownloadNewVersion;

		// Privacy Mode
		_privacyMode = _uiConfig.PrivacyMode;

		_oobe = _uiConfig.Oobe;
		_showCoordinatorAnnouncement = _uiConfig.ShowCoordinatorAnnouncement;

		_windowState = (WindowState)Enum.Parse(typeof(WindowState), _uiConfig.WindowState);

		// Save on change
		var configSaveTrigger1 =
			this.WhenAnyValue(
					x => x.EnableGpu,
					x => x.Network,
					x => x.StartLocalBitcoinCoreOnStartup,
					x => x.LocalBitcoinCoreDataDir,
					x => x.StopLocalBitcoinCoreOnShutdown,
					x => x.BitcoinP2PEndPoint,
					x => x.DustThreshold,
					x => x.UseTor,
					x => x.TerminateTorOnExit,
					x => x.DownloadNewVersion,
					(_, _, _, _, _, _, _, _, _, _) => Unit.Default)
				.Skip(1);
		var configSaveTrigger2 =
			this.WhenAnyValue(
					x => x.MaxCoordinationFeeRate,
					x => x.MaxCoinJoinMiningFeeRate,
					x => x.AbsoluteMinInputCount,
					x => x.MainNetCoordinatorUri,
					x => x.TestNetCoordinatorUri,
					x => x.RegTestCoordinatorUri,
					(_, _, _, _, _, _) => Unit.Default)
				.Skip(1);

		Observable
			.Merge(configSaveTrigger1, configSaveTrigger2)
			.ObserveOn(RxApp.MainThreadScheduler)
			.Throttle(TimeSpan.FromMilliseconds(ThrottleTime))
			.Do(_ => Save())
			.Subscribe();


		// Save UiConfig on change
		this.WhenAnyValue(
				x => x.DarkModeEnabled,
				x => x.AutoCopy,
				x => x.AutoPaste,
				x => x.CustomChangeAddress,
				x => x.SelectedFeeDisplayUnit,
				x => x.RunOnSystemStartup,
				x => x.HideOnClose,
				x => x.Oobe,
				x => x.ShowCoordinatorAnnouncement,
				x => x.WindowState)
			.Skip(1)
			.Throttle(TimeSpan.FromMilliseconds(ThrottleTime))
			.Do(_ => ApplyUiConfigChanges())
			.Subscribe();

		// Save UiConfig on change without throttling
		this.WhenAnyValue(x => x.PrivacyMode)
			.Skip(1)
			.Do(_ => ApplyUiConfigPrivacyModeChange())
			.Subscribe();

		// Set Default BitcoinCoreDataDir if required
		this.WhenAnyValue(x => x.StartLocalBitcoinCoreOnStartup)
			.Skip(1)
			.Where(value => value && string.IsNullOrEmpty(LocalBitcoinCoreDataDir))
			.Subscribe(_ => LocalBitcoinCoreDataDir = EnvironmentHelpers.GetDefaultBitcoinCoreDataDirOrEmptyString());

		// Apply RunOnSystemStartup
		this.WhenAnyValue(x => x.RunOnSystemStartup)
			.DoAsync(async _ => await StartupHelper.ModifyStartupSettingAsync(RunOnSystemStartup))
			.Subscribe();

		// Apply DoUpdateOnClose
		this.WhenAnyValue(x => x.DoUpdateOnClose)
			.Do(x => Services.UpdateManager.DoUpdateOnClose = x)
			.Subscribe();
	}

	public bool IsOverridden => _config.IsOverridden;

	public IObservable<bool> IsRestartNeeded => _isRestartNeeded;

	public bool CheckIfRestartIsNeeded(PersistentConfig config)
	{
		return !_startupConfig.DeepEquals(config);
	}

	private void Save()
	{
		RxApp.MainThreadScheduler.Schedule(
			() =>
			{
				try
				{
					PersistentConfig currentConfig = ConfigManagerNg.LoadFile<PersistentConfig>(_persistentConfigFilePath);
					PersistentConfig newConfig = ApplyChanges(currentConfig);
					ConfigManagerNg.ToFile(_persistentConfigFilePath, newConfig);

					_isRestartNeeded.OnNext(CheckIfRestartIsNeeded(newConfig));
				}
				catch (Exception ex)
				{
					Logger.LogDebug(ex);
				}
			});
	}

	private PersistentConfig ApplyChanges(PersistentConfig config)
	{
		PersistentConfig result = config;

		// Advanced
		result = result with { EnableGpu = EnableGpu };

		// Bitcoin
		if (Network == config.Network)
		{
			if (EndPointParser.TryParse(BitcoinP2PEndPoint, Network.DefaultPort, out EndPoint? endPoint))
			{
				if (Network == Network.Main)
				{
					result = result with { MainNetBitcoinP2pEndPoint = endPoint };
				}
				else if (Network == Network.TestNet)
				{
					result = result with { TestNetBitcoinP2pEndPoint = endPoint };
				}
				else if (Network == Network.RegTest)
				{
					result = result with { RegTestBitcoinP2pEndPoint = endPoint };
				}
				else
				{
					throw new NotSupportedNetworkException(Network);
				}
			}

			result = result with { MainNetCoordinatorUri = MainNetCoordinatorUri };
			result = result with { TestNetCoordinatorUri = TestNetCoordinatorUri };
			result = result with { RegTestCoordinatorUri = RegTestCoordinatorUri };

			if (Network == Network.Main)
			{
				result = result with { MainNetBackendUri = BackendUri };
			}
			else if (Network == Network.TestNet)
			{
				result = result with { TestNetBackendUri = BackendUri };
			}
			else if (Network == Network.RegTest)
			{
				result = result with { RegTestBackendUri = BackendUri };
			}
			else
			{
				throw new NotSupportedNetworkException(Network);
			}

			result = result with
			{
				StartLocalBitcoinCoreOnStartup = StartLocalBitcoinCoreOnStartup,
				StopLocalBitcoinCoreOnShutdown = StopLocalBitcoinCoreOnShutdown,
				LocalBitcoinCoreDataDir = Guard.Correct(LocalBitcoinCoreDataDir),
				DustThreshold = decimal.TryParse(DustThreshold, out var threshold) ? Money.Coins(threshold) : PersistentConfig.DefaultDustThreshold,
				MaxCoordinationFeeRate = decimal.TryParse(MaxCoordinationFeeRate, out var maxCoordinationFeeRate) ? maxCoordinationFeeRate : Constants.DefaultMaxCoordinationFeeRate,
				MaxCoinJoinMiningFeeRate = decimal.TryParse(MaxCoinJoinMiningFeeRate, out var maxCoinjoinMiningFeeRate) ? maxCoinjoinMiningFeeRate : Constants.DefaultMaxCoinJoinMiningFeeRate,
				AbsoluteMinInputCount = int.TryParse(AbsoluteMinInputCount, out var absoluteMinInputCount) ? absoluteMinInputCount : Constants.DefaultAbsoluteMinInputCount
			};
		}
		else
		{
			result = result with
			{
				Network = Network
			};

			BitcoinP2PEndPoint = result.GetBitcoinP2pEndPoint().ToString(defaultPort: -1);
			BackendUri = result.GetBackendUri();
		}

		// General
		result = result with
		{
			UseTor = UseTor.ToString(),
			TerminateTorOnExit = TerminateTorOnExit,
			DownloadNewVersion = DownloadNewVersion,
		};

		return result;
	}

	public bool TryProcessCoordinatorConnectionString(CoordinatorConnectionString coordinatorConnectionString)
	{
		// Sanity checks
		if (coordinatorConnectionString.CoordinatorFeePercent > 1)
		{
			Logger.LogWarning($"New intended coordinator fee rate was {coordinatorConnectionString.CoordinatorFeePercent}%, but absolute max is 1%");
			return false;
		}

		if (coordinatorConnectionString.AbsoluteMinInputCount < 2)
		{
			Logger.LogWarning($"New intended absolute min input count was {coordinatorConnectionString.AbsoluteMinInputCount}, but absolute min is 2");
			return false;
		}

		if (!TrySetCoordinatorUri(coordinatorConnectionString.Endpoint.ToString(), coordinatorConnectionString.Network))
		{
			return false;
		}

		MaxCoordinationFeeRate = coordinatorConnectionString.CoordinatorFeePercent.ToString(CultureInfo.InvariantCulture);
		AbsoluteMinInputCount = coordinatorConnectionString.AbsoluteMinInputCount.ToString();

		// TODO: Save Name and ReadMoreUri to display it after.

		return true;
	}

	public bool TrySetCoordinatorUri(string uri, Network? network = null)
	{
		network ??= Network;

		if (network == Network.Main)
		{
			MainNetCoordinatorUri = uri;
			return true;
		}

		if (network == Network.TestNet)
		{
			TestNetCoordinatorUri = uri;
			return true;
		}

		if (network == Network.RegTest)
		{
			RegTestCoordinatorUri = uri;
			return true;
		}

		Logger.LogWarning($"Coordinator URI didn't change due to unknown network: {network}");
		return false;
	}

	public string GetCoordinatorUri()
	{
		if (Network == Network.Main)
		{
			return MainNetCoordinatorUri;
		}

		if (Network == Network.TestNet)
		{
			return TestNetCoordinatorUri;
		}

		if (Network == Network.RegTest)
		{
			return RegTestCoordinatorUri;
		}

		throw new NotSupportedNetworkException(Network);
	}

	private void ApplyUiConfigChanges()
	{
		_uiConfig.DarkModeEnabled = DarkModeEnabled;
		_uiConfig.Autocopy = AutoCopy;
		_uiConfig.AutoPaste = AutoPaste;
		_uiConfig.IsCustomChangeAddress = CustomChangeAddress;
		_uiConfig.FeeDisplayUnit = (int)SelectedFeeDisplayUnit;
		_uiConfig.RunOnSystemStartup = RunOnSystemStartup;
		_uiConfig.HideOnClose = HideOnClose;
		_uiConfig.Oobe = Oobe;
		_uiConfig.ShowCoordinatorAnnouncement = ShowCoordinatorAnnouncement;
		_uiConfig.WindowState = WindowState.ToString();
	}

	private void ApplyUiConfigPrivacyModeChange()
	{
		_uiConfig.PrivacyMode = PrivacyMode;
	}
}
