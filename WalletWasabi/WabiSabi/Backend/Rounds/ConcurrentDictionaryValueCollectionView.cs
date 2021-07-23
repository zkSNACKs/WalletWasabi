using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;

namespace WalletWasabi.WabiSabi.Backend.Rounds
{
	// Expose the values of a concurrent dictionary as a mutable collection
	// using a function that maps to keys.
	public record ConcurrentDictionaryValueCollectionView<T>(ConcurrentDictionary<uint256, T> ConcurrentDictionary, Func<T, uint256> KeyFunc) : IEnumerable<T>
	{
		public int Count => ConcurrentDictionary.Count;

		public void Add(T value)
		{
			ConcurrentDictionary[KeyFunc(value)] = value;
		}

		public void Remove(T value)
		{
			ConcurrentDictionary.Remove(KeyFunc(value), out _);
		}

		public IEnumerator<T> GetEnumerator() => ConcurrentDictionary.Select(x => x.Value).GetEnumerator();

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
	}
}
