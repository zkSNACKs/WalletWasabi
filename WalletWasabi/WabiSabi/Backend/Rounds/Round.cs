using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using Nito.AsyncEx;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Crypto;
using WalletWasabi.Crypto.StrobeProtocol;
using WalletWasabi.WabiSabi.Backend.Banning;
using WalletWasabi.WabiSabi.Backend.Models;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Crypto;
using WalletWasabi.WabiSabi.Models;
using WalletWasabi.WabiSabi.Models.MultipartyTransaction;

namespace WalletWasabi.WabiSabi.Backend.Rounds
{
	public class Round
	{
		public Round(RoundParameters roundParameters)
		{
			RoundParameters = roundParameters;

			var allowedAmounts = new MoneyRange(roundParameters.MinRegistrableAmount, RoundParameters.MaxRegistrableAmount);
			var txParams = new MultipartyTransactionParameters(roundParameters.FeeRate, allowedAmounts, allowedAmounts, roundParameters.Network);
			CoinjoinState = new ConstructionState(txParams);

			InitialInputVsizeAllocation = CoinjoinState.Parameters.MaxTransactionSize - MultipartyTransactionParameters.SharedOverhead;
			MaxRegistrableVsize = Math.Min(InitialInputVsizeAllocation / RoundParameters.MaxInputCountByRound, (int)ProtocolConstants.MaxVsizeCredentialValue);
			MaxVsizeAllocationPerAlice = MaxRegistrableVsize;

			AmountCredentialIssuer = new(new(RoundParameters.Random), RoundParameters.Random, MaxRegistrableAmount);
			VsizeCredentialIssuer = new(new(RoundParameters.Random), RoundParameters.Random, MaxRegistrableVsize);
			AmountCredentialIssuerParameters = AmountCredentialIssuer.CredentialIssuerSecretKey.ComputeCredentialIssuerParameters();
			VsizeCredentialIssuerParameters = VsizeCredentialIssuer.CredentialIssuerSecretKey.ComputeCredentialIssuerParameters();

			Id = CalculateHash();

			Alices = new(AlicesById, alice => alice.Id);
		}

		private AsyncLock AsyncLock { get; } = new();
		public MultipartyTransactionState CoinjoinState { get; set; }
		public uint256 Id { get; }
		public Network Network => RoundParameters.Network;
		public Money MinRegistrableAmount => RoundParameters.MinRegistrableAmount;
		public Money MaxRegistrableAmount => RoundParameters.MaxRegistrableAmount;
		public int MaxRegistrableVsize { get; }
		public int MaxVsizeAllocationPerAlice { get; }
		public FeeRate FeeRate => RoundParameters.FeeRate;
		public CredentialIssuer AmountCredentialIssuer { get; }
		public CredentialIssuer VsizeCredentialIssuer { get; }
		public CredentialIssuerParameters AmountCredentialIssuerParameters { get; }
		public CredentialIssuerParameters VsizeCredentialIssuerParameters { get; }
		public ConcurrentDictionary<uint256, Alice> AlicesById { get; } = new();
		[ObsoleteAttribute("Access to internal Arena state should be removed from tests.")]
		public ConcurrentDictionaryValueCollectionView<Alice> Alices { get; }
		public int InputCount => AlicesById.Count;
		public ConcurrentBag<Bob> Bobs { get; } = new();

		public Round? BlameOf => RoundParameters.BlameOf;
		public bool IsBlameRound => RoundParameters.IsBlameRound;
		public ISet<OutPoint> BlameWhitelist => RoundParameters.BlameWhitelist;

		public TimeSpan ConnectionConfirmationTimeout => RoundParameters.ConnectionConfirmationTimeout;
		public TimeSpan OutputRegistrationTimeout => RoundParameters.OutputRegistrationTimeout;
		public TimeSpan TransactionSigningTimeout => RoundParameters.TransactionSigningTimeout;

		public Phase Phase { get; private set; } = Phase.InputRegistration;
		public DateTimeOffset InputRegistrationStart { get; } = DateTimeOffset.UtcNow;
		public DateTimeOffset ConnectionConfirmationStart { get; private set; }
		public DateTimeOffset OutputRegistrationStart { get; private set; }
		public DateTimeOffset TransactionSigningStart { get; private set; }
		public DateTimeOffset End { get; private set; }
		public bool WasTransactionBroadcast { get; set; }
		public int InitialInputVsizeAllocation { get; internal set; }
		public int RemainingInputVsizeAllocation => InitialInputVsizeAllocation - InputCount * MaxVsizeAllocationPerAlice;

		private RoundParameters RoundParameters { get; }

		public TState Assert<TState>() where TState : MultipartyTransactionState =>
			CoinjoinState switch
			{
				TState s => s,
				_ => throw new InvalidOperationException($"{typeof(TState).Name} state was expected but {CoinjoinState.GetType().Name} state was received.")
			};

		// TODO private, needs holding of async lock to be safe
		public void SetPhase(Phase phase)
		{
			if (!Enum.IsDefined<Phase>(phase))
			{
				throw new ArgumentException($"Invalid phase {phase}. This is a bug.", nameof(phase));
			}

			this.LogInfo($"Phase changed: {Phase} -> {phase}");
			Phase = phase;

			if (phase == Phase.ConnectionConfirmation)
			{
				ConnectionConfirmationStart = DateTimeOffset.UtcNow;
			}
			else if (phase == Phase.OutputRegistration)
			{
				OutputRegistrationStart = DateTimeOffset.UtcNow;
			}
			else if (phase == Phase.TransactionSigning)
			{
				TransactionSigningStart = DateTimeOffset.UtcNow;
			}
			else if (phase == Phase.Ended)
			{
				End = DateTimeOffset.UtcNow;
			}
		}

		public bool IsInputRegistrationEnded(int maxInputCount, TimeSpan inputRegistrationTimeout)
		{
			if (Phase > Phase.InputRegistration)
			{
				return true;
			}

			if (IsBlameRound)
			{
				if (BlameWhitelist.Count <= InputCount)
				{
					return true;
				}
			}
			else if (InputCount >= maxInputCount)
			{
				return true;
			}

			if (InputRegistrationStart + inputRegistrationTimeout < DateTimeOffset.UtcNow)
			{
				return true;
			}

			return false;
		}

		// TODO remove only used in tests
		public ConstructionState AddInput(Coin coin)
			=> Assert<ConstructionState>().AddInput(coin);

		public ConstructionState AddOutput(TxOut output)
			=> Assert<ConstructionState>().AddOutput(output);

		public SigningState AddWitness(int index, WitScript witness)
			=> Assert<SigningState>().AddWitness(index, witness);

		private uint256 CalculateHash()
			=> StrobeHasher.Create(ProtocolConstants.RoundStrobeDomain)
				.Append(ProtocolConstants.RoundMinRegistrableAmountStrobeLabel, MinRegistrableAmount)
				.Append(ProtocolConstants.RoundMaxRegistrableAmountStrobeLabel, MaxRegistrableAmount)
				.Append(ProtocolConstants.RoundMaxRegistrableVsizeStrobeLabel, MaxRegistrableVsize)
				.Append(ProtocolConstants.RoundMaxVsizePerAliceStrobeLabel, MaxVsizeAllocationPerAlice)
				.Append(ProtocolConstants.RoundAmountCredentialIssuerParametersStrobeLabel, AmountCredentialIssuerParameters)
				.Append(ProtocolConstants.RoundVsizeCredentialIssuerParametersStrobeLabel, VsizeCredentialIssuerParameters)
				.Append(ProtocolConstants.RoundFeeRateStrobeLabel, FeeRate.FeePerK)
				.GetHash();

		public async Task TimeoutAliceAsync(Alice alice, Arena arena, CancellationToken cancel)
		{
			using (await AsyncLock.LockAsync(cancel).ConfigureAwait(false))
			{
				// FIXME also time them out during connection confirmation, and
				// avoid locking round unless removing (alice is locked so only
				// round phase matters)
				if (Phase == Phase.InputRegistration && alice.Deadline < DateTimeOffset.UtcNow)
				{
					AlicesById.Remove(alice.Id, out _);
					arena.RemoveAlice(alice);
					this.LogInfo($"Alice {alice.Id} timed out and removed.");
				}
			}
		}

		public async Task StepAsync(Arena arena, CancellationToken cancel)
		{
			using (await AsyncLock.LockAsync(cancel).ConfigureAwait(false))
			{
				switch (Phase)
				{
					case Phase.InputRegistration:
						StepInputRegistrationPhase(arena.Config);
						break;
					case Phase.ConnectionConfirmation:
						StepConnectionConfirmationPhase(arena.Config, arena.Prison);
						break;
					case Phase.OutputRegistration:
						StepOutputRegistrationPhase(arena.Config);
						break;
					case Phase.TransactionSigning:
						await StepTransactionSigningPhase(arena, cancel).ConfigureAwait(false);
						break;
					case Phase.Ended:
						await StepTransactionBroadcastingAsync(arena.Rpc, cancel).ConfigureAwait(false);
						break;
				}
			}
		}

		private void StepInputRegistrationPhase(WabiSabiConfig config)
		{
			if (IsInputRegistrationEnded(config.MaxInputCountByRound, config.GetInputRegistrationTimeout(this)))
			{
				if (InputCount < config.MinInputCountByRound)
				{
					SetPhase(Phase.Ended);
					this.LogInfo($"Not enough inputs ({InputCount}) in {nameof(Phase.InputRegistration)} phase.");
				}
				else
				{
					SetPhase(Phase.ConnectionConfirmation);
				}
			}
		}

		private void StepConnectionConfirmationPhase(WabiSabiConfig config, Prison prison)
		{
			// TODO check if inputcount == confirmed count when alices are
			// allowed to expire during connection confirmation?

			if (ConnectionConfirmationStart + ConnectionConfirmationTimeout < DateTimeOffset.UtcNow)
			{
				var alicesDidntConfirm = AlicesById.Select(x => x.Value).Where(x => !x.ConfirmedConnection).ToArray();
				var removedAliceCount = 0;
				foreach (var alice in alicesDidntConfirm)
				{
					prison.Note(alice, Id);
					AlicesById.Remove(alice.Id, out _);
					removedAliceCount++;
				}
				this.LogInfo($"{removedAliceCount} alices removed because they didn't confirm.");

				// TODO is this a race condition?
				if (InputCount < config.MinInputCountByRound)
				{
					SetPhase(Phase.Ended);
					this.LogInfo($"Not enough inputs ({InputCount}) in {nameof(Phase.ConnectionConfirmation)} phase.");
				}
				else
				{
					SetPhase(Phase.OutputRegistration);
				}
			}
		}

		private void StepOutputRegistrationPhase(WabiSabiConfig config)
		{
			if (OutputRegistrationStart + OutputRegistrationTimeout < DateTimeOffset.UtcNow)
			{
				var coinjoin = Assert<ConstructionState>();

				this.LogInfo($"{coinjoin.Inputs.Count} inputs were added.");
				this.LogInfo($"{coinjoin.Outputs.Count} outputs were added.");

				long aliceSum = AlicesById.Sum(x => x.Value.CalculateRemainingAmountCredentials(FeeRate));
				long bobSum = Bobs.Sum(x => x.CredentialAmount);
				var diff = aliceSum - bobSum;

				// If timeout we must fill up the outputs to build a reasonable transaction.
				// This won't be signed by the alice who failed to provide output, so we know who to ban.
				var diffMoney = Money.Satoshis(diff) - coinjoin.Parameters.FeeRate.GetFee(config.BlameScript.EstimateOutputVsize());

				var allReady = AlicesById.All(a => a.Value.ReadyToSign); // FIXME remove: always false?
				if (!allReady && diffMoney > coinjoin.Parameters.AllowedOutputAmounts.Min)
				{
					coinjoin = coinjoin.AddOutput(new TxOut(diffMoney, config.BlameScript));
					this.LogInfo("Filled up the outputs to build a reasonable transaction because some alice failed to provide its output.");
				}

				CoinjoinState = coinjoin.Finalize();

				SetPhase(Phase.TransactionSigning);
			}
		}

		private async Task StepTransactionSigningPhase(Arena arena, CancellationToken cancel)
		{
			if (TransactionSigningStart + TransactionSigningTimeout < DateTimeOffset.UtcNow && !Assert<SigningState>().IsFullySigned)
			{
				this.LogWarning($"Round {Id}: Signing phase timed out after {TransactionSigningTimeout.TotalSeconds} seconds.");
				await FailTransactionSigningPhaseAsync(arena, cancel);
			}
		}

		private async Task FailTransactionSigningPhaseAsync(Arena arena, CancellationToken cancel)
		{
			var state = Assert<SigningState>();

			var unsignedPrevouts = state.UnsignedInputs.ToHashSet();

			var alicesWhoDidntSign = AlicesById.Select(x => x.Value)
				.Select(alice => (Alice: alice, alice.Coin))
				.Where(x => unsignedPrevouts.Contains(x.Coin))
				.Select(x => x.Alice)
				.ToHashSet();

			foreach (var alice in AlicesById.Select(x => x.Value))
			{
				arena.RemoveAlice(alice);
			}

			foreach (var alice in alicesWhoDidntSign)
			{
				arena.Prison.Note(alice, Id);
				AlicesById.Remove(alice.Id, out _);
			}

			SetPhase(Phase.Ended);

			if (InputCount >= arena.Config.MinInputCountByRound)
			{
				await arena.CreateBlameRoundAsync(this, cancel).ConfigureAwait(false);
			}
		}

		private async Task StepTransactionBroadcastingAsync(IRPCClient rpc, CancellationToken cancel)
		{
			if (CoinjoinState is SigningState state)
			{
				if (state.IsFullySigned && !WasTransactionBroadcast)
				{
					var coinjoin = state.CreateTransaction();

					// Logging.
					this.LogInfo("Trying to broadcast coinjoin.");
					Coin[]? spentCoins = AlicesById.Select(x => x.Value.Coin).ToArray();
					Money networkFee = coinjoin.GetFee(spentCoins);
					FeeRate feeRate = coinjoin.GetFeeRate(spentCoins);
					this.LogInfo($"Network Fee: {networkFee.ToString(false, false)} BTC.");
					this.LogInfo($"Network Fee Rate: {feeRate.FeePerK.ToDecimal(MoneyUnit.Satoshi) / 1000} sat/vByte.");
					this.LogInfo($"Number of inputs: {coinjoin.Inputs.Count}.");
					this.LogInfo($"Number of outputs: {coinjoin.Outputs.Count}.");
					this.LogInfo($"Serialized Size: {coinjoin.GetSerializedSize() / 1024} KB.");
					this.LogInfo($"VSize: {coinjoin.GetVirtualSize() / 1024} KB.");
					foreach (var (value, count) in coinjoin.GetIndistinguishableOutputs(includeSingle: false))
					{
						this.LogInfo($"There are {count} occurrences of {value.ToString(true, false)} BTC output.");
					}

					try
					{
						await rpc.SendRawTransactionAsync(coinjoin).ConfigureAwait(false);
						WasTransactionBroadcast = true;
						this.LogInfo($"Successfully broadcast the CoinJoin: {coinjoin.GetHash()}.");
					}
					catch (Exception ex)
					{
						this.LogWarning($"Transaction broadcasting failed, reason: '{ex}'.");
					}
				}
			}
		}

		public async Task<InputRegistrationResponse> RegisterInputAsync(Alice alice, InputRegistrationRequest request, WabiSabiConfig config)
		{
			using (await AsyncLock.LockAsync().ConfigureAwait(false))
			{
				if (IsInputRegistrationEnded(config.MaxInputCountByRound, config.GetInputRegistrationTimeout(this)))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase);
				}

				if (IsBlameRound && !BlameWhitelist.Contains(alice.Coin.Outpoint))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputNotWhitelisted);
				}

				// Compute but don't commit updated CoinJoin to round state, it will
				// be re-calculated on input confirmation. This is computed it here
				// for validation purposes.
				Assert<ConstructionState>().AddInput(alice.Coin);
			}

			var coinJoinInputCommitmentData = new CoinJoinInputCommitmentData("CoinJoinCoordinatorIdentifier", Id);
			if (!OwnershipProof.VerifyCoinJoinInputProof(alice.OwnershipProof, alice.Coin.TxOut.ScriptPubKey, coinJoinInputCommitmentData))
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongOwnershipProof);
			}

			if (alice.TotalInputAmount < MinRegistrableAmount)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.NotEnoughFunds);
			}
			if (alice.TotalInputAmount > MaxRegistrableAmount)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.TooMuchFunds);
			}

			if (alice.TotalInputVsize > MaxVsizeAllocationPerAlice)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.TooMuchVsize);
			}

			if (RemainingInputVsizeAllocation < MaxVsizeAllocationPerAlice)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.VsizeQuotaExceeded);
			}

			var zeroAmountCredentialRequests = request.ZeroAmountCredentialRequests;
			var zeroVsizeCredentialRequests = request.ZeroVsizeCredentialRequests;

			var commitAmountCredentialResponse = AmountCredentialIssuer.PrepareResponse(zeroAmountCredentialRequests);
			var commitVsizeCredentialResponse = VsizeCredentialIssuer.PrepareResponse(zeroVsizeCredentialRequests);

			using (await AsyncLock.LockAsync().ConfigureAwait(false))
			{
				// Check that everything is the same
				if (IsInputRegistrationEnded(config.MaxInputCountByRound, config.GetInputRegistrationTimeout(this)))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase);
				}

				// Have alice timeouts a bit sooner than the timeout of connection confirmation phase.
				// FIXME is this correct?
				alice.SetDeadline(ConnectionConfirmationTimeout * 0.9);

				if (!AlicesById.TryAdd(alice.Id, alice))
				{
					throw new InvalidOperationException();
				}
			}

			return new(alice.Id,
					   commitAmountCredentialResponse.Commit(),
					   commitVsizeCredentialResponse.Commit());
		}

		public async Task RemoveInputAsync(Alice alice, Arena arena, InputsRemovalRequest request)
		{
			using (await AsyncLock.LockAsync().ConfigureAwait(false))
			{
				if (Phase != Phase.InputRegistration)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({request.RoundId}): Wrong phase ({Phase}).");
				}

				// At this point ownership proofs have not yet been revealed
				// to other participants, so AliceId can only be known to
				// its owner.
				AlicesById.Remove(alice.Id, out _);
				arena.RemoveAlice(alice);
			}
		}

		public async Task<ConnectionConfirmationResponse> ConfirmConnectionAsync(Alice alice, ConnectionConfirmationRequest request)
		{
			var realAmountCredentialRequests = request.RealAmountCredentialRequests;
			var realVsizeCredentialRequests = request.RealVsizeCredentialRequests;

			if (realVsizeCredentialRequests.Delta != alice.CalculateRemainingVsizeCredentials(MaxVsizeAllocationPerAlice))
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.IncorrectRequestedVsizeCredentials, $"Round ({request.RoundId}): Incorrect requested vsize credentials.");
			}
			if (realAmountCredentialRequests.Delta != alice.CalculateRemainingAmountCredentials(FeeRate))
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.IncorrectRequestedAmountCredentials, $"Round ({request.RoundId}): Incorrect requested amount credentials.");
			}

			var commitAmountZeroCredentialResponse = AmountCredentialIssuer.PrepareResponse(request.ZeroAmountCredentialRequests);
			var commitVsizeZeroCredentialResponse = VsizeCredentialIssuer.PrepareResponse(request.ZeroVsizeCredentialRequests);

			switch (Phase)
			{
				case Phase.InputRegistration:
					alice.SetDeadline(ConnectionConfirmationTimeout * 0.9); // FIXME is this correct?
					return new(
						commitAmountZeroCredentialResponse.Commit(),
						commitVsizeZeroCredentialResponse.Commit());

				case Phase.ConnectionConfirmation:
					// Ensure the input can be added to the CoinJoin
					Assert<ConstructionState>().AddInput(alice.Coin);
					break;

				default:
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({request.RoundId}): Wrong phase ({Phase}).");
			}

			// Connection confirmation phase, verify the range proofs
			var commitAmountRealCredentialResponse = AmountCredentialIssuer.PrepareResponse(realAmountCredentialRequests);
			var commitVsizeRealCredentialResponse = VsizeCredentialIssuer.PrepareResponse(realVsizeCredentialRequests);

			// Re-acquire lock to commit confirmation
			using (await AsyncLock.LockAsync().ConfigureAwait(false))
			{
				if (Phase != Phase.ConnectionConfirmation)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({request.RoundId}): Wrong phase ({Phase}).");
				}

				var state = Assert<ConstructionState>();

				// Ensure the input can still be added to the CoinJoin
				state = state.AddInput(alice.Coin);

				// update state
				alice.ConfirmedConnection = true;
				CoinjoinState = state;

				if (AlicesById.All(x => x.Value.ConfirmedConnection))
				{
					SetPhase(Phase.OutputRegistration);
				}

				return new(
					commitAmountZeroCredentialResponse.Commit(),
					commitVsizeZeroCredentialResponse.Commit(),
					commitAmountRealCredentialResponse.Commit(),
					commitVsizeRealCredentialResponse.Commit());
			}
		}

		public ReissueCredentialResponse ReissueCredentials(ReissueCredentialRequest request)
		{
			if (Phase is not (Phase.ConnectionConfirmation or Phase.OutputRegistration))
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({Id}): Wrong phase ({Phase}).");
			}

			if (request.RealAmountCredentialRequests.Delta != 0)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.DeltaNotZero, $"Round ({Id}): Amount credentials delta must be zero.");
			}

			if (request.RealAmountCredentialRequests.Requested.Count() != ProtocolConstants.CredentialNumber)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongNumberOfCreds, $"Round ({Id}): Incorrect requested number of amount credentials.");
			}

			if (request.RealVsizeCredentialRequests.Requested.Count() != ProtocolConstants.CredentialNumber)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongNumberOfCreds, $"Round ({Id}): Incorrect requested number of weight credentials.");
			}

			var commitRealAmountCredentialResponse = AmountCredentialIssuer.PrepareResponse(request.RealAmountCredentialRequests);
			var commitRealVsizeCredentialResponse = VsizeCredentialIssuer.PrepareResponse(request.RealVsizeCredentialRequests);
			var commitZeroAmountCredentialResponse = AmountCredentialIssuer.PrepareResponse(request.ZeroAmountCredentialRequests);
			var commitZeroVsizeCredentialResponse = VsizeCredentialIssuer.PrepareResponse(request.ZeroVsizeCredentialsRequests);

			return new(
				commitRealAmountCredentialResponse.Commit(),
				commitRealVsizeCredentialResponse.Commit(),
				commitZeroAmountCredentialResponse.Commit(),
				commitZeroVsizeCredentialResponse.Commit()
			);
		}

		public async Task<OutputRegistrationResponse> RegisterOutputAsync(OutputRegistrationRequest request)
		{
			var credentialAmount = -request.AmountCredentialRequests.Delta;

			Bob bob = new(request.Script, credentialAmount);

			var outputValue = bob.CalculateOutputAmount(FeeRate);

			var vsizeCredentialRequests = request.VsizeCredentialRequests;
			if (-vsizeCredentialRequests.Delta != bob.OutputVsize)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.IncorrectRequestedVsizeCredentials, $"Round ({request.RoundId}): Incorrect requested vsize credentials.");
			}

			if (Phase != Phase.OutputRegistration)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({request.RoundId}): Wrong phase ({Phase}).");
			}

			// Calculate state with the additional output to ensure it's valid.
			_ = AddOutput(new TxOut(outputValue, bob.Script));

			// Verify the credential requests and prepare their responses.
			var commitAmountCredentialResponse = AmountCredentialIssuer.PrepareResponse(request.AmountCredentialRequests);
			var commitVsizeCredentialResponse = VsizeCredentialIssuer.PrepareResponse(vsizeCredentialRequests);

			using (await AsyncLock.LockAsync().ConfigureAwait(false))
			{
				// Check to ensure phase is still valid
				if (Phase != Phase.OutputRegistration)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({request.RoundId}): Wrong phase ({Phase}).");
				}

				// Recalculate state, since it may have been updated. Success of
				// inclusion is guaranteed if it succeeded at the previous
				// state, because the total output vsize is limited by the vsize
				// credentials, and all other conditions are state invariant.
				var newState = AddOutput(new TxOut(outputValue, bob.Script));

				// Update round state.
				Bobs.Add(bob);
				CoinjoinState = newState;
			}

			// Issue credentials and mark presented credentials as used.
			return new(
				commitAmountCredentialResponse.Commit(),
				commitVsizeCredentialResponse.Commit());
		}

		public async Task ReadyToSignAsync(Alice alice, ReadyToSignRequestRequest request)
		{
			var coinJoinInputCommitmentData = new CoinJoinInputCommitmentData("CoinJoinCoordinatorIdentifier", request.RoundId);
			if (!OwnershipProof.VerifyCoinJoinInputProof(request.OwnershipProof, alice.Coin.TxOut.ScriptPubKey, coinJoinInputCommitmentData))
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongOwnershipProof);
			}

			using (await AsyncLock.LockAsync().ConfigureAwait(false))
			{
				alice.ReadyToSign = true;

				if (AlicesById.All(a => a.Value.ReadyToSign))
				{
					var coinjoin = Assert<ConstructionState>();

					this.LogInfo($"{coinjoin.Inputs.Count} inputs were added.");
					this.LogInfo($"{coinjoin.Outputs.Count} outputs were added.");

					CoinjoinState = coinjoin.Finalize();

					SetPhase(Phase.TransactionSigning);
				}
			}
		}

		public async Task SignTransactionAsync(TransactionSignaturesRequest request)
		{
			using (await AsyncLock.LockAsync().ConfigureAwait(false))
			{
				if (Phase != Phase.TransactionSigning)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({request.RoundId}): Wrong phase ({Phase}).");
				}

				var state = Assert<SigningState>();
				foreach (var inputWitnessPair in request.InputWitnessPairs)
				{
					state = state.AddWitness((int)inputWitnessPair.InputIndex, inputWitnessPair.Witness);
				}

				// at this point all of the witnesses have been verified and the state can be updated
				CoinjoinState = state;

				if (state.IsFullySigned)
				{
					SetPhase(Phase.Ended);
				}
			}
		}
	}
}
