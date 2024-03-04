using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin.RPC;
using WalletWasabi.Bases;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Extensions;
using WalletWasabi.Services;
using WalletWasabi.Services.Events;

namespace WalletWasabi.BitcoinCore.Monitoring;

public class RpcFeeProvider : PeriodicRunner
{
	public RpcFeeProvider(TimeSpan period, IRPCClient rpcClient, RpcMonitor rpcMonitor, EventBus eventBus) : base(period)
	{
		RpcClient = rpcClient;
		RpcMonitor = rpcMonitor;
		EventBus = eventBus;
	}

	private EventBus EventBus { get; }
	public IRPCClient RpcClient { get; set; }
	public RpcMonitor RpcMonitor { get; }
	public AllFeeEstimate? LastAllFeeEstimate { get; private set; }

	protected override async Task ActionAsync(CancellationToken cancel)
	{
		try
		{
			var allFeeEstimate = await RpcClient.EstimateAllFeeAsync(cancel).ConfigureAwait(false);

			LastAllFeeEstimate = allFeeEstimate;
			if (allFeeEstimate.Estimations.Any())
			{
				EventBus.Publish(new MiningFeeRatesChanged(FeeRateSource.LocalNodeRpc, allFeeEstimate));
			}
		}
		catch (NoEstimationException)
		{
			Logging.Logger.LogInfo("Couldn't get fee estimation from the Bitcoin node, probably because it was not yet initialized.");
		}
	}
}
