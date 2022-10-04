using NBitcoin;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Bases;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Models;

namespace WalletWasabi.WabiSabi.Client.RoundStateAwaiters;

public class RoundStateUpdater : PeriodicRunner
{
	public RoundStateUpdater(TimeSpan requestInterval, IWabiSabiApiRequestHandler arenaRequestHandler) : base(requestInterval)
	{
		ArenaRequestHandler = arenaRequestHandler;
	}

	private IWabiSabiApiRequestHandler ArenaRequestHandler { get; }
	private IDictionary<uint256, RoundState> RoundStates { get; set; } = new Dictionary<uint256, RoundState>();
	public Dictionary<TimeSpan, FeeRate> CoinJoinFeeRateMedians { get; private set; } = new();

	private List<RoundStateAwaiter> Awaiters { get; } = new();
	private object AwaitersLock { get; } = new();

	public bool AnyRound => RoundStates.Any();

	protected override async Task ActionAsync(CancellationToken cancellationToken)
	{
		var request = new RoundStateRequest(
			RoundStates.Select(x => new RoundStateCheckpoint(x.Key, x.Value.CoinjoinState.Events.Count)).ToImmutableList());

		using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(30));
		using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

		var response = await ArenaRequestHandler.GetStatusAsync(request, linkedCts.Token).ConfigureAwait(false);
		RoundState[] roundStates = response.RoundStates;

		CoinJoinFeeRateMedians = response.CoinJoinFeeRateMedians.ToDictionary(a => a.TimeFrame, a => a.MedianFeeRate);

		var updatedRoundStates = roundStates
			.Where(rs => RoundStates.ContainsKey(rs.Id))
			.Select(rs => (NewRoundState: rs, CurrentRoundState: RoundStates[rs.Id]))
			.Select(
				x => x.NewRoundState with
				{
					CoinjoinState = x.NewRoundState.CoinjoinState.AddPreviousStates(x.CurrentRoundState.CoinjoinState)
				})
			.ToList();

		var newRoundStates = roundStates
			.Where(rs => !RoundStates.ContainsKey(rs.Id));

		var listRoundStates = newRoundStates.Concat(updatedRoundStates).ToList();
		for (var i = 0; i < listRoundStates.Count - 1; i++)
		{
			if (listRoundStates[i].Phase != Phase.InputRegistration)
			{
				continue;
			}

			for (var j = i + 1; j < listRoundStates.Count; j++)
			{
				if (listRoundStates[j].Phase == Phase.InputRegistration
				    || listRoundStates[i].BlameOf.Equals(listRoundStates[j].BlameOf)
				    || Math.Abs((listRoundStates[i].InputRegistrationEnd - listRoundStates[j].InputRegistrationEnd).TotalSeconds) <= 30
				    || listRoundStates[i].CoinjoinState.Parameters.MaxSuggestedAmount > listRoundStates[j].CoinjoinState.Parameters.MaxSuggestedAmount)
				{
					// Both i and j were created by the load balancer and big round is first, swap
					(listRoundStates[i], listRoundStates[j]) = (listRoundStates[j], listRoundStates[i]);
					break;
				}
			}
		}

		// Don't use ToImmutable dictionary, because that ruins the original order and makes the server unable to suggest a round preference.
		// ToDo: ToDictionary doesn't guarantee the order by design so .NET team might change this out of our feet, so there's room for improvement here.
		RoundStates = listRoundStates.ToDictionary(x => x.Id, x => x);

		lock (AwaitersLock)
		{
			foreach (var awaiter in Awaiters.Where(awaiter => awaiter.IsCompleted(RoundStates)).ToArray())
			{
				// The predicate was fulfilled.
				Awaiters.Remove(awaiter);
				break;
			}
		}
	}

	private Task<RoundState> CreateRoundAwaiter(uint256? roundId, Phase? phase, Predicate<RoundState>? predicate, CancellationToken cancellationToken)
	{
		RoundStateAwaiter? roundStateAwaiter = null;

		lock (AwaitersLock)
		{
			roundStateAwaiter = new RoundStateAwaiter(predicate, roundId, phase, cancellationToken);
			Awaiters.Add(roundStateAwaiter);
		}

		cancellationToken.Register(() =>
		{
			lock (AwaitersLock)
			{
				Awaiters.Remove(roundStateAwaiter);
			}
		});

		return roundStateAwaiter.Task;
	}

	public Task<RoundState> CreateRoundAwaiter(Predicate<RoundState> predicate, CancellationToken cancellationToken)
	{
		return CreateRoundAwaiter(null, null, predicate, cancellationToken);
	}

	public Task<RoundState> CreateRoundAwaiter(uint256 roundId, Phase phase, CancellationToken cancellationToken)
	{
		return CreateRoundAwaiter(roundId, phase, null, cancellationToken);
	}

	public Task<RoundState> CreateRoundAwaiter(Phase phase, CancellationToken cancellationToken)
	{
		return CreateRoundAwaiter(null, phase, null, cancellationToken);
	}

	public bool TryGetRoundState(uint256 roundId, [NotNullWhen(true)] out RoundState? roundState)
	{
		return RoundStates.TryGetValue(roundId, out roundState);
	}

	public override Task StopAsync(CancellationToken cancellationToken)
	{
		lock (AwaitersLock)
		{
			foreach (var awaiter in Awaiters)
			{
				awaiter.Cancel();
			}
		}

		return base.StopAsync(cancellationToken);
	}
}
