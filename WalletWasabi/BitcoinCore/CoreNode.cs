using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Protocol;
using NBitcoin.RPC;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.BitcoinCore.Configuration;
using WalletWasabi.BitcoinCore.Configuration.Whitening;
using WalletWasabi.BitcoinCore.Processes;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Mempool;
using WalletWasabi.Extensions;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Userfacing;

namespace WalletWasabi.BitcoinCore;

public class CoreNode
{
	public CoreNode(string dataDir, Network network, MempoolService mempoolService, CoreConfig config, EndPoint p2pEndPoint, EndPoint rpcEndPoint, IRPCClient rpcClient)
	{
		DataDir = dataDir;
		Network = network;
		MempoolService = mempoolService;
		Config = config;
		P2pEndPoint = p2pEndPoint;
		RpcEndPoint = rpcEndPoint;
		RpcClient = rpcClient;
	}

	public EndPoint P2pEndPoint { get; }
	public EndPoint RpcEndPoint { get; }
	public IRPCClient RpcClient { get; }
	private BitcoindRpcProcessBridge Bridge { get; set; }
	public string DataDir { get; }
	public Network Network { get; }
	public MempoolService MempoolService { get; }

	public CoreConfig Config { get; }
	public P2pNode P2pNode { get; private set; }

	public static async Task<CoreNode> CreateAsync(CoreNodeParams coreNodeParams, CancellationToken cancel)
	{
		string configPath = Path.Combine(coreNodeParams.DataDir, "bitcoin.conf");
		CoreConfig coreConfig = new();

		if (File.Exists(configPath))
		{
			string configString = await File.ReadAllTextAsync(configPath, cancel).ConfigureAwait(false);
			coreConfig.AddOrUpdate(configString); // Bitcoin Core considers the last entry to be valid.
		}

		// Read Bitcoin Core config parameters.
		CoreConfigTranslator configTranslator = new(coreConfig, coreNodeParams.Network);
		string? rpcUser = configTranslator.TryGetRpcUser();
		string? rpcPassword = configTranslator.TryGetRpcPassword();
		string? rpcCookieFilePath = configTranslator.TryGetRpcCookieFile();
		string? rpcHost = configTranslator.TryGetRpcBind();
		int? rpcPort = configTranslator.TryGetRpcPort();
		WhiteBind? whiteBind = configTranslator.TryGetWhiteBind();

		string authString;
		bool cookieAuth = rpcCookieFilePath is not null;
		if (cookieAuth)
		{
			authString = $"cookiefile={rpcCookieFilePath}";
		}
		else
		{
			rpcUser ??= Encoders.Hex.EncodeData(RandomUtils.GetBytes(21));
			rpcPassword ??= Encoders.Hex.EncodeData(RandomUtils.GetBytes(21));
			authString = $"{rpcUser}:{rpcPassword}";
		}

		EndPoint p2pEndPoint = whiteBind?.EndPoint ?? coreNodeParams.P2pEndPointStrategy.EndPoint;

		if (rpcHost is null)
		{
			coreNodeParams.RpcEndPointStrategy.EndPoint.TryGetHost(out rpcHost);
		}

		if (rpcPort is null)
		{
			coreNodeParams.RpcEndPointStrategy.EndPoint.TryGetPort(out rpcPort);
		}

		if (!EndPointParser.TryParse($"{rpcHost}:{rpcPort}", coreNodeParams.Network.RPCPort, out EndPoint? rpcEndPoint))
		{
			throw new InvalidOperationException($"Failed to get RPC endpoint on {rpcHost}:{rpcPort}.");
		}

		RPCClient rpcClient = new(
			authenticationString: $"{authString}",
			rpcEndPoint.ToString(coreNodeParams.Network.DefaultPort),
			coreNodeParams.Network);
		CachedRpcClient cachedRpcClient = new(rpcClient, coreNodeParams.Cache);

		CoreNode coreNode = new(coreNodeParams.DataDir, coreNodeParams.Network, coreNodeParams.MempoolService, coreConfig, p2pEndPoint, rpcEndPoint, cachedRpcClient);

		if (coreNodeParams.TryRestart)
		{
			await coreNode.TryStopAsync(false).ConfigureAwait(false);
		}

		cancel.ThrowIfCancellationRequested();

		if (coreNodeParams.TryDeleteDataDir)
		{
			await IoHelpers.TryDeleteDirectoryAsync(coreNode.DataDir).ConfigureAwait(false);
		}

		cancel.ThrowIfCancellationRequested();

		IoHelpers.EnsureDirectoryExists(coreNode.DataDir);

		var configPrefix = NetworkTranslator.GetConfigPrefix(coreNode.Network);
		var whiteBindPermissionsPart = !string.IsNullOrWhiteSpace(whiteBind?.Permissions) ? $"{whiteBind?.Permissions}@" : "";

		if (!coreNode.RpcEndPoint.TryGetHost(out string? rpcBindParameter) || !coreNode.RpcEndPoint.TryGetPort(out int? rpcPortParameter))
		{
			throw new ArgumentException("Endpoint type is not supported.", nameof(coreNode.RpcEndPoint));
		}

		var desiredConfigLines = new List<string>()
		{
			$"{configPrefix}.server			= 1",
			$"{configPrefix}.listen			= 1",
			$"{configPrefix}.daemon			= 0", // https://github.com/zkSNACKs/WalletWasabi/issues/3588
			$"{configPrefix}.whitebind		= {whiteBindPermissionsPart}{coreNode.P2pEndPoint.ToString(coreNode.Network.DefaultPort)}",
			$"{configPrefix}.rpcbind		= {rpcBindParameter}",
			$"{configPrefix}.rpcallowip		= {IPAddress.Loopback}",
			$"{configPrefix}.rpcport		= {rpcPortParameter}",
			$"{configPrefix}.softwareexpiry	= 0",
		};

		if (!cookieAuth)
		{
			desiredConfigLines.Add($"{configPrefix}.rpcuser		= {coreNode.RpcClient.CredentialString.UserPassword.UserName}");
			desiredConfigLines.Add($"{configPrefix}.rpcpassword	= {coreNode.RpcClient.CredentialString.UserPassword.Password}");
		}

		if (coreNodeParams.TxIndex is { })
		{
			desiredConfigLines.Add($"{configPrefix}.txindex = {coreNodeParams.TxIndex}");
		}

		if (coreNodeParams.Prune is { })
		{
			desiredConfigLines.Add($"{configPrefix}.prune = {coreNodeParams.Prune}");
		}

		if (coreNodeParams.DisableWallet is { })
		{
			desiredConfigLines.Add($"{configPrefix}.disablewallet = {coreNodeParams.DisableWallet}");
		}

		if (coreNodeParams.MempoolReplacement is { })
		{
			desiredConfigLines.Add($"{configPrefix}.mempoolreplacement = {coreNodeParams.MempoolReplacement}");
		}

		if (coreNodeParams.FallbackFee is { })
		{
			desiredConfigLines.Add($"{configPrefix}.fallbackfee = {coreNodeParams.FallbackFee.ToString(fplus: false, trimExcessZero: true)}");
		}

		if (coreNodeParams.ListenOnion is { })
		{
			desiredConfigLines.Add($"{configPrefix}.listenonion = {coreNodeParams.ListenOnion}");
		}

		if (coreNodeParams.Listen is { })
		{
			desiredConfigLines.Add($"{configPrefix}.listen = {coreNodeParams.Listen}");
		}

		if (coreNodeParams.Discover is { })
		{
			desiredConfigLines.Add($"{configPrefix}.discover = {coreNodeParams.Discover}");
		}

		if (coreNodeParams.DnsSeed is { })
		{
			desiredConfigLines.Add($"{configPrefix}.dnsseed = {coreNodeParams.DnsSeed}");
		}

		if (coreNodeParams.FixedSeeds is { })
		{
			desiredConfigLines.Add($"{configPrefix}.fixedseeds = {coreNodeParams.FixedSeeds}");
		}

		if (coreNodeParams.Upnp is { })
		{
			desiredConfigLines.Add($"{configPrefix}.upnp = {coreNodeParams.Upnp}");
		}

		if (coreNodeParams.NatPmp is { })
		{
			desiredConfigLines.Add($"{configPrefix}.natpmp = {coreNodeParams.NatPmp}");
		}

		if (coreNodeParams.PersistMempool is { })
		{
			desiredConfigLines.Add($"{configPrefix}.persistmempool = {coreNodeParams.PersistMempool}");
		}

		if (coreNodeParams.RpcWorkQueue is { })
		{
			desiredConfigLines.Add($"{configPrefix}.rpcworkqueue = {coreNodeParams.RpcWorkQueue}");
		}

		if (coreNodeParams.RpcThreads is { })
		{
			desiredConfigLines.Add($"{configPrefix}.rpcthreads = {coreNodeParams.RpcThreads}");
		}

		var sectionComment = $"# The following configuration options were added or modified by Wasabi Wallet.";

		// If the comment is not already present.
		// And there would be new config entries added.
		var throwAwayConfig = new CoreConfig(coreNode.Config);
		throwAwayConfig.AddOrUpdate(string.Join(Environment.NewLine, desiredConfigLines));
		if (!coreNode.Config.ToString().Contains(sectionComment, StringComparison.Ordinal)
			&& throwAwayConfig.Count != coreNode.Config.Count)
		{
			desiredConfigLines.Insert(0, sectionComment);
		}

		if (coreNode.Config.RemoveAll("mempoolreplacement") != 0 // We remove the line, so it will use the default - that is full-RBF.
			|| coreNode.Config.AddOrUpdate(string.Join(Environment.NewLine, desiredConfigLines))
			|| !File.Exists(configPath))
		{
			IoHelpers.EnsureContainingDirectoryExists(configPath);
			await File.WriteAllTextAsync(configPath, coreNode.Config.ToString(), CancellationToken.None).ConfigureAwait(false);
		}

		// If it isn't already running, then we run it.
		if (await coreNode.RpcClient.TestAsync(cancel).ConfigureAwait(false) is null)
		{
			Logger.LogInfo("A Bitcoin node is already running.");
		}
		else
		{
			coreNode.Bridge = new BitcoindRpcProcessBridge(coreNode.RpcClient, coreNode.DataDir, printToConsole: false);
			await coreNode.Bridge.StartAsync(cancel).ConfigureAwait(false);
			Logger.LogInfo($"Started {Constants.BuiltinBitcoinNodeName}.");
		}

		coreNode.P2pNode = new P2pNode(coreNode.Network, coreNode.P2pEndPoint, coreNode.MempoolService);
		await coreNode.P2pNode.ConnectAsync(cancel).ConfigureAwait(false);

		return coreNode;
	}

	public async Task<Node> CreateNewP2pNodeAsync()
	{
		return await Node.ConnectAsync(Network, P2pEndPoint).ConfigureAwait(false);
	}

	public async Task<IEnumerable<Block>> GenerateAsync(int blockCount)
	{
		var blocks = await RpcClient.GenerateAsync(blockCount).ConfigureAwait(false);
		var rpc = RpcClient.PrepareBatch();
		var tasks = blocks.Select(b => rpc.GetBlockAsync(b));
		await rpc.SendBatchAsync().ConfigureAwait(false);
		return await Task.WhenAll(tasks).ConfigureAwait(false);
	}

	/// <summary>
	/// This method disposes resources but it does not necessarily mean that we need to stop bitcoind process
	/// because it might not have been started by us.
	/// <para>Use <see cref="TryStopAsync(bool)"/> to stop bitcoind process.</para>
	/// </summary>
	public async Task DisposeAsync()
	{
		if (P2pNode is { } p2pNode)
		{
			await p2pNode.DisposeAsync().ConfigureAwait(false);
		}
	}

	/// <param name="onlyOwned">Only stop if this node owns the process.</param>
	public async Task<bool> TryStopAsync(bool onlyOwned = true)
	{
		await DisposeAsync().ConfigureAwait(false);

		BitcoindRpcProcessBridge? bridge = null;
		if (Bridge is { })
		{
			bridge = Bridge;
		}
		else if (!onlyOwned)
		{
			bridge = new BitcoindRpcProcessBridge(RpcClient, DataDir, printToConsole: false);
		}

		if (bridge is { })
		{
			try
			{
				await bridge.StopAsync(onlyOwned).ConfigureAwait(false);
				Logger.LogInfo("Stopped.");
				return true;
			}
			catch (Exception ex)
			{
				Logger.LogInfo("Did not stop the Bitcoin node. Reason:");
				Logger.LogWarning(ex);
				return false;
			}
		}

		Logger.LogInfo("Did not stop the Bitcoin node. Reason:");
		Logger.LogInfo("The Bitcoin node was started externally.");
		return false;
	}
}
