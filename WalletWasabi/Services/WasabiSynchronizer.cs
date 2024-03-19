using NBitcoin.RPC;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Backend.Models.Responses;
using WalletWasabi.Bases;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Blockchain.Blocks;
using WalletWasabi.Models;
using WalletWasabi.Services.Events;
using WalletWasabi.Tor.Socks5.Exceptions;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.WebClients.Wasabi;

namespace WalletWasabi.Services;

public class WasabiSynchronizer : PeriodicRunner, INotifyPropertyChanged, IThirdPartyFeeProvider, IWasabiBackendStatusProvider
{
	private decimal _usdExchangeRate;

	private TorStatus _torStatus;

	private BackendStatus _backendStatus;

	public WasabiSynchronizer(TimeSpan period, SmartHeaderChain smartHeaderChain, WasabiClient wasabiClient, EventBus eventBus) : base(period)
	{
		LastResponse = null;
		SmartHeaderChain = smartHeaderChain;
		WasabiClient = wasabiClient;

		EventBus = eventBus;
		ExchangeRateChangedSubscription = EventBus.Subscribe((ExchangeRateChanged e) => UsdExchangeRate = e.UsdBtcRate);
		FeeEstimationChangedSubscription = EventBus.Subscribe(
			(MiningFeeRatesChanged e) =>
			{
				LastAllFeeEstimate = e.AllFeeEstimate;
				AllFeeEstimateArrived?.Invoke(this, e.AllFeeEstimate);
			});
		BackendConnectionChangedSubscription = EventBus.Subscribe(
			(ConnectionStateChanged e) =>
			{
				BackendStatus = e.Connected ? BackendStatus.Connected : BackendStatus.NotConnected;
				SynchronizeRequestFinished?.Invoke(this, e.Connected);
			});
	}

	#region EventsPropertiesMembers

	public event EventHandler<bool>? SynchronizeRequestFinished;

	public event EventHandler<SynchronizeResponse>? ResponseArrived;

	public event EventHandler<AllFeeEstimate>? AllFeeEstimateArrived;

	public event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>Task completion source that is completed once a first synchronization request succeeds or fails.</summary>
	public TaskCompletionSource<bool> InitialRequestTcs { get; } = new();

	public SynchronizeResponse? LastResponse { get; private set; }
	public WasabiClient WasabiClient { get; }
	private EventBus EventBus { get; }
	private IDisposable ExchangeRateChangedSubscription { get; }
	private IDisposable FeeEstimationChangedSubscription { get; }
	private IDisposable BackendConnectionChangedSubscription { get; }

	/// <summary>Gets the Bitcoin price in USD.</summary>
	public decimal UsdExchangeRate
	{
		get => _usdExchangeRate;
		private set => RaiseAndSetIfChanged(ref _usdExchangeRate, value);
	}

	public TorStatus TorStatus
	{
		get => _torStatus;
		private set => RaiseAndSetIfChanged(ref _torStatus, value);
	}

	public BackendStatus BackendStatus
	{
		get => _backendStatus;
		private set
		{
			if (RaiseAndSetIfChanged(ref _backendStatus, value))
			{
				BackendStatusChangedAt = DateTimeOffset.UtcNow;
			}
		}
	}

	private DateTimeOffset BackendStatusChangedAt { get; set; } = DateTimeOffset.UtcNow;
	public TimeSpan BackendStatusChangedSince => DateTimeOffset.UtcNow - BackendStatusChangedAt;
	private SmartHeaderChain SmartHeaderChain { get; }
	public AllFeeEstimate? LastAllFeeEstimate { get; private set; }

	#endregion EventsPropertiesMembers

	protected override async Task ActionAsync(CancellationToken cancel)
	{
		try
		{
			SynchronizeResponse response;

			try
			{
				if (SmartHeaderChain.TipHash is null)
				{
					return;
				}

				response = await WasabiClient
					.GetSynchronizeAsync(SmartHeaderChain.TipHash, 1, EstimateSmartFeeMode.Conservative, cancel)
					.ConfigureAwait(false);

				// NOT GenSocksServErr
				TorStatus = TorStatus.Running;
				OnSynchronizeRequestFinished();
			}
			catch (HttpRequestException ex) when (ex.InnerException is TorException innerEx)
			{
				TorStatus = innerEx is TorConnectionException ? TorStatus.NotRunning : TorStatus.Running;
				OnSynchronizeRequestFinished();
				throw;
			}
			catch (Exception)
			{
				TorStatus = TorStatus.Running;
				OnSynchronizeRequestFinished();
				throw;
			}

			LastResponse = response;
			ResponseArrived?.Invoke(this, response);
		}
		catch (HttpRequestException)
		{
			await Task.Delay(3000, cancel).ConfigureAwait(false); // Retry sooner in case of connection error.
			TriggerRound();
			throw;
		}
	}

	private void OnSynchronizeRequestFinished()
	{
		var isBackendConnected = BackendStatus is BackendStatus.Connected;

		// One time trigger for the UI about the first request.
		InitialRequestTcs.TrySetResult(isBackendConnected);
	}

	protected bool RaiseAndSetIfChanged<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
		{
			return false;
		}

		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		return true;
	}

	public override void Dispose()
	{
		ExchangeRateChangedSubscription.Dispose();
		FeeEstimationChangedSubscription.Dispose();
		BackendConnectionChangedSubscription.Dispose();
		base.Dispose();
	}
}
