using Microsoft.Win32;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WalletWasabi.Helpers;

namespace WalletWasabi.Fluent.Helpers
{
	public static class StartupHelper
	{
		private const string KeyPath = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";

		// Arguments to add Wasabi to macOS startup settings.
		private static readonly string AddCmd = $"osascript -e \' tell application \"System Events\" to make new login item at end with properties {{name:\"{Constants.AppName}\", path:\"/Applications/{Constants.AppName}.app\", hidden:true}} \'";

		// Arguments to delete Wasabi from macOS startup settings.
		private static readonly string DeleteCmd = $"osascript -e \' tell application \"System Events\" to delete login item \"{Constants.AppName}\" \'";

		public static async Task ModifyStartupSettingAsync(bool runOnSystemStartup)
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				string pathToExeFile = EnvironmentHelpers.GetExecutablePath();
				if (!File.Exists(pathToExeFile))
				{
					throw new InvalidOperationException($"Path {pathToExeFile} does not exist.");
				}
				StartOnWindowsStartup(runOnSystemStartup, pathToExeFile);
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				await StartOnLinuxStartupAsync(runOnSystemStartup);
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				if (runOnSystemStartup)
				{
					await EnvironmentHelpers.ShellExecAsync(AddCmd).ConfigureAwait(false);
				}
				else
				{
					await EnvironmentHelpers.ShellExecAsync(DeleteCmd).ConfigureAwait(false);
				}
			}
		}

		private static void StartOnWindowsStartup(bool runOnSystemStartup, string pathToExeFile)
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				throw new InvalidOperationException("Registry modification can only be done on Windows.");
			}

			using RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true) ?? throw new InvalidOperationException("Registry operation failed.");
			if (runOnSystemStartup)
			{
				key.SetValue(nameof(WalletWasabi), pathToExeFile);
			}
			else
			{
				key.DeleteValue(nameof(WalletWasabi), false);
			}
		}

		private static async Task StartOnLinuxStartupAsync(bool runOnSystemStartup)
		{
			string pathToDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart");
			string pathToDesktopFile = Path.Combine(pathToDir, "Wasabi.desktop");

			IoHelpers.EnsureContainingDirectoryExists(pathToDesktopFile);

			if (runOnSystemStartup)
			{
				string pathToExec = EnvironmentHelpers.GetExecutablePath();
				string fileContents = string.Join(
					"\n",
					"[Desktop Entry]",
					$"Name={Constants.AppName}",
					"Type=Application",
					$"Exec={pathToExec}",
					"Hidden=false",
					"Terminal=false",
					"X-GNOME-Autostart-enabled=true");

				await File.WriteAllTextAsync(pathToDesktopFile, fileContents).ConfigureAwait(false);
			}
			else
			{
				File.Delete(pathToDesktopFile);
			}
		}
	}
}
