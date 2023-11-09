using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WalletWasabi.Helpers;

namespace WalletWasabi.JsonConverters;

public class TestNetBitcoinP2pEndPointConverterNg : EndPointJsonConverterNg
{
	public TestNetBitcoinP2pEndPointConverterNg()
		: base(Constants.DefaultTestNetBitcoinP2pPort)
	{
	}
}
