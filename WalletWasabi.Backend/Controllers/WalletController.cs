using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.RPC;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using WalletWasabi.Backend.Models;
using WalletWasabi.Backend.Models.Responses;
using WalletWasabi.BitcoinCore.Mempool;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.BlockFilters;
using WalletWasabi.Cache;
using WalletWasabi.Helpers;
using WalletWasabi.JsonConverters;
using WalletWasabi.Logging;
using WalletWasabi.Models;

namespace WalletWasabi.Backend.Controllers;

/// <summary>
/// To make batched requests.
/// </summary>
[Produces("application/json")]
[Route("api/v" + Constants.WalletProtocolVersion + "/[controller]")]
public class WalletController : ControllerBase
{
	private static readonly MemoryCacheEntryOptions UnconfirmedTransactionChainCacheEntryOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10) };
	private static readonly MemoryCacheEntryOptions UnconfirmedTransactionChainItemCacheEntryOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10) };
	private static readonly MemoryCacheEntryOptions TransactionCacheOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(20) };

	private static readonly VersionsResponse VersionsResponse = new()
	{
		BackendMajorVersion = Constants.BackendMajorVersion,
		CommitHash = GetCommitHash()
	};

	public WalletController(IRPCClient rpcClient, IndexBuilderService indexBuilderService, MempoolMirror mempoolMirror, IMemoryCache memoryCache)
	{
		Cache = new(memoryCache);
		RpcClient = rpcClient;
		IndexBuilderService = indexBuilderService;
		Mempool = mempoolMirror;
	}

	private IRPCClient RpcClient { get; }
	private IndexBuilderService IndexBuilderService { get; }
	private MempoolMirror Mempool { get; }
	private IdempotencyRequestCache Cache { get; }

	/// <summary>
	/// Gets the latest versions of the client and backend.
	/// </summary>
	/// <returns>ClientVersion, BackendMajorVersion.</returns>
	/// <response code="200">ClientVersion, BackendMajorVersion.</response>
	[HttpGet("versions")]
	[ProducesResponseType(typeof(VersionsResponse), 200)]
	public VersionsResponse GetVersions()
	{
		return VersionsResponse;
	}

	private static string GetCommitHash() =>
		ReflectionUtils.GetAssemblyMetadata("CommitHash") ?? "";

	[HttpGet("synchronize")]
	[ResponseCache(Duration = 60)]
	public IActionResult GetSynchronize([FromQuery, Required] string bestKnownBlockHash)
	{
		if (!uint256.TryParse(bestKnownBlockHash, out var knownHash))
		{
			return BadRequest($"Invalid {nameof(bestKnownBlockHash)}.");
		}

		var numberOfFilters = RpcClient.Network == Network.Main ? 1000 : 10000;
		(Height bestHeight, IEnumerable<FilterModel> filters) = IndexBuilderService.GetFilterLinesExcluding(knownHash, numberOfFilters, out bool found);

		var response = new SynchronizeResponse { Filters = Enumerable.Empty<FilterModel>(), BestHeight = bestHeight };

		if (!found)
		{
			response.FiltersResponseState = FiltersResponseState.BestKnownHashNotFound;
		}
		else if (!filters.Any())
		{
			response.FiltersResponseState = FiltersResponseState.NoNewFilter;
		}
		else
		{
			response.FiltersResponseState = FiltersResponseState.NewFilters;
			response.Filters = filters;
		}

		return Ok(response);
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
			action: (request, token) => GetRawMempoolStringsNoCacheAsync(token),
			options: cacheOptions,
			cancellationToken);
	}

	private async Task<IEnumerable<string>> GetRawMempoolStringsNoCacheAsync(CancellationToken cancellationToken = default)
	{
		uint256[] transactionHashes = await RpcClient.GetRawMempoolAsync(cancellationToken).ConfigureAwait(false);
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
				// It's necessary to always set a result to the task completion sources. Otherwise, cache can get corrupted.
				Exception ex = new InvalidOperationException("Failed to get the transaction.");
				foreach ((uint256 txid, TaskCompletionSource<Transaction> tcs) in txIdsRetrieve)
				{
					if (!tcs.Task.IsCompleted)
					{
						// Prefer new cache requests to try again rather than getting the exception. The window is small though.
						Cache.Remove(txid);
						tcs.SetException(ex);
					}
				}
			}
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
			transaction = Transaction.Parse(hex, RpcClient.Network);
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
			var spenders = Mempool.GetSpenderTransactions(transaction.Inputs.Select(x => x.PrevOut));
			return BadRequest($"{ex.Message}:::{string.Join(":::", spenders.Select(x => x.ToHex()))}");
		}

		return Ok("Transaction is successfully broadcasted.");
	}

	[HttpGet("unconfirmed-transaction-chain")]
	[ProducesResponseType(200)]
	[ProducesResponseType(400)]
	public async Task<IActionResult> GetUnconfirmedTransactionChainAsync([FromQuery, Required] string transactionId, CancellationToken cancellationToken)
	{
		try
		{
			uint256 txId = new(transactionId);

			var cacheKey = $"{nameof(GetUnconfirmedTransactionChainAsync)}_{txId}";
			var ret = await Cache.GetCachedResponseAsync(
				cacheKey,
				action: (request, token) => GetUnconfirmedTransactionChainNoCacheAsync(txId, token),
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

	private async Task<Dictionary<uint256, UnconfirmedTransactionChainItem>> BuildUnconfirmedTransactionChainAsync(uint256 requestedTxId, IEnumerable<uint256> mempoolHashes, CancellationToken cancellationToken)
	{
		var unconfirmedTxsChainById = new Dictionary<uint256, UnconfirmedTransactionChainItem>();
		var toFetchFeeList = new List<uint256> { requestedTxId };

		while (toFetchFeeList.Count > 0)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var currentTxId = toFetchFeeList.First();

			// Check if we just computed the item.
			var cacheKey = $"{nameof(ComputeUnconfirmedTransactionChainItemAsync)}_{currentTxId}";

			var currentTxChainItem = await Cache.GetCachedResponseAsync(
				cacheKey,
				action: (request, token) => ComputeUnconfirmedTransactionChainItemAsync(currentTxId, mempoolHashes, token),
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

	private async Task<UnconfirmedTransactionChainItem> ComputeUnconfirmedTransactionChainItemAsync(uint256 currentTxId, IEnumerable<uint256> mempoolHashes, CancellationToken cancellationToken)
	{
		var currentTx = (await FetchTransactionsAsync([currentTxId], cancellationToken).ConfigureAwait(false)).FirstOrDefault() ?? throw new InvalidOperationException("Tx not found");

		var txsToFetch = currentTx.Inputs.Select(input => input.PrevOut.Hash).Distinct().ToArray();

		var parentTxs = await FetchTransactionsAsync(txsToFetch, cancellationToken).ConfigureAwait(false);

		// Get unconfirmed parents and children
		var unconfirmedParents = parentTxs.Where(x => mempoolHashes.Contains(x.GetHash())).ToHashSet();
		var unconfirmedChildrenTxs = Mempool.GetSpenderTransactions(currentTx.Outputs.Select((txo, index) => new OutPoint(currentTx, index))).ToHashSet();

		return new UnconfirmedTransactionChainItem(
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
