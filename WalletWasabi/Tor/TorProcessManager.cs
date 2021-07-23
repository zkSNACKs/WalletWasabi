using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Logging;
using WalletWasabi.Microservices;
using WalletWasabi.Tor.Control;
using WalletWasabi.Tor.Control.Exceptions;
using WalletWasabi.Tor.Control.Messages;
using WalletWasabi.Tor.Socks5;

namespace WalletWasabi.Tor
{
	/// <summary>Manages lifetime of Tor process.</summary>
	/// <seealso href="https://2019.www.torproject.org/docs/tor-manual.html.en"/>
	public class TorProcessManager : IAsyncDisposable
	{
		/// <summary>Task completion source returning a cancellation token which is canceled when Tor process is terminated.</summary>
		private volatile TaskCompletionSource<CancellationToken> _tcs = new();

		public TorProcessManager(TorSettings settings) :
			this(settings, new(settings.SocksEndpoint))
		{
		}

		/// <summary>For tests.</summary>
		internal TorProcessManager(TorSettings settings, TorTcpConnectionFactory tcpConnectionFactory)
		{
			TorProcess = null;
			TorControlClient = null;
			LoopCts = new();
			LoopTask = null;
			Settings = settings;
			TcpConnectionFactory = tcpConnectionFactory;
		}

		/// <summary>Guards <see cref="TorProcess"/> and <see cref="TorControlClient"/>.</summary>
		private object StateLock { get; } = new();

		private Task? LoopTask { get; set; }

		/// <summary>To stop the loop that keeps starting Tor process.</summary>
		private CancellationTokenSource LoopCts { get; }
		private TorSettings Settings { get; }
		private TorTcpConnectionFactory TcpConnectionFactory { get; }

		/// <remarks>Guarded by <see cref="StateLock"/>.</remarks>
		private ProcessAsync? TorProcess { get; set; }

		/// <remarks>Guarded by <see cref="StateLock"/>.</remarks>
		private TorControlClient? TorControlClient { get; set; }

		/// <summary>Starts loop which makes sure that Tor process is started.</summary>
		/// <param name="cancellationToken">Application lifetime cancellation token.</param>
		/// <returns>Cancellation token which is canceled once Tor process terminates (either forcefully or gracefully).</returns>
		/// <remarks>This method must be called exactly once.</remarks>		
		/// <exception cref="OperationCanceledException"/>
		public async Task<CancellationToken> StartAsync(CancellationToken cancellationToken = default)
		{
			LoopTask = RestartingLoopAsync(cancellationToken);

			return await WaitForNextAttemptAsync(cancellationToken).ConfigureAwait(false);
		}

		/// <summary>Waits until Tor process is fully started or until it is stopped for some reason.</summary>
		/// <returns>Cancellation token which is canceled once Tor process terminates.</returns>
		/// <remarks>This is useful to set up Tor control monitors that need to be restarted once Tor process is started again.</remarks>
		public Task<CancellationToken> WaitForNextAttemptAsync(CancellationToken cancellationToken = default)
		{
			return _tcs.Task.WithAwaitCancellationAsync(cancellationToken);
		}

		/// <summary>Keeps starting Tor OS process.</summary>
		/// <param name="globalCancellationToken">Application lifetime cancellation token.</param>
		private async Task RestartingLoopAsync(CancellationToken globalCancellationToken)
		{
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(globalCancellationToken, LoopCts.Token);
			CancellationToken cancellationToken = linkedCts.Token;

			while (!cancellationToken.IsCancellationRequested)
			{
				ProcessAsync? process = null;
				TorControlClient? controlClient = null;
				bool setNewTcs = true;

				// Use CancellationTokenSource to signal that Tor process terminated.
				using CancellationTokenSource cts = new();

				try
				{
					// Is Tor already running? Either our Tor process from previous Wasabi Wallet run or possibly user's own Tor.
					bool isAlreadyRunning = await TcpConnectionFactory.IsTorRunningAsync().ConfigureAwait(false);

					if (isAlreadyRunning)
					{
						Logger.LogInfo($"Tor is already running on {Settings.SocksEndpoint.Address}:{Settings.SocksEndpoint.Port}.");
						controlClient = await InitTorControlAsync(cancellationToken).ConfigureAwait(false);

						// Tor process can crash even between these two commands too.
						int processId = await controlClient.GetTorProcessIdAsync(cancellationToken).ConfigureAwait(false);
						process = new ProcessAsync(Process.GetProcessById(processId));
					}
					else
					{
						string arguments = Settings.GetCmdArguments();
						process = StartProcess(arguments);

						bool isRunning = await EnsureRunningAsync(process, cancellationToken).ConfigureAwait(false);

						if (!isRunning)
						{
							Logger.LogTrace("Failed to start Tor process. Trying again.");
							continue;
						}

						controlClient = await InitTorControlAsync(cancellationToken).ConfigureAwait(false);
					}

					Logger.LogInfo("Tor is running.");
					_tcs.SetResult(cts.Token);

					// Only now we know that Tor process is fully started.
					lock (StateLock)
					{
						TorProcess = process;
						TorControlClient = controlClient;
					}

					await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					Logger.LogDebug("User canceled operation.");
					setNewTcs = false;
					break;
				}
				catch (Exception ex)
				{
					Logger.LogError("Unexpected problem in starting Tor.", ex);
					setNewTcs = false;
					throw;
				}
				finally
				{
					TaskCompletionSource<CancellationToken> originalTcs = _tcs;

					if (setNewTcs)
					{
						// (1) and (2) must be in this order. Otherwise, there is a race condition risk of getting invalid CT by clients.
						TaskCompletionSource<CancellationToken> newTcs = new();
						originalTcs = Interlocked.Exchange(ref _tcs, newTcs); // (1)
					}

					cts.Cancel(); // (2)
					originalTcs.TrySetResult(cts.Token);
					cts.Dispose();

					if (controlClient is not null)
					{
						await controlClient.DisposeAsync().ConfigureAwait(false);
					}

					process?.Dispose();

					lock (StateLock)
					{
						TorProcess = null;
						TorControlClient = null;
					}
				}
			}
		}

		/// <summary>Ensure <paramref name="process"/> is actually running.</summary>
		internal virtual async Task<bool> EnsureRunningAsync(ProcessAsync process, CancellationToken token)
		{
			int i = 0;
			while (true)
			{
				i++;

				bool isRunning = await TcpConnectionFactory.IsTorRunningAsync().ConfigureAwait(false);

				if (isRunning)
				{
					return true;
				}

				if (process.HasExited)
				{
					Logger.LogError("Tor process failed to start!");
					return false;
				}

				const int MaxAttempts = 25;

				if (i >= MaxAttempts)
				{
					Logger.LogError($"All {MaxAttempts} attempts to connect to Tor failed.");
					return false;
				}

				// Wait 250 milliseconds between attempts.
				await Task.Delay(250, token).ConfigureAwait(false);
			}
		}

		/// <param name="arguments">Command line arguments to start Tor OS process with.</param>
		internal virtual ProcessAsync StartProcess(string arguments)
		{
			ProcessStartInfo startInfo = new()
			{
				FileName = Settings.TorBinaryFilePath,
				Arguments = arguments,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				WorkingDirectory = Settings.TorBinaryDir
			};

			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				var env = startInfo.EnvironmentVariables;

				env["LD_LIBRARY_PATH"] = !env.ContainsKey("LD_LIBRARY_PATH") || string.IsNullOrEmpty(env["LD_LIBRARY_PATH"])
					? Settings.TorBinaryDir
					: Settings.TorBinaryDir + Path.PathSeparator + env["LD_LIBRARY_PATH"];

				Logger.LogDebug($"Environment variable 'LD_LIBRARY_PATH' set to: '{env["LD_LIBRARY_PATH"]}'.");
			}

			Logger.LogInfo("Starting Tor process ...");
			ProcessAsync process = new(startInfo);
			process.Start();

			return process;
		}

		/// <summary>Connects to Tor control using a TCP client or throws <see cref="TorControlException"/>.</summary>
		/// <exception cref="TorControlException">When authentication fails for some reason.</exception>
		/// <seealso href="https://gitweb.torproject.org/torspec.git/tree/control-spec.txt">This method follows instructions in 3.23. TAKEOWNERSHIP.</seealso>
		internal virtual async Task<TorControlClient> InitTorControlAsync(CancellationToken token = default)
		{
			// Get cookie.
			string cookieString = ByteHelpers.ToHex(File.ReadAllBytes(Settings.CookieAuthFilePath));

			// Authenticate.
			TorControlClientFactory factory = new();
			TorControlClient client = await factory.ConnectAndAuthenticateAsync(Settings.ControlEndpoint, cookieString, token).ConfigureAwait(false);

			if (Settings.TerminateOnExit)
			{
				// This is necessary for the scenario when Tor was started by a previous WW instance with TerminateTorOnExit=false configuration option.
				TorControlReply takeReply = await client.TakeOwnershipAsync(token).ConfigureAwait(false);

				if (!takeReply)
				{
					throw new TorControlException($"Failed to take ownership of the Tor instance. Reply: '{takeReply}'.");
				}

				TorControlReply resetReply = await client.ResetOwningControllerProcessConfAsync(token).ConfigureAwait(false);

				if (!resetReply)
				{
					throw new TorControlException($"Failed to reset __OwningControllerProcess. Reply: '{resetReply}'.");
				}
			}

			return client;
		}

		public async ValueTask DisposeAsync()
		{
			LoopCts.Cancel();

			if (LoopTask is Task t)
			{
				await t.ConfigureAwait(false);
			}

			LoopCts.Dispose();

			ProcessAsync? process;
			TorControlClient? torControlClient;

			lock (StateLock)
			{
				process = TorProcess;
				torControlClient = TorControlClient;
			}

			if (torControlClient is TorControlClient)
			{
				// Even though terminating the TCP connection with Tor would shut down Tor,
				// the spec is quite clear:
				// > As of Tor 0.2.5.2-alpha, Tor does not wait a while for circuits to
				// > close when shutting down because of an exiting controller. If you
				// > want to ensure a clean shutdown--and you should!--then send "SIGNAL
				// > SHUTDOWN" and wait for the Tor process to close.)
				if (Settings.TerminateOnExit)
				{
					await torControlClient.SignalShutdownAsync().ConfigureAwait(false);
				}

				// Leads to Tor termination because we sent TAKEOWNERSHIP command.
				await torControlClient.DisposeAsync().ConfigureAwait(false);
			}

			// Dispose Tor process resources (does not stop/kill Tor process).
			process?.Dispose();
		}
	}
}
