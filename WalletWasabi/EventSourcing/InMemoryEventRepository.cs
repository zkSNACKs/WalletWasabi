using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using WalletWasabi.Exceptions;
using WalletWasabi.Helpers;
using WalletWasabi.Interfaces.EventSourcing;

namespace WalletWasabi.EventSourcing
{
	/// <summary>
	/// Thread safe without locks in memory event repository implementation
	/// </summary>
	public class InMemoryEventRepository : IEventRepository
	{
		protected static readonly IReadOnlyList<WrappedEvent> EmptyResult = Array.Empty<WrappedEvent>().ToList().AsReadOnly();
		protected static readonly IReadOnlyList<string> EmptyIds = Array.Empty<string>().ToList().AsReadOnly();

		private readonly ConcurrentDictionary<
			// aggregateType
			string,
			ConcurrentDictionary<
				// aggregateId
				string,
				(
					// SequenceId of the last event of this aggregate
					long TailSequenceId,

					// Locked flag for appending into EventsBatches of this aggregate
					WriteLockEnum LockState,

					// List of lists of events for atomic insertion of multiple events
					// in one "database transaction"
					ConcurrentQueue<IReadOnlyList<WrappedEvent>> EventsBatches
				)
			>
		> _aggregatesEventsBatches = new();

		private readonly ConcurrentDictionary<
			// aggregateType
			string,
			(
				// Index of the last aggregateId in this aggregateType
				long TailIndex,
				ConcurrentDictionary<
					// Index of aggregateId in this aggregateType
					long,
					// aggregateId
					string> Ids
			)
		> _aggregatesIds = new();

		protected enum WriteLockEnum
		{
			Unlocked,
			WritingLocked,
		}

		public Task AppendEventsAsync(
			string aggregateType,
			string aggregateId,
			IEnumerable<WrappedEvent> wrappedEvents)
		{
			Guard.NotNullOrEmpty(nameof(aggregateType), aggregateType);
			Guard.NotNullOrEmpty(nameof(aggregateId), aggregateId);
			Guard.NotNull(nameof(wrappedEvents), wrappedEvents);
			var wrappedEventsList = wrappedEvents.ToList().AsReadOnly();
			if (wrappedEventsList.Count <= 0)
			{
				return Task.CompletedTask;
			}
			var firstSequenceId = wrappedEventsList[0].SequenceId;
			var lastSequenceId = wrappedEventsList[^1].SequenceId;
			if (firstSequenceId <= 0)
			{
				throw new ArgumentException("First event sequenceId is not natural number.", nameof(wrappedEvents));
			}
			if (lastSequenceId <= 0)
			{
				throw new ArgumentException("Last event sequenceId is not natural number.", nameof(wrappedEvents));
			}
			if (lastSequenceId - firstSequenceId + 1 != wrappedEventsList.Count)
			{
				throw new ArgumentException("Event sequence ids are out of whack.", nameof(wrappedEvents));
			}

			var aggregateEventsBatches = _aggregatesEventsBatches.GetOrAdd(aggregateType, _ => new());
			var (tailSequenceId, locked, eventsBatches) = aggregateEventsBatches.GetOrAdd(aggregateId, _ => (0, WriteLockEnum.Unlocked, new()));

			if (tailSequenceId + 1 < firstSequenceId)
			{
				throw new ArgumentException($"Invalid firstSequenceId (gap in sequence ids) expected: '{tailSequenceId + 1}' given: '{firstSequenceId}'.", nameof(wrappedEvents));
			}

			// no action
			Validated();

			// Atomically detect conflict and replace lastSequenceId and lock to ensure strong order in eventsBatches.
			if (!aggregateEventsBatches.TryUpdate(
				key: aggregateId,
				newValue: (lastSequenceId, WriteLockEnum.WritingLocked, eventsBatches),
				comparisonValue: (firstSequenceId - 1, WriteLockEnum.Unlocked, eventsBatches)))
			{
				Conflicted(); // no action
				throw new OptimisticConcurrencyException($"Conflict while commiting events. Retry command. aggregate: '{aggregateType}' id: '{aggregateId}'");
			}
			try
			{
				Locked(); // no action
				eventsBatches.Enqueue(wrappedEventsList);
				Appended(); // no action
			}
			finally
			{
				// Unlock.
				if (!aggregateEventsBatches.TryUpdate(
					key: aggregateId,
					newValue: (lastSequenceId, WriteLockEnum.Unlocked, eventsBatches),
					comparisonValue: (lastSequenceId, WriteLockEnum.WritingLocked, eventsBatches)))
				{
					throw new AssertionFailedException("Unexpected failure to unlock.");
				}
				Unlocked(); // no action
			}

			// If it is a first event for given aggregate.
			if (tailSequenceId == 0)
			{ // Add index of aggregate id into the dictionary.
				IndexNewAggregateId(aggregateType, aggregateId);
			}
			return Task.CompletedTask;
		}

		public Task<IReadOnlyList<WrappedEvent>> ListEventsAsync(
			string aggregateType,
			string aggregateId,
			long afterSequenceId = 0,
			int? limit = null)
		{
			if (_aggregatesEventsBatches.TryGetValue(aggregateType, out var aggregateEventsBatches) &&
				aggregateEventsBatches.TryGetValue(aggregateId, out var value))
			{
				var result = value.EventsBatches.SelectMany(a => a);
				if (afterSequenceId > 0)
				{
					result = result.Where(a => afterSequenceId < a.SequenceId);
				}
				if (limit.HasValue)
				{
					result = result.Take(limit.Value);
				}
				return Task.FromResult((IReadOnlyList<WrappedEvent>)result.ToList().AsReadOnly());
			}
			return Task.FromResult(EmptyResult);
		}

		public Task<IReadOnlyList<string>> ListAggregateIdsAsync(
			string aggregateType,
			string? afterAggregateId = null,
			int? limit = null)
		{
			if (afterAggregateId != null)
			{
				throw new NotImplementedException();
			}
			limit ??= int.MaxValue;
			if (_aggregatesIds.TryGetValue(aggregateType, out var tuple))
			{
				var tailIndex = tuple.TailIndex;
				var ids = tuple.Ids;
				var result = new List<string>();
				for (var i = 1L; i <= tailIndex && result.Count < limit; i++)
				{
					if (!ids.TryGetValue(i, out var id))
					{
						throw new AssertionFailedException($"Unexpected failure to get aggregate id. aggregate type: '{aggregateType}' index: '{i}'");
					}
					result.Add(id);
				}
				return Task.FromResult((IReadOnlyList<string>)result.AsReadOnly());
			}
			return Task.FromResult(EmptyIds);
		}

		private void IndexNewAggregateId(string aggregateType, string id)
		{
			var tailIndex = 0L;
			ConcurrentDictionary<long, string> aggregateIds;
			var liveLockLimit = 10000;
			do
			{
				if (liveLockLimit-- <= 0)
				{
					throw new ApplicationException("Live lock detected.");
				}
				(tailIndex, aggregateIds) = _aggregatesIds.GetOrAdd(aggregateType,
					_ => new(0, new()));
			}
			while (!_aggregatesIds.TryUpdate(
				key: aggregateType,
				newValue: (tailIndex + 1, aggregateIds),
				comparisonValue: (tailIndex, aggregateIds)));
			if (!aggregateIds.TryAdd(tailIndex + 1, id))
			{
				throw new AssertionFailedException("Unexpected failure to add aggregate id to index.");
			}
		}

		// Helper for parallel critical section testing in DEBUG build only.
		[Conditional("DEBUG")]
		protected virtual void Validated()
		{
		}

		// Helper for parallel critical section testing in DEBUG build only.
		[Conditional("DEBUG")]
		protected virtual void Conflicted()
		{
		}

		// Helper for parallel critical section testing in DEBUG build only.
		[Conditional("DEBUG")]
		protected virtual void Locked()
		{
		}

		// helper for parallel critical section testing in DEBUG build only.
		[Conditional("DEBUG")]
		protected virtual void Appended()
		{
		}

		// Helper for parallel critical section testing in DEBUG build only.
		[Conditional("DEBUG")]
		protected virtual void Unlocked()
		{
		}
	}
}
