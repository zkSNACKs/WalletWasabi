using NBitcoin;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WalletWasabi.Extensions;
using WalletWasabi.Helpers;
using WalletWasabi.Hwi.Models;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.Wallets;
using Xunit;

namespace WalletWasabi.Tests.UnitTests;

public class WalletDirTests
{
	private async Task<(string walletsPath, string walletsBackupPath)> CleanupWalletDirectoriesAsync(string baseDir)
	{
		var walletsPath = Path.Combine(baseDir, WalletDirectories.WalletsDirName);
		var walletsBackupPath = Path.Combine(baseDir, WalletDirectories.WalletsBackupDirName);
		await IoHelpers.TryDeleteDirectoryAsync(walletsPath);
		await IoHelpers.TryDeleteDirectoryAsync(walletsBackupPath);

		return (walletsPath, walletsBackupPath);
	}

	[Fact]
	public async Task CreatesWalletDirectoriesAsync()
	{
		var baseDir = Common.GetWorkDir();
		(string walletsPath, string walletsBackupPath) = await CleanupWalletDirectoriesAsync(baseDir);

		_ = new WalletDirectories(Network.Main, baseDir);
		Assert.True(Directory.Exists(walletsPath));
		Assert.True(Directory.Exists(walletsBackupPath));

		// Testing what happens if the directories are already exist.
		_ = new WalletDirectories(Network.Main, baseDir);
		Assert.True(Directory.Exists(walletsPath));
		Assert.True(Directory.Exists(walletsBackupPath));
	}

	[Fact]
	public async Task TestPathsAsync()
	{
		var baseDir = Common.GetWorkDir();
		await CleanupWalletDirectoriesAsync(baseDir);

		var mainWd = new WalletDirectories(Network.Main, baseDir);
		Assert.Equal(Network.Main, mainWd.Network);
		Assert.Equal(Path.Combine(baseDir, "Wallets"), mainWd.WalletsDir);
		Assert.Equal(Path.Combine(baseDir, "WalletBackups"), mainWd.WalletsBackupDir);

		var testWd = new WalletDirectories(Network.TestNet, baseDir);
		Assert.Equal(Network.TestNet, testWd.Network);
		Assert.Equal(Path.Combine(baseDir, "Wallets", "TestNet"), testWd.WalletsDir);
		Assert.Equal(Path.Combine(baseDir, "WalletBackups", "TestNet"), testWd.WalletsBackupDir);

		var regWd = new WalletDirectories(Network.RegTest, baseDir);
		Assert.Equal(Network.RegTest, regWd.Network);
		Assert.Equal(Path.Combine(baseDir, "Wallets", "RegTest"), regWd.WalletsDir);
		Assert.Equal(Path.Combine(baseDir, "WalletBackups", "RegTest"), regWd.WalletsBackupDir);
	}

	[Fact]
	public async Task CorrectWalletDirectoryNameAsync()
	{
		var baseDir = Common.GetWorkDir();
		(string walletsPath, string walletsBackupPath) = await CleanupWalletDirectoriesAsync(baseDir);

		var walletDirectories = new WalletDirectories(Network.Main, $" {baseDir} ");
		Assert.Equal(walletsPath, walletDirectories.WalletsDir);
		Assert.Equal(walletsBackupPath, walletDirectories.WalletsBackupDir);
	}

	[Fact]
	public async Task ServesWalletFilesAsync()
	{
		var baseDir = Common.GetWorkDir();
		await CleanupWalletDirectoriesAsync(baseDir);

		var walletDirectories = new WalletDirectories(Network.Main, baseDir);
		string walletName = "FooWallet.json";

		(string walletPath, string walletBackupPath) = walletDirectories.GetWalletFilePaths(walletName);

		Assert.Equal(Path.Combine(walletDirectories.WalletsDir, walletName), walletPath);
		Assert.Equal(Path.Combine(walletDirectories.WalletsBackupDir, walletName), walletBackupPath);
	}

	[Fact]
	public async Task EnsuresJsonAsync()
	{
		var baseDir = Common.GetWorkDir();
		await CleanupWalletDirectoriesAsync(baseDir);

		var walletDirectories = new WalletDirectories(Network.Main, baseDir);
		string walletName = "FooWallet";
		string walletFileName = $"{walletName}.json";

		(string walletPath, string walletBackupPath) = walletDirectories.GetWalletFilePaths(walletName);

		Assert.Equal(Path.Combine(walletDirectories.WalletsDir, walletFileName), walletPath);
		Assert.Equal(Path.Combine(walletDirectories.WalletsBackupDir, walletFileName), walletBackupPath);
	}

	[Fact]
	public async Task EnumerateFilesAsync()
	{
		var baseDir = Common.GetWorkDir();
		await CleanupWalletDirectoriesAsync(baseDir);

		var walletDirectories = new WalletDirectories(Network.Main, baseDir);

		var wallets = new List<string>();
		var walletBackups = new List<string>();
		const int NumberOfWallets = 4;
		for (int i = 0; i < NumberOfWallets; i++)
		{
			var walletFile = Path.Combine(walletDirectories.WalletsDir, $"FooWallet{i}.json");
			var dummyFile = Path.Combine(walletDirectories.WalletsDir, $"FooWallet{i}.dummy");
			var backupFile = Path.Combine(walletDirectories.WalletsBackupDir, $"FooWallet{i}.json");

			await File.Create(walletFile).DisposeAsync();
			await File.Create(dummyFile).DisposeAsync();
			await File.Create(backupFile).DisposeAsync();

			wallets.Add(walletFile);
			walletBackups.Add(backupFile);
		}

		Assert.True(wallets.ToHashSet().SetEquals(walletDirectories.EnumerateWalletFiles().Select(x => x.FullName).ToHashSet()));
		Assert.True(wallets.Concat(walletBackups).ToHashSet().SetEquals(walletDirectories.EnumerateWalletFiles(true).Select(x => x.FullName).ToHashSet()));
	}

	[Fact]
	public async Task EnumerateOrdersByAccessAsync()
	{
		var baseDir = Common.GetWorkDir();
		await CleanupWalletDirectoriesAsync(baseDir);

		var walletDirectories = new WalletDirectories(Network.Main, baseDir);

		var walletFile1 = Path.Combine(walletDirectories.WalletsDir, $"FooWallet1.json");
		await File.Create(walletFile1).DisposeAsync();
		File.SetLastAccessTimeUtc(walletFile1, new DateTime(2005, 1, 1, 1, 1, 1, DateTimeKind.Utc));

		var walletFile2 = Path.Combine(walletDirectories.WalletsDir, $"FooWallet2.json");
		await File.Create(walletFile2).DisposeAsync();
		File.SetLastAccessTimeUtc(walletFile2, new DateTime(2000, 1, 1, 1, 1, 1, DateTimeKind.Utc));

		var walletFile3 = Path.Combine(walletDirectories.WalletsDir, $"FooWallet3.json");
		await File.Create(walletFile3).DisposeAsync();
		File.SetLastAccessTimeUtc(walletFile3, new DateTime(2010, 1, 1, 1, 1, 1, DateTimeKind.Utc));

		var orderedWallets = new[] { walletFile3, walletFile1, walletFile2 };

		Assert.Equal(orderedWallets, walletDirectories.EnumerateWalletFiles().Select(x => x.FullName));
	}

	[Fact]
	public async Task EnumerateMissingDirAsync()
	{
		var baseDir = Common.GetWorkDir();
		(string walletsPath, string walletsBackupPath) = await CleanupWalletDirectoriesAsync(baseDir);

		var walletDirectories = new WalletDirectories(Network.Main, baseDir);

		Assert.Empty(walletDirectories.EnumerateWalletFiles());
		Directory.Delete(walletsBackupPath);
		Assert.Empty(walletDirectories.EnumerateWalletFiles());
		Directory.Delete(walletsPath);
		Assert.Empty(walletDirectories.EnumerateWalletFiles());
		Directory.Delete(baseDir);
		Assert.Empty(walletDirectories.EnumerateWalletFiles());
	}

	[Fact]
	public async Task GetNextWalletTestAsync()
	{
		var baseDir = Common.GetWorkDir();
		await CleanupWalletDirectoriesAsync(baseDir);
		var walletDirectories = new WalletDirectories(Network.Main, baseDir);
		IoHelpers.CreateOrOverwriteFile(Path.Combine(walletDirectories.WalletsDir, "Random Wallet 3.json"));

		Assert.Equal("Random Wallet", walletDirectories.GetNextWalletName());
		IoHelpers.CreateOrOverwriteFile(Path.Combine(walletDirectories.WalletsDir, "Random Wallet.json"));
		Assert.Equal("Random Wallet 2", walletDirectories.GetNextWalletName());
		IoHelpers.CreateOrOverwriteFile(Path.Combine(walletDirectories.WalletsDir, "Random Wallet 2.json"));
		Assert.Equal("Random Wallet 4", walletDirectories.GetNextWalletName());

		IoHelpers.CreateOrOverwriteFile(Path.Combine(walletDirectories.WalletsDir, "Random Wallet 4.dat"));
		IoHelpers.CreateOrOverwriteFile(Path.Combine(walletDirectories.WalletsDir, "Random Wallet 4"));
		Assert.Equal("Random Wallet 4", walletDirectories.GetNextWalletName());

		File.Delete(Path.Combine(walletDirectories.WalletsDir, "Random Wallet.json"));
		File.Delete(Path.Combine(walletDirectories.WalletsDir, "Random Wallet 3.json"));
		Assert.Equal("Random Wallet", walletDirectories.GetNextWalletName());
		IoHelpers.CreateOrOverwriteFile(Path.Combine(walletDirectories.WalletsDir, "Random Wallet.json"));
		Assert.Equal("Random Wallet 3", walletDirectories.GetNextWalletName());
		IoHelpers.CreateOrOverwriteFile(Path.Combine(walletDirectories.WalletsDir, "Random Wallet 3.json"));
		File.Delete(Path.Combine(walletDirectories.WalletsDir, "Random Wallet 3.json"));

		Assert.Equal("Foo", walletDirectories.GetNextWalletName("Foo"));
		IoHelpers.CreateOrOverwriteFile(Path.Combine(walletDirectories.WalletsDir, "Foo.json"));
		Assert.Equal("Foo 2", walletDirectories.GetNextWalletName("Foo"));
		IoHelpers.CreateOrOverwriteFile(Path.Combine(walletDirectories.WalletsDir, "Foo 2.json"));
	}

	[Fact]
	public void GetFriendlyNameTest()
	{
		Assert.Equal("Hardware Wallet", HardwareWalletModels.Unknown.FriendlyName());
		Assert.Equal("Coldcard", HardwareWalletModels.Coldcard.FriendlyName());
		Assert.Equal("Coldcard Simulator", HardwareWalletModels.Coldcard_Simulator.FriendlyName());
		Assert.Equal("BitBox", HardwareWalletModels.DigitalBitBox_01.FriendlyName());
		Assert.Equal("BitBox Simulator", HardwareWalletModels.DigitalBitBox_01_Simulator.FriendlyName());
		Assert.Equal("KeepKey", HardwareWalletModels.KeepKey.FriendlyName());
		Assert.Equal("KeepKey Simulator", HardwareWalletModels.KeepKey_Simulator.FriendlyName());
		Assert.Equal("Ledger Nano S", HardwareWalletModels.Ledger_Nano_S.FriendlyName());
		Assert.Equal("Ledger Nano X", HardwareWalletModels.Ledger_Nano_X.FriendlyName());
		Assert.Equal("Trezor One", HardwareWalletModels.Trezor_1.FriendlyName());
		Assert.Equal("Trezor One Simulator", HardwareWalletModels.Trezor_1_Simulator.FriendlyName());
		Assert.Equal("Trezor T", HardwareWalletModels.Trezor_T.FriendlyName());
		Assert.Equal("Trezor T Simulator", HardwareWalletModels.Trezor_T_Simulator.FriendlyName());
		Assert.Equal("Trezor Safe 3", HardwareWalletModels.Trezor_Safe_3.FriendlyName());
		Assert.Equal("BitBox", HardwareWalletModels.BitBox02_BTCOnly.FriendlyName());
		Assert.Equal("BitBox", HardwareWalletModels.BitBox02_Multi.FriendlyName());
		Assert.Equal("Jade", HardwareWalletModels.Jade.FriendlyName());
	}
}
