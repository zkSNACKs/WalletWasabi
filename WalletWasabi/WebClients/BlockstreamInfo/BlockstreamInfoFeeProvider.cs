using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Bases;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;

namespace WalletWasabi.WebClients.BlockstreamInfo;

public class BlockstreamInfoFeeProvider : PeriodicRunner, IThirdPartyFeeProvider
{
	public BlockstreamInfoFeeProvider(TimeSpan period, BlockstreamInfoClient blockstreamInfoClient) : base(period)
	{
		BlockstreamInfoClient = blockstreamInfoClient;
	}

	public event EventHandler<AllFeeEstimate>? AllFeeEstimateArrived;

	public BlockstreamInfoClient BlockstreamInfoClient { get; set; }
	public AllFeeEstimate? LastAllFeeEstimate { get; private set; }
	public bool InError { get; private set; } = false;
	public bool IsPaused { get; set; } = false;

	public void TriggerUpdate()
	{
		TriggerRound();
	}

	protected override async Task ActionAsync(CancellationToken cancel)
	{
		if (IsPaused)
		{
			return;
		}
		try
		{
			var allFeeEstimate = await BlockstreamInfoClient.GetFeeEstimatesAsync(cancel).ConfigureAwait(false);
			LastAllFeeEstimate = allFeeEstimate;

			if (allFeeEstimate.Estimations.Count != 0)
			{
				AllFeeEstimateArrived?.Invoke(this, allFeeEstimate);
			}

			InError = false;
		}
		catch
		{
			InError = true;
			throw;
		}
	}
}
