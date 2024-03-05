using NBitcoin;
using NBitcoin.Protocol;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using WalletWasabi.Backend.Models;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Blockchain.BlockFilters;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Helpers;
using WalletWasabi.Models;
using WalletWasabi.Services;
using WalletWasabi.Stores;
using WalletWasabi.Tests.XunitConfiguration;
using WalletWasabi.Wallets;
using WalletWasabi.WebClients.Wasabi;
using Xunit;

namespace WalletWasabi.Tests.RegressionTests;

/// <seealso cref="RegTestCollectionDefinition"/>
[Collection("RegTest collection")]
public class FilterDownloaderTest : IClassFixture<RegTestFixture>
{
	public FilterDownloaderTest(RegTestFixture regTestFixture)
	{
		RegTestFixture = regTestFixture;
	}

	private RegTestFixture RegTestFixture { get; }

	[Fact]
	public async Task FilterDownloaderTestAsync()
	{
		using CancellationTokenSource testDeadlineCts = new(TimeSpan.FromMinutes(5));

		await using RegTestSetup setup = await RegTestSetup.InitializeTestEnvironmentAsync(RegTestFixture, numberOfBlocksToGenerate: 1);
		IRPCClient rpc = setup.RpcClient;
		BitcoinStore bitcoinStore = setup.BitcoinStore;

		await using WasabiHttpClientFactory httpClientFactory = new(torEndPoint: null, backendUriGetter: () => new Uri(RegTestFixture.BackendEndPoint));
		using WasabiSynchronizer synchronizer = new(period: TimeSpan.FromSeconds(1), bitcoinStore, httpClientFactory, setup.Global.EventBus);
		try
		{
			await synchronizer.StartAsync(CancellationToken.None);

			var blockCount = await rpc.GetBlockCountAsync() + 1; // Plus one because of the zeroth.
																 // Test initial synchronization.
			var times = 0;
			int filterCount;
			while ((filterCount = bitcoinStore.SmartHeaderChain.HashCount) < blockCount)
			{
				if (times > 500) // 30 sec
				{
					throw new TimeoutException($"{nameof(WasabiSynchronizer)} test timed out. Needed filters: {blockCount}, got only: {filterCount}.");
				}
				await Task.Delay(100);
				times++;
			}

			Assert.Equal(blockCount, bitcoinStore.SmartHeaderChain.HashCount);

			// Test later synchronization.
			await RegTestFixture.BackendRegTestNode.GenerateAsync(10);
			times = 0;
			while ((filterCount = bitcoinStore.SmartHeaderChain.HashCount) < blockCount + 10)
			{
				if (times > 500) // 30 sec
				{
					throw new TimeoutException($"{nameof(WasabiSynchronizer)} test timed out. Needed filters: {blockCount + 10}, got only: {filterCount}.");
				}
				await Task.Delay(100);
				times++;
			}

			// Test correct number of filters is received.
			Assert.Equal(blockCount + 10, bitcoinStore.SmartHeaderChain.HashCount);

			// Test filter block hashes are correct.
			FilterModel[] filters = await bitcoinStore.IndexStore.FetchBatchAsync(fromHeight: 0, batchSize: -1, testDeadlineCts.Token);

			for (int i = 0; i < 101; i++)
			{
				var expectedHash = await rpc.GetBlockHashAsync(i);
				var filter = filters[i];
				Assert.Equal(i, (int)filter.Header.Height);
				Assert.Equal(expectedHash, filter.Header.BlockHash);
				Assert.Equal(IndexBuilderService.CreateDummyEmptyFilter(expectedHash).ToString(), filter.Filter.ToString());
			}
		}
		finally
		{
			await synchronizer.StopAsync(CancellationToken.None);
		}
	}
}
