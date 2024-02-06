using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WalletWasabi.Tests.UnitTests.Helpers;

internal class UsbAttachHelper
{
	public static async Task<string?> DetectNewDriveAsync()
	{
		List<string> previousDrives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name).ToList();
		var tcs = new TaskCompletionSource<string?>();

		var timer = new Timer(o =>
		{
			List<string> currentDrives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name).ToList();
			var newDrives = currentDrives.Except(previousDrives).ToList();

			if (newDrives.Count != 0)
			{
				tcs.TrySetResult(newDrives.First()); // Return the first new drive detected
				((Timer)o)?.Change(Timeout.Infinite, Timeout.Infinite);
			}

			previousDrives = currentDrives;
		}, null, 0, 500);

		// Cancel the task if no drive is detected within 20 seconds
		var cancelTask = Task.Delay(60000);
		await Task.WhenAny(tcs.Task, cancelTask);

		timer.Change(Timeout.Infinite, Timeout.Infinite);
		await timer.DisposeAsync();

		return tcs.Task.IsCompleted ? tcs.Task.Result : null;
	}
}
