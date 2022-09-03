using NBitcoin;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security;
using System.Text;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Extensions;
using WalletWasabi.Helpers;
using WalletWasabi.Io;
using WalletWasabi.JsonConverters;
using WalletWasabi.JsonConverters.Bitcoin;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Wallets;
using static WalletWasabi.Blockchain.Keys.WpkhOutputDescriptorHelper;

namespace WalletWasabi.Blockchain.Keys;

[JsonObject(MemberSerialization.OptIn)]
public class KeyManager
{
	public const int DefaultAnonScoreTarget = 5;
	public const bool DefaultAutoCoinjoin = false;
	public const bool DefaultRedCoinIsolation = false;
	public const int DefaultFeeRateMedianTimeFrameHours = 0;

	public const int AbsoluteMinGapLimit = 21;
	public const int MaxGapLimit = 10_000;
	public static Money DefaultPlebStopThreshold = Money.Coins(0.01m);

	// BIP84-ish derivation scheme
	// m / purpose' / coin_type' / account' / change / address_index
	// https://github.com/bitcoin/bips/blob/master/bip-0084.mediawiki
	private static readonly KeyPath DefaultAccountKeyPath = new("m/84h/0h/0h");

	private static readonly KeyPath TestNetAccountKeyPath = new("m/84h/1h/0h");

	private static readonly JsonConverter[] JsonConverters =
	{
		new BitcoinEncryptedSecretNoECJsonConverter(),
		new ByteArrayJsonConverter(),
		new HDFingerprintJsonConverter(),
		new ExtPubKeyJsonConverter(),
		new KeyPathJsonConverter(),
		new MoneyBtcJsonConverter()
	};

	[JsonConstructor]
	public KeyManager(BitcoinEncryptedSecretNoEC encryptedSecret, byte[] chainCode, HDFingerprint? masterFingerprint, ExtPubKey extPubKey, bool skipSynchronization, int? minGapLimit, BlockchainState blockchainState, string? filePath = null, KeyPath? accountKeyPath = null)
	{
		EncryptedSecret = encryptedSecret;
		ChainCode = chainCode;
		MasterFingerprint = masterFingerprint;
		ExtPubKey = Guard.NotNull(nameof(extPubKey), extPubKey);

		SkipSynchronization = skipSynchronization;
		SetMinGapLimit(minGapLimit);

		BlockchainState = blockchainState;

		AccountKeyPath = accountKeyPath ?? GetAccountKeyPath(BlockchainState.Network);

		SegWitExternalKeys = new HdPubKeyManager(ExtPubKey.Derive(0), AccountKeyPath.Derive(0), HdPubKeyCache, MinGapLimit);
		SegWitInternalKeys = new HdPubKeyManager(ExtPubKey.Derive(1), AccountKeyPath.Derive(1), HdPubKeyCache, MinGapLimit);
		
		SetFilePath(filePath);
		ToFile();
	}

	public KeyManager(BitcoinEncryptedSecretNoEC encryptedSecret, byte[] chainCode, string password, Network network)
	{
		BlockchainState = new BlockchainState(network);

		password ??= "";

		SetMinGapLimit(AbsoluteMinGapLimit);

		EncryptedSecret = Guard.NotNull(nameof(encryptedSecret), encryptedSecret);
		ChainCode = Guard.NotNull(nameof(chainCode), chainCode);
		var extKey = new ExtKey(encryptedSecret.GetKey(password), chainCode);

		MasterFingerprint = extKey.Neuter().PubKey.GetHDFingerPrint();
		AccountKeyPath = GetAccountKeyPath(BlockchainState.Network);
		ExtPubKey = extKey.Derive(AccountKeyPath).Neuter();
		
		SegWitExternalKeys = new HdPubKeyManager(ExtPubKey.Derive(0), AccountKeyPath.Derive(0), HdPubKeyCache, MinGapLimit);
		SegWitInternalKeys = new HdPubKeyManager(ExtPubKey.Derive(1), AccountKeyPath.Derive(1), HdPubKeyCache, MinGapLimit);
	}

	[OnDeserialized]
	private void OnDeserializedMethod(StreamingContext context)
	{
		// This should be impossible but in any case, coinjoin can only happen,
		// if a profile is selected. Otherwise, the user's money can be drained.
		if (AutoCoinJoin && !IsCoinjoinProfileSelected)
		{
			AutoCoinJoin = false;
		}
		HdPubKeyCache.AddRangeKeys(HdPubKeys);
	}

	public static KeyPath GetAccountKeyPath(Network network) =>
		network == Network.TestNet ? TestNetAccountKeyPath : DefaultAccountKeyPath;

	public WpkhDescriptors GetOutputDescriptors(string password, Network network)
	{
		if (!MasterFingerprint.HasValue)
		{
			throw new InvalidOperationException($"{nameof(MasterFingerprint)} is not defined.");
		}

		return WpkhOutputDescriptorHelper.GetOutputDescriptors(network, MasterFingerprint.Value, GetMasterExtKey(password), AccountKeyPath);
	}

	[JsonProperty(PropertyName = "EncryptedSecret")]
	public BitcoinEncryptedSecretNoEC EncryptedSecret { get; }

	[JsonProperty(PropertyName = "ChainCode")]
	public byte[] ChainCode { get; }

	[JsonProperty(PropertyName = "MasterFingerprint")]
	public HDFingerprint? MasterFingerprint { get; private set; }

	[JsonProperty(PropertyName = "ExtPubKey")]
	public ExtPubKey ExtPubKey { get; }

	[JsonProperty(PropertyName = "SkipSynchronization")]
	public bool SkipSynchronization { get; private set; } = false;

	[JsonProperty(PropertyName = "MinGapLimit")]
	public int MinGapLimit { get; private set; }

	[JsonProperty(PropertyName = "AccountKeyPath")]
	public KeyPath AccountKeyPath { get; private set; }

	[JsonProperty(PropertyName = "BlockchainState")]
	private BlockchainState BlockchainState { get; }

	[JsonProperty(PropertyName = "PreferPsbtWorkflow")]
	public bool PreferPsbtWorkflow { get; set; }

	[JsonProperty(PropertyName = "AutoCoinJoin")]
	public bool AutoCoinJoin { get; set; } = DefaultAutoCoinjoin;

	/// <summary>
	/// Won't coinjoin automatically if there are less than this much non-private coins in the wallet.
	/// </summary>
	[JsonProperty(PropertyName = "PlebStopThreshold")]
	[JsonConverter(typeof(MoneyBtcJsonConverter))]
	public Money PlebStopThreshold { get; set; } = DefaultPlebStopThreshold;

	[JsonProperty(PropertyName = "Icon")]
	public string? Icon { get; private set; }

	[JsonProperty(PropertyName = "AnonScoreTarget")]
	public int AnonScoreTarget { get; private set; } = DefaultAnonScoreTarget;

	[JsonProperty(PropertyName = "FeeRateMedianTimeFrameHours")]
	public int FeeRateMedianTimeFrameHours { get; private set; } = DefaultFeeRateMedianTimeFrameHours;

	[JsonProperty(PropertyName = "IsCoinjoinProfileSelected")]
	public bool IsCoinjoinProfileSelected { get; set; } = false;

	[JsonProperty(PropertyName = "RedCoinIsolation")]
	public bool RedCoinIsolation { get; set; } = DefaultRedCoinIsolation;

	[JsonProperty(Order = 999, PropertyName = "HdPubKeys")]
	private List<HdPubKey> HdPubKeys { get; } = new();

	public string? FilePath { get; private set; }

	[MemberNotNullWhen(returnValue: false, nameof(EncryptedSecret))]
	public bool IsWatchOnly => EncryptedSecret is null;

	[MemberNotNullWhen(returnValue: true, nameof(MasterFingerprint))]
	public bool IsHardwareWallet => EncryptedSecret is null && MasterFingerprint is not null;

	private HdPubKeyCache HdPubKeyCache { get; } = new();
	private object HdPubKeyRegistryLock { get; } = new();
	
	private object BlockchainStateLock { get; } = new();

	private object ToFileLock { get; } = new();

	private HdPubKeyManager SegWitExternalKeys { get; }
	private HdPubKeyManager SegWitInternalKeys { get; }
	
	public string WalletName => string.IsNullOrWhiteSpace(FilePath) ? "" : Path.GetFileNameWithoutExtension(FilePath);

	public static KeyManager CreateNew(out Mnemonic mnemonic, string password, Network network, string? filePath = null)
	{
		mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
		return CreateNew(mnemonic, password, network, filePath);
	}

	public static KeyManager CreateNew(Mnemonic mnemonic, string password, Network network, string? filePath = null)
	{
		password ??= "";

		ExtKey extKey = mnemonic.DeriveExtKey(password);
		var encryptedSecret = extKey.PrivateKey.GetEncryptedBitcoinSecret(password, Network.Main);

		HDFingerprint masterFingerprint = extKey.Neuter().PubKey.GetHDFingerPrint();
		BlockchainState blockchainState = new(network);
		KeyPath keyPath = GetAccountKeyPath(network);
		ExtPubKey extPubKey = extKey.Derive(keyPath).Neuter();
		return new KeyManager(encryptedSecret, extKey.ChainCode, masterFingerprint, extPubKey, skipSynchronization: true, AbsoluteMinGapLimit, blockchainState, filePath, keyPath);
	}

	public static KeyManager CreateNewWatchOnly(ExtPubKey extPubKey, string? filePath = null)
	{
		return new KeyManager(null, null, null, extPubKey, skipSynchronization: false, AbsoluteMinGapLimit, new BlockchainState(), filePath);
	}

	public static KeyManager CreateNewHardwareWalletWatchOnly(HDFingerprint masterFingerprint, ExtPubKey extPubKey, Network network, string? filePath = null)
	{
		return new KeyManager(null, null, masterFingerprint, extPubKey, skipSynchronization: false, AbsoluteMinGapLimit, new BlockchainState(network), filePath);
	}

	public static KeyManager Recover(Mnemonic mnemonic, string password, Network network, KeyPath accountKeyPath, string? filePath = null, int minGapLimit = AbsoluteMinGapLimit)
	{
		Guard.NotNull(nameof(mnemonic), mnemonic);
		password ??= "";

		ExtKey extKey = mnemonic.DeriveExtKey(password);
		var encryptedSecret = extKey.PrivateKey.GetEncryptedBitcoinSecret(password, Network.Main);

		HDFingerprint masterFingerprint = extKey.Neuter().PubKey.GetHDFingerPrint();

		KeyPath keyPath = accountKeyPath ?? DefaultAccountKeyPath;
		ExtPubKey extPubKey = extKey.Derive(keyPath).Neuter();
		return new KeyManager(encryptedSecret, extKey.ChainCode, masterFingerprint, extPubKey, skipSynchronization: false, minGapLimit, new BlockchainState(network), filePath, keyPath);
	}

	public static KeyManager FromFile(string filePath)
	{
		filePath = Guard.NotNullOrEmptyOrWhitespace(nameof(filePath), filePath);

		if (!File.Exists(filePath))
		{
			throw new FileNotFoundException($"Wallet file not found at: `{filePath}`.");
		}

		SafeIoManager safeIoManager = new(filePath);
		string jsonString = safeIoManager.ReadAllText(Encoding.UTF8);

		KeyManager km = JsonConvert.DeserializeObject<KeyManager>(jsonString, JsonConverters)
			?? throw new JsonSerializationException($"Wallet file at: `{filePath}` is not a valid wallet file or it is corrupted.");

		km.SetFilePath(filePath);
		
		return km;
	}

	public void SetFilePath(string? filePath)
	{
		FilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath;
		if (FilePath is null)
		{
			return;
		}

		IoHelpers.EnsureContainingDirectoryExists(FilePath);
	}

	public HdPubKey GenerateNewPersistentKey(SmartLabel label, KeyState keyState, bool isInternal)
	{
		var newKey = GenerateNewKey(label, keyState, isInternal);
		ToFile();
		return newKey;
	}

	public HdPubKey GenerateNewKey(SmartLabel label, KeyState keyState, bool isInternal)
	{
		var hdPubKeyRegistry = isInternal
			? SegWitInternalKeys
			: SegWitExternalKeys;

		lock (HdPubKeyRegistryLock)
		{
			var (keyPath, extPubKey) = hdPubKeyRegistry.GenerateNewKey();
			var hdPubKey = new HdPubKey(extPubKey.PubKey, keyPath, label, keyState);
			HdPubKeyCache.AddKey(hdPubKey, ScriptPubKeyType.Segwit);

			HdPubKeys.Add(hdPubKey);
			return hdPubKey;
		}
	}

	public HdPubKey GetNextReceiveKey(SmartLabel label, out bool minGapLimitIncreased)
	{
		if (label.IsEmpty)
		{
			throw new InvalidOperationException("Label is required.");
		}

		minGapLimitIncreased = false;

		AssertCleanKeysIndexed(isInternal: false);

		// Find the next clean external key with empty label.
		var newKey = GetKeys(x => x.IsInternal == false && x.KeyState == KeyState.Clean && x.Label.IsEmpty).FirstOrDefault();

		// If not found, generate a new.
		if (newKey is null)
		{
			SetMinGapLimit(MinGapLimit + 1);
			newKey = AssertCleanKeysIndexed(isInternal: false).First();

			// If the new is over the MinGapLimit, set minGapLimitIncreased to true.
			minGapLimitIncreased = true;
		}

		newKey.SetLabel(label, kmToFile: this);

		SetDoNotSkipSynchronization();

		return newKey;
	}

	public IEnumerable<HdPubKey> GetKeys(Func<HdPubKey, bool>? wherePredicate)
	{
		// BIP44-ish derivation scheme
		// m / purpose' / coin_type' / account' / change / address_index
		lock (HdPubKeyRegistryLock)
		{
			var allKeys = HdPubKeyCache.Select(x => x.HdPubKey);
			return wherePredicate switch
			{
				null => allKeys.ToList(),
				_ => allKeys.Where(wherePredicate).ToList()
			};
		}
	}

	public void SetDoNotSkipSynchronization()
	{
		// Don't set it unnecessarily
		if (SkipSynchronization == false)
		{
			return;
		}

		SkipSynchronization = false;
		ToFile();
	}

	public IEnumerable<HdPubKey> GetKeys(KeyState? keyState = null, bool? isInternal = null) =>
		(keyState, isInternal) switch
		{
			(null, null) => GetKeys(x => true),
			(null, { } i) => GetKeys(x => x.IsInternal == i),
			({ } k, null) => GetKeys(x => x.KeyState == k),
			({ } k, { } i) => GetKeys(x => x.IsInternal == i && x.KeyState == k)
		};

	public int CountConsecutiveUnusedKeys(bool isInternal)
	{
		var keySource = isInternal ? SegWitInternalKeys : SegWitExternalKeys;
		return keySource.GetGaps().MaxOrDefault(0);
	}

	public IEnumerable<byte[]> GetPubKeyScriptBytes()
	{
		lock (HdPubKeyRegistryLock)
		{
			return HdPubKeyCache.Select(x => x.ScriptPubKeyBytes).ToList();
		}
	}

	public bool TryGetKeyForScriptPubKey(Script scriptPubKey, [NotNullWhen(true)] out HdPubKey? hdPubKey)
	{
		hdPubKey = default;

		lock (HdPubKeyRegistryLock)
		{
			if (HdPubKeyCache.TryGetPubKey(scriptPubKey, out var key))
			{
				hdPubKey = key.HdPubKey;
				return true;
			}

			return false;
		}
	}

	public IEnumerable<ExtKey> GetSecrets(string password, params Script[] scripts)
	{
		return GetSecretsAndPubKeyPairs(password, scripts).Select(x => x.secret);
	}

	public IEnumerable<(ExtKey secret, HdPubKey pubKey)> GetSecretsAndPubKeyPairs(string password, params Script[] scripts)
	{
		ExtKey extKey = GetMasterExtKey(password);
		var extKeysAndPubs = new List<(ExtKey secret, HdPubKey pubKey)>();

		lock (HdPubKeyRegistryLock)
		{
			foreach (HdPubKey key in GetKeys(x =>
				scripts.Contains(x.P2wpkhScript)
				|| scripts.Contains(x.P2shOverP2wpkhScript)
				|| scripts.Contains(x.P2pkhScript)
				|| scripts.Contains(x.P2pkScript)))
			{
				ExtKey ek = extKey.Derive(key.FullKeyPath);
				extKeysAndPubs.Add((ek, key));
			}
		}
		return extKeysAndPubs;
	}

	public IEnumerable<SmartLabel> GetChangeLabels() => GetKeys(x => x.IsInternal).Select(x => x.Label);

	public IEnumerable<SmartLabel> GetReceiveLabels() => GetKeys(x => !x.IsInternal).Select(x => x.Label);

	public ExtKey GetMasterExtKey(string password)
	{
		password ??= "";

		if (IsWatchOnly)
		{
			throw new SecurityException("This is a watchonly wallet.");
		}

		try
		{
			Key secret = EncryptedSecret.GetKey(password);
			var extKey = new ExtKey(secret, ChainCode);

			// Backwards compatibility:
			MasterFingerprint ??= secret.PubKey.GetHDFingerPrint();

			return extKey;
		}
		catch (SecurityException ex)
		{
			throw new SecurityException("Invalid password.", ex);
		}
	}

	/// <summary>
	/// Make sure there's always clean keys generated and indexed.
	/// Call SetMinGapLimit() to set how many keys should be asserted.
	/// </summary>
	public IEnumerable<HdPubKey> AssertCleanKeysIndexedAndPersist(bool? isInternal = null)
	{
		var newKeys = AssertCleanKeysIndexed(isInternal);
		if (newKeys.Any())
		{
			ToFile();
		}

		return newKeys;
	}

	public IEnumerable<HdPubKey> AssertCleanKeysIndexed(bool? isInternal = null)
	{
		var newKeys = isInternal switch
		{
			true => SegWitInternalKeys.AssertCleanKeysIndexed(),
			false => SegWitExternalKeys.AssertCleanKeysIndexed(),
			null => SegWitInternalKeys.AssertCleanKeysIndexed().Concat(
				    SegWitExternalKeys.AssertCleanKeysIndexed())
		};

		return HdPubKeyCache.AddRangeKeys(newKeys.Select(CreateHdPubKey));
	}

	/// <summary>
	/// Make sure there's always locked internal keys generated and indexed.
	/// </summary>
	public void AssertLockedInternalKeysIndexedAndPersist(int howMany)
	{
		if (AssertLockedInternalKeysIndexed(howMany))
		{
			ToFile();
		}
	}

	public bool AssertLockedInternalKeysIndexed(int howMany)
	{
		Guard.InRangeAndNotNull(nameof(howMany), howMany, 0, SegWitInternalKeys.MinGapLimit);
		var lockedKeyCount = SegWitInternalKeys.LockedKeys.Count();
		var missingLockedKeys = Math.Max(howMany - lockedKeyCount, 0);
			
		HdPubKeyCache.AddRangeKeys(SegWitInternalKeys.AssertCleanKeysIndexed().Select(CreateHdPubKey));

		var availableCandidates = SegWitInternalKeys
			.CleanKeys
			.Where(x => x.Label.IsEmpty)
			.Take(missingLockedKeys)
			.ToList();

		foreach (var hdPubKeys in availableCandidates)
		{
			hdPubKeys.SetKeyState(KeyState.Locked);
		}

		return availableCandidates.Count > 0;
	}

	private void SetMinGapLimit(int? minGapLimit)
	{
		MinGapLimit = minGapLimit is int val ? Math.Max(AbsoluteMinGapLimit, val) : AbsoluteMinGapLimit;
		// AssertCleanKeysIndexed(); Do not do this. Wallet file is null yet.
	}

	public void ToFile()
	{
		lock (HdPubKeyRegistryLock)
		{
			lock (BlockchainStateLock)
			{
				lock (ToFileLock)
				{
					ToFileNoLock();
				}
			}
		}
	}

	public void ToFile(string filePath)
	{
		lock (HdPubKeyRegistryLock)
		{
			lock (BlockchainStateLock)
			{
				lock (ToFileLock)
				{
					ToFileNoLock(filePath);
				}
			}
		}
	}

	
	private void ToFileNoBlockchainStateLock()
	{
		lock (HdPubKeyRegistryLock)
		{
			lock (ToFileLock)
			{
				ToFileNoLock();
			}
		}
	}

	private void ToFileNoLock()
	{
		if (FilePath is null)
		{
			return;
		}

		ToFileNoLock(FilePath);
	}

	private void ToFileNoLock(string filePath)
	{
		IoHelpers.EnsureContainingDirectoryExists(filePath);
		// Remove the last 100 blocks to ensure verification on the next run. This is needed of reorg.
		int maturity = 101;
		Height prevHeight = BlockchainState.Height;
		int matureHeight = Math.Max(0, prevHeight.Value - maturity);

		BlockchainState.Height = new Height(matureHeight);

		string jsonString = JsonConvert.SerializeObject(this, Formatting.Indented, JsonConverters);

		SafeIoManager safeIoManager = new(filePath);
		safeIoManager.WriteAllText(jsonString, Encoding.UTF8);

		// Re-add removed items for further operations.
		BlockchainState.Height = prevHeight;
	}

	#region BlockchainState

	public Height GetBestHeight()
	{
		Height res;
		lock (BlockchainStateLock)
		{
			res = BlockchainState.Height;
		}
		return res;
	}

	public Network GetNetwork()
	{
		lock (BlockchainStateLock)
		{
			return BlockchainState.Network;
		}
	}

	public void SetBestHeight(Height height)
	{
		lock (BlockchainStateLock)
		{
			BlockchainState.Height = height;
			ToFileNoBlockchainStateLock();
		}
	}

	public void SetMaxBestHeight(Height height)
	{
		lock (BlockchainStateLock)
		{
			var prevHeight = BlockchainState.Height;
			var newHeight = Math.Min(prevHeight, height);
			if (prevHeight != newHeight)
			{
				BlockchainState.Height = newHeight;
				ToFileNoBlockchainStateLock();
				Logger.LogWarning($"Wallet ({WalletName}) height has been set back by {prevHeight - newHeight}. From {prevHeight} to {newHeight}.");
			}
		}
	}

	public void SetIcon(string icon)
	{
		Icon = icon;
		ToFile();
	}

	public void SetIcon(WalletType type)
	{
		SetIcon(type.ToString());
	}

	public void SetAnonScoreTarget(int anonScoreTarget, bool toFile = true)
	{
		AnonScoreTarget = anonScoreTarget;
		if (toFile)
		{
			ToFile();
		}
	}

	public void SetFeeRateMedianTimeFrame(int hours, bool toFile = true)
	{
		if (hours != 0 && !Constants.CoinJoinFeeRateMedianTimeFrames.Contains(hours))
		{
			throw new ArgumentOutOfRangeException(nameof(hours), $"Hours can be only one of {string.Join(",", Constants.CoinJoinFeeRateMedianTimeFrames)}.");
		}

		FeeRateMedianTimeFrameHours = hours;
		if (toFile)
		{
			ToFile();
		}
	}

	public void AssertNetworkOrClearBlockState(Network expectedNetwork)
	{
		lock (BlockchainStateLock)
		{
			var lastNetwork = BlockchainState.Network;
			if (lastNetwork is null || lastNetwork != expectedNetwork)
			{
				BlockchainState.Network = expectedNetwork;
				BlockchainState.Height = 0;
				ToFileNoBlockchainStateLock();

				if (lastNetwork is { })
				{
					Logger.LogWarning($"Wallet is opened on {expectedNetwork}. Last time it was opened on {lastNetwork}.");
				}
				Logger.LogInfo("Blockchain cache is cleared.");
			}
		}
	}

	#endregion BlockchainState
	
	private static HdPubKey CreateHdPubKey((KeyPath KeyPath, ExtPubKey ExtPubKey) x) =>
		new (x.ExtPubKey.PubKey, x.KeyPath, SmartLabel.Empty, KeyState.Clean);
}
