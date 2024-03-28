using NBitcoin;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using WalletWasabi.Bases;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Helpers;
using WalletWasabi.JsonConverters;
using WalletWasabi.Models;

namespace WalletWasabi.Blockchain.Keys;

[JsonObject(MemberSerialization.OptIn)]
public class HdPubKey : NotifyPropertyChangedBase, IEquatable<HdPubKey>
{
	public const int DefaultHighAnonymitySet = int.MaxValue;

	private readonly Lazy<Script> _p2wpkhScript;
	private readonly Lazy<Script> _p2Taproot;

	private double _anonymitySet = DefaultHighAnonymitySet;
	private Cluster _cluster;

	public HdPubKey(PubKey pubKey, KeyPath fullKeyPath, LabelsArray labels, KeyState keyState)
	{
		PubKey = Guard.NotNull(nameof(pubKey), pubKey);
		FullKeyPath = Guard.NotNull(nameof(fullKeyPath), fullKeyPath);
		_cluster = new Cluster(this);
		Labels = labels;
		Cluster.UpdateLabels();
		KeyState = keyState;

		_p2wpkhScript = new Lazy<Script>(() => PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit), isThreadSafe: true);
		_p2Taproot = new Lazy<Script>(() => PubKey.GetScriptPubKey(ScriptPubKeyType.TaprootBIP86), isThreadSafe: true);

		Index = (int)FullKeyPath.Indexes[4];

		int change = (int)FullKeyPath.Indexes[3];
		if (change == 0)
		{
			IsInternal = false;
		}
		else if (change == 1)
		{
			IsInternal = true;
		}
		else
		{
			throw new ArgumentException(nameof(FullKeyPath));
		}
	}

	public Cluster Cluster
	{
		get => _cluster;
		set => RaiseAndSetIfChanged(ref _cluster, value);
	}

	public HashSet<uint256> OutputAnonSetReasons { get; } = new();

	public double AnonymitySet
	{
		get => _anonymitySet;
		private set => RaiseAndSetIfChanged(ref _anonymitySet, value);
	}

	[JsonProperty(Order = 1)]
	[JsonConverter(typeof(PubKeyJsonConverter))]
	public PubKey PubKey { get; }

	[JsonProperty(Order = 2)]
	[JsonConverter(typeof(KeyPathJsonConverter))]
	public KeyPath FullKeyPath { get; }

	[JsonProperty(Order = 3, PropertyName = "Label")]
	[JsonConverter(typeof(LabelsArrayJsonConverter))]
	public LabelsArray Labels { get; private set; }

	[JsonProperty(Order = 4)]
	public KeyState KeyState { get; private set; }

	/// <summary>Height of the block where all coins associated with the key were spent, or <c>null</c> if not yet spent.</summary>
	/// <remarks>Value can be non-<c>null</c> only for <see cref="IsInternal">internal keys</see> as they should be used just once.</remarks>
	public Height? LatestSpendingHeight { get; set; }
	public Height? FirstReceivingHeight { get; set; }

	public Script P2wpkhScript => _p2wpkhScript.Value;
	public Script P2Taproot => _p2Taproot.Value;

	public int Index { get; }
	public bool IsInternal { get; }

	public void SetAnonymitySet(double anonset, uint256? outputAnonSetReason = null)
	{
		if (outputAnonSetReason is not null)
		{
			OutputAnonSetReasons.Add(outputAnonSetReason);
		}

		AnonymitySet = anonset;
	}

	public void SetLabel(LabelsArray labels, KeyManager? kmToFile = null)
	{
		if (Labels == labels)
		{
			return;
		}

		Labels = labels;
		Cluster.UpdateLabels();

		kmToFile?.ToFile();
	}

	public void SetKeyState(KeyState state, KeyManager? kmToFile = null)
	{
		if (KeyState == state)
		{
			return;
		}

		KeyState = state;

		kmToFile?.ToFile();
	}

	public BitcoinWitPubKeyAddress GetP2wpkhAddress(Network network) => (BitcoinWitPubKeyAddress)PubKey.GetAddress(ScriptPubKeyType.Segwit, network);

	public bool ContainsScript(Script scriptPubKey)
	{
		var scripts = new[]
		{
			P2wpkhScript,
			P2Taproot
		};

		return scripts.Contains(scriptPubKey);
	}

	#region Equality

	public override bool Equals(object? obj) => Equals(obj as HdPubKey);

	public bool Equals(HdPubKey? other) => this == other;

	public override int GetHashCode() => PubKey.GetHashCode();

	public static bool operator ==(HdPubKey? x, HdPubKey? y) => x?.PubKey == y?.PubKey;

	public static bool operator !=(HdPubKey? x, HdPubKey? y) => !(x == y);

	#endregion Equality
}
