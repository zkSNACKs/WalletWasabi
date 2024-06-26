using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.Bases;
using WalletWasabi.Daemon;
using WalletWasabi.Logging;

namespace WalletWasabi.Fluent.Helpers;

public static class CoordinatorConfigStringHelper
{
	// Format: ccs|<network>|<endpoint>|<coordinatorFee>|<absoluteMinInputCount>|<readMore>
	private const int ExpectedParts = 6;
	private const char Separator = '|';
	private const string Prefix = "ccs";

	public static async Task ProcessAsync(CoordinatorConfigString coordinatorConfigString)
	{
		// TODO: Verify with GetStatus?

		// TODO: SANITY CHECKS !!!

		PersistentConfig config = ConfigManagerNg.LoadFile<PersistentConfig>(Services.PersistentConfigFilePath);

		if (coordinatorConfigString.Network == Network.Main)
		{
			config = config with { MainNetCoordinatorUri = coordinatorConfigString.Endpoint.ToString() };
		}
		else if (coordinatorConfigString.Network == Network.TestNet)
		{
			config = config with { TestNetCoordinatorUri = coordinatorConfigString.Endpoint.ToString() };
		}
		else if (coordinatorConfigString.Network == Network.RegTest)
		{
			config = config with { RegTestCoordinatorUri = coordinatorConfigString.Endpoint.ToString() };
		}
		else
		{
			Logger.LogWarning("Unknown network");
			return;
		}

		config = config with { MaxCoordinationFeeRate = coordinatorConfigString.CoordinatorFee };

		// TODO: Implement AbsoluteMinInputCount and ReadMore

		ConfigManagerNg.ToFile(Services.PersistentConfigFilePath, config);

		await ApplicationHelper.SetTextAsync("");
	}

	public static async Task<CoordinatorConfigString?> ParseAsync(string text)
	{

		if (!text.StartsWith(Prefix + Separator))
		{
			return null;
		}

		var parts = text.Split(Separator);
		if (parts.Length != ExpectedParts)
		{
			return null;
		}

		try
		{
			return new CoordinatorConfigString(
				Network.GetNetwork(parts[1])!,
				new Uri(parts[2]),
				decimal.Parse(parts[3]),
				int.Parse(parts[4]),
				new Uri(parts[5]));
		}
		catch (Exception)
		{
			Logger.LogWarning("One parameter of the magic string was incorrect.");

			// Clear clipboard to avoid repetitive failed call.
			await ApplicationHelper.SetTextAsync("");
			return null;
		}
	}

	public record CoordinatorConfigString(
		Network Network,
		Uri Endpoint,
		decimal CoordinatorFee,
		int AbsoluteMinInputCount,
		Uri ReadMore);
}
