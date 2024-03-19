using System.IO;
using WalletWasabi.Extensions;

namespace WalletWasabi.Synchronizarion;

public record VersionMessage(Version clientVersion, Version backendVersion)
{
	public byte[] ToByteArray()
	{
		using var mem = new MemoryStream();
		using var writer = new BinaryWriter(mem);

		writer.Write((byte)ResponseMessage.SoftwareVersion);
		writer.Write(clientVersion);
		writer.Write(backendVersion);

		return mem.ToArray();
	}
}
