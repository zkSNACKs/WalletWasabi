using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using WalletWasabi.Extensions;
using WalletWasabi.Logging;
using WalletWasabi.WabiSabi.Backend.Rounds;

namespace WalletWasabi.WabiSabi.Client.Batching;

// Represents a collection of payments (
public class PaymentBatch
{
	private readonly List<Payment> _payments = new();

	private IEnumerable<PendingPayment> PendingPayments => _payments.OfType<PendingPayment>();
	private IEnumerable<InProgressPayment> InProgressPayments => _payments.OfType<InProgressPayment>();
	
	public void AddPendingPayment(PendingPayment payment)
	{
		_payments.Add(payment);
		Logger.LogInfo($"Payment for {payment.Amount} to {payment.Destination.ScriptPubKey.GetDestinationAddress(Network.TestNet)}");
	}

	public PaymentSet GetBestPaymentSet(Money availableAmount, int availableVsize, RoundParameters roundParameters)
	{
		// Not all payments are allowed. Wasabi coordinator only supports P2WPKH and Taproot
		// and even those depend on the round parameters.
		var allowedOutputTypes = roundParameters.AllowedOutputTypes;
		var allowedOutputAmounts = roundParameters.AllowedOutputAmounts;

		var allowedPayments = PendingPayments
			.Where(payment => payment.FitParameters(allowedOutputTypes, allowedOutputAmounts))
			.ToArray();

		// Once we know how much money we have registered in the coinjoin, lets see how many payments
		// we can do we that. Maximum 4 payments in a single coinjoin (arbitrary number)
		var allCombinationOfPayments = allowedPayments.CombinationsWithoutRepetition(1, 4);
		var bestPaymentSet = allCombinationOfPayments
			.Select(paymentSet => new PaymentSet(paymentSet, roundParameters.MiningFeeRate))
			.Where(paymentSet => paymentSet.TotalAmount <= availableAmount)
			.Where(paymentSet => paymentSet.TotalVSize < availableVsize)
			.DefaultIfEmpty(PaymentSet.Empty)
			.MaxBy(x => x.PaymentCount)!;

		LogPaymentSetDetails(bestPaymentSet);
		return bestPaymentSet;
	}

	public IEnumerable<InProgressPayment> MovePaymentsToInProgress(IEnumerable<PendingPayment> payments, uint256 roundId)
	{
		MovePaymentsTo(payments, p => p.ToInprogressPayment(roundId));
		return InProgressPayments;
	}

	public void MovePaymentsToFinished(uint256 txId) =>
		MovePaymentsTo(InProgressPayments, p => p.ToFinished(txId));

	public void MovePaymentsToPending() =>
		MovePaymentsTo(InProgressPayments, p => p.ToPending());
	
	private void MovePaymentsTo<TOldState, TNewState>(
		IEnumerable<TOldState> payments, 
		Func<TOldState, TNewState> move) where TOldState : Payment where TNewState : Payment
	{
		var paymentsToMove = payments.ToArray();
		foreach (var payment in paymentsToMove)
		{
			_payments.Remove(payment);
			_payments.Add(move (payment));
		}
	}
	
	private static void LogPaymentSetDetails(PaymentSet paymentSet)
	{
		Logger.LogInfo($"Best payment set contains {paymentSet.PaymentCount} payments");
		foreach (var payment in paymentSet.Payments)
		{
			Logger.LogInfo($"Id {payment.Id} to {payment.Destination.ScriptPubKey}  {payment.Amount.ToDecimal(MoneyUnit.BTC)}btc");
		}
	}
}
