using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Web;
using NBitcoin;

namespace WalletWasabi.Models;

public record CoordinatorConnectionString(
    string Name,
    Network Network,
    Uri Endpoint,
    decimal CoordinatorFeePercent,
    int AbsoluteMinInputCount,
    Uri ReadMore)
{
    public override string ToString()
    {
        return $"name={Uri.EscapeDataString(Name)}" +
               $"&network={Network.Name}" +
               $"&endpoint={Uri.EscapeDataString(Endpoint.ToString())}" +
               $"&coordinatorFeePercent={CoordinatorFeePercent.ToString(CultureInfo.InvariantCulture)}" +
               $"&absoluteMinInputCount={AbsoluteMinInputCount}" +
               $"&readMore={Uri.EscapeDataString(ReadMore.ToString())}";
    }

    public static bool TryParse(string s, [NotNullWhen(true)] out CoordinatorConnectionString? coordinatorConnectionString)
    {
        coordinatorConnectionString = null;

        var queryString = HttpUtility.ParseQueryString(s);
        string[] requiredParams = ["name", "network", "endpoint", "coordinatorFeePercent", "absoluteMinInputCount", "readMore"];

        if (requiredParams.Any(param => string.IsNullOrEmpty(queryString[param])))
        {
	        return false;
        }

        var name = queryString["name"]!;

        var network = Network.GetNetwork(queryString["network"]!);
        if (network is null)
        {
            return false;
        }

        if (!Uri.TryCreate(queryString["endpoint"], UriKind.Absolute, out var endpoint))
        {
            return false;
        }

        if (!decimal.TryParse(queryString["coordinatorFeePercent"], NumberStyles.Any, CultureInfo.InvariantCulture, out var coordinatorFeePercent))
        {
            return false;
        }

        if (!int.TryParse(queryString["absoluteMinInputCount"], out var absoluteMinInputCount))
        {
            return false;
        }

        if (!Uri.TryCreate(queryString["readMore"], UriKind.Absolute, out var readMore))
        {
            return false;
        }

        coordinatorConnectionString = new CoordinatorConnectionString(
            name,
            network,
            endpoint,
            coordinatorFeePercent,
            absoluteMinInputCount,
            readMore);

        return true;
    }
}
