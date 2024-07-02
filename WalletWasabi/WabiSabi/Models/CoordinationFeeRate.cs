using NBitcoin;
using WalletWasabi.Helpers;

namespace WalletWasabi.WabiSabi.Models;

public readonly struct CoordinationFeeRate
{
	public static readonly CoordinationFeeRate Zero = new(0, Money.Zero);

	public CoordinationFeeRate(decimal rate, Money plebsDontPayThreshold)
	{
		Rate = Guard.InRangeAndNotNull(nameof(rate), rate, 0m, Constants.AbsoluteMaxCoordinationFeeRate);
		PlebsDontPayThreshold = plebsDontPayThreshold ?? Money.Zero;
	}

	public decimal Rate { get; }
	public Money PlebsDontPayThreshold { get; }

	public Money GetFee(Money amount)
	{
		// Plebs don't have to pay.
		if (amount <= PlebsDontPayThreshold)
		{
			return Money.Zero;
		}
		else
		{
			return Money.Satoshis(Math.Floor(amount.Satoshi * Rate));
		}
	}
}
