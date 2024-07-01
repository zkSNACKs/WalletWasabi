using NBitcoin;

namespace WalletWasabi.Models;

public record CoordinatorConfigString(
	string Name,
	Network Network,
	Uri Endpoint,
	decimal CoordinatorFee,
	int AbsoluteMinInputCount,
	Uri ReadMore);
