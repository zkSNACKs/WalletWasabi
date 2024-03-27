using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using WalletWasabi.Helpers;

namespace WalletWasabi.Packager;

public static class MacSignTools
{
	public static void Sign(ArgsProcessor argsProcessor)
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			throw new NotSupportedException("This signing method is only valid on macOS!");
		}

		Console.WriteLine("Phase: finding the zip file on desktop which contains the compiled binaries from Windows.");

		string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
		string removableDriveFolder = Tools.GetSingleUsbDrive();

		var srcZipFileNamePattern = "WasabiToNotarize-*";
		var files = Directory.GetFiles(removableDriveFolder, srcZipFileNamePattern);
		if (files.Length != 2)
		{
			throw new InvalidDataException($"{srcZipFileNamePattern} file missing or there are more! There must be exactly two!");
		}

		var (appleId, password) = argsProcessor.GetAppleIdAndPassword();

		while (string.IsNullOrWhiteSpace(appleId))
		{
			Console.WriteLine("Enter appleId (email):");
			appleId = Console.ReadLine();
		}

		while (string.IsNullOrWhiteSpace(password))
		{
			Console.WriteLine("Enter password:");
			password = Console.ReadLine();
		}

		foreach (var zipPath in files)
		{
			var zipFile = Path.GetFileName(zipPath);
			var versionPrefix = Path.GetFileNameWithoutExtension(zipPath).Split('-')[1]; // Example: "WasabiToNotarize-2.0.0.0-arm64.zip or WasabiToNotarize-2.0.0.0.zip ".
			var workingDir = Path.Combine(desktopPath, "wasabiTemp");
			var dmgPath = Path.Combine(workingDir, "dmg");
			var unzippedPath = Path.Combine(workingDir, "unzipped");
			var appName = $"{Constants.AppName}.app";
			var appPath = Path.Combine(dmgPath, appName);
			var appContentsPath = Path.Combine(appPath, "Contents");
			var appMacOsPath = Path.Combine(appContentsPath, "MacOS");
			var appResPath = Path.Combine(appContentsPath, "Resources");
			var appFrameworksPath = Path.Combine(appContentsPath, "Frameworks");
			var infoFilePath = Path.Combine(appContentsPath, "Info.plist");
			var dmgFileName = zipFile.Replace("WasabiToNotarize", "Wasabi").Replace("zip", "dmg");
			var dmgFilePath = Path.Combine(workingDir, dmgFileName);
			var dmgUnzippedFilePath = Path.Combine(workingDir, $"Wasabi.tmp.dmg");
			var appNotarizeFilePath = Path.Combine(workingDir, $"Wasabi-{versionPrefix}.zip");
			var contentsPath = Path.GetFullPath(Path.Combine(Program.PackagerProjectDirectory.Replace("\\", "//"), "Content", "Osx"));
			var entitlementsPath = Path.Combine(contentsPath, "entitlements.plist");
			var dmgContentsDir = Path.Combine(contentsPath, "Dmg");
			var desktopDmgFilePath = Path.Combine(desktopPath, dmgFileName);

			var signArguments = $"--sign \"L233B2JQ68\" --verbose --force --options runtime --timestamp";

			Console.WriteLine("Phase: creating the working directory.");

			if (Directory.Exists(workingDir))
			{
				DeleteWithChmod(workingDir);
			}

			if (File.Exists(desktopDmgFilePath))
			{
				File.Delete(desktopDmgFilePath);
			}

			Console.WriteLine("Phase: creating the app.");

			IoHelpers.EnsureDirectoryExists(appResPath);
			IoHelpers.EnsureDirectoryExists(appMacOsPath);

			ZipFile.ExtractToDirectory(zipPath, appMacOsPath); // Copy the binaries.

			IoHelpers.CopyFilesRecursively(new DirectoryInfo(Path.Combine(contentsPath, "App")), new DirectoryInfo(appPath));

			Console.WriteLine("Update the plist file with current information for example with version.");

			var lines = File.ReadAllLines(infoFilePath);
			string? bundleIdentifier = null;

			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i];
				if (!line.TrimStart().StartsWith("<key>", StringComparison.InvariantCultureIgnoreCase))
				{
					continue;
				}

				if (line.Contains("CFBundleShortVersionString", StringComparison.InvariantCulture) ||
					line.Contains("CFBundleVersion", StringComparison.InvariantCulture))
				{
					lines[i + 1] = lines[i + 1].Replace("?", $"{Version.Parse(versionPrefix).ToString(3)}"); // Apple allow only 3 version tags in plist.
				}
				else if (line.Contains("CFBundleIdentifier", StringComparison.InvariantCulture))
				{
					bundleIdentifier = lines[i + 1].Trim().Replace("<string>", "").Replace("</string>", "");
				}
			}
			if (string.IsNullOrWhiteSpace(bundleIdentifier))
			{
				throw new InvalidDataException("Bundle identifier not found in plist file.");
			}

			File.Delete(infoFilePath);

			File.WriteAllLines(infoFilePath, lines);

			using (var process = Process.Start(new ProcessStartInfo
			{
				FileName = "chmod",
				Arguments = $"-R u+rwX,go+rX,go-w \"{appPath}\"",
				WorkingDirectory = workingDir
			}))
			{
				WaitProcessToFinish(process, "chmod");
			}

			var filesToCheck = new[] { entitlementsPath };

			foreach (var file in filesToCheck)
			{
				if (!File.Exists(file))
				{
					throw new FileNotFoundException($"File missing: {file}");
				}
			}

			Console.WriteLine("Signing the files in app.");

			IoHelpers.EnsureDirectoryExists(appResPath);
			IoHelpers.EnsureDirectoryExists(appMacOsPath);

			var executables = GetExecutables(appPath);

			// The main executable needs to be signed last.
			var filesToSignInOrder = Directory.GetFiles(appPath, "*.*", SearchOption.AllDirectories)
				.OrderBy(file => executables.Contains(file))
				.OrderBy(file => new FileInfo(file).Name == "wassabee")
				.ToArray();

			foreach (var file in executables)
			{
				using var process = Process.Start(new ProcessStartInfo
				{
					FileName = "chmod",
					Arguments = $"u+x \"{file}\"",
					WorkingDirectory = workingDir
				});
				WaitProcessToFinish(process, "chmod");
			}

			SignDirectory(filesToSignInOrder, workingDir, signArguments, entitlementsPath);

			Console.WriteLine("Phase: verifying the signature.");

			Verify(appPath);

			Console.WriteLine("Phase: notarize the app.");

			// Source: https://blog.frostwire.com/2019/08/27/apple-notarization-the-signature-of-the-binary-is-invalid-one-other-reason-not-explained-in-apple-developer-documentation/
			using (var process = Process.Start(new ProcessStartInfo
			{
				FileName = "ditto",
				Arguments = $"-c -k --keepParent \"{appPath}\" \"{appNotarizeFilePath}\"",
				WorkingDirectory = workingDir
			}))
			{
				WaitProcessToFinish(process, "ditto");
			}

			Notarize(appleId, appNotarizeFilePath);
			Staple(appPath);

			using (var process = Process.Start(new ProcessStartInfo
			{
				FileName = "spctl",
				Arguments = $"-a -t exec -vv \"{appPath}\"",
				WorkingDirectory = workingDir,
				RedirectStandardError = true
			}))
			{
				var nonNullProcess = WaitProcessToFinish(process, "spctl");
				string result = nonNullProcess.StandardError.ReadToEnd();
				if (!result.Contains(": accepted"))
				{
					throw new InvalidOperationException(result);
				}
			}

			Console.WriteLine("Phase: creating the dmg.");

			if (File.Exists(dmgFilePath))
			{
				File.Delete(dmgFilePath);
			}

			Console.WriteLine("Phase: creating dmg.");

			IoHelpers.CopyFilesRecursively(new DirectoryInfo(dmgContentsDir), new DirectoryInfo(dmgPath));

			File.Copy(Path.Combine(contentsPath, "WasabiLogo.icns"), Path.Combine(dmgPath, ".VolumeIcon.icns"), true);

			var temp = Path.Combine(dmgPath, ".DS_Store.dat");
			File.Move(temp, Path.Combine(dmgPath, ".DS_Store"), true);

			using (var process = Process.Start(new ProcessStartInfo
			{
				FileName = "ln",
				Arguments = "-s /Applications",
				WorkingDirectory = dmgPath
			}))
			{
				WaitProcessToFinish(process, "ln");
			}

			var hdutilCreateArgs = string.Join(
				" ",
				new string[]
				{
					"create",
					$"\"{dmgUnzippedFilePath}\"",
					"-ov",
					$"-volname \"Wasabi Wallet\"",
					"-fs HFS+",
					$"-srcfolder \"{dmgPath}\""
				});

			using (var process = Process.Start(new ProcessStartInfo
			{
				FileName = "hdiutil",
				Arguments = hdutilCreateArgs,
				WorkingDirectory = dmgPath
			}))
			{
				WaitProcessToFinish(process, "hdiutil");
			}

			var hdutilConvertArgs = string.Join(
				" ",
				new string[]
				{
					"convert",
					$"\"{dmgUnzippedFilePath}\"",
					"-format UDZO",
					$"-o \"{dmgFilePath}\""
				});

			using (var process = Process.Start(new ProcessStartInfo
			{
				FileName = "hdiutil",
				Arguments = hdutilConvertArgs,
				WorkingDirectory = dmgPath
			}))
			{
				WaitProcessToFinish(process, "hdiutil");
			}

			Console.WriteLine("Phase: signing the dmg file.");

			SignFile($"{signArguments} --entitlements \"{entitlementsPath}\" \"{dmgFilePath}\"", dmgPath);

			Console.WriteLine("Phase: verifying the signature.");

			Verify(dmgFilePath);

			Console.WriteLine("Phase: notarize dmg");
			Notarize(appleId, dmgFilePath);

			Console.WriteLine("Phase: staple dmp");
			Staple(dmgFilePath);

			using (var process = Process.Start(new ProcessStartInfo
			{
				FileName = "spctl",
				Arguments = $"-a -t open --context context:primary-signature -v \"{dmgFilePath}\"",
				WorkingDirectory = workingDir,
				RedirectStandardError = true
			}))
			{
				var nonNullProcess = WaitProcessToFinish(process, "spctl");
				string result = nonNullProcess.StandardError.ReadToEnd();
				if (!result.Contains(": accepted"))
				{
					throw new InvalidOperationException(result);
				}
			}

			File.Move(dmgFilePath, desktopDmgFilePath);
			DeleteWithChmod(workingDir);

			Console.WriteLine($"Phase: finished for {dmgFileName}.");

			var toRemovableFilePath = Path.Combine(removableDriveFolder, Path.GetFileName(desktopDmgFilePath));
			File.Move(desktopDmgFilePath, toRemovableFilePath, true);

			if (File.Exists(zipPath))
			{
				File.Delete(zipPath);
			}
		}
		Console.WriteLine("Phase: finished successfully.");
	}

	private static Process WaitProcessToFinish(Process? process, string processName)
	{
		if (process is null)
		{
			throw new InvalidOperationException($"Could not start ${processName} process.");
		}
		process.WaitForExit();
		return process;
	}

	private static void Notarize(string appleId, string filePath)
	{
		Console.WriteLine("Start notarizing, uploading file.");

		// -p WasabiNotarize = Saved the credentials in the keychain profile which keeps the password safe on the local machine. Name of the profile is "WasabiNotarize".
		using var process = Process.Start(new ProcessStartInfo
		{
			FileName = "xcrun",
			Arguments = $"notarytool submit --wait --apple-id \"{appleId}\" -p \"WasabiNotarize\" \"{filePath}\" ",
			RedirectStandardOutput = true,
		});

		var nonNullProcess = WaitProcessToFinish(process, "xcrum");
		string result = nonNullProcess.StandardOutput.ReadToEnd();

		Console.WriteLine(result);
	}

	private static void Staple(string filePath)
	{
		using var process = Process.Start(new ProcessStartInfo
		{
			FileName = "xcrun",
			Arguments = $"stapler staple \"{filePath}\"",
		});
		WaitProcessToFinish(process, "xcrum");
	}

	private static void DeleteWithChmod(string path)
	{
		using (var process = Process.Start(new ProcessStartInfo
		{
			FileName = "chmod",
			Arguments = $"-R ugo+rwx \"{path}\"",
		}))
		{
			WaitProcessToFinish(process, "chmod");
		}

		IoHelpers.TryDeleteDirectoryAsync(path).GetAwaiter().GetResult();
	}

	private static void SignFile(string arguments, string workingDir)
	{
		using var process = Process.Start(new ProcessStartInfo
		{
			FileName = "codesign",
			Arguments = arguments,
			WorkingDirectory = workingDir,
			RedirectStandardError = true
		});
		var nonNullProcess = WaitProcessToFinish(process, "codesign");
		var result = nonNullProcess.StandardError.ReadToEnd();
		if (result.Contains("code object is not signed at all"))
		{
			throw new InvalidOperationException(result);
		}

		if (result.Contains("xcrun: error: invalid active developer path"))
		{
			throw new InvalidOperationException($"{result}\ntip: run xcode-select --install");
		}

		Console.WriteLine(result.Trim());
	}

	private static void Verify(string path)
	{
		using var process = Process.Start(new ProcessStartInfo
		{
			FileName = "codesign",
			Arguments = $"-dv --verbose=4 \"{path}\"",
			RedirectStandardError = true,
		});
		var nonNullProcess = WaitProcessToFinish(process, "codesign");
		string result = nonNullProcess.StandardError.ReadToEnd();
		if (!result.Contains("Authority=Developer ID Application: zkSNACKs Ltd."))
		{
			throw new InvalidOperationException(result);
		}
	}

	private static void SignDirectory(string[] files, string workingDir, string signArguments, string entitlementsPath)
	{
		// Tor already signed by: The Tor Project, Inc (MADPSAYN6T)

		// Wassabee has to be signed at the end. Otherwise codesign will throw a "submodule not signed" error.
		foreach (var file in files)
		{
			var fileName = new FileInfo(file).Name;

			if (fileName == ".DS_Store")
			{
				File.Delete(file);
				continue;
			}

			SignFile($"{signArguments} --entitlements \"{entitlementsPath}\" \"{file}\"", workingDir);
		}
	}

	private static IEnumerable<string> GetExecutables(string appPath)
	{
		string result = ExecuteBashCommand($"find -H \"{appPath}\" -print0 | xargs -0 file | grep \"Mach-O.* executable\"");

		var lines = result.Split("\n").Where(x => !string.IsNullOrWhiteSpace(x));
		var files = lines.Select(line => line.Split(":").First());

		return files;
	}

	private static string ExecuteBashCommand(string command)
	{
		// according to: https://stackoverflow.com/a/15262019/637142
		// Thanks to this we will pass everything as one command.
		command = command.Replace("\"", "\"\"");

		using var process = Process.Start(new ProcessStartInfo
		{
			FileName = "/bin/bash",
			Arguments = $"-c \"{command}\"",
			UseShellExecute = false,
			RedirectStandardError = true,
			RedirectStandardOutput = true,
			CreateNoWindow = true
		})
		?? throw new InvalidOperationException("Could not start bash process.");
		var result = process.StandardOutput.ReadToEnd();
		process.WaitForExit();

		return result;
	}
}
