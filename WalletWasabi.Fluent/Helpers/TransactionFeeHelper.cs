using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NBitcoin;
using WalletWasabi.Fluent.ViewModels.Wallets.Send;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.Helpers;

public static class TransactionFeeHelper
{
	public const decimal FeePercentageThreshold = 130;

	private static readonly Dictionary<int, int> TestNetFeeEstimates = new()
	{
		[1] = 17,
		[2] = 12,
		[3] = 9,
		[6] = 9,
		[18] = 2,
		[36] = 2,
		[72] = 2,
		[144] = 2,
		[432] = 1,
		[1008] = 1
	};

	public static Dictionary<int, int> GetFeeEstimates(Wallet wallet)
	{
		if (wallet.FeeProvider.AllFeeEstimate is null)
		{
			throw new InvalidOperationException($"Not possible to get the fee estimates. {nameof(wallet.FeeProvider.AllFeeEstimate)} is null.");
		}

		return wallet.Network == Network.TestNet ? TestNetFeeEstimates : wallet.FeeProvider.AllFeeEstimate.Estimations;
	}

	public static bool AreTransactionFeesEqual(Wallet wallet)
	{
		var feeEstimates = GetFeeEstimates(wallet);

		var first = feeEstimates.First();
		var last = feeEstimates.Last();

		return first.Value == last.Value;
	}

	public static TimeSpan CalculateConfirmationTime(FeeRate feeRate, Wallet wallet)
	{
		var feeChartViewModel = new FeeChartViewModel();
		feeChartViewModel.UpdateFeeEstimates(GetFeeEstimates(wallet));
		var blockTarget = feeChartViewModel.GetConfirmationTarget(feeRate);

		return CalculateConfirmationTime(blockTarget);
	}

	public static TimeSpan CalculateConfirmationTime(double targetBlock)
	{
		var timeInMinutes = Math.Ceiling(targetBlock) * 10;
		var time = TimeSpan.FromMinutes(timeInMinutes);
		return time;
	}

	public static bool TryGetMaximumPossibleFeeRate(decimal percentageOfOverpayment, Wallet wallet, FeeRate currentFeeRate, out FeeRate maximumPossibleFeeRate)
	{
		var maxPossibleFeeRateInSatoshiPerByte = (currentFeeRate.SatoshiPerByte / percentageOfOverpayment) * 100;
		maximumPossibleFeeRate = new FeeRate(maxPossibleFeeRateInSatoshiPerByte);

		var feeChartViewModel = new FeeChartViewModel();
		feeChartViewModel.UpdateFeeEstimates(GetFeeEstimates(wallet));

		var blockTarget = feeChartViewModel.GetConfirmationTarget(maximumPossibleFeeRate);
		var newFeeRate = new FeeRate(feeChartViewModel.GetSatoshiPerByte(blockTarget));

		return newFeeRate <= maximumPossibleFeeRate;
	}
}
