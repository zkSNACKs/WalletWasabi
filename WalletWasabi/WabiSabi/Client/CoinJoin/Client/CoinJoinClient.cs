using NBitcoin;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WabiSabi.Crypto.Randomness;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Exceptions;
using WalletWasabi.Extensions;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Tor.Socks5.Pool.Circuits;
using WalletWasabi.WabiSabi.Backend.Models;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Client.CoinJoinProgressEvents;
using WalletWasabi.WabiSabi.Client.CredentialDependencies;
using WalletWasabi.WabiSabi.Client.RoundStateAwaiters;
using WalletWasabi.WabiSabi.Client.StatusChangedEvents;
using WalletWasabi.WabiSabi.Models;
using WalletWasabi.WabiSabi.Models.MultipartyTransaction;
using WalletWasabi.WebClients.Wasabi;

namespace WalletWasabi.WabiSabi.Client.CoinJoin.Client;

public class CoinJoinClient
{
	private static readonly Money MinimumOutputAmountSanity = Money.Coins(0.0001m); // ignore rounds with too big minimum denominations
	private static readonly TimeSpan ExtraPhaseTimeoutMargin = TimeSpan.FromMinutes(2);
	private static readonly TimeSpan ExtraRoundTimeoutMargin = TimeSpan.FromMinutes(10);

	// Maximum delay when spreading the requests in time, except input registration requests which
	// timings only depends on the input-reg timeout and signing requests which timings must be larger.
	// This is a maximum cap the delay can be smaller if the remaining time is less.
	private static readonly TimeSpan MaximumRequestDelay = TimeSpan.FromSeconds(10);

	public CoinJoinClient(
		IWasabiHttpClientFactory httpClientFactory,
		IKeyChain keyChain,
		OutputProvider outputProvider,
		RoundStateUpdater roundStatusUpdater,
		string coordinatorIdentifier,
		CoinJoinCoinSelector coinJoinCoinSelector,
		LiquidityClueProvider liquidityClueProvider,
		TimeSpan feeRateMedianTimeFrame = default,
		TimeSpan doNotRegisterInLastMinuteTimeLimit = default,
		CoinjoinSkipFactors? skipFactors = null)
	{
		HttpClientFactory = httpClientFactory;
		KeyChain = keyChain;
		OutputProvider = outputProvider;
		RoundStatusUpdater = roundStatusUpdater;
		CoordinatorIdentifier = coordinatorIdentifier;
		LiquidityClueProvider = liquidityClueProvider;
		CoinJoinCoinSelector = coinJoinCoinSelector;
		FeeRateMedianTimeFrame = feeRateMedianTimeFrame;
		SkipFactors = skipFactors ?? CoinjoinSkipFactors.NoSkip;
		SecureRandom = new SecureRandom();
		DoNotRegisterInLastMinuteTimeLimit = doNotRegisterInLastMinuteTimeLimit;
	}

	public event EventHandler<CoinJoinProgressEventArgs>? CoinJoinClientProgress;

	public ImmutableList<SmartCoin> CoinsInCriticalPhase { get; private set; } = ImmutableList<SmartCoin>.Empty;

	private SecureRandom SecureRandom { get; }
	private IWasabiHttpClientFactory HttpClientFactory { get; }
	private IKeyChain KeyChain { get; }
	private OutputProvider OutputProvider { get; }
	private RoundStateUpdater RoundStatusUpdater { get; }
	private string CoordinatorIdentifier { get; }
	private LiquidityClueProvider LiquidityClueProvider { get; }
	private CoinJoinCoinSelector CoinJoinCoinSelector { get; }
	private TimeSpan DoNotRegisterInLastMinuteTimeLimit { get; }
	private TimeSpan FeeRateMedianTimeFrame { get; }
	private CoinjoinSkipFactors SkipFactors { get; }
	private TimeSpan MaxWaitingTimeForRound { get; } = TimeSpan.FromMinutes(10);

	private async Task<RoundState> WaitForRoundAsync(uint256 excludeRound, CancellationToken token)
	{
		CoinJoinClientProgress.SafeInvoke(this, new WaitingForRound());

		using CancellationTokenSource cts = new(MaxWaitingTimeForRound);
		using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, cts.Token);

		return await RoundStatusUpdater
			.CreateRoundAwaiterAsync(
				roundState =>
					roundState.InputRegistrationEnd - DateTimeOffset.UtcNow > DoNotRegisterInLastMinuteTimeLimit
					&& roundState.CoinjoinState.Parameters.AllowedOutputAmounts.Min < MinimumOutputAmountSanity
					&& roundState.Phase == Phase.InputRegistration
					&& !roundState.IsBlame
					&& roundState.Id != excludeRound,
				linkedCts.Token)
			.ConfigureAwait(false);
	}

	private async Task<RoundState> WaitForBlameRoundAsync(uint256 blameRoundId, CancellationToken token)
	{
		var timeout = TimeSpan.FromMinutes(5);
		CoinJoinClientProgress.SafeInvoke(this, new WaitingForBlameRound(DateTimeOffset.UtcNow + timeout));

		using CancellationTokenSource waitForBlameRoundCts = new(timeout);
		using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(waitForBlameRoundCts.Token, token);

		var roundState = await RoundStatusUpdater
				.CreateRoundAwaiterAsync(
					roundState => roundState.BlameOf == blameRoundId,
					linkedCts.Token)
				.ConfigureAwait(false);

		if (roundState.Phase is not Phase.InputRegistration)
		{
			throw new InvalidOperationException($"Blame Round ({roundState.Id}): Abandoning: the round is not in Input Registration but in '{roundState.Phase}'.");
		}

		if (roundState.CoinjoinState.Parameters.AllowedOutputAmounts.Min >= MinimumOutputAmountSanity)
		{
			throw new InvalidOperationException($"Blame Round ({roundState.Id}): Abandoning: the minimum output amount is too high.");
		}

		if (!roundState.IsBlame && !IsRoundEconomic(roundState.CoinjoinState.Parameters.MiningFeeRate, RoundStatusUpdater.CoinJoinFeeRateMedians, FeeRateMedianTimeFrame))
		{
			throw new InvalidOperationException($"Blame Round ({roundState.Id}): Abandoning: the round is not economic.");
		}

		return roundState;
	}

	public async Task<CoinJoinResult> StartCoinJoinAsync(Func<Task<IEnumerable<SmartCoin>>> coinCandidatesFunc, bool stopWhenAllMixed, CancellationToken cancellationToken)
	{
		RoundState? currentRoundState;
		uint256 excludeRound = uint256.Zero;
		ImmutableList<SmartCoin> coins;
		IEnumerable<SmartCoin> coinCandidates;

		do
		{
			// Sanity check if we would get coins at all otherwise this will throw.
			await coinCandidatesFunc().ConfigureAwait(false);

			currentRoundState = await WaitForRoundAsync(excludeRound, cancellationToken).ConfigureAwait(false);
			RoundParameters roundParameters = currentRoundState.CoinjoinState.Parameters;

			if (!currentRoundState.IsBlame)
			{
				if (!IsRoundEconomic(roundParameters.MiningFeeRate, RoundStatusUpdater.CoinJoinFeeRateMedians, FeeRateMedianTimeFrame))
				{
					string roundSkippedMessage = "Uneconomical round skipped.";
					currentRoundState.LogInfo(roundSkippedMessage);
					throw new CoinJoinClientException(CoinjoinError.UneconomicalRound, roundSkippedMessage);
				}
				if (SkipFactors.ShouldSkipRoundRandomly(SecureRandom, roundParameters.MiningFeeRate, RoundStatusUpdater.CoinJoinFeeRateMedians, currentRoundState.Id))
				{
					string roundSkippedMessage = "Round skipped randomly for better privacy.";
					currentRoundState.LogInfo(roundSkippedMessage);
					throw new CoinJoinClientException(CoinjoinError.RandomlySkippedRound, roundSkippedMessage);
				}
			}

			coinCandidates = await coinCandidatesFunc().ConfigureAwait(false);

			var liquidityClue = LiquidityClueProvider.GetLiquidityClue(roundParameters.MaxSuggestedAmount);
			var utxoSelectionParameters = UtxoSelectionParameters.FromRoundParameters(roundParameters, OutputProvider.DestinationProvider.SupportedScriptTypes.ToArray());

			coins = CoinJoinCoinSelector.SelectCoinsForRound(coinCandidates, utxoSelectionParameters, liquidityClue);

			if (!roundParameters.AllowedInputTypes.Contains(ScriptType.P2WPKH) || !roundParameters.AllowedOutputTypes.Contains(ScriptType.P2WPKH))
			{
				excludeRound = currentRoundState.Id;
				currentRoundState.LogInfo("Skipping the round since it doesn't support P2WPKH inputs and outputs.");

				continue;
			}

			if (roundParameters.MaxSuggestedAmount != default && coins.Any(c => c.Amount > roundParameters.MaxSuggestedAmount))
			{
				excludeRound = currentRoundState.Id;
				currentRoundState.LogInfo($"Skipping the round for more optimal mixing. Max suggested amount is '{roundParameters.MaxSuggestedAmount}' BTC, biggest coin amount is: '{coins.Select(c => c.Amount).Max()}' BTC.");

				continue;
			}

			break;
		}
		while (!cancellationToken.IsCancellationRequested);

		if (coins.IsEmpty)
		{
			throw new CoinJoinClientException(CoinjoinError.NoCoinsEligibleToMix, $"No coin was selected from '{coinCandidates.Count()}' number of coins. Probably it was not economical, total amount of coins were: {Money.Satoshis(coinCandidates.Sum(c => c.Amount))} BTC.");
		}

		// Keep going to blame round until there's none, so CJs won't be DDoS-ed.
		while (true)
		{
			using CancellationTokenSource coinJoinRoundTimeoutCts = new(
				currentRoundState.InputRegistrationTimeout +
				currentRoundState.CoinjoinState.Parameters.ConnectionConfirmationTimeout +
				currentRoundState.CoinjoinState.Parameters.OutputRegistrationTimeout +
				currentRoundState.CoinjoinState.Parameters.TransactionSigningTimeout +
				ExtraRoundTimeoutMargin);
			using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, coinJoinRoundTimeoutCts.Token);

			var result = await StartRoundAsync(coins, currentRoundState, linkedCts.Token).ConfigureAwait(false);

			switch (result)
			{
				case DisruptedCoinJoinResult info:
					// Only use successfully registered coins in the blame round.
					coins = info.SignedCoins;

					currentRoundState.LogInfo("Waiting for the blame round.");
					currentRoundState = await WaitForBlameRoundAsync(currentRoundState.Id, cancellationToken).ConfigureAwait(false);
					break;

				case SuccessfulCoinJoinResult success:
					return success;

				case FailedCoinJoinResult failure:
					return failure;

				default:
					throw new InvalidOperationException("The coinjoin result type was not handled.");
			}
		}

		throw new InvalidOperationException("Blame rounds were not successful.");
	}

	public async Task<CoinJoinResult> StartRoundAsync(IEnumerable<SmartCoin> smartCoins, RoundState roundState, CancellationToken cancellationToken)
	{
		var roundId = roundState.Id;

		// the task is watching if the round ends during operations. If it does it will trigger cancellation.
		using CancellationTokenSource waitRoundEndedTaskCts = new();
		using CancellationTokenSource roundEndedCts = new();
		var waitRoundEndedTask = Task.Run(async () =>
		{
			using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(waitRoundEndedTaskCts.Token, cancellationToken);
			var rs = await RoundStatusUpdater.CreateRoundAwaiterAsync(roundId, Phase.Ended, linkedCts.Token).ConfigureAwait(false);

			// Indicate that the round was ended. Cancel ongoing operations those are using this CTS.
			roundEndedCts.Cancel();
			return rs;
		});

		try
		{
			ImmutableArray<AliceClient> aliceClientsThatSigned = ImmutableArray<AliceClient>.Empty;
			IEnumerable<TxOut> outputTxOuts = Enumerable.Empty<TxOut>();
			Transaction? unsignedCoinJoin = null;
			try
			{
				using CancellationTokenSource cancelOrRoundEndedCts = CancellationTokenSource.CreateLinkedTokenSource(roundEndedCts.Token, cancellationToken);
				(aliceClientsThatSigned, outputTxOuts, unsignedCoinJoin) = await ProceedWithRoundAsync(roundState, smartCoins, cancelOrRoundEndedCts.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				// If the Round ended we let the execution continue, otherwise throw.
				if (!roundEndedCts.IsCancellationRequested)
				{
					throw;
				}
			}
			catch (UnexpectedRoundPhaseException ex) when (ex.Actual == Phase.Ended)
			{
				// Do nothing - if the actual state of the round is Ended we let the execution continue.
			}

			var signedCoins = aliceClientsThatSigned.Select(a => a.SmartCoin).ToImmutableList();

			try
			{
				roundState = await waitRoundEndedTask.ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				roundState.Log(LogLevel.Warning, $"Waiting for the round to end failed with: '{ex}'.");
				throw new UnknownRoundEndingException(signedCoins, outputTxOuts.Select(o => o.ScriptPubKey).ToImmutableList(), ex);
			}

			var hash = unsignedCoinJoin is { } tx ? tx.GetHash().ToString() : "Not available";

			var msg = roundState.EndRoundState switch
			{
				EndRoundState.TransactionBroadcasted => $"Broadcasted. Coinjoin TxId: ({hash})",
				EndRoundState.TransactionBroadcastFailed => $"Failed to broadcast. Coinjoin TxId: ({hash})",
				EndRoundState.AbortedWithError => "Round abnormally finished.",
				EndRoundState.AbortedNotEnoughAlices => "Aborted. Not enough participants.",
				EndRoundState.AbortedNotEnoughAlicesSigned => "Aborted. Not enough participants signed the coinjoin transaction.",
				EndRoundState.NotAllAlicesSign => "Aborted. Some Alices didn't sign. Go to blame round.",
				EndRoundState.AbortedNotAllAlicesConfirmed => "Aborted. Some Alices didn't confirm.",
				EndRoundState.AbortedLoadBalancing => "Aborted. Load balancing registrations.",
				EndRoundState.None => "Unknown.",
				_ => throw new ArgumentOutOfRangeException(nameof(roundState))
			};

			roundState.LogInfo(msg);

			// Coinjoin succeeded but wallet had no input in it.
			if (signedCoins.IsEmpty && roundState.EndRoundState == EndRoundState.TransactionBroadcasted)
			{
				throw new CoinJoinClientException(CoinjoinError.UserWasntInRound, "No inputs participated in this round.");
			}

			return roundState.EndRoundState switch
			{
				EndRoundState.TransactionBroadcasted => new SuccessfulCoinJoinResult(
					Coins: signedCoins,
					OutputScripts: outputTxOuts.Select(o => o.ScriptPubKey).ToImmutableList(),
					UnsignedCoinJoin: unsignedCoinJoin!),
				EndRoundState.NotAllAlicesSign => new DisruptedCoinJoinResult(signedCoins),
				_ => new FailedCoinJoinResult()
			};
		}
		finally
		{
			// Cancel and handle the task.
			waitRoundEndedTaskCts.Cancel();
			try
			{
				await waitRoundEndedTask.ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				// Make sure that to not generate UnobservedTaskException.
				roundState.LogDebug(ex.Message);
			}

			CoinJoinClientProgress.SafeInvoke(this, new LeavingCriticalPhase());

			// Try to update to the latest roundState.
			var currentRoundState = RoundStatusUpdater.TryGetRoundState(roundState.Id, out var latestRoundState) ? latestRoundState : roundState;
			CoinJoinClientProgress.SafeInvoke(this, new RoundEnded(currentRoundState));
		}
	}

	private async Task<(ImmutableArray<AliceClient> aliceClientsThatSigned, IEnumerable<TxOut> OutputTxOuts, Transaction UnsignedCoinJoin)> ProceedWithRoundAsync(RoundState roundState, IEnumerable<SmartCoin> smartCoins, CancellationToken cancellationToken)
	{
		ImmutableArray<(AliceClient AliceClient, PersonCircuit PersonCircuit)> registeredAliceClientAndCircuits = ImmutableArray<(AliceClient, PersonCircuit)>.Empty;
		try
		{
			var roundId = roundState.Id;

			registeredAliceClientAndCircuits = await ProceedWithInputRegAndConfirmAsync(smartCoins, roundState, cancellationToken).ConfigureAwait(false);
			if (!registeredAliceClientAndCircuits.Any())
			{
				throw new CoinJoinClientException(CoinjoinError.CoinsRejected, $"The coordinator rejected all {smartCoins.Count()} inputs.");
			}

			roundState.LogInfo($"Successfully registered {registeredAliceClientAndCircuits.Length} inputs.");

			var registeredAliceClients = registeredAliceClientAndCircuits.Select(x => x.AliceClient).ToImmutableArray();

			CoinsInCriticalPhase = registeredAliceClients.Select(alice => alice.SmartCoin).ToImmutableList();

			var outputTxOuts = await ProceedWithOutputRegistrationPhaseAsync(roundId, registeredAliceClients, cancellationToken).ConfigureAwait(false);

			var (unsignedCoinJoin, aliceClientsThatSigned) = await ProceedWithSigningStateAsync(roundId, registeredAliceClients, outputTxOuts, cancellationToken).ConfigureAwait(false);
			LogCoinJoinSummary(registeredAliceClients, outputTxOuts, roundState);

			LiquidityClueProvider.UpdateLiquidityClue(roundState.CoinjoinState.Parameters.MaxSuggestedAmount, unsignedCoinJoin, outputTxOuts);

			return (aliceClientsThatSigned, outputTxOuts, unsignedCoinJoin);
		}
		finally
		{
			foreach (var coins in smartCoins)
			{
				coins.CoinJoinInProgress = false;
			}

			foreach (var aliceClientAndCircuit in registeredAliceClientAndCircuits)
			{
				aliceClientAndCircuit.AliceClient.Finish();
				aliceClientAndCircuit.PersonCircuit.Dispose();
			}
		}
	}

	private async Task<ImmutableArray<(AliceClient AliceClient, PersonCircuit PersonCircuit)>> CreateRegisterAndConfirmCoinsAsync(IEnumerable<SmartCoin> smartCoins, RoundState roundState, CancellationToken cancel)
	{
		int eventInvokedAlready = 0;

		UnexpectedRoundPhaseException? lastUnexpectedRoundPhaseException = null;

		var remainingInputRegTime = roundState.InputRegistrationEnd - DateTimeOffset.UtcNow;

		using CancellationTokenSource strictInputRegTimeoutCts = new(remainingInputRegTime);
		using CancellationTokenSource inputRegTimeoutCts = new(remainingInputRegTime + ExtraPhaseTimeoutMargin);
		using CancellationTokenSource connConfTimeoutCts = new(remainingInputRegTime + roundState.CoinjoinState.Parameters.ConnectionConfirmationTimeout + ExtraPhaseTimeoutMargin);
		using CancellationTokenSource registrationsCts = new();
		using CancellationTokenSource confirmationsCts = new();
		using CancellationTokenSource coinBanCheckMode = new();

		using CancellationTokenSource linkedUnregisterCts = CancellationTokenSource.CreateLinkedTokenSource(strictInputRegTimeoutCts.Token, registrationsCts.Token);
		using CancellationTokenSource linkedRegistrationsCts = CancellationTokenSource.CreateLinkedTokenSource(inputRegTimeoutCts.Token, registrationsCts.Token, cancel);
		using CancellationTokenSource linkedConfirmationsCts = CancellationTokenSource.CreateLinkedTokenSource(connConfTimeoutCts.Token, confirmationsCts.Token, cancel);
		using CancellationTokenSource timeoutAndGlobalCts = CancellationTokenSource.CreateLinkedTokenSource(inputRegTimeoutCts.Token, connConfTimeoutCts.Token, cancel);

		async Task<(AliceClient? AliceClient, PersonCircuit? PersonCircuit)> RegisterInputAsync(SmartCoin coin)
		{
			PersonCircuit? personCircuit = null;
			bool disposeCircuit = true;
			try
			{
				var (newPersonCircuit, httpClient) = HttpClientFactory.NewHttpClientWithPersonCircuit();
				personCircuit = newPersonCircuit;

				// Alice client requests are inherently linkable to each other, so the circuit can be reused
				var arenaRequestHandler = new WabiSabiHttpApiClient(httpClient);

				var aliceArenaClient = new ArenaClient(
					roundState.CreateAmountCredentialClient(SecureRandom),
					roundState.CreateVsizeCredentialClient(SecureRandom),
					CoordinatorIdentifier,
					arenaRequestHandler);

				var aliceClient = await AliceClient.CreateRegisterAndConfirmInputAsync(roundState, aliceArenaClient, coin, KeyChain, RoundStatusUpdater, linkedUnregisterCts.Token, linkedRegistrationsCts.Token, linkedConfirmationsCts.Token, coinBanCheckMode.Token).ConfigureAwait(false);

				// Right after the first real-cred confirmation happened we entered into critical phase.
				if (Interlocked.Exchange(ref eventInvokedAlready, 1) == 0)
				{
					CoinJoinClientProgress.SafeInvoke(this, new EnteringCriticalPhase());
				}

				// Do not dispose the circuit, it will be used later.
				disposeCircuit = false;
				return (aliceClient, personCircuit);
			}
			catch (WabiSabiProtocolException wpe)
			{
				switch (wpe.ErrorCode)
				{
					case WabiSabiProtocolErrorCode.RoundNotFound:
						// if the round does not exist then it ended/aborted.
						roundState.LogInfo($"{coin.Coin.Outpoint} arrived too late because the round doesn't exist anymore. Aborting input registrations: '{WabiSabiProtocolErrorCode.RoundNotFound}'.");
						registrationsCts.Cancel();
						confirmationsCts.Cancel();
						break;

					case WabiSabiProtocolErrorCode.WrongPhase:
						if (wpe.ExceptionData is WrongPhaseExceptionData wrongPhaseExceptionData)
						{
							roundState.LogInfo($"{coin.Coin.Outpoint} arrived too late. Aborting input registrations: '{WabiSabiProtocolErrorCode.WrongPhase}'.");
							if (wrongPhaseExceptionData.CurrentPhase != Phase.InputRegistration)
							{
								// Cancel all remaining pending input registrations because they will arrive late too.
								registrationsCts.Cancel();

								if (wrongPhaseExceptionData.CurrentPhase != Phase.ConnectionConfirmation)
								{
									// Cancel all remaining pending connection confirmations because they will arrive late too.
									confirmationsCts.Cancel();
								}
							}
						}
						else
						{
							throw new InvalidOperationException(
								$"Unexpected condition. {nameof(WrongPhaseException)} doesn't contain a {nameof(WrongPhaseExceptionData)} data field.");
						}
						break;

					case WabiSabiProtocolErrorCode.AliceAlreadyRegistered:
						roundState.LogInfo($"{coin.Coin.Outpoint} was already registered.");
						break;

					case WabiSabiProtocolErrorCode.AliceAlreadyConfirmedConnection:
						roundState.LogInfo($"{coin.Coin.Outpoint} already confirmed connection.");
						break;

					case WabiSabiProtocolErrorCode.InputSpent:
						coin.SpentAccordingToBackend = true;
						roundState.LogInfo($"{coin.Coin.Outpoint} is spent according to the backend. The wallet is not fully synchronized or corrupted.");
						break;

					case WabiSabiProtocolErrorCode.InputBanned or WabiSabiProtocolErrorCode.InputLongBanned:
						var inputBannedExData = wpe.ExceptionData as InputBannedExceptionData;
						if (inputBannedExData is null)
						{
							Logger.LogError($"{nameof(InputBannedExceptionData)} is missing.");
						}
						var bannedUntil = inputBannedExData?.BannedUntil ?? DateTimeOffset.UtcNow + TimeSpan.FromDays(1);
						if (bannedUntil < DateTimeOffset.UtcNow + TimeSpan.FromMinutes(30))
						{
							bannedUntil = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(30 + SecureRandom.Instance.GetInt(0, 30));
						}
						CoinJoinClientProgress.SafeInvoke(this, new CoinBanned(coin, bannedUntil));
						roundState.LogInfo($"{coin.Coin.Outpoint} is banned until {bannedUntil}.");
						if (!coinBanCheckMode.IsCancellationRequested)
						{
							roundState.LogInfo($"Switching to banned coin check mode.");
							coinBanCheckMode.Cancel();
						}
						break;

					case WabiSabiProtocolErrorCode.InputNotWhitelisted:
						coin.SpentAccordingToBackend = false;
						Logger.LogWarning($"{coin.Coin.Outpoint} cannot be registered in the blame round.");
						break;

					default:
						roundState.LogInfo($"{coin.Coin.Outpoint} cannot be registered: '{wpe.ErrorCode}'.");
						break;
				}
			}
			catch (OperationCanceledException ex)
			{
				if (cancel.IsCancellationRequested)
				{
					Logger.LogDebug("User requested cancellation of registration and confirmation.");
				}
				else if (registrationsCts.IsCancellationRequested)
				{
					Logger.LogDebug("Registration was cancelled.");
				}
				else if (connConfTimeoutCts.IsCancellationRequested)
				{
					Logger.LogDebug("Connection confirmation was cancelled.");
				}
				else
				{
					Logger.LogDebug(ex);
				}
			}
			catch (UnexpectedRoundPhaseException ex)
			{
				lastUnexpectedRoundPhaseException = ex;
				Logger.LogTrace(ex);
			}
			catch (Exception ex)
			{
				Logger.LogWarning(ex);
			}
			finally
			{
				if (disposeCircuit)
				{
					personCircuit?.Dispose();
				}
			}

			// In case of any exception.
			return (null, null);
		}

		// Gets the list of scheduled dates/time in the remaining available time frame when each alice has to be registered.
		var remainingTimeForRegistration = roundState.InputRegistrationEnd - DateTimeOffset.UtcNow;

		roundState.LogDebug($"Inputs({smartCoins.Count()}) registration started - it will end in: {remainingTimeForRegistration:hh\\:mm\\:ss}.");

		// Decrease the available time, so the clients hurry up.
		var safetyBuffer = TimeSpan.FromMinutes(1);
		var remainingTime = roundState.InputRegistrationEnd - safetyBuffer;
		var scheduledDates = remainingTime.GetScheduledDates(smartCoins.Count());

		// Creates scheduled tasks (tasks that wait until the specified date/time and then perform the real registration)
		var aliceClients = smartCoins.Zip(
			scheduledDates,
			async (coin, date) =>
			{
				var delay = date - DateTimeOffset.UtcNow;
				if (delay > TimeSpan.Zero)
				{
					await Task.Delay(delay, timeoutAndGlobalCts.Token).ConfigureAwait(false);
				}
				return await RegisterInputAsync(coin).ConfigureAwait(false);
			})
			.ToImmutableArray();

		await Task.WhenAll(aliceClients).ConfigureAwait(false);

		var successfulAlices = aliceClients
			.Select(x => x.Result)
			.Where(r => r.AliceClient is not null && r.PersonCircuit is not null)
			.Select(r => (r.AliceClient!, r.PersonCircuit!))
			.ToImmutableArray();

		if (!successfulAlices.Any() && lastUnexpectedRoundPhaseException is { })
		{
			// In this case the coordinator aborted the round - throw only one exception and log outside.
			throw lastUnexpectedRoundPhaseException;
		}

		return successfulAlices;
	}

	private BobClient CreateBobClient(RoundState roundState)
	{
		var arenaRequestHandler = new WabiSabiHttpApiClient(HttpClientFactory.NewHttpClientWithCircuitPerRequest());

		return new BobClient(
			roundState.Id,
			new(
				roundState.CreateAmountCredentialClient(SecureRandom),
				roundState.CreateVsizeCredentialClient(SecureRandom),
				CoordinatorIdentifier,
				arenaRequestHandler));
	}

	internal static bool SanityCheck(IEnumerable<TxOut> expectedOutputs, IEnumerable<TxOut> coinJoinOutputs)
	{
		bool AllExpectedScriptsArePresent() =>
			coinJoinOutputs
				.Select(x => x.ScriptPubKey)
				.IsSuperSetOf(expectedOutputs.Select(x => x.ScriptPubKey));

		bool AllOutputsHaveAtLeastTheExpectedValue() =>
			coinJoinOutputs
				.Join(
					expectedOutputs,
					x => x.ScriptPubKey,
					x => x.ScriptPubKey,
					(coinjoinOutput, expectedOutput) => coinjoinOutput.Value - expectedOutput.Value)
				.All(x => x >= Money.Zero);

		return AllExpectedScriptsArePresent() && AllOutputsHaveAtLeastTheExpectedValue();
	}

	private async Task SignTransactionAsync(
		IEnumerable<AliceClient> aliceClients,
		TransactionWithPrecomputedData unsignedCoinJoinTransaction,
		DateTimeOffset signingStartTime,
		DateTimeOffset signingEndTime,
		CancellationToken cancellationToken)
	{
		// Maximum signing request delay is 50 seconds, because
		// - the fast track signing phase will be 1m 30s, so we want to give a decent time for the requests to be sent out.
		var maximumSigningRequestDelay = TimeSpan.FromSeconds(50);
		var scheduledDates = signingEndTime.GetScheduledDates(aliceClients.Count(), signingStartTime, maximumSigningRequestDelay);

		var tasks = aliceClients.Zip(
			scheduledDates,
			async (aliceClient, scheduledDate) =>
			{
				var delay = scheduledDate - DateTimeOffset.UtcNow;
				if (delay > TimeSpan.Zero)
				{
					await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
				}
				try
				{
					await aliceClient.SignTransactionAsync(unsignedCoinJoinTransaction, KeyChain, cancellationToken).ConfigureAwait(false);
				}
				catch (WabiSabiProtocolException ex) when (ex.ErrorCode == WabiSabiProtocolErrorCode.WitnessAlreadyProvided)
				{
					Logger.LogDebug("Signature was already sent - bypassing error.", ex);
				}
			})
			.ToImmutableArray();

		await Task.WhenAll(tasks).ConfigureAwait(false);
	}

	private async Task ReadyToSignAsync(IEnumerable<AliceClient> aliceClients, DateTimeOffset readyToSignEndTime, CancellationToken cancellationToken)
	{
		var scheduledDates = readyToSignEndTime.GetScheduledDates(aliceClients.Count(), DateTimeOffset.UtcNow, MaximumRequestDelay);

		var tasks = aliceClients.Zip(
			scheduledDates,
			async (aliceClient, scheduledDate) =>
			{
				var delay = scheduledDate - DateTimeOffset.UtcNow;
				if (delay > TimeSpan.Zero)
				{
					await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
				}

				try
				{
					await aliceClient.ReadyToSignAsync(cancellationToken).ConfigureAwait(false);
				}
				catch (Exception e)
				{
					// This cannot fail. Otherwise the whole conjoin process will be halted.
					Logger.LogDebug(e.ToString());
					Logger.LogInfo($"Failed to register signal ready to sign with message {e.Message}. Ignoring...");
				}
			})
			.ToImmutableArray();

		await Task.WhenAll(tasks).ConfigureAwait(false);
	}

	internal virtual ImmutableList<DateTimeOffset> GetScheduledDates(int howMany, DateTimeOffset startTime, DateTimeOffset endTime, TimeSpan maximumRequestDelay)
	{
		return endTime.GetScheduledDates(howMany, startTime, maximumRequestDelay);
	}

	private void LogCoinJoinSummary(ImmutableArray<AliceClient> registeredAliceClients, IEnumerable<TxOut> myOutputs, RoundState roundState)
	{
		RoundParameters roundParameters = roundState.CoinjoinState.Parameters;
		FeeRate feeRate = roundParameters.MiningFeeRate;

		var totalEffectiveInputAmount = Money.Satoshis(registeredAliceClients.Sum(a => a.EffectiveValue));
		var totalEffectiveOutputAmount = Money.Satoshis(myOutputs.Sum(a => a.Value - feeRate.GetFee(a.ScriptPubKey.EstimateOutputVsize())));
		var effectiveDifference = totalEffectiveInputAmount - totalEffectiveOutputAmount;

		var totalInputAmount = Money.Satoshis(registeredAliceClients.Sum(a => a.SmartCoin.Amount));
		var totalOutputAmount = Money.Satoshis(myOutputs.Sum(a => a.Value));
		var totalDifference = Money.Satoshis(totalInputAmount - totalOutputAmount);

		var inputNetworkFee = Money.Satoshis(registeredAliceClients.Sum(alice => feeRate.GetFee(alice.SmartCoin.Coin.ScriptPubKey.EstimateInputVsize())));
		var outputNetworkFee = Money.Satoshis(myOutputs.Sum(output => feeRate.GetFee(output.ScriptPubKey.EstimateOutputVsize())));
		var totalNetworkFee = inputNetworkFee + outputNetworkFee;
		var totalCoordinationFee = Money.Satoshis(registeredAliceClients.Where(a => !a.IsCoordinationFeeExempted).Sum(a => roundParameters.CoordinationFeeRate.GetFee(a.SmartCoin.Amount)));

		string[] summary = new string[]
		{
			"",
			$"\tInput total : {totalInputAmount.ToString(true, false)} Eff: {totalEffectiveInputAmount.ToString(true, false)} NetworkFee: {inputNetworkFee.ToString(true, false)} CoordFee: {totalCoordinationFee.ToString(true)}",
			$"\tOutput total: {totalOutputAmount.ToString(true, false)} Eff: {totalEffectiveOutputAmount.ToString(true, false)} NetworkFee: {outputNetworkFee.ToString(true, false)}",
			$"\tTotal diff  : {totalDifference.ToString(true, false)}",
			$"\tEffect diff : {effectiveDifference.ToString(true, false)}",
			$"\tTotal fee   : {totalNetworkFee.ToString(true, false)}"
		};

		roundState.LogDebug(string.Join(Environment.NewLine, summary));
	}

	internal static bool IsRoundEconomic(FeeRate roundFeeRate, Dictionary<TimeSpan, FeeRate> coinJoinFeeRateMedians, TimeSpan feeRateMedianTimeFrame)
	{
		if (feeRateMedianTimeFrame == default)
		{
			return true;
		}

		if (coinJoinFeeRateMedians.ContainsKey(feeRateMedianTimeFrame))
		{
			// Round is not economic if any TimeFrame lower than FeeRateMedianTimeFrame has a FeeRate lower than current round's FeeRate.
			// 0.5 satoshi difference is allowable, to avoid rounding errors.
			return coinJoinFeeRateMedians
				.Where(x => x.Key <= feeRateMedianTimeFrame)
				.All(lowerTimeFrame => roundFeeRate.SatoshiPerByte <= lowerTimeFrame.Value.SatoshiPerByte + 0.5m);
		}

		throw new InvalidOperationException($"Could not find median fee rate for time frame: {feeRateMedianTimeFrame}.");
	}

	private async Task<IEnumerable<TxOut>> ProceedWithOutputRegistrationPhaseAsync(uint256 roundId, ImmutableArray<AliceClient> registeredAliceClients, CancellationToken cancellationToken)
	{
		// Waiting for OutputRegistration phase, all the Alices confirmed their connections, so the list of the inputs will be complete.
		var roundState = await RoundStatusUpdater.CreateRoundAwaiterAsync(roundId, Phase.OutputRegistration, cancellationToken).ConfigureAwait(false);
		var roundParameters = roundState.CoinjoinState.Parameters;
		var remainingTime = roundParameters.OutputRegistrationTimeout - RoundStatusUpdater.Period;
		var now = DateTimeOffset.UtcNow;
		var outputRegistrationPhaseEndTime = now + remainingTime;

		// Splitting the remaining time.
		// Both operations are done under output registration phase, so we have to do the random timing taking that into account.
		var outputRegistrationEndTime = now + (remainingTime * 0.8); // 80% of the time.
		var readyToSignEndTime = outputRegistrationEndTime + (remainingTime * 0.2); // 20% of the time.

		CoinJoinClientProgress.SafeInvoke(this, new EnteringOutputRegistrationPhase(roundState, outputRegistrationPhaseEndTime));

		using CancellationTokenSource phaseTimeoutCts = new(remainingTime + ExtraPhaseTimeoutMargin);
		using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, phaseTimeoutCts.Token);

		var registeredCoins = registeredAliceClients.Select(x => x.SmartCoin.Coin);
		var inputEffectiveValuesAndSizes = registeredAliceClients.Select(x => (x.EffectiveValue, x.SmartCoin.ScriptPubKey.EstimateInputVsize()));
		var availableVsize = registeredAliceClients.SelectMany(x => x.IssuedVsizeCredentials).Sum(x => x.Value);

		// Calculate outputs values
		var constructionState = roundState.Assert<ConstructionState>();

		var theirCoins = constructionState.Inputs.Where(x => !registeredCoins.Any(y => x.Outpoint == y.Outpoint));
		var registeredCoinEffectiveValues = registeredAliceClients.Select(x => x.EffectiveValue);
		var theirCoinEffectiveValues = theirCoins.Select(x => x.EffectiveValue(roundParameters.MiningFeeRate, roundParameters.CoordinationFeeRate));

		var outputTxOuts = OutputProvider.GetOutputs(roundId, roundParameters, registeredCoinEffectiveValues, theirCoinEffectiveValues, (int)availableVsize).ToArray();

		DependencyGraph dependencyGraph = DependencyGraph.ResolveCredentialDependencies(inputEffectiveValuesAndSizes, outputTxOuts, roundParameters.MiningFeeRate, roundParameters.MaxVsizeAllocationPerAlice);
		DependencyGraphTaskScheduler scheduler = new(dependencyGraph);

		var combinedToken = linkedCts.Token;
		try
		{
			// Re-issuances.
			var bobClient = CreateBobClient(roundState);
			roundState.LogInfo("Starting reissuances.");
			await scheduler.StartReissuancesAsync(registeredAliceClients, bobClient, combinedToken).ConfigureAwait(false);

			// Output registration.
			roundState.LogDebug($"Output registration started - it will end in: {outputRegistrationEndTime - DateTimeOffset.UtcNow:hh\\:mm\\:ss}.");

			var outputRegistrationScheduledDates = outputRegistrationEndTime.GetScheduledDates(outputTxOuts.Length, DateTimeOffset.UtcNow, MaximumRequestDelay);
			var registrationResult = await scheduler.StartOutputRegistrationsAsync(outputTxOuts, bobClient, outputRegistrationScheduledDates, combinedToken).ConfigureAwait(false);
			registrationResult.MatchDo(
				OnOutputRegistrationSuccess,
				OnOutputRegistrationErrors);
		}
		catch (Exception e)
		{
			roundState.LogInfo($"Failed to register outputs with message {e.Message}. Ignoring...");
			roundState.LogDebug(e.ToString());
		}

		// ReadyToSign.
		roundState.LogDebug($"ReadyToSign phase started - it will end in: {readyToSignEndTime - DateTimeOffset.UtcNow:hh\\:mm\\:ss}.");
		await ReadyToSignAsync(registeredAliceClients, readyToSignEndTime, combinedToken).ConfigureAwait(false);
		roundState.LogDebug($"Alices({registeredAliceClients.Length}) are ready to sign.");
		return outputTxOuts;

		void OnOutputRegistrationSuccess(Unit _) =>
			roundState.LogInfo($"Outputs({outputTxOuts.Length}) were registered.");

		void OnOutputRegistrationErrors(DependencyGraphTaskScheduler.OutputRegistrationError[] errors)
		{
			foreach (var e in errors)
			{
				switch (e)
				{
					case DependencyGraphTaskScheduler.UnknownError s:
						roundState.LogInfo($"Script ({s.ScriptPubKey}) registration failed by unknown reasons. Continuing...");
						break;
					case DependencyGraphTaskScheduler.AlreadyRegisteredScriptError s:
						OutputProvider.DestinationProvider.TrySetScriptStates(KeyState.Used, [s.ScriptPubKey]);
						roundState.LogInfo($"Script ({s.ScriptPubKey}) was already registered. Continuing...");
						break;
				}
			}
		}
	}

	private async Task<(Transaction UnsignedCoinJoin, ImmutableArray<AliceClient> AliceClientsThatSigned)> ProceedWithSigningStateAsync(
		uint256 roundId,
		ImmutableArray<AliceClient> registeredAliceClients,
		IEnumerable<TxOut> outputTxOuts,
		CancellationToken cancellationToken)
	{
		// Signing.
		var roundState = await RoundStatusUpdater.CreateRoundAwaiterAsync(roundId, Phase.TransactionSigning, cancellationToken).ConfigureAwait(false);
		var remainingTime = roundState.CoinjoinState.Parameters.TransactionSigningTimeout - RoundStatusUpdater.Period;
		var signingStateEndTime = DateTimeOffset.UtcNow + remainingTime;

		CoinJoinClientProgress.SafeInvoke(this, new EnteringSigningPhase(roundState, signingStateEndTime));

		using CancellationTokenSource phaseTimeoutCts = new(remainingTime + ExtraPhaseTimeoutMargin);
		using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, phaseTimeoutCts.Token);

		roundState.LogDebug($"Transaction signing phase started - it will end in: {signingStateEndTime - DateTimeOffset.UtcNow:hh\\:mm\\:ss}.");

		var signingState = roundState.Assert<SigningState>();
		var unsignedCoinJoin = signingState.CreateUnsignedTransactionWithPrecomputedData();

		// If everything is okay, then sign all the inputs. Otherwise, in case there are missing outputs, the server is
		// lying (it lied us before when it responded with 200 OK to the OutputRegistration requests or it is lying us
		// now when we identify as satoshi.
		// In this scenario we should ban the coordinator and stop dealing with it.
		// see more: https://github.com/zkSNACKs/WalletWasabi/issues/8171
		bool mustSignAllInputs = SanityCheck(outputTxOuts, unsignedCoinJoin.Transaction.Outputs);
		if (!mustSignAllInputs)
		{
			roundState.LogInfo($"There are missing outputs. A subset of inputs will be signed.");
		}

		// Send signature.
		var combinedToken = linkedCts.Token;
		var alicesToSign = mustSignAllInputs
			? registeredAliceClients
			: registeredAliceClients.RemoveAt(SecureRandom.GetInt(0, registeredAliceClients.Length));

		var delayBeforeSigning = TimeSpan.FromSeconds(roundState.CoinjoinState.Parameters.DelayTransactionSigning ? 50 : 0);
		var signingStateStartTime = DateTimeOffset.UtcNow + delayBeforeSigning;
		await SignTransactionAsync(alicesToSign, unsignedCoinJoin, signingStateStartTime, signingStateEndTime, combinedToken).ConfigureAwait(false);
		roundState.LogInfo($"{alicesToSign.Length} out of {registeredAliceClients.Length} Alices have signed the coinjoin tx.");

		return (unsignedCoinJoin.Transaction, alicesToSign);
	}

	private async Task<ImmutableArray<(AliceClient, PersonCircuit)>> ProceedWithInputRegAndConfirmAsync(IEnumerable<SmartCoin> smartCoins, RoundState roundState, CancellationToken cancellationToken)
	{
		// Because of the nature of the protocol, the input registration and the connection confirmation phases are done subsequently thus they have a merged timeout.
		var timeUntilOutputReg = roundState.InputRegistrationEnd - DateTimeOffset.UtcNow + roundState.CoinjoinState.Parameters.ConnectionConfirmationTimeout;

		using CancellationTokenSource timeUntilOutputRegCts = new(timeUntilOutputReg + ExtraPhaseTimeoutMargin);
		using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeUntilOutputRegCts.Token);

		CoinJoinClientProgress.SafeInvoke(this, new EnteringInputRegistrationPhase(roundState, roundState.InputRegistrationEnd));

		// Register coins.
		var result = await CreateRegisterAndConfirmCoinsAsync(smartCoins, roundState, cancellationToken).ConfigureAwait(false);

		if (!RoundStatusUpdater.TryGetRoundState(roundState.Id, out var newRoundState))
		{
			throw new InvalidOperationException($"Round '{roundState.Id}' is missing.");
		}

		if (!result.IsDefaultOrEmpty)
		{
			// Be aware: at this point we are already in connection confirmation and all the coins got their first confirmation, so this is not exactly the starting time of the phase.
			var estimatedRemainingFromConnectionConfirmation = DateTimeOffset.UtcNow + roundState.CoinjoinState.Parameters.ConnectionConfirmationTimeout;
			CoinJoinClientProgress.SafeInvoke(this, new EnteringConnectionConfirmationPhase(newRoundState, estimatedRemainingFromConnectionConfirmation));
		}

		return result;
	}
}
