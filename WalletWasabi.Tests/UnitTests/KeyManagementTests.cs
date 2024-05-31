using NBitcoin;
using System.IO;
using System.Linq;
using System.Security;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Crypto.Randomness;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Tests.Helpers;
using Xunit;
using WalletWasabi.Wallets;

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
		Assert.NotNull(manager.TaprootExtPubKey);

		Assert.NotNull(manager2.ChainCode);
		Assert.NotNull(manager2.EncryptedSecret);
		Assert.NotNull(manager2.SegwitExtPubKey);
		Assert.NotNull(manager2.TaprootExtPubKey);

		Assert.NotNull(manager3.ChainCode);
		Assert.NotNull(manager3.EncryptedSecret);
		Assert.NotNull(manager3.SegwitExtPubKey);
		Assert.NotNull(manager3.TaprootExtPubKey);

		var sameManager = new KeyManager(manager.EncryptedSecret, manager.ChainCode, manager.MasterFingerprint, manager.SegwitExtPubKey, manager.TaprootExtPubKey, null, new BlockchainState(Network.Main));
		var sameManager2 = new KeyManager(manager.EncryptedSecret, manager.ChainCode, password, Network.Main);
		Logger.TurnOff();
		Assert.Throws<SecurityException>(() => new KeyManager(manager.EncryptedSecret, manager.ChainCode, "differentPassword", Network.Main));
		Logger.TurnOn();

		Assert.Equal(manager.ChainCode, sameManager.ChainCode);
		Assert.Equal(manager.EncryptedSecret, sameManager.EncryptedSecret);
		Assert.Equal(manager.SegwitExtPubKey, sameManager.SegwitExtPubKey);
		Assert.Equal(manager.TaprootExtPubKey, sameManager.TaprootExtPubKey);

		Assert.Equal(manager.ChainCode, sameManager2.ChainCode);
		Assert.Equal(manager.EncryptedSecret, sameManager2.EncryptedSecret);
		Assert.Equal(manager.SegwitExtPubKey, sameManager2.SegwitExtPubKey);
		Assert.Equal(manager.TaprootExtPubKey, sameManager2.TaprootExtPubKey);

		var differentManager = KeyManager.CreateNew(out Mnemonic mnemonic4, password, Network.Main);
		Assert.NotEqual(mnemonic, mnemonic4);
		Assert.NotEqual(manager.ChainCode, differentManager.ChainCode);
		Assert.NotEqual(manager.EncryptedSecret, differentManager.EncryptedSecret);
		Assert.NotEqual(manager.SegwitExtPubKey, differentManager.SegwitExtPubKey);
		Assert.NotEqual(manager.TaprootExtPubKey, differentManager.TaprootExtPubKey);

		var manager5 = new KeyManager(manager2.EncryptedSecret, manager2.ChainCode, password: null!, Network.Main);
		Assert.Equal(manager2.ChainCode, manager5.ChainCode);
		Assert.Equal(manager2.EncryptedSecret, manager5.EncryptedSecret);
		Assert.Equal(manager2.SegwitExtPubKey, manager5.SegwitExtPubKey);
		Assert.Equal(manager2.TaprootExtPubKey, manager5.TaprootExtPubKey);
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
		Assert.Equal(manager.TaprootExtPubKey, sameManager.TaprootExtPubKey);

		var differentManager = KeyManager.Recover(mnemonic, "differentPassword", Network.Main, KeyPath.Parse("m/999'/999'/999'"), null, null, 55);
		Assert.NotEqual(manager.ChainCode, differentManager.ChainCode);
		Assert.NotEqual(manager.EncryptedSecret, differentManager.EncryptedSecret);
		Assert.NotEqual(manager.SegwitExtPubKey, differentManager.SegwitExtPubKey);
		Assert.NotEqual(manager.TaprootExtPubKey, differentManager.TaprootExtPubKey);

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

		var filePath = Path.Combine(Common.GetWorkDir(nameof(CanSerialize)), "Wallet.json");
		DeleteFileAndDirectoryIfExists(filePath);

		Logger.TurnOff();
		Assert.Throws<FileNotFoundException>(() => KeyManager.FromFile(filePath));
		Logger.TurnOn();

		var manager = KeyManager.CreateNew(out _, password, Network.Main, filePath);
		KeyManager.FromFile(filePath);

		manager.ToFile();

		manager.ToFile(); // assert it does not throw

		void Generate500keys(ScriptPubKeyType scriptPubKeyType)
		{
			for (int i = 0; i < 500; i++)
			{
				var isInternal = Random.Shared.Next(2) == 0;
				var label = RandomString.AlphaNumeric(21);
				var keyState = (KeyState)Random.Shared.Next(3);
				manager.GenerateNewKey(label, keyState, isInternal, scriptPubKeyType);
			}

			manager.ToFile();
		}

		Generate500keys(ScriptPubKeyType.Segwit);
		Generate500keys(ScriptPubKeyType.TaprootBIP86);

		Assert.True(File.Exists(filePath));

		var sameManager = KeyManager.FromFile(filePath);

		Assert.Equal(manager.ChainCode, sameManager.ChainCode);
		Assert.Equal(manager.EncryptedSecret, sameManager.EncryptedSecret);
		Assert.Equal(manager.SegwitExtPubKey, sameManager.SegwitExtPubKey);
		Assert.Equal(manager.TaprootExtPubKey, sameManager.TaprootExtPubKey);

		DeleteFileAndDirectoryIfExists(filePath);
	}

	[Fact]
	public void CanSerializeHeightCorrectly()
	{
		var filePath = "wallet-serialization.json";
		var manager = KeyManager.CreateNew(out _, "", Network.Main, filePath);
		manager.SetBestHeight(10_000);
		manager.ToFile();

		var sameManager = KeyManager.FromFile(filePath);
		Assert.Equal(new Height(9_899), sameManager.GetBestHeight(SyncType.Complete));

		DeleteFileAndDirectoryIfExists(filePath);
	}

	[Fact]
	public void CanGenerateKeys()
	{
		string password = "password";
		var network = Network.Main;
		var manager = KeyManager.CreateNew(out _, password, network);

		var k1 = manager.GenerateNewKey(LabelsArray.Empty, KeyState.Clean, true);
		Assert.Equal(LabelsArray.Empty, k1.Labels);

		for (int i = 0; i < 1000; i++)
		{
			var isInternal = Random.Shared.Next(2) == 0;
			var label = RandomString.AlphaNumeric(21);
			var keyState = (KeyState)Random.Shared.Next(3);
			var generatedKey = manager.GenerateNewKey(label, keyState, isInternal);

			Assert.Equal(isInternal, generatedKey.IsInternal);
			Assert.Equal(label, generatedKey.Labels);
			Assert.Equal(keyState, generatedKey.KeyState);
			Assert.StartsWith(KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit).ToString(), generatedKey.FullKeyPath.ToString());
		}
	}

	[Fact]
	public void CanGenerateRealKeys()
	{
		string password = "password";
		var network = Network.Main;
		var manager = KeyManager.CreateNew(out _, password, network);

		var labels = new LabelsArray("who-knows");
		var segwitKey = manager.GetNextReceiveKey(labels, ScriptPubKeyType.Segwit);
		var taprootKey = manager.GetNextReceiveKey(labels, ScriptPubKeyType.TaprootBIP86);
		Assert.Equal("84'/0'/0'/0/0", segwitKey.FullKeyPath.ToString());
		Assert.Equal("86'/0'/0'/0/0", taprootKey.FullKeyPath.ToString());
	}

	[Fact]
	public void AlternatesScriptTypeForChange()
	{
		var keyManager = KeyManager.CreateNew(out _, "", Network.Main);
		var keysForChange = Enumerable
			.Range(0, 10)
			.Select(_ =>
			{
				var key = keyManager.GetNextChangeKey();
				keyManager.SetKeyState(KeyState.Used, key);
				return key;
			})
			.ToList();

		static bool IsScriptType(HdPubKey key, ScriptPubKeyType scriptType) =>
			key.FullKeyPath.GetScriptTypeFromKeyPath() == scriptType;

		static bool IsTaproot(HdPubKey key) => IsScriptType(key, ScriptPubKeyType.TaprootBIP86);
		static bool IsSegwit(HdPubKey key) => IsScriptType(key, ScriptPubKeyType.Segwit);

		Assert.Contains(keysForChange, IsTaproot);
		Assert.Contains(keysForChange, IsSegwit);
		Assert.Equal(keysForChange.Count(IsTaproot), keysForChange.Count(IsSegwit));
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
