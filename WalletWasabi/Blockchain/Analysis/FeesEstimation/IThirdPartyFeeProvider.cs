namespace WalletWasabi.Blockchain.Analysis.FeesEstimation;

public interface IThirdPartyFeeProvider
{
	event EventHandler<AllFeeEstimate>? AllFeeEstimateArrived;

	AllFeeEstimate? LastAllFeeEstimate { get; }
	bool InError { get; }
	bool IsPaused { get; set; }

	public void TriggerUpdate();
}
