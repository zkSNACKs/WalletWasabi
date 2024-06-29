using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.Blockchain.Analysis.FeeEstimation;

public class ThirdPartyFeeProviderTests
{
	protected class TestFeeProvider : IThirdPartyFeeProvider
	{
		public int OutOfOrderUpdate { get; set; } = -1;

		public event EventHandler<AllFeeEstimate>? AllFeeEstimateArrived;

		public AllFeeEstimate? LastAllFeeEstimate { get; set; }
		public bool InError { get; set; }
		public bool IsPaused { get; set; }

		public void SendSimpleEstimate(int key, int value)
		{
			InError = false;
			AllFeeEstimate fees = new(new Dictionary<int, int>() { { key, value } });
			LastAllFeeEstimate = fees;
			AllFeeEstimateArrived?.Invoke(this, fees);
		}

		public void TriggerUpdate()
		{
			if (OutOfOrderUpdate > 0)
			{
				SendSimpleEstimate(2, OutOfOrderUpdate);
			}
		}
	}

	[Fact]
	public async void PriorityTestsAsync()
	{
		var feeProvider1 = new TestFeeProvider();
		var feeProvider2 = new TestFeeProvider();
		var feeProvider3 = new TestFeeProvider();

		using CancellationTokenSource cts = new CancellationTokenSource();
		using ThirdPartyFeeProvider thirdPartyFeeProvider = new(TimeSpan.FromSeconds(2), [feeProvider1, feeProvider2, feeProvider3]);
		thirdPartyFeeProvider.AdmitErrorTimeSpan = TimeSpan.FromSeconds(4);

		await thirdPartyFeeProvider.StartAsync(cts.Token);

		int result = 0;

		// we shouldn't move to error mode instantly
		feeProvider1.InError = true;
		feeProvider2.InError = true;
		thirdPartyFeeProvider.TriggerRound();
		Assert.False(thirdPartyFeeProvider.InError);

		// more than 4 sec ellapsed, time to move to error mode
		await Task.Delay(5000);
		thirdPartyFeeProvider.TriggerRound();
		Assert.True(thirdPartyFeeProvider.InError);

		// first result, accept it, not in error mode anymore
		feeProvider3.SendSimpleEstimate(2, 3);
		thirdPartyFeeProvider.LastAllFeeEstimate?.Estimations.TryGetValue(2, out result);
		Assert.False(thirdPartyFeeProvider.InError);
		Assert.Equal(3, result);

		// higher priority result, we should accept it
		feeProvider1.SendSimpleEstimate(2, 1);
		thirdPartyFeeProvider.LastAllFeeEstimate?.Estimations.TryGetValue(2, out result);
		Assert.Equal(1, result);

		// lower priority result, we shouldn't accept it
		result = 0;
		feeProvider2.SendSimpleEstimate(2, 2);
		thirdPartyFeeProvider.LastAllFeeEstimate?.Estimations.TryGetValue(2, out result);
		Assert.Equal(1, result);

		// TriggerOutOfOrderUpdate check
		feeProvider3.OutOfOrderUpdate = 4;
		feeProvider1.InError = true;
		feeProvider2.InError = true;
		feeProvider3.InError = true;
		await Task.Delay(5000);
		thirdPartyFeeProvider.TriggerRound();
		Assert.False(thirdPartyFeeProvider.InError);
		thirdPartyFeeProvider.LastAllFeeEstimate?.Estimations.TryGetValue(2, out result);
		Assert.Equal(4, result);

		await thirdPartyFeeProvider.StopAsync(cts.Token);
	}
}
