using NBitcoin;
using NBitcoin.Protocol;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.BitcoinCore.Monitoring;
using WalletWasabi.BitcoinP2p;
using WalletWasabi.Blockchain.Mempool;
using WalletWasabi.Extensions;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;

namespace WalletWasabi.BitcoinCore;

public class P2pNode
{
	private bool _disposed = false;

	public P2pNode(Network network, EndPoint endPoint, MempoolService mempoolService)
	{
		Network = Guard.NotNull(nameof(network), network);
		EndPoint = Guard.NotNull(nameof(endPoint), endPoint);
		MempoolService = Guard.NotNull(nameof(mempoolService), mempoolService);

		Stop = new CancellationTokenSource();
		NodeEventsSubscribed = false;
		SubscriptionLock = new object();
		P2pReconnector = new P2pReconnector(TimeSpan.FromSeconds(7), this);
	}

	public event EventHandler<uint256>? BlockInv;

	public event EventHandler<Transaction>? OnTransactionArrived;

	private Node? Node { get; set; }
	private TrustedP2pBehavior? TrustedP2pBehavior { get; set; }
	private Network Network { get; }
	private EndPoint EndPoint { get; }
	public MempoolService MempoolService { get; }

	private bool NodeEventsSubscribed { get; set; }
	private object SubscriptionLock { get; }
	private CancellationTokenSource Stop { get; }
	private P2pReconnector P2pReconnector { get; set; }

	public async Task ConnectAsync(CancellationToken cancel)
	{
		using var handshakeTimeout = new CancellationTokenSource();
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(handshakeTimeout.Token, cancel, Stop.Token);
		handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(21));
		var parameters = new NodeConnectionParameters()
		{
			ConnectCancellation = linked.Token,
			IsRelay = true
		};

		parameters.TemplateBehaviors.Add(new TrustedP2pBehavior(MempoolService));

		Node = await Node.ConnectAsync(Network, EndPoint, parameters).ConfigureAwait(false);
		Node.VersionHandshake(cancel);

		if (!Node.PeerVersion.Services.HasFlag(NodeServices.Network))
		{
			throw new InvalidOperationException("Wasabi cannot use the local node because it does not provide blocks.");
		}

		if (!Node.IsConnected)
		{
			throw new InvalidOperationException(
				$"Could not complete the handshake with the local node and dropped the connection.{Environment.NewLine}" +
				"Probably this is because the node does not support retrieving full blocks or segwit serialization.");
		}

		lock (SubscriptionLock)
		{
			TrustedP2pBehavior = Node.Behaviors.Find<TrustedP2pBehavior>();
			Node.UncaughtException += Node_UncaughtException;
			Node.StateChanged += P2pNode_StateChanged;
			Node.Disconnected += Node_DisconnectedAsync;
			TrustedP2pBehavior.BlockInv += TrustedP2pBehavior_BlockInv;
			TrustedP2pBehavior.OnTransactionArrived += TrustedP2pBehavior_OnTransactionArrived;
			NodeEventsSubscribed = true;
			MempoolService.TrustedNodeMode = Node.IsConnected;
		}
	}

	private void Node_UncaughtException(Node sender, Exception ex)
	{
		Logger.LogInfo($"Node {sender.Peer.Endpoint} failed with exception: {ex}");
	}

	private async void Node_DisconnectedAsync(Node node)
	{
		try
		{
			await P2pReconnector.StartAndAwaitReconnectionAsync(Stop.Token).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Logger.LogDebug(ex);
		}
	}

	private void TrustedP2pBehavior_BlockInv(object? sender, uint256 e)
	{
		BlockInv?.Invoke(this, e);
	}

	private void TrustedP2pBehavior_OnTransactionArrived(object? sender, Transaction tx)
	{
		OnTransactionArrived.SafeInvoke(this, tx);
	}

	private void P2pNode_StateChanged(Node node, NodeState oldState)
	{
		var isConnected = node.IsConnected;
		var trustedNodeMode = MempoolService.TrustedNodeMode;
		if (trustedNodeMode != isConnected)
		{
			MempoolService.TrustedNodeMode = isConnected;
			Logger.LogInfo($"CoreNode connection state changed. Triggered {nameof(MempoolService)}.{nameof(MempoolService.TrustedNodeMode)} to be {MempoolService.TrustedNodeMode}");
		}
	}

	public async Task DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;

		Stop.Cancel();

		await P2pReconnector.StopAsync(CancellationToken.None).ConfigureAwait(false);
		P2pReconnector.Dispose();

		await TryDisconnectAsync(CancellationToken.None).ConfigureAwait(false);
		Stop.Dispose();
	}

	/// <summary>
	/// It is not equivalent to Dispose, but it is being called from Dispose.
	/// </summary>
	public async Task<bool> TryDisconnectAsync(CancellationToken cancel)
	{
		var node = Node;
		if (node is null)
		{
			return true;
		}

		try
		{
			lock (SubscriptionLock)
			{
				MempoolService.TrustedNodeMode = false;
				if (NodeEventsSubscribed)
				{
					if (TrustedP2pBehavior is { } trustedP2pBehavior)
					{
						trustedP2pBehavior.BlockInv -= TrustedP2pBehavior_BlockInv;
						trustedP2pBehavior.OnTransactionArrived -= TrustedP2pBehavior_OnTransactionArrived;
					}
					node.Disconnected -= Node_DisconnectedAsync;
					node.StateChanged -= P2pNode_StateChanged;
					node.UncaughtException -= Node_UncaughtException;
					NodeEventsSubscribed = false;
				}
			}

			await DisconnectAsync(node, cancel).ConfigureAwait(false);

			Logger.LogInfo("P2p Bitcoin node is disconnected.");
			return true;
		}
		catch (Exception ex)
		{
			Logger.LogError($"P2p Bitcoin node failed to disconnect. '{ex}'");
			return false;
		}
		finally
		{
			Node = null;
		}
	}

	private static async Task DisconnectAsync(Node node, CancellationToken cancel)
	{
		TaskCompletionSource<bool> tcs = new();
		node.Disconnected += Node_Disconnected;
		try
		{
			if (!node.IsConnected)
			{
				return;
			}

			// Disconnection not waited here.
			node.DisconnectAsync();
			await tcs.Task.WaitAsync(cancel).ConfigureAwait(false);
		}
		finally
		{
			node.Disconnected -= Node_Disconnected;
		}

		void Node_Disconnected(Node node)
		{
			tcs.TrySetResult(true);
		}
	}
}
