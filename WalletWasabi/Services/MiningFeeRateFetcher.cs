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
	private AllFeeEstimate? _lastFeeEstimate;

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
			var allFeeEstimate = await _rpcClient.EstimateAllFeeAsync(stoppingToken).ConfigureAwait(false);
			if (_lastFeeEstimate != allFeeEstimate)
			{
				_lastFeeEstimate = allFeeEstimate;
				_eventBus.Publish(allFeeEstimate);
			}
		}
	}
}

