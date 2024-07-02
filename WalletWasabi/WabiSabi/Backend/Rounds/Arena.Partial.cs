using System.Collections.Generic;
using NBitcoin;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WabiSabi.CredentialRequesting;
using WabiSabi.Crypto;
using WalletWasabi.WabiSabi.Backend.Models;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Models;
using WalletWasabi.WabiSabi.Models.MultipartyTransaction;
using WalletWasabi.Logging;
using WalletWasabi.Crypto.Randomness;

namespace WalletWasabi.WabiSabi.Backend.Rounds;

public partial class Arena : IWabiSabiApiRequestHandler
{
	public async Task<InputRegistrationResponse> RegisterInputAsync(InputRegistrationRequest request, CancellationToken cancellationToken)
	{
		try
		{
			return await RegisterInputCoreAsync(request, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (IsUserCheating(ex))
		{
			Logger.LogInfo($"{request.Input} is cheating: {ex.Message}");
			Prison.CheatingDetected(request.Input, request.RoundId);
			throw;
		}
	}

	private async Task<InputRegistrationResponse> RegisterInputCoreAsync(InputRegistrationRequest request, CancellationToken cancellationToken)
	{
		var (coin, confirmations) = await OutpointToCoinAsync(request, cancellationToken).ConfigureAwait(false);

		using (await AsyncLock.LockAsync(cancellationToken).ConfigureAwait(false))
		{
			var round = GetRound(request.RoundId);

			// Compute but don't commit updated coinjoin to round state, it will
			// be re-calculated on input confirmation. This is computed in here
			// for validation purposes.
			_ = round.Assert<ConstructionState>().AddInput(coin, request.OwnershipProof, round.CoinJoinInputCommitmentData);

			CheckCoinIsNotBanned(coin.Outpoint, round);

			var registeredCoins = Rounds.Where(x => !(x.Phase == Phase.Ended && x.EndRoundState != EndRoundState.TransactionBroadcasted))
				.SelectMany(r => r.Alices.Select(a => a.Coin));

			if (registeredCoins.Any(x => x.Outpoint == coin.Outpoint))
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceAlreadyRegistered);
			}

			if (round.IsInputRegistrationEnded(Config.MaxInputCountByRound))
			{
				throw new WrongPhaseException(round, Phase.InputRegistration);
			}

			if (round is BlameRound blameRound && !blameRound.BlameWhitelist.Contains(coin.Outpoint))
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputNotWhitelisted);
			}

			// Generate a new GUID with the secure random source, to be sure
			// that it is not guessable (Guid.NewGuid() documentation does
			// not say anything about GUID version or randomness source,
			// only that the probability of duplicates is very low).
			var id = new Guid(SecureRandom.Instance.GetBytes(16));

			var comingFromCoinJoin = CoinJoinIdStore.Contains(coin.Outpoint.Hash);
			bool oneHop = false;

			if (!comingFromCoinJoin)
			{
				// If the coin comes from a tx that all of the tx inputs are coming from a CJ (1 hop - no pay).
				Transaction tx = await Rpc.GetRawTransactionAsync(coin.Outpoint.Hash, true, cancellationToken).ConfigureAwait(false);

				if (tx.Inputs.All(input => CoinJoinIdStore.Contains(input.PrevOut.Hash)))
				{
					oneHop = true;
				}
			}

			var isCoordinationFeeExempted = comingFromCoinJoin || oneHop;
			var alice = new Alice(coin, request.OwnershipProof, round, id, isCoordinationFeeExempted);

			if (alice.CalculateRemainingAmountCredentials(round.Parameters.MiningFeeRate, round.Parameters.CoordinationFeeRate) <= Money.Zero)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.UneconomicalInput);
			}

			if (alice.TotalInputAmount < round.Parameters.MinAmountCredentialValue)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.NotEnoughFunds);
			}
			if (alice.TotalInputAmount > round.Parameters.MaxAmountCredentialValue)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.TooMuchFunds);
			}

			if (alice.TotalInputVsize > round.Parameters.MaxVsizeAllocationPerAlice)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.TooMuchVsize);
			}

			var amountCredentialTask = round.AmountCredentialIssuer.HandleRequestAsync(request.ZeroAmountCredentialRequests, cancellationToken);
			var vsizeCredentialTask = round.VsizeCredentialIssuer.HandleRequestAsync(request.ZeroVsizeCredentialRequests, cancellationToken);

			if (round.RemainingInputVsizeAllocation < round.Parameters.MaxVsizeAllocationPerAlice)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.VsizeQuotaExceeded);
			}

			var commitAmountCredentialResponse = await amountCredentialTask.ConfigureAwait(false);
			var commitVsizeCredentialResponse = await vsizeCredentialTask.ConfigureAwait(false);

			alice.SetDeadlineRelativeTo(round.ConnectionConfirmationTimeFrame.Duration);
			round.Alices.Add(alice);

			return new(alice.Id,
				commitAmountCredentialResponse,
				commitVsizeCredentialResponse);
		}
	}

	public async Task ReadyToSignAsync(ReadyToSignRequestRequest request, CancellationToken cancellationToken)
	{
		using (await AsyncLock.LockAsync(cancellationToken).ConfigureAwait(false))
		{
			var round = GetRound(request.RoundId, Phase.OutputRegistration);
			var alice = GetAlice(request.AliceId, round);
			alice.ReadyToSign = true;
		}
	}

	public async Task RemoveInputAsync(InputsRemovalRequest request, CancellationToken cancellationToken)
	{
		using (await AsyncLock.LockAsync(cancellationToken).ConfigureAwait(false))
		{
			var round = GetRound(request.RoundId, Phase.InputRegistration);

			round.Alices.RemoveAll(x => x.Id == request.AliceId && x.ConfirmedConnection == false);
		}
	}

	public async Task<ConnectionConfirmationResponse> ConfirmConnectionAsync(ConnectionConfirmationRequest request, CancellationToken cancellationToken)
	{
		try
		{
			return await ConfirmConnectionCoreAsync(request, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (IsUserCheating(ex))
		{
			var round = GetRound(request.RoundId);
			var alice = GetAlice(request.AliceId, round);
			Logger.LogInfo($"{alice.Coin.Outpoint} is cheating: {ex.Message}");
			Prison.CheatingDetected(alice.Coin.Outpoint, request.RoundId);
			throw;
		}
	}

	private async Task<ConnectionConfirmationResponse> ConfirmConnectionCoreAsync(ConnectionConfirmationRequest request, CancellationToken cancellationToken)
	{
		Round round;
		Alice alice;
		var realAmountCredentialRequests = request.RealAmountCredentialRequests;
		var realVsizeCredentialRequests = request.RealVsizeCredentialRequests;

		using (await AsyncLock.LockAsync(cancellationToken).ConfigureAwait(false))
		{
			round = GetRound(request.RoundId, Phase.InputRegistration, Phase.ConnectionConfirmation);

			alice = GetAlice(request.AliceId, round);

			if (alice.ConfirmedConnection)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceAlreadyConfirmedConnection, $"Round ({request.RoundId}): Alice ({request.AliceId}) already confirmed connection.");
			}

			if (realVsizeCredentialRequests.Delta != alice.CalculateRemainingVsizeCredentials(round.Parameters.MaxVsizeAllocationPerAlice))
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.IncorrectRequestedVsizeCredentials, $"Round ({request.RoundId}): Incorrect requested vsize credentials.");
			}

			var remaining = alice.CalculateRemainingAmountCredentials(round.Parameters.MiningFeeRate, round.Parameters.CoordinationFeeRate);
			if (realAmountCredentialRequests.Delta != remaining)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.IncorrectRequestedAmountCredentials, $"Round ({request.RoundId}): Incorrect requested amount credentials.");
			}
		}

		var amountZeroCredentialTask = round.AmountCredentialIssuer.HandleRequestAsync(request.ZeroAmountCredentialRequests, cancellationToken);
		var vsizeZeroCredentialTask = round.VsizeCredentialIssuer.HandleRequestAsync(request.ZeroVsizeCredentialRequests, cancellationToken);
		Task<CredentialsResponse>? amountRealCredentialTask = null;
		Task<CredentialsResponse>? vsizeRealCredentialTask = null;

		if (round.Phase is Phase.ConnectionConfirmation)
		{
			amountRealCredentialTask = round.AmountCredentialIssuer.HandleRequestAsync(realAmountCredentialRequests, cancellationToken);
			vsizeRealCredentialTask = round.VsizeCredentialIssuer.HandleRequestAsync(realVsizeCredentialRequests, cancellationToken);
		}

		using (await AsyncLock.LockAsync(cancellationToken).ConfigureAwait(false))
		{
			alice = GetAlice(request.AliceId, round);

			switch (round.Phase)
			{
				case Phase.InputRegistration:
					var commitAmountZeroCredentialResponse = await amountZeroCredentialTask.ConfigureAwait(false);
					var commitVsizeZeroCredentialResponse = await vsizeZeroCredentialTask.ConfigureAwait(false);
					alice.SetDeadlineRelativeTo(round.ConnectionConfirmationTimeFrame.Duration);
					return new(
						commitAmountZeroCredentialResponse,
						commitVsizeZeroCredentialResponse);

				case Phase.ConnectionConfirmation:
					// If the phase was InputRegistration before then we did not pre-calculate real credentials.
					amountRealCredentialTask ??= round.AmountCredentialIssuer.HandleRequestAsync(realAmountCredentialRequests, cancellationToken);
					vsizeRealCredentialTask ??= round.VsizeCredentialIssuer.HandleRequestAsync(realVsizeCredentialRequests, cancellationToken);

					ConnectionConfirmationResponse response = new(
						await amountZeroCredentialTask.ConfigureAwait(false),
						await vsizeZeroCredentialTask.ConfigureAwait(false),
						await amountRealCredentialTask.ConfigureAwait(false),
						await vsizeRealCredentialTask.ConfigureAwait(false));

					// Update the coinjoin state, adding the confirmed input.
					round.CoinjoinState = round.Assert<ConstructionState>().AddInput(alice.Coin, alice.OwnershipProof, round.CoinJoinInputCommitmentData);
					alice.ConfirmedConnection = true;
					return response;

				default:
					throw new WrongPhaseException(round, Phase.InputRegistration, Phase.ConnectionConfirmation);
			}
		}
	}

	public Task RegisterOutputAsync(OutputRegistrationRequest request, CancellationToken cancellationToken)
	{
		return RegisterOutputCoreAsync(request, cancellationToken);
	}

	public async Task<EmptyResponse> RegisterOutputCoreAsync(OutputRegistrationRequest request, CancellationToken cancellationToken)
	{
		using (await AsyncLock.LockAsync(cancellationToken).ConfigureAwait(false))
		{
			var round = GetRound(request.RoundId, Phase.OutputRegistration);

			var credentialAmount = -request.AmountCredentialRequests.Delta;

			if (CoinJoinScriptStore?.Contains(request.Script) is true)
			{
				Logger.LogWarning($"Round ({request.RoundId}): Already registered script in previous coinjoins.");
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AlreadyRegisteredScript, $"Round ({request.RoundId}): Already registered script.");
			}

			var outputScripts = Rounds
				.Where(r => r.Id != round.Id && r.Phase != Phase.Ended)
				.SelectMany(r => r.Bobs)
				.Select(x => x.Script)
				.ToHashSet();
			if (outputScripts.Contains(request.Script))
			{
				Logger.LogWarning($"Round ({request.RoundId}): Already registered script in some round (output side).");
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AlreadyRegisteredScript, $"Round ({request.RoundId}): Already registered script in some round.");
			}

			var inputScripts = Rounds.SelectMany(r => round.Alices).Select(a => a.Coin.ScriptPubKey).ToHashSet();
			if (inputScripts.Contains(request.Script))
			{
				Logger.LogWarning($"Round ({request.RoundId}): Already registered script in some round (input side).");
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AlreadyRegisteredScript, $"Round ({request.RoundId}): Already registered script some round.");
			}

			Bob bob = new(request.Script, credentialAmount);

			var outputValue = bob.CalculateOutputAmount(round.Parameters.MiningFeeRate);

			var vsizeCredentialRequests = request.VsizeCredentialRequests;
			if (-vsizeCredentialRequests.Delta != bob.OutputVsize)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.IncorrectRequestedVsizeCredentials, $"Round ({request.RoundId}): Incorrect requested vsize credentials.");
			}

			// Update the current round state with the additional output to ensure it's valid.
			var newState = round.AddOutput(new TxOut(outputValue, bob.Script));

			// Verify the credential requests and prepare their responses.
			await round.AmountCredentialIssuer.HandleRequestAsync(request.AmountCredentialRequests, cancellationToken).ConfigureAwait(false);
			await round.VsizeCredentialIssuer.HandleRequestAsync(vsizeCredentialRequests, cancellationToken).ConfigureAwait(false);

			// Update round state.
			round.Bobs.Add(bob);
			round.CoinjoinState = newState;
		}

		return EmptyResponse.Instance;
	}

	public async Task SignTransactionAsync(TransactionSignaturesRequest request, CancellationToken cancellationToken)
	{
		using (await AsyncLock.LockAsync(cancellationToken).ConfigureAwait(false))
		{
			var round = GetRound(request.RoundId, Phase.TransactionSigning);

			var state = round.Assert<SigningState>().AddWitness((int)request.InputIndex, request.Witness);

			// at this point all of the witnesses have been verified and the state can be updated
			round.CoinjoinState = state;
		}
	}

	public async Task<ReissueCredentialResponse> ReissuanceAsync(ReissueCredentialRequest request, CancellationToken cancellationToken)
	{
		Round round;
		using (await AsyncLock.LockAsync(cancellationToken).ConfigureAwait(false))
		{
			round = GetRound(request.RoundId, Phase.ConnectionConfirmation, Phase.OutputRegistration);
		}

		if (request.RealAmountCredentialRequests.Delta != 0)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.DeltaNotZero, $"Round ({round.Id}): Amount credentials delta must be zero.");
		}

		if (request.RealVsizeCredentialRequests.Delta != 0)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.DeltaNotZero, $"Round ({round.Id}): Vsize credentials delta must be zero.");
		}

		if (request.RealAmountCredentialRequests.Requested.Count() != ProtocolConstants.CredentialNumber)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongNumberOfCreds, $"Round ({round.Id}): Incorrect requested number of amount credentials.");
		}

		if (request.RealVsizeCredentialRequests.Requested.Count() != ProtocolConstants.CredentialNumber)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongNumberOfCreds, $"Round ({round.Id}): Incorrect requested number of weight credentials.");
		}

		var realAmountTask = round.AmountCredentialIssuer.HandleRequestAsync(request.RealAmountCredentialRequests, cancellationToken);
		var realVsizeTask = round.VsizeCredentialIssuer.HandleRequestAsync(request.RealVsizeCredentialRequests, cancellationToken);
		var zeroAmountTask = round.AmountCredentialIssuer.HandleRequestAsync(request.ZeroAmountCredentialRequests, cancellationToken);
		var zeroVsizeTask = round.VsizeCredentialIssuer.HandleRequestAsync(request.ZeroVsizeCredentialsRequests, cancellationToken);

		return new(
			await realAmountTask.ConfigureAwait(false),
			await realVsizeTask.ConfigureAwait(false),
			await zeroAmountTask.ConfigureAwait(false),
			await zeroVsizeTask.ConfigureAwait(false));
	}

	public async Task<(Coin coin, int Confirmations)> OutpointToCoinAsync(InputRegistrationRequest request, CancellationToken cancellationToken)
	{
		OutPoint input = request.Input;

		var txOutResponse = await Rpc.GetTxOutAsync(input.Hash, (int)input.N, includeMempool: true, cancellationToken).ConfigureAwait(false)
			?? throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputSpent);
		if (txOutResponse.Confirmations == 0)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputUnconfirmed);
		}

		if (txOutResponse.IsCoinBase && txOutResponse.Confirmations <= 100)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputImmature);
		}

		return (new Coin(input, txOutResponse.TxOut), txOutResponse.Confirmations);
	}

	public Task<RoundStateResponse> GetStatusAsync(RoundStateRequest request, CancellationToken cancellationToken)
	{
		if (Config.IsCoordinationEnabled is false)
		{
			return Task.FromResult(new RoundStateResponse(Array.Empty<RoundState>(), Array.Empty<CoinJoinFeeRateMedian>()));
		}
		var requestCheckPointDictionary = request.RoundCheckpoints.ToDictionary(r => r.RoundId, r => r);
		var responseRoundStates = RoundStates.Select(x =>
		{
			if (requestCheckPointDictionary.TryGetValue(x.Id, out RoundStateCheckpoint? checkPoint) && checkPoint.StateId > 0)
			{
				return x.GetSubState(checkPoint.StateId);
			}

			return x;
		}).ToArray();
		return Task.FromResult(new RoundStateResponse(responseRoundStates, Array.Empty<CoinJoinFeeRateMedian>()));
	}

	public (uint256 RoundId, FeeRate MiningFeeRate)[] GetRoundsContainingOutpoints(IEnumerable<OutPoint> outPoints) =>
		Rounds
		.Where(r => r.Phase != Phase.Ended && r.Phase >= Phase.ConnectionConfirmation)
		.SelectMany(r => r.CoinjoinState.Inputs.Select(a => (RoundId: r.Id, MiningFeeRate: r.Parameters.MiningFeeRate, Coin: a)))
		.Where(x => outPoints.Any(outpoint => outpoint == x.Coin.Outpoint))
		.Select(x => (x.RoundId, x.MiningFeeRate))
		.Distinct()
		.ToArray();

	private void CheckCoinIsNotBanned(OutPoint input, Round round)
	{
		var banningTime = Prison.GetBanTimePeriod(input, Config.GetDoSConfiguration());
		if (banningTime.Includes(DateTimeOffset.UtcNow))
		{
			round.LogInfo($"{input} rejected. Banned until {banningTime.EndTime}");
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputBanned, exceptionData: new InputBannedExceptionData(banningTime.EndTime));
		}
	}

	private Round GetRound(uint256 roundId) =>
		Rounds.FirstOrDefault(x => x.Id == roundId)
		?? throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({roundId}) not found.");

	private Round InPhase(Round round, Phase[] phases) =>
		phases.Contains(round.Phase)
		? round
		: throw new WrongPhaseException(round, phases);

	private Round GetRound(uint256 roundId, params Phase[] phases) =>
		InPhase(GetRound(roundId), phases);

	private Alice GetAlice(Guid aliceId, Round round) =>
		round.Alices.Find(x => x.Id == aliceId)
		?? throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceNotFound, $"Round ({round.Id}): Alice ({aliceId}) not found.");

	private static bool IsUserCheating(Exception e) =>
		e is WabiSabiCryptoException || (e is WabiSabiProtocolException wpe && wpe.ErrorCode.IsEvidencingClearMisbehavior());
}
