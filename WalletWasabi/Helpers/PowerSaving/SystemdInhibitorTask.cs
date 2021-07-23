using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Logging;
using WalletWasabi.Microservices;

namespace WalletWasabi.Helpers.PowerSaving
{
	/// <summary><c>systemd-inhibitor</c> API wrapper.</summary>
	/// <remarks>Only works on Linux machines that use systemd.</remarks>
	/// <seealso href="https://www.freedesktop.org/wiki/Software/systemd/inhibit/"/>
	public class SystemdInhibitorTask : IPowerSavingInhibitorTask
	{
		/// <remarks>Guarded by <see cref="StateLock"/>.</remarks>
		private bool _isDone;

		/// <remarks>Use the constructor only in tests.</remarks>
		internal SystemdInhibitorTask(InhibitWhat what, TimeSpan period, string reason, ProcessAsync process)
		{
			What = what;
			BasePeriod = period;
			Reason = reason;
			Process = process;
			Cts = new CancellationTokenSource(period);

			Task = WaitAsync();
		}

		[Flags]
		public enum InhibitWhat
		{
			/// <summary>
			/// Inhibits that the system goes into idle mode, possibly resulting in automatic system
			/// suspend or shutdown depending on configuration.
			/// </summary>
			Idle = 1,

			/// <summary>Inhibits system suspend and hibernation requested by (unprivileged) users.</summary>
			Sleep = 2,

			/// <summary>Inhibits high-level system power-off and reboot requested by (unprivileged) users.</summary>
			Shutdown = 4,

			All = Idle | Sleep | Shutdown
		}

		/// <remarks>Guards <see cref="_isDone"/>.</remarks>
		private object StateLock { get; } = new();

		public InhibitWhat What { get; }

		/// <remarks>It holds: inhibitorEndTime = now + BasePeriod + ProlongInterval.</remarks>
		public TimeSpan BasePeriod { get; }

		/// <summary>Reason why the power saving is inhibited.</summary>
		public string Reason { get; }
		private ProcessAsync Process { get; }
		private CancellationTokenSource Cts { get; }
		private TaskCompletionSource StoppedTcs { get; } = new();
		private Task Task { get; }

		/// <inheritdoc/>
		public bool IsDone
		{
			get
			{
				lock (StateLock)
				{
					return _isDone;
				}
			}
		}

		private async Task WaitAsync()
		{
			try
			{
				await Process.WaitForExitAsync(Cts.Token).ConfigureAwait(false);

				// This should be hit only when somebody externally kills the systemd-inhibit process.
				Logger.LogError("systemd-inhibit task ended prematurely.");
			}
			catch (OperationCanceledException)
			{
				Logger.LogTrace("Elapsed time limit for the inhibitor task to live.");

				// Process cannot stop on its own so we know it is actually running.
				Process.Kill();

				Logger.LogWarning($"XXX: systemd-inhibit task was killed.");
			}
			finally
			{
				lock (StateLock)
				{
					Cts.Cancel();
					Cts.Dispose();
					_isDone = true;
					StoppedTcs.SetResult();
				}

				Logger.LogTrace("systemd-inhibit task is finished.");
				Logger.LogWarning($"XXX: systemd-inhibit task is finished.");
			}
		}

		/// <inheritdoc/>
		public bool Prolong(TimeSpan period)
		{
			string logMessage = "N/A";

			try
			{
				lock (StateLock)
				{
					if (!_isDone && !Cts.IsCancellationRequested)
					{
						// This does nothing when cancellation of CTS is already requested.
						Cts.CancelAfter(period);
						logMessage = $"Power saving task was prolonged to: {DateTime.UtcNow.Add(period)}";
						return !Cts.IsCancellationRequested;
					}

					logMessage = "Power saving task is already finished.";
					return false;
				}
			}
			catch (Exception ex)
			{
				Logger.LogError(ex);
				return false;
			}
			finally
			{
				Logger.LogWarning($"XXX: {logMessage}");
				Logger.LogTrace(logMessage);
			}
		}

		public Task StopAsync()
		{
			lock (StateLock)
			{
				if (!_isDone)
				{
					Cts.Cancel();
				}
			}

			return StoppedTcs.Task;
		}

		public static async Task<bool> IsSupportedAsync()
		{
			string shellCommand = "systemd-inhibit --help";

			try
			{
				using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
				ProcessStartInfo processStartInfo = EnvironmentHelpers.GetShellProcessStartInfo(shellCommand);
				Process process = System.Diagnostics.Process.Start(processStartInfo)!;

				await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
				bool success = process.ExitCode == 0;
				Logger.LogDebug($"systemd-inhibit is {(success ? "supported" : "NOT supported")}.");

				return success;
			}
			catch (Exception ex)
			{
				Logger.LogError("Failed to find out whether systemd-inhibit is supported or not.", ex);
			}

			return false;
		}

		/// <remarks><paramref name="reason"/> cannot contain apostrophe characters.</remarks>
		public static SystemdInhibitorTask Create(InhibitWhat what, TimeSpan basePeriod, string reason)
		{
			string whatArgument = Convert(what);

			// Make sure that the systemd-inhibit is terminated once the parent process (WW) finishes.
			string innerCommand = $"tail --pid={Environment.ProcessId} -f /dev/null";
			string shellCommand = $"systemd-inhibit --why='{reason}' --what='{whatArgument}' --mode=block {innerCommand}";

			Logger.LogWarning($"XXX: shell command to invoke: {shellCommand}");

			ProcessStartInfo processStartInfo = EnvironmentHelpers.GetShellProcessStartInfo(shellCommand);
			ProcessAsync process = new(processStartInfo);
			process.Start();
			SystemdInhibitorTask task = new(what, basePeriod, reason, process);

			return task;
		}

		private static string Convert(InhibitWhat what)
		{
			List<string> whatList = new();

			if (what.HasFlag(InhibitWhat.Idle))
			{
				whatList.Add("idle");
			}

			if (what.HasFlag(InhibitWhat.Sleep))
			{
				whatList.Add("sleep");
			}

			if (what.HasFlag(InhibitWhat.Shutdown))
			{
				whatList.Add("shutdown");
			}

			string whatArgument = string.Join(':', whatList);
			return whatArgument;
		}
	}
}
