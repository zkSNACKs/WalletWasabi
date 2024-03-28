using Microsoft.Extensions.Hosting;
using NBitcoin;
using Nito.AsyncEx;
using System.Collections.Generic;
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
using WalletWasabi.Services.Terminate;
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
		set
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

						uint currentHeight = (uint)lastHeight.Value + 1;

						FilterModel filter = await FilterIteratorsBySyncType[request.SyncType].GetAndRemoveAsync(currentHeight, cancellationToken).ConfigureAwait(false);
						var matchFound = await ProcessFilterModelAsync(filter, request.SyncType, cancellationToken).ConfigureAwait(false);

						reachedBlockChainTip = currentHeight == BitcoinStore.SmartHeaderChain.TipHeight;
						bool storeToDisk = matchFound || reachedBlockChainTip;
						KeyManager.SetBestHeight(request.SyncType, new Height(currentHeight), storeToDisk);
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
						Logger.LogWarning($"Tried to set exception for {request.SyncType.FriendlyName()} but status was already {request.Tcs.Task.Status}.");
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
			TerminateService.Instance?.SignalGracefulCrash(ex);
			throw;
		}
		finally
		{
			lock (Lock)
			{
				while (SynchronizationRequests.TryDequeue(out var request, out _))
				{
					request.Tcs.TrySetCanceled(cancellationToken);
				}
			}
		}
	}

	private Dictionary<(ScriptPubKeyType, bool), int> knownIndexLastUsedKeyByTypeAndIsInternal = new Dictionary<(ScriptPubKeyType, bool), int>()
	{
		{ (ScriptPubKeyType.Segwit, true), 0 },
		{ (ScriptPubKeyType.Segwit, false), 0 },
		{ (ScriptPubKeyType.TaprootBIP86, true), 0 },
		{ (ScriptPubKeyType.TaprootBIP86, false), 0 },
	};

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

		var listScriptPubKeyAccordingSyncType = scriptPubKeyAccordingSyncType.GroupBy(x => (x.Type, x.IsInternal));

		if (listScriptPubKeyAccordingSyncType.Sum(x => x.Count()) == 0)
		{
			return Enumerable.Empty<byte[]>();
		}

		var result = Enumerable.Empty<byte[]>();
		foreach (var group in listScriptPubKeyAccordingSyncType)
		{
			var list = group.ToList();
			if (KeyManager.InvalidateFirstReceivingHeight)
			{
				knownIndexLastUsedKeyByTypeAndIsInternal[group.Key] = Math.Max(0, list.FindLastIndex(x => x.FirstReceivingHeight is not null));
			}
			var lastCleanKeyToTestIndex = Math.Min(list.Count, knownIndexLastUsedKeyByTypeAndIsInternal[group.Key] + KeyManager.MinGapLimit);
			result = result.Union(list[..lastCleanKeyToTestIndex].Select(x => x.CompressedScriptPubKey));
		}
		KeyManager.InvalidateFirstReceivingHeight = false;

		return result;
	}

	private async Task<bool> ProcessFilterModelAsync(FilterModel filter, SyncType syncType, CancellationToken cancel)
	{
		var height = new Height(filter.Header.Height);
		var toTestKeys = GetScriptPubKeysToTest(height, syncType);

		var matchFound = false;
		if (toTestKeys.Any())
		{
			var compressedScriptPubKeys = toTestKeys;
			matchFound = filter.Filter.MatchAny(compressedScriptPubKeys, filter.FilterKey);

			if (matchFound)
			{
				// Wait until downloaded.
				Block currentBlock = await KeepTryingToGetBlockAsync(filter.Header.BlockHash, new Priority(syncType, filter.Header.Height), cancel)
					.ConfigureAwait(false);

				var txsToProcess = new List<SmartTransaction>();
				for (int i = 0; i < currentBlock.Transactions.Count; i++)
				{
					Transaction tx = currentBlock.Transactions[i];
					txsToProcess.Add(new SmartTransaction(tx, height, currentBlock.GetHash(), i, firstSeen: currentBlock.Header.BlockTime, labels: BitcoinStore.MempoolService.TryGetLabel(tx.GetHash())));
				}

				TransactionProcessor.Process(txsToProcess);
			}
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
				KeyManager.SetMaxBestHeight(new Height(invalidFilter.Header.Height - 1));
				TransactionProcessor.UndoBlock((int)invalidFilter.Header.Height);
				BitcoinStore.TransactionStore.ReleaseToMempoolFromBlock(invalidBlockHash);

				await BlockDownloadService.RemoveBlocksAsync(invalidFilter.Header.Height).ConfigureAwait(false);
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
		BlockDownloadService.IResult fullNodeResult = await BlockDownloadService.TryGetBlockAsync(TrustedFullNodeSourceRequest.Instance, blockHash, priority, cancellationToken)
			.ConfigureAwait(false);

		if (fullNodeResult is BlockDownloadService.SuccessResult successFullNodeResult)
		{
			return successFullNodeResult.Block;
		}

		if (fullNodeResult is BlockDownloadService.CanceledResult)
		{
			throw new OperationCanceledException();
		}

		while (true)
		{
			BlockDownloadService.IResult p2pNodeResult = await BlockDownloadService.TryGetBlockAsync(P2pSourceRequest.Automatic, blockHash, priority, cancellationToken)
				.ConfigureAwait(false);

			if (p2pNodeResult is BlockDownloadService.SuccessResult successP2pResult)
			{
				return successP2pResult.Block;
			}

			if (p2pNodeResult is BlockDownloadService.CanceledResult)
			{
				throw new OperationCanceledException();
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
}
