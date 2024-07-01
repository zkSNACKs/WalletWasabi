using NBitcoin;
using WalletWasabi.Logging;
using WalletWasabi.Models;

namespace WalletWasabi.Helpers;

public static class CoordinatorConfigStringHelper
{
	// Format: ccs|<name>|<network>|<endpoint>|<coordinatorFee>|<absoluteMinInputCount>|<readMore>
	private const int ExpectedParts = 7;
	private const char Separator = '|';
	private const string Prefix = "ccs";

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
}
