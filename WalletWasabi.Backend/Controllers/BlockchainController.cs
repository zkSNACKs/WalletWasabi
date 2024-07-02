using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;
using NBitcoin.RPC;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Backend.Models.Responses;
using WalletWasabi.BitcoinCore.Mempool;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Cache;
using WalletWasabi.Extensions;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Models;

namespace WalletWasabi.Backend.Controllers;

/// <summary>
/// To interact with the Bitcoin Blockchain.
/// </summary>
[Produces("application/json")]
[Route("api/v" + Constants.BackendMajorVersion + "/btc/[controller]")]
public class BlockchainController : ControllerBase
{
	public static readonly TimeSpan FilterTimeout = TimeSpan.FromMinutes(20);
	private static readonly MemoryCacheEntryOptions CacheEntryOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) };
	private static readonly MemoryCacheEntryOptions UnconfirmedTransactionChainCacheEntryOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10) };
	private static readonly MemoryCacheEntryOptions UnconfirmedTransactionChainItemCacheEntryOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10) };
	private static readonly MemoryCacheEntryOptions TransactionCacheOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(20) };

	public BlockchainController(IMemoryCache memoryCache, Global global)
	{
		Cache = new(memoryCache);
		Global = global;
	}

	private IRPCClient RpcClient => Global.RpcClient;
	private Network Network => Global.Config.Network;
	private MempoolMirror Mempool => Global.MempoolMirror;

	public IdempotencyRequestCache Cache { get; }

	public Global Global { get; }

	/// <summary>
	/// Get all fees.
	/// </summary>
	/// <param name="estimateSmartFeeMode">Bitcoin Core's estimatesmartfee mode: ECONOMICAL/CONSERVATIVE.</param>
	/// <returns>A dictionary of fee targets and estimations.</returns>
	/// <response code="200">A dictionary of fee targets and estimations.</response>
	/// <response code="400">Invalid estimation mode is provided, possible values: ECONOMICAL/CONSERVATIVE.</response>
	[HttpGet("all-fees")]
	[ProducesResponseType(200)]
	[ProducesResponseType(400)]
	public async Task<IActionResult> GetAllFeesAsync([FromQuery, Required] string estimateSmartFeeMode, CancellationToken cancellationToken)
	{
		if (!Enum.TryParse(estimateSmartFeeMode, ignoreCase: true, out EstimateSmartFeeMode mode))
		{
			return BadRequest("Invalid estimation mode is provided, possible values: ECONOMICAL/CONSERVATIVE.");
		}

		AllFeeEstimate estimation = await GetAllFeeEstimateAsync(mode, cancellationToken);

		return Ok(estimation.Estimations);
	}

	internal Task<AllFeeEstimate> GetAllFeeEstimateAsync(EstimateSmartFeeMode mode, CancellationToken cancellationToken = default)
	{
		var cacheKey = $"{nameof(GetAllFeeEstimateAsync)}_{mode}";

		return Cache.GetCachedResponseAsync(
			cacheKey,
			action: (string request, CancellationToken token) => RpcClient.EstimateAllFeeAsync(token),
			options: CacheEntryOptions,
			cancellationToken);
	}

	/// <summary>
	/// Gets mempool hashes.
	/// </summary>
	/// <param name="compactness">Can strip the last x characters from the hashes.</param>
	/// <returns>A collection of transaction hashes.</returns>
	/// <response code="200">A collection of transaction hashes.</response>
	/// <response code="400">Invalid model state.</response>
	[HttpGet("mempool-hashes")]
	[ProducesResponseType(200)]
	[ProducesResponseType(400)]
	[ResponseCache(Duration = 5)]
	public async Task<IActionResult> GetMempoolHashesAsync([FromQuery] int compactness = 64, CancellationToken cancellationToken = default)
	{
		if (compactness is < 1 or > 64)
		{
			return BadRequest("Invalid compactness parameter is provided.");
		}

		IEnumerable<string> fulls = await GetRawMempoolStringsWithCacheAsync(cancellationToken);

		if (compactness == 64)
		{
			return Ok(fulls);
		}
		else
		{
			IEnumerable<string> compacts = fulls.Select(x => x[..compactness]);
			return Ok(compacts);
		}
	}

	internal async Task<IEnumerable<string>> GetRawMempoolStringsWithCacheAsync(CancellationToken cancellationToken = default)
	{
		var cacheKey = $"{nameof(GetRawMempoolStringsWithCacheAsync)}";
		var cacheOptions = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(3) };

		return await Cache.GetCachedResponseAsync(
			cacheKey,
			action: (string request, CancellationToken token) => GetRawMempoolStringsNoCacheAsync(token),
			options: cacheOptions,
			cancellationToken);
	}

	private async Task<IEnumerable<string>> GetRawMempoolStringsNoCacheAsync(CancellationToken cancellationToken = default)
	{
		uint256[] transactionHashes = await Global.RpcClient.GetRawMempoolAsync(cancellationToken).ConfigureAwait(false);
		return transactionHashes.Select(x => x.ToString());
	}

	/// <summary>
	/// Attempts to get transactions.
	/// </summary>
	/// <param name="transactionIds">The transactions the client is interested in.</param>
	/// <returns>200 Ok on with the list of found transactions. This list can be empty if none of the transactions are found.</returns>
	/// <response code="200">Returns the list of transactions hexes. The list can be empty.</response>
	/// <response code="400">Something went wrong.</response>
	[HttpGet("transaction-hexes")]
	[ProducesResponseType(200)]
	[ProducesResponseType(400)]
	public async Task<IActionResult> GetTransactionsAsync([FromQuery, Required] IEnumerable<string> transactionIds, CancellationToken cancellationToken)
	{
		const int MaxTxToRequest = 10;
		int requestCount = transactionIds.Count();

		if (requestCount > MaxTxToRequest)
		{
			return BadRequest($"Maximum {MaxTxToRequest} transactions can be requested.");
		}

		uint256[] parsedTxIds;

		// Make sure TXIDs are not malformed.
		try
		{
			parsedTxIds = transactionIds.Select(x => new uint256(x)).ToArray();
		}
		catch
		{
			return BadRequest("Invalid transaction Ids.");
		}

		try
		{
			Transaction[] txs = await FetchTransactionsAsync(parsedTxIds, cancellationToken).ConfigureAwait(false);
			string[] hexes = txs.Select(x => x.ToHex()).ToArray();

			return Ok(hexes);
		}
		catch (Exception ex)
		{
			Logger.LogDebug(ex);
			return BadRequest(ex.Message);
		}
	}

	/// <summary>
	/// Fetches transactions from cache if possible and missing transactions are fetched using RPC.
	/// </summary>
	/// <exception cref="AggregateException">If RPC client succeeds in getting some transactions but not all.</exception>
	private async Task<Transaction[]> FetchTransactionsAsync(uint256[] txIds, CancellationToken cancellationToken)
	{
		int requestCount = txIds.Length;
		Dictionary<uint256, TaskCompletionSource<Transaction>> txIdsRetrieve = [];
		TaskCompletionSource<Transaction>[] txsCompletionSources = new TaskCompletionSource<Transaction>[requestCount];

		try
		{
			// Get task completion sources for transactions. They are either new (no one else is getting that transaction right now) or existing
			// and then some other caller needs the same transaction so we can use the existing task completion source.
			for (int i = 0; i < requestCount; i++)
			{
				uint256 txId = txIds[i];
				string cacheKey = $"{nameof(GetTransactionsAsync)}#{txId}";

				if (Cache.TryAddKey(cacheKey, TransactionCacheOptions, out TaskCompletionSource<Transaction> tcs))
				{
					txIdsRetrieve.Add(txId, tcs);
				}

				txsCompletionSources[i] = tcs;
			}

			if (txIdsRetrieve.Count > 0)
			{
				// Ask to get missing transactions over RPC.
				IEnumerable<Transaction> txs = await RpcClient.GetRawTransactionsAsync(txIdsRetrieve.Keys, cancellationToken).ConfigureAwait(false);

				Dictionary<uint256, Transaction> rpcBatch = txs.ToDictionary(x => x.GetHash(), x => x);

				foreach (KeyValuePair<uint256, Transaction> kvp in rpcBatch)
				{
					txIdsRetrieve[kvp.Key].TrySetResult(kvp.Value);
				}

				// RPC client does not throw if a transaction is missing, so we need to account for this case.
				if (rpcBatch.Count < txIdsRetrieve.Count)
				{
					IReadOnlyList<Exception> exceptions = MarkNotFinishedTasksAsFailed(txIdsRetrieve);
					throw new AggregateException(exceptions);
				}
			}

			Transaction[] result = new Transaction[requestCount];

			// Add missing transactions to the result array.
			for (int i = 0; i < requestCount; i++)
			{
				Transaction tx = await txsCompletionSources[i].Task.ConfigureAwait(false);
				result[i] = tx;
			}

			return result;
		}
		finally
		{
			if (txIdsRetrieve.Count > 0)
			{
				MarkNotFinishedTasksAsFailed(txIdsRetrieve);
			}
		}

		IReadOnlyList<Exception> MarkNotFinishedTasksAsFailed(Dictionary<uint256, TaskCompletionSource<Transaction>> txIdsRetrieve)
		{
			List<Exception>? exceptions = null;

			// It's necessary to always set a result to the task completion sources. Otherwise, cache can get corrupted.
			foreach ((uint256 txid, TaskCompletionSource<Transaction> tcs) in txIdsRetrieve)
			{
				if (!tcs.Task.IsCompleted)
				{
					exceptions ??= new();

					// Prefer new cache requests to try again rather than getting the exception. The window is small though.
					Exception e = new InvalidOperationException($"Failed to get the transaction '{txid}'.");
					exceptions.Add(e);
					Cache.Remove($"{nameof(GetTransactionsAsync)}#{txid}");
					tcs.SetException(e);
				}
			}

			return exceptions ?? [];
		}
	}

	/// <summary>
	/// Attempts to broadcast a transaction.
	/// </summary>
	/// <remarks>
	/// Sample request:
	///
	///     POST /broadcast
	///     "01000000014b6b6fced23fa0d772f83fd849ce2f4e8fa51ea49cc12710ebcdc722d74c87f5000000006a47304402206bf1118e381342d0387e47807c83d2c1e919e2e3792f2673579a9ce87a380db002207e471504f96d7830dc9cbb7442332d747a25dcfd5d1530feea92b8a302aa57f4012102a40230b345856cc18ca1d745e7ea52319a012753b050e24d7be64ca0b978fb3effffffff0235662803000000001976a9146adfacaab3dc7c51b3300c4256b184f95cc48f4288acd0dd0600000000001976a91411ff558b1790b8d57cb25b9c07094591cfd2051c88ac00000000"
	///
	/// </remarks>
	/// <param name="hex">The hex string of the raw transaction.</param>
	/// <returns>200 Ok on successful broadcast or 400 BadRequest on failure.</returns>
	/// <response code="200">If broadcast is successful.</response>
	/// <response code="400">If broadcast fails.</response>
	[HttpPost("broadcast")]
	[ProducesResponseType(200)]
	[ProducesResponseType(400)]
	public async Task<IActionResult> BroadcastAsync([FromBody, Required] string hex, CancellationToken cancellationToken)
	{
		Transaction transaction;
		try
		{
			transaction = Transaction.Parse(hex, Network);
		}
		catch (Exception ex)
		{
			Logger.LogDebug(ex);
			return BadRequest("Invalid hex.");
		}

		try
		{
			await RpcClient.SendRawTransactionAsync(transaction, cancellationToken);
		}
		catch (RPCException ex) when (ex.Message.Contains("already in block chain", StringComparison.InvariantCultureIgnoreCase))
		{
			return Ok("Transaction is already in the blockchain.");
		}
		catch (RPCException ex)
		{
			Logger.LogDebug(ex);
			var spenders = Global.HostedServices.Get<MempoolMirror>().GetSpenderTransactions(transaction.Inputs.Select(x => x.PrevOut));
			return BadRequest($"{ex.Message}:::{string.Join(":::", spenders.Select(x => x.ToHex()))}");
		}

		return Ok("Transaction is successfully broadcasted.");
	}

	/// <summary>
	/// Gets block filters from the provided block hash.
	/// </summary>
	/// <remarks>
	/// Filter examples:
	///
	///     Main: 0000000000000000001c8018d9cb3b742ef25114f27563e3fc4a1902167f9893
	///     TestNet: 00000000000f0d5edcaeba823db17f366be49a80d91d15b77747c2e017b8c20a
	///     RegTest: 0f9188f13cb7b2c71f2a335e3a4fc328bf5beb436012afca590b1a11466e2206
	///
	/// </remarks>
	/// <param name="bestKnownBlockHash">The best block hash the client knows its filter.</param>
	/// <param name="count">The number of filters to return.</param>
	/// <returns>The best height and an array of block hash : element count : filter pairs.</returns>
	/// <response code="200">The best height and an array of block hash : element count : filter pairs.</response>
	/// <response code="204">When the provided hash is the tip.</response>
	/// <response code="400">The provided hash was malformed or the count value is out of range</response>
	/// <response code="404">If the hash is not found. This happens at blockchain reorg.</response>
	[HttpGet("filters")]
	[ProducesResponseType(200)] // Note: If you add typeof(IList<string>) then swagger UI visualization will be ugly.
	[ProducesResponseType(204)]
	[ProducesResponseType(400)]
	[ProducesResponseType(404)]
	[ResponseCache(Duration = 60)]
	public IActionResult GetFilters([FromQuery, Required] string bestKnownBlockHash, [FromQuery, Required] int count)
	{
		if (count <= 0)
		{
			return BadRequest("Invalid block hash or count is provided.");
		}

		var knownHash = new uint256(bestKnownBlockHash);

		var (bestHeight, filters) = Global.IndexBuilderService.GetFilterLinesExcluding(knownHash, count, out bool found);

		if (!found)
		{
			return NotFound($"Provided {nameof(bestKnownBlockHash)} is not found: {bestKnownBlockHash}.");
		}

		if (!filters.Any())
		{
			return NoContent();
		}

		var response = new FiltersResponse
		{
			BestHeight = bestHeight,
			Filters = filters
		};

		return Ok(response);
	}

	[HttpGet("status")]
	[ProducesResponseType(typeof(StatusResponse), 200)]
	public async Task<StatusResponse> GetStatusAsync(CancellationToken cancellationToken)
	{
		try
		{
			var cacheKey = $"{nameof(GetStatusAsync)}";
			var cacheOptions = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(7) };

			return await Cache.GetCachedResponseAsync(
				cacheKey,
				action: (string request, CancellationToken token) => FetchStatusAsync(token),
				options: cacheOptions,
				cancellationToken);
		}
		catch (Exception ex)
		{
			Logger.LogDebug(ex);
			throw;
		}
	}

	private async Task<StatusResponse> FetchStatusAsync(CancellationToken cancellationToken = default)
	{
		StatusResponse status = new();

		// Updating the status of the filters.
		if (DateTimeOffset.UtcNow - Global.IndexBuilderService.LastFilterBuildTime > FilterTimeout)
		{
			// Checking if the last generated filter is created for one of the last two blocks on the blockchain.
			var lastFilter = Global.IndexBuilderService.GetLastFilter();
			var lastFilterHash = lastFilter.Header.BlockHash;
			var bestHash = await RpcClient.GetBestBlockHashAsync(cancellationToken);
			var lastBlockHeader = await RpcClient.GetBlockHeaderAsync(bestHash, cancellationToken);
			var prevHash = lastBlockHeader.HashPrevBlock;

			if (bestHash == lastFilterHash || prevHash == lastFilterHash)
			{
				status.FilterCreationActive = true;
			}
		}
		else
		{
			status.FilterCreationActive = true;
		}

		// Updating the status of WabiSabi coinjoin.
		if (Global.WabiSabiCoordinator is { } wabiSabiCoordinator)
		{
			var ww2CjDownAfter = TimeSpan.FromHours(3);
			var wabiSabiValidInterval = wabiSabiCoordinator.Config.StandardInputRegistrationTimeout * 2;
			if (wabiSabiValidInterval < ww2CjDownAfter)
			{
				wabiSabiValidInterval = ww2CjDownAfter;
			}
			if (DateTimeOffset.UtcNow - wabiSabiCoordinator.LastSuccessfulCoinJoinTime < wabiSabiValidInterval)
			{
				status.WabiSabiCoinJoinCreationActive = true;
			}
		}

		return status;
	}

	[HttpGet("unconfirmed-transaction-chain")]
	[ProducesResponseType(200)]
	[ProducesResponseType(400)]
	public async Task<IActionResult> GetUnconfirmedTransactionChainAsync([FromQuery, Required] string transactionId, CancellationToken cancellationToken)
	{
		try
		{
			var before = DateTimeOffset.UtcNow;
			uint256 txId = new(transactionId);

			var cacheKey = $"{nameof(GetUnconfirmedTransactionChainAsync)}_{txId}";
			var ret = await Cache.GetCachedResponseAsync(
				cacheKey,
				action: (string request, CancellationToken token) => GetUnconfirmedTransactionChainNoCacheAsync(txId, token),
				options: UnconfirmedTransactionChainCacheEntryOptions,
				cancellationToken);
			return ret;
		}
		catch (OperationCanceledException)
		{
			return BadRequest("Operation took more than 10 seconds. Aborting.");
		}
		catch (Exception ex)
		{
			Logger.LogDebug($"Failed to compute unconfirmed chain for {transactionId}. {ex}");
			return BadRequest($"Failed to compute unconfirmed chain for {transactionId}");
		}
	}

	private async Task<IActionResult> GetUnconfirmedTransactionChainNoCacheAsync(uint256 txId, CancellationToken cancellationToken)
	{
		var mempoolHashes = Mempool.GetMempoolHashes();
		if (!mempoolHashes.Contains(txId))
		{
			return BadRequest("Requested transaction is not present in the mempool, probably confirmed.");
		}

		using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(10));
		using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
		var linkedCancellationToken = linkedCts.Token;

		var unconfirmedTxsChainById = await BuildUnconfirmedTransactionChainAsync(txId, mempoolHashes, linkedCancellationToken);
		return Ok(unconfirmedTxsChainById.Values.ToList());
	}

	private async Task<Dictionary<uint256, UnconfirmedTransactionChainItemLegacy>> BuildUnconfirmedTransactionChainAsync(uint256 requestedTxId, IEnumerable<uint256> mempoolHashes, CancellationToken cancellationToken)
	{
		var unconfirmedTxsChainById = new Dictionary<uint256, UnconfirmedTransactionChainItemLegacy>();
		var toFetchFeeList = new List<uint256> { requestedTxId };

		while (toFetchFeeList.Count > 0)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var currentTxId = toFetchFeeList.First();

			// Check if we just computed the item.
			var cacheKey = $"{nameof(ComputeUnconfirmedTransactionChainItemAsync)}_{currentTxId}";

			var currentTxChainItem = await Cache.GetCachedResponseAsync(
				cacheKey,
				action: (string request, CancellationToken token) => ComputeUnconfirmedTransactionChainItemAsync(currentTxId, mempoolHashes, cancellationToken),
				options: UnconfirmedTransactionChainItemCacheEntryOptions,
				cancellationToken);

			toFetchFeeList.Remove(currentTxId);

			var discoveredTxsToFetchFee = currentTxChainItem.Parents
				.Union(currentTxChainItem.Children)
				.Where(x => !unconfirmedTxsChainById.ContainsKey(x) && !toFetchFeeList.Contains(x));

			toFetchFeeList.AddRange(discoveredTxsToFetchFee);

			unconfirmedTxsChainById.Add(currentTxId, currentTxChainItem);
		}

		return unconfirmedTxsChainById;
	}

	private async Task<UnconfirmedTransactionChainItemLegacy> ComputeUnconfirmedTransactionChainItemAsync(uint256 currentTxId, IEnumerable<uint256> mempoolHashes, CancellationToken cancellationToken)
	{
		var currentTx = (await FetchTransactionsAsync([currentTxId], cancellationToken).ConfigureAwait(false)).First();

		var txsToFetch = currentTx.Inputs.Select(input => input.PrevOut.Hash).Distinct().ToArray();

		Transaction[] parentTxs;
		try
		{
			parentTxs = await FetchTransactionsAsync(txsToFetch, cancellationToken).ConfigureAwait(false);
		}
		catch(AggregateException ex)
		{
			throw new InvalidOperationException($"Some transactions part of the chain were not found: {ex}");
		}

		// Get unconfirmed parents and children
		var unconfirmedParents = parentTxs.Where(x => mempoolHashes.Contains(x.GetHash())).ToHashSet();
		var unconfirmedChildrenTxs = Mempool.GetSpenderTransactions(currentTx.Outputs.Select((txo, index) => new OutPoint(currentTx, index))).ToHashSet();

		return new UnconfirmedTransactionChainItemLegacy(
			TxId: currentTxId,
			Size: currentTx.GetVirtualSize(),
			Fee: ComputeFee(currentTx, parentTxs, cancellationToken),
			Parents: unconfirmedParents.Select(x => x.GetHash()).ToHashSet(),
			Children: unconfirmedChildrenTxs.Select(x => x.GetHash()).ToHashSet());
	}

	private Money ComputeFee(Transaction currentTx, IEnumerable<Transaction> parentTxs, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var inputs = new List<Coin>();

		var prevOutsForCurrentTx = currentTx.Inputs
			.Select(input => input.PrevOut)
			.ToList();

		foreach (var prevOut in prevOutsForCurrentTx)
		{
			var parentTx = parentTxs.First(x => x.GetHash() == prevOut.Hash);
			var txOut = parentTx.Outputs[prevOut.N];
			inputs.Add(new Coin(prevOut, txOut));
		}

		return currentTx.GetFee(inputs.ToArray());
	}
}
