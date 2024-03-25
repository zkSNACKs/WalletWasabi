using NBitcoin;
using ReactiveUI;
using System.Reactive.Linq;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Infrastructure;
using WalletWasabi.Models;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.Models.Wallets;

[AppLifetime]
[AutoInterface]
public partial class WalletSettingsModel : ReactiveObject
{
	private readonly KeyManager _keyManager;
	private bool _isDirty;

	[AutoNotify] private bool _isNewWallet;
	[AutoNotify] private bool _autoCoinjoin;
	[AutoNotify] private bool _isCoinjoinProfileSelected;
	[AutoNotify] private bool _preferPsbtWorkflow;
	[AutoNotify] private Money _plebStopThreshold;
	[AutoNotify] private int _anonScoreTarget;
	[AutoNotify] private bool _redCoinIsolation;
	[AutoNotify] private CoinjoinSkipFactors _coinjoinSkipFactors;
	[AutoNotify] private int _feeRateMedianTimeFrameHours;

	public WalletSettingsModel(KeyManager keyManager, bool isNewWallet = false, bool isCoinJoinPaused = false)
	{
		_keyManager = keyManager;

		_isNewWallet = isNewWallet;
		_isDirty = isNewWallet;
		IsCoinJoinPaused = isCoinJoinPaused;

		_autoCoinjoin = _keyManager.AutoCoinJoin;
		_isCoinjoinProfileSelected = _keyManager.IsCoinjoinProfileSelected;
		_preferPsbtWorkflow = _keyManager.PreferPsbtWorkflow;
		_plebStopThreshold = _keyManager.PlebStopThreshold ?? KeyManager.DefaultPlebStopThreshold;
		_anonScoreTarget = _keyManager.AnonScoreTarget;
		_redCoinIsolation = _keyManager.RedCoinIsolation;
		_coinjoinSkipFactors = _keyManager.CoinjoinSkipFactors;
		_feeRateMedianTimeFrameHours = _keyManager.FeeRateMedianTimeFrameHours;

		WalletType = WalletHelpers.GetType(_keyManager);

		this.WhenAnyValue(
			x => x.AutoCoinjoin,
			x => x.IsCoinjoinProfileSelected,
			x => x.PreferPsbtWorkflow,
			x => x.PlebStopThreshold,
			x => x.AnonScoreTarget,
			x => x.RedCoinIsolation,
			x => x.FeeRateMedianTimeFrameHours)
			.Skip(1)
			.Do(_ => SetValues())
			.Subscribe();

		// This should go to the previous WhenAnyValue, it's just that it's not working for some reason.
		this.WhenAnyValue(
			x => x.CoinjoinSkipFactors)
			.Skip(1)
			.Do(_ => SetValues())
			.Subscribe();
	}

	public WalletType WalletType { get; }

	public bool IsCoinJoinPaused { get; set; }

	/// <summary>
	/// Saves to current configuration to file.
	/// </summary>
	/// <returns>The unique ID of the wallet.</returns>
	public WalletId Save()
	{
		if (_isDirty)
		{
			_keyManager.ToFile();

			if (IsNewWallet)
			{
				Services.WalletManager.AddWallet(_keyManager);
				IsNewWallet = false;
			}

			_isDirty = false;
		}

		return Services.WalletManager.GetWalletByName(_keyManager.WalletName).WalletId;
	}

	private void SetValues()
	{
		_keyManager.AutoCoinJoin = AutoCoinjoin;
		_keyManager.IsCoinjoinProfileSelected = IsCoinjoinProfileSelected;
		_keyManager.PreferPsbtWorkflow = PreferPsbtWorkflow;
		_keyManager.PlebStopThreshold = PlebStopThreshold;
		_keyManager.AnonScoreTarget = AnonScoreTarget;
		_keyManager.RedCoinIsolation = RedCoinIsolation;
		_keyManager.CoinjoinSkipFactors = CoinjoinSkipFactors;
		_keyManager.SetFeeRateMedianTimeFrame(FeeRateMedianTimeFrameHours);

		_isDirty = true;
	}
}
