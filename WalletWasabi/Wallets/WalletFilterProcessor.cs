using Microsoft.Extensions.Hosting;
using NBitcoin;
using Nito.AsyncEx;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Backend.Models;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionProcessing;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Extensions;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Stores;
using WalletWasabi.Wallets.BlockProvider;
using WalletWasabi.Wallets.FilterProcessor;

namespace WalletWasabi.Wallets;

/// <summary>
/// Service that keeps processing block filters.
/// </summary>
/// <seealso href="https://github.com/zkSNACKs/WalletWasabi/issues/10219">TurboSync specification.</seealso>
public class WalletFilterProcessor : BackgroundService
{
	/// <remarks>Guarded by <see cref="Lock"/>.</remarks>
	private FilterModel? _lastProcessedFilter;

	public WalletFilterProcessor(KeyManager keyManager, BitcoinStore bitcoinStore, TransactionProcessor transactionProcessor, BlockDownloadService blockDownloadService)
	{
		KeyManager = keyManager;
		BitcoinStore = bitcoinStore;
		TransactionProcessor = transactionProcessor;
		BlockDownloadService = blockDownloadService;

		FilterIteratorsBySyncType = new()
		{
			{ SyncType.Turbo, new BlockFilterIterator(BitcoinStore.IndexStore) },
			{ SyncType.NonTurbo, new BlockFilterIterator(BitcoinStore.IndexStore) },
			{ SyncType.Complete, new BlockFilterIterator(BitcoinStore.IndexStore) },
		};
	}

	/// <remarks>Guarded by <see cref="Lock"/>.</remarks>
	private PriorityQueue<SyncRequest, Priority> SynchronizationRequests { get; } = new(Priority.Comparer);

	/// <remarks>Guards <see cref="SynchronizationRequests"/> and <see cref="_lastProcessedFilter"/>.</remarks>
	private object Lock { get; } = new();
	private SemaphoreSlim SynchronizationRequestsSemaphore { get; } = new(initialCount: 0);

	private KeyManager KeyManager { get; }
	private BitcoinStore BitcoinStore { get; }
	private TransactionProcessor TransactionProcessor { get; }
	private BlockDownloadService BlockDownloadService { get; }

	public FilterModel? LastProcessedFilter
	{
		get
		{
			lock (Lock)
			{
				return _lastProcessedFilter;
			}
		}
		private set
		{
			lock (Lock)
			{
				_lastProcessedFilter = value;
			}
		}
	}

	/// <remarks>Internal only to allow modifications in tests.</remarks>
	internal Dictionary<SyncType, BlockFilterIterator> FilterIteratorsBySyncType { get; }

	/// <summary>Make sure we don't process any request while a reorg is happening.</summary>
	private AsyncLock ReorgLock { get; } = new();

	public async Task ProcessAsync(IEnumerable<SyncType> syncTypes, CancellationToken cancellationToken)
	{
		var tasks = syncTypes.Select(syncType => ProcessAsync(syncType, cancellationToken)).ToList();
		await Task.WhenAll(tasks).ConfigureAwait(false);
	}

	public async Task ProcessAsync(SyncType syncType, CancellationToken cancellationToken)
	{
		SyncRequest request = new(syncType, new TaskCompletionSource());
		Priority priority = new(request.SyncType);

		lock (Lock)
		{
			SynchronizationRequests.Enqueue(request, priority);
			SynchronizationRequestsSemaphore.Release(releaseCount: 1);
		}

		await request.Tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	/// <summary>Used for filter synchronization.</summary>
	protected override async Task ExecuteAsync(CancellationToken cancellationToken)
	{
		try
		{
			// This will store all pre processing tasks per sync type. No need for Priority here as requests are added by successive Heights.
			Dictionary<SyncType, Queue<FilterPreProcessing>> preProcessTasks = Enum.GetValues<SyncType>().ToDictionary(syncType => syncType, _ => new Queue<FilterPreProcessing>());

			while (true)
			{
				await SynchronizationRequestsSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

				SyncRequest? request;

				lock (Lock)
				{
					if (!SynchronizationRequests.TryPeek(out request, out _))
					{
						continue;
					}
				}

				try
				{
					bool reachedBlockChainTip;
					using (await ReorgLock.LockAsync(cancellationToken).ConfigureAwait(false))
					{
						Height lastHeight = KeyManager.GetBestHeight(request.SyncType);

						if (lastHeight == BitcoinStore.SmartHeaderChain.TipHeight)
						{
							request.Tcs.SetResult();
							lock (Lock)
							{
								SynchronizationRequests.Dequeue();
							}
							continue;
						}

						uint toProcessHeight = (uint)lastHeight.Value + 1;

						// MaxNumberFiltersInMemory is also the maximum of pre-process slots because filters are in memory until the end of the task.
						// This is not 100% true because the filter has to stay in memory until after ProcessFilterModelAsync finishes.
						var availablePreProcessSlots = Math.Max(0, FilterIteratorsBySyncType[request.SyncType].MaxNumberFiltersInMemory - preProcessTasks[request.SyncType].Count);

						// We have to skip all the current tasks.
						var toPreProcessHeight = toProcessHeight + (uint)preProcessTasks[request.SyncType].Count;
						for (uint i = 0; i < availablePreProcessSlots; i++)
						{
							if (toPreProcessHeight > BitcoinStore.SmartHeaderChain.TipHeight)
							{
								break;
							}
							var filter = await FilterIteratorsBySyncType[request.SyncType].GetAndRemoveAsync(toPreProcessHeight, cancellationToken).ConfigureAwait(false);
							preProcessTasks[request.SyncType].Enqueue(new FilterPreProcessing(filter, PreProcessFilterModel(filter, request.SyncType, cancellationToken)));
							toPreProcessHeight++;
						}

						if (preProcessTasks[request.SyncType].Count == 0)
						{
							// TODO: I believe this must be a problem?
							continue;
						}

						// TODO: Reorgs??
						var matchFound = await ProcessFilterModelAsync(preProcessTasks[request.SyncType].Dequeue(), request.SyncType, cancellationToken).ConfigureAwait(false);

						reachedBlockChainTip = toProcessHeight == BitcoinStore.SmartHeaderChain.TipHeight;
						bool storeToDisk = matchFound || reachedBlockChainTip;
						KeyManager.SetBestHeight(request.SyncType, new Height(toProcessHeight), storeToDisk);
					}

					if (reachedBlockChainTip)
					{
						request.Tcs.SetResult();
						lock (Lock)
						{
							SynchronizationRequests.Dequeue();
						}
					}
					else
					{
						SynchronizationRequestsSemaphore.Release(1);
					}
				}
				catch (Exception ex)
				{
					if (!request.Tcs.TrySetException(ex))
					{
						Logger.LogWarning($"Tried to set exception for {request.SyncType.ToString()} but status was already {request.Tcs.Task.Status}.");
					}

					throw;
				}

				// Clear all caches once not needed.
				if (SynchronizationRequestsSemaphore.CurrentCount == 0)
				{
					foreach (SyncType syncType in Enum.GetValues<SyncType>())
					{
						FilterIteratorsBySyncType[syncType].Clear();
					}
				}
			}
		}
		catch (OperationCanceledException)
		{
			Logger.LogDebug("Filter processor's execution was stopped.");
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);
			throw;
		}
		finally
		{
			lock (Lock)
			{
				while (SynchronizationRequests.TryDequeue(out var request, out _))
				{
					_ = request.Tcs.TrySetCanceled(cancellationToken);
				}
			}
		}
	}

	/// <summary>
	/// Return the keys to test against the filter depending on the height of the filter and the type of synchronization.
	/// </summary>
	/// <param name="filterHeight">Height of the filter that needs to be tested.</param>
	/// <param name="syncType">First sync of TurboSync, second one, or complete synchronization.</param>
	/// <returns>Keys to test against this filter.</returns>
	private IEnumerable<byte[]> GetScriptPubKeysToTest(Height filterHeight, SyncType syncType)
	{
		bool ScriptAlreadySpent(KeyManager.ScriptPubKeySpendingInfo spendingInfo) =>
			spendingInfo.LatestSpendingHeight is { } spendingHeight && spendingHeight < filterHeight;

		bool ScriptNotSpentAtTheMoment(KeyManager.ScriptPubKeySpendingInfo spendingInfo) =>
			!ScriptAlreadySpent(spendingInfo);

		var scriptsSpendingInfo = KeyManager.UnsafeGetSynchronizationInfos();
		var scriptPubKeyAccordingSyncType = syncType switch
		{
			SyncType.Complete => scriptsSpendingInfo,
			SyncType.Turbo => scriptsSpendingInfo.Where(ScriptNotSpentAtTheMoment),
			SyncType.NonTurbo => scriptsSpendingInfo.Where(ScriptAlreadySpent),
			_ => throw new ArgumentOutOfRangeException(nameof(syncType), syncType, null)
		};

		return scriptPubKeyAccordingSyncType.Select(x => x.CompressedScriptPubKey);
	}

	private Task<Block>? PreProcessFilterModel(FilterModel filter, SyncType syncType, CancellationToken cancel)
	{
		var height = new Height(filter.Header.Height);
		var toTestKeys = GetScriptPubKeysToTest(height, syncType);
		var match = toTestKeys.Any() && filter.Filter.MatchAny(toTestKeys, filter.FilterKey);
		return match ?
			KeepTryingToGetBlockAsync(filter.Header.BlockHash, new Priority(syncType, filter.Header.Height), cancel) :
			null;
	}

	private async Task<bool> ProcessFilterModelAsync(FilterPreProcessing preProcessing, SyncType syncType, CancellationToken cancel)
	{
		var preProcessingTask = preProcessing.Task;
		var filter = preProcessing.Filter;

		bool matchFound;
		var height = new Height(filter.Header.Height);

		if (preProcessingTask is not null)
		{
			matchFound = true;
		}
		else
		{
			// Test against the keys that haven't been used for the preprocessing
			// TODO: Don't retest against keys that were tested during preprocessing.
			var toTestKeys = GetScriptPubKeysToTest(height, syncType);
			matchFound = toTestKeys.Any() && filter.Filter.MatchAny(toTestKeys, filter.FilterKey);
		}

		if (matchFound)
		{
			Stopwatch sw = Stopwatch.StartNew();
			Block block = preProcessingTask is not null ?
				await preProcessingTask.ConfigureAwait(false) :
				await KeepTryingToGetBlockAsync(
					filter.Header.BlockHash,
					new Priority(syncType, filter.Header.Height),
					cancel).ConfigureAwait(false);
			Logger.LogError($"{height}: Dl finished at {DateTime.UtcNow} - preprocessed: {preProcessingTask is not null} - waited {sw.ElapsedMilliseconds}ms synchronously");

			var txsToProcess = new List<SmartTransaction>();
			for (int i = 0; i < block.Transactions.Count; i++)
			{
				Transaction tx = block.Transactions[i];
				txsToProcess.Add(new SmartTransaction(tx, height, block.GetHash(), i, firstSeen: block.Header.BlockTime, labels: BitcoinStore.MempoolService.TryGetLabel(tx.GetHash())));
			}

			TransactionProcessor.Process(txsToProcess);
		}

		LastProcessedFilter = filter;
		return matchFound;
	}

	private async void ReorgedAsync(object? sender, FilterModel invalidFilter)
	{
		try
		{
			uint256 invalidBlockHash = invalidFilter.Header.BlockHash;

			using (await ReorgLock.LockAsync(CancellationToken.None).ConfigureAwait(false))
			{
				var newBestHeight = new Height(invalidFilter.Header.Height - 1);
				KeyManager.SetMaxBestHeight(newBestHeight);
				TransactionProcessor.UndoBlock((int)invalidFilter.Header.Height);
				BitcoinStore.TransactionStore.ReleaseToMempoolFromBlock(invalidBlockHash);
				await BlockDownloadService.RemoveBlocksAsync((uint)newBestHeight.Value).ConfigureAwait(false);
			}
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex);
		}
	}

	/// <summary>
	/// Attempt to get the bitcoin block from a full node or use P2P as a fallback.
	/// </summary>
	private async Task<Block> KeepTryingToGetBlockAsync(uint256 blockHash, Priority priority, CancellationToken cancellationToken)
	{
		BlockDownloadService.IResult fullNodeResult = await BlockDownloadService.TryGetBlockAsync(FullNodeSourceRequest.Instance, blockHash, priority, cancellationToken).ConfigureAwait(false);

		if (fullNodeResult is BlockDownloadService.SuccessResult successFullNodeResult)
		{
			return successFullNodeResult.Block;
		}

		while (true)
		{
			BlockDownloadService.IResult p2pNodeResult = await BlockDownloadService.TryGetBlockAsync(P2pSourceRequest.Automatic, blockHash, priority, cancellationToken).ConfigureAwait(false);

			if (p2pNodeResult is BlockDownloadService.SuccessResult successP2pResult)
			{
				return successP2pResult.Block;
			}
		}
	}

	public override async Task StartAsync(CancellationToken cancellationToken)
	{
		BitcoinStore.IndexStore.Reorged += ReorgedAsync;
		await base.StartAsync(cancellationToken).ConfigureAwait(false);
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		BitcoinStore.IndexStore.Reorged -= ReorgedAsync;
		await base.StopAsync(cancellationToken).ConfigureAwait(false);
	}

	public override void Dispose()
	{
		SynchronizationRequestsSemaphore.Dispose();
		base.Dispose();
	}

	public record SyncRequest(SyncType SyncType, TaskCompletionSource Tcs);
	private record FilterPreProcessing(FilterModel Filter, Task<Block>? Task);
}
