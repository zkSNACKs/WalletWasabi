using System;

namespace WalletWasabi.Daemon.BitcoinCore.Processes;

public class BitcoindException : Exception
{
	public BitcoindException(string message) : base(message)
	{
	}

	public BitcoindException(string message, Exception innerException) : base(message, innerException)
	{
	}
}
