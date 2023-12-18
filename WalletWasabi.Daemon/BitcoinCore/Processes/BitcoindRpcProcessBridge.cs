using NBitcoin;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Bases;
using WalletWasabi.BitcoinCore.Configuration;
using WalletWasabi.BitcoinCore.Processes;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Microservices;

namespace WalletWasabi.Daemon.BitcoinCore.Processes;

/// <summary>
/// Class for starting and stopping of Bitcoin daemon.
/// </summary>
public class BitcoindRpcProcessBridge
{
	public const string PidFileName = "bitcoin.pid";

	/// <summary>Experimentally found constant.</summary>
	private readonly TimeSpan _reasonableCoreShutdownTimeout = TimeSpan.FromSeconds(30);

	public BitcoindRpcProcessBridge(IRPCClient rpcClient, string dataDir, bool printToConsole)
	{
		RpcClient = rpcClient;
		Network = RpcClient.Network;
		DataDir = dataDir;
		PrintToConsole = printToConsole;
		PidFile = new PidFile(Path.Combine(DataDir, NetworkTranslator.GetDataDirPrefix(Network)), PidFileName);
		CachedPid = null;
		Process = null;
	}

	public Network Network { get; }
	public IRPCClient RpcClient { get; }
	public string DataDir { get; }
	public bool PrintToConsole { get; }
	public PidFile PidFile { get; }
	private ProcessAsync? Process { get; set; }
	private int? CachedPid { get; set; }

	/// <summary>
	/// This method can be called only once.
	/// </summary>
	public async Task StartAsync(CancellationToken cancel)
	{
		int ptcv = PrintToConsole ? 1 : 0;
		string processPath = MicroserviceHelpers.GetBinaryPath("bitcoind");
		string networkArgument = NetworkTranslator.GetCommandLineArguments(Network);

		string args = $"{networkArgument} -datadir=\"{DataDir}\" -printtoconsole={ptcv}";

		// Start bitcoind process.
		Process = new ProcessAsync(ProcessStartInfoFactory.Make(processPath, args));
		Process.Start();

		// Store PID in PID file.
		await PidFile.WriteFileAsync(Process.Id).ConfigureAwait(false);
		CachedPid = Process.Id;

		try
		{
			var exceptionTracker = new LastExceptionTracker();

			// Try to connect to bitcoin daemon RPC until we succeed.
			while (true)
			{
				try
				{
					TimeSpan timeSpan = await RpcClient.UptimeAsync(cancel).ConfigureAwait(false);

					Logger.LogInfo("RPC connection is successfully established.");
					Logger.LogDebug($"RPC uptime is: {timeSpan}.");

					// Bitcoin daemon is started. We are done.
					break;
				}
				catch (Exception ex)
				{
					ExceptionInfo exceptionInfo = exceptionTracker.Process(ex);

					// Don't log extensively.
					if (exceptionInfo.IsFirst)
					{
						Logger.LogInfo($"{Constants.BuiltinBitcoinNodeName} is not yet ready... Reason: {exceptionInfo.Exception.Message}");
					}

					if (Process is { } p && p.HasExited)
					{
						throw new BitcoindException($"Failed to start daemon, location: '{p.StartInfo.FileName} {p.StartInfo.Arguments}'", ex);
					}
				}

				if (cancel.IsCancellationRequested)
				{
					Logger.LogDebug("Bitcoin daemon was not started yet and user requested to cancel the operation.");
					await StopAsync(onlyOwned: true).ConfigureAwait(false);
					cancel.ThrowIfCancellationRequested();
				}

				// Wait a moment before the next check.
				await Task.Delay(100, cancel).ConfigureAwait(false);
			}
		}
		catch (Exception)
		{
			Process?.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Stops bitcoin daemon process when PID file exists.
	/// </summary>
	/// <remarks>If there is not PID file, no process is stopped.</remarks>
	/// <param name="onlyOwned">Only stop if this node owns the process.</param>
	public async Task StopAsync(bool onlyOwned)
	{
		Logger.LogDebug($"> {nameof(onlyOwned)}={onlyOwned}");

		if (Process is null)
		{
			Logger.LogDebug("< Process is null.");
			return;
		}

		// "process" variable is guaranteed to be non-null at this point.
		ProcessAsync process = Process;

		using var cts = new CancellationTokenSource(_reasonableCoreShutdownTimeout);
		int? pid = await PidFile.TryReadAsync().ConfigureAwait(false);

		// If the cached PID is PID, then we own the process.
		if (pid.HasValue && (!onlyOwned || CachedPid == pid))
		{
			Logger.LogDebug($"User is responsible for the daemon process with PID {pid}. Stop it.");

			try
			{
				bool isKilled = false;

				try
				{
					// Stop Bitcoin daemon using RPC "stop" command.
					// The command actually only initiates the bitcoind graceful shutdown procedure.
					// Our time budget for the bitcoind to stop is given by "ReasonableCoreShutdownTimeout".
					await RpcClient.StopAsync().ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					Logger.LogWarning(ex);
					process.Kill();
					isKilled = true;
				}

				if (!isKilled)
				{
					Logger.LogDebug($"Wait until the process is stopped.");
					await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
				}
			}
			finally
			{
				Logger.LogDebug($"Wait until the process is stopped.");
				process.Dispose();
				Process = null;
				PidFile.TryDelete();
			}
		}
		else
		{
			Logger.LogDebug("User is NOT responsible for the daemon process.");
		}

		Logger.LogDebug("<");
	}
}
