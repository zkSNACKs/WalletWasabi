using NBitcoin;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WabiSabi.Crypto.Randomness;
using WalletWasabi.Backend.Models;
using WalletWasabi.Backend.Models.Responses;
using WalletWasabi.Blockchain.BlockFilters;
using WalletWasabi.Extensions;
using WalletWasabi.Models;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.Tests.XunitConfiguration;
using WalletWasabi.Tor;
using WalletWasabi.Tor.Http;
using WalletWasabi.Tor.Socks5;
using WalletWasabi.Tor.Socks5.Pool;
using WalletWasabi.WebClients.Wasabi;
using Xunit;

namespace WalletWasabi.Tests.IntegrationTests;

[Collection("LiveServerTests collection")]
public class LiveServerTests : IAsyncLifetime
{
	public LiveServerTests(LiveServerTestsFixture liveServerTestsFixture)
	{
		LiveServerTestsFixture = liveServerTestsFixture;

		TorHttpPool = new(new TorTcpConnectionFactory(Common.TorSocks5Endpoint));
		TorProcessManager = new(Common.TorSettings);
	}

	private TorProcessManager TorProcessManager { get; }
	private TorHttpPool TorHttpPool { get; }
	private LiveServerTestsFixture LiveServerTestsFixture { get; }

	public async Task InitializeAsync()
	{
		using CancellationTokenSource startTimeoutCts = new(TimeSpan.FromMinutes(2));

		await TorProcessManager.StartAsync(startTimeoutCts.Token);
	}

	public async Task DisposeAsync()
	{
		await TorHttpPool.DisposeAsync();
		await TorProcessManager.DisposeAsync();
	}

	[Theory]
	[MemberData(nameof(GetNetworks))]
	public async Task GetFiltersAsync(Network network)
	{
		using CancellationTokenSource ctsTimeout = new(TimeSpan.FromMinutes(2));

		FilterModel filterModel = StartingFilters.GetStartingFilter(network);

		WasabiClient client = MakeWasabiClient(network);
		FiltersResponse? filtersResponse = await client.GetFiltersAsync(filterModel.Header.BlockHash, count: 2, ctsTimeout.Token);

		Assert.NotNull(filtersResponse);
		Assert.Equal(2, filtersResponse.Filters.Count());
	}

	[Theory]
	[MemberData(nameof(GetNetworks))]
	public async Task GetTransactionsAsync(Network network)
	{
		using CancellationTokenSource ctsTimeout = new(TimeSpan.FromMinutes(2));

		WasabiClient client = MakeWasabiClient(network);
		IEnumerable<uint256> randomTxIds = Enumerable.Range(0, 20).Select(_ => RandomUtils.GetUInt256());

		// We don't really expect that the random strings represent some actual transactions.
		IEnumerable<Transaction> retrievedTxs = await client.GetTransactionsAsync(network, randomTxIds.Take(4), ctsTimeout.Token);
		Assert.Empty(retrievedTxs);

		var mempoolTxIds = await client.GetMempoolHashesAsync(ctsTimeout.Token);
		randomTxIds = Enumerable.Range(0, 5).Select(_ => mempoolTxIds.RandomElement(InsecureRandom.Instance)!).Distinct().ToArray();
		var txs = await client.GetTransactionsAsync(network, randomTxIds, ctsTimeout.Token);
		var returnedTxIds = txs.Select(tx => tx.GetHash());
		Assert.Equal(returnedTxIds.OrderBy(x => x).ToArray(), randomTxIds.OrderBy(x => x).ToArray());
	}

	[Theory]
	[MemberData(nameof(GetNetworks))]
	public async Task GetVersionsTestsAsync(Network network)
	{
		using CancellationTokenSource ctsTimeout = new(TimeSpan.FromMinutes(2));

		WasabiClient client = MakeWasabiClient(network);
		var versions = await client.GetVersionsAsync(ctsTimeout.Token);
		Assert.InRange(versions.ClientVersion, new(2, 0, 0), new(2, 99, 99));
		Assert.InRange(versions.ClientVersion, new(2, 0, 0), WalletWasabi.Helpers.Constants.ClientVersion);
		Assert.Equal(4, versions.BackendMajorVersion);
		Assert.Equal(new(1, 0), versions.LegalDocumentsVersion);
	}

	[Theory]
	[MemberData(nameof(GetNetworks))]
	public async Task GetLegalDocumentsTestsAsync(Network network)
	{
		using CancellationTokenSource ctsTimeout = new(TimeSpan.FromMinutes(2));

		WasabiClient client = MakeWasabiClient(network);
		var content = await client.GetLegalDocumentsAsync(ctsTimeout.Token);
		var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

		Assert.Equal("Last Updated: 2022-06-15", lines[0]);
		var lineCount = lines.Length;
		Assert.InRange(lineCount, 100, 1000);
	}

	private WasabiClient MakeWasabiClient(Network network)
	{
		Uri baseUri = LiveServerTestsFixture.UriMappings[network];
		TorHttpClient torHttpClient = new(baseUri, TorHttpPool);
		return new WasabiClient(torHttpClient);
	}

	public static IEnumerable<object[]> GetNetworks()
	{
		yield return new object[] { Network.Main };
		yield return new object[] { Network.TestNet };
	}
}
