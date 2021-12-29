using NBitcoin;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Bases;
using WalletWasabi.Tor.Socks5.Pool.Circuits;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Models;
using WalletWasabi.WebClients.Wasabi;

namespace WalletWasabi.WabiSabi.Client
{
	public class RoundStateUpdater : PeriodicRunner
	{
		private readonly PersonCircuit? _personCircuit;

		public RoundStateUpdater(TimeSpan requestInterval, IWabiSabiApiRequestHandler arenaRequestHandler) : base(requestInterval)
		{
			ArenaRequestHandler = arenaRequestHandler;
		}

		public RoundStateUpdater(TimeSpan requestInterval, IBackendHttpClientFactory backendHttpClientFactory) : base(requestInterval)
		{
			_personCircuit = new PersonCircuit();			
			ArenaRequestHandler = new WabiSabiHttpApiClient(backendHttpClientFactory.NewBackendHttpClient(Mode.SingleCircuitPerLifetime, _personCircuit));
		}

		private IWabiSabiApiRequestHandler ArenaRequestHandler { get; }
		private Dictionary<uint256, RoundState> RoundStates { get; set; } = new();

		private List<RoundStateAwaiter> Awaiters { get; } = new();
		private object AwaitersLock { get; } = new();

		public bool AnyRound => RoundStates.Any();

		protected override async Task ActionAsync(CancellationToken cancellationToken)
		{
			var statusResponse = await ArenaRequestHandler.GetStatusAsync(cancellationToken).ConfigureAwait(false);
			RoundStates = statusResponse.ToDictionary(round => round.Id);

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

		public Task<RoundState> CreateRoundAwaiter(uint256? roundId, Predicate<RoundState> predicate, CancellationToken cancellationToken)
		{
			RoundStateAwaiter? roundStateAwaiter = null;

			lock (AwaitersLock)
			{
				roundStateAwaiter = new RoundStateAwaiter(predicate, roundId, cancellationToken);
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
			return CreateRoundAwaiter(null, predicate, cancellationToken);
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

			_personCircuit?.Dispose();

			return base.StopAsync(cancellationToken);
		}
	}
}
