using NBitcoin;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.BitcoinCore;
using WalletWasabi.BitcoinCore.Mempool;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.BlockFilters;
using WalletWasabi.Blockchain.Blocks;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Services;
using WalletWasabi.WabiSabi;
using WalletWasabi.WabiSabi.Backend.Statistics;

namespace WalletWasabi.Backend;

public class Global : IDisposable
{
	private bool _disposedValue;

	public Global(string dataDir, IRPCClient rpcClient, Config config)
	{
		DataDir = dataDir ?? EnvironmentHelpers.GetDataDir(Path.Combine("WalletWasabi", "Backend"));
		RpcClient = rpcClient;
		Config = config;
		HostedServices = new();

		CoordinatorParameters = new(DataDir);

		// We have to find it, because it's cloned by the node and not perfectly cloned (event handlers cannot be cloned.)
		P2pNode = new(config.Network, config.GetBitcoinP2pEndPoint(), new());
		HostedServices.Register<BlockNotifier>(() => new BlockNotifier(rpcClient, P2pNode), "Block Notifier");

		// Initialize index building
		var indexBuilderServiceDir = Path.Combine(DataDir, "IndexBuilderService");
		var indexFilePath = Path.Combine(indexBuilderServiceDir, $"Index{RpcClient.Network}.dat");
		IndexBuilderService = new(RpcClient, HostedServices.Get<BlockNotifier>());

		MempoolMirror = new MempoolMirror(RpcClient, P2pNode);
	}

	public string DataDir { get; }

	public IRPCClient RpcClient { get; }

	public P2pNode P2pNode { get; }

	public HostedServices HostedServices { get; }

	public IndexBuilderService IndexBuilderService { get; }

	public Config Config { get; }

	private CoordinatorParameters CoordinatorParameters { get; }

	public WabiSabiCoordinator? WabiSabiCoordinator { get; private set; }
	public MempoolMirror MempoolMirror { get; }

	public async Task InitializeAsync(CancellationToken cancel)
	{
		// Make sure RPC works.
		await AssertRpcNodeFullyInitializedAsync(cancel);

		// Make sure P2P works.
		await P2pNode.ConnectAsync(cancel).ConfigureAwait(false);

		HostedServices.Register<MempoolMirror>(() => MempoolMirror, "Full Node Mempool Mirror");

		var blockNotifier = HostedServices.Get<BlockNotifier>();

		var coinJoinScriptStore = CoinJoinScriptStore.LoadFromFile(CoordinatorParameters.CoinJoinScriptStoreFilePath);

		WabiSabiCoordinator = new WabiSabiCoordinator(CoordinatorParameters, RpcClient, coinJoinScriptStore);
		blockNotifier.OnBlock += WabiSabiCoordinator.BanDescendant;
		HostedServices.Register<WabiSabiCoordinator>(() => WabiSabiCoordinator, "WabiSabi Coordinator");
		P2pNode.OnTransactionArrived += WabiSabiCoordinator.BanDoubleSpenders;

		await HostedServices.StartAllAsync(cancel);

		IndexBuilderService.Synchronize();
		Logger.LogInfo($"{nameof(IndexBuilderService)} is successfully initialized and started synchronization.");
	}

	private async Task AssertRpcNodeFullyInitializedAsync(CancellationToken cancellationToken)
	{
		var rpcClient = Guard.NotNull(nameof(RpcClient), RpcClient);
		var config = Guard.NotNull(nameof(Config), Config);

		try
		{
			var blockchainInfo = await rpcClient.GetBlockchainInfoAsync(cancellationToken);

			var blocks = blockchainInfo.Blocks;
			if (blocks == 0 && config.Network != Network.RegTest)
			{
				throw new NotSupportedException($"{nameof(blocks)} == 0");
			}

			var headers = blockchainInfo.Headers;
			if (headers == 0 && config.Network != Network.RegTest)
			{
				throw new NotSupportedException($"{nameof(headers)} == 0");
			}

			if (blocks != headers)
			{
				throw new NotSupportedException($"{Constants.BuiltinBitcoinNodeName} is not fully synchronized.");
			}

			Logger.LogInfo($"{Constants.BuiltinBitcoinNodeName} is fully synchronized.");

			if (config.Network == Network.RegTest) // Make sure there's at least 101 block, if not generate it
			{
				if (blocks < 101)
				{
					var generateBlocksResponse = await rpcClient.GenerateAsync(101, cancellationToken)
						?? throw new NotSupportedException($"{Constants.BuiltinBitcoinNodeName} cannot generate blocks on the {Network.RegTest}.");
					blockchainInfo = await rpcClient.GetBlockchainInfoAsync(cancellationToken);
					blocks = blockchainInfo.Blocks;
					if (blocks == 0)
					{
						throw new NotSupportedException($"{nameof(blocks)} == 0");
					}
					Logger.LogInfo($"Generated 101 block on {Network.RegTest}. Number of blocks {blocks}.");
				}
			}
		}
		catch (WebException)
		{
			Logger.LogError($"{Constants.BuiltinBitcoinNodeName} is not running, or incorrect RPC credentials, or network is given in the config file: `{config.FilePath}`.");
			throw;
		}
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_disposedValue)
		{
			if (disposing)
			{
				if (WabiSabiCoordinator is { } wabiSabiCoordinator)
				{
					var blockNotifier = HostedServices.Get<BlockNotifier>();
					blockNotifier.OnBlock -= wabiSabiCoordinator.BanDescendant;
					P2pNode.OnTransactionArrived -= wabiSabiCoordinator.BanDoubleSpenders;
				}

				var stoppingTask = Task.Run(DisposeAsync);

				stoppingTask.GetAwaiter().GetResult();
			}

			_disposedValue = true;
		}
	}

	private async Task DisposeAsync()
	{
		await IndexBuilderService.StopAsync().ConfigureAwait(false);
		Logger.LogInfo($"{nameof(IndexBuilderService)} is stopped.");

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(21));
		await HostedServices.StopAllAsync(cts.Token).ConfigureAwait(false);
		HostedServices.Dispose();

		await P2pNode.DisposeAsync().ConfigureAwait(false);
		Logger.LogInfo($"{nameof(P2pNode)} is disposed.");
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
