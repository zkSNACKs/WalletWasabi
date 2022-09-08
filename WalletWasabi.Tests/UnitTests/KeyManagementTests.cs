using System.Collections.Generic;
using NBitcoin;
using System.IO;
using System.Linq;
using System.Security;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Crypto.Randomness;
using WalletWasabi.Extensions;
using WalletWasabi.Logging;
using WalletWasabi.Tests.Helpers;
using Xunit;

namespace WalletWasabi.Tests.UnitTests;

public class KeyManagementTests
{
	[Fact]
	public void CanCreateNew()
	{
		string password = "password";
		var manager = KeyManager.CreateNew(out Mnemonic mnemonic, password, Network.Main);
		var manager2 = KeyManager.CreateNew(out Mnemonic mnemonic2, "", Network.Main);
		var manager3 = KeyManager.CreateNew(out _, "P@ssw0rdé", Network.Main);

		Assert.Equal(12, mnemonic.ToString().Split(' ').Length);
		Assert.Equal(12, mnemonic2.ToString().Split(' ').Length);
		Assert.Equal(12, mnemonic2.ToString().Split(' ').Length);

		Assert.NotNull(manager.ChainCode);
		Assert.NotNull(manager.EncryptedSecret);
		Assert.NotNull(manager.SegwitExtPubKey);

		Assert.NotNull(manager2.ChainCode);
		Assert.NotNull(manager2.EncryptedSecret);
		Assert.NotNull(manager2.SegwitExtPubKey);

		Assert.NotNull(manager3.ChainCode);
		Assert.NotNull(manager3.EncryptedSecret);
		Assert.NotNull(manager3.SegwitExtPubKey);

		var sameManager = new KeyManager(manager.EncryptedSecret, manager.ChainCode, manager.MasterFingerprint, manager.SegwitExtPubKey, manager.TaprootExtPubKey, true, null, new BlockchainState(Network.Main));
		var sameManager2 = new KeyManager(manager.EncryptedSecret, manager.ChainCode, password, Network.Main);
		Logger.TurnOff();
		Assert.Throws<SecurityException>(() => new KeyManager(manager.EncryptedSecret, manager.ChainCode, "differentPassword", Network.Main));
		Logger.TurnOn();

		Assert.Equal(manager.ChainCode, sameManager.ChainCode);
		Assert.Equal(manager.EncryptedSecret, sameManager.EncryptedSecret);
		Assert.Equal(manager.SegwitExtPubKey, sameManager.SegwitExtPubKey);

		Assert.Equal(manager.ChainCode, sameManager2.ChainCode);
		Assert.Equal(manager.EncryptedSecret, sameManager2.EncryptedSecret);
		Assert.Equal(manager.SegwitExtPubKey, sameManager2.SegwitExtPubKey);

		var differentManager = KeyManager.CreateNew(out Mnemonic mnemonic4, password, Network.Main);
		Assert.NotEqual(mnemonic, mnemonic4);
		Assert.NotEqual(manager.ChainCode, differentManager.ChainCode);
		Assert.NotEqual(manager.EncryptedSecret, differentManager.EncryptedSecret);
		Assert.NotEqual(manager.SegwitExtPubKey, differentManager.SegwitExtPubKey);

		var manager5 = new KeyManager(manager2.EncryptedSecret, manager2.ChainCode, password: null!, Network.Main);
		Assert.Equal(manager2.ChainCode, manager5.ChainCode);
		Assert.Equal(manager2.EncryptedSecret, manager5.EncryptedSecret);
		Assert.Equal(manager2.SegwitExtPubKey, manager5.SegwitExtPubKey);
	}

	[Fact]
	public void CanRecover()
	{
		string password = "password";
		var manager = KeyManager.CreateNew(out Mnemonic mnemonic, password, Network.Main);
		var sameManager = KeyManager.Recover(mnemonic, password, Network.Main, KeyManager.GetAccountKeyPath(Network.Main, ScriptPubKeyType.Segwit));

		Assert.Equal(manager.ChainCode, sameManager.ChainCode);
		Assert.Equal(manager.EncryptedSecret, sameManager.EncryptedSecret);
		Assert.Equal(manager.SegwitExtPubKey, sameManager.SegwitExtPubKey);

		var differentManager = KeyManager.Recover(mnemonic, "differentPassword", Network.Main, KeyPath.Parse("m/999'/999'/999'"), null, null, 55);
		Assert.NotEqual(manager.ChainCode, differentManager.ChainCode);
		Assert.NotEqual(manager.EncryptedSecret, differentManager.EncryptedSecret);
		Assert.NotEqual(manager.SegwitExtPubKey, differentManager.SegwitExtPubKey);

		var newKey = differentManager.GenerateNewKey("some-label", KeyState.Clean, true);
		Assert.Equal(newKey.Index, differentManager.MinGapLimit);
		Assert.Equal("999'/999'/999'/1/55", newKey.FullKeyPath.ToString());
	}

	[Fact]
	public void CanHandleGap()
	{
		string password = "password";
		var manager = KeyManager.CreateNew(out _, password, Network.Main);

		var lastKey = manager.GetKeys(KeyState.Clean, isInternal: false).Last();
		manager.SetKeyState(KeyState.Used, lastKey);

		var newLastKey = manager.GetKeys(KeyState.Clean, isInternal: false).Last();
		Assert.Equal(manager.MinGapLimit, newLastKey.Index - lastKey.Index);
	}

	[Fact]
	public void CanSerialize()
	{
		string password = "password";

		var filePath = Path.Combine(Common.GetWorkDir(), "Wallet.json");
		DeleteFileAndDirectoryIfExists(filePath);

		Logger.TurnOff();
		Assert.Throws<FileNotFoundException>(() => KeyManager.FromFile(filePath));
		Logger.TurnOn();

		var manager = KeyManager.CreateNew(out _, password, Network.Main, filePath);
		KeyManager.FromFile(filePath);

		manager.ToFile();

		manager.ToFile(); // assert it does not throw

		for (int i = 0; i < 1000; i++)
		{
			var isInternal = Random.Shared.Next(2) == 0;
			var label = RandomString.AlphaNumeric(21);
			var keyState = (KeyState)Random.Shared.Next(3);
			manager.GenerateNewKey(label, keyState, isInternal);
		}
		manager.ToFile();

		Assert.True(File.Exists(filePath));

		var sameManager = KeyManager.FromFile(filePath);

		Assert.Equal(manager.ChainCode, sameManager.ChainCode);
		Assert.Equal(manager.EncryptedSecret, sameManager.EncryptedSecret);
		Assert.Equal(manager.SegwitExtPubKey, sameManager.SegwitExtPubKey);

		DeleteFileAndDirectoryIfExists(filePath);
	}

	[Fact]
	public void CanGenerateKeys()
	{
		string password = "password";
		var network = Network.Main;
		var manager = KeyManager.CreateNew(out _, password, network);

		var k1 = manager.GenerateNewKey(SmartLabel.Empty, KeyState.Clean, true);
		Assert.Equal(SmartLabel.Empty, k1.Label);

		for (int i = 0; i < 1000; i++)
		{
			var isInternal = Random.Shared.Next(2) == 0;
			var label = RandomString.AlphaNumeric(21);
			var keyState = (KeyState)Random.Shared.Next(3);
			var generatedKey = manager.GenerateNewKey(label, keyState, isInternal);

			Assert.Equal(isInternal, generatedKey.IsInternal);
			Assert.Equal(label, generatedKey.Label);
			Assert.Equal(keyState, generatedKey.KeyState);
			Assert.StartsWith(KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit).ToString(), generatedKey.FullKeyPath.ToString());
		}
	}

	[Fact]
	public void GapCountingTests()
	{
		var km = KeyManager.CreateNew(out _, "", Network.Main);
		var hdPubKeys = Enumerable.Range(0, 100)
			.Select(i => km.GenerateNewKey(SmartLabel.Empty, i % 2 == 0 ? KeyState.Clean : KeyState.Locked, true))
			.ToArray();

		km.SetKeyState(KeyState.Used, hdPubKeys[0]);
		Assert.Equal(0, km.CountConsecutiveUnusedKeys(true));

		km.SetKeyState(KeyState.Used, hdPubKeys[10]);
		Assert.Equal(10, km.CountConsecutiveUnusedKeys(true));

		km.SetKeyState(KeyState.Used, hdPubKeys[30]);
		Assert.Equal(20, km.CountConsecutiveUnusedKeys(true));

		km.SetKeyState(KeyState.Used, hdPubKeys[80]);
		Assert.Equal(50, km.CountConsecutiveUnusedKeys(true));

		km.SetKeyState(KeyState.Clean, hdPubKeys[30]);
		Assert.Equal(70, km.CountConsecutiveUnusedKeys(true));
	}

	private static void DeleteFileAndDirectoryIfExists(string filePath)
	{
		var dir = Path.GetDirectoryName(filePath);

		if (File.Exists(filePath))
		{
			File.Delete(filePath);
		}

		if (dir is not null && Directory.Exists(dir))
		{
			if (Directory.GetFiles(dir).Length == 0)
			{
				Directory.Delete(dir);
			}
		}
	}
}
