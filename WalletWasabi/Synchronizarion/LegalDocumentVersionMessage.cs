using System.IO;
using WalletWasabi.Extensions;

namespace WalletWasabi.Synchronizarion;

public record LegalDocumentVersionMessage(Version version)
{
	public byte[] ToByteArray()
	{
		using var mem = new MemoryStream();
		using var writer = new BinaryWriter(mem);

		writer.Write((byte)ResponseMessage.LegalDocumentVersion);
		writer.Write(version);

		return mem.ToArray();
	}
}
