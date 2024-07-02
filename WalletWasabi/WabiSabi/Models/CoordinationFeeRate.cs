using NBitcoin;

namespace WalletWasabi.WabiSabi.Models;

public readonly struct CoordinationFeeRate
{
	public static readonly CoordinationFeeRate Zero = new(0);

	public CoordinationFeeRate(decimal rate)
	{
		Rate = rate >= 0 ? rate : throw new ArgumentOutOfRangeException(nameof(rate));
	}

	public decimal Rate { get; }

	public Money GetFee(Money amount)
	{
		return Money.Satoshis(Math.Floor(amount.Satoshi * Rate));
	}
}
