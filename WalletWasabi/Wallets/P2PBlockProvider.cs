using System.Diagnostics;
using NBitcoin;
using NBitcoin.Protocol;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Extensions;
using WalletWasabi.Logging;
using WalletWasabi.Wallets.BlockProvider;

namespace WalletWasabi.Wallets;

/// <summary>
/// P2P block provider is a blocks provider getting the blocks from bitcoin nodes using the P2P bitcoin protocol.
/// </summary>
public class P2PBlockProvider : IP2PBlockProvider
{
	public P2PBlockProvider(P2PNodesManager p2PNodesManager)
	{
		P2PNodesManager = p2PNodesManager;
	}

	/// <remarks>For tests only.</remarks>
	internal P2PBlockProvider(Network network, NodesGroup nodes, bool isTorEnabled)
		: this(new P2PNodesManager(network, nodes, isTorEnabled))
	{
	}

	private P2PNodesManager P2PNodesManager { get; }

	/// <summary>
	/// Gets the given block from a single, automatically selected, P2P node using the P2P bitcoin protocol.
	/// </summary>
	/// <param name="blockHash">Block's hash to download.</param>
	/// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
	/// <returns>Requested block, or <c>null</c> if the block could not get downloaded for any reason.</returns>
	public async Task<Block?> TryGetBlockAsync(uint256 blockHash, CancellationToken cancellationToken)
	{
		P2pBlockResponse blockWithSourceData = await TryGetBlockWithSourceDataAsync(blockHash, P2pSourceRequest.Automatic, cancellationToken).ConfigureAwait(false);
		return blockWithSourceData.Block;
	}

	/// <inheritdoc/>
	public async Task<P2pBlockResponse> TryGetBlockWithSourceDataAsync(uint256 blockHash, P2pSourceRequest sourceRequest, CancellationToken cancellationToken)
	{
		var duration = Stopwatch.StartNew();

		Node? node = sourceRequest.Node;

		if (node is null)
		{
			node = await P2PNodesManager.GetBestNodeAsync(cancellationToken).ConfigureAwait(false);

			if (node is null || !node.IsConnected)
			{
				return await NotifyNodeManagerAndCreateResponseAsync(
					block: null,
					statusCode: P2pSourceDataStatusCode.NoPeerAvailable,
					duration: duration.Elapsed,
					node: null,
					connectedNodes: P2PNodesManager.ConnectedNodesCount).ConfigureAwait(false);
			}
		}

		double timeout = sourceRequest.Timeout ?? P2PNodesManager.SuggestedTimeout;

		uint connectedNodes = P2PNodesManager.ConnectedNodesCount;

		// Download block from the selected node.
		try
		{
			Block? block;

			using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout)))
			{
				using var lts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
				block = await node.DownloadBlockAsync(blockHash, lts.Token).ConfigureAwait(false);
			}

			// Validate block
			if (!block.Check())
			{
				return await NotifyNodeManagerAndCreateResponseAsync(
					block: null,
					statusCode: P2pSourceDataStatusCode.InvalidBlockProvided,
					duration: duration.Elapsed,
					node: node,
					connectedNodes: connectedNodes).ConfigureAwait(false);
			}

			return await NotifyNodeManagerAndCreateResponseAsync(
				block: block,
				statusCode: P2pSourceDataStatusCode.InvalidBlockProvided,
				duration: duration.Elapsed,
				node: node,
				connectedNodes: connectedNodes).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			if (ex is OperationCanceledException or TimeoutException)
			{
				return await NotifyNodeManagerAndCreateResponseAsync(
					block: null,
					statusCode: P2pSourceDataStatusCode.Cancelled,
					duration: duration.Elapsed,
					node: node,
					connectedNodes: connectedNodes).ConfigureAwait(false);
			}

			Logger.LogDebug(ex);
			return await NotifyNodeManagerAndCreateResponseAsync(
				block: null,
				statusCode: P2pSourceDataStatusCode.Failure,
				duration: duration.Elapsed,
				node: node,
				connectedNodes: connectedNodes).ConfigureAwait(false);
		}
	}

	private async Task<P2pBlockResponse> NotifyNodeManagerAndCreateResponseAsync(Block? block, P2pSourceDataStatusCode statusCode, TimeSpan duration, Node? node, uint connectedNodes)
	{
		var p2pSourceData = new P2pSourceData(statusCode, duration, node, connectedNodes);
		await P2PNodesManager.NotifyDownloadFinishedAsync(p2pSourceData).ConfigureAwait(false);
		return new P2pBlockResponse(Block: block, p2pSourceData);
	}
}
