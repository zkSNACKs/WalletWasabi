using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Logging;
using WalletWasabi.Microservices;

namespace WalletWasabi.Helpers
{
	public static class EnvironmentHelpers
	{
		[Flags]
		private enum EXECUTION_STATE : uint
		{
			ES_AWAYMODE_REQUIRED = 0x00000040,
			ES_CONTINUOUS = 0x80000000,
			ES_DISPLAY_REQUIRED = 0x00000002,
			ES_SYSTEM_REQUIRED = 0x00000001
		}

		// appName, dataDir
		private static ConcurrentDictionary<string, string> DataDirDict { get; } = new ConcurrentDictionary<string, string>();

		// Do not change the output of this function. Backwards compatibility depends on it.
		public static string GetDataDir(string appName)
		{
			if (DataDirDict.TryGetValue(appName, out string? dataDir))
			{
				return dataDir;
			}

			string directory;

			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				var home = Environment.GetEnvironmentVariable("HOME");
				if (!string.IsNullOrEmpty(home))
				{
					directory = Path.Combine(home, "." + appName.ToLowerInvariant());
					Logger.LogInfo($"Using HOME environment variable for initializing application data at `{directory}`.");
				}
				else
				{
					throw new DirectoryNotFoundException("Could not find suitable datadir.");
				}
			}
			else
			{
				var localAppData = Environment.GetEnvironmentVariable("APPDATA");
				if (!string.IsNullOrEmpty(localAppData))
				{
					directory = Path.Combine(localAppData, appName);
					Logger.LogInfo($"Using APPDATA environment variable for initializing application data at `{directory}`.");
				}
				else
				{
					throw new DirectoryNotFoundException("Could not find suitable datadir.");
				}
			}

			if (Directory.Exists(directory))
			{
				DataDirDict.TryAdd(appName, directory);
				return directory;
			}

			Logger.LogInfo($"Creating data directory at `{directory}`.");
			Directory.CreateDirectory(directory);

			DataDirDict.TryAdd(appName, directory);
			return directory;
		}

		/// <summary>
		/// Gets Bitcoin <c>datadir</c> parameter from:
		/// <list type="bullet">
		/// <item><c>APPDATA</c> environment variable on Windows, and</item>
		/// <item><c>HOME</c> environment variable on other platforms.</item>
		/// </list>
		/// </summary>
		/// <returns><c>datadir</c> or empty string.</returns>
		/// <seealso href="https://en.bitcoin.it/wiki/Data_directory"/>
		public static string GetDefaultBitcoinCoreDataDirOrEmptyString()
		{
			string directory = "";

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				var localAppData = Environment.GetEnvironmentVariable("APPDATA");
				if (!string.IsNullOrEmpty(localAppData))
				{
					directory = Path.Combine(localAppData, "Bitcoin");
				}
				else
				{
					Logger.LogDebug($"Could not find suitable default {Constants.BuiltinBitcoinNodeName} datadir.");
				}
			}
			else
			{
				var home = Environment.GetEnvironmentVariable("HOME");
				if (!string.IsNullOrEmpty(home))
				{
					directory = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
						? Path.Combine(home, "Library", "Application Support", "Bitcoin")
						: Path.Combine(home, ".bitcoin"); // Linux
				}
				else
				{
					Logger.LogDebug($"Could not find suitable default {Constants.BuiltinBitcoinNodeName} datadir.");
				}
			}

			return directory;
		}

		// This method removes the path and file extension.
		//
		// Given Wasabi releases are currently built using Windows, the generated assemblies contain
		// the hardcoded "C:\Users\User\Desktop\WalletWasabi\.......\FileName.cs" string because that
		// is the real path of the file, it doesn't matter what OS was targeted.
		// In Windows and Linux that string is a valid path and that means Path.GetFileNameWithoutExtension
		// can extract the file name but in the case of OSX the same string is not a valid path so, it assumes
		// the whole string is the file name.
		public static string ExtractFileName(string callerFilePath)
		{
			var lastSeparatorIndex = callerFilePath.LastIndexOf("\\");
			if (lastSeparatorIndex == -1)
			{
				lastSeparatorIndex = callerFilePath.LastIndexOf("/");
			}

			var fileName = callerFilePath;

			if (lastSeparatorIndex != -1)
			{
				lastSeparatorIndex++;
				fileName = callerFilePath[lastSeparatorIndex..]; // From lastSeparatorIndex until the end of the string.
			}

			var fileNameWithoutExtension = fileName.TrimEnd(".cs", StringComparison.InvariantCultureIgnoreCase);
			return fileNameWithoutExtension;
		}

		/// <summary>
		/// Executes a command with Bourne shell.
		/// https://stackoverflow.com/a/47918132/2061103
		/// </summary>
		public static async Task ShellExecAsync(string cmd, bool waitForExit = true)
		{
			ProcessStartInfo startInfo = GetShellProcessStartInfo(cmd);

			if (waitForExit)
			{
				using ProcessAsync process = new(startInfo);
				process.Start();

				await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

				if (process.ExitCode != 0)
				{
					Logger.LogError($"{nameof(ShellExecAsync)} command: {cmd} exited with exit code: {process.ExitCode}, instead of 0.");
				}
			}
			else
			{
				using var process = Process.Start(startInfo);
			}
		}

		public static ProcessStartInfo GetShellProcessStartInfo(string cmd)
		{
			string escapedArgs = cmd.Replace("\"", "\\\"");

			ProcessStartInfo startInfo = new()
			{
				FileName = "/usr/bin/env",
				Arguments = $"sh -c \"{escapedArgs}\"",
				RedirectStandardOutput = true,
				UseShellExecute = false,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden
			};

			return startInfo;
		}

		public static bool IsFileTypeAssociated(string fileExtension)
		{
			// Source article: https://edi.wang/post/2019/3/4/read-and-write-windows-registry-in-net-core

			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				throw new InvalidOperationException("Operation only supported on windows.");
			}

			fileExtension = fileExtension.TrimStart('.'); // Remove . if added by the caller.

			using (var key = Registry.ClassesRoot.OpenSubKey($".{fileExtension}"))
			{
				// Read the (Default) value.
				if (key?.GetValue(null) is not null)
				{
					return true;
				}
			}
			return false;
		}

		public static string GetFullBaseDirectory()
		{
			var fullBaseDirectory = Path.GetFullPath(AppContext.BaseDirectory);

			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				if (!fullBaseDirectory.StartsWith('/'))
				{
					fullBaseDirectory = fullBaseDirectory.Insert(0, "/");
				}
			}

			return fullBaseDirectory;
		}

		public static string GetExecutablePath()
		{
			var fullBaseDir = GetFullBaseDirectory();
			var wassabeeFileName = Path.Combine(fullBaseDir, Constants.ExecutableName);
			wassabeeFileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"{wassabeeFileName}.exe" : $"{wassabeeFileName}";
			if (File.Exists(wassabeeFileName))
			{
				return wassabeeFileName;
			}
			var assemblyName = Assembly.GetEntryAssembly()?.GetName().Name ?? throw new NullReferenceException("Assembly or Assembly's Name was null.");
			var fluentExecutable = Path.Combine(fullBaseDir, assemblyName);
			return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"{fluentExecutable}.exe" : $"{fluentExecutable}";
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

		/// <summary>
		/// Reset the system sleep timer, this method has to be called from time to time to prevent sleep.
		/// It does not prevent the display to turn off.
		/// </summary>
		public static async Task ProlongSystemAwakeAsync()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				// Reset the system sleep timer.
				var result = SetThreadExecutionState(EXECUTION_STATE.ES_SYSTEM_REQUIRED);
				if (result == 0)
				{
					throw new InvalidOperationException("SetThreadExecutionState failed.");
				}
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				// Prevent macOS system from idle sleep and keep it for 1 second. This will reset the idle sleep timer.
				string shellCommand = $"caffeinate -i -t 1";
				await ShellExecAsync(shellCommand, waitForExit: true).ConfigureAwait(false);
			}
		}
	}
}
