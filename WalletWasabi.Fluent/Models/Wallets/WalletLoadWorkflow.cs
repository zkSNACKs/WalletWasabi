using ReactiveUI;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.BlockFilters;
using WalletWasabi.Blockchain.Blocks;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Logging;
using WalletWasabi.Services;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.Models.Wallets;

[AutoInterface]
public partial class WalletLoadWorkflow
{
	private readonly CompositeDisposable _disposables = new();
	private readonly Wallet _wallet;
	private Stopwatch? _stopwatch;
	private uint _filtersToDownloadCount;
	private uint _filtersToProcessCount;
	private uint _filterProcessStartingHeight;
	private uint _filterProcessCurrentTipHeight;
	private Subject<(double PercentComplete, TimeSpan TimeRemaining)> _progress;
	[AutoNotify] private bool _isLoading;

	public WalletLoadWorkflow(Wallet wallet)
	{
		_wallet = wallet;
		_progress = new();
		_progress.OnNext((0, TimeSpan.Zero));

		LoadCompleted =
			Observable.FromEventPattern<WalletState>(_wallet, nameof(Wallet.StateChanged))
					.ObserveOn(RxApp.MainThreadScheduler)
					.Select(x => x.EventArgs)
					.Where(x => x == WalletState.Started)
					.ToSignal();
	}

	public IObservable<(double PercentComplete, TimeSpan TimeRemaining)> Progress => _progress;

	public IObservable<Unit> LoadCompleted { get; }

	private uint TotalCount => _filtersToProcessCount + _filtersToDownloadCount;

	private uint RemainingFiltersToDownload => (uint)Services.SmartHeaderChain.HashesLeft;

	public void Start()
	{
		_stopwatch = Stopwatch.StartNew();
		_disposables.Add(Disposable.Create(_stopwatch.Stop));

		Observable.FromAsync(() => Services.HostedServices.Get<WasabiSynchronizer>().InitialRequestTcs.Task)
			.ObserveOn(RxApp.MainThreadScheduler)
			.SubscribeAsync(LoadWalletAsync)
			.DisposeWith(_disposables);

		Observable.Interval(TimeSpan.FromSeconds(1))
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(
					_ =>
					{
						UpdateCurrentTipHeight();
						var processedCount = GetCurrentProcessedCount();
						UpdateProgress(processedCount);
					})
				.DisposeWith(_disposables);
	}

	public void Stop()
	{
		_disposables.Dispose();
	}

	private async Task LoadWalletAsync(bool isBackendAvailable)
	{
		if (IsLoading)
		{
			return;
		}

		IsLoading = true;

		await SetInitValuesAsync(isBackendAvailable).ConfigureAwait(false);

		if (_wallet.State != WalletState.Uninitialized)
		{
			throw new Exception("Wallet is already being logged in.");
		}

		try
		{
			await Task.Run(async () => await Services.WalletManager.StartWalletAsync(_wallet));
		}
		catch (OperationCanceledException ex)
		{
			Logger.LogTrace(ex);
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);
		}
	}

	private async Task SetInitValuesAsync(bool isBackendAvailable)
	{
		if (isBackendAvailable)
		{
			// Wait until "server tip height" is initialized. It can be initialized only if Backend is available.
			await Services.SmartHeaderChain.ServerTipInitializedTcs.Task.ConfigureAwait(true);
		}

		// Wait until "client tip height" is initialized.
		await Services.BitcoinStore.IndexStore.InitializedTcs.Task.ConfigureAwait(true);

		_filtersToDownloadCount = (uint)Services.SmartHeaderChain.HashesLeft;

		uint serverTipHeight = Services.SmartHeaderChain.ServerTipHeight;
		uint clientTipHeight = Services.SmartHeaderChain.TipHeight;

		var tipHeight = Math.Max(serverTipHeight, clientTipHeight);
		var startingHeight = SmartHeader.GetStartingHeader(_wallet.Network).Height;
		var bestHeight = (uint)_wallet.KeyManager.GetBestHeight(SyncType.Complete).Value;
		_filterProcessStartingHeight = bestHeight < startingHeight ? startingHeight : bestHeight;

		_filtersToProcessCount = tipHeight - _filterProcessStartingHeight;
		_filterProcessCurrentTipHeight = tipHeight;
	}

	private uint GetCurrentProcessedCount()
	{
		uint downloadedFilters = 0;
		if (_filtersToDownloadCount > 0)
		{
			downloadedFilters = _filtersToDownloadCount - RemainingFiltersToDownload;
		}

		uint processedFilters = 0;
		if (_wallet.LastProcessedFilter?.Header?.Height is { } lastProcessedFilterHeight)
		{
			processedFilters = lastProcessedFilterHeight - _filterProcessStartingHeight - 1;
		}

		var processedCount = downloadedFilters + processedFilters;

		return processedCount;
	}

	private void UpdateCurrentTipHeight()
	{
		var smartHeaderChainTipHeight = Services.SmartHeaderChain.TipHeight;
		if (_filtersToProcessCount == 0 || smartHeaderChainTipHeight == _filterProcessCurrentTipHeight)
		{
			return;
		}

		_filtersToProcessCount += smartHeaderChainTipHeight - _filterProcessCurrentTipHeight;
		_filterProcessCurrentTipHeight = smartHeaderChainTipHeight;
	}

	private void UpdateProgress(uint processedCount)
	{
		if (TotalCount == 0 || processedCount == 0 || processedCount > TotalCount || _stopwatch is null)
		{
			return;
		}

		var percent = (decimal)processedCount / TotalCount * 100;
		var remainingCount = TotalCount - processedCount;
		var tempPercent = (uint)Math.Round(percent);

		if (tempPercent == 0)
		{
			return;
		}

		var remainingMilliseconds = (double)_stopwatch.ElapsedMilliseconds / processedCount * remainingCount;
		var remainingTimeSpan = TimeSpan.FromMilliseconds(remainingMilliseconds);

		if (remainingTimeSpan > TimeSpan.FromHours(1))
		{
			remainingTimeSpan = new TimeSpan(remainingTimeSpan.Days, remainingTimeSpan.Hours, remainingTimeSpan.Minutes, seconds: 0);
		}

		_progress.OnNext((tempPercent, remainingTimeSpan));
	}
}
