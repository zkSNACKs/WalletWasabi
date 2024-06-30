using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.Bases;
using WalletWasabi.Daemon;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Logging;

namespace WalletWasabi.Fluent.Helpers;

public static class CoordinatorConfigStringHelper
{
	// Format: ccs|<name>|<network>|<endpoint>|<coordinatorFee>|<absoluteMinInputCount>|<readMore>
	private const int ExpectedParts = 7;
	private const char Separator = '|';
	private const string Prefix = "ccs";

	public static void Process(CoordinatorConfigString coordinatorConfigString, IApplicationSettings applicationSettings)
	{
		// Sanity checks
		if (coordinatorConfigString.CoordinatorFee > 1)
		{
			Logger.LogWarning($"New intended coordinator fee rate was {coordinatorConfigString.CoordinatorFee}%, but absolute max is 1%");
			return;
		}
		if (coordinatorConfigString.AbsoluteMinInputCount < 2)
		{
			Logger.LogWarning($"New intended absolute min input count was {coordinatorConfigString.AbsoluteMinInputCount}, but absolute min is 2");
		}

		PersistentConfig config = ConfigManagerNg.LoadFile<PersistentConfig>(Services.PersistentConfigFilePath);

		if (applicationSettings.Network == coordinatorConfigString.Network)
		{
			// Only change CoordinatorUri in the UI if Network is the current one.
			applicationSettings.CoordinatorUri = coordinatorConfigString.Endpoint.ToString();
		}

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
		applicationSettings.MaxCoordinationFeeRate = coordinatorConfigString.CoordinatorFee.ToString(CultureInfo.InvariantCulture);

		config = config with { AbsoluteMinInputCount = coordinatorConfigString.AbsoluteMinInputCount };
		applicationSettings.AbsoluteMinInputCount = coordinatorConfigString.AbsoluteMinInputCount.ToString();

		// TODO: Save Name and ReadMoreUri to display it after.

		ConfigManagerNg.ToFile(Services.PersistentConfigFilePath, config);
	}

	public static CoordinatorConfigString? Parse(string text)
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
				parts[1],
				Network.GetNetwork(parts[2])!,
				new Uri(parts[3]),
				decimal.Parse(parts[4]),
				int.Parse(parts[5]),
				new Uri(parts[6]));
		}
		catch (Exception ex)
		{
			Logger.LogWarning($"One parameter of the coordinator config string was incorrect: {ex}");
			return null;
		}
	}

	public record CoordinatorConfigString(
		string Name,
		Network Network,
		Uri Endpoint,
		decimal CoordinatorFee,
		int AbsoluteMinInputCount,
		Uri ReadMore);
}
