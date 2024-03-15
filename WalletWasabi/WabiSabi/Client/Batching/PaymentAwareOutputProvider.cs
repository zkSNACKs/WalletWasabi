using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using WalletWasabi.Extensions;
using WalletWasabi.WabiSabi.Backend.Rounds;

namespace WalletWasabi.WabiSabi.Client.Batching;

// Represents an `OutputProvider` that has a reference to the `BatchedPayments` instance created by a `Wallet`.
// This class is then aware of the existence of payments for a `Wallet`, what allows it to provide outputs for
// coinjoin round which include those outputs that are payments.
public class PaymentAwareOutputProvider : OutputProvider
{
	public PaymentAwareOutputProvider(IDestinationProvider destinationProvider, PaymentBatch batchedPayments)
		: base(destinationProvider)
	{
		BatchedPayments = batchedPayments;
	}

	private PaymentBatch BatchedPayments { get; }

	public Money[] GetBatchedPaymentsAmounts => BatchedPayments.SumPendingPayments;

	public override IEnumerable<TxOut> GetOutputs(
		uint256 roundId,
		RoundParameters roundParameters,
		IEnumerable<Money> registeredCoinEffectiveValues,
		IEnumerable<Money> theirCoinEffectiveValues,
		int availableVsize)
	{
		// Get the best combination of payments that can be done with the current amount
		// registered in the round.
		var registeredValues = registeredCoinEffectiveValues.ToArray();
		var availableAmount = registeredValues.Sum();
		var bestPaymentSet = BatchedPayments.GetBestPaymentSet(availableAmount, availableVsize, roundParameters);

		// Return the payments.
		foreach (var payment in BatchedPayments.MovePaymentsToInProgress(bestPaymentSet.Payments, roundId))
		{
			yield return payment.ToTxOut();
		}
		availableVsize -= bestPaymentSet.TotalVSize;
		availableAmount -= bestPaymentSet.TotalAmount;

		// Decompose and return the rest. But before doing that it is important to minimize the impact
		// on the AmountDecomposer by removing those coins that sum enough to make the payments.
		var orderedValues = registeredValues.OrderDescending().ToArray();
		var usedValues = orderedValues
			.Scan(Money.Zero, (acc, current) => acc + current)
			.Zip(orderedValues, (s, v) => (Value: v, Sum: s))
			.TakeUntil(x => x.Sum > bestPaymentSet.TotalAmount)
			.Select(x => x.Value)
			.ToArray();

		var availableValues = orderedValues.Skip(usedValues.Length);

		// in case we over consumed money we reintroduce a virtual coin for the the difference.
		var totalValueUsedForPayment = availableAmount - availableValues.Sum();
		if (totalValueUsedForPayment > 0L)
		{
			availableValues = availableValues.Append(totalValueUsedForPayment).ToArray();
		}

		// Decompose the available values and return them.
		var decomposedOutputs = base.GetOutputs(roundId, roundParameters, availableValues, theirCoinEffectiveValues, availableVsize);
		foreach (var txOut in decomposedOutputs)
		{
			yield return txOut;
		}
	}
}
