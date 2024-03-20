using Microsoft.Extensions.Hosting;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Logging;
using WalletWasabi.Services;
using WalletWasabi.Services.Events;

namespace WalletWasabi.Blockchain.Analysis.FeesEstimation;

/// <summary>
/// Manages multiple fee sources. Returns the best one.
/// Prefers local full node, as long as the fee is accurate.
/// </summary>
public class HybridFeeProvider : IHostedService
{
	public HybridFeeProvider(EventBus eventBus)
	{
		EventBus = eventBus;
		MiningFeeRatesChangedSubscription = EventBus.Subscribe<MiningFeeRatesChanged>(OnAllFeeEstimateArrived);
	}

	public event EventHandler<AllFeeEstimate>? AllFeeEstimateChanged;

	private object Lock { get; } = new();
	public AllFeeEstimate? AllFeeEstimate { get; private set; }
	private EventBus EventBus { get; }
	private IDisposable MiningFeeRatesChangedSubscription { get; set; }

	public Task StartAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}


	public Task StopAsync(CancellationToken cancellationToken)
	{
		MiningFeeRatesChangedSubscription.Dispose();
		return Task.CompletedTask;
	}

	private void OnAllFeeEstimateArrived(MiningFeeRatesChanged e)
	{
		// Only go further if we have estimations.
		if (e.AllFeeEstimate.Estimations.Any() is not true)
		{
			return;
		}

		lock (Lock)
		{
			if (AllFeeEstimate == e.AllFeeEstimate)
			{
				return;
			}
			AllFeeEstimate = e.AllFeeEstimate;
		}

		var from = e.AllFeeEstimate.Estimations.First();
		var to = e.AllFeeEstimate.Estimations.Last();
		var sender = Enum.GetName(e.Source);
		Logger.LogInfo($"Fee rates are acquired from {sender} ranging from target {from.Key} blocks at {from.Value} sat/vByte to target {to.Key} blocks at {to.Value} sat/vByte.");
		AllFeeEstimateChanged?.Invoke(this, e.AllFeeEstimate);
	}
}
