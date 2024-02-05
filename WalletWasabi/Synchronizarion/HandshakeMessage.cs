using System.IO;
using NBitcoin;
using WalletWasabi.Extensions;

namespace WalletWasabi.Synchronizarion;

public record HandshakeMessage(uint256 bestKnownBlockHash)
{
	public byte[] ToByteArray()
	{
		using var mem = new MemoryStream();
		using var writer = new BinaryWriter(mem);

		writer.Write((byte)RequestMessage.BestKnowBlockHash);
		writer.Write(bestKnownBlockHash);

		return mem.ToArray();
	}
}
