using NBitcoin;
using System.Collections.Immutable;
using System.Linq;
using WabiSabi.Crypto.Randomness;
using WalletWasabi.Extensions;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Models;

namespace WalletWasabi.WabiSabi.Client;

public record UtxoSelectionParameters(
	MoneyRange AllowedInputAmounts,
	Money MinAllowedOutputAmount,
	CoordinationFeeRate CoordinationFeeRate,
	FeeRate MiningFeeRate,
	ImmutableSortedSet<ScriptType> AllowedInputScriptTypes)
{
	public static UtxoSelectionParameters FromRoundParameters(RoundParameters roundParameters)
	{
		return new(
			roundParameters.AllowedInputAmounts,
			roundParameters.CalculateMinReasonableOutputAmount(),
			roundParameters.CoordinationFeeRate,
			roundParameters.MiningFeeRate,
			roundParameters.AllowedInputTypes);
	}
}

public static class RoundParametersExtensions
{
	/// <returns>Min: must be larger than the smallest economical denom. Max: max allowed in the round.</returns>
	/// <returns>Min output amount that's economically reasonable to be registered with current network conditions.</returns>
	/// <remarks>It won't be smaller than min allowed output amount.</remarks>
	public static Money CalculateMinReasonableOutputAmount(this RoundParameters roundParameters)
	{
		var scriptTypesSupportedByWallet= new[] { ScriptType.P2WPKH, ScriptType.Taproot }; // I doubt this will change

		var outputTypes = roundParameters.AllowedOutputTypes.Intersect(scriptTypesSupportedByWallet);
		var maxVsizeInputOutputPair = outputTypes.Max(x => x.EstimateInputVsize() + x.EstimateOutputVsize());
		var minEconomicalOutput = roundParameters.MiningFeeRate.GetFee(maxVsizeInputOutputPair);
		return Math.Max(minEconomicalOutput, roundParameters.AllowedOutputAmounts.Min);
	}
}
