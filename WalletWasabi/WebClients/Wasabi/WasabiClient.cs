using NBitcoin;
using NBitcoin.RPC;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Backend.Models.Responses;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Extensions;
using WalletWasabi.Logging;
using WalletWasabi.Services;
using WalletWasabi.Tor.Http;
using WalletWasabi.Tor.Http.Extensions;

namespace WalletWasabi.WebClients.Wasabi;

public class WasabiClient
{
	public WasabiClient(IHttpClient httpClient)
	{
		HttpClient = httpClient;
	}

	private IHttpClient HttpClient { get; }

	public static Dictionary<uint256, Transaction> TransactionCache { get; } = new();
	private static Queue<uint256> TransactionIdQueue { get; } = new();
	public static object TransactionCacheLock { get; } = new();
	public static ushort ApiVersion { get; private set; } = ushort.Parse(Helpers.Constants.WalletProtocolVersion);

	/// <remarks>
	/// Throws OperationCancelledException if <paramref name="cancel"/> is set.
	/// </remarks>
	public async Task<SynchronizeResponse> GetSynchronizeAsync(uint256 bestKnownBlockHash, int count, EstimateSmartFeeMode? estimateMode = null, CancellationToken cancel = default)
	{
		string relativeUri = $"api/v{ApiVersion}/wallet/synchronize?bestKnownBlockHash={bestKnownBlockHash}&maxNumberOfFilters={count}";
		if (estimateMode is { })
		{
			relativeUri = $"{relativeUri}&estimateSmartFeeMode={estimateMode}";
		}

		using HttpResponseMessage response = await HttpClient.SendAsync(HttpMethod.Get, relativeUri, cancellationToken: cancel).ConfigureAwait(false);

		if (response.StatusCode != HttpStatusCode.OK)
		{
			await response.ThrowRequestExceptionFromContentAsync(cancel).ConfigureAwait(false);
		}

		using HttpContent content = response.Content;
		var ret = await content.ReadAsJsonAsync<SynchronizeResponse>().ConfigureAwait(false);

		return ret;
	}

	public async Task<IEnumerable<Transaction>> GetTransactionsAsync(Network network, IEnumerable<uint256> txHashes, CancellationToken cancel)
	{
		var allTxs = new List<Transaction>();
		var txHashesToQuery = new List<uint256>();
		lock (TransactionCacheLock)
		{
			var cachedTxs = TransactionCache.Where(x => txHashes.Contains(x.Key));
			allTxs.AddRange(cachedTxs.Select(x => x.Value));
			txHashesToQuery.AddRange(txHashes.Except(cachedTxs.Select(x => x.Key)));
		}

		foreach (IEnumerable<uint256> chunk in txHashesToQuery.ChunkBy(10))
		{
			cancel.ThrowIfCancellationRequested();

			using HttpResponseMessage response = await HttpClient.SendAsync(
				HttpMethod.Get,
				$"api/v{ApiVersion}/wallet/transaction-hexes?&transactionIds={string.Join("&transactionIds=", chunk.Select(x => x.ToString()))}",
				cancellationToken: cancel).ConfigureAwait(false);

			if (response.StatusCode != HttpStatusCode.OK)
			{
				await response.ThrowRequestExceptionFromContentAsync(cancel).ConfigureAwait(false);
			}

			using HttpContent content = response.Content;
			var retString = await content.ReadAsJsonAsync<IEnumerable<string>>().ConfigureAwait(false);
			var ret = retString.Select(x => Transaction.Parse(x, network)).ToList();

			lock (TransactionCacheLock)
			{
				foreach (var tx in ret)
				{
					tx.PrecomputeHash(false, true);
					if (TransactionCache.TryAdd(tx.GetHash(), tx))
					{
						TransactionIdQueue.Enqueue(tx.GetHash());
						if (TransactionCache.Count > 1000) // No more than 1000 txs in cache
						{
							var toRemove = TransactionIdQueue.Dequeue();
							TransactionCache.Remove(toRemove);
						}
					}
				}
			}
			allTxs.AddRange(ret);
		}

		return allTxs.ToDependencyGraph().OrderByDependency();
	}

	public async Task<IEnumerable<uint256>> GetMempoolHashesAsync(CancellationToken cancel = default)
	{
		using HttpResponseMessage response = await HttpClient.SendAsync(
			HttpMethod.Get,
			$"api/v{ApiVersion}/wallet/mempool-hashes",
			cancellationToken: cancel).ConfigureAwait(false);

		if (response.StatusCode != HttpStatusCode.OK)
		{
			await response.ThrowRequestExceptionFromContentAsync(cancel).ConfigureAwait(false);
		}

		using HttpContent content = response.Content;
		var strings = await content.ReadAsJsonAsync<IEnumerable<string>>().ConfigureAwait(false);
		var ret = strings.Select(x => new uint256(x));

		return ret;
	}

	/// <summary>
	/// Gets mempool hashes, but strips the last x characters of each hash.
	/// </summary>
	/// <param name="compactness">1 to 64</param>
	public async Task<ISet<string>> GetMempoolHashesAsync(int compactness, CancellationToken cancel = default)
	{
		using HttpResponseMessage response = await HttpClient.SendAsync(
			HttpMethod.Get,
			$"api/v{ApiVersion}/wallet/mempool-hashes?compactness={compactness}",
			cancellationToken: cancel).ConfigureAwait(false);

		if (response.StatusCode != HttpStatusCode.OK)
		{
			await response.ThrowRequestExceptionFromContentAsync(cancel).ConfigureAwait(false);
		}

		using HttpContent content = response.Content;
		var strings = await content.ReadAsJsonAsync<ISet<string>>().ConfigureAwait(false);

		return strings;
	}

	public async Task<ushort> GetBackendMajorVersionAsync(CancellationToken cancel)
	{
		using HttpResponseMessage response = await HttpClient.SendAsync(HttpMethod.Get, $"api/v{ApiVersion}/wallet/versions", cancellationToken: cancel).ConfigureAwait(false);

		if (response.StatusCode != HttpStatusCode.OK)
		{
			await response.ThrowRequestExceptionFromContentAsync(cancel).ConfigureAwait(false);
		}

		using HttpContent content = response.Content;
		var resp = await content.ReadAsJsonAsync<VersionsResponse>().ConfigureAwait(false);

		return ushort.Parse(resp.BackendMajorVersion);
	}

	public async Task<bool> CheckUpdatesAsync(CancellationToken cancel)
	{
		ushort backendMajorVersion;
		try
		{
			 backendMajorVersion = await GetBackendMajorVersionAsync(cancel).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Logger.LogWarning($"Could not get the backend major version: {ex}");
			throw;
		}

		// If ClientSupportBackendVersionMin <= backend major <= ClientSupportBackendVersionMax, then our software is compatible.
		var backendCompatible = int.Parse(Helpers.Constants.ClientSupportBackendVersionMax) >= backendMajorVersion && backendMajorVersion >= int.Parse(Helpers.Constants.ClientSupportBackendVersionMin);
		var currentBackendMajorVersion = backendMajorVersion;

		if (backendCompatible)
		{
			// Only refresh if compatible.
			ApiVersion = currentBackendMajorVersion;
		}

		return backendCompatible;
	}
}
