using System.IO;

namespace WalletWasabi.Synchronizarion;

public record BlockHeightMessage(uint height)
{
	public byte[] ToByteArray()
	{
		using var mem = new MemoryStream();
		using var writer = new BinaryWriter(mem);

		writer.Write((byte)ResponseMessage.BlockHeight);
		writer.Write(height);

		return mem.ToArray();
	}
}
