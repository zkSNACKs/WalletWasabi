using NBitcoin;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using WalletWasabi.Logging;

namespace WalletWasabi.Blockchain.TransactionBuilding;

/// <seealso href="https://murch.one/wp-content/uploads/2016/11/erhardt2016coinselection.pdf">Section "5.3 Branch and Bound".</seealso>
public class BranchAndBound
{
	private readonly Random _random = new();
	private readonly long _defaultTolerance = 500;

	/// <param name="values">All values must be strictly positive.</param>
	public BranchAndBound(List<long> values)
	{
		if (values.Count == 0)
		{
			throw new ArgumentException("List is empty.");
		}

		if (values.Any(x => x <= 0))
		{
			throw new ArgumentException("Only strictly positive values are supported.");
		}

		Count = values.Count;
		SortedValues = values.OrderByDescending(x => x).ToArray();
	}

	private enum NextAction
	{
		/// <summary>First try to include a value and then try not to include the value in the selection.</summary>
		IncludeFirstThenOmit,

		/// <summary>First try NOT to include a value and then try to include the value in the selection.</summary>
		OmitFirstThenInclude,

		/// <summary>Include value.</summary>
		Include,

		/// <summary>Omit value.</summary>
		Omit,

		/// <summary>Current selection is wrong, rolling back and trying different combination.</summary>
		Backtrack
	}

	/// <remarks>Input values sorted in descending order.</remarks>
	private long[] SortedValues { get; }

	/// <summary>Number of input values.</summary>
	private int Count { get; }

	/// <summary>
	/// Attempts to find a set of values that sum up to the target value.
	/// </summary>
	/// <param name="target">Target value we want to sum up from the input values.</param>
	/// <param name="selectedValues">Values that sum up to the <paramref name="target"/> value.</param>
	/// <returns><c>true</c> when a match is found, <c>false</c> otherwise.</returns>
	public bool TryGetExactMatch(long target, [NotNullWhen(true)] out List<long>? selectedValues, CancellationToken cancellationToken)
	{
		selectedValues = null;

		if (SortedValues.Sum() < target)
		{
			return false;
		}

		if (TryFindSolution(target, out long[]? solution, cancellationToken))
		{
			selectedValues = new List<long>();

			for (int i = 0; i < Count; i++)
			{
				if (solution[i] > 0)
				{
					selectedValues.Add(SortedValues[i]);
				}
			}

			Logger.LogInfo($"{Count} coins were involved in 'Branch and Bound' selection.");
			selectedValues = selectedValues.OrderByDescending(x => x).ToList();
			return true;
		}

		return false;
	}

	private bool TryFindSolution(long target, [NotNullWhen(true)] out long[]? solution, CancellationToken cancellationToken)
	{
		// Current effective value.
		long effValue = 0L;

		// Current depth (think of the depth in the recursive algorithm sense).
		int depth = 0;

		int loopCounter = 0;

		solution = new long[Count];
		NextAction[] actions = new NextAction[Count];
		actions[0] = GetRandomNextAction();

		do
		{
			NextAction action = actions[depth];

			loopCounter++;

			// Branch WITH the value included.
			if ((action == NextAction.IncludeFirstThenOmit) || (action == NextAction.Include))
			{
				actions[depth] = GetNextStep(action);

				solution[depth] = SortedValues[depth];
				effValue += solution[depth];

				if (effValue > target + _defaultTolerance)
				{
					// Excessive funds, cut the branch!
					continue;
				}
				else if (effValue <= target + _defaultTolerance && effValue >= target)
				{
					// Match found!
					return true;
				}
				else if (depth + 1 == Count)
				{
					// Leaf reached, no match
					continue;
				}

				depth++;
				actions[depth] = GetRandomNextAction();
			}
			else if ((action == NextAction.OmitFirstThenInclude) || (action == NextAction.Omit))
			{
				actions[depth] = GetNextStep(action);

				// Branch WITHOUT the value included.
				effValue -= solution[depth];
				solution[depth] = 0;

				if (depth + 1 == Count)
				{
					// Leaf reached, no match
					continue;
				}

				depth++;
				actions[depth] = GetRandomNextAction();
			}
			else
			{
				effValue -= solution[depth];
				solution[depth] = 0;
				depth--;
			}

			// Optimization: Do not check cancellation token every time as it requires accessing volatile memory.
			if (loopCounter % 10_000 == 0 && cancellationToken.IsCancellationRequested)
			{
				return false;
			}
		}
		while (depth >= 0);

		return false;
	}

	private NextAction GetRandomNextAction()
	{
		return _random.Next(0, 2) == 1 ? NextAction.IncludeFirstThenOmit : NextAction.OmitFirstThenInclude;
	}

	private static NextAction GetNextStep(NextAction action)
	{
		return action switch
		{
			NextAction.IncludeFirstThenOmit => NextAction.Omit,
			NextAction.OmitFirstThenInclude => NextAction.Include,
			NextAction.Include => NextAction.Backtrack,
			NextAction.Omit => NextAction.Backtrack,
			NextAction.Backtrack => throw new InvalidOperationException("This should never happen."),
			_ => throw new InvalidOperationException("No other values are valid.")
		};
	}
}
