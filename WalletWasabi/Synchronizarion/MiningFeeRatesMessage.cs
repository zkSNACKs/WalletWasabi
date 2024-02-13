using System.IO;
using WalletWasabi.Extensions;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;

namespace WalletWasabi.Synchronizarion;

public record MiningFeeRatesMessage(AllFeeEstimate allFeeEstimate)
{
	public byte[] ToByteArray()
	{
		using var mem = new MemoryStream();
		using var writer = new BinaryWriter(mem);

		writer.Write((byte)ResponseMessage.MiningFeeRates);
		writer.Write(allFeeEstimate);

		return mem.ToArray();
	}
}
