using NBitcoin;
using NBitcoin.Protocol;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.FeeRateEstimation;
using WalletWasabi.Helpers;
using WalletWasabi.Models;
using WalletWasabi.Services;
using WalletWasabi.Stores;
using WalletWasabi.Tests.XunitConfiguration;
using WalletWasabi.Wallets;
using WalletWasabi.Wallets.FilterProcessor;
using WalletWasabi.WebClients.Wasabi;
using Xunit;

namespace WalletWasabi.Tests.RegressionTests;

/// <seealso cref="RegTestCollectionDefinition"/>
[Collection("RegTest collection")]
public class WalletTests : IClassFixture<RegTestFixture>
{
	public WalletTests(RegTestFixture regTestFixture)
	{
		RegTestFixture = regTestFixture;
	}

	private RegTestFixture RegTestFixture { get; }

	[Fact]
	public async Task WalletTestsAsync()
	{
		using CancellationTokenSource testDeadlineCts = new(TimeSpan.FromMinutes(5));

		await using RegTestSetup setup = await RegTestSetup.InitializeTestEnvironmentAsync(RegTestFixture, numberOfBlocksToGenerate: 1, nameof(WalletTestsAsync));
		IRPCClient rpc = setup.RpcClient;
		Network network = setup.Network;
		BitcoinStore bitcoinStore = setup.BitcoinStore;

		// Create the services.
		// 1. Create connection service.
		NodesGroup nodes = new(setup.Network, requirements: Constants.NodeRequirements);
		nodes.ConnectedNodes.Add(await RegTestFixture.BackendRegTestNode.CreateNewP2pNodeAsync());

		Node node = await RegTestFixture.BackendRegTestNode.CreateNewP2pNodeAsync();
		node.Behaviors.Add(bitcoinStore.CreateUntrustedP2pBehavior());

		// 2. Create wasabi synchronizer service.
		await using WasabiHttpClientFactory httpClientFactory = new(torEndPoint: null, backendUriGetter: () => new Uri(RegTestFixture.BackendEndPoint));
		using WasabiSynchronizer synchronizer = new(period: TimeSpan.FromSeconds(3), 1000, bitcoinStore, httpClientFactory.SharedWasabiClient);
		using FeeRateEstimationUpdater feeProvider = new(TimeSpan.Zero, () => "BlockstreamInfo");

		// 3. Create key manager service.
		var keyManager = KeyManager.CreateNew(out _, setup.Password, network);

		// 4. Create wallet service.
		var workDir = Helpers.Common.GetWorkDir(nameof(WalletTestsAsync));

		IFileSystemBlockRepository blockRepository = bitcoinStore.BlockRepository;

		using MemoryCache cache = new(new MemoryCacheOptions
		{
			SizeLimit = 1_000,
			ExpirationScanFrequency = TimeSpan.FromSeconds(30)
		});

		await using SpecificNodeBlockProvider specificNodeBlockProvider = new(network, setup.ServiceConfiguration, httpClientFactory.TorEndpoint);

		using BlockDownloadService blockDownloadService = new(
			bitcoinStore.BlockRepository,
			[specificNodeBlockProvider],
			new P2PBlockProvider(network, nodes, httpClientFactory.IsTorEnabled));

		using UnconfirmedTransactionChainProvider unconfirmedChainProvider = new(httpClientFactory);

		WalletFactory walletFactory = new(workDir, network, bitcoinStore, synchronizer, httpClientFactory.SharedWasabiClient, setup.ServiceConfiguration, feeProvider, blockDownloadService, unconfirmedChainProvider);
		using Wallet wallet = walletFactory.CreateAndInitialize(keyManager);
		wallet.NewFiltersProcessed += setup.Wallet_NewFiltersProcessed;

		// Get some money, make it confirm.
		var key = keyManager.GetNextReceiveKey("foo label");
		var txId = await rpc.SendToAddressAsync(key.GetP2wpkhAddress(network), Money.Coins(0.1m));
		await rpc.GenerateAsync(1);

		try
		{
			Interlocked.Exchange(ref setup.FiltersProcessedByWalletCount, 0);
			nodes.Connect(); // Start connection service.
			node.VersionHandshake(); // Start mempool service.
			await synchronizer.StartAsync(CancellationToken.None); // Start wasabi synchronizer service.
			await feeProvider.StartAsync(testDeadlineCts.Token);

			using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
			{
				await wallet.StartAsync(cts.Token); // Initialize wallet and filter processing services.
			}

			// Wait until the filter our previous transaction is present.
			var blockCount = await rpc.GetBlockCountAsync();
			await setup.WaitForFiltersToBeProcessedAsync(TimeSpan.FromSeconds(120), blockCount);

			Assert.Equal(1, await blockRepository.CountAsync(testDeadlineCts.Token));

			Assert.Single(wallet.Coins);
			var firstCoin = wallet.Coins.Single();
			Assert.Equal(Money.Coins(0.1m), firstCoin.Amount);
			Assert.Equal(new Height((int)bitcoinStore.SmartHeaderChain.TipHeight), firstCoin.Height);
			Assert.InRange(firstCoin.Index, 0U, 1U);
			Assert.True(firstCoin.IsAvailable());
			Assert.Equal("foo label", firstCoin.HdPubKey.Labels);
			Assert.Equal(key.P2wpkhScript, firstCoin.ScriptPubKey);
			Assert.Null(firstCoin.SpenderTransaction);
			Assert.Equal(txId, firstCoin.TransactionId);
			Assert.Single(keyManager.GetKeys(KeyState.Used, false));
			Assert.Equal("foo label", keyManager.GetKeys(KeyState.Used, false).Single().Labels);

			// Get some money, make it confirm.
			var key2 = keyManager.GetNextReceiveKey("bar label");
			var txId2 = await rpc.SendToAddressAsync(key2.GetP2wpkhAddress(network), Money.Coins(0.01m));
			Interlocked.Exchange(ref setup.FiltersProcessedByWalletCount, 0);
			await rpc.GenerateAsync(1);
			var txId3 = await rpc.SendToAddressAsync(key2.GetP2wpkhAddress(network), Money.Coins(0.02m));
			await rpc.GenerateAsync(1);

			await setup.WaitForFiltersToBeProcessedAsync(TimeSpan.FromSeconds(120), 2);
			Assert.Equal(3, await blockRepository.CountAsync(testDeadlineCts.Token));

			Assert.Equal(3, wallet.Coins.Count());
			firstCoin = wallet.Coins.OrderBy(x => x.Height).First();
			var secondCoin = wallet.Coins.OrderBy(x => x.Height).Take(2).Last();
			var thirdCoin = wallet.Coins.OrderBy(x => x.Height).Last();
			Assert.Equal(Money.Coins(0.01m), secondCoin.Amount);
			Assert.Equal(Money.Coins(0.02m), thirdCoin.Amount);
			Assert.Equal(new Height(bitcoinStore.SmartHeaderChain.TipHeight).Value - 2, firstCoin.Height.Value);
			Assert.Equal(new Height(bitcoinStore.SmartHeaderChain.TipHeight).Value - 1, secondCoin.Height.Value);
			Assert.Equal(new Height(bitcoinStore.SmartHeaderChain.TipHeight), thirdCoin.Height);
			Assert.True(thirdCoin.IsAvailable());
			Assert.Equal("foo label", firstCoin.HdPubKey.Labels);
			Assert.Equal("bar label", secondCoin.HdPubKey.Labels);
			Assert.Equal("bar label", thirdCoin.HdPubKey.Labels);
			Assert.Equal(key.P2wpkhScript, firstCoin.ScriptPubKey);
			Assert.Equal(key2.P2wpkhScript, secondCoin.ScriptPubKey);
			Assert.Equal(key2.P2wpkhScript, thirdCoin.ScriptPubKey);
			Assert.Null(thirdCoin.SpenderTransaction);
			Assert.Equal(txId, firstCoin.TransactionId);
			Assert.Equal(txId2, secondCoin.TransactionId);
			Assert.Equal(txId3, thirdCoin.TransactionId);

			Assert.Equal(2, keyManager.GetKeys(KeyState.Used, false).Count());
			Assert.Empty(keyManager.GetKeys(KeyState.Used, true));
			Assert.Equal(2, keyManager.GetKeys(KeyState.Used).Count());
			Assert.Empty(keyManager.GetKeys(KeyState.Locked, false));
			Assert.Empty(keyManager.GetKeys(KeyState.Locked, true));
			Assert.Empty(keyManager.GetKeys(KeyState.Locked));
			Assert.Equal(42, keyManager.GetKeys(KeyState.Clean, true).Count());
			Assert.Equal(43, keyManager.GetKeys(KeyState.Clean, false).Count());
			Assert.Equal(85, keyManager.GetKeys(KeyState.Clean).Count());
			Assert.Equal(87, keyManager.GetKeys().Count());

			Assert.Single(keyManager.GetKeys(x => x.Labels == "foo label" && x.KeyState == KeyState.Used && !x.IsInternal));
			Assert.Single(keyManager.GetKeys(x => x.Labels == "bar label" && x.KeyState == KeyState.Used && !x.IsInternal));

			// REORG TESTS
			var txId4 = await rpc.SendToAddressAsync(key2.GetP2wpkhAddress(network), Money.Coins(0.03m), replaceable: true);
			Interlocked.Exchange(ref setup.FiltersProcessedByWalletCount, 0);
			await rpc.GenerateAsync(2);
			await setup.WaitForFiltersToBeProcessedAsync(TimeSpan.FromSeconds(120), 2);

			Assert.NotEmpty(wallet.Coins.Where(x => x.TransactionId == txId4));
			var tip = await rpc.GetBestBlockHashAsync();
			await rpc.InvalidateBlockAsync(tip); // Reorg 1
			tip = await rpc.GetBestBlockHashAsync();
			await rpc.InvalidateBlockAsync(tip); // Reorg 2
			var tx4bumpRes = await rpc.BumpFeeAsync(txId4); // RBF it
			Interlocked.Exchange(ref setup.FiltersProcessedByWalletCount, 0);
			await rpc.GenerateAsync(3);
			await setup.WaitForFiltersToBeProcessedAsync(TimeSpan.FromSeconds(120), 3);
			Assert.Equal(4, await blockRepository.CountAsync(testDeadlineCts.Token));

			Assert.Equal(4, wallet.Coins.Count());
			Assert.Empty(wallet.Coins.Where(x => x.TransactionId == txId4));
			Assert.NotEmpty(wallet.Coins.Where(x => x.TransactionId == tx4bumpRes.TransactionId));
			var rbfCoin = wallet.Coins.Single(x => x.TransactionId == tx4bumpRes.TransactionId);

			Assert.Equal(Money.Coins(0.03m), rbfCoin.Amount);
			Assert.Equal(new Height(bitcoinStore.SmartHeaderChain.TipHeight).Value - 2, rbfCoin.Height.Value);
			Assert.True(rbfCoin.IsAvailable());
			Assert.Equal("bar label", rbfCoin.HdPubKey.Labels);
			Assert.Equal(key2.P2wpkhScript, rbfCoin.ScriptPubKey);
			Assert.Null(rbfCoin.SpenderTransaction);
			Assert.Equal(tx4bumpRes.TransactionId, rbfCoin.TransactionId);

			Assert.Equal(2, keyManager.GetKeys(KeyState.Used, false).Count());
			Assert.Empty(keyManager.GetKeys(KeyState.Used, true));
			Assert.Equal(2, keyManager.GetKeys(KeyState.Used).Count());
			Assert.Empty(keyManager.GetKeys(KeyState.Locked, false));
			Assert.Empty(keyManager.GetKeys(KeyState.Locked, true));
			Assert.Empty(keyManager.GetKeys(KeyState.Locked));
			Assert.Equal(42, keyManager.GetKeys(KeyState.Clean, true).Count());
			Assert.Equal(43, keyManager.GetKeys(KeyState.Clean, false).Count());
			Assert.Equal(85, keyManager.GetKeys(KeyState.Clean).Count());
			Assert.Equal(87, keyManager.GetKeys().Count());

			Assert.Single(keyManager.GetKeys(KeyState.Used, false).Where(x => x.Labels == "foo label"));
			Assert.Single(keyManager.GetKeys(KeyState.Used, false).Where(x => x.Labels == "bar label"));

			// TEST MEMPOOL
			var txId5 = await rpc.SendToAddressAsync(key.GetP2wpkhAddress(network), Money.Coins(0.1m));
			await Task.Delay(1000); // Wait tx to arrive and get processed.
			Assert.NotEmpty(wallet.Coins.Where(x => x.TransactionId == txId5));
			var mempoolCoin = wallet.Coins.Single(x => x.TransactionId == txId5);
			Assert.Equal(Height.Mempool, mempoolCoin.Height);

			Interlocked.Exchange(ref setup.FiltersProcessedByWalletCount, 0);
			await rpc.GenerateAsync(1);
			await setup.WaitForFiltersToBeProcessedAsync(TimeSpan.FromSeconds(120), 1);
			var res = await rpc.GetTxOutAsync(mempoolCoin.TransactionId, (int)mempoolCoin.Index, true);
			Assert.Equal(new Height(bitcoinStore.SmartHeaderChain.TipHeight), mempoolCoin.Height);
		}
		finally
		{
			wallet.NewFiltersProcessed -= setup.Wallet_NewFiltersProcessed;
			await wallet.StopAsync(testDeadlineCts.Token);
			await synchronizer.StopAsync(testDeadlineCts.Token);
			await feeProvider.StopAsync(testDeadlineCts.Token);
			nodes?.Dispose();
			node?.Disconnect();
		}
	}
}
