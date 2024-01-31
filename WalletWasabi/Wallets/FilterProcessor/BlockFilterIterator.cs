using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Backend.Models;
using WalletWasabi.Stores;

namespace WalletWasabi.Wallets.FilterProcessor;

/// <summary>
/// Iterator that caches block filters from the index store to allow efficient iteration over block filters in ascending order.
/// </summary>
public class BlockFilterIterator
{
	public BlockFilterIterator(IIndexStore indexStore, int maxNumberFiltersInMemory = 20000)
	{
		IndexStore = indexStore;
		MaxNumberFiltersInMemory = maxNumberFiltersInMemory;
	}

	/// <remarks>Internal only to allow modifications in tests.</remarks>
	internal Dictionary<uint, FilterModel> Cache { get; } = new();
	private IIndexStore IndexStore { get; }
	public int MaxNumberFiltersInMemory { get; }

	/// <summary>
	/// Gets block filter for the block of specified height.
	/// </summary>
	/// <remarks>Filter is immediately removed from the cache once the method returns. Repeated calls for single height are thus expensive.</remarks>
	public async Task<FilterModel> GetAndRemoveAsync(uint height, CancellationToken cancellationToken)
	{
		// Each block filter is to needed just once, so we can remove the block right now and free the memory sooner.
		if (Cache.Remove(height, out FilterModel? result))
		{
			return result;
		}

		// We don't have the next filter to process, so fetch another batch of filters from the database.
		Clear();

		FilterModel[] filtersBatch = await IndexStore.FetchBatchAsync(height, MaxNumberFiltersInMemory, cancellationToken).ConfigureAwait(false);

		// Check that we get a block filter and that the filter is actually the one we want as the previous command does not guarantee that we get such block.
		if (filtersBatch.Length == 0)
		{
			throw new UnreachableException($"No block was found for a batch starting with block height {height}.");
		}

		if (filtersBatch[0].Header.Height != height)
		{
			throw new UnreachableException($"Block filter for height {height} was not found.");
		}

		// Cache filters.
		uint expectedHeight = height + 1;

		// Do not store the first filter, the semantics is that the returned filter is no longer stored in the cache.
		foreach (FilterModel filter in filtersBatch.Skip(1))
		{
			// Make sure that the sequence of blocks is consecutive.
			if (expectedHeight != filter.Header.Height)
			{
				throw new UnreachableException($"Expected block with height {expectedHeight}, got {filter.Header.Height} (block hash: {filter.Header.BlockHash}).");
			}

			Cache[filter.Header.Height] = filter;
			expectedHeight++;
		}

		result = filtersBatch[0];

		return result;
	}

	public void Clear()
	{
		Cache.Clear();
	}
}
