using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NBitcoin;
using NBitcoin.Policy;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Exceptions;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Wallets;

namespace WalletWasabi.Helpers;

public static class FeeHelpers
{
	public static bool TryGetMaxFeeRateForChangeless(
		Wallet wallet,
		IDestination destination,
		LabelsArray labels,
		FeeRate startingFeeRate,
		IEnumerable<SmartCoin> coins,
		[NotNullWhen(true)] out FeeRate? maxFeeRate,
		bool allowDoubleSpend = false,
		bool tryToSign = false)
	{
		maxFeeRate = SeekMaxFeeRate(
			startingFeeRate,
			feeRate => wallet.BuildChangelessTransaction(destination, labels, feeRate, coins, allowDoubleSpend, tryToSign));

		return maxFeeRate is not null;
	}

	public static bool TryGetMaxFeeRate(
		Wallet wallet,
		IDestination destination,
		Money amount,
		LabelsArray labels,
		FeeRate startingFeeRate,
		IEnumerable<SmartCoin> coins,
		bool subtractFee,
		[NotNullWhen(true)] out FeeRate? maxFeeRate,
		bool tryToSign = false)
	{
		maxFeeRate = SeekMaxFeeRate(
			startingFeeRate,
			feeRate => wallet.BuildTransaction(destination, amount, labels, feeRate, coins, subtractFee, null, tryToSign));

		return maxFeeRate is not null;
	}

	/// <summary>
	/// SeekMaxFeeRate iteratively searches for the highest feasible fee rate
	/// that allows the provided 'buildTransaction' action to succeed.
	/// If the action succeeded it increases the fee rate to try, if fails, it reduces.
	/// It repeats it until the difference between the last succeeded and last wrong fee rate is 0.001.
	/// </summary>
	/// <param name="feeRate">The initial fee rate to start the search from.</param>
	/// <param name="buildTransaction">The action to build a transaction with the given fee rate.</param>
	/// <returns>
	/// The maximum feasible fee rate discovered, or null if no suitable fee rate is found.
	/// </returns>
	private static FeeRate? SeekMaxFeeRate(FeeRate feeRate, Action<FeeRate> buildTransaction)
	{
		if (feeRate.SatoshiPerByte < 1m)
		{
			feeRate = new FeeRate(1m);
		}

		var lastWrongFeeRate = new FeeRate(0m);
		var lastCorrectFeeRate = new FeeRate(0m);

		var foundClosestSolution = false;
		while (!foundClosestSolution)
		{
			try
			{
				buildTransaction(feeRate);
				lastCorrectFeeRate = feeRate;
				var increaseBy = lastWrongFeeRate.SatoshiPerByte == 0 ? feeRate.SatoshiPerByte : (lastWrongFeeRate.SatoshiPerByte - feeRate.SatoshiPerByte) / 2;
				feeRate = new FeeRate(feeRate.SatoshiPerByte + increaseBy);
			}
			catch (Exception ex) when (ex is NotEnoughFundsException or TransactionFeeOverpaymentException or InsufficientBalanceException || (ex is InvalidTxException itx && itx.Errors.OfType<FeeTooHighPolicyError>().Any()))
			{
				if (feeRate.SatoshiPerByte == 1m)
				{
					break;
				}

				lastWrongFeeRate = feeRate;
				var decreaseBy = (feeRate.SatoshiPerByte - lastCorrectFeeRate.SatoshiPerByte) / 2;
				var nextSatPerByteCandidate = feeRate.SatoshiPerByte - decreaseBy;
				var newSatPerByte = nextSatPerByteCandidate < 1m ? 1m : nextSatPerByteCandidate; // make sure to always try 1 sat/vbyte as a last chance.
				feeRate = new FeeRate(newSatPerByte);
			}
			catch (Exception ex)
			{
				Logger.LogWarning(ex);
				return null;
			}

			foundClosestSolution = Math.Abs(lastWrongFeeRate.SatoshiPerByte - lastCorrectFeeRate.SatoshiPerByte) == 0.001m;
		}

		return foundClosestSolution ? lastCorrectFeeRate : null;
	}
}
