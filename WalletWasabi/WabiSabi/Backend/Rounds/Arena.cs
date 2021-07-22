using NBitcoin;
using NBitcoin.RPC;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Bases;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Crypto;
using WalletWasabi.Crypto.Randomness;
using WalletWasabi.WabiSabi.Backend.Banning;
using WalletWasabi.WabiSabi.Backend.Models;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Models;
using WalletWasabi.WabiSabi.Models.MultipartyTransaction;

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
		}

		public HashSet<Round> Rounds { get; } = new();
		private AsyncLock AsyncLock { get; } = new();
		public Network Network { get; }
		public WabiSabiConfig Config { get; }
		public IRPCClient Rpc { get; }
		public Prison Prison { get; }
		public SecureRandom Random { get; }

		public IEnumerable<Round> ActiveRounds => Rounds.Where(x => x.Phase != Phase.Ended);

		protected override async Task ActionAsync(CancellationToken cancel)
		{
			using (await AsyncLock.LockAsync(cancel).ConfigureAwait(false))
			{
				TimeoutRounds();

				TimeoutAlices();

				await StepTransactionSigningPhaseAsync().ConfigureAwait(false);

				StepOutputRegistrationPhase();

				StepConnectionConfirmationPhase();

				StepInputRegistrationPhase();

				cancel.ThrowIfCancellationRequested();

				// Ensure there's at least one non-blame round in input registration.
				await CreateRoundsAsync().ConfigureAwait(false);
			}
		}

		private void StepInputRegistrationPhase()
		{
			foreach (var round in Rounds.Where(x =>
				x.Phase == Phase.InputRegistration
				&& x.IsInputRegistrationEnded(Config.MaxInputCountByRound, Config.GetInputRegistrationTimeout(x)))
				.ToArray())
			{
				if (round.InputCount < Config.MinInputCountByRound)
				{
					round.SetPhase(Phase.Ended);
					round.LogInfo($"Not enough inputs ({round.InputCount}) in {nameof(Phase.InputRegistration)} phase.");
				}
				else
				{
					round.SetPhase(Phase.ConnectionConfirmation);
				}
			}
		}

		private void StepConnectionConfirmationPhase()
		{
			foreach (var round in Rounds.Where(x => x.Phase == Phase.ConnectionConfirmation).ToArray())
			{
				if (round.Alices.All(x => x.ConfirmedConnection))
				{
					round.SetPhase(Phase.OutputRegistration);
				}
				else if (round.ConnectionConfirmationStart + round.ConnectionConfirmationTimeout < DateTimeOffset.UtcNow)
				{
					var alicesDidntConfirm = round.Alices.Where(x => !x.ConfirmedConnection).ToArray();
					foreach (var alice in alicesDidntConfirm)
					{
						Prison.Note(alice, round.Id);
					}
					var removedAliceCount = round.Alices.RemoveAll(x => alicesDidntConfirm.Contains(x));
					round.LogInfo($"{removedAliceCount} alices removed because they didn't confirm.");

					if (round.InputCount < Config.MinInputCountByRound)
					{
						round.SetPhase(Phase.Ended);
						round.LogInfo($"Not enough inputs ({round.InputCount}) in {nameof(Phase.ConnectionConfirmation)} phase.");
					}
					else
					{
						round.SetPhase(Phase.OutputRegistration);
					}
				}
			}
		}

		private void StepOutputRegistrationPhase()
		{
			foreach (var round in Rounds.Where(x => x.Phase == Phase.OutputRegistration).ToArray())
			{
				long aliceSum = round.Alices.Sum(x => x.CalculateRemainingAmountCredentials(round.FeeRate));
				long bobSum = round.Bobs.Sum(x => x.CredentialAmount);
				var diff = aliceSum - bobSum;
				var allReady = round.Alices.All(a => a.ReadyToSign);

				if (allReady || round.OutputRegistrationStart + round.OutputRegistrationTimeout < DateTimeOffset.UtcNow)
				{
					var coinjoin = round.Assert<ConstructionState>();

					round.LogInfo($"{coinjoin.Inputs.Count} inputs were added.");
					round.LogInfo($"{coinjoin.Outputs.Count} outputs were added.");

					// If timeout we must fill up the outputs to build a reasonable transaction.
					// This won't be signed by the alice who failed to provide output, so we know who to ban.
					var diffMoney = Money.Satoshis(diff) - coinjoin.Parameters.FeeRate.GetFee(Config.BlameScript.EstimateOutputVsize());
					if (!allReady && diffMoney > coinjoin.Parameters.AllowedOutputAmounts.Min)
					{
						coinjoin = coinjoin.AddOutput(new TxOut(diffMoney, Config.BlameScript));
						round.LogInfo("Filled up the outputs to build a reasonable transaction because some alice failed to provide its output.");
					}

					round.CoinjoinState = coinjoin.Finalize();

					round.SetPhase(Phase.TransactionSigning);
				}
			}
		}

		private async Task StepTransactionSigningPhaseAsync()
		{
			foreach (var round in Rounds.Where(x => x.Phase == Phase.TransactionSigning).ToArray())
			{
				var state = round.Assert<SigningState>();

				try
				{
					if (state.IsFullySigned)
					{
						var coinjoin = state.CreateTransaction();

						// Logging.
						round.LogInfo("Trying to broadcast coinjoin.");
						Coin[]? spentCoins = round.Alices.Select(x => x.Coin).ToArray();
						Money networkFee = coinjoin.GetFee(spentCoins);
						uint256 roundId = round.Id;
						FeeRate feeRate = coinjoin.GetFeeRate(spentCoins);
						round.LogInfo($"Network Fee: {networkFee.ToString(false, false)} BTC.");
						round.LogInfo($"Network Fee Rate: {feeRate.FeePerK.ToDecimal(MoneyUnit.Satoshi) / 1000} sat/vByte.");
						round.LogInfo($"Number of inputs: {coinjoin.Inputs.Count}.");
						round.LogInfo($"Number of outputs: {coinjoin.Outputs.Count}.");
						round.LogInfo($"Serialized Size: {coinjoin.GetSerializedSize() / 1024} KB.");
						round.LogInfo($"VSize: {coinjoin.GetVirtualSize() / 1024} KB.");
						foreach (var (value, count) in coinjoin.GetIndistinguishableOutputs(includeSingle: false))
						{
							round.LogInfo($"There are {count} occurrences of {value.ToString(true, false)} BTC output.");
						}

						// Broadcasting.
						await Rpc.SendRawTransactionAsync(coinjoin).ConfigureAwait(false);
						round.WasTransactionBroadcast = true;
						round.SetPhase(Phase.Ended);

						round.LogInfo($"Successfully broadcast the CoinJoin: {coinjoin.GetHash()}.");
					}
					else if (round.TransactionSigningStart + round.TransactionSigningTimeout < DateTimeOffset.UtcNow)
					{
						throw new TimeoutException($"Round {round.Id}: Signing phase timed out after {round.TransactionSigningTimeout.TotalSeconds} seconds.");
					}
				}
				catch (Exception ex)
				{
					round.LogWarning($"Signing phase failed, reason: '{ex}'.");
					await FailTransactionSigningPhaseAsync(round).ConfigureAwait(false);
				}
			}
		}

		private async Task FailTransactionSigningPhaseAsync(Round round)
		{
			var state = round.Assert<SigningState>();

			var unsignedPrevouts = state.UnsignedInputs.ToHashSet();

			var alicesWhoDidntSign = round.Alices
				.Select(alice => (Alice: alice, alice.Coin))
				.Where(x => unsignedPrevouts.Contains(x.Coin))
				.Select(x => x.Alice)
				.ToHashSet();

			foreach (var alice in alicesWhoDidntSign)
			{
				Prison.Note(alice, round.Id);
			}

			round.Alices.RemoveAll(x => alicesWhoDidntSign.Contains(x));
			round.SetPhase(Phase.Ended);

			if (round.InputCount >= Config.MinInputCountByRound)
			{
				await CreateBlameRoundAsync(round).ConfigureAwait(false);
			}
		}

		private async Task CreateBlameRoundAsync(Round round)
		{
			var feeRate = (await Rpc.EstimateSmartFeeAsync((int)Config.ConfirmationTarget, EstimateSmartFeeMode.Conservative, simulateIfRegTest: true).ConfigureAwait(false)).FeeRate;
			RoundParameters parameters = new(Config, Network, Random, feeRate, blameOf: round);
			Round blameRound = new(parameters);
			Rounds.Add(blameRound);
		}

		private async Task CreateRoundsAsync()
		{
			if (!Rounds.Any(x => !x.IsBlameRound && x.Phase == Phase.InputRegistration))
			{
				var feeRate = (await Rpc.EstimateSmartFeeAsync((int)Config.ConfirmationTarget, EstimateSmartFeeMode.Conservative, simulateIfRegTest: true).ConfigureAwait(false)).FeeRate;

				RoundParameters roundParams = new(Config, Network, Random, feeRate);
				Round r = new(roundParams);
				Rounds.Add(r);
			}
		}

		private void TimeoutRounds()
		{
		    foreach (var expiredRound in Rounds.Where(
				x =>
				x.Phase == Phase.Ended
				&& x.End + Config.RoundExpiryTimeout < DateTimeOffset.UtcNow).ToArray())
			{
				Rounds.Remove(expiredRound);
			}
		}

		private void TimeoutAlices()
		{
			foreach (var round in Rounds.Where(x => !x.IsInputRegistrationEnded(Config.MaxInputCountByRound, Config.GetInputRegistrationTimeout(x))).ToArray())
			{
				var removedAliceCount = round.Alices.RemoveAll(x => x.Deadline < DateTimeOffset.UtcNow);
				if (removedAliceCount > 0)
				{
					round.LogInfo($"{removedAliceCount} alices timed out and removed.");
				}
			}
		}

		public async Task<RoundState[]> GetStatusAsync(CancellationToken cancellationToken)
		{
			using (await AsyncLock.LockAsync(cancellationToken).ConfigureAwait(false))
			{
				return Rounds.Select(x => RoundState.FromRound(x)).ToArray();
			}
		}

		public async Task<InputRegistrationResponse> RegisterInputAsync(InputRegistrationRequest request)
		{
			var coin = await InputRegistrationHandler.OutpointToCoinAsync(request, Prison, Rpc, Config).ConfigureAwait(false);
			using (await AsyncLock.LockAsync().ConfigureAwait(false))
			{
				var registeredCoins = Rounds.Where(x => !(x.Phase == Phase.Ended && !x.WasTransactionBroadcast)).SelectMany(r => r.Alices.Select(a => a.Coin));

				if (registeredCoins.Any(x => x.Outpoint == coin.Outpoint))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceAlreadyRegistered);
				}
				return InputRegistrationHandler.RegisterInput(
					Config,
					request.RoundId,
					coin,
					request.OwnershipProof,
					request.ZeroAmountCredentialRequests,
					request.ZeroVsizeCredentialRequests,
					Rounds);
			}
		}

		public async Task ReadyToSignAsync(ReadyToSignRequestRequest request)
		{
			using (await AsyncLock.LockAsync().ConfigureAwait(false))
			{
				if (Rounds.FirstOrDefault(r => r.Id == request.RoundId) is not Round round)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
				}

				if (round.Alices.FirstOrDefault(a => a.Id == request.AliceId) is not Alice alice)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceNotFound, $"Round ({request.RoundId}): Alice id ({request.AliceId}).");
				}

				var coinJoinInputCommitmentData = new CoinJoinInputCommitmentData("CoinJoinCoordinatorIdentifier", request.RoundId);
				if (!OwnershipProof.VerifyCoinJoinInputProof(request.OwnershipProof, alice.Coin.TxOut.ScriptPubKey, coinJoinInputCommitmentData))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongOwnershipProof);
				}

				alice.ReadyToSign = true;
			}
		}

		public async Task RemoveInputAsync(InputsRemovalRequest request)
		{
			using (await AsyncLock.LockAsync().ConfigureAwait(false))
			{
				if (Rounds.FirstOrDefault(x => x.Id == request.RoundId) is not Round round)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
				}
				if (round.Phase != Phase.InputRegistration)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({request.RoundId}): Wrong phase ({round.Phase}).");
				}
				round.Alices.RemoveAll(x => x.Id == request.AliceId);
			}
		}

		public async Task<ConnectionConfirmationResponse> ConfirmConnectionAsync(ConnectionConfirmationRequest request)
		{
			using (await AsyncLock.LockAsync().ConfigureAwait(false))
			{
				if (Rounds.FirstOrDefault(x => x.Id == request.RoundId) is not Round round)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
				}

				var alice = round.Alices.FirstOrDefault(x => x.Id == request.AliceId);
				if (alice is null)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceNotFound, $"Round ({request.RoundId}): Alice ({request.AliceId}) not found.");
				}

				var realAmountCredentialRequests = request.RealAmountCredentialRequests;
				var realVsizeCredentialRequests = request.RealVsizeCredentialRequests;

				if (realVsizeCredentialRequests.Delta != alice.CalculateRemainingVsizeCredentials(round.MaxVsizeAllocationPerAlice))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.IncorrectRequestedVsizeCredentials, $"Round ({request.RoundId}): Incorrect requested vsize credentials.");
				}
				if (realAmountCredentialRequests.Delta != alice.CalculateRemainingAmountCredentials(round.FeeRate))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.IncorrectRequestedAmountCredentials, $"Round ({request.RoundId}): Incorrect requested amount credentials.");
				}

				var commitAmountZeroCredentialResponse = round.AmountCredentialIssuer.PrepareResponse(request.ZeroAmountCredentialRequests);
				var commitVsizeZeroCredentialResponse = round.VsizeCredentialIssuer.PrepareResponse(request.ZeroVsizeCredentialRequests);

				if (round.Phase == Phase.InputRegistration)
				{
					alice.SetDeadlineRelativeTo(round.ConnectionConfirmationTimeout);
					return new(
						commitAmountZeroCredentialResponse.Commit(),
						commitVsizeZeroCredentialResponse.Commit());
				}
				else if (round.Phase == Phase.ConnectionConfirmation)
				{
					var state = round.Assert<ConstructionState>();

					// Ensure the input can be added to the CoinJoin
					state = state.AddInput(alice.Coin);

					var commitAmountRealCredentialResponse = round.AmountCredentialIssuer.PrepareResponse(realAmountCredentialRequests);
					var commitVsizeRealCredentialResponse = round.VsizeCredentialIssuer.PrepareResponse(realVsizeCredentialRequests);

					// update state
					alice.ConfirmedConnection = true;
					round.CoinjoinState = state;

					return new(
						commitAmountZeroCredentialResponse.Commit(),
						commitVsizeZeroCredentialResponse.Commit(),
						commitAmountRealCredentialResponse.Commit(),
						commitVsizeRealCredentialResponse.Commit());
				}
				else
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({request.RoundId}): Wrong phase ({round.Phase}).");
				}
			}
		}

		public async Task RegisterOutputAsync(OutputRegistrationRequest request)
		{
			using (await AsyncLock.LockAsync().ConfigureAwait(false))
			{
				if (Rounds.FirstOrDefault(x => x.Id == request.RoundId) is not Round round)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
				}

				var credentialAmount = -request.AmountCredentialRequests.Delta;

				Bob bob = new(request.Script, credentialAmount);

				var outputValue = bob.CalculateOutputAmount(round.FeeRate);

				var vsizeCredentialRequests = request.VsizeCredentialRequests;
				if (-vsizeCredentialRequests.Delta < bob.OutputVsize)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.IncorrectRequestedVsizeCredentials, $"Round ({request.RoundId}): Incorrect requested vsize credentials.");
				}

				if (round.Phase != Phase.OutputRegistration)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({request.RoundId}): Wrong phase ({round.Phase}).");
				}

				// Update the current round state with the additional output to ensure it's valid.
				var newState = round.AddOutput(new TxOut(outputValue, bob.Script));

				// Verify the credential requests and prepare their responses.
				var commitAmountCredentialResponse = round.AmountCredentialIssuer.PrepareResponse(request.AmountCredentialRequests);
				var commitVsizeCredentialResponse = round.VsizeCredentialIssuer.PrepareResponse(vsizeCredentialRequests);

				// Update round state.
				round.Bobs.Add(bob);
				round.CoinjoinState = newState;

				// Issue credentials and mark presented credentials as used.
				commitAmountCredentialResponse.Commit();
				commitVsizeCredentialResponse.Commit();
			}
		}

		public async Task SignTransactionAsync(TransactionSignaturesRequest request)
		{
			using (await AsyncLock.LockAsync().ConfigureAwait(false))
			{
				if (Rounds.FirstOrDefault(x => x.Id == request.RoundId) is not Round round)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
				}

				if (round.Phase != Phase.TransactionSigning)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({request.RoundId}): Wrong phase ({round.Phase}).");
				}

				var state = round.Assert<SigningState>();
				foreach (var inputWitnessPair in request.InputWitnessPairs)
				{
					state = state.AddWitness((int)inputWitnessPair.InputIndex, inputWitnessPair.Witness);
				}

				// at this point all of the witnesses have been verified and the state can be updated
				round.CoinjoinState = state;
			}
		}

		public async Task<ReissueCredentialResponse> ReissuanceAsync(ReissueCredentialRequest request)
		{
			using (await AsyncLock.LockAsync().ConfigureAwait(false))
			{
				if (Rounds.FirstOrDefault(x => x.Id == request.RoundId) is not Round round)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
				}

				if (round.Phase is not (Phase.ConnectionConfirmation or Phase.OutputRegistration))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({round.Id}): Wrong phase ({round.Phase}).");
				}

				if (request.RealAmountCredentialRequests.Delta != 0)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.DeltaNotZero, $"Round ({round.Id}): Amount credentials delta must be zero.");
				}

				if (request.RealAmountCredentialRequests.Requested.Count() != ProtocolConstants.CredentialNumber)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongNumberOfCreds, $"Round ({round.Id}): Incorrect requested number of amount credentials.");
				}

				if (request.RealVsizeCredentialRequests.Requested.Count() != ProtocolConstants.CredentialNumber)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongNumberOfCreds, $"Round ({round.Id}): Incorrect requested number of weight credentials.");
				}

				var commitRealAmountCredentialResponse = round.AmountCredentialIssuer.PrepareResponse(request.RealAmountCredentialRequests);
				var commitRealVsizeCredentialResponse = round.VsizeCredentialIssuer.PrepareResponse(request.RealVsizeCredentialRequests);
				var commitZeroAmountCredentialResponse = round.AmountCredentialIssuer.PrepareResponse(request.ZeroAmountCredentialRequests);
				var commitZeroVsizeCredentialResponse = round.VsizeCredentialIssuer.PrepareResponse(request.ZeroVsizeCredentialsRequests);

				return new(
					commitRealAmountCredentialResponse.Commit(),
					commitRealVsizeCredentialResponse.Commit(),
					commitZeroAmountCredentialResponse.Commit(),
					commitZeroVsizeCredentialResponse.Commit()
					);
			}
		}

		public override void Dispose()
		{
			Random.Dispose();
			base.Dispose();
		}
	}
}
