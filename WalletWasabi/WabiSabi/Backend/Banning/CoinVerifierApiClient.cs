using NBitcoin;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Logging;

namespace WalletWasabi.WabiSabi.Backend.Banning;

public class CoinVerifierApiClient
{
	public CoinVerifierApiClient(string token, Network network, HttpClient httpClient)
	{
		ApiToken = token;
		Network = network;
		HttpClient = httpClient;
	}

	public CoinVerifierApiClient() : this("", Network.Main, new() { BaseAddress = new("https://www.test.test") })
	{
	}

	private string ApiToken { get; set; }
	private Network Network { get; set; }

	private HttpClient HttpClient { get; set; }

	public virtual async Task<(ApiResponseItem ApiResponseItem, Script Script)> SendRequestAsync(Script script, CancellationToken cancellationToken)
	{
		if (HttpClient.BaseAddress is null)
		{
			throw new HttpRequestException($"{nameof(HttpClient.BaseAddress)} was null.");
		}
		if (HttpClient.BaseAddress.Scheme != "https")
		{
			throw new HttpRequestException($"The connection to the API is not safe. Expected https but was {HttpClient.BaseAddress.Scheme}.");
		}

		var address = script.GetDestinationAddress(Network.Main); // API provider don't accept testnet/regtest addresses.

		using CancellationTokenSource timeoutTokenSource = new(TimeSpan.FromSeconds(15));
		using CancellationTokenSource linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTokenSource.Token);
		using var content = new HttpRequestMessage(HttpMethod.Get, $"{HttpClient.BaseAddress}{address}");
		content.Headers.Authorization = new("Bearer", ApiToken);

		var response = await HttpClient.SendAsync(content, linkedTokenSource.Token).ConfigureAwait(false);

		if (response.StatusCode == HttpStatusCode.Forbidden)
		{
			throw new UnauthorizedAccessException("User roles access forbidden.");
		}
		else if (response.StatusCode != HttpStatusCode.OK)
		{
			throw new InvalidOperationException($"API request failed. {nameof(HttpStatusCode)} was {response.StatusCode}.");
		}

		string responseString = await response.Content.ReadAsStringAsync(linkedTokenSource.Token).ConfigureAwait(false);

		ApiResponseItem deserializedRecord = JsonConvert.DeserializeObject<ApiResponseItem>(responseString)
			?? throw new JsonSerializationException($"Failed to deserialize API response, response string was: '{responseString}'");
		return (deserializedRecord, script);
	}

	public async IAsyncEnumerable<(ApiResponseItem? ApiResponseItem, Script ScriptPubKey)> VerifyScriptsAsync(IEnumerable<Script> scriptsToCheck, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		Dictionary<Task<(ApiResponseItem, Script)>, Script> taskScriptPairs = new();

		foreach (Script script in scriptsToCheck)
		{
			taskScriptPairs.Add(Task.Run(() => SendRequestAsync(script, cancellationToken)), script);
		}

		foreach (var task in taskScriptPairs.Keys)
		{
			(ApiResponseItem? ApiResponseItem, Script? ScriptPubKey) response = (null, null);
			try
			{
				var completedTask = await Task.WhenAny(task).ConfigureAwait(false);

				response = await completedTask.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				Logger.LogWarning($"API response didn't arrive in time, operation was cancelled.");
			}
			catch (Exception ex)
			{
				Logger.LogError(ex);
				continue;
			}

			// If response.ScriptPubKey is null, then the task timed out, use the dictionary to map back the timed out script.
			yield return (response.ApiResponseItem, response.ScriptPubKey ?? taskScriptPairs[task]);
		}
	}
}
