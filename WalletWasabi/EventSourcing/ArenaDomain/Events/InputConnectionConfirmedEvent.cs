using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.Crypto;
using WalletWasabi.EventSourcing.Interfaces;

namespace WalletWasabi.EventSourcing.ArenaDomain.Events
{
	public record InputConnectionConfirmedEvent(Guid AliceId, Coin Coin, OwnershipProof OwnershipProof) : IEvent, IRoundClientEvent;
}
