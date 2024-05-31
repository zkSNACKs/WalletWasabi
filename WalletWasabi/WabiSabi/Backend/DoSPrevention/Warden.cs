using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;

namespace WalletWasabi.WabiSabi.Backend.DoSPrevention;

public class Warden : BackgroundService
{
	public Warden(WabiSabiConfig config)
	{
		PrisonFilePath = "Prison.txt";
		Config = config;
		OffendersToSaveChannel = Channel.CreateUnbounded<Offender>();

		Prison = DeserializePrison(PrisonFilePath, OffendersToSaveChannel.Writer);
	}

	public Prison Prison { get; }

	public string PrisonFilePath { get; }
	private WabiSabiConfig Config { get; }

	private Channel<Offender> OffendersToSaveChannel { get; }

	private static Prison DeserializePrison( string prisonFilePath, ChannelWriter<Offender> channelWriter)
	{
		IoHelpers.EnsureContainingDirectoryExists(prisonFilePath);
		var offenders = new List<Offender>();
		if (File.Exists(prisonFilePath))
		{
			try
			{
				foreach (var offender in File.ReadAllLines(prisonFilePath).Select(Offender.FromStringLine))
				{
					offenders.Add(offender);
				}
			}
			catch (Exception ex)
			{
				Logger.LogError(ex);
				Logger.LogWarning($"Deleting {prisonFilePath}");
				File.Delete(prisonFilePath);
			}
		}

		return new Prison(offenders, channelWriter);
	}

	protected override async Task ExecuteAsync(CancellationToken cancel)
	{
		try
		{
			while (!cancel.IsCancellationRequested)
			{
				await foreach (var inmate in OffendersToSaveChannel.Reader.ReadAllAsync(cancel).ConfigureAwait(false))
				{
					var lines = Enumerable.Repeat(inmate.ToStringLine(), 1);
					await File.AppendAllLinesAsync(PrisonFilePath, lines, CancellationToken.None).ConfigureAwait(false);
				}
			}
		}
		catch (OperationCanceledException)
		{
			Logger.LogInfo("Warden was requested to stop.");
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);
			throw;
		}
	}

	public override Task StopAsync(CancellationToken cancellationToken)
	{
		OffendersToSaveChannel.Writer.Complete();
		return base.StopAsync(cancellationToken);
	}
}
