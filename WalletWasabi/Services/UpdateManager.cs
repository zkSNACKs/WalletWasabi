using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Microservices;
using WalletWasabi.Services.Events;
using WalletWasabi.Tor.Http;

namespace WalletWasabi.Services;

public record UpdateStatus2(bool ClientUpToDate, bool BackendCompatible, bool IsReadyToInstall, Version ClientVersion);
public class UpdateManager : IDisposable
{
	private string InstallerPath { get; set; } = "";
	private const byte MaxTries = 2;
	private const string ReleaseURL = "https://api.github.com/repos/zkSNACKs/WalletWasabi/releases/latest";

	public UpdateManager(string dataDir, bool downloadNewVersion, IHttpClient httpClient, EventBus eventBus)
	{
		InstallerDir = Path.Combine(dataDir, "Installer");
		HttpClient = httpClient;

		CancellationToken = CancellationTokenSource.Token;

		// The feature is disabled on linux at the moment because we install Wasabi Wallet as a Debian package.
		DownloadNewVersion = downloadNewVersion && !RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
		VersionChangedSubscription = eventBus.Subscribe<SoftwareVersionChanged>(OnVersionChanged);
	}

	public event EventHandler<UpdateStatus2>? UpdateAvailableToGet;
	private IDisposable VersionChangedSubscription { get; }

	public string InstallerDir { get; }
	private IHttpClient HttpClient { get; }

	///<summary>Whether to download the new installer in the background or not.</summary>
	private bool DownloadNewVersion { get; }

	///<summary>Install new version on shutdown or not.</summary>
	public bool DoUpdateOnClose { get; set; }

	private CancellationTokenSource CancellationTokenSource { get; } = new();

	/// <remarks>Defensive copy of the token to avoid issues with <see cref="CancellationTokenSource"/> being disposed.</remarks>
	private CancellationToken CancellationToken { get; }

	private async void OnVersionChanged(SoftwareVersionChanged softwareVersionChanged)
	{
		// If the client version locally is greater than or equal to the backend's reported client version, then good.
		var clientUpToDate = Helpers.Constants.ClientVersion >= softwareVersionChanged.ClientVersion;

		// If ClientSupportBackendVersionMin <= backend major <= ClientSupportBackendVersionMax, then our software is compatible.
		var backendCompatible =
			int.Parse(Helpers.Constants.ClientSupportBackendVersionMax) >= softwareVersionChanged.ServerVersion.Major &&
			softwareVersionChanged.ServerVersion.Major >= int.Parse(Helpers.Constants.ClientSupportBackendVersionMin);

		var tries = 0;
		bool updateAvailable = !clientUpToDate || !backendCompatible;
		Version targetVersion = softwareVersionChanged.ClientVersion;

		if (!updateAvailable)
		{
			// After updating Wasabi, remove old installer file.
			Cleanup();
			return;
		}

		var isReadyToInstall = false;
		var clientVersion = softwareVersionChanged.ClientVersion;

		if (DownloadNewVersion)
		{
			do
			{
				tries++;
				try
				{
					(string installerPath, Version newVersion) = await GetInstallerAsync(targetVersion, CancellationToken).ConfigureAwait(false);
					InstallerPath = installerPath;
					Logger.LogInfo($"Version {newVersion} downloaded successfully.");
					isReadyToInstall = true;
					clientVersion = newVersion;
					break;
				}
				catch (OperationCanceledException ex)
				{
					Logger.LogTrace($"Getting new update was canceled.", ex);
					break;
				}
				catch (InvalidOperationException ex)
				{
					Logger.LogError($"Getting new update failed with error.", ex);
					Cleanup();
					break;
				}
				catch (Exception ex)
				{
					Logger.LogError($"Getting new update failed with error.", ex);
				}
			} while (tries < MaxTries);
		}

		UpdateAvailableToGet?.Invoke(this, new UpdateStatus2(clientUpToDate, backendCompatible, isReadyToInstall, clientVersion));
	}

	/// <summary>
	/// Get or download installer for the newest release.
	/// </summary>
	/// <param name="targetVersion">This does not contains the revision number, because backend always sends zero.</param>
	private async Task<(string filePath, Version newVersion)> GetInstallerAsync(Version targetVersion, CancellationToken cancellationToken)
	{
		var result = await GetLatestReleaseFromGithubAsync(targetVersion, cancellationToken).ConfigureAwait(false);
		var sha256SumsFilePath = Path.Combine(InstallerDir, "SHA256SUMS.asc");

		// This will throw InvalidOperationException in case of invalid signature.
		await DownloadAndValidateWasabiSignatureAsync(sha256SumsFilePath, result.Sha256SumsUrl, result.WasabiSigUrl, cancellationToken).ConfigureAwait(false);

		var installerFilePath = Path.Combine(InstallerDir, result.InstallerFileName);

		try
		{
			if (!File.Exists(installerFilePath))
			{
				EnsureToRemoveCorruptedFiles();

				// This should also be done using Tor.
				// TODO: https://github.com/zkSNACKs/WalletWasabi/issues/8800
				Logger.LogInfo($"Trying to download new version: {result.LatestVersion}");

				// Get file stream and copy it to downloads folder to access.
				using HttpRequestMessage request = new(HttpMethod.Get, result.InstallerDownloadUrl);
				using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
				byte[] installerFileBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

				Logger.LogInfo("Installer downloaded, copying...");

				using MemoryStream stream = new(installerFileBytes);
				await CopyStreamContentToFileAsync(stream, installerFilePath, cancellationToken).ConfigureAwait(false);
			}
			string expectedHash = await GetHashFromSha256SumsFileAsync(result.InstallerFileName, sha256SumsFilePath).ConfigureAwait(false);
			await VerifyInstallerHashAsync(installerFilePath, expectedHash, cancellationToken).ConfigureAwait(false);
		}
		catch (IOException)
		{
			cancellationToken.ThrowIfCancellationRequested();
			throw;
		}

		return (installerFilePath, result.LatestVersion);
	}

	private async Task VerifyInstallerHashAsync(string installerFilePath, string expectedHash, CancellationToken cancellationToken)
	{
		var bytes = await WasabiSignerHelpers.GetShaComputedBytesOfFileAsync(installerFilePath, cancellationToken).ConfigureAwait(false);
		string downloadedHash = Convert.ToHexString(bytes).ToLower();

		if (expectedHash != downloadedHash)
		{
			throw new InvalidOperationException("Downloaded file hash doesn't match expected hash.");
		}
	}

	private async Task<string> GetHashFromSha256SumsFileAsync(string installerFileName, string sha256SumsFilePath)
	{
		string[] lines = await File.ReadAllLinesAsync(sha256SumsFilePath).ConfigureAwait(false);
		var correctLine = lines.FirstOrDefault(line => line.Contains(installerFileName))
			?? throw new InvalidOperationException($"{installerFileName} was not found.");
		return correctLine.Split(" ")[0];
	}

	private async Task CopyStreamContentToFileAsync(Stream stream, string filePath, CancellationToken cancellationToken)
	{
		if (File.Exists(filePath))
		{
			return;
		}
		var tmpFilePath = $"{filePath}.tmp";
		IoHelpers.EnsureContainingDirectoryExists(tmpFilePath);
		using (var file = File.OpenWrite(tmpFilePath))
		{
			await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);

			// Closing the file to rename.
			file.Close();
		};
		File.Move(tmpFilePath, filePath);
	}

	private async Task<(Version LatestVersion, string InstallerDownloadUrl, string InstallerFileName, string Sha256SumsUrl, string WasabiSigUrl)> GetLatestReleaseFromGithubAsync(Version targetVersion, CancellationToken cancellationToken)
	{
		using HttpRequestMessage message = new(HttpMethod.Get, ReleaseURL);
		message.Headers.UserAgent.Add(new("WalletWasabi", "2.0"));
		var response = await HttpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);

		JObject jsonResponse = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

		string softwareVersion = jsonResponse["tag_name"]?.ToString() ?? throw new InvalidDataException("Endpoint gave back wrong json data or it's changed.");

		// Make sure there are no non-numeric characters (besides '.') in the version string.
		softwareVersion = string.Concat(softwareVersion.Where(c => char.IsDigit(c) || c == '.').ToArray());

		Version githubVersion = new(softwareVersion);
		Version shortGithubVersion = new(githubVersion.Major, githubVersion.Minor, githubVersion.Build);
		if (targetVersion != shortGithubVersion)
		{
			throw new InvalidDataException("Target version from backend does not match with the latest GitHub release. This should be impossible.");
		}

		// Get all asset names and download URLs to find the correct one.
		List<JToken> assetsInfo = jsonResponse["assets"]?.Children().ToList() ?? throw new InvalidDataException("Missing assets from response.");
		List<string> assetDownloadURLs = new();
		foreach (JToken asset in assetsInfo)
		{
			assetDownloadURLs.Add(asset["browser_download_url"]?.ToString() ?? throw new InvalidDataException("Missing download url from response."));
		}

		string sha256SumsUrl = assetDownloadURLs.First(url => url.Contains("SHA256SUMS.asc"));
		string wasabiSigUrl = assetDownloadURLs.First(url => url.Contains("SHA256SUMS.wasabisig"));

		(string url, string fileName) = GetAssetToDownload(assetDownloadURLs);

		return (githubVersion, url, fileName, sha256SumsUrl, wasabiSigUrl);
	}

	private async Task DownloadAndValidateWasabiSignatureAsync(string sha256SumsFilePath, string sha256SumsUrl, string wasabiSigUrl, CancellationToken cancellationToken)
	{
		var wasabiSigFilePath = Path.Combine(InstallerDir, "SHA256SUMS.wasabisig");

		try
		{
			using HttpRequestMessage sha256Request = new(HttpMethod.Get, sha256SumsUrl);
			using HttpResponseMessage sha256Response = await HttpClient.SendAsync(sha256Request, cancellationToken).ConfigureAwait(false);
			string sha256Content = await sha256Response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

			IoHelpers.EnsureContainingDirectoryExists(sha256SumsFilePath);
			File.WriteAllText(sha256SumsFilePath, sha256Content);

			using HttpRequestMessage signatureRequest = new(HttpMethod.Get, wasabiSigUrl);
			using HttpResponseMessage signatureResponse = await HttpClient.SendAsync(signatureRequest, cancellationToken).ConfigureAwait(false);
			string signatureContent = await signatureResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

			IoHelpers.EnsureContainingDirectoryExists(wasabiSigFilePath);
			File.WriteAllText(wasabiSigFilePath, signatureContent);

			await WasabiSignerHelpers.VerifySha256SumsFileAsync(sha256SumsFilePath).ConfigureAwait(false);
		}
		catch (HttpRequestException ex)
		{
			string message = "";
			if (ex.StatusCode is HttpStatusCode.NotFound)
			{
				message = "Wasabi signature files were not found under the API.";
			}
			else
			{
				message = "Something went wrong while getting Wasabi signature files.";
			}
			throw new InvalidOperationException(message, ex);
		}
		catch (IOException)
		{
			// There's a chance to get IOException when closing Wasabi during stream copying. Throw OperationCancelledException instead.
			cancellationToken.ThrowIfCancellationRequested();
			throw;
		}
	}

	private (string url, string fileName) GetAssetToDownload(List<string> assetDownloadURLs)
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			var url = assetDownloadURLs.First(url => url.Contains(".msi"));
			return (url, url.Split("/").Last());
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			var cpu = RuntimeInformation.ProcessArchitecture;
			if (cpu.ToString() == "Arm64")
			{
				var arm64url = assetDownloadURLs.First(url => url.Contains("arm64.dmg"));
				return (arm64url, arm64url.Split("/").Last());
			}
			var url = assetDownloadURLs.First(url => url.Contains(".dmg") && !url.Contains("arm64"));
			return (url, url.Split("/").Last());
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			throw new InvalidOperationException("For Linux, get the correct update manually.");
		}
		else
		{
			throw new InvalidOperationException("OS not recognized, download manually.");
		}
	}

	private void EnsureToRemoveCorruptedFiles()
	{
		DirectoryInfo folder = new(InstallerDir);
		if (folder.Exists)
		{
			IEnumerable<FileSystemInfo> corruptedFiles = folder.GetFileSystemInfos().Where(file => file.Extension.Equals(".tmp"));
			foreach (var file in corruptedFiles)
			{
				File.Delete(file.FullName);
			}
		}
	}

	private void Cleanup()
	{
		try
		{
			var folder = new DirectoryInfo(InstallerDir);
			if (folder.Exists)
			{
				Directory.Delete(InstallerDir, true);
			}
		}
		catch (Exception exc)
		{
			Logger.LogError("Failed to delete installer directory.", exc);
		}
	}

	public void StartInstallingNewVersion()
	{
		try
		{
			ProcessStartInfo startInfo;
			if (!File.Exists(InstallerPath))
			{
				throw new FileNotFoundException(InstallerPath);
			}
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				startInfo = ProcessStartInfoFactory.Make(InstallerPath, "", true);
			}
			else
			{
				startInfo = new()
				{
					FileName = InstallerPath,
					UseShellExecute = true,
					WindowStyle = ProcessWindowStyle.Normal
				};
			}

			using Process? p = Process.Start(startInfo);

			if (p is null)
			{
				throw new InvalidOperationException($"Can't start {nameof(p)} {startInfo.FileName}.");
			}
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				// For MacOS, you need to start the process twice, first start => permission denied
				// TODO: find out why and fix.

				p!.WaitForExit(5000);
				p.Start();
			}
		}
		catch (Exception ex)
		{
			Logger.LogError("Failed to install latest release. File might be corrupted.", ex);
		}
	}

	public void Dispose()
	{
		VersionChangedSubscription.Dispose();
		CancellationTokenSource.Cancel();
		CancellationTokenSource.Dispose();
	}
}
