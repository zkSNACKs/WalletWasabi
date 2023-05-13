using NBitcoin;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using WalletWasabi.Bases;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Models;
using WalletWasabi.Extensions;

namespace WalletWasabi.Blockchain.TransactionOutputs;

/// <summary>
/// An UTXO that knows more.
/// </summary>
[DebuggerDisplay("{Amount}BTC {Confirmed} {HdPubKey.Label} OutPoint={Coin.Outpoint}")]
public class SmartCoin : NotifyPropertyChangedBase, IEquatable<SmartCoin>, IDestination, ISmartCoin
{
	private Height _height;
	private SmartTransaction? _spenderTransaction;
	private bool _coinJoinInProgress;
	private DateTimeOffset? _bannedUntilUtc;
	private bool _spentAccordingToBackend;

	private ISecret? _secret;

	private bool _confirmed;
	private bool _isBanned;
	private bool _isExcludedFromCoinJoin;

	private Lazy<uint256> _transactionId;
	private Lazy<OutPoint> _outPoint;
	private Lazy<TxOut> _txOut;
	private Lazy<Coin> _coin;
	private Lazy<int> _hashCode;

	public SmartCoin(SmartTransaction transaction, uint outputIndex, HdPubKey pubKey)
	{
		Transaction = transaction;
		Index = outputIndex;
		_transactionId = new Lazy<uint256>(() => Transaction.GetHash(), true);

		_outPoint = new Lazy<OutPoint>(() => new OutPoint(TransactionId, Index), true);
		_txOut = new Lazy<TxOut>(() => Transaction.Transaction.Outputs[Index], true);
		_coin = new Lazy<Coin>(() => new Coin(Outpoint, TxOut), true);

		_hashCode = new Lazy<int>(() => Outpoint.GetHashCode(), true);

		_height = transaction.Height;
		_confirmed = _height.Type == HeightType.Chain;

		HdPubKey = pubKey;

		Transaction.TryAddWalletOutput(this);
	}

	public SmartTransaction Transaction { get; }
	public uint Index { get; }
	public uint256 TransactionId => _transactionId.Value;

	public OutPoint Outpoint => _outPoint.Value;
	public TxOut TxOut => _txOut.Value;
	public Coin Coin => _coin.Value;

	public Script ScriptPubKey => TxOut.ScriptPubKey;
	public ScriptType ScriptType => ScriptPubKey.GetScriptType();
	public Money Amount => TxOut.Value;
	public double AnonymitySet => HdPubKey.AnonymitySet;

	public Height Height
	{
		get => _height;
		set
		{
			if (RaiseAndSetIfChanged(ref _height, value))
			{
				Confirmed = _height.Type == HeightType.Chain;
			}
		}
	}

	public SmartTransaction? SpenderTransaction
	{
		get => _spenderTransaction;
		set => RaiseAndSetIfChanged(ref _spenderTransaction, value);
	}

	public bool CoinJoinInProgress
	{
		get => _coinJoinInProgress;
		set => RaiseAndSetIfChanged(ref _coinJoinInProgress, value);
	}

	public DateTimeOffset? BannedUntilUtc
	{
		get => _bannedUntilUtc;
		set
		{
			if (RaiseAndSetIfChanged(ref _bannedUntilUtc, value))
			{
				RefreshAndGetIsBanned();
			}
		}
	}

	/// <summary>
	/// If the backend thinks it's spent, but Wasabi does not yet know.
	/// </summary>
	public bool SpentAccordingToBackend
	{
		get => _spentAccordingToBackend;
		set => RaiseAndSetIfChanged(ref _spentAccordingToBackend, value);
	}

	public HdPubKey HdPubKey { get; }

	/// <summary>
	/// It's a secret, so it's usually going to be null. Do not use it.
	/// This will not get serialized, because that's a security risk.
	/// </summary>
	public ISecret? Secret
	{
		get => _secret;
		set => RaiseAndSetIfChanged(ref _secret, value);
	}

	public bool Confirmed
	{
		get => _confirmed;
		private set => RaiseAndSetIfChanged(ref _confirmed, value);
	}

	/// <summary>
	/// If you want to have a notification about a coin is released, then you have to periodically read IsBanned.
	/// </summary>
	public bool IsBanned
	{
		get => RefreshAndGetIsBanned();
		private set => RaiseAndSetIfChanged(ref _isBanned, value);
	}

	public bool IsExcludedFromCoinJoin
	{
		get => _isExcludedFromCoinJoin;
		set => RaiseAndSetIfChanged(ref _isExcludedFromCoinJoin, value);
	}

	public bool RegisterToHdPubKey()
		=> HdPubKey.Coins.Add(this);

	public bool UnregisterFromHdPubKey()
		=> HdPubKey.Coins.Remove(this);

	public bool IsImmature(int bestHeight)
	{
		return Transaction.Transaction.IsCoinBase && Height < bestHeight - 100;
	}

	public bool RefreshAndGetIsBanned()
	{
		if (BannedUntilUtc is { } && BannedUntilUtc > DateTimeOffset.UtcNow)
		{
			IsBanned = true;
			return true;
		}

		IsBanned = false;
		BannedUntilUtc = null;

		return false;
	}

	[MemberNotNullWhen(returnValue: true, nameof(SpenderTransaction))]
	public bool IsSpent() => SpenderTransaction is not null;

	/// <summary>
	/// IsUnspent() AND !SpentAccordingToBackend AND !CoinJoinInProgress
	/// </summary>
	public bool IsAvailable() => SpenderTransaction is null && !SpentAccordingToBackend && !CoinJoinInProgress;

	public bool IsReplaceable() => Transaction.IsRBF;

	public override string ToString() => $"{TransactionId.ToString()[..7]}.. - {Index}, {ScriptPubKey.ToString()[..7]}.. - {Amount} BTC";

	#region EqualityAndComparison

	public override bool Equals(object? obj) => Equals(obj as SmartCoin);

	public bool Equals(SmartCoin? other) => this == other;

	public override int GetHashCode() => _hashCode.Value;

	internal uint GetInputIndex()
	{
		if (SpenderTransaction is null)
		{
			throw new InvalidOperationException("This input is unspent.");
		}

		var inputs = SpenderTransaction.Transaction.Inputs;
		for (uint i = 0; i < inputs.Count; i++)
		{
			var currentInput = inputs[i];
			if (currentInput.PrevOut == Outpoint)
			{
				return i;
			}
		}

		throw new NotSupportedException("This is impossible!");
	}

	public static bool operator ==(SmartCoin? x, SmartCoin? y)
	{
		if (ReferenceEquals(x, y))
		{
			return true;
		}
		else if (x is null || y is null)
		{
			return false;
		}
		else
		{
			var hashEquals = x.GetHashCode() == y.GetHashCode();
			return hashEquals && y.TransactionId == x.TransactionId && y.Index == x.Index;
		}
	}

	public static bool operator !=(SmartCoin? x, SmartCoin? y) => !(x == y);

	#endregion EqualityAndComparison
}
