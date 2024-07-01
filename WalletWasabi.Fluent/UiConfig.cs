using Newtonsoft.Json;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using WalletWasabi.Bases;
using WalletWasabi.Fluent.Converters;
using System.Runtime.Serialization;
using System.Runtime.InteropServices;

namespace WalletWasabi.Fluent;

[JsonObject(MemberSerialization.OptIn)]
public class UiConfig : ConfigBase
{
	private bool _privacyMode;
	private bool _isCustomChangeAddress;
	private bool _autocopy;
	private int _feeDisplayUnit;
	private bool _darkModeEnabled;
	private string? _lastSelectedWallet;
	private string _windowState = "Normal";
	private bool _runOnSystemStartup;
	private bool _oobe;
	private bool _hideOnClose;
	private bool _autoPaste;
	private int _feeTarget;
	private bool _sendAmountConversionReversed;
	private double? _windowWidth;
	private double? _windowHeight;

	public UiConfig() : base()
	{
	}

	public UiConfig(string filePath) : base(filePath)
	{
		this.WhenAnyValue(
				x => x.Autocopy,
				x => x.AutoPaste,
				x => x.IsCustomChangeAddress,
				x => x.DarkModeEnabled,
				x => x.FeeDisplayUnit,
				x => x.LastSelectedWallet,
				x => x.WindowState,
				x => x.Oobe,
				x => x.RunOnSystemStartup,
				x => x.PrivacyMode,
				x => x.HideOnClose,
				x => x.FeeTarget,
				(_, _, _, _, _, _, _, _, _, _, _, _) => Unit.Default)
			.Throttle(TimeSpan.FromMilliseconds(500))
			.Skip(1) // Won't save on UiConfig creation.
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(_ => ToFile());

		this.WhenAnyValue(x => x.SendAmountConversionReversed)
			.Throttle(TimeSpan.FromMilliseconds(500))
			.Skip(1) // Won't save on UiConfig creation.
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(_ => ToFile());

		this.WhenAnyValue(
				x => x.WindowWidth,
				x => x.WindowHeight)
			.Throttle(TimeSpan.FromMilliseconds(500))
			.Skip(1) // Won't save on UiConfig creation.
			.ObserveOn(RxApp.TaskpoolScheduler)
			.Subscribe(_ => ToFile());
	}

	[JsonProperty(PropertyName = "Oobe", DefaultValueHandling = DefaultValueHandling.Populate)]
	[DefaultValue(true)]
	public bool Oobe
	{
		get => _oobe;
		set => RaiseAndSetIfChanged(ref _oobe, value);
	}

	[JsonProperty(PropertyName = "WindowState")]
	[JsonConverter(typeof(WindowStateAfterStartJsonConverter))]
	public string WindowState
	{
		get => _windowState;
		internal set => RaiseAndSetIfChanged(ref _windowState, value);
	}

	[DefaultValue(2)]
	[JsonProperty(PropertyName = "FeeTarget", DefaultValueHandling = DefaultValueHandling.Populate)]
	public int FeeTarget
	{
		get => _feeTarget;
		internal set => RaiseAndSetIfChanged(ref _feeTarget, value);
	}

	[DefaultValue(0)]
	[JsonProperty(PropertyName = "FeeDisplayUnit", DefaultValueHandling = DefaultValueHandling.Populate)]
	public int FeeDisplayUnit
	{
		get => _feeDisplayUnit;
		set => RaiseAndSetIfChanged(ref _feeDisplayUnit, value);
	}

	[DefaultValue(true)]
	[JsonProperty(PropertyName = "Autocopy", DefaultValueHandling = DefaultValueHandling.Populate)]
	public bool Autocopy
	{
		get => _autocopy;
		set => RaiseAndSetIfChanged(ref _autocopy, value);
	}

	[DefaultValue(false)]
	[JsonProperty(PropertyName = nameof(AutoPaste), DefaultValueHandling = DefaultValueHandling.Populate)]
	public bool AutoPaste
	{
		get => _autoPaste;
		set => RaiseAndSetIfChanged(ref _autoPaste, value);
	}

	[DefaultValue(false)]
	[JsonProperty(PropertyName = "IsCustomChangeAddress", DefaultValueHandling = DefaultValueHandling.Populate)]
	public bool IsCustomChangeAddress
	{
		get => _isCustomChangeAddress;
		set => RaiseAndSetIfChanged(ref _isCustomChangeAddress, value);
	}

	[DefaultValue(false)]
	[JsonProperty(PropertyName = "PrivacyMode", DefaultValueHandling = DefaultValueHandling.Populate)]
	public bool PrivacyMode
	{
		get => _privacyMode;
		set => RaiseAndSetIfChanged(ref _privacyMode, value);
	}

	[DefaultValue(true)]
	[JsonProperty(PropertyName = "DarkModeEnabled", DefaultValueHandling = DefaultValueHandling.Populate)]
	public bool DarkModeEnabled
	{
		get => _darkModeEnabled;
		set => RaiseAndSetIfChanged(ref _darkModeEnabled, value);
	}

	[DefaultValue(null)]
	[JsonProperty(PropertyName = "LastSelectedWallet", DefaultValueHandling = DefaultValueHandling.Populate)]
	public string? LastSelectedWallet
	{
		get => _lastSelectedWallet;
		set => RaiseAndSetIfChanged(ref _lastSelectedWallet, value);
	}

	// OnDeserialized changes this default on Linux.
	[DefaultValue(true)]
	[JsonProperty(PropertyName = "RunOnSystemStartup", DefaultValueHandling = DefaultValueHandling.Populate)]
	public bool RunOnSystemStartup
	{
		get => _runOnSystemStartup;
		set => RaiseAndSetIfChanged(ref _runOnSystemStartup, value);
	}

	[DefaultValue(true)]
	[JsonProperty(PropertyName = "HideOnClose", DefaultValueHandling = DefaultValueHandling.Populate)]
	public bool HideOnClose
	{
		get => _hideOnClose;
		set => RaiseAndSetIfChanged(ref _hideOnClose, value);
	}

	[DefaultValue(false)]
	[JsonProperty(PropertyName = "SendAmountConversionReversed", DefaultValueHandling = DefaultValueHandling.Populate)]
	public bool SendAmountConversionReversed
	{
		get => _sendAmountConversionReversed;
		internal set => RaiseAndSetIfChanged(ref _sendAmountConversionReversed, value);
	}

	[JsonProperty(PropertyName = "WindowWidth")]
	public double? WindowWidth
	{
		get => _windowWidth;
		internal set => RaiseAndSetIfChanged(ref _windowWidth, value);
	}

	[JsonProperty(PropertyName = "WindowHeight")]
	public double? WindowHeight
	{
		get => _windowHeight;
		internal set => RaiseAndSetIfChanged(ref _windowHeight, value);
	}

	[OnDeserialized]
	internal void OnDeserialized(StreamingContext context)
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) // On win this works perfectly. By default Wasabi will run after startup.
		{
			return;
		}

		if (!Oobe) // We do not touch anything if it is not the first run.
		{
			return;
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) // On Linux we do not start Wasabi with OS by default - because Linux users knows better.
		{
			RunOnSystemStartup = false;
		}
	}
}
