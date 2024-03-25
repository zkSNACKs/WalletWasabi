using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using ReactiveUI;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Infrastructure;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.State;
using WalletWasabi.Fluent.ViewModels.Wallets.Settings;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Client.CoinJoinProgressEvents;
using WalletWasabi.WabiSabi.Client.StatusChangedEvents;

namespace WalletWasabi.Fluent.ViewModels.Wallets;

[AppLifetime]
public partial class CoinJoinStateViewModel : ViewModelBase
{
	private const string CountDownMessage = "Awaiting auto-start of coinjoin";
	private const string WaitingMessage = "Awaiting coinjoin";
	private const string UneconomicalRoundMessage = "Awaiting cheaper coinjoins";
	private const string RandomlySkippedRoundMessage = "Skipping a round for better privacy";
	private const string PauseMessage = "Coinjoin is paused";
	private const string StoppedMessage = "Coinjoin has stopped";
	private const string PressPlayToStartMessage = "Press Play to start";
	private const string RoundSucceedMessage = "Coinjoin successful! Continuing...";
	private const string RoundFinishedMessage = "Round ended, awaiting next round";
	private const string AbortedNotEnoughAlicesMessage = "Insufficient participants, retrying...";
	private const string CoinJoinInProgress = "Coinjoin in progress";
	private const string InputRegistrationMessage = "Awaiting other participants";
	private const string WaitingForBlameRoundMessage = "Awaiting the blame round";
	private const string WaitingRoundMessage = "Awaiting a round";
	private const string PlebStopMessage = "Coinjoin may be uneconomical";
	private const string PlebStopMessageBelow = "Add more funds or press Play to bypass";
	private const string NoCoinsEligibleToMixMessage = "Insufficient funds eligible for coinjoin";
	private const string UserInSendWorkflowMessage = "Awaiting closure of send dialog";
	private const string AllPrivateMessage = "Hurray! All your funds are private!";
	private const string BackendNotConnected = "Awaiting connection";
	private const string GeneralErrorMessage = "Awaiting valid conditions";
	private const string WaitingForConfirmedFunds = "Awaiting confirmed funds";
	private const string CoinsRejectedMessage = "Some funds are rejected from coinjoining";
	private const string OnlyImmatureCoinsAvailableMessage = "Only immature funds are available";
	private const string OnlyExcludedCoinsAvailableMessage = "Only excluded funds are available";

	private readonly IWalletModel _wallet;
	private readonly StateMachine<State, Trigger> _stateMachine;
	private readonly DispatcherTimer _countdownTimer;
	private readonly DispatcherTimer _autoCoinJoinStartTimer;

	[AutoNotify] private bool _isAutoWaiting;
	[AutoNotify] private bool _playVisible;
	[AutoNotify] private bool _pauseVisible;
	[AutoNotify] private bool _pauseSpreading;
	[AutoNotify] private bool _stopVisible;
	[AutoNotify] private string _currentStatus = "";
	[AutoNotify] private bool _isProgressReversed;
	[AutoNotify] private double _progressValue;
	[AutoNotify] private string _leftText = "";
	[AutoNotify] private string _rightText = "";
	[AutoNotify] private bool _isInCriticalPhase;
	[AutoNotify] private bool _isCountDownDelayHappening;
	[AutoNotify] private bool _areAllCoinsPrivate;

	private DateTimeOffset _countDownStartTime;
	private DateTimeOffset _countDownEndTime;

	public CoinJoinStateViewModel(UiContext uiContext, IWalletModel wallet, WalletSettingsViewModel settings)
	{
		UiContext = uiContext;
		_wallet = wallet;

		wallet.Coinjoin.StatusUpdated
					   .Do(ProcessStatusChange)
					   .Subscribe();

		wallet.Privacy.IsWalletPrivate
					  .BindTo(this, x => x.AreAllCoinsPrivate);

		var initialState =
			wallet.Settings.AutoCoinjoin
			? State.WaitingForAutoStart
			: State.StoppedOrPaused;

		if (wallet.IsHardwareWallet || wallet.IsWatchOnlyWallet)
		{
			initialState = State.Disabled;
		}

		if (wallet.Settings.IsCoinJoinPaused)
		{
			initialState = State.StoppedOrPaused;
		}

		_stateMachine = new StateMachine<State, Trigger>(initialState);

		ConfigureStateMachine();

		wallet.Balances
			  .Do(_ => _stateMachine.Fire(Trigger.BalanceChanged))
			  .Subscribe();

		this.WhenAnyValue(x => x.AreAllCoinsPrivate)
			.Do(_ => _stateMachine.Fire(Trigger.AreAllCoinsPrivateChanged))
			.Subscribe();

		PlayCommand = ReactiveCommand.CreateFromTask(async () =>
		{
			if (!wallet.Settings.IsCoinjoinProfileSelected)
			{
				await UiContext.Navigate().To().CoinJoinProfiles(wallet.Settings).GetResultAsync();
			}

			if (wallet.Settings.IsCoinjoinProfileSelected)
			{
				var overridePlebStop = _stateMachine.IsInState(State.PlebStopActive);
				await wallet.Coinjoin.StartAsync(stopWhenAllMixed: !IsAutoCoinJoinEnabled, overridePlebStop);
			}
		});

		var stopPauseCommandCanExecute =
			this.WhenAnyValue(
				x => x.IsInCriticalPhase,
				x => x.PauseSpreading,
				(isInCriticalPhase, pauseSpreading) => !isInCriticalPhase && !pauseSpreading);

		StopPauseCommand = ReactiveCommand.CreateFromTask(wallet.Coinjoin.StopAsync, stopPauseCommandCanExecute);

		AutoCoinJoinObservable = wallet.Settings.WhenAnyValue(x => x.AutoCoinjoin);

		AutoCoinJoinObservable
			.Skip(1) // The first one is triggered at the creation.
			.Where(x => !x)
			.Do(_ => _stateMachine.Fire(Trigger.AutoCoinJoinOff))
			.Subscribe();

		wallet.Settings.WhenAnyValue(x => x.PlebStopThreshold)
					   .SubscribeAsync(async _ =>
					   {
						   // Hack: we take the value from KeyManager but it is saved later.
						   await Task.Delay(1500);
						   _stateMachine.Fire(Trigger.PlebStopChanged);
					   });

		_autoCoinJoinStartTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(Random.Shared.Next(5, 16)) };
		_autoCoinJoinStartTimer.Tick += async (_, _) =>
		{
			await wallet.Coinjoin.StartAsync(stopWhenAllMixed: false, false);
			_autoCoinJoinStartTimer.Stop();
		};

		_countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
		_countdownTimer.Tick += (_, _) => _stateMachine.Fire(Trigger.Tick);

		_stateMachine.Start();

		var coinJoinSettingsCommand = ReactiveCommand.Create(
			() =>
			{
				settings.SelectedTab = 1;
				UiContext.Navigate(NavigationTarget.DialogScreen).To(settings);
			},
			Observable.Return(!_wallet.IsWatchOnlyWallet));

		NavigateToSettingsCommand = coinJoinSettingsCommand;
		CanNavigateToCoinjoinSettings = coinJoinSettingsCommand.CanExecute;
	}

	private enum State
	{
		Invalid = 0,
		Disabled,
		StoppedOrPaused,
		Playing,
		PlebStopActive,
		WaitingForAutoStart,
	}

	private enum Trigger
	{
		Invalid = 0,
		PlebStopActivated,
		StartError,
		BalanceChanged,
		Tick,
		PlebStopChanged,
		WalletStartedCoinJoin,
		WalletStoppedCoinJoin,
		AutoCoinJoinOff,
		AreAllCoinsPrivateChanged
	}

	public IObservable<bool> CanNavigateToCoinjoinSettings { get; }

	public ICommand NavigateToSettingsCommand { get; }

	public bool IsAutoCoinJoinEnabled => _wallet.Settings.AutoCoinjoin;

	public IObservable<bool> AutoCoinJoinObservable { get; }

	private bool IsCountDownFinished => GetRemainingTime() <= TimeSpan.Zero;

	private bool IsCounting => _countdownTimer.IsEnabled;

	public ICommand PlayCommand { get; }

	public ICommand StopPauseCommand { get; }

	private void ConfigureStateMachine()
	{
		_stateMachine.Configure(State.Disabled);

		_stateMachine.Configure(State.WaitingForAutoStart)
			.Permit(Trigger.WalletStartedCoinJoin, State.Playing)
			.Permit(Trigger.AutoCoinJoinOff, State.StoppedOrPaused)
			.Permit(Trigger.PlebStopActivated, State.PlebStopActive)
			.Permit(Trigger.StartError, State.Playing)
			.OnEntry(() =>
			{
				PlayVisible = true;
				PauseVisible = false;
				StopVisible = false;
				IsAutoWaiting = true;

				var now = DateTimeOffset.UtcNow;
				var autoStartEnd = now + _autoCoinJoinStartTimer.Interval;
				_autoCoinJoinStartTimer.Start();

				StartCountDown(CountDownMessage, now, autoStartEnd);
			})
			.OnExit(() =>
			{
				IsAutoWaiting = false;
				_autoCoinJoinStartTimer.Stop();
				StopCountDown();
			})
			.OnTrigger(Trigger.Tick, UpdateCountDown);

		_stateMachine.Configure(State.StoppedOrPaused)
			.Permit(Trigger.WalletStartedCoinJoin, State.Playing)
			.Permit(Trigger.PlebStopActivated, State.PlebStopActive)
			.OnEntry(() =>
			{
				StopCountDown();
				PauseVisible = false;
				PauseSpreading = false;
				StopVisible = false;

				// PlayVisible, CurrentStatus and LeftText set inside.
				RefreshButtonAndTextInStateStoppedOrPaused();
			})
			.OnTrigger(Trigger.AreAllCoinsPrivateChanged, () =>
			{
				// Refresh the UI according to AreAllCoinsPrivate, the play button and the left-text.
				RefreshButtonAndTextInStateStoppedOrPaused();
			})
			.OnExit(() => LeftText = "");

		_stateMachine.Configure(State.Playing)
			.Permit(Trigger.WalletStoppedCoinJoin, State.StoppedOrPaused)
			.Permit(Trigger.PlebStopActivated, State.PlebStopActive)
			.OnEntry(() =>
			{
				PlayVisible = false;
				PauseVisible = IsAutoCoinJoinEnabled;
				StopVisible = !IsAutoCoinJoinEnabled;

				CurrentStatus = WaitingMessage;
			})
			.OnTrigger(Trigger.Tick, UpdateCountDown);

		_stateMachine.Configure(State.PlebStopActive)
			.Permit(Trigger.BalanceChanged, State.Playing)
			.Permit(Trigger.PlebStopChanged, State.Playing)
			.Permit(Trigger.WalletStartedCoinJoin, State.Playing)
			.Permit(Trigger.WalletStoppedCoinJoin, State.StoppedOrPaused)
			.Permit(Trigger.StartError, State.Playing)
			.OnEntry(() =>
			{
				PlayVisible = true;
				PauseVisible = false;
				StopVisible = false;

				CurrentStatus = PlebStopMessage;
				LeftText = PlebStopMessageBelow;
			})
			.OnExit(() => LeftText = "");
	}

	private void RefreshButtonAndTextInStateStoppedOrPaused()
	{
		if (IsAutoCoinJoinEnabled)
		{
			PlayVisible = true;
			CurrentStatus = PauseMessage;
			LeftText = PressPlayToStartMessage;
		}
		else if (AreAllCoinsPrivate)
		{
			PlayVisible = false;
			LeftText = "";
			CurrentStatus = AllPrivateMessage;
		}
		else
		{
			PlayVisible = true;
			CurrentStatus = StoppedMessage;
			LeftText = PressPlayToStartMessage;
		}
	}

	private void UpdateCountDown()
	{
		IsCountDownDelayHappening = IsCounting && IsCountDownFinished;

		// This case mostly happens when there is some delay between the client and the server,
		// and the countdown has finished but the client hasn't received any new phase changed message.
		if (IsCountDownDelayHappening)
		{
			LeftText = "Waiting for response";
			RightText = "";
			return;
		}

		var format = @"hh\:mm\:ss";
		LeftText = $"{GetElapsedTime().ToString(format)}";
		RightText = $"-{GetRemainingTime().ToString(format)}";
		ProgressValue = GetPercentage();
	}

	private TimeSpan GetElapsedTime() => DateTimeOffset.UtcNow - _countDownStartTime;

	private TimeSpan GetRemainingTime() => _countDownEndTime - DateTimeOffset.UtcNow;

	private TimeSpan GetTotalTime() => _countDownEndTime - _countDownStartTime;

	private double GetPercentage() => GetElapsedTime().TotalSeconds / GetTotalTime().TotalSeconds * 100;

	private void ProcessStatusChange(StatusChangedEventArgs e)
	{
		switch (e)
		{
			case WalletStartedCoinJoinEventArgs:
				_stateMachine.Fire(Trigger.WalletStartedCoinJoin);
				break;

			case WalletStoppedCoinJoinEventArgs:
				_stateMachine.Fire(Trigger.WalletStoppedCoinJoin);
				break;

			case StartErrorEventArgs start:
				if (start.Error is CoinjoinError.NotEnoughUnprivateBalance)
				{
					_stateMachine.Fire(Trigger.PlebStopActivated);
					break;
				}

				_stateMachine.Fire(Trigger.StartError);
				CurrentStatus = start.Error switch
				{
					CoinjoinError.NoCoinsEligibleToMix => NoCoinsEligibleToMixMessage,
					CoinjoinError.NoConfirmedCoinsEligibleToMix => WaitingForConfirmedFunds,
					CoinjoinError.UserInSendWorkflow => UserInSendWorkflowMessage,
					CoinjoinError.AllCoinsPrivate => AllPrivateMessage,
					CoinjoinError.UserWasntInRound => RoundFinishedMessage,
					CoinjoinError.BackendNotSynchronized => BackendNotConnected,
					CoinjoinError.CoinsRejected => CoinsRejectedMessage,
					CoinjoinError.OnlyImmatureCoinsAvailable => OnlyImmatureCoinsAvailableMessage,
					CoinjoinError.OnlyExcludedCoinsAvailable => OnlyExcludedCoinsAvailableMessage,
					CoinjoinError.UneconomicalRound => UneconomicalRoundMessage,
					CoinjoinError.RandomlySkippedRound => RandomlySkippedRoundMessage,
					_ => GeneralErrorMessage
				};

				StopCountDown();
				break;

			case CoinJoinStatusEventArgs coinJoinStatusEventArgs:
				OnCoinJoinPhaseChanged(coinJoinStatusEventArgs.CoinJoinProgressEventArgs);
				break;
		}
	}

	private void OnCoinJoinPhaseChanged(CoinJoinProgressEventArgs coinJoinProgress)
	{
		switch (coinJoinProgress)
		{
			case RoundEnded roundEnded:
				if (roundEnded.IsStopped)
				{
					PauseSpreading = true;
				}
				else
				{
					CurrentStatus = roundEnded.LastRoundState.EndRoundState switch
					{
						EndRoundState.TransactionBroadcasted => RoundSucceedMessage,
						EndRoundState.AbortedNotEnoughAlices => AbortedNotEnoughAlicesMessage,
						_ => RoundFinishedMessage
					};
					StopCountDown();
				}
				break;

			case EnteringOutputRegistrationPhase outputRegPhase:
				_countDownEndTime = outputRegPhase.TimeoutAt + outputRegPhase.RoundState.CoinjoinState.Parameters.TransactionSigningTimeout;
				break;

			case EnteringSigningPhase signingPhase:
				_countDownEndTime = signingPhase.TimeoutAt;
				break;

			case EnteringInputRegistrationPhase inputRegPhase:
				StartCountDown(
					message: InputRegistrationMessage,
					start: inputRegPhase.TimeoutAt - inputRegPhase.RoundState.InputRegistrationTimeout,
					end: inputRegPhase.TimeoutAt);
				break;

			case WaitingForBlameRound waitingForBlameRound:
				StartCountDown(message: WaitingForBlameRoundMessage, start: DateTimeOffset.UtcNow, end: waitingForBlameRound.TimeoutAt);
				break;

			case WaitingForRound:
				CurrentStatus = WaitingRoundMessage;
				StopCountDown();
				break;

			case EnteringConnectionConfirmationPhase confirmationPhase:

				var startTime = confirmationPhase.TimeoutAt - confirmationPhase.RoundState.CoinjoinState.Parameters.ConnectionConfirmationTimeout;
				var totalEndTime = confirmationPhase.TimeoutAt +
								   confirmationPhase.RoundState.CoinjoinState.Parameters.OutputRegistrationTimeout +
								   confirmationPhase.RoundState.CoinjoinState.Parameters.TransactionSigningTimeout;

				StartCountDown(
					message: CoinJoinInProgress,
					start: startTime,
					end: totalEndTime);

				break;

			case EnteringCriticalPhase:
				IsInCriticalPhase = true;
				break;

			case LeavingCriticalPhase:
				IsInCriticalPhase = false;
				break;
		}
	}

	private void StartCountDown(string message, DateTimeOffset start, DateTimeOffset end)
	{
		CurrentStatus = message;
		_countDownStartTime = start;
		_countDownEndTime = end;
		UpdateCountDown(); // force the UI to apply the changes at the same time.
		_countdownTimer.Start();
	}

	private void StopCountDown()
	{
		_countdownTimer.Stop();
		IsCountDownDelayHappening = false;
		_countDownStartTime = DateTimeOffset.MinValue;
		_countDownEndTime = DateTimeOffset.MinValue;
		LeftText = "";
		RightText = "";
		ProgressValue = 0;
	}
}
