using System.Collections.Generic;
using System.IO;
using NBitcoin;
using NBitcoin.RPC;
using WalletWasabi.Backend.Models;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Blockchain.Blocks;

namespace WalletWasabi.Extensions;

public static class StreamReaderWriterExtensions
{
	public static void Write(this BinaryWriter writer, uint256 val)
	{
		writer.Write(val.ToBytes());
	}

	public static uint256 ReadUInt256(this BinaryReader reader)
	{
		return new uint256(reader.ReadBytes(32));
	}

	public static void Write(this BinaryWriter writer, SmartHeader header)
	{
		writer.Write(header.BlockHash);
		writer.Write(header.PrevHash);
		writer.Write(header.Height);
		writer.Write(header.EpochBlockTime);
	}

	public static SmartHeader ReadSmartHeader(this BinaryReader reader)
	{
		var blockHash = reader.ReadUInt256();
		var prevBlockHash = reader.ReadUInt256();
		var height = reader.ReadUInt32();
		var epochBlockTime = reader.ReadInt64();
		return new SmartHeader(blockHash, prevBlockHash, height, epochBlockTime);
	}

	public static void Write(this BinaryWriter writer, GolombRiceFilter filter)
	{
		var bytes = filter.ToBytes();
		writer.Write(bytes.Length);
		writer.Write(bytes);
	}

	public static GolombRiceFilter ReadGRFilter(this BinaryReader reader)
	{
		var size = reader.ReadInt32();
		var data = reader.ReadBytes(size);
		return new GolombRiceFilter(data);
	}

	public static void Write(this BinaryWriter writer, FilterModel filterModel)
	{
		writer.Write(filterModel.Header);
		writer.Write(filterModel.Filter);
	}

	public static FilterModel ReadFilterModel(this BinaryReader reader)
	{
		return new FilterModel( reader.ReadSmartHeader(), reader.ReadGRFilter());
	}

	public static void Write(this BinaryWriter writer, AllFeeEstimate allFeeEstimate)
	{
		writer.Write(allFeeEstimate.Estimations.Count);
		foreach (var estimation in allFeeEstimate.Estimations)
		{
			writer.Write(estimation.Key);
			writer.Write(estimation.Value);
		}
	}

	public static AllFeeEstimate ReadMiningFeeRates(this BinaryReader reader)
	{
		var estimations = new Dictionary<int, int>();
		var count = reader.ReadInt32();
		for (var i = 0; i < count; i++)
		{
			estimations.Add(reader.ReadInt32(), reader.ReadInt32());
		}

		return new AllFeeEstimate(estimations);
	}
}
