using WalletWasabi.Blockchain.Analysis.FeesEstimation;

namespace WalletWasabi.Services.Events;

public record ExchangeRateChanged(decimal UsdBtcRate);

public enum FeeRateSource
{
	Backend,
	LocalNodeRpc,
}

public record MiningFeeRatesChanged(FeeRateSource Source, AllFeeEstimate AllFeeEstimate);
public record ServerTipHeightChanged(uint Height);
public record ConnectionStateChanged(bool Connected);
