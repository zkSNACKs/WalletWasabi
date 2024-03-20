using System.IO;
using WalletWasabi.Backend.Models;

namespace WalletWasabi.Synchronizarion;

public record ExchangeRateMessage(ExchangeRate exchangeRate)
{
	public byte[] ToByteArray()
	{
		using var mem = new MemoryStream();
		using var writer = new BinaryWriter(mem);

		writer.Write((byte)ResponseMessage.ExchangeRate);
		writer.Write(exchangeRate.Rate);

		return mem.ToArray();
	}
}
