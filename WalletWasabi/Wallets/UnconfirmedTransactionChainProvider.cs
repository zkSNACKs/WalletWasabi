using Microsoft.Extensions.Hosting;
using NBitcoin;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
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
		HttpClient = httpClientFactory.NewHttpClient(() => new Uri("https://mempool.space/api/"), Tor.Socks5.Pool.Circuits.Mode.NewCircuitPerRequest);
	}

	public event EventHandler<EventArgs>? RequestedUnconfirmedChainArrived;

	public ConcurrentDictionary<uint256, UnconfirmedTransactionChain> UnconfirmedChainCache { get; } = new();

	private IHttpClient HttpClient { get; }

	private Channel<uint256> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<uint256>();

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
					$"v1/cpfp/{txid}",
					null,
					linkedCts.Token).ConfigureAwait(false);

				if (response.StatusCode != HttpStatusCode.OK)
				{
					await response.ThrowRequestExceptionFromContentAsync(cancellationToken).ConfigureAwait(false);
				}

				var unconfirmedChain = await response.Content.ReadAsJsonAsync<UnconfirmedTransactionChain>().ConfigureAwait(false);

				if (!UnconfirmedChainCache.TryAdd(txid, unconfirmedChain))
				{
					throw new InvalidOperationException($"Failed to cache unconfirmed tx chain for {txid}");
				}

				RequestedUnconfirmedChainArrived?.Invoke(this, EventArgs.Empty);

				return;
			}
			catch (OperationCanceledException)
			{
				Logger.LogTrace("Request was cancelled by exiting the app.");
			}
			catch (Exception ex)
			{
				Logger.LogWarning($"Attempt: {i}. Failed to fetch transaction fee. {ex}");
			}
		}
	}

	public void CheckAndScheduleRequestIfNeeded(SmartTransaction tx)
	{
		if (!tx.Confirmed && tx.ForeignInputs.Count != 0 && !UnconfirmedChainCache.ContainsKey(tx.GetHash()))
		{
			Channel.Writer.TryWrite(tx.GetHash());
		}
	}

	protected override async Task ExecuteAsync(CancellationToken cancel)
	{
		List<Task> tasks = [];
		while (!cancel.IsCancellationRequested)
		{
			var txidToFetch = await Channel.Reader.ReadAsync(cancel).ConfigureAwait(false);

			tasks.Add(Task.Run(() => ScheduledTask(txidToFetch), cancel));

			if (tasks.Count >= MaximumRequestsInParallel)
			{
				Task completedTask = await Task.WhenAny(tasks).ConfigureAwait(false);
				tasks.Remove(completedTask);
			}
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

	public UnconfirmedTransactionChain? GetUnconfirmedTransactionChain(uint256 txId)
	{
		return UnconfirmedChainCache.TryGet(txId);
	}
}
