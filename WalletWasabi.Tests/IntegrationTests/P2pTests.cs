using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.Protocol.Behaviors;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Blockchain.Blocks;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.Mempool;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Exceptions;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Services;
using WalletWasabi.Stores;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.Wallets;
using WalletWasabi.Wallets.BlockProvider;
using WalletWasabi.Wallets.FilterProcessor;
using WalletWasabi.WebClients.Wasabi;
using Xunit;

namespace WalletWasabi.Tests.IntegrationTests;

public class P2pTests
{
	[Theory]
	[InlineData("test")]
	[InlineData("main")]
	public async Task TestServicesAsync(string networkString)
	{
		await RuntimeParams.LoadAsync();
		var network = Network.GetNetwork(networkString);
		var blocksToDownload = new List<uint256>();

		if (network == Network.Main)
		{
			blocksToDownload.Add(new uint256("00000000000000000037c2de35bd85f3e57f14ddd741ce6cee5b28e51473d5d0"));
			blocksToDownload.Add(new uint256("000000000000000000115315a43cb0cdfc4ea54a0e92bed127f4e395e718d8f9"));
			blocksToDownload.Add(new uint256("00000000000000000011b5b042ad0522b69aae36f7de796f563c895714bbd629"));
		}
		else if (network == Network.TestNet)
		{
			blocksToDownload.Add(new uint256("0000000097a664c4084b49faa6fd4417055cb8e5aac480abc31ddc57a8208524"));
			blocksToDownload.Add(new uint256("000000009ed5b82259ecd2aa4cd1f119db8da7a70e7ea78d9c9f603e01f93bcc"));
			blocksToDownload.Add(new uint256("00000000e6da8c2da304e9f5ad99c079df2c3803b49efded3061ecaf206ddc66"));
		}
		else
		{
			throw new NotSupportedNetworkException(network);
		}

		var dataDir = Common.GetWorkDir();

		SmartHeaderChain smartHeaderChain = new();
		await using var indexStore = new IndexStore(Path.Combine(dataDir, "indexStore"), network, smartHeaderChain);
		await using var transactionStore = new AllTransactionStore(Path.Combine(dataDir, "transactionStore"), network);
		var mempoolService = new MempoolService();
		var blocks = new FileSystemBlockRepository(Path.Combine(dataDir, "blocks"), network);
		BitcoinStore bitcoinStore = new(indexStore, transactionStore, mempoolService, smartHeaderChain, blocks);
		await bitcoinStore.InitializeAsync();

		var addressManagerFolderPath = Path.Combine(dataDir, "AddressManager");
		var addressManagerFilePath = Path.Combine(addressManagerFolderPath, $"AddressManager{network}.dat");
		var connectionParameters = new NodeConnectionParameters();
		AddressManager addressManager;

		try
		{
			addressManager = await NBitcoinHelpers.LoadAddressManagerFromPeerFileAsync(addressManagerFilePath);
			Logger.LogInfo($"Loaded {nameof(AddressManager)} from `{addressManagerFilePath}`.");
		}
		catch (DirectoryNotFoundException)
		{
			addressManager = new AddressManager();
		}
		catch (FileNotFoundException)
		{
			addressManager = new AddressManager();
		}
		catch (OverflowException)
		{
			File.Delete(addressManagerFilePath);
			addressManager = new AddressManager();
		}
		catch (FormatException)
		{
			File.Delete(addressManagerFilePath);
			addressManager = new AddressManager();
		}

		connectionParameters.TemplateBehaviors.Add(new AddressManagerBehavior(addressManager));
		connectionParameters.TemplateBehaviors.Add(bitcoinStore.CreateUntrustedP2pBehavior());

		using var nodes = new NodesGroup(network, connectionParameters, requirements: Constants.NodeRequirements);

		KeyManager keyManager = KeyManager.CreateNew(out _, "password", network);
		await using WasabiHttpClientFactory httpClientFactory = new(Common.TorSocks5Endpoint, backendUriGetter: () => new Uri("http://localhost:12345"));
		using WasabiSynchronizer synchronizer = new(period: TimeSpan.FromSeconds(3), 10000, bitcoinStore, httpClientFactory);
		var feeProvider = new HybridFeeProvider(synchronizer, null);

		ServiceConfiguration serviceConfig = new(new IPEndPoint(IPAddress.Loopback, network.DefaultPort), Money.Coins(Constants.DefaultDustThreshold));
		using MemoryCache cache = new(new MemoryCacheOptions
		{
			SizeLimit = 1_000,
			ExpirationScanFrequency = TimeSpan.FromSeconds(30)
		});

		IFileSystemBlockRepository blockRepository = bitcoinStore.BlockRepository;
		await using SpecificNodeBlockProvider specificNodeBlockProvider = new(network, serviceConfig, httpClientFactory.TorEndpoint);

		using BlockDownloadService blockDownloadService = new(
			bitcoinStore.BlockRepository,
			[specificNodeBlockProvider],
			new P2PBlockProvider(network, nodes, httpClientFactory.IsTorEnabled));

		using UnconfirmedTransactionChainProvider unconfirmedChainProvider = new(httpClientFactory);

		ServiceConfiguration serviceConfiguration = new(new IPEndPoint(IPAddress.Loopback, network.DefaultPort), Money.Coins(Constants.DefaultDustThreshold));
		WalletFactory walletFactory = new(dataDir, network, bitcoinStore, synchronizer, serviceConfiguration, feeProvider, blockDownloadService, unconfirmedChainProvider);
		using Wallet wallet = walletFactory.CreateAndInitialize(keyManager);

		Assert.True(Directory.Exists(blocks.BlocksFolderPath));

		try
		{
			var mempoolTransactionAwaiter = new EventsAwaiter<SmartTransaction>(
				h => bitcoinStore.MempoolService.TransactionReceived += h,
				h => bitcoinStore.MempoolService.TransactionReceived -= h,
				3);

			var nodeConnectionAwaiter = new EventsAwaiter<NodeEventArgs>(
				h => nodes.ConnectedNodes.Added += h,
				h => nodes.ConnectedNodes.Added -= h,
				3);

			nodes.Connect();

			var downloadTasks = new List<Task<BlockDownloadService.IResult>>();
			using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
			foreach (var hash in blocksToDownload)
			{
				downloadTasks.Add(blockDownloadService.TryGetBlockAsync(P2pSourceRequest.Automatic, hash, new Priority(SyncType.Complete), cts.Token));
			}

			await nodeConnectionAwaiter.WaitAsync(TimeSpan.FromMinutes(3));

			var i = 0;
			var hashArray = blocksToDownload.ToArray();
			foreach (var block in await Task.WhenAll(downloadTasks))
			{
				Assert.True(File.Exists(Path.Combine(blocks.BlocksFolderPath, hashArray[i].ToString())));
				i++;
			}

			await mempoolTransactionAwaiter.WaitAsync(TimeSpan.FromMinutes(3));
		}
		finally
		{
			// So next test will download the block.
			foreach (var hash in blocksToDownload)
			{
				await blockRepository.RemoveAsync(hash, CancellationToken.None);
			}

			if (wallet is { })
			{
				await wallet.StopAsync(CancellationToken.None);
			}

			if (Directory.Exists(blocks.BlocksFolderPath))
			{
				Directory.Delete(blocks.BlocksFolderPath, recursive: true);
			}

			IoHelpers.EnsureContainingDirectoryExists(addressManagerFilePath);
			addressManager?.SavePeerFile(addressManagerFilePath, network);
			Logger.LogInfo($"Saved {nameof(AddressManager)} to `{addressManagerFilePath}`.");
			cache.Dispose();
		}
	}
}
