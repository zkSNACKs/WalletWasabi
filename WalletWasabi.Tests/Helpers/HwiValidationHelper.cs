using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using WalletWasabi.Hwi.Models;

namespace WalletWasabi.Tests.Helpers;

public static class HwiValidationHelper
{
	/// <summary>
	/// Parse the wallet's path from the response.
	/// </summary>
	/// <param name="path">The wallet path which come from HWI enumerate command.</param>
	/// <param name="model">The hardware wallet model.</param>
	/// <param name="osPlatform">Mock test specific platform</param>
	/// <returns><c>true</c> if the path matches the model's regex, <c>false</c> otherwise.</returns>
	public static bool ValidatePathString(HardwareWalletModels model, string path, OSPlatform? osPlatform = null)
	{
		string? pattern = null;

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || (osPlatform != null && osPlatform == OSPlatform.Windows))
		{
			pattern = model switch
			{
				HardwareWalletModels.Trezor_T => "^webusb:",
				HardwareWalletModels.Trezor_1 => @"(?i)^\\\\\?\\HID#VID_534C&PID_0001",
				HardwareWalletModels.Coldcard => @"(?i)^\\\\\?\\HID#VID_D13E&PID_CC10",
				HardwareWalletModels.Ledger_Nano_S or HardwareWalletModels.Ledger_Nano_X => @"(?i)^\\\\\?\\HID#VID_2C97&PID_0001",
				HardwareWalletModels.Jade => @"^COM\d+",
				HardwareWalletModels.BitBox02_BTCOnly => @"(?i)^\\\\\?\\HID#VID_03EB&PID_2403",
				_ => "",
			};
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || (osPlatform != null && osPlatform == OSPlatform.Linux))
		{
			pattern = model switch
			{
				HardwareWalletModels.Trezor_T => "^webusb:",
				HardwareWalletModels.Trezor_1 => @"\d+(-\d+\.\d+)+:\d+\.\d+",
				HardwareWalletModels.Coldcard => @"\d+(-\d+\.\d+)+:\d+\.\d+",
				HardwareWalletModels.Ledger_Nano_S or HardwareWalletModels.Ledger_Nano_X => @"\d+(-\d+\.\d+)+:\d+\.\d+",
				HardwareWalletModels.Jade => @"/dev/ttyACM\d+",
				HardwareWalletModels.BitBox02_BTCOnly => @"\d+(-\d+\.\d+)+:\d+\.\d+",
				_ => "",
			};
		}
		else
		{
			pattern = model switch
			{
				HardwareWalletModels.Trezor_T => "^webusb:",
				HardwareWalletModels.Trezor_1 => @"DevSrvsID:\d+",
				HardwareWalletModels.Coldcard => @"DevSrvsID:\d+",
				HardwareWalletModels.Ledger_Nano_S or HardwareWalletModels.Ledger_Nano_X => @"DevSrvsID:\d+",
				HardwareWalletModels.Jade => @"/dev/cu\.usbserial-[A-Za-z0-9]+",
				HardwareWalletModels.BitBox02_BTCOnly => @"DevSrvsID:\d+",
				_ => "",
			};
		}

		return Regex.IsMatch(path, pattern);
	}
}
