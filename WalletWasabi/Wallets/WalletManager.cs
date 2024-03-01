using NBitcoin;
using Nito.AsyncEx;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Blockchain.BlockFilters;
using WalletWasabi.Blockchain.Blocks;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Blockchain.TransactionProcessing;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Extensions;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Services;
using WalletWasabi.Services.Terminate;
using WalletWasabi.Stores;
using WalletWasabi.WabiSabi.Client;

namespace WalletWasabi.Wallets;

public class WalletManager : IWalletProvider
{
	/// <remarks>All access must be guarded by <see cref="Lock"/> object.</remarks>
	private volatile bool _disposedValue = false;

	public WalletManager(
		Network network,
		string workDir,
		WalletDirectories walletDirectories,
		BitcoinStore bitcoinStore,
		WasabiSynchronizer synchronizer,
		HybridFeeProvider feeProvider,
		IBlockProvider blockProvider,
		ServiceConfiguration serviceConfiguration)
	{
		using IDisposable _ = BenchmarkLogger.Measure();

		Network = network;
		WorkDir = Guard.NotNullOrEmptyOrWhitespace(nameof(workDir), workDir, true);
		Directory.CreateDirectory(WorkDir);
		WalletDirectories = walletDirectories;
		BitcoinStore = bitcoinStore;
		Synchronizer = synchronizer;
		FeeProvider = feeProvider;
		BlockProvider = blockProvider;
		ServiceConfiguration = serviceConfiguration;
		CancelAllTasksToken = CancelAllTasks.Token;

		LoadWalletListFromFileSystem();
	}

	/// <summary>
	/// Triggered if any of the Wallets processes a transaction. The sender of the event will be the Wallet.
	/// </summary>
	public event EventHandler<ProcessedResult>? WalletRelevantTransactionProcessed;

	/// <summary>
	/// Triggered if any of the Wallets changes its state. The sender of the event will be the Wallet.
	/// </summary>
	public event EventHandler<WalletState>? WalletStateChanged;

	/// <summary>
	/// Triggered if a wallet added to the Wallet collection. The sender of the event will be the WalletManager and the argument is the added Wallet.
	/// </summary>
	public event EventHandler<Wallet>? WalletAdded;

	/// <summary>Cancels initialization of wallets.</summary>
	private CancellationTokenSource CancelAllTasks { get; } = new();

	/// <summary>Token from <see cref="CancelAllTasks"/>.</summary>
	/// <remarks>Accessing the token of <see cref="CancelAllTasks"/> can lead to <see cref="ObjectDisposedException"/>. So we copy the token and no exception can be thrown.</remarks>
	private CancellationToken CancelAllTasksToken { get; }

	/// <remarks>All access must be guarded by <see cref="Lock"/> object.</remarks>
	private HashSet<Wallet> Wallets { get; } = new();

	private object Lock { get; } = new();
	private AsyncLock StartStopWalletLock { get; } = new();

	private BitcoinStore BitcoinStore { get; }
	private WasabiSynchronizer Synchronizer { get; }
	private ServiceConfiguration ServiceConfiguration { get; }
	private bool IsInitialized { get; set; }

	private HybridFeeProvider FeeProvider { get; }
	public Network Network { get; }
	public WalletDirectories WalletDirectories { get; }
	private IBlockProvider BlockProvider { get; }
	private string WorkDir { get; }

	private void LoadWalletListFromFileSystem()
	{
		var walletFileNames = WalletDirectories.EnumerateWalletFiles().Select(fi => Path.GetFileNameWithoutExtension(fi.FullName));

		string[]? walletNamesToLoad = null;
		lock (Lock)
		{
			walletNamesToLoad = walletFileNames.Where(walletFileName => !Wallets.Any(wallet => wallet.WalletName == walletFileName)).ToArray();
		}

		if (walletNamesToLoad.Length == 0)
		{
			return;
		}

		List<Task<Wallet>> walletLoadTasks = walletNamesToLoad.Select(walletName => Task.Run(() => LoadWalletByNameFromDisk(walletName), CancelAllTasksToken)).ToList();

		while (walletLoadTasks.Count > 0)
		{
			var tasksArray = walletLoadTasks.ToArray();
			var finishedTaskIndex = Task.WaitAny(tasksArray, CancelAllTasksToken);
			var finishedTask = tasksArray[finishedTaskIndex];
			walletLoadTasks.Remove(finishedTask);
			try
			{
				var wallet = finishedTask.Result;
				AddWallet(wallet);
			}
			catch (Exception ex)
			{
				Logger.LogWarning(ex);
			}
		}
	}

	public void RenameWallet(Wallet wallet, string newWalletName)
	{
		if (newWalletName == wallet.WalletName)
		{
			return;
		}

		if (ValidateWalletName(newWalletName) is { } error)
		{
			Logger.LogWarning($"Invalid name '{newWalletName}' when attempting to rename '{error.Message}'");
			throw new InvalidOperationException($"Invalid name {newWalletName} - {error.Message}");
		}

		var (currentWalletFilePath, currentWalletBackupFilePath) = WalletDirectories.GetWalletFilePaths(wallet.WalletName);
		var (newWalletFilePath, newWalletBackupFilePath) = WalletDirectories.GetWalletFilePaths(newWalletName);

		Logger.LogInfo($"Renaming file {currentWalletFilePath} to {newWalletFilePath}");
		File.Move(currentWalletFilePath, newWalletFilePath);

		try
		{
			File.Move(currentWalletBackupFilePath, newWalletBackupFilePath);
		}
		catch (Exception e)
		{
			Logger.LogWarning($"Could not rename wallet backup file. Reason: {e.Message}");
		}

		wallet.KeyManager.SetFilePath(newWalletFilePath);
	}

	public (ErrorSeverity Severity, string Message)? ValidateWalletName(string walletName)
	{
		string walletFilePath = Path.Combine(WalletDirectories.WalletsDir, $"{walletName}.json");

		if (string.IsNullOrEmpty(walletName))
		{
			return (ErrorSeverity.Error, "The name cannot be empty");
		}

		if (walletName.IsTrimmable())
		{
			return (ErrorSeverity.Error, "Leading and trailing white spaces are not allowed!");
		}

		if (File.Exists(walletFilePath))
		{
			return (ErrorSeverity.Error, $"A wallet named {walletName} already exists. Please try a different name.");
		}

		if (!WalletGenerator.ValidateWalletName(walletName))
		{
			return (ErrorSeverity.Error, "Selected wallet name is not valid. Please try a different name.");
		}

		return null;
	}

	public Task<IEnumerable<IWallet>> GetWalletsAsync() => Task.FromResult<IEnumerable<IWallet>>(GetWallets());

	public IEnumerable<Wallet> GetWallets()
	{
		lock (Lock)
		{
			return Wallets.ToList();
		}
	}

	public bool HasWallet()
	{
		lock (Lock)
		{
			return Wallets.Count > 0;
		}
	}

	public async Task<Wallet> StartWalletAsync(Wallet wallet)
	{
		lock (Lock)
		{
			if (_disposedValue)
			{
				Logger.LogError("Object was already disposed.");
				throw new OperationCanceledException("Object was already disposed.");
			}

			if (CancelAllTasks.IsCancellationRequested)
			{
				throw new OperationCanceledException($"Stopped loading {wallet}, because cancel was requested.");
			}

			// Throw an exception if the wallet was not added to the WalletManager.
			Wallets.Single(x => x == wallet);
		}

		wallet.SetWaitingForInitState();

		// Wait for the WalletManager to be initialized.
		while (!IsInitialized)
		{
			await Task.Delay(100, CancelAllTasks.Token).ConfigureAwait(false);
		}

		if (wallet.State == WalletState.WaitingForInit)
		{
			wallet.Initialize();
		}

		using (await StartStopWalletLock.LockAsync(CancelAllTasks.Token).ConfigureAwait(false))
		{
			try
			{
				Logger.LogInfo($"Starting wallet '{wallet.WalletName}'...");
				await wallet.StartAndSetUpUnhandledExceptionCallbackAsync(TerminateService.Instance, CancelAllTasksToken).ConfigureAwait(false);
				Logger.LogInfo($"Wallet '{wallet.WalletName}' started.");
				CancelAllTasksToken.ThrowIfCancellationRequested();
				return wallet;
			}
			catch
			{
				await wallet.StopAsync(CancellationToken.None).ConfigureAwait(false);
				throw;
			}
		}
	}

	public Task<Wallet> AddAndStartWalletAsync(KeyManager keyManager)
	{
		var wallet = AddWallet(keyManager);
		return StartWalletAsync(wallet);
	}

	public Wallet AddWallet(KeyManager keyManager)
	{
		Wallet wallet = CreateWalletInstance(keyManager);
		AddWallet(wallet);
		return wallet;
	}

	private Wallet LoadWalletByNameFromDisk(string walletName)
	{
		(string walletFullPath, string walletBackupFullPath) = WalletDirectories.GetWalletFilePaths(walletName);
		Wallet wallet;
		try
		{
			wallet = CreateWalletInstance(KeyManager.FromFile(walletFullPath));
		}
		catch (Exception ex)
		{
			if (!File.Exists(walletBackupFullPath))
			{
				throw;
			}

			Logger.LogWarning($"Wallet got corrupted.\n" +
				$"Wallet file path: {walletFullPath}\n" +
				$"Trying to recover it from backup.\n" +
				$"Backup path: {walletBackupFullPath}\n" +
				$"Exception: {ex}");
			if (File.Exists(walletFullPath))
			{
				string corruptedWalletBackupPath = $"{walletBackupFullPath}_CorruptedBackup";
				if (File.Exists(corruptedWalletBackupPath))
				{
					File.Delete(corruptedWalletBackupPath);
					Logger.LogInfo($"Deleted previous corrupted wallet file backup from `{corruptedWalletBackupPath}`.");
				}
				File.Move(walletFullPath, corruptedWalletBackupPath);
				Logger.LogInfo($"Backed up corrupted wallet file to `{corruptedWalletBackupPath}`.");
			}
			File.Copy(walletBackupFullPath, walletFullPath);

			wallet = CreateWalletInstance(KeyManager.FromFile(walletFullPath));
		}

		return wallet;
	}

	private void AddWallet(Wallet wallet)
	{
		lock (Lock)
		{
			if (Wallets.Any(w => w.WalletId == wallet.WalletId))
			{
				throw new InvalidOperationException($"Wallet with the same name was already added: {wallet.WalletName}.");
			}
			Wallets.Add(wallet);
		}

		if (!File.Exists(WalletDirectories.GetWalletFilePaths(wallet.WalletName).walletFilePath))
		{
			wallet.KeyManager.ToFile();
		}

		wallet.WalletRelevantTransactionProcessed += TransactionProcessor_WalletRelevantTransactionProcessed;
		wallet.StateChanged += Wallet_StateChanged;

		WalletAdded?.Invoke(this, wallet);
	}

	private Wallet CreateWalletInstance(KeyManager keyManager)
		=> new(WorkDir, Network, keyManager, BitcoinStore, Synchronizer, ServiceConfiguration, FeeProvider, BlockProvider);

	public bool WalletExists(HDFingerprint? fingerprint) => GetWallets().Any(x => fingerprint is { } && x.KeyManager.MasterFingerprint == fingerprint);

	private void TransactionProcessor_WalletRelevantTransactionProcessed(object? sender, ProcessedResult e)
	{
		WalletRelevantTransactionProcessed?.Invoke(sender, e);
	}

	private void Wallet_StateChanged(object? sender, WalletState e)
	{
		WalletStateChanged?.Invoke(sender, e);
	}

	public async Task RemoveAndStopAllAsync(CancellationToken cancel)
	{
		lock (Lock)
		{
			// Already disposed.
			if (_disposedValue)
			{
				return;
			}

			_disposedValue = true;
		}

		CancelAllTasks.Cancel();

		using (await StartStopWalletLock.LockAsync(cancel).ConfigureAwait(false))
		{
			foreach (var wallet in GetWallets())
			{
				cancel.ThrowIfCancellationRequested();

				wallet.WalletRelevantTransactionProcessed -= TransactionProcessor_WalletRelevantTransactionProcessed;
				wallet.StateChanged -= Wallet_StateChanged;

				lock (Lock)
				{
					if (!Wallets.Remove(wallet))
					{
						throw new InvalidOperationException("Wallet service doesn't exist.");
					}
				}

				try
				{
					if (wallet.State >= WalletState.Initialized)
					{
						var keyManager = wallet.KeyManager;
						string backupWalletFilePath = WalletDirectories.GetWalletFilePaths(Path.GetFileName(keyManager.FilePath)!).walletBackupFilePath;
						keyManager.ToFile(backupWalletFilePath);
						Logger.LogInfo($"{nameof(wallet.KeyManager)} backup saved to `{backupWalletFilePath}`.");
						await wallet.StopAsync(cancel).ConfigureAwait(false);
						Logger.LogInfo($"'{wallet.WalletName}' wallet is stopped.");
					}

					wallet.Dispose();
				}
				catch (Exception ex)
				{
					Logger.LogError(ex);
				}
			}
		}

		CancelAllTasks.Dispose();
	}

	public void ProcessCoinJoin(SmartTransaction tx)
	{
		lock (Lock)
		{
			foreach (var wallet in Wallets.Where(x => x.State == WalletState.Started && !x.TransactionProcessor.IsAware(tx.GetHash())))
			{
				wallet.TransactionProcessor.Process(tx);
			}
		}
	}

	public void Process(SmartTransaction transaction)
	{
		lock (Lock)
		{
			foreach (var wallet in Wallets.Where(x => x.State == WalletState.Started))
			{
				wallet.TransactionProcessor.Process(transaction);
			}
		}
	}

	public IEnumerable<SmartCoin> CoinsByOutPoint(OutPoint input)
	{
		lock (Lock)
		{
			var res = new List<SmartCoin>();
			foreach (var wallet in Wallets.Where(x => x.State == WalletState.Started))
			{
				if (wallet.Coins.TryGetByOutPoint(input, out var coin))
				{
					res.Add(coin);
				}
			}

			return res;
		}
	}

	public ISet<uint256> FilterUnknownCoinjoins(IEnumerable<uint256> cjs)
	{
		lock (Lock)
		{
			var unknowns = new HashSet<uint256>();
			foreach (var wallet in Wallets.Where(x => x.State == WalletState.Started))
			{
				// If a wallet service doesn't know about the tx, then we add it for processing.
				foreach (var tx in cjs.Where(x => !wallet.TransactionProcessor.IsAware(x)))
				{
					unknowns.Add(tx);
				}
			}
			return unknowns;
		}
	}

	public void Initialize()
	{
		foreach (var wallet in GetWallets().Where(w => w.State == WalletState.WaitingForInit))
		{
			wallet.Initialize();
		}

		IsInitialized = true;
	}

	public void EnsureTurboSyncHeightConsistency()
	{
		foreach (var km in GetWallets().Select(x => x.KeyManager).Where(x => x.GetNetwork() == Network))
		{
			km.EnsureTurboSyncHeightConsistency();
		}
	}

	public void SetMaxBestHeight(uint bestHeight)
	{
		foreach (var km in GetWallets().Select(x => x.KeyManager).Where(x => x.GetNetwork() == Network))
		{
			km.SetMaxBestHeight(new Height(bestHeight));
		}
	}

	public Wallet GetWalletByName(string walletName)
	{
		lock (Lock)
		{
			return Wallets.Single(x => x.KeyManager.WalletName == walletName);
		}
	}
}
