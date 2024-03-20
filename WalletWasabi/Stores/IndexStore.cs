using Microsoft.Data.Sqlite;
using NBitcoin;
using Nito.AsyncEx;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Backend.Models;
using WalletWasabi.Blockchain.BlockFilters;
using WalletWasabi.Blockchain.Blocks;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Models;

namespace WalletWasabi.Stores;

/// <summary>
/// Manages to store the filters safely.
/// </summary>
public class IndexStore : IIndexStore, IAsyncDisposable
{
	public IndexStore(string workFolderPath, Network network, SmartHeaderChain smartHeaderChain)
	{
		SmartHeaderChain = smartHeaderChain;

		workFolderPath = Guard.NotNullOrEmptyOrWhitespace(nameof(workFolderPath), workFolderPath, trim: true);
		IoHelpers.EnsureDirectoryExists(workFolderPath);

		// Migration data.
		OldIndexFilePath = Path.Combine(workFolderPath, "MatureIndex.dat");
		OldImmatureIndexFilePath = Path.Combine(workFolderPath, "ImmatureIndex.dat");
		NewIndexFilePath = Path.Combine(workFolderPath, "IndexStore.sqlite");
		RunMigration = File.Exists(OldIndexFilePath);

		if (network == Network.RegTest)
		{
			File.Delete(NewIndexFilePath);
		}

		try
		{
			IndexStorage = BlockFilterSqliteStorage.FromFile(dataSource: NewIndexFilePath, startingFilter: StartingFilters.GetStartingFilter(network));
		}
		catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 11) // 11 ~ SQLITE_CORRUPT error code
		{
			Logger.LogError($"Failed to open SQLite storage file because it's corrupted. Deleting the storage file '{NewIndexFilePath}'.");

			// The database file can still be in use, clear all pools to unlock the filter database file.
			SqliteConnection.ClearAllPools();
			File.Delete(NewIndexFilePath);
			throw;
		}
	}

	public event EventHandler<FilterModel>? Reorged;

	public event EventHandler<IEnumerable<FilterModel>>? NewFilters;

	/// <summary>Mature index path for migration purposes.</summary>
	private string OldIndexFilePath { get; }

	/// <summary>Immature index path for migration purposes.</summary>
	private string OldImmatureIndexFilePath { get; }

	/// <summary>SQLite file path for migration purposes.</summary>
	private string NewIndexFilePath { get; }

	/// <summary>Run migration if SQLite file does not exist.</summary>
	private bool RunMigration { get; }

	private SmartHeaderChain SmartHeaderChain { get; }

	/// <summary>Task completion source that is completed once a <see cref="InitializeAsync(CancellationToken)"/> finishes.</summary>
	/// <remarks><c>true</c> if it finishes successfully, <c>false</c> in all other cases.</remarks>
	public TaskCompletionSource<bool> InitializedTcs { get; } = new();

	/// <summary>Filter disk storage.</summary>
	/// <remarks>Guarded by <see cref="IndexLock"/>.</remarks>
	private BlockFilterSqliteStorage IndexStorage { get; }

	/// <summary>Guards <see cref="IndexStorage"/>.</summary>
	private AsyncLock IndexLock { get; } = new();

	public async Task InitializeAsync(CancellationToken cancellationToken)
	{
		using IDisposable _ = BenchmarkLogger.Measure();

		try
		{
			using (await IndexLock.LockAsync(cancellationToken).ConfigureAwait(false))
			{
				cancellationToken.ThrowIfCancellationRequested();

				// Migration code.
				if (RunMigration)
				{
					MigrateToSqliteNoLock(cancellationToken);
				}

				// If the automatic migration to SQLite is stopped, we would not delete the old index data.
				// So check it every time.
				RemoveOldIndexFilesIfExist();

				await InitializeFiltersNoLockAsync(cancellationToken).ConfigureAwait(false);

				// Initialization succeeded.
				InitializedTcs.SetResult(true);
			}
		}
		catch (Exception)
		{
			InitializedTcs.SetResult(false);
			throw;
		}
	}

	private void RemoveOldIndexFilesIfExist()
	{
		if (File.Exists(OldIndexFilePath))
		{
			try
			{
				File.Delete($"{OldImmatureIndexFilePath}.dig"); // No exception is thrown if file does not exist.
				File.Delete(OldImmatureIndexFilePath);
				File.Delete($"{OldIndexFilePath}.dig");
				File.Delete(OldIndexFilePath);

				Logger.LogInfo("Removed old index file data.");
			}
			catch (Exception ex)
			{
				Logger.LogDebug(ex);
			}
		}
	}

	private void MigrateToSqliteNoLock(CancellationToken cancel)
	{
		int i = 0;

		try
		{
			Logger.LogWarning("Migration of block filters to SQLite format is about to begin. Please wait a moment.");

			Stopwatch stopwatch = Stopwatch.StartNew();

			IndexStorage.Clear();

			List<string> filters = new(capacity: 10_000);
			using (FileStream fs = File.Open(OldIndexFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
			using (BufferedStream bs = new(fs))
			using (StreamReader sr = new(bs))
			{
				while (true)
				{
					cancel.ThrowIfCancellationRequested();

					i++;
					string? line = sr.ReadLine();

					if (line is null)
					{
						break;
					}

					// Starting filter is already added at this point.
					if (i == 1)
					{
						continue;
					}

					filters.Add(line);

					if (i % 10_000 == 0)
					{
						IndexStorage.BulkAppend(filters);
						filters.Clear();
					}
				}
			}

			IndexStorage.BulkAppend(filters);

			Logger.LogInfo($"Migration of {i} filters to SQLite was finished in {stopwatch.Elapsed} seconds.");
		}
		catch (OperationCanceledException)
		{
			SqliteConnection.ClearAllPools();
			throw;
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);

			SqliteConnection.ClearAllPools();

			// Do not run migration code again if it fails.
			File.Delete(NewIndexFilePath);
			File.Delete(OldIndexFilePath);
		}
	}

	/// <remarks>Guarded by <see cref="IndexLock"/>.</remarks>
	private Task InitializeFiltersNoLockAsync(CancellationToken cancellationToken)
	{
		try
		{
			using IDisposable _ = BenchmarkLogger.Measure(LogLevel.Debug, "Block filters loading");

			int i = 0;

			// Read last N filters. There is no need to read all of them.
			foreach (FilterModel filter in IndexStorage.FetchLast(n: 5000))
			{
				i++;

				if (!TryProcessFilterNoLock(filter, enqueue: false))
				{
					throw new InvalidOperationException("Index file inconsistency detected.");
				}

				cancellationToken.ThrowIfCancellationRequested();
			}

			Logger.LogDebug($"Loaded {i} lines from the mature index file.");
		}
		catch (InvalidOperationException ex)
		{
			// We found a corrupted entry. Clear the corrupted database and stop here.
			Logger.LogError("Filter index got corrupted. Clearing the filter index...");
			Logger.LogDebug(ex);
			IndexStorage.Clear();
			throw;
		}

		return Task.CompletedTask;
	}

	/// <remarks>Requires <see cref="IndexLock"/> lock acquired.</remarks>
	private bool TryProcessFilterNoLock(FilterModel filter, bool enqueue)
	{
		try
		{
			SmartHeaderChain.AppendTip(filter.Header);

			if (enqueue)
			{
				if (!IndexStorage.TryAppend(filter))
				{
					throw new InvalidOperationException("Failed to append filter to the database.");
				}
			}

			return true;
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);
			return false;
		}
	}

	public async Task AddNewFiltersAsync(IEnumerable<FilterModel> filters)
	{
		using (await IndexLock.LockAsync(CancellationToken.None).ConfigureAwait(false))
		{
			using SqliteTransaction sqliteTransaction = IndexStorage.BeginTransaction();

			int processed = 0;

			try
			{
				foreach (FilterModel filter in filters)
				{
					if (!TryProcessFilterNoLock(filter, enqueue: true))
					{
						throw new InvalidOperationException($"Failed to process filter with height {filter.Header.Height}.");
					}

					processed++;
					SmartHeaderChain.SetServerTipHeight(Math.Max(SmartHeaderChain.ServerTipHeight, filter.Header.Height));
				}
			}
			finally
			{
				sqliteTransaction.Commit();

				if (processed > 0)
				{
					NewFilters?.Invoke(this, filters.Take(processed));
				}
			}
		}
	}

	public async Task<FilterModel[]> FetchBatchAsync(uint fromHeight, int batchSize, CancellationToken cancellationToken)
	{
		using (await IndexLock.LockAsync(cancellationToken).ConfigureAwait(false))
		{
			return IndexStorage.Fetch(fromHeight: fromHeight, limit: batchSize).ToArray();
		}
	}

	public Task<FilterModel?> TryRemoveLastFilterAsync()
	{
		return TryRemoveLastFilterIfNewerThanAsync(height: null);
	}

	private async Task<FilterModel?> TryRemoveLastFilterIfNewerThanAsync(uint? height)
	{
		FilterModel? filter;

		using (await IndexLock.LockAsync(CancellationToken.None).ConfigureAwait(false))
		{
			if (height is null)
			{
				if (!IndexStorage.TryRemoveLast(out filter))
				{
					throw new InvalidOperationException("No last filter.");
				}
			}
			else
			{
				if (!IndexStorage.TryRemoveLastIfNewerThan(height.Value, out filter))
				{
					throw new InvalidOperationException("No last filter.");
				}
			}

			if (SmartHeaderChain.TipHeight != filter.Header.Height)
			{
				throw new InvalidOperationException($"{nameof(SmartHeaderChain)} and {nameof(IndexStorage)} are not in sync.");
			}

			SmartHeaderChain.RemoveTip();
		}

		Reorged?.Invoke(this, filter);

		return filter;
	}

	public async Task RemoveAllNewerThanAsync(uint height)
	{
		while (true)
		{
			FilterModel? filterModel = await TryRemoveLastFilterIfNewerThanAsync(height).ConfigureAwait(false);

			if (filterModel is null)
			{
				break;
			}
		}
	}

	/// <inheritdoc/>
	public ValueTask DisposeAsync()
	{
		IndexStorage.Dispose();
		return ValueTask.CompletedTask;
	}
}
