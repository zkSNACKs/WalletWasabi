using NBitcoin;
using NBitcoin.Protocol;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using WabiSabi.Crypto.Randomness;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Extensions;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Stores;
using WalletWasabi.Tor.Http;
using WalletWasabi.Wallets;
using WalletWasabi.WebClients.Wasabi;

namespace WalletWasabi.Blockchain.TransactionBroadcasting;

public class TransactionBroadcaster
{
	public TransactionBroadcaster(Network network, BitcoinStore bitcoinStore, WalletManager walletManager)
	{
		Network = network;
		BitcoinStore = bitcoinStore;
		WalletManager = walletManager;
	}

	private BitcoinStore BitcoinStore { get; }
	private Network Network { get; }
	private NodesGroup? Nodes { get; set; }
	private IRPCClient? RpcClient { get; set; }
	private WalletManager WalletManager { get; }
	private WasabiRandom Random { get; } = SecureRandom.Instance;

	public void Initialize(NodesGroup nodes, IRPCClient? rpcClient)
	{
		Nodes = nodes;
		RpcClient = rpcClient;
	}

	private async Task BroadcastTransactionToNetworkNodeAsync(SmartTransaction transaction, Node node)
	{
		Logger.LogInfo($"Trying to broadcast transaction with random node ({node.RemoteSocketAddress}):{transaction.GetHash()}.");
		if (!BitcoinStore.MempoolService.TryAddToBroadcastStore(transaction, node.RemoteSocketEndpoint.ToString())) // So we'll reply to INV with this transaction.
		{
			Logger.LogWarning($"Transaction {transaction.GetHash()} was already present in the broadcast store.");
		}
		var invPayload = new InvPayload(transaction.Transaction);

		// Give 7 seconds to send the inv payload.
		await node.SendMessageAsync(invPayload).WaitAsync(TimeSpan.FromSeconds(7)).ConfigureAwait(false); // ToDo: It's dangerous way to cancel. Implement proper cancellation to NBitcoin!

		if (BitcoinStore.MempoolService.TryGetFromBroadcastStore(transaction.GetHash(), out TransactionBroadcastEntry? entry))
		{
			// Give 7 seconds for serving.
			var timeout = 0;
			while (!entry.IsBroadcasted())
			{
				if (timeout > 7)
				{
					throw new TimeoutException("Did not serve the transaction.");
				}
				await Task.Delay(1_000).ConfigureAwait(false);
				timeout++;
			}
			node.DisconnectAsync("Thank you!");
			Logger.LogInfo($"Disconnected node: {node.RemoteSocketAddress}. Successfully broadcasted transaction: {transaction.GetHash()}.");

			// Give 21 seconds for propagation.
			timeout = 0;
			while (entry.GetPropagationConfirmations() < 2)
			{
				if (timeout > 21)
				{
					throw new TimeoutException("Did not serve the transaction.");
				}
				await Task.Delay(1_000).ConfigureAwait(false);
				timeout++;
			}
			Logger.LogInfo($"Transaction is successfully propagated: {transaction.GetHash()}.");
		}
		else
		{
			Logger.LogWarning($"Expected transaction {transaction.GetHash()} was not found in the broadcast store.");
		}
	}

	private void BelieveTransaction(SmartTransaction transaction)
	{
		if (transaction.Height == Height.Unknown)
		{
			transaction.SetUnconfirmed();
		}

		BitcoinStore.MempoolService.TryAddToBroadcastStore(transaction, "N/A");

		WalletManager.Process(transaction);
	}

	public async Task SendTransactionAsync(SmartTransaction transaction)
	{
		try
		{
			// Broadcast to a random node.
			// Wait until it arrives to at least two other nodes.
			// If something's wrong, fall back broadcasting with rpc.

			if (Nodes is null)
			{
				throw new InvalidOperationException($"Nodes are not yet initialized.");
			}

			Node? node = Nodes.ConnectedNodes.RandomElement(Random);

			var minimumRequiredNodeCount = Network == Network.RegTest ? 1 : 5;

			while (node is null || !node.IsConnected || Nodes.ConnectedNodes.Count < minimumRequiredNodeCount)
			{
				// As long as we are connected to at least 4 nodes, we can always try again.
				// 3 should be enough, but make it 5 so 2 nodes could disconnect in the meantime.

				await Task.Delay(100).ConfigureAwait(false);
				node = Nodes.ConnectedNodes.RandomElement(Random);
			}
			await BroadcastTransactionToNetworkNodeAsync(transaction, node).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Logger.LogInfo($"Random node could not broadcast transaction. Reason: {ex.Message}.");
			Logger.LogDebug(ex);

			if (RpcClient is { })
			{
				await BroadcastTransactionWithRpcAsync(transaction).ConfigureAwait(false);
			}
		}
	}

	private async Task BroadcastTransactionWithRpcAsync(SmartTransaction transaction)
	{
		if (RpcClient is null)
		{
			throw new InvalidOperationException("Trying to broadcast on RPC but it is not initialized.");
		}

		await RpcClient.SendRawTransactionAsync(transaction.Transaction).ConfigureAwait(false);
		BelieveTransaction(transaction);
		Logger.LogInfo($"Transaction is successfully broadcasted with RPC: {transaction.GetHash()}.");
	}
}
