using NBitcoin;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using WabiSabi.Crypto.Randomness;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Extensions;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Wallets;
using SecureRandom = WabiSabi.Crypto.Randomness.SecureRandom;

namespace WalletWasabi.WabiSabi.Client.CoinJoin.Client;

public class CoinJoinCoinSelector
{
	public const int MaxInputsRegistrableByWallet = 10; // how many
	public const int MaxWeightedAnonLoss = 3; // Maximum tolerable WeightedAnonLoss.

	/// <param name="consolidationMode">If true it attempts to select as many coins as it can.</param>
	/// <param name="anonScoreTarget">Tries to select few coins over this threshold.</param>
	/// <param name="semiPrivateThreshold">Minimum anonymity of coins that can be selected together.</param>
	public CoinJoinCoinSelector(
		bool consolidationMode,
		int anonScoreTarget,
		int semiPrivateThreshold,
		CoinJoinCoinSelectorRandomnessGenerator? generator = null)
	{
		ConsolidationMode = consolidationMode;
		AnonScoreTarget = anonScoreTarget;
		SemiPrivateThreshold = semiPrivateThreshold;

		Generator = generator ?? new(MaxInputsRegistrableByWallet, SecureRandom.Instance);
	}

	public bool ConsolidationMode { get; }
	public int AnonScoreTarget { get; }
	public int SemiPrivateThreshold { get; }
	private WasabiRandom Rnd => Generator.Rnd;
	private CoinJoinCoinSelectorRandomnessGenerator Generator { get; }

	public static CoinJoinCoinSelector FromWallet(IWallet wallet) =>
		new(
			wallet.ConsolidationMode,
			wallet.AnonScoreTarget,
			wallet.RedCoinIsolation ? Constants.SemiPrivateThreshold : 0);

	/// <param name="liquidityClue">Weakly prefer not to select inputs over this.</param>
	public ImmutableList<TCoin> SelectCoinsForRound<TCoin>(IEnumerable<TCoin> coins, UtxoSelectionParameters parameters, Money liquidityClue)
		where TCoin : class, ISmartCoin, IEquatable<TCoin>
	{
		liquidityClue = liquidityClue > Money.Zero
			? liquidityClue
			: Constants.MaximumNumberOfBitcoinsMoney;

		var filteredCoins = coins
			.Where(x => parameters.AllowedInputAmounts.Contains(x.Amount))
			.Where(x => parameters.AllowedInputScriptTypes.Contains(x.ScriptType))
			.Where(x => x.EffectiveValue(parameters.MiningFeeRate) > Money.Zero)
			.ToArray();

		// Sanity check.
		if (filteredCoins.Length == 0)
		{
			Logger.LogDebug("No suitable coins for this round.");
			return ImmutableList<TCoin>.Empty;
		}

		var privateCoins = filteredCoins
			.Where(x => x.IsPrivate(AnonScoreTarget))
			.ToArray();
		var semiPrivateCoins = filteredCoins
			.Where(x => x.IsSemiPrivate(AnonScoreTarget, SemiPrivateThreshold))
			.ToArray();

		// redCoins will only fill up if redCoinIsolation is turned on. Otherwise the coin will be in semiPrivateCoins.
		var redCoins = filteredCoins
			.Where(x => x.IsRedCoin(SemiPrivateThreshold))
			.ToArray();

		if (semiPrivateCoins.Length + redCoins.Length == 0)
		{
			Logger.LogDebug("No suitable coins for this round.");
			return ImmutableList<TCoin>.Empty;
		}

		Logger.LogDebug($"Coin selection started:");
		Logger.LogDebug($"{nameof(filteredCoins)}: {filteredCoins.Length} coins, valued at BTC {Money.Satoshis(filteredCoins.Sum(x => x.Amount)).ToString(false, true)}.");
		Logger.LogDebug($"{nameof(privateCoins)}: {privateCoins.Length} coins, valued at BTC {Money.Satoshis(privateCoins.Sum(x => x.Amount)).ToString(false, true)}.");
		Logger.LogDebug($"{nameof(semiPrivateCoins)}: {semiPrivateCoins.Length} coins, valued at BTC {Money.Satoshis(semiPrivateCoins.Sum(x => x.Amount)).ToString(false, true)}.");
		Logger.LogDebug($"{nameof(redCoins)}: {redCoins.Length} coins, valued at BTC {Money.Satoshis(redCoins.Sum(x => x.Amount)).ToString(false, true)}.");

		// We want to isolate red coins from each other. We only let a single red coin get into our selection candidates.
		var allowedNonPrivateCoins = semiPrivateCoins.ToList();
		var red = redCoins.RandomElement(Rnd);
		if (red is not null)
		{
			allowedNonPrivateCoins.Add(red);
			Logger.LogDebug($"One red coin got selected: BTC {red.Amount.ToString(false, true)}. Isolating the rest.");
		}

		Logger.LogDebug($"{nameof(allowedNonPrivateCoins)}: {allowedNonPrivateCoins.Count} coins, valued at BTC {Money.Satoshis(allowedNonPrivateCoins.Sum(x => x.Amount)).ToString(false, true)}.");

		int inputCount = Math.Min(
			privateCoins.Length + allowedNonPrivateCoins.Count,
			ConsolidationMode ? MaxInputsRegistrableByWallet : Generator.GetInputTarget());
		if (ConsolidationMode)
		{
			Logger.LogDebug($"Consolidation mode is on.");
		}
		Logger.LogDebug($"Targeted {nameof(inputCount)}: {inputCount}.");

		var biasShuffledPrivateCoins = AnonScoreTxSourceBiasedShuffle(privateCoins).ToArray();

		// Deprioritize private coins those are too large.
		var smallerPrivateCoins = biasShuffledPrivateCoins.Where(x => x.Amount <= liquidityClue);
		var largerPrivateCoins = biasShuffledPrivateCoins.Where(x => x.Amount > liquidityClue);

		// Let's allow only inputCount - 1 private coins to play.
		var allowedPrivateCoins = smallerPrivateCoins.Concat(largerPrivateCoins).Take(inputCount - 1).ToArray();
		Logger.LogDebug($"{nameof(allowedPrivateCoins)}: {allowedPrivateCoins.Length} coins, valued at BTC {Money.Satoshis(allowedPrivateCoins.Sum(x => x.Amount)).ToString(false, true)}.");

		var allowedCoins = allowedNonPrivateCoins.Concat(allowedPrivateCoins).ToArray();
		Logger.LogDebug($"{nameof(allowedCoins)}: {allowedCoins.Length} coins, valued at BTC {Money.Satoshis(allowedCoins.Sum(x => x.Amount)).ToString(false, true)}.");

		// Shuffle coins, while randomly biasing towards lower AS.
		var orderedAllowedCoins = AnonScoreTxSourceBiasedShuffle(allowedCoins).ToArray();

		// Always use the largest amounts, so we do not participate with insignificant amounts and fragment wallet needlessly.
		var largestNonPrivateCoins = allowedNonPrivateCoins
			.OrderByDescending(x => x.Amount)
			.Take(3)
			.ToArray();
		Logger.LogDebug($"Largest non-private coins: BTC {string.Join(", ", largestNonPrivateCoins.Select(x => x.Amount.ToString(false, true)).ToArray())}.");

		// Select a group of coins those are close to each other by anonymity score.
		Dictionary<int, IEnumerable<TCoin>> groups = new();

		// Create a bunch of combinations.
		var sw1 = Stopwatch.StartNew();
		foreach (var coin in largestNonPrivateCoins)
		{
			// Create a base combination just in case.
			var baseGroup = orderedAllowedCoins.Except(new[] { coin }).Take(inputCount - 1).Concat(new[] { coin });
			TryAddGroup(parameters, groups, baseGroup);

			var sw2 = Stopwatch.StartNew();
			foreach (var group in orderedAllowedCoins
				.Except(new[] { coin })
				.CombinationsWithoutRepetition(inputCount - 1)
				.Select(x => x.Concat(new[] { coin })))
			{
				TryAddGroup(parameters, groups, group);

				if (sw2.Elapsed > TimeSpan.FromSeconds(1))
				{
					break;
				}
			}

			sw2.Reset();

			if (sw1.Elapsed > TimeSpan.FromSeconds(10))
			{
				break;
			}
		}

		if (groups.Count == 0)
		{
			Logger.LogDebug($"Couldn't create any combinations, ending.");
			return ImmutableList<TCoin>.Empty;
		}
		Logger.LogDebug($"Created {groups.Count} combinations within {(int)sw1.Elapsed.TotalSeconds} seconds.");

		// Select the group where the less coins coming from the same tx.
		var bestRep = groups.Values.Select(x => GetReps(x)).Min(x => x);
		var bestRepGroups = groups.Values.Where(x => GetReps(x) == bestRep);
		Logger.LogDebug($"{nameof(bestRep)}: {bestRep}.");
		Logger.LogDebug($"Filtered combinations down to {nameof(bestRepGroups)}: {bestRepGroups.Count()}.");

		var remainingLargestNonPrivateCoins = largestNonPrivateCoins.Where(x => bestRepGroups.Any(y => y.Contains(x)));
		Logger.LogDebug($"Remaining largest non-private coins: BTC {string.Join(", ", remainingLargestNonPrivateCoins.Select(x => x.Amount.ToString(false, true)).ToArray())}.");

		// Bias selection towards larger numbers.
		var selectedNonPrivateCoin = remainingLargestNonPrivateCoins.RandomElement(Rnd); // Select randomly at first just to have a starting value.
		foreach (var coin in remainingLargestNonPrivateCoins.OrderByDescending(x => x.Amount))
		{
			if (Rnd.GetInt(1, 101) <= 50)
			{
				selectedNonPrivateCoin = coin;
				break;
			}
		}
		if (selectedNonPrivateCoin is null)
		{
			Logger.LogDebug($"Couldn't select largest non-private coin, ending.");
			return ImmutableList<TCoin>.Empty;
		}
		Logger.LogDebug($"Randomly selected large non-private coin: {selectedNonPrivateCoin.Amount.ToString(false, true)}.");

		var finalCandidate = bestRepGroups
			.Where(x => x.Contains(selectedNonPrivateCoin))
			.RandomElement(Rnd);
		if (finalCandidate is null)
		{
			Logger.LogDebug($"Couldn't select final selection candidate, ending.");
			return ImmutableList<TCoin>.Empty;
		}
		Logger.LogDebug($"Selected the final selection candidate: {finalCandidate.Count()} coins, BTC {string.Join(", ", finalCandidate.Select(x => x.Amount.ToString(false, true)).ToArray())}.");

		// Let's remove some coins coming from the same tx in the final candidate:
		// The smaller our balance is the more privacy we gain and the more the user cares about the costs, so more interconnectedness allowance makes sense.
		var toRegister = finalCandidate.Sum(x => x.Amount);
		int percent;
		if (toRegister < 10_000)
		{
			percent = 20;
		}
		else if (toRegister < 100_000)
		{
			percent = 30;
		}
		else if (toRegister < 1_000_000)
		{
			percent = 40;
		}
		else if (toRegister < 10_000_000)
		{
			percent = 50;
		}
		else if (toRegister < 100_000_000) // BTC 1
		{
			percent = 60;
		}
		else if (toRegister < 1_000_000_000)
		{
			percent = 70;
		}
		else
		{
			percent = 80;
		}

		int sameTxAllowance = Generator.GetRandomBiasedSameTxAllowance(percent);

		List<TCoin> winner = new()
		{
			selectedNonPrivateCoin
		};

		foreach (var coin in finalCandidate
			.Except(new[] { selectedNonPrivateCoin })
			.OrderBy(x => x.AnonymitySet)
			.ThenByDescending(x => x.Amount))
		{
			// If the coin is coming from same tx, then check our allowance.
			if (winner.Any(x => x.TransactionId == coin.TransactionId))
			{
				var sameTxUsed = winner.Count - winner.Select(x => x.TransactionId).Distinct().Count();
				if (sameTxUsed < sameTxAllowance)
				{
					winner.Add(coin);
				}
			}
			else
			{
				winner.Add(coin);
			}
		}

		double winnerAnonLoss = GetAnonLoss(winner);

		// Only stay in the while if we are above the liquidityClue (we are a whale) AND the weightedAnonLoss is not tolerable.
		while (winner.Sum(x => x.Amount) > liquidityClue && winnerAnonLoss > MaxWeightedAnonLoss)
		{
			List<TCoin> bestReducedWinner = winner;
			var bestAnonLoss = winnerAnonLoss;
			bool winnerChanged = false;

			// We always want to keep the non-private coins.
			foreach (TCoin coin in winner.Except(new[] { selectedNonPrivateCoin }))
			{
				var reducedWinner = winner.Except(new[] { coin });
				var anonLoss = GetAnonLoss(reducedWinner);

				if (anonLoss <= bestAnonLoss)
				{
					bestAnonLoss = anonLoss;
					bestReducedWinner = reducedWinner.ToList();
					winnerChanged = true;
				}
			}

			if (!winnerChanged)
			{
				break;
			}

			winner = bestReducedWinner;
			winnerAnonLoss = bestAnonLoss;
		}

		if (winner.Count != finalCandidate.Count())
		{
			Logger.LogDebug($"Optimizing selection, removing coins coming from the same tx.");
			Logger.LogDebug($"{nameof(sameTxAllowance)}: {sameTxAllowance}.");
			Logger.LogDebug($"{nameof(winner)}: {winner.Count} coins, BTC {string.Join(", ", winner.Select(x => x.Amount.ToString(false, true)).ToArray())}.");
		}

		if (winner.Count < MaxInputsRegistrableByWallet)
		{
			// If the address of a winner contains other coins (address reuse, same HdPubKey) that are available but not selected,
			// complete the selection with them until MaxInputsRegistrableByWallet threshold.
			// Order by most to least reused to try not splitting coins from same address into several rounds.
			var nonSelectedCoinsOnSameAddresses = filteredCoins
				.Except(winner)
				.Where(x => winner.Any(y => y.ScriptPubKey == x.ScriptPubKey))
				.GroupBy(x => x.ScriptPubKey)
				.OrderByDescending(g => g.Count())
				.SelectMany(g => g)
				.Take(MaxInputsRegistrableByWallet - winner.Count)
				.ToList();

			winner.AddRange(nonSelectedCoinsOnSameAddresses);

			if (nonSelectedCoinsOnSameAddresses.Count > 0)
			{
				Logger.LogInfo($"{nonSelectedCoinsOnSameAddresses.Count} coins were added to the selection because they are on the same addresses of some selected coins.");
			}
		}

		return winner.ToShuffled(Rnd).ToImmutableList();
	}

	private IEnumerable<TCoin> AnonScoreTxSourceBiasedShuffle<TCoin>(TCoin[] coins)
		where TCoin : ISmartCoin
	{
		var orderedCoins = new List<TCoin>();
		for (int i = 0; i < coins.Length; i++)
		{
			// Order by anonscore first.
			var remaining = coins.Except(orderedCoins).OrderBy(x => x.AnonymitySet);

			// Then manipulate the list so repeating tx sources go to the end.
			var alternating = new List<TCoin>();
			var skipped = new List<TCoin>();
			foreach (var c in remaining)
			{
				if (alternating.Any(x => x.TransactionId == c.TransactionId) || orderedCoins.Any(x => x.TransactionId == c.TransactionId))
				{
					skipped.Add(c);
				}
				else
				{
					alternating.Add(c);
				}
			}
			alternating.AddRange(skipped);

			var coin = alternating.BiasedRandomElement(biasPercent: 50, Rnd)!;
			orderedCoins.Add(coin);
			yield return coin;
		}
	}

	private static bool TryAddGroup<TCoin>(UtxoSelectionParameters parameters, Dictionary<int, IEnumerable<TCoin>> groups, IEnumerable<TCoin> group)
		where TCoin : ISmartCoin
	{
		var effectiveInputSum = group.Sum(x => x.EffectiveValue(parameters.MiningFeeRate, parameters.CoordinationFeeRate));
		if (effectiveInputSum >= parameters.MinAllowedOutputAmount)
		{
			var k = HashCode.Combine(group.OrderBy(x => x.TransactionId).ThenBy(x => x.Index));
			return groups.TryAdd(k, group);
		}

		return false;
	}

	private static double GetAnonLoss<TCoin>(IEnumerable<TCoin> coins)
		where TCoin : ISmartCoin
	{
		double minimumAnonScore = coins.Min(x => x.AnonymitySet);
		return coins.Sum(x => (x.AnonymitySet - minimumAnonScore) * x.Amount.Satoshi) / coins.Sum(x => x.Amount.Satoshi);
	}

	private static int GetReps<TCoin>(IEnumerable<TCoin> group)
		where TCoin : ISmartCoin
		=> group.GroupBy(x => x.TransactionId).Sum(coinsInTxGroup => coinsInTxGroup.Count() - 1);
}
