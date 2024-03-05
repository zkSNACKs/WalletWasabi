using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Extensions;

namespace WalletWasabi.Services;

public class MiningFeeRateFetcher : BackgroundService
{
	private readonly IRPCClient _rpcClient;
	private readonly EventBus _eventBus;

	public MiningFeeRateFetcher(IRPCClient rpcClient, EventBus eventBus)
	{
		_rpcClient = rpcClient;
		_eventBus = eventBus;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
		while (
			!stoppingToken.IsCancellationRequested &&
			await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
		{
			try
			{
				var allFeeEstimate = await _rpcClient.EstimateAllFeeAsync(stoppingToken).ConfigureAwait(false);
				_eventBus.Publish(allFeeEstimate);
			}
			catch
			{
				// ignored
			}
		}
	}
}

