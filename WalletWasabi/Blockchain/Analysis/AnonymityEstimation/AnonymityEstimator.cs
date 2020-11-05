using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Blockchain.Transactions;

namespace WalletWasabi.Blockchain.Analysis.AnonymityEstimation
{
	public class AnonymityEstimator
	{
		public AnonymityEstimator(CoinsRegistry allWalletCoins, KeyManager keyManager, Money dustThreshold)
		{
			AllWalletCoins = allWalletCoins;
			KeyManager = keyManager;
			DustThreshold = dustThreshold;
		}

		public CoinsRegistry AllWalletCoins { get; }
		public KeyManager KeyManager { get; }
		public Money DustThreshold { get; }

		/// <param name="updateOtherCoins">Only estimate -> does not touch other coins' anonsets.</param>
		/// <returns>Dictionary of own output indexes and their calculated anonymity sets.</returns>
		public IDictionary<uint, double> EstimateAnonymitySets(Transaction tx, bool updateOtherCoins = false)
		{
			var ownOutputs = new List<uint>();
			for (var i = 0U; i < tx.Outputs.Count; i++)
			{
				// If transaction received to any of the wallet keys:
				var output = tx.Outputs[i];
				HdPubKey foundKey = KeyManager.GetKeyForScriptPubKey(output.ScriptPubKey);
				if (foundKey is { })
				{
					ownOutputs.Add(i);
				}
			}

			return EstimateAnonymitySets(tx, ownOutputs, updateOtherCoins);
		}

		/// <param name="updateOtherCoins">Only estimate -> does not touch other coins' anonsets.</param>
		/// <returns>Dictionary of own output indexes and their calculated anonymity sets.</returns>
		public IDictionary<uint, double> EstimateAnonymitySets(Transaction tx, IEnumerable<uint> ownOutputIndices, bool updateOtherCoins = false)
		{
			// Estimation of anonymity sets only makes sense for own outputs.
			var ownOutputCount = ownOutputIndices.Count();
			if (ownOutputCount == 0)
			{
				return new Dictionary<uint, double>();
			}

			var allWalletCoinsView = AllWalletCoins.AsAllCoinsView();
			var spentOwnCoins = allWalletCoinsView.OutPoints(tx.Inputs.Select(x => x.PrevOut)).ToList();
			var ownInputCount = spentOwnCoins.Count;

			var inputCount = tx.Inputs.Count;
			var outputCount = tx.Outputs.Count;

			// In normal payments we expose things to our counterparties.
			// If it's a normal tx (that isn't self spent, nor a coinjoin,) then anonymity should be stripped.
			// All the inputs must be ours AND there must be at least one output that isn't ours.
			// Note: this is only a good idea from WWII, with WWI we calculate anonsets from the point the coin first hit the wallet.
			if (ownInputCount == inputCount && outputCount > ownOutputCount)
			{
				var changeAnonset = 1;

				// If we have one output and all the outputs in the tx have all the decimal places, then our change can gain anonymity.
				if (tx.Outputs.All(x => x.Value.Satoshi % 10 != 0))
				{
					changeAnonset = outputCount - ownOutputCount;
				}
				var ret = new Dictionary<uint, double>();
				foreach (var outputIndex in ownOutputIndices)
				{
					ret.Add(outputIndex, changeAnonset);
				}
				return ret;
			}

			// 1 in, 1 out self spend tx is at least deniable.
			if (ownInputCount == inputCount && ownOutputCount == outputCount && inputCount == 1 && outputCount == 1)
			{
				return new Dictionary<uint, double>
				{
					{ 0, spentOwnCoins.First().AnonymitySet + 0.2 }
				};
			}

			var anonsets = new Dictionary<uint, double>();
			foreach (var outputIndex in ownOutputIndices)
			{
				// Get the anonymity set of i-th output in the transaction.
				var anonset = tx.GetAnonymitySet(outputIndex);

				// Let's assume the blockchain analyser also participates in the transaction.
				anonset = Math.Max(1, anonset - 1);

				// If we provided inputs to the transaction.
				if (ownInputCount > 0)
				{
					var privacyBonus = Intersect(spentOwnCoins.Select(x => x.AnonymitySet), (double)ownInputCount / inputCount);

					// If the privacy bonus is <=1 then we are not inheriting any privacy from the inputs.
					var normalizedBonus = privacyBonus - 1;

					// And add that to the base anonset from the tx.
					anonset += normalizedBonus;
				}

				// Factor in script reuse.
				var output = tx.Outputs[outputIndex];
				var reusedCoins = allWalletCoinsView.FilterBy(x => x.ScriptPubKey == output.ScriptPubKey).ToList();
				anonset = Intersect(reusedCoins.Select(x => x.AnonymitySet).Append(anonset), 1);

				// Dust attack could ruin the anonset of our existing mixed coins, so it's better not to do that.
				if (updateOtherCoins && output.Value > DustThreshold)
				{
					foreach (var coin in reusedCoins)
					{
						UpdateAnonset(coin, anonset);
					}
				}

				anonsets.Add(outputIndex, anonset);
			}
			return anonsets;
		}

		private void UpdateAnonset(SmartCoin coin, double anonset)
		{
			if (coin.AnonymitySet == anonset)
			{
				return;
			}

			coin.AnonymitySet = anonset;

			var childTx = coin.SpenderTransaction;
			if (childTx is { })
			{
				var anonymitySets = EstimateAnonymitySets(childTx.Transaction, updateOtherCoins: false);
				for (uint i = 0; i < childTx.Transaction.Outputs.Count; i++)
				{
					var allWalletCoinsView = AllWalletCoins.AsAllCoinsView();
					var childCoin = allWalletCoinsView.GetByOutPoint(new OutPoint(childTx.GetHash(), i));
					if (childCoin is { })
					{
						UpdateAnonset(childCoin, anonymitySets[i]);
					}
				}
			}
		}

		/// <param name="coefficient">If larger than 1, then penalty is larger, if smaller than 1 then penalty is smaller.</param>
		private double Intersect(IEnumerable<double> anonsets, double coefficient)
		{
			// Sanity check.
			if (!anonsets.Any())
			{
				return 1;
			}

			// Our smallest anonset is the relevant here, because anonsets cannot grow by intersection punishments.
			var smallestAnon = anonsets.Min();
			// Punish intersection exponentially.
			// If there is only a single anonset then the exponent should be zero to divide by 1 thus retain the input coin anonset.
			var intersectPenalty = Math.Pow(2, anonsets.Count() - 1);
			var intersectionAnonset = smallestAnon / Math.Max(1, intersectPenalty * coefficient);

			// Sanity check.
			var normalizedIntersectionAnonset = Math.Max(1d, intersectionAnonset);
			return normalizedIntersectionAnonset;
		}
	}
}
