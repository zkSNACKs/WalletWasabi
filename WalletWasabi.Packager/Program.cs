using NBitcoin;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WalletWasabi.Helpers;
using WalletWasabi.Userfacing;

namespace WalletWasabi.Packager;

/// <summary>
/// Instructions:
/// <list type="number">
/// <item>Bump Client version (or else wrong .msi will be created) - <see cref="Constants.ClientVersion"/>.</item>
/// <item>Publish with Packager.</item>
/// <item>Build WIX project with Release and x64 configuration.</item>
/// <item>Sign with Packager, set restore true so the password won't be kept.</item>
/// </list>
/// <seealso href="https://github.com/zkSNACKs/WalletWasabi/blob/master/WalletWasabi.Documentation/ClientDeployment.md"/>
/// </summary>
public static class Program
{
	public const string PfxPath = "C:\\digicert.pfx";

	public const string DaemonExecutableName = Constants.DaemonExecutableName;
	public const string ExecutableName = Constants.ExecutableName;
	public const string SilentExecutableName = Constants.SilentExecutableName;

	private const string WasabiPrivateKeyFilePath = @"C:\wasabi\Wasabi.privkey";
	private const string WasabiPublicKeyFilePath = @"C:\wasabi\Wasabi.pubkey";

	/// <remarks>Only 64-bit platforms are supported for now.</remarks>
	/// <seealso href="https://docs.microsoft.com/en-us/dotnet/articles/core/rid-catalog"/>
	private static string[] Targets = new[]
	{
		"win-x64",
		"linux-x64",
		"osx-x64",
		"osx-arm64"
	};

	private static string VersionPrefix = Constants.ClientVersion.Revision == 0 ? Constants.ClientVersion.ToString(3) : Constants.ClientVersion.ToString();

	private static bool OnlyBinaries;
	private static bool IsContinuousDelivery;

	public static string PackagerProjectDirectory { get; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
	public static string SolutionDirectory { get; } = Path.GetFullPath(Path.Combine(PackagerProjectDirectory, ".."));
	public static string DesktopProjectDirectory { get; } = Path.GetFullPath(Path.Combine(SolutionDirectory, "WalletWasabi.Fluent.Desktop"));
	public static string StartsilentProjectDirectory { get; } = Path.GetFullPath(Path.Combine(SolutionDirectory, "WalletWasabi.OSXStartsilent"));
	public static string LibraryProjectDirectory { get; } = Path.GetFullPath(Path.Combine(SolutionDirectory, "WalletWasabi"));
	public static string WixProjectDirectory { get; } = Path.GetFullPath(Path.Combine(SolutionDirectory, "WalletWasabi.WindowsInstaller"));
	public static string BinDistDirectory { get; } = Path.GetFullPath(Path.Combine(DesktopProjectDirectory, "bin", "dist"));

	/// <summary>
	/// Main entry point.
	/// </summary>
	private static async Task Main(string[] args)
	{
		var argsProcessor = new ArgsProcessor(args);

		// For now this is enough. If you run it on macOS you want to sign.
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			MacSignTools.Sign(argsProcessor);
			return;
		}

		if (argsProcessor.IsGeneratePrivateKey())
		{
			await WasabiSignerHelpers.GeneratePrivateAndPublicKeyToFileAsync(WasabiPrivateKeyFilePath, WasabiPublicKeyFilePath).ConfigureAwait(false);
			return;
		}

		// Only binaries mode is for deterministic builds.
		OnlyBinaries = argsProcessor.IsOnlyBinariesMode();

		IsContinuousDelivery = argsProcessor.IsContinuousDeliveryMode();

		ReportStatus();

		if (argsProcessor.IsPublish() || IsContinuousDelivery || OnlyBinaries)
		{
			await PublishAsync().ConfigureAwait(false);

			IoHelpers.OpenFolderInFileExplorer(BinDistDirectory);
		}

		if (argsProcessor.IsSign())
		{
			await SignAsync().ConfigureAwait(false);
		}
	}

	private static void ReportStatus()
	{
		if (OnlyBinaries)
		{
			Console.WriteLine("I'll only generate binaries and disregard all other options.");
		}
		Console.WriteLine($"{nameof(VersionPrefix)}:\t\t\t{VersionPrefix}");
		Console.WriteLine($"{nameof(ExecutableName)}:\t\t\t{ExecutableName}");

		Console.WriteLine();
		Console.Write($"{nameof(Targets)}:\t\t\t");
		foreach (var target in Targets)
		{
			if (Targets.Last() != target)
			{
				Console.Write($"{target}, ");
			}
			else
			{
				Console.Write(target);
			}
		}
		Console.WriteLine();
	}

	private static async Task SignAsync()
	{
		foreach (string target in Targets)
		{
			if (target.StartsWith("win", StringComparison.OrdinalIgnoreCase))
			{
				string publishedFolder = Path.Combine(BinDistDirectory, target);

				Console.WriteLine("Move created .msi");
				var msiPath = Path.Combine(WixProjectDirectory, "bin", "Release", "Wasabi.msi");
				var msiFileName = Path.GetFileNameWithoutExtension(msiPath);
				var newMsiPath = Path.Combine(BinDistDirectory, $"{msiFileName}-{VersionPrefix}.msi");

				if (File.Exists(newMsiPath))
				{
					Console.WriteLine("MSI file was already there, skipping code signing phase.");
					continue;
				}

				if (!File.Exists(msiPath))
				{
					throw new Exception(".msi does not exist. Expected path: Wasabi.msi.");
				}

				File.Move(msiPath, newMsiPath);

				Console.Write("Enter Code Signing Certificate Password: ");
				string pfxPassword = PasswordConsole.ReadPassword();

				// Sign code with digicert.
				StartProcessAndWaitForExit("cmd", BinDistDirectory, $"signtool sign /d \"Wasabi Wallet\" /f \"{PfxPath}\" /p {pfxPassword} /t http://timestamp.digicert.com /a \"{newMsiPath}\" && exit");

				await IoHelpers.TryDeleteDirectoryAsync(publishedFolder).ConfigureAwait(false);
				Console.WriteLine($"Deleted {publishedFolder}");
			}
			else if (target.StartsWith("osx", StringComparison.OrdinalIgnoreCase))
			{
				string dmgFileName = target.Contains("arm") ? $"Wasabi-{VersionPrefix}.dmg" : $"Wasabi-{VersionPrefix}-arm64.dmg";
				string destinationFilePath = Path.Combine(BinDistDirectory, dmgFileName);
				if (File.Exists(destinationFilePath))
				{
					continue;
				}

				string dmgFilePath = Path.Combine(Tools.GetSingleUsbDrive(), dmgFileName);

				if (!File.Exists(dmgFilePath))
				{
					throw new Exception(".dmg does not exist.");
				}

				File.Move(dmgFilePath, destinationFilePath);
			}
		}

		Console.WriteLine("Signing final files...");
		var finalFiles = Directory.GetFiles(BinDistDirectory);

		var sha256SumsFilePath = Path.Combine(BinDistDirectory, "SHA256SUMS");

		foreach (var finalFile in finalFiles)
		{
			StartProcessAndWaitForExit("cmd", BinDistDirectory, $"gpg --armor --detach-sign {finalFile} && exit");

			StartProcessAndWaitForExit("cmd", WixProjectDirectory, $"git checkout -- ComponentsGenerated.wxs && exit");

			ExecuteBashCommands(new[] { $"sha256sum {Path.GetFileName(finalFile)} >> SHA256SUMS" });
		}

		StartProcessAndWaitForExit("cmd", BinDistDirectory, $"gpg --sign --digest-algo sha256 -a --clearsign --armor --output SHA256SUMS.asc SHA256SUMS && exit");

		// We do not need this file anymore SHA256SUMS.ASC contains the hashes and the signature as well.
		File.Delete(sha256SumsFilePath);

		using var key = await WasabiSignerHelpers.GetPrivateKeyFromFileAsync(WasabiPrivateKeyFilePath).ConfigureAwait(false);

		// We will sign the whole file with the hashes and the pgp signature.
		var sha256sumAscFilePath = Path.Combine(BinDistDirectory, "SHA256SUMS.asc");
		await WasabiSignerHelpers.SignSha256SumsFileAsync(sha256sumAscFilePath, key).ConfigureAwait(false);

		// Verify back the signature file.
		await WasabiSignerHelpers.VerifySha256SumsFileAsync(sha256sumAscFilePath).ConfigureAwait(false);

		// Verify back Wasabi installer's hashes
		await WasabiSignerHelpers.VerifyInstallerFileHashesAsync(finalFiles, sha256sumAscFilePath).ConfigureAwait(false);

		IoHelpers.OpenFolderInFileExplorer(BinDistDirectory);
	}

	private static async Task PublishAsync()
	{
		if (Directory.Exists(BinDistDirectory))
		{
			await IoHelpers.TryDeleteDirectoryAsync(BinDistDirectory).ConfigureAwait(false);
			Console.WriteLine($"# Deleted {BinDistDirectory}");
		}

		Console.WriteLine($"# Run dotnet restore");
		StartProcessAndWaitForExit("dotnet", DesktopProjectDirectory, arguments: "restore --locked-mode");

		Console.WriteLine($"# Run dotnet clean");
		StartProcessAndWaitForExit("dotnet", DesktopProjectDirectory, arguments: "clean --configuration Release");

		string desktopBinReleaseDirectory = Path.GetFullPath(Path.Combine(DesktopProjectDirectory, "bin", "Release"));
		string libraryBinReleaseDirectory = Path.GetFullPath(Path.Combine(LibraryProjectDirectory, "bin", "Release"));

		if (Directory.Exists(desktopBinReleaseDirectory))
		{
			await IoHelpers.TryDeleteDirectoryAsync(desktopBinReleaseDirectory).ConfigureAwait(false);
			Console.WriteLine($"# Deleted {desktopBinReleaseDirectory}");
		}

		if (Directory.Exists(libraryBinReleaseDirectory))
		{
			await IoHelpers.TryDeleteDirectoryAsync(libraryBinReleaseDirectory).ConfigureAwait(false);
			Console.WriteLine($"# Deleted {libraryBinReleaseDirectory}");
		}

		var deterministicFileNameTag = IsContinuousDelivery ? $"{DateTimeOffset.UtcNow:ddMMyyyy}{DateTimeOffset.UtcNow.TimeOfDay.TotalSeconds}" : VersionPrefix;
		var deliveryPath = IsContinuousDelivery ? Path.Combine(BinDistDirectory, "cdelivery") : BinDistDirectory;

		IoHelpers.EnsureDirectoryExists(deliveryPath);
		Console.WriteLine($"# Binaries will be delivered here: {deliveryPath}");

		string buildInfoJson = GetBuildInfoData();

		CheckUncommittedGitChanges();

		foreach (string target in Targets)
		{
			string publishedFolder = Path.Combine(BinDistDirectory, target);
			string currentBinDistDirectory = publishedFolder;

			Console.WriteLine();
			Console.WriteLine($"# Packaging for platform '{target}' to folder:\t{currentBinDistDirectory}");

			Console.WriteLine();
			if (!Directory.Exists(currentBinDistDirectory))
			{
				Directory.CreateDirectory(currentBinDistDirectory);
				Console.WriteLine($"# Created {currentBinDistDirectory}");
			}

			string buildInfoPath = Path.Combine(currentBinDistDirectory, "BUILDINFO.json");
			File.WriteAllText(buildInfoPath, buildInfoJson);

			StartProcessAndWaitForExit("dotnet", DesktopProjectDirectory, arguments: "clean");

			// See https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-publish for details.
			string dotnetProcessArgs = string.Join(
				" ",
				$"publish",
				$"--configuration Release",
				$"--force",
				$"--output \"{currentBinDistDirectory}\"",
				$"--self-contained true",
				$"--runtime \"{target}\"",
				$"--disable-parallel",
				$"--no-cache",
				$"--no-restore",
				$"/p:VersionPrefix={VersionPrefix}",
				$"/p:DebugType=none",
				$"/p:DebugSymbols=false",
				$"/p:ErrorReport=none",
				$"/p:DocumentationFile=\"\"",
				$"/p:Deterministic=true");

			StartProcessAndWaitForExit(
				"dotnet",
				DesktopProjectDirectory,
				arguments: dotnetProcessArgs,
				redirectStandardOutput: true);

			Tools.ClearSha512Tags(currentBinDistDirectory);

			// Remove Tor binaries that are not relevant to the platform.
			var toNotRemove = "";
			if (target.StartsWith("win"))
			{
				toNotRemove = "win";
			}
			else if (target.StartsWith("linux"))
			{
				toNotRemove = "lin";
			}
			else if (target.StartsWith("osx"))
			{
				toNotRemove = "osx";
			}

			// Remove binaries that are not relevant to the platform.
			var binaryFolder = new DirectoryInfo(Path.Combine(currentBinDistDirectory, "Microservices", "Binaries"));

			foreach (var dir in binaryFolder.EnumerateDirectories())
			{
				if (!dir.Name.Contains(toNotRemove, StringComparison.OrdinalIgnoreCase))
				{
					await IoHelpers.TryDeleteDirectoryAsync(dir.FullName).ConfigureAwait(false);
				}
			}

			// Rename WalletWasabi.Fluent.Desktop(.exe) -> wassabee(.exe).
			string executableExtension = target.StartsWith("win") ? ".exe" : "";
			string oldExecutablePath = Path.Combine(currentBinDistDirectory, $"WalletWasabi.Fluent.Desktop{executableExtension}");
			string newExecutablePath = Path.Combine(currentBinDistDirectory, $"{ExecutableName}{executableExtension}");
			File.Move(oldExecutablePath, newExecutablePath);

			// Rename WalletWasabi.Daemon(.exe) -> wassabeed(.exe).
			oldExecutablePath = Path.Combine(currentBinDistDirectory, $"WalletWasabi.Daemon{executableExtension}");
			newExecutablePath = Path.Combine(currentBinDistDirectory, $"{DaemonExecutableName}{executableExtension}");
			File.Move(oldExecutablePath, newExecutablePath);

			// Delete unused executables.
			File.Delete(Path.Combine(currentBinDistDirectory, $"WalletWasabi.Fluent{executableExtension}"));

			if (target.StartsWith("osx"))
			{
				// Deliver WalletWasabi.OSXStartsilent as well.
				ReleaseOSXStartsilent(target);
			}

			// IF IT'S IN ONLYBINARIES MODE DON'T DO ANYTHING FANCY PACKAGING AFTER THIS!!!
			if (OnlyBinaries)
			{
				continue;
			}

			long installedSizeKb = Tools.DirSize(new DirectoryInfo(publishedFolder)) / 1000;

			if (target.StartsWith("win"))
			{
				ZipFile.CreateFromDirectory(currentBinDistDirectory, Path.Combine(deliveryPath, $"Wasabi-{deterministicFileNameTag}-{GetPackageTargetPostfix(target)}.zip"));

				if (IsContinuousDelivery)
				{
					continue;
				}
			}
			else if (target.StartsWith("osx"))
			{
				ZipFile.CreateFromDirectory(currentBinDistDirectory, Path.Combine(deliveryPath, $"Wasabi-{deterministicFileNameTag}-{GetPackageTargetPostfix(target)}.zip"));

				if (IsContinuousDelivery)
				{
					continue;
				}

				// Only add postfix to the final package if arm64, otherwise nothing.
				var postfix = target.Contains("arm64") ? "-arm64" : "";

				// After notarization this will be the filename of the dmg file.
				var zipFileName = $"WasabiToNotarize-{deterministicFileNameTag}{postfix}.zip";
				var zipFilePath = Path.Combine(BinDistDirectory, zipFileName);

				ZipFile.CreateFromDirectory(currentBinDistDirectory, zipFilePath);

				await IoHelpers.TryDeleteDirectoryAsync(currentBinDistDirectory).ConfigureAwait(false);
				Console.WriteLine($"# Deleted {currentBinDistDirectory}");

				string drive = Tools.GetSingleUsbDrive();
				string targetFilePath = Path.Combine(drive, zipFileName);

				try
				{
					File.Move(zipFilePath, targetFilePath, overwrite: true);
					Console.WriteLine($"# Moved '{zipFilePath}' unsigned zip file to the USB disk drive ('{targetFilePath}').");
				}
				catch (Exception ex)
				{
					Console.WriteLine($"# There was an error during moving '{zipFilePath}' file to the USB disk drive ('{targetFilePath}'): '{ex.Message}'. Ignoring.");
				}
			}
			else if (target.StartsWith("linux"))
			{
				ZipFile.CreateFromDirectory(currentBinDistDirectory, Path.Combine(deliveryPath, $"Wasabi-{deterministicFileNameTag}-{GetPackageTargetPostfix(target)}.zip"));

				if (IsContinuousDelivery)
				{
					continue;
				}

				Console.WriteLine("# Create Linux .tar.gz");

				if (!Directory.Exists(publishedFolder))
				{
					throw new Exception($"{publishedFolder} does not exist.");
				}

				var newFolderName = $"Wasabi-{VersionPrefix}";
				var newFolderPath = Path.Combine(BinDistDirectory, newFolderName);

				Console.WriteLine($"# Move '{publishedFolder}' to '{newFolderPath}'.");
				Directory.Move(publishedFolder, newFolderPath);
				publishedFolder = newFolderPath;
				string chmodExecutablesArgs = $$"""-type f \( -name '{{ExecutableName}}' -o -name '{{DaemonExecutableName}}' -o -name 'hwi' -o -name 'bitcoind' -o -name 'tor' \) -exec chmod +x {} \;""";

				string[] commands = new string[]
				{
					$"sudo find ./{newFolderName} -type f -exec chmod 644 {{}} \\;",
					$"sudo find ./{newFolderName} {chmodExecutablesArgs}",
					$"tar -pczvf {newFolderName}.tar.gz {newFolderName}",
				};

				ExecuteBashCommands(commands);

				Console.WriteLine("# Create Linux .deb");

				var debFolderRelativePath = "deb";
				var debFolderPath = Path.Combine(BinDistDirectory, debFolderRelativePath);
				var linuxUsrLocalBinFolder = "/usr/local/bin/";
				var debUsrLocalBinFolderRelativePath = Path.Combine(debFolderRelativePath, "usr", "local", "bin");
				var debUsrLocalBinFolderPath = Path.Combine(BinDistDirectory, debUsrLocalBinFolderRelativePath);
				Directory.CreateDirectory(debUsrLocalBinFolderPath);
				var debUsrAppFolderRelativePath = Path.Combine(debFolderRelativePath, "usr", "share", "applications");
				var debUsrAppFolderPath = Path.Combine(BinDistDirectory, debUsrAppFolderRelativePath);
				Directory.CreateDirectory(debUsrAppFolderPath);
				var debUsrShareIconsFolderRelativePath = Path.Combine(debFolderRelativePath, "usr", "share", "icons", "hicolor");
				var debUsrShareIconsFolderPath = Path.Combine(BinDistDirectory, debUsrShareIconsFolderRelativePath);
				var debianFolderRelativePath = Path.Combine(debFolderRelativePath, "DEBIAN");
				var debianFolderPath = Path.Combine(BinDistDirectory, debianFolderRelativePath);
				Directory.CreateDirectory(debianFolderPath);
				newFolderName = "wasabiwallet";
				var linuxWasabiWalletFolder = Tools.LinuxPathCombine(linuxUsrLocalBinFolder, newFolderName);
				var newFolderRelativePath = Path.Combine(debUsrLocalBinFolderRelativePath, newFolderName);
				newFolderPath = Path.Combine(BinDistDirectory, newFolderRelativePath);
				Directory.Move(publishedFolder, newFolderPath);

				var assetsFolder = Path.Combine(DesktopProjectDirectory, "Assets");
				var assetsInfo = new DirectoryInfo(assetsFolder);

				foreach (var file in assetsInfo.EnumerateFiles())
				{
					var number = file.Name.Split(new string[] { "WasabiLogo", ".png" }, StringSplitOptions.RemoveEmptyEntries);
					if (number.Length == 1 && int.TryParse(number.First(), out int size))
					{
						string destinationFolder = Path.Combine(debUsrShareIconsFolderPath, $"{size}x{size}", "apps");
						Directory.CreateDirectory(destinationFolder);
						file.CopyTo(Path.Combine(destinationFolder, $"{ExecutableName}.png"));
					}
				}

				var controlFilePath = Path.Combine(debianFolderPath, "control");

				// License format does not yet work, but should work in the future, it's work in progress: https://bugs.launchpad.net/ubuntu/+source/software-center/+bug/435183
				var controlFileContent = $"Package: {ExecutableName}\n" +
					$"Priority: optional\n" +
					$"Section: utils\n" +
					$"Maintainer: zkSNACKs Ltd <info@zksnacks.com>\n" +
					$"Version: {VersionPrefix}\n" +
					$"Homepage: https://wasabiwallet.io\n" +
					$"Vcs-Git: git://github.com/zkSNACKs/WalletWasabi.git\n" +
					$"Vcs-Browser: https://github.com/zkSNACKs/WalletWasabi\n" +
					$"Architecture: amd64\n" +
					$"License: Open Source (MIT)\n" +
					$"Installed-Size: {installedSizeKb}\n" +
					$"Recommends: policykit-1\n" +
					$"Description: open-source, non-custodial, privacy focused Bitcoin wallet\n" +
					$"  Built-in Tor, coinjoin, payjoin and coin control features.\n";

				File.WriteAllText(controlFilePath, controlFileContent, Encoding.ASCII);

				var desktopFilePath = Path.Combine(debUsrAppFolderPath, $"{ExecutableName}.desktop");
				var desktopFileContent = $"[Desktop Entry]\n" +
					$"Type=Application\n" +
					$"Name=Wasabi Wallet\n" +
					$"StartupWMClass=Wasabi Wallet\n" +
					$"GenericName=Bitcoin Wallet\n" +
					$"Comment=Privacy focused Bitcoin wallet.\n" +
					$"Icon={ExecutableName}\n" +
					$"Terminal=false\n" +
					$"Exec={ExecutableName}\n" +
					$"Categories=Office;Finance;\n" +
					$"Keywords=bitcoin;wallet;crypto;blockchain;wasabi;privacy;anon;awesome;\n";

				File.WriteAllText(desktopFilePath, desktopFileContent, Encoding.ASCII);

				const string Shebang = "#!/usr/bin/env sh\n";
				var wasabiStarterScriptPath = Path.Combine(debUsrLocalBinFolderPath, $"{ExecutableName}");
				var wasabiStarterScriptContent = Shebang +
					$"{linuxWasabiWalletFolder.TrimEnd('/')}/{ExecutableName} $@\n";
				var wasabiDaemonStarterScriptPath = Path.Combine(debUsrLocalBinFolderPath, $"{DaemonExecutableName}");
				var wasabiDaemonStarterScriptContent = Shebang +
					$"{linuxWasabiWalletFolder.TrimEnd('/')}/{DaemonExecutableName} $@\n";

				File.WriteAllText(wasabiStarterScriptPath, wasabiStarterScriptContent, Encoding.ASCII);
				File.WriteAllText(wasabiDaemonStarterScriptPath, wasabiDaemonStarterScriptContent, Encoding.ASCII);

				string debDesktopFileLinuxPath = Tools.LinuxPathCombine(debUsrAppFolderRelativePath, $"{ExecutableName}.desktop");

				commands = new string[]
				{
					$"sudo find {Tools.LinuxPath(newFolderRelativePath)} -type f -exec chmod 644 {{}} \\;",
					$"sudo find {Tools.LinuxPath(newFolderRelativePath)} {chmodExecutablesArgs}",
					$"sudo chmod -R 0775 {Tools.LinuxPath(debianFolderRelativePath)}",
					$"sudo chmod -R 0644 {debDesktopFileLinuxPath}",
					$"dpkg --build {Tools.LinuxPath(debFolderRelativePath)} $(pwd)"
				};

				ExecuteBashCommands(commands);

				await IoHelpers.TryDeleteDirectoryAsync(debFolderPath).ConfigureAwait(false);

				string oldDeb = Path.Combine(BinDistDirectory, $"{ExecutableName}_{VersionPrefix}_amd64.deb");
				string newDeb = Path.Combine(BinDistDirectory, $"Wasabi-{VersionPrefix}.deb");
				File.Move(oldDeb, newDeb);

				await IoHelpers.TryDeleteDirectoryAsync(publishedFolder).ConfigureAwait(false);
				Console.WriteLine($"# Deleted {publishedFolder}");
			}
		}
	}

	private static void ReleaseOSXStartsilent(string target)
	{
		Console.WriteLine($"# Run dotnet restore");
		StartProcessAndWaitForExit("dotnet", StartsilentProjectDirectory, arguments: "restore --locked-mode");

		Console.WriteLine($"# Run dotnet clean");
		StartProcessAndWaitForExit("dotnet", StartsilentProjectDirectory, arguments: "clean --configuration Release");

		string currentBinDistDirectory = Path.Combine(BinDistDirectory, target, "startsilent"); ;

		if (!Directory.Exists(currentBinDistDirectory))
		{
			Directory.CreateDirectory(currentBinDistDirectory);
			Console.WriteLine($"# Created {currentBinDistDirectory}");
		}

		string dotnetProcessArgs = string.Join(
				" ",
				$"publish",
				$"--configuration Release",
				$"--force",
				$"--output \"{currentBinDistDirectory}\"",
				$"--self-contained true",
				$"--runtime \"{target}\"",
				$"--disable-parallel",
				$"--no-cache",
				$"--no-restore",
				$"/p:VersionPrefix={VersionPrefix}",
				$"/p:DebugType=none",
				$"/p:DebugSymbols=false",
				$"/p:ErrorReport=none",
				$"/p:DocumentationFile=\"\"",
				$"/p:Deterministic=true");

		StartProcessAndWaitForExit(
			"dotnet",
			StartsilentProjectDirectory,
			arguments: dotnetProcessArgs,
			redirectStandardOutput: true);

		string oldExecutablePath = Path.Combine(currentBinDistDirectory, $"WalletWasabi.OSXStartsilent");
		string newExecutablePath = Path.Combine(currentBinDistDirectory, "..", $"{SilentExecutableName}");
		File.Move(oldExecutablePath, newExecutablePath);

		string oldDllPath = Path.Combine(currentBinDistDirectory, $"WalletWasabi.OSXStartsilent.dll");
		string newDllPath = Path.Combine(currentBinDistDirectory, "..", $"WalletWasabi.OSXStartsilent.dll");
		File.Move(oldDllPath, newDllPath);

		string oldDepsJsonPath = Path.Combine(currentBinDistDirectory, $"WalletWasabi.OSXStartsilent.deps.json");
		string newDepsJsonPath = Path.Combine(currentBinDistDirectory, "..", $"WalletWasabi.OSXStartsilent.deps.json");
		File.Move(oldDepsJsonPath, newDepsJsonPath);

		Directory.Delete(currentBinDistDirectory, true);
	}

	/// <summary>Checks whether there are uncommitted changes.</summary>
	/// <remarks>This is important to really release a build that corresponds with a git hash.</remarks>
	private static void CheckUncommittedGitChanges()
	{
		if (TryStartProcessAndWaitForExit("git", workingDirectory: SolutionDirectory, out var gitStatus, arguments: "status --porcelain", redirectStandardOutput: true) && !string.IsNullOrEmpty(gitStatus))
		{
			Console.WriteLine("BEWARE: There are uncommitted changes in the repository. Do you want to continue? (Y/N)");
			int i = Console.Read();
			char ch = Convert.ToChar(i);

			if (ch != 'y' && ch != 'Y')
			{
				Console.WriteLine("\nExiting.");
				Environment.Exit(1);
			}
		}
	}

	/// <summary>
	/// Gets information about .NET SDK and .NET runtime so that deterministic build is easier to reproduce.
	/// </summary>
	/// <returns>JSON string to write to a <c>BUILDINFO.json</c> file.</returns>
	private static string GetBuildInfoData()
	{
		// .NET runtime version. We rely on the fact that this version is the same as if we run "dotnet" command. This should be a safe assumption.
		Version runtimeVersion = Environment.Version;

		// Get .NET SDK version.
		if (!TryStartProcessAndWaitForExit("dotnet", workingDirectory: SolutionDirectory, result: out var sdkVersion, arguments: "--version", redirectStandardOutput: true))
		{
			sdkVersion = "Failed to get .NET SDK version.";
		}

		// Get git commit ID.
		if (!TryStartProcessAndWaitForExit("git", workingDirectory: SolutionDirectory, result: out var gitCommitId, arguments: "rev-parse HEAD", redirectStandardOutput: true))
		{
			gitCommitId = "Failed to get git commit ID.";
		}

		return JsonSerializer.Serialize(new BuildInfo(runtimeVersion.ToString(), sdkVersion, gitCommitId), new JsonSerializerOptions() { WriteIndented = true });
	}

	/// <summary>
	/// Executes a set of commands in either WSL2 (on Windows) or Bash (on other platforms).
	/// </summary>
	/// <param name="commands">Commands to execute.</param>
	private static void ExecuteBashCommands(string[] commands)
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			// Use WSL on Windows.
			string arguments = Tools.CreateWslCommand(BinDistDirectory, commands);
			StartProcessAndWaitForExit("wsl", BinDistDirectory, arguments: arguments);
		}
		else
		{
			// Use Bash on other platforms.
			string arguments = string.Join(" && ", commands);
			StartProcessAndWaitForExit("bash", BinDistDirectory, arguments: $"-c \"{arguments}\"");
		}
	}

	private static string? StartProcessAndWaitForExit(string command, string workingDirectory, string? writeToStandardInput = null, string? arguments = null, bool redirectStandardOutput = false)
	{
		var isWriteToStandardInput = !string.IsNullOrWhiteSpace(writeToStandardInput);

		using var process = Process.Start(new ProcessStartInfo
		{
			FileName = command,
			Arguments = arguments ?? "",
			RedirectStandardInput = isWriteToStandardInput,
			RedirectStandardOutput = redirectStandardOutput,
			WorkingDirectory = workingDirectory
		})
		?? throw new InvalidOperationException($"Process '{command}' is invalid.");

		if (isWriteToStandardInput)
		{
			process.StandardInput.WriteLine(writeToStandardInput);
		}

		string? output = null;

		if (redirectStandardOutput)
		{
			output = process.StandardOutput.ReadToEnd();
		}

		process.WaitForExit();

		if (process.ExitCode is not 0)
		{
			Console.WriteLine($"Process failed:");
			Console.WriteLine($"* Command: '{command} {arguments}'");
			Console.WriteLine($"* Working directory: '{workingDirectory}'");
			Console.WriteLine($"* Exit code: '{process.ExitCode}'");

			if (redirectStandardOutput)
			{
				string prettyPrint = string.Join(Environment.NewLine, (output ?? "").Split(Environment.NewLine).Select(line => $"  > {line}"));
				Console.WriteLine($"* Output:\n{prettyPrint}");
			}

			throw new InvalidOperationException("Process exited with unexpected exit code");
		}

		return output;
	}

	private static bool TryStartProcessAndWaitForExit(string command, string workingDirectory, [NotNullWhen(true)] out string? result, string? writeToStandardInput = null, string? arguments = null, bool redirectStandardOutput = false)
	{
		result = null;

		try
		{
			result = StartProcessAndWaitForExit(command, workingDirectory, writeToStandardInput, arguments, redirectStandardOutput)?.Trim() ?? "";
			return true;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"# Process failed: '{ex}'.");
		}

		return false;
	}

	private static string GetPackageTargetPostfix(string target)
	{
		if (target.StartsWith("osx"))
		{
			return target.Replace("osx", "macOS");
		}

		return target;
	}
}
