using NBitcoin;

namespace WalletWasabi.Exceptions;

public class InsufficientBalanceException : Exception
{
	public InsufficientBalanceException(Money minimum, Money actual) : base($"Needed: BTC {minimum.ToString(false, true)}, got only: BTC {actual.ToString(false, true)}.")
	{
		Minimum = minimum ?? Money.Zero;
		Actual = actual ?? Money.Zero;
	}

	public Money Minimum { get; }
	public Money Actual { get; }
}
