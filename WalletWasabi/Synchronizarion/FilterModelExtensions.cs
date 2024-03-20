using System.IO;
using WalletWasabi.Backend.Models;
using WalletWasabi.Extensions;

namespace WalletWasabi.Synchronizarion;

public static class FilterModelExtensions
{
	public static FilterModel FromByteArray(byte[] buffer)
	{
		using var mem = new MemoryStream(buffer);
		using var reader = new BinaryReader(mem);

		return reader.ReadFilterModel();
	}
}
