using WalletWasabi.Models;

namespace WalletWasabi.Hwi.Models;

/// <summary>
/// Source: https://github.com/bitcoin-core/HWI/pull/228
/// </summary>
public enum HardwareWalletModels
{
	[FriendlyName("Hardware Wallet")]
	Unknown,

	[FriendlyName("Coldcard")]
	Coldcard,

	[FriendlyName("Coldcard Simulator")]
	Coldcard_Simulator,

	[FriendlyName("BitBox")]
	DigitalBitBox_01,

	[FriendlyName("BitBox Simulator")]
	DigitalBitBox_01_Simulator,

	[FriendlyName("KeepKey")]
	KeepKey,

	[FriendlyName("KeepKey Simulator")]
	KeepKey_Simulator,

	[FriendlyName("Ledger Nano S")]
	Ledger_Nano_S,

	[FriendlyName("Ledger Nano S Plus")]
	Ledger_Nano_S_Plus,

	[FriendlyName("Ledger Nano X")]
	Ledger_Nano_X,

	[FriendlyName("Trezor One")]
	Trezor_1,

	[FriendlyName("Trezor One Simulator")]
	Trezor_1_Simulator,

	[FriendlyName("Trezor T")]
	Trezor_T,

	[FriendlyName("Trezor T Simulator")]
	Trezor_T_Simulator,

	[FriendlyName("Trezor Safe 3")]
	Trezor_Safe_3,

	[FriendlyName("BitBox")]
	BitBox02_BTCOnly,

	[FriendlyName("BitBox")]
	BitBox02_Multi,

	[FriendlyName("Jade")]
	Jade,
}
