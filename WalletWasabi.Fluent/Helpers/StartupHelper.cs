using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace WalletWasabi.Fluent.Helpers;

public static class StartupHelper
{
	public const string SilentArgument = "startsilent";

	public static async Task ModifyStartupSettingAsync(bool runOnSystemStartup)
	{
		await MacOsStartupHelper.AddOrRemoveLoginItemAsync(runOnSystemStartup).ConfigureAwait(false);

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			WindowsStartupHelper.AddOrRemoveRegistryKey(runOnSystemStartup);
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			await LinuxStartupHelper.AddOrRemoveDesktopFileAsync(runOnSystemStartup).ConfigureAwait(false);
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			await MacOsStartupHelper.AddOrRemoveLoginItemAsync(runOnSystemStartup).ConfigureAwait(false);
		}
	}
}
