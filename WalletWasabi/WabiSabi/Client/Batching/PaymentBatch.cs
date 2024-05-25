using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NBitcoin;
using WalletWasabi.Extensions;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.WabiSabi.Backend.Rounds;

namespace WalletWasabi.WabiSabi.Client.Batching;

// Represents a collection of payments.
// It is possible to add new (pending) payments to be embedded in a coinjoin.
//
// This class is able to select the best set of pending payments that can be done in
// the ongoing coinjoin round based on how much money was registered in it. The set
// of chosen set of payments is moved to in-progress state.
//
// Depending on whether a set of payments is done successfully or not all its belonging
// payments are moved to finished or back to pending state.
public class PaymentBatch
{
	private readonly List<Payment> _payments = new();
	private readonly object _syncObj = new();
	private IEnumerable<Payment> PendingPayments => GetPayments().Where(p => p.State is PendingPayment);
	private IEnumerable<Payment> InProgressPayments => GetPayments().Where(p => p.State is InProgressPayment);

	public Guid AddPayment(IDestination destination, Money amount)
	{
		var payment = new Payment(destination, amount);
		lock (_syncObj)
		{
			_payments.Add(payment);
		}
		Logger.LogInfo($"Payment {payment.Id} for BTC {payment.Amount} to {payment.Destination.ScriptPubKey}.");
		return payment.Id;
	}

	public void AbortPayment(Guid id)
	{
		lock (_syncObj)
		{
			if (_payments.FirstOrDefault(p => p.Id == id) is { } payment)
			{
				if (payment.State is PendingPayment)
				{
					_payments.Remove(payment);
					Logger.LogInfo($"Payment {payment.Id} for {payment.Amount} BTC to {payment.Destination.ScriptPubKey} was canceled.");
				}
				else
				{
					Logger.LogInfo($"Payment {payment.Id} could not be canceled because it is not pending.");
					throw new InvalidOperationException("Payment could not be canceled because it is not pending.");
				}
			}
			else
			{
				Logger.LogInfo($"Payment {id} was not found.");
				throw new InvalidOperationException("Payment was not found.");
			}
		}
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
		var allCombinationOfPendingPayments = allowedPayments.CombinationsWithoutRepetition(1, 4);
		var bestPaymentSet = allCombinationOfPendingPayments
			.Select(pendingPaymentSet => new PaymentSet(pendingPaymentSet, roundParameters.MiningFeeRate))
			.Where(paymentSet => paymentSet.TotalAmount <= availableAmount)
			.Where(paymentSet => paymentSet.TotalAmount == availableAmount // edge case where payments match exactly the available amount
				? paymentSet.TotalVSize <= availableVsize
				: paymentSet.TotalVSize + Math.Max(Constants.P2trOutputVirtualSize, Constants.P2wpkhOutputVirtualSize) <= availableVsize)
			.DefaultIfEmpty(PaymentSet.Empty)
			.MaxBy(x => x.PaymentCount)!;

		LogPaymentSetDetails(bestPaymentSet);
		return bestPaymentSet;
	}

	public IEnumerable<Payment> MovePaymentsToInProgress(IEnumerable<Payment> payments, uint256 roundId)
	{
		MovePaymentsTo(payments, payment => payment with { State = new InProgressPayment(payment.State, roundId) });
		return InProgressPayments;
	}

	public void MovePaymentsToFinished(uint256 txId) =>
		MovePaymentsTo(InProgressPayments, payment => payment with { State = new FinishedPayment(payment.State, txId) });

	public void MovePaymentsToPending() =>
		MovePaymentsTo(InProgressPayments, payment => payment with { State = new PendingPayment(payment.State) });

	public bool AreTherePendingPayments => PendingPayments.Any();

	private void MovePaymentsTo<TOldState, TNewState>(
		IEnumerable<TOldState> payments,
		Func<TOldState, TNewState> move) where TOldState : Payment where TNewState : Payment
	{
		lock (_syncObj)
		{
			var paymentsToMove = payments.ToArray();
			foreach (var payment in paymentsToMove)
			{
				_payments.Remove(payment);
				_payments.Add(move(payment));
			}
		}
	}

	public ReadOnlyCollection<Payment> GetPayments()
	{
		lock (_syncObj)
		{
			return _payments.AsReadOnly();
		}
	}

	private static void LogPaymentSetDetails(PaymentSet paymentSet)
	{
		Logger.LogInfo($"Best payment set contains {paymentSet.PaymentCount} payments.");
		foreach (var payment in paymentSet.Payments)
		{
			Logger.LogInfo($"Id {payment.Id} to {payment.Destination.ScriptPubKey}   BTC {payment.Amount.ToDecimal(MoneyUnit.BTC)}.");
		}
	}
}
