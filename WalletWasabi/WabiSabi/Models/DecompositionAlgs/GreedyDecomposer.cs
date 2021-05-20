using NBitcoin;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace WalletWasabi.WabiSabi.Models.DecompositionAlgs
{
	public class GreedyDecomposer
	{
		public GreedyDecomposer(IEnumerable<Money> baseDenominations, Money dustThreshold, FeeRate feeRate)
		{
			BaseDenominations = baseDenominations.OrderBy(d => d).ToImmutableArray();
			DustThreshold = dustThreshold;
			FeeRate = feeRate;
		}

		private ImmutableArray<Money> BaseDenominations { get; }

		private List<Money> Decomposition { get; set; } = new List<Money>();
		private Money DustThreshold { get; }

		private FeeRate FeeRate { get; }

		public void Decompose(Coin coin)
		{
			Money remaining = coin.Amount - FeeRate.GetFee(coin.ScriptPubKey.EstimateOutputVsize());

			while (remaining > DustThreshold)
			{
				if (!TryGetLargestDenomBelowIncl(remaining, out var denom))
				{
					Decomposition.InsertSorted(remaining);
					break;
				}

				Decomposition.InsertSorted(denom);

				var effectiveCost = coin.Amount + FeeRate.GetFee(coin.ScriptPubKey.EstimateOutputVsize());

				remaining -= effectiveCost;
			}
		}

		private bool TryGetLargestDenomBelowIncl(Money amount, [NotNullWhen(true)] out Money? result)
		{
			result = BaseDenominations.LastOrDefault(denoms => denoms <= amount);
			return result is not null;
		}

		public ImmutableArray<Money> GetDecomposition()
		{
			return Decomposition.ToImmutableArray();
		}
	}
}
