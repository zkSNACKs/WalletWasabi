using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Wabisabi.Models;
using WalletWasabi.Wallets;

namespace WalletWasabi.WabiSabi.Client;

public class CoinJoinTracker : IDisposable
{
	private bool _disposedValue;

	public event EventHandler<RoundStateAndRemainingTimeChangedEventArgs>? RoundStateChanged; 
	public CoinJoinTracker(
		Wallet wallet,
		CoinJoinClient coinJoinClient,
		IEnumerable<SmartCoin> coinCandidates,
		bool restartAutomatically,
		CancellationToken cancellationToken)
	{
		Wallet = wallet;
		CoinJoinClient = coinJoinClient;
		CoinCandidates = coinCandidates;
		RestartAutomatically = restartAutomatically;
		CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		CoinJoinClient.RoundStateChanged += OnRoundStateChanged;
		CoinJoinTask = coinJoinClient.StartCoinJoinAsync(coinCandidates, CancellationTokenSource.Token);
	}

	private CoinJoinClient CoinJoinClient { get; }
	private CancellationTokenSource CancellationTokenSource { get; }

	public Wallet Wallet { get; }
	public Task<CoinJoinResult> CoinJoinTask { get; }
	public IEnumerable<SmartCoin> CoinCandidates { get; }
	public bool RestartAutomatically { get; }

	public bool IsCompleted => CoinJoinTask.IsCompleted;
	public bool InCriticalCoinJoinState => CoinJoinClient.InCriticalCoinJoinState;
	public bool IsStopped { get; private set; }

	private void OnRoundStateChanged(object? sender, RoundStateAndRemainingTimeChangedEventArgs e)
	{
		RoundStateChanged?.Invoke(this, e);
	}
	
	public void Stop()
	{
		IsStopped = true;
		CancellationTokenSource.Cancel();
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_disposedValue)
		{
			if (disposing)
			{
				CoinJoinClient.RoundStateChanged -= OnRoundStateChanged;
				CancellationTokenSource.Dispose();
			}

			_disposedValue = true;
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
