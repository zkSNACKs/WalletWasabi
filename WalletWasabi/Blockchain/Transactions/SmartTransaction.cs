using NBitcoin;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.JsonConverters;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Models;

namespace WalletWasabi.Blockchain.Transactions;

[JsonObject(MemberSerialization.OptIn)]
public class SmartTransaction : IEquatable<SmartTransaction>
{
	#region Constructors

	public SmartTransaction(Transaction transaction, Height height, uint256? blockHash = null, int blockIndex = 0, SmartLabel? label = null, bool isReplacement = false, DateTimeOffset firstSeen = default)
	{
		Transaction = transaction;

		// Because we don't modify those transactions, we can cache the hash
		Transaction.PrecomputeHash(false, true);

		Label = label ?? SmartLabel.Empty;

		Height = height;
		BlockHash = blockHash;
		BlockIndex = blockIndex;

		FirstSeen = firstSeen == default ? DateTimeOffset.UtcNow : firstSeen;

		IsReplacement = isReplacement;
		_walletInputs = new HashSet<SmartCoin>(Transaction.Inputs.Count);
		_walletOutputs = new HashSet<SmartCoin>(Transaction.Outputs.Count);
	}

	#endregion Constructors

	#region Members

	public IReadOnlyCollection<SmartCoin> WalletInputs => _walletInputs;

	public void AddWalletInput(SmartCoin input)
	{
		// When a wallet input is added, foreign inputs and wallet virtual inputs has to be recalculated.
		// Instead of doing it immediately, we mark them invalid and recalculate them only when they are needed.
		_walletInputs.Add(input);
		_foreignInputs = null;
		_walletVirtualInputs = null;
	}

	public IReadOnlyCollection<SmartCoin> WalletOutputs => _walletOutputs;

	public void AddWalletOutput(SmartCoin output)
	{
		// When a wallet output is added, foreign outputs, wallet virtual outputs and foreign virtual outputs has to be recalculated.
		// Instead of doing it immediately, we mark them invalid and recalculate them only when they are needed.
		_walletOutputs.Add(output);
		_foreignOutputs = null;
		_walletVirtualOutputs = null;
		_foreignVirtualOutputs = null;
	}

	public void RemoveWalletOutput(SmartCoin output)
	{
		// When a wallet output is removed, foreign outputs, wallet virtual outputs and foreign virtual outputs has to be recalculated.
		// Instead of doing it immediately, we mark them invalid and recalculate them only when they are needed.
		_walletOutputs.Remove(output);
		_foreignOutputs = null;
		_walletVirtualOutputs = null;
		_foreignVirtualOutputs = null;
	}

	[JsonProperty]
	[JsonConverter(typeof(TransactionJsonConverter))]
	public Transaction Transaction { get; }

	[JsonProperty]
	[JsonConverter(typeof(HeightJsonConverter))]
	public Height Height { get; private set; }

	[JsonProperty]
	[JsonConverter(typeof(Uint256JsonConverter))]
	public uint256? BlockHash { get; private set; }

	[JsonProperty]
	public int BlockIndex { get; private set; }

	[JsonProperty]
	[JsonConverter(typeof(SmartLabelJsonConverter))]
	public SmartLabel Label { get; set; }

	[JsonProperty]
	[JsonConverter(typeof(DateTimeOffsetUnixSecondsConverter))]
	public DateTimeOffset FirstSeen { get; private set; }

	[JsonProperty(PropertyName = "FirstSeenIfMempoolTime")]
	[JsonConverter(typeof(BlockCypherDateTimeOffsetJsonConverter))]
	[Obsolete("This property exists only for json backwards compatibility. If someone tries to set it, it'll set the FirstSeen. https://stackoverflow.com/a/43715009/2061103", error: true)]
	[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "json backwards compatibility")]
	private DateTimeOffset? FirstSeenCompatibility
	{
		set
		{
			// If it's null, let FirstSeen's default to be set.
			// If it's not null, then check if FirstSeen has just been recently set to utcnow which is its default.
			if (value.HasValue && DateTimeOffset.UtcNow - FirstSeen < TimeSpan.FromSeconds(1))
			{
				FirstSeen = value.Value;
			}
		}
	}

	[JsonProperty]
	public bool IsReplacement { get; private set; }

	public bool Confirmed => Height.Type == HeightType.Chain;

	public uint256 GetHash() => Transaction.GetHash();

	public int GetConfirmationCount(Height bestHeight) => Height == Height.Mempool ? 0 : bestHeight.Value - Height.Value + 1;

	/// <summary>
	/// A transaction can signal that is replaceable by fee in two ways:
	/// * Explicitly by using a nSequence &lt; (0xffffffff - 1) or,
	/// * Implicitly in case one of its unconfirmed ancestors are replaceable
	/// </summary>
	public bool IsRBF => !Confirmed && (Transaction.RBF || IsReplacement || WalletInputs.Any(x => x.IsReplaceable()));

	public IReadOnlyCollection<IndexedTxIn> ForeignInputs => _foreignInputs ??= GetForeignInputs();

	public IReadOnlyCollection<IndexedTxOut> ForeignOutputs => _foreignOutputs ??= GetForeignOutputs();

	public IReadOnlyCollection<WalletVirtualInput> WalletVirtualInputs => _walletVirtualInputs ??= GetWalletVirtualInputs();

	public IReadOnlyCollection<WalletVirtualOutput> WalletVirtualOutputs => _walletVirtualOutputs ??= GetWalletVirtualOutputs();

	public IReadOnlyCollection<ForeignVirtualOutput> ForeignVirtualOutputs => _foreignVirtualOutputs ??= GetForeignVirtualOutputs();

	/// <summary>
	/// Coins that are on the input side of the tx and belong to ANY loaded wallet. Later if more wallets are loaded this list can increase.
	/// </summary>
	private HashSet<SmartCoin> _walletInputs;

	/// <summary>
	/// Coins that are on the output side of the tx and belong to ANY loaded wallet. Later if more wallets are loaded this list can increase.
	/// </summary>
	private HashSet<SmartCoin> _walletOutputs;

	private HashSet<IndexedTxIn>? _foreignInputs = null;
	private HashSet<IndexedTxOut>? _foreignOutputs = null;

	private HashSet<WalletVirtualInput>? _walletVirtualInputs = null;
	private HashSet<WalletVirtualOutput>? _walletVirtualOutputs = null;
	private HashSet<ForeignVirtualOutput>? _foreignVirtualOutputs = null;

	#endregion Members

	public HashSet<IndexedTxIn> GetForeignInputs()
	{
		var walletInputOutpoints = WalletInputs.Select(smartCoin => smartCoin.OutPoint).ToHashSet();
		return Transaction.Inputs.AsIndexedInputs().Where(i => !walletInputOutpoints.Contains(i.PrevOut)).ToHashSet();
	}

	public HashSet<IndexedTxOut> GetForeignOutputs()
	{
		var walletOutputIndices = WalletOutputs.Select(smartCoin => smartCoin.OutPoint.N).ToHashSet();
		return Transaction.Outputs.AsIndexedOutputs().Where(o => !walletOutputIndices.Contains(o.N)).ToHashSet();
	}

	private static byte[] ExtractKeyId(Script scriptPubKey)
		=> scriptPubKey.TryGetScriptType() switch
		{
			ScriptType.P2WPKH => PayToWitPubKeyHashTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey).ToBytes(),
			ScriptType.P2PKH => PayToPubkeyHashTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey).ToBytes(),
			ScriptType.P2PK => PayToPubkeyTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey).ToBytes(),
			_ => scriptPubKey.ToBytes()
		};

	private static byte[] ExtractKeyId(HdPubKey hdPubKey) => hdPubKey.PubKey.ToBytes();

	public HashSet<WalletVirtualInput> GetWalletVirtualInputs()
	{
		Func<IGrouping<byte[], SmartCoin>, WalletVirtualInput> groupToVirtualInput = g => new WalletVirtualInput(g.First().HdPubKey, g.ToHashSet());
		return WalletInputs.GroupBy(i => ExtractKeyId(i.HdPubKey), new ByteArrayEqualityComparer()).Select(groupToVirtualInput).ToHashSet();
	}

	public HashSet<WalletVirtualOutput> GetWalletVirtualOutputs()
	{
		var transactionHash = GetHash();
		Func<IGrouping<byte[], SmartCoin>, WalletVirtualOutput> groupToVirtualOutput = g => new WalletVirtualOutput(g.Key, g.Sum(o => o.Amount), g.Select(o => new OutPoint(transactionHash, o.Index)).ToHashSet());
		return WalletOutputs.GroupBy(o => ExtractKeyId(o.HdPubKey), new ByteArrayEqualityComparer()).Select(groupToVirtualOutput).ToHashSet();
	}

	public HashSet<ForeignVirtualOutput> GetForeignVirtualOutputs()
	{
		var transactionHash = GetHash();
		Func<IGrouping<byte[], IndexedTxOut>, ForeignVirtualOutput> groupToVirtualOutput = g => new ForeignVirtualOutput(g.Key, g.Sum(o => o.TxOut.Value), g.Select(o => new OutPoint(transactionHash, o.N)).ToHashSet());
		return ForeignOutputs.GroupBy(o => ExtractKeyId(o.TxOut.ScriptPubKey), new ByteArrayEqualityComparer()).Select(groupToVirtualOutput).ToHashSet();
	}

	/// <summary>
	/// Update the transaction with the data acquired from another transaction. (For example merge their labels.)
	/// </summary>
	public bool TryUpdate(SmartTransaction tx)
	{
		var updated = false;

		// If this is not the same tx, then don't update.
		if (this != tx)
		{
			throw new InvalidOperationException($"{GetHash()} != {tx.GetHash()}");
		}

		// Set the height related properties, only if confirmed.
		if (tx.Confirmed)
		{
			if (Height != tx.Height)
			{
				Height = tx.Height;
				updated = true;
			}

			if (tx.BlockHash is { } && BlockHash != tx.BlockHash)
			{
				BlockHash = tx.BlockHash;
				BlockIndex = tx.BlockIndex;
				updated = true;
			}
		}

		// Always the earlier seen is the firstSeen.
		if (tx.FirstSeen < FirstSeen)
		{
			FirstSeen = tx.FirstSeen;
			updated = true;
		}

		// Merge labels.
		if (Label != tx.Label)
		{
			Label = SmartLabel.Merge(Label, tx.Label);
			updated = true;
		}

		return updated;
	}

	public void SetReplacement()
	{
		IsReplacement = true;
	}

	/// <summary>
	/// First looks at height, then block index, then mempool firstseen.
	/// </summary>
	public static IComparer<SmartTransaction> GetBlockchainComparer()
	{
		return Comparer<SmartTransaction>.Create((a, b) =>
		{
			var heightCompareResult = a.Height.CompareTo(b.Height);
			if (heightCompareResult != 0)
			{
				return heightCompareResult;
			}

			// If mempool this should be 0, so they should be equal so no worry about it.
			var blockIndexCompareResult = a.BlockIndex.CompareTo(b.BlockIndex);
			if (blockIndexCompareResult != 0)
			{
				return blockIndexCompareResult;
			}

			var firstSeenCompareResult = a.FirstSeen.CompareTo(b.FirstSeen);
			return firstSeenCompareResult;
		});
	}

	public void SetUnconfirmed()
	{
		Height = Height.Mempool;
		BlockHash = null;
		BlockIndex = 0;
	}

	public bool IsOwnCoinjoin()
	   => WalletInputs.Any() // We must be a participant in order for this transaction to be our coinjoin.
	   && Transaction.Inputs.Count != WalletInputs.Count; // Some inputs must not be ours for it to be a coinjoin.

	#region LineSerialization

	public string ToLine()
	{
		// GetHash is also serialized, so file can be interpreted with our eyes better.

		return string.Join(
			':',
			GetHash(),
			Transaction.ToHex(),
			Height,
			BlockHash,
			BlockIndex,
			Label,
			FirstSeen.ToUnixTimeSeconds(),
			IsReplacement);
	}

	public static SmartTransaction FromLine(string line, Network expectedNetwork)
	{
		var parts = line.Split(':', StringSplitOptions.None).Select(x => x.Trim()).ToArray();

		var transactionString = parts[1];
		Transaction transaction = Transaction.Parse(transactionString, expectedNetwork);

		try
		{
			// First is redundant txhash serialization.
			var heightString = parts[2];
			var blockHashString = parts[3];
			var blockIndexString = parts[4];
			var labelString = parts[5];
			var firstSeenString = parts[6];
			var isReplacementString = parts[7];

			if (!Height.TryParse(heightString, out Height height))
			{
				height = Height.Unknown;
			}
			if (!uint256.TryParse(blockHashString, out var blockHash))
			{
				blockHash = null;
			}
			if (!int.TryParse(blockIndexString, out int blockIndex))
			{
				blockIndex = 0;
			}
			var label = new SmartLabel(labelString);
			DateTimeOffset firstSeen = default;
			if (long.TryParse(firstSeenString, out long unixSeconds))
			{
				firstSeen = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
			}
			if (!bool.TryParse(isReplacementString, out bool isReplacement))
			{
				isReplacement = false;
			}

			return new SmartTransaction(transaction, height, blockHash, blockIndex, label, isReplacement, firstSeen);
		}
		catch (Exception ex)
		{
			Logger.LogDebug(ex);
			return new SmartTransaction(transaction, Height.Unknown);
		}
	}

	#endregion LineSerialization

	#region EqualityAndComparison

	public override bool Equals(object? obj) => Equals(obj as SmartTransaction);

	public bool Equals(SmartTransaction? other) => this == other;

	public override int GetHashCode() => GetHash().GetHashCode();

	public static bool operator ==(SmartTransaction? x, SmartTransaction? y) => y?.GetHash() == x?.GetHash();

	public static bool operator !=(SmartTransaction? x, SmartTransaction? y) => !(x == y);

	#endregion EqualityAndComparison
}
