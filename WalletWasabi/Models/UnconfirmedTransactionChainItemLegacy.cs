using System.Collections.Generic;
using NBitcoin;
using Newtonsoft.Json;
using WalletWasabi.JsonConverters;
using WalletWasabi.JsonConverters.Bitcoin;
using WalletWasabi.JsonConverters.Collections;

namespace WalletWasabi.Models;

// This class is here for backward compatibility and should be removed on removal of unconfirmed-transaction-chain endpoint.
public record UnconfirmedTransactionChainItemLegacy(
	[JsonProperty]
	[JsonConverter(typeof(Uint256JsonConverter))]
	uint256 TxId,
	[JsonProperty]
	int Size,
	[JsonProperty]
	[JsonConverter(typeof(MoneySatoshiJsonConverter))]
	Money Fee,
	[JsonProperty]
	[JsonConverter(typeof(HashSetUint256JsonConverter))]
	HashSet<uint256> Parents,
	[JsonProperty]
	[JsonConverter(typeof(HashSetUint256JsonConverter))]
	HashSet<uint256> Children);
