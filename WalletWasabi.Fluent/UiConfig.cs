using Newtonsoft.Json;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using WalletWasabi.Bases;
using WalletWasabi.Fluent.Converters;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace WalletWasabi.Fluent;

[JsonObject(MemberSerialization.OptIn)]
public record UiConfig : ConfigBase
{
	private bool _privacyMode;
	private bool _isCustomChangeAddress;
	private bool _autocopy;
	private int _feeDisplayUnit;
	private bool _darkModeEnabled;
	private string? _lastSelectedWallet;
	private string _windowState = "Normal";
	private int? _windowX;
	private int? _windowY;
	private double? _windowWidth;
	private double? _windowHeight;
	private bool _runOnSystemStartup;
	private bool _oobe;
	private bool _hideOnClose;
	private bool _autoPaste;
	private int _feeTarget;
	private bool _sendAmountConversionReversed;

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
				x => x.WindowState,
				x => x.WindowX,
				x => x.WindowY,
				x => x.WindowWidth,
				x => x.WindowHeight,
				(_, _, _, _, _) => Unit.Default)
			.Throttle(TimeSpan.FromMilliseconds(750))
			.Skip(1) // Won't save on UiConfig creation.
			.ObserveOn(RxApp.MainThreadScheduler)
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

	[JsonProperty(PropertyName = "WindowX")]
	public int? WindowX
	{
		get => _windowX;
		internal set => RaiseAndSetIfChanged(ref _windowX, value);
	}

	[JsonProperty(PropertyName = "WindowY")]
	public int? WindowY
	{
		get => _windowY;
		internal set => RaiseAndSetIfChanged(ref _windowY, value);
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

	[DefaultValue(false)]
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

	public event PropertyChangedEventHandler? PropertyChanged;

	protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	protected bool RaiseAndSetIfChanged<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
		{
			return false;
		}

		field = value;
		OnPropertyChanged(propertyName);
		return true;
	}
}
