using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Bases;
using WalletWasabi.Helpers;
using WalletWasabi.Helpers.PowerSaving;
using WalletWasabi.Logging;
using WalletWasabi.Wallets;
using static WalletWasabi.Helpers.PowerSaving.SystemdInhibitorTask;

namespace WalletWasabi.Services
{
	public class SystemAwakeChecker : PeriodicRunner
	{
		private const string Reason = "CoinJoin is in progress.";
		private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

		private volatile IPowerSavingInhibitorTask? _powerSavingTask;

		public SystemAwakeChecker(WalletManager walletManager) : base(TimeSpan.FromMinutes(1))
		{
			WalletManager = walletManager;
		}

		private WalletManager WalletManager { get; }

		protected override async Task ActionAsync(CancellationToken cancel)
		{
			IPowerSavingInhibitorTask? task = _powerSavingTask;

			if (WalletManager.AnyCoinJoinInProgress())
			{
				Logger.LogWarning($"XXX: CJ is in progress, make sure to prolong awake state.");

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				{					
					if (task is not null)
					{
						Logger.LogWarning($"XXX: Prolong power saving prevention task by one minute.");
						task.Prolong(TimeSpan.FromMinutes(1));
					}
					else
					{
						Logger.LogWarning($"XXX: Create new power saving prevention task.");
						_powerSavingTask = SystemdInhibitorTask.Create(InhibitWhat.All, Timeout, Reason);
					}
				}
				else
				{
					await EnvironmentHelpers.ProlongSystemAwakeAsync().ConfigureAwait(false);
				}
			}
			else
			{
				if (task is not null)
				{
					Logger.LogWarning($"XXX: Stop task.");
					await task.StopAsync().ConfigureAwait(false);
				}
			}			
		}
	}
}
