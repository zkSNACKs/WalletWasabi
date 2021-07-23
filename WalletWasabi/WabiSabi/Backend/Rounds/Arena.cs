using NBitcoin;
using NBitcoin.RPC;
using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Bases;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Crypto.Randomness;
using WalletWasabi.WabiSabi.Backend.Banning;
using WalletWasabi.WabiSabi.Backend.Models;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Models;

namespace WalletWasabi.WabiSabi.Backend.Rounds
{
	public class Arena : PeriodicRunner
	{
		public Arena(TimeSpan period, Network network, WabiSabiConfig config, IRPCClient rpc, Prison prison) : base(period)
		{
			Network = network;
			Config = config;
			Rpc = rpc;
			Prison = prison;
			Random = new SecureRandom();
			Rounds = new(RoundsById, round => round.Id);
		}

		public ConcurrentDictionary<OutPoint, Alice> AlicesByOutpoint { get; } = new();
		public ConcurrentDictionary<uint256, Alice> AlicesById { get; } = new();
		public ConcurrentDictionary<uint256, Round> RoundsById { get; } = new();
		[ObsoleteAttribute("Access to internal Arena state should be removed from tests.")]
		public ConcurrentDictionaryValueCollectionView<Round> Rounds { get; }

		public Network Network { get; }
		public WabiSabiConfig Config { get; }
		public IRPCClient Rpc { get; }
		public Prison Prison { get; }
		public SecureRandom Random { get; }

		public IEnumerable<Round> ActiveRounds => RoundsById.Select(x => x.Value).Where(x => x.Phase != Phase.Ended);

		private void RemoveRound(Round round)
		{
			RoundsById.Remove(round.Id, out _);

			foreach (var alice in AlicesById.Values.Where(alice => alice.Round == round))
			{
				RemoveAlice(alice);
			}
		}

		public void RemoveAlice(Alice alice)
		{
			AlicesById.Remove(alice.Id, out _);
			AlicesByOutpoint.Remove(alice.Coin.Outpoint, out _);
			alice.Dispose();
		}

		protected override async Task ActionAsync(CancellationToken cancel)
		{
			TimeoutRounds();

			foreach (var round in RoundsById.Select(x => x.Value))
			{
				await round.StepAsync(this, cancel);

				cancel.ThrowIfCancellationRequested();

				if (round.Phase != Phase.Ended)
				{
					// FIXME remove, hack to make alices injected by tests accessible from requests
					foreach (var kvp in round.AlicesById.Where(kvp => !AlicesById.Contains(kvp)))
					{
						if (!AlicesByOutpoint.TryAdd(kvp.Value.Coin.Outpoint, kvp.Value) || !AlicesById.TryAdd(kvp.Key, kvp.Value))
						{
							throw new InvalidOperationException();
						}
					}
				}
			}

			// Ensure there's at least one non-blame round in input registration.
			await CreateRoundsAsync(cancel).ConfigureAwait(false);
		}

		private void TimeoutRounds()
		{
		    foreach (var expiredRound in RoundsById.Select(x => x.Value).Where(
						 x =>
						 x.Phase == Phase.Ended
						 && x.End + Config.RoundExpiryTimeout < DateTimeOffset.UtcNow).ToArray())
			{
				RemoveRound(expiredRound);
			}
		}

		public async Task CreateBlameRoundAsync(Round round, CancellationToken cancel)
		{
			var feeRate = (await Rpc.EstimateSmartFeeAsync((int)Config.ConfirmationTarget, EstimateSmartFeeMode.Conservative, simulateIfRegTest: true).ConfigureAwait(false)).FeeRate;
			RoundParameters parameters = new(Config, Network, Random, feeRate, blameOf: round);
			await AddRound(new(parameters), cancel);
		}

		private async Task CreateRoundsAsync(CancellationToken cancel)
		{
			// Ensure there is always a round accepting inputs
			if (!RoundsById.Select(x => x.Value).Any(x => !x.IsBlameRound && x.Phase == Phase.InputRegistration))
			{
				var smartFeeEstimation = await Rpc.EstimateSmartFeeAsync((int)Config.ConfirmationTarget, EstimateSmartFeeMode.Conservative, simulateIfRegTest: true).ConfigureAwait(false);
				var feeRate = smartFeeEstimation.FeeRate;
				RoundParameters roundParams = new(Config, Network, Random, feeRate);
				await AddRound(new(roundParams), cancel);
			}
		}

		private async Task AddRound(Round round, CancellationToken cancel)
		{
			if (!RoundsById.TryAdd(round.Id, round))
			{
				throw new InvalidOperationException();
			}
		}

		public async Task<RoundState[]> GetStatusAsync(CancellationToken cancellationToken)
		{
			return RoundsById.Select(x => RoundState.FromRound(x.Value)).ToArray();
		}

		public async Task<InputRegistrationResponse> RegisterInputAsync(InputRegistrationRequest request)
		{
			if (!RoundsById.TryGetValue(request.RoundId, out var round))
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
			}

			var coin = await OutpointToCoinAsync(request).ConfigureAwait(false);

			var alice = new Alice(coin, request.OwnershipProof, round);

			// Begin with Alice locked, to serialize requests concerning a
			// single coin.
			using (await alice.AsyncLock.LockAsync().ConfigureAwait(false))
			{
				var otherAlice = AlicesByOutpoint.GetOrAdd(coin.Outpoint, alice);

				if (otherAlice != alice)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceAlreadyRegistered, $"The coin {coin.Outpoint} is already registered into round {otherAlice.Round.Id}.");
				}

				try
				{
					var response = await round.RegisterInputAsync(alice, request, Config);

					// Now that alice is in the round, make it available by id.
					(AlicesById as IDictionary<uint256, Alice>).Add(alice.Id, alice);

					// TODO start automatically? needs Deadline to be set at construction
					alice.StartTimeoutAsync(this);

					return response;
				}
				catch
				{
					AlicesByOutpoint.Remove(coin.Outpoint, out _);
					throw;
				}
			}
		}

		private async Task<Coin> OutpointToCoinAsync(InputRegistrationRequest request)
		{
			OutPoint input = request.Input;

			if (Prison.TryGet(input, out var inmate) && (!Config.AllowNotedInputRegistration || inmate.Punishment != Punishment.Noted))
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputBanned);
			}

			var txOutResponse = await Rpc.GetTxOutAsync(input.Hash, (int)input.N, includeMempool: true).ConfigureAwait(false);
			if (txOutResponse is null)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputSpent);
			}
			if (txOutResponse.Confirmations == 0)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputUnconfirmed);
			}
			if (txOutResponse.IsCoinBase && txOutResponse.Confirmations <= 100)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputImmature);
			}

			return new Coin(input, txOutResponse.TxOut);
		}

		public async Task ReadyToSignAsync(ReadyToSignRequestRequest request)
		{
			if (!RoundsById.TryGetValue(request.RoundId, out var round))
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
			}

			if (!AlicesById.TryGetValue(request.AliceId, out var alice) || alice.Round != round)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceNotFound, $"Round ({request.RoundId}): Alice ({request.AliceId}) not found.");
			}

			using (await alice.AsyncLock.LockAsync().ConfigureAwait(false))
			{
				if (!AlicesById.ContainsKey(alice.Id))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceNotFound, $"Round ({request.RoundId}): Alice ({request.AliceId}) not found.");
				}
				await round.ReadyToSignAsync(alice, request);
			}
		}

		public async Task RemoveInputAsync(InputsRemovalRequest request)
		{
			if (!RoundsById.TryGetValue(request.RoundId, out var round))
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
			}

			if (!AlicesById.TryGetValue(request.AliceId, out var alice))
			{
				// Idempotent removal
				return;
			}

			if (alice.Round != round)
			{
				// Alice exists, but not in this round
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceNotFound, $"Round ({request.RoundId}): Alice ({request.AliceId}) not found.");
			}

			using (await alice.AsyncLock.LockAsync().ConfigureAwait(false))
			{
				if (!AlicesById.ContainsKey(alice.Id))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceNotFound, $"Round ({request.RoundId}): Alice ({request.AliceId}) not found.");
				}

				await round.RemoveInputAsync(alice, this, request);
			}
		}

		public async Task<ConnectionConfirmationResponse> ConfirmConnectionAsync(ConnectionConfirmationRequest request)
		{
			if (!RoundsById.TryGetValue(request.RoundId, out var round))
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
			}

			if (!AlicesById.TryGetValue(request.AliceId, out var alice) || alice.Round != round)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceNotFound, $"Round ({request.RoundId}): Alice ({request.AliceId}) not found.");
			}

			using (await alice.AsyncLock.LockAsync().ConfigureAwait(false))
			{
				if (!AlicesById.ContainsKey(alice.Id))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceNotFound, $"Round ({request.RoundId}): Alice ({request.AliceId}) not found.");
				}

				return await round.ConfirmConnectionAsync(alice, request);
			}
		}

		public async Task<OutputRegistrationResponse> RegisterOutputAsync(OutputRegistrationRequest request)
		{
			if (!RoundsById.TryGetValue(request.RoundId, out var round))
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
			}

			return await round.RegisterOutputAsync(request);
		}

		public async Task SignTransactionAsync(TransactionSignaturesRequest request)
		{
			if (!RoundsById.TryGetValue(request.RoundId, out var round))
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
			}

			await round.SignTransactionAsync(request);
		}

		public ReissueCredentialResponse ReissueCredentials(ReissueCredentialRequest request)
		{
			if (!RoundsById.TryGetValue(request.RoundId, out var round))
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
			}

			return round.ReissueCredentials(request);
		}

		public override void Dispose()
		{
			Random.Dispose();
			base.Dispose();
		}
	}
}
