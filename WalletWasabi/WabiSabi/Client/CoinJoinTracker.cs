using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.WabiSabi.Client.CoinJoinProgressEvents;
using WalletWasabi.Wallets;

namespace WalletWasabi.WabiSabi.Client;

public class CoinJoinTracker : IDisposable
{
	private bool _disposedValue;

	public CoinJoinTracker(
		IWallet wallet,
		CoinJoinClient coinJoinClient,
		bool stopWhenAllMixed,
		bool overridePlebStop,
		CancellationToken cancellationToken)
	{
		Wallet = wallet;
		CoinJoinClient = coinJoinClient;
		CoinJoinClient.CoinJoinClientProgress += CoinJoinClient_CoinJoinClientProgress;

		StopWhenAllMixed = stopWhenAllMixed;
		OverridePlebStop = overridePlebStop;
		CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		CoinJoinTask = coinJoinClient.StartCoinJoinAsync(wallet.GetCoinjoinCoinCandidates(), CancellationTokenSource.Token);
	}

	public event EventHandler<CoinJoinProgressEventArgs>? WalletCoinJoinProgressChanged;

	private CoinJoinClient CoinJoinClient { get; }
	private CancellationTokenSource CancellationTokenSource { get; }

	public IWallet Wallet { get; }
	public Task<CoinJoinResult> CoinJoinTask { get; }
	public bool StopWhenAllMixed { get; set; }
	public bool OverridePlebStop { get; }

	public bool IsCompleted => CoinJoinTask.IsCompleted;
	public bool InCriticalCoinJoinState { get; private set; }
	public bool IsStopped { get; private set; }

	public void Stop()
	{
		IsStopped = true;
		if (!InCriticalCoinJoinState)
		{
			CancellationTokenSource.Cancel();
		}
	}

	private void CoinJoinClient_CoinJoinClientProgress(object? sender, CoinJoinProgressEventArgs coinJoinProgressEventArgs)
	{
		switch (coinJoinProgressEventArgs)
		{
			case EnteringCriticalPhase:
				InCriticalCoinJoinState = true;
				break;

			case LeavingCriticalPhase:
				InCriticalCoinJoinState = false;
				break;

			case RoundEnded roundEnded:
				roundEnded.IsStopped = IsStopped;
				break;
		}

		WalletCoinJoinProgressChanged?.Invoke(Wallet, coinJoinProgressEventArgs);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_disposedValue)
		{
			if (disposing)
			{
				CoinJoinClient.CoinJoinClientProgress -= CoinJoinClient_CoinJoinClientProgress;
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
