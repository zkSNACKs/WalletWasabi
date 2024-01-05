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
	public async Task CheckUpdatesTestsAsync(Network network)
	{
		using CancellationTokenSource ctsTimeout = new(TimeSpan.FromMinutes(2));

		WasabiClient client = MakeWasabiClient(network);
		UpdateStatus updateStatus = await client.CheckUpdatesAsync(ctsTimeout.Token);

		Assert.True(updateStatus.BackendCompatible);
		Assert.True(updateStatus.ClientUpToDate);
		Assert.Equal(new Version(1, 0), updateStatus.LegalDocumentsVersion);
		Assert.Equal((ushort)4, updateStatus.CurrentBackendMajorVersion);
		Assert.Equal(WalletWasabi.Helpers.Constants.ClientVersion.ToString(3), updateStatus.ClientVersion.ToString());

		var versions = await client.GetVersionsAsync(ctsTimeout.Token);
		Assert.Equal(versions.LegalDocumentsVersion, updateStatus.LegalDocumentsVersion);
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
