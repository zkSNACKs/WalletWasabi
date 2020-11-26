using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using WalletWasabi.Tor.Http;
using static WalletWasabi.Crypto.SchnorrBlinding;

namespace WalletWasabi.CoinJoin.Client.Clients
{
	public class AliceClient4 : AliceClientBase
	{
		internal AliceClient4(
			long roundId,
			IEnumerable<BitcoinAddress> registeredAddresses,
			IEnumerable<PubKey> signerPubKeys,
			IEnumerable<Requester> requesters,
			Network network,
			IRelativeHttpClient httpClient)
			: base(roundId, registeredAddresses, requesters, network, httpClient)
		{
			SignerPubKeys = signerPubKeys.ToArray();
		}

		public PubKey[] SignerPubKeys { get; }

		protected override PubKey GetSignerPubKey(int i)
		{
			return SignerPubKeys[i];
		}
	}
}
