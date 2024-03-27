using Microsoft.Extensions.Hosting;
using NBitcoin;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Tor.Http;
using WalletWasabi.Tor.Http.Extensions;
using WalletWasabi.WebClients.Wasabi;

namespace WalletWasabi.Wallets;

public class UnconfirmedTransactionChainProvider : BackgroundService
{
	private const int MaximumDelayInSeconds = 120;
	private const int MaximumRequestsInParallel = 3;

	public UnconfirmedTransactionChainProvider(WasabiHttpClientFactory httpClientFactory)
	{
		HttpClient = httpClientFactory.NewHttpClient(httpClientFactory.BackendUriGetter, Tor.Socks5.Pool.Circuits.Mode.NewCircuitPerRequest);
	}

	public ConcurrentDictionary<uint256, List<UnconfirmedTransactionChainItem>> UnconfirmedChainCache { get; } = new();
	public ConcurrentQueue<uint256> Queue { get; } = new();
	private SemaphoreSlim Semaphore { get; } = new(initialCount: 0, maxCount: MaximumRequestsInParallel);
	private IHttpClient HttpClient { get; }

	private async Task FetchUnconfirmedTransactionChainAsync(uint256 txid, CancellationToken cancellationToken)
	{
		const int MaxAttempts = 3;

		for (int i = 0; i < MaxAttempts; i++)
		{
			try
			{
				using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(60));
				using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

				var response = await HttpClient.SendAsync(
					HttpMethod.Get,
					$"api/v{Helpers.Constants.BackendMajorVersion}/btc/Blockchain/unconfirmed-transaction-chain?transactionId={txid}",
					null,
					linkedCts.Token).ConfigureAwait(false);

				if (response.StatusCode != HttpStatusCode.OK)
				{
					await response.ThrowRequestExceptionFromContentAsync(cancellationToken).ConfigureAwait(false);
				}

				var unconfirmedChain = await response.Content.ReadAsJsonAsync<UnconfirmedTransactionChainItem[]>().ConfigureAwait(false);

				if (!UnconfirmedChainCache.TryAdd(txid, unconfirmedChain.ToList()))
				{
					throw new InvalidOperationException($"Failed to cache unconfirmed tx chain for {txid}");
				}
			}
			catch (Exception ex)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					return;
				}

				Logger.LogWarning($"Attempt: {i}. Failed to fetch transaction fee. {ex}");
			}
		}
	}

	public void BeginRequestUnconfirmedChain(SmartTransaction tx)
	{
		if (!tx.Confirmed && tx.ForeignInputs.Count != 0 && !Queue.Any(x => x == tx.GetHash()) && !UnconfirmedChainCache.ContainsKey(tx.GetHash()))
		{
			Queue.Enqueue(tx.GetHash());
			Semaphore.Release(1);
		}
	}

	protected override async Task ExecuteAsync(CancellationToken cancel)
	{
		while (!cancel.IsCancellationRequested)
		{
			await Semaphore.WaitAsync(cancel).ConfigureAwait(false);

			if (!Queue.TryDequeue(out var txidToFetch))
			{
				continue;
			}

			_ = ScheduledTask(txidToFetch);
		}

		async Task ScheduledTask(uint256 txid)
		{
			var random = new Random();
			var delayInSeconds = random.Next(MaximumDelayInSeconds);
			var delay = TimeSpan.FromSeconds(delayInSeconds);

			try
			{
				await Task.Delay(delay, cancel).ConfigureAwait(false);

				await FetchUnconfirmedTransactionChainAsync(txid, cancel).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				Logger.LogTrace("Request was cancelled by exiting the app.");
			}
			catch (Exception e)
			{
				Logger.LogWarning(e);
			}
		}
	}

	public override void Dispose()
	{
		Semaphore.Dispose();
		base.Dispose();
	}
}
