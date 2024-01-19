using NBitcoin;
using System.IO;
using System.Net;
using System.Net.Http;
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
using WalletWasabi.WabiSabi.Backend;
using WalletWasabi.WabiSabi.Backend.Banning;
using WalletWasabi.WabiSabi.Backend.Rounds.CoinJoinStorage;
using WalletWasabi.WabiSabi.Backend.Statistics;
using WalletWasabi.WebClients.Wasabi;

namespace WalletWasabi.Backend;

public class Global : IDisposable
{
	private bool _disposedValue;

	public Global(string dataDir, IRPCClient rpcClient, Config config, IHttpClientFactory httpClientFactory)
	{
		DataDir = dataDir ?? EnvironmentHelpers.GetDataDir(Path.Combine("WalletWasabi", "Backend"));
		RpcClient = rpcClient;
		Config = config;
		HostedServices = new();
		CoinVerifierHttpClient = WasabiHttpClientFactory.CreateLongLivedHttpClient();
		HttpClientFactory = httpClientFactory;

		CoordinatorParameters = new(DataDir);
		CoinJoinIdStore = CoinJoinIdStore.Create(CoordinatorParameters.CoinJoinIdStoreFilePath);

		// We have to find it, because it's cloned by the node and not perfectly cloned (event handlers cannot be cloned.)
		P2pNode = new(config.Network, config.GetBitcoinP2pEndPoint(), new(), $"/WasabiCoordinator:{Constants.BackendMajorVersion}/");
		HostedServices.Register<BlockNotifier>(() => new BlockNotifier(TimeSpan.FromSeconds(7), rpcClient, P2pNode), "Block Notifier");

		// Initialize index building
		// Initialize index building
		var indexBuilderServiceDir = Path.Combine(DataDir, "IndexBuilderService");
		var segwitTaprootIndexFilePath = Path.Combine(indexBuilderServiceDir, $"Index{RpcClient.Network}.dat");
		var taprootIndexFilePath = Path.Combine(indexBuilderServiceDir, $"TaprootIndex{RpcClient.Network}.dat");

		SegwitTaprootIndexBuilderService = new(IndexType.SegwitTaproot, RpcClient, HostedServices.Get<BlockNotifier>(), segwitTaprootIndexFilePath);
		TaprootIndexBuilderService = new(IndexType.Taproot, RpcClient, HostedServices.Get<BlockNotifier>(), taprootIndexFilePath);

		MempoolMirror = new MempoolMirror(TimeSpan.FromSeconds(21), RpcClient, P2pNode);
		CoinJoinMempoolManager = new CoinJoinMempoolManager(CoinJoinIdStore, MempoolMirror);
	}

	public string DataDir { get; }

	public IRPCClient RpcClient { get; }

	public P2pNode P2pNode { get; }

	public HostedServices HostedServices { get; }

	public IndexBuilderService SegwitTaprootIndexBuilderService { get; }
	public IndexBuilderService TaprootIndexBuilderService { get; }

	private HttpClient CoinVerifierHttpClient { get; }
	private IHttpClientFactory HttpClientFactory { get; }

	private CoinVerifierApiClient? CoinVerifierApiClient { get; set; }
	public CoinVerifier? CoinVerifier { get; private set; }

	public Config Config { get; }

	private CoordinatorParameters CoordinatorParameters { get; }

	public CoinJoinIdStore CoinJoinIdStore { get; }
	public WabiSabiCoordinator? WabiSabiCoordinator { get; private set; }
	private Whitelist? WhiteList { get; set; }
	public MempoolMirror MempoolMirror { get; }
	public CoinJoinMempoolManager CoinJoinMempoolManager { get; private set; }

	public async Task InitializeAsync(CancellationToken cancel)
	{
		// Make sure RPC works.
		await AssertRpcNodeFullyInitializedAsync(cancel);

		// Make sure P2P works.
		await P2pNode.ConnectAsync(cancel).ConfigureAwait(false);

		HostedServices.Register<MempoolMirror>(() => MempoolMirror, "Full Node Mempool Mirror");

		var blockNotifier = HostedServices.Get<BlockNotifier>();

		var wabiSabiConfig = CoordinatorParameters.RuntimeCoordinatorConfig;
		bool coinVerifierEnabled = wabiSabiConfig.IsCoinVerifierEnabled;

		if (coinVerifierEnabled)
		{
			try
			{
				if (!Uri.TryCreate(wabiSabiConfig.CoinVerifierApiUrl, UriKind.RelativeOrAbsolute, out Uri? url))
				{
					throw new ArgumentException($"Blacklist API URL is invalid in {nameof(WabiSabiConfig)}.");
				}
				if (string.IsNullOrEmpty(wabiSabiConfig.CoinVerifierApiAuthToken))
				{
					throw new ArgumentException($"Blacklist API token was not provided in {nameof(WabiSabiConfig)}.");
				}
				if (wabiSabiConfig.RiskFlags is null)
				{
					throw new ArgumentException($"Risk indicators were not provided in {nameof(WabiSabiConfig)}.");
				}

				CoinVerifierHttpClient.BaseAddress = url;
				CoinVerifierHttpClient.Timeout = CoinVerifierApiClient.ApiRequestTimeout;

				WhiteList = await Whitelist.CreateAndLoadFromFileAsync(CoordinatorParameters.WhitelistFilePath, wabiSabiConfig, cancel).ConfigureAwait(false);
				CoinVerifierApiClient = new CoinVerifierApiClient(CoordinatorParameters.RuntimeCoordinatorConfig.CoinVerifierApiAuthToken, CoinVerifierHttpClient);
				CoinVerifier = new(CoinJoinIdStore, CoinVerifierApiClient, WhiteList, CoordinatorParameters.RuntimeCoordinatorConfig, auditsDirectoryPath: Path.Combine(CoordinatorParameters.CoordinatorDataDir, "CoinVerifierAudits"));

				Logger.LogInfo("CoinVerifier created successfully.");
			}
			catch (Exception exc)
			{
				throw new InvalidOperationException($"There was an error when creating {nameof(CoinVerifier)}. Details: '{exc}'", exc);
			}
		}

		var coinJoinScriptStore = CoinJoinScriptStore.LoadFromFile(CoordinatorParameters.CoinJoinScriptStoreFilePath);

		WabiSabiCoordinator = new WabiSabiCoordinator(CoordinatorParameters, RpcClient, CoinJoinIdStore, coinJoinScriptStore, HttpClientFactory, wabiSabiConfig.IsCoinVerifierEnabled ? CoinVerifier : null);
		blockNotifier.OnBlock += WabiSabiCoordinator.BanDescendant;
		HostedServices.Register<WabiSabiCoordinator>(() => WabiSabiCoordinator, "WabiSabi Coordinator");
		P2pNode.OnTransactionArrived += WabiSabiCoordinator.BanDoubleSpenders;

		await HostedServices.StartAllAsync(cancel);

		SegwitTaprootIndexBuilderService.Synchronize();
		Logger.LogInfo($"{nameof(SegwitTaprootIndexBuilderService)} is successfully initialized and started synchronization.");
		TaprootIndexBuilderService.Synchronize();
		Logger.LogInfo($"{nameof(TaprootIndexBuilderService)} is successfully initialized and started synchronization.");
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

				CoinVerifierHttpClient.Dispose();
				CoinJoinMempoolManager.Dispose();

				var stoppingTask = Task.Run(DisposeAsync);

				stoppingTask.GetAwaiter().GetResult();
			}

			_disposedValue = true;
		}
	}

	private async Task DisposeAsync()
	{
		await SegwitTaprootIndexBuilderService.StopAsync().ConfigureAwait(false);
		Logger.LogInfo($"{nameof(SegwitTaprootIndexBuilderService)} is stopped.");

		await TaprootIndexBuilderService.StopAsync().ConfigureAwait(false);
		Logger.LogInfo($"{nameof(TaprootIndexBuilderService)} is stopped.");

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(21));
		await HostedServices.StopAllAsync(cts.Token).ConfigureAwait(false);
		HostedServices.Dispose();

		await P2pNode.DisposeAsync().ConfigureAwait(false);
		Logger.LogInfo($"{nameof(P2pNode)} is disposed.");

		if (WhiteList is { } whiteList)
		{
			if (await whiteList.WriteToFileIfChangedAsync().ConfigureAwait(false))
			{
				Logger.LogInfo($"{nameof(WhiteList)} is saved to file.");
			}
		}

		if (CoinVerifier is { } coinVerifier)
		{
			await coinVerifier.DisposeAsync().ConfigureAwait(false);
		}

		if (CoinVerifierApiClient is { } coinVerifierApiClient)
		{
			await coinVerifierApiClient.DisposeAsync().ConfigureAwait(false);
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
