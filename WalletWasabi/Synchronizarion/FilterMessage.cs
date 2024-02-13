using System.IO;
using WalletWasabi.Backend.Models;
using WalletWasabi.Extensions;

namespace WalletWasabi.Synchronizarion;

public enum RequestMessage
{
	BestKnowBlockHash
}

public enum ResponseMessage
{
	Filter,
	HandshakeError
	HandshakeError,
	BlockHeight,
}

public record FilterMessage(FilterModel filterModel)
{
	public byte[] ToByteArray()
	{
		using var mem = new MemoryStream();
		using var writer = new BinaryWriter(mem);

		writer.Write((byte)ResponseMessage.Filter);
		writer.Write(filterModel);

		return mem.ToArray();
	}
}
