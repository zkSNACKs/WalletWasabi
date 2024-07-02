using System.ComponentModel;
using WalletWasabi.WabiSabi.Models;

namespace WalletWasabi.JsonConverters;

public class DefaultValueCoordinationFeeRateAttribute : DefaultValueAttribute
{
	public DefaultValueCoordinationFeeRateAttribute(double feeRate)
		: base(new CoordinationFeeRate((decimal)feeRate))
	{
	}
}
