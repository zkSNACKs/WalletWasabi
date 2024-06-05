using NBitcoin;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Crypto;
using WalletWasabi.Logging;
using WalletWasabi.WabiSabi.Backend.Models;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Models;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.WabiSabi.Client.RoundStateAwaiters;
using WalletWasabi.Extensions;
using System.Net.Http;
using WabiSabi.Crypto.ZeroKnowledge;
using WalletWasabi.WabiSabi.Models.MultipartyTransaction;
using WalletWasabi.WabiSabi.Client.StatusChangedEvents;
using WabiSabi.Crypto.Randomness;

namespace WalletWasabi.WabiSabi.Client.CoinJoin.Client;

public class AliceClient
{
	private AliceClient(
		Guid aliceId,
		RoundState roundState,
		ArenaClient arenaClient,
		SmartCoin coin,
		IEnumerable<Credential> issuedAmountCredentials,
		IEnumerable<Credential> issuedVsizeCredentials,
		bool isCoordinationFeeExempted)
	{
		var roundParameters = roundState.CoinjoinState.Parameters;
		AliceId = aliceId;
		RoundId = roundState.Id;
		ArenaClient = arenaClient;
		SmartCoin = coin;
		FeeRate = roundParameters.MiningFeeRate;
		CoordinationFeeRate = roundParameters.CoordinationFeeRate;
		IssuedAmountCredentials = issuedAmountCredentials;
		IssuedVsizeCredentials = issuedVsizeCredentials;
		MaxVsizeAllocationPerAlice = roundParameters.MaxVsizeAllocationPerAlice;
		ConfirmationTimeout = roundParameters.ConnectionConfirmationTimeout / 2;
		IsCoordinationFeeExempted = isCoordinationFeeExempted;
	}

	public Guid AliceId { get; }
	public uint256 RoundId { get; }
	private ArenaClient ArenaClient { get; }
	public SmartCoin SmartCoin { get; }
	private FeeRate FeeRate { get; }
	private CoordinationFeeRate CoordinationFeeRate { get; }
	public IEnumerable<Credential> IssuedAmountCredentials { get; private set; }
	public IEnumerable<Credential> IssuedVsizeCredentials { get; private set; }
	private long MaxVsizeAllocationPerAlice { get; }
	private TimeSpan ConfirmationTimeout { get; }
	public bool IsCoordinationFeeExempted { get; }

	public DateTimeOffset LastSuccessfulInputConnectionConfirmation { get; private set; } = DateTimeOffset.UtcNow;

	public static async Task<AliceClient> CreateRegisterAndConfirmInputAsync(
		RoundState roundState,
		ArenaClient arenaClient,
		SmartCoin coin,
		IKeyChain keyChain,
		RoundStateUpdater roundStatusUpdater,
		CancellationToken unregisterCancellationToken,
		CancellationToken registrationCancellationToken,
		CancellationToken confirmationCancellationToken,
		CancellationToken coinBanCheckMode)
	{
		var aliceClient = await RegisterInputAsync(roundState, arenaClient, coin, keyChain, registrationCancellationToken).ConfigureAwait(false);
		try
		{
			await aliceClient.ConfirmConnectionAsync(roundState, roundStatusUpdater, confirmationCancellationToken, coinBanCheckMode).ConfigureAwait(false);

			Logger.LogInfo($"Round ({aliceClient.RoundId}), Alice ({aliceClient.AliceId}): Connection was confirmed.");
		}
		catch (WabiSabiProtocolException wpe) when (wpe.ErrorCode
			is WabiSabiProtocolErrorCode.RoundNotFound
			or WabiSabiProtocolErrorCode.WrongPhase
			or WabiSabiProtocolErrorCode.AliceAlreadyRegistered
			or WabiSabiProtocolErrorCode.AliceAlreadyConfirmedConnection)
		{
			// Do not unregister.
			throw;
		}
		catch (UnexpectedRoundPhaseException)
		{
			// Do not unregister.
			throw;
		}
		catch (Exception) when (aliceClient is { })
		{
			var aliceWouldBeRemovedByBackendTime = aliceClient.LastSuccessfulInputConnectionConfirmation + roundState.CoinjoinState.Parameters.ConnectionConfirmationTimeout;

			// We only need to unregister if alice wouldn't be removed because of the connection confirmation timeout - otherwise just leave it there.
			if (aliceWouldBeRemovedByBackendTime > roundState.InputRegistrationEnd)
			{
				// Unregistering coins is only possible before connection confirmation phase.
				await aliceClient.TryToUnregisterAlicesAsync(unregisterCancellationToken).ConfigureAwait(false);
			}
			throw;
		}

		return aliceClient;
	}

	private static async Task<AliceClient> RegisterInputAsync(RoundState roundState, ArenaClient arenaClient, SmartCoin coin, IKeyChain keyChain, CancellationToken cancellationToken)
	{
		AliceClient? aliceClient;

		var ownershipProof = keyChain.GetOwnershipProof(
			coin,
			new CoinJoinInputCommitmentData(arenaClient.CoordinatorIdentifier, roundState.Id));

		var (response, isCoordinationFeeExempted) = await arenaClient.RegisterInputAsync(roundState.Id, coin.Coin.Outpoint, ownershipProof, cancellationToken).ConfigureAwait(false);
		aliceClient = new(response.Value, roundState, arenaClient, coin, response.IssuedAmountCredentials, response.IssuedVsizeCredentials, isCoordinationFeeExempted);
		coin.CoinJoinInProgress = true;

		Logger.LogInfo($"Round ({roundState.Id}), Alice ({aliceClient.AliceId}): Registered {coin.Outpoint}.");

		return aliceClient;
	}

	private async Task ConfirmConnectionAsync(RoundState roundState, RoundStateUpdater roundStatusUpdater, CancellationToken cancellationToken, CancellationToken coinBanCheckMode)
	{
		long[] amountsToRequest = { EffectiveValue.Satoshi };
		long[] vsizesToRequest = { MaxVsizeAllocationPerAlice - SmartCoin.ScriptPubKey.EstimateInputVsize() };

		do
		{
			using CancellationTokenSource timeout = new(ConfirmationTimeout);
			using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

			try
			{
				await roundStatusUpdater
					.CreateRoundAwaiterAsync(
						RoundId,
						Phase.ConnectionConfirmation,
						cts.Token)
					.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				cancellationToken.ThrowIfCancellationRequested();
			}
			if (coinBanCheckMode.IsCancellationRequested)
			{
				var aliceWouldBeRemovedByBackendTime = DateTime.UtcNow + roundState.CoinjoinState.Parameters.ConnectionConfirmationTimeout;
				if (aliceWouldBeRemovedByBackendTime > roundState.InputRegistrationEnd - TimeSpan.FromMinutes(1) || SecureRandom.Instance.GetInt(0, 100) < 50)
				{
					// We "forget" to confirm the connection
					throw new CoinJoinClientException(CoinjoinError.BannedCoinCheckFinished, "Willingly leaved the connection confirmation due to coin ban check mode.");
				}
			}
		}
		while (!await TryConfirmConnectionAsync(amountsToRequest, vsizesToRequest, cancellationToken).ConfigureAwait(false));
	}

	private async Task<bool> TryConfirmConnectionAsync(IEnumerable<long> amountsToRequest, IEnumerable<long> vsizesToRequest, CancellationToken cancellationToken)
	{
		var response = await ArenaClient
			.ConfirmConnectionAsync(
				RoundId,
				AliceId,
				amountsToRequest,
				vsizesToRequest,
				IssuedAmountCredentials,
				IssuedVsizeCredentials,
				cancellationToken)
			.ConfigureAwait(false);

		IssuedAmountCredentials = response.IssuedAmountCredentials;
		IssuedVsizeCredentials = response.IssuedVsizeCredentials;

		LastSuccessfulInputConnectionConfirmation = DateTimeOffset.UtcNow;

		var isConfirmed = response.Value;
		return isConfirmed;
	}

	public async Task TryToUnregisterAlicesAsync(CancellationToken cancellationToken)
	{
		try
		{
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(7));
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);

			await RemoveInputAsync(linkedCts.Token).ConfigureAwait(false);
			SmartCoin.CoinJoinInProgress = false;
			Logger.LogInfo($"Round ({RoundId}), Alice ({AliceId}): Unregistered {SmartCoin.Outpoint}.");
		}
		catch (OperationCanceledException e)
		{
			Logger.LogTrace(e);
		}
		catch (Exception e) when (e is HttpRequestException or WabiSabiProtocolException)
		{
			Logger.LogDebug($"Unregistration failed for coin '{SmartCoin.Coin.Outpoint}'.", e);
		}
		catch (Exception e)
		{
			// Log and swallow the exception because there is nothing else that can be done here.
			Logger.LogWarning($"{SmartCoin.Coin.Outpoint} unregistration failed with {e}.");
		}
	}

	public void Finish()
	{
		SmartCoin.CoinJoinInProgress = false;
	}

	public async Task RemoveInputAsync(CancellationToken cancellationToken)
	{
		await ArenaClient.RemoveInputAsync(RoundId, AliceId, cancellationToken).ConfigureAwait(false);
		SmartCoin.CoinJoinInProgress = false;
		Logger.LogInfo($"Round ({RoundId}), Alice ({AliceId}): Inputs removed.");
	}

	public async Task SignTransactionAsync(TransactionWithPrecomputedData unsignedCoinJoin, IKeyChain keyChain, CancellationToken cancellationToken)
	{
		await ArenaClient.SignTransactionAsync(RoundId, SmartCoin.Coin, keyChain, unsignedCoinJoin, cancellationToken).ConfigureAwait(false);

		Logger.LogInfo($"Round ({RoundId}), Alice ({AliceId}): Posted a signature.");
	}

	public async Task ReadyToSignAsync(CancellationToken cancellationToken)
	{
		await ArenaClient.ReadyToSignAsync(RoundId, AliceId, cancellationToken).ConfigureAwait(false);
		Logger.LogInfo($"Round ({RoundId}), Alice ({AliceId}): Ready to sign.");
	}

	public Money EffectiveValue => SmartCoin.EffectiveValue(FeeRate, IsCoordinationFeeExempted ? CoordinationFeeRate.Zero : CoordinationFeeRate);
}
