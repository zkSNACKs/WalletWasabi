using System.Collections.Generic;
using NBitcoin;
using Newtonsoft.Json;
using WalletWasabi.JsonConverters;

namespace WalletWasabi.Models;

public record UnconfirmedTransactionChainItem
{
	[JsonConverter(typeof(Uint256JsonConverter))]
	public uint256 txid { get; set; }
	public long fee { get; set; }
	public long weight { get; set; }
}

public record UnconfirmedTransactionChain
{
	public List<UnconfirmedTransactionChainItem> ancestors { get; set; }
	public UnconfirmedTransactionChainItem bestDescendant { get; set; }
	public List<UnconfirmedTransactionChainItem> descendants { get; set; }
	public double effectiveFeePerVsize { get; set; }
	public int sigops { get; set; }
	public double adjustedVsize { get; set; }
}

