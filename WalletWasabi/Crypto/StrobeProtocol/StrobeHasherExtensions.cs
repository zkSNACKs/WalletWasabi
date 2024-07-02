using System.Collections.Immutable;
using System.Linq;
using NBitcoin;
using WabiSabi.Crypto.StrobeProtocol;
using WalletWasabi.WabiSabi.Models;

namespace WalletWasabi.Crypto.StrobeProtocol;

public static class StrobeHasherExtensions
{
	public static StrobeHasher Append(this StrobeHasher hasher, string fieldName, IBitcoinSerializable serializable)
		=> hasher.Append(fieldName, serializable.ToBytes());

	public static StrobeHasher Append(this StrobeHasher hasher, string fieldName, Money money)
		=> hasher.Append(fieldName, money.Satoshi);

	public static StrobeHasher Append(this StrobeHasher hasher, string fieldName, MoneyRange range)
		=> hasher.Append(fieldName + "-min", range.Min).Append(fieldName + "-max", range.Max);

	public static StrobeHasher Append(this StrobeHasher me, string fieldName, ImmutableSortedSet<ScriptType> scriptTypes)
	{
		return scriptTypes
			.Select((scriptType, idx) => (scriptType, idx))
			.Aggregate(me, (hasher, scriptTypeIdxPair) => hasher.Append(fieldName + "-" + scriptTypeIdxPair.idx, Enum.GetName(scriptTypeIdxPair.scriptType)));
	}

	public static StrobeHasher Append(this StrobeHasher hasher, string fieldName, decimal value)
		=> hasher.Append(fieldName, (long)(value * Money.COIN));

	public static StrobeHasher Append(this StrobeHasher hasher, string fieldName, CoordinationFeeRate coordinationFeeRate)
		=> hasher
			.Append($"{fieldName}.Rate", coordinationFeeRate.Rate)
			.Append($"{fieldName}.PlebsDontPayThreshold", Money.Zero);
}
