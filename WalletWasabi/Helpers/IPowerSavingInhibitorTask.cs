using System;
using System.Threading.Tasks;

namespace WalletWasabi.Helpers
{
	/// <summary>Task that makes sure that computer does not go to sleep, switch to idle state, etc.</summary>
	public interface IPowerSavingInhibitorTask
	{
		/// <remarks>Useful to know whether a new task needs to be created.</remarks>
		bool IsDone { get; }

		/// <summary>Prolongs the interval how long the power saving should be postponed by.</summary>
		/// <returns><c>true</c> when prolonging of the power saving task succeeded, <c>false</c> when the task is already stopped.</returns>
		bool Prolong(TimeSpan timeSpan);

		/// <summary>Stop the internal power saving mechanism.</summary>
		/// <remarks>Once the task is stopped, computer may go idle/reboot, etc. Operating systems' power saving timers may be reseted or not - we provide no guarantees here.</remarks>
		Task StopAsync();
	}
}
