using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WalletWasabi.EventSourcing.Interfaces;

namespace WalletWasabi.EventSourcing
{
	public record WrappedEvent(long SequenceId, IEvent DomainEvent, Guid SourceId);
}
