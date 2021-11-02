using System;
using System.Linq;
using System.Collections.Immutable;
using NBitcoin;

namespace WalletWasabi.WabiSabi.Models.Decomposition
{
	// TODO use Money instead of long.
	// Unfortunately this can consume a significant amount of memory, so in
	// order to provide the nicer Money based API, DecompositionsOfASize should
	// first be modified to no longer use this type internally. For now, this
	// class uses a long of the effective cost and only that to represent self
	// spend outputs, which is less convenient but does reduce memory
	// consumption somewhat.
	public sealed record Decomposition : IComparable<Decomposition>, IEquatable<Decomposition>
	{
		// Useful for prettifying test assertions, or alternatively as a debug
		// display attribute:
		// public override string ToString()
		// 	=> $"[ {TotalValue} = { string.Join(' ', Outputs) } ]";

		internal Decomposition(params long[] outputs)
		{
			Outputs = outputs.OrderByDescending(x => x).Select(x => (long)x).ToImmutableArray();
			TotalValue = this.Outputs.Sum();
		}

		public ImmutableArray<long> Outputs { get; private init; }

		public long TotalValue { get; private init; }

		public Decomposition Extend(long output) =>
			this with {
				Outputs = (output <= Outputs[^1])
					? Outputs.Add(output)
					: throw new InvalidOperationException("Generated decompositions must be monotonically decreasing"),
				TotalValue = TotalValue + output
			};

		public int CompareTo(Decomposition? other)
		{
			static int InternalCompare(Decomposition left, Decomposition right)
			{
				// Total effective value
				var cmp = left.TotalValue.CompareTo(right.TotalValue);
				if (cmp != 0)
				{
					return cmp;
				}

				// If the effective value is the same, the shorter one must
				// contain larger values
				cmp = right.Outputs.Length.CompareTo(left.Outputs.Length);
				if (cmp != 0)
				{
					return cmp;
				}

				// If they are the same length, compare lexicographically
				return Enumerable.Zip(left.Outputs, right.Outputs, (x, y) => x.CompareTo(y)).FirstOrDefault(x => x != 0);
			}

			return (this, other) switch
			{
				(null, null) => 0,
				(null,    _) => 1,
				(_   , null) => -1,
				_ => InternalCompare(this, other),
			};
		}

		public bool Equals(Decomposition? other)
			=> (other, this) switch
			{
				({}, {}) => Outputs.SequenceEqual(other.Outputs),
				(null, null) => true,
				_ => false
			};

		public override int GetHashCode()
			=> Outputs.GetHashCode();
	}
}
