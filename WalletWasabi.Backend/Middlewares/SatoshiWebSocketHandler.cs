using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.Backend.Models;
using WalletWasabi.Blockchain.BlockFilters;
using WalletWasabi.Logging;

namespace WalletWasabi.Backend.Middlewares;

/// <summary>
/// SatoshiWebSocketHandler is the websocket handler than provides all information from the server that
/// can be delivered to the Satoshi identity. This are:
/// * Compact filters
/// * Rounds' state updates
/// * Mining fee updates
/// </summary>
/// <remarks>
/// This websockethandler is essentially a one-way only channel (server -> client). The only exception to that
/// is the initial handshake that is started by the client sending the best known block hash. After that, all
/// messages from the client are simply ignored.
/// </remarks>
public class SatoshiWebSocketHandler : WebSocketHandlerBase, IObserver<FilterModel>
{
	private enum RequestMessage
	{
		BestKnowBlockHash
	}

	private enum ResponseMessage
	{
		Filter
	}

	private readonly IndexBuilderService _indexBuilderService;

	public SatoshiWebSocketHandler( WebSocketsConnectionTracker connectionTracker, IndexBuilderService indexBuilderService)
		: base(connectionTracker)
	{
		_indexBuilderService = indexBuilderService;
	}

	/// <summary>
	/// Receives the initial message from the client containing the bestknownblockhash required
	/// to start sending the missing filters to the client. After that it launches the process
	/// that sends the filters and other info to the client.
	/// </summary>
	/// <param name="socketState">The websocket connection state.</param>
	/// <param name="result">The reading result.</param>
	/// <param name="buffer">The buffer containing the message received from the client.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns></returns>
	public override Task ReceiveAsync(WebSocketConnectionState socketState, WebSocketReceiveResult result, byte[] buffer, CancellationToken cancellationToken)
	{
		if (!socketState.Handshaked && result.MessageType == WebSocketMessageType.Binary)
		{
			if (buffer is [(byte)RequestMessage.BestKnowBlockHash, 32, 0, .. var blockHashBytes])
			{
				var bestKnownBlockHash = new uint256(blockHashBytes[..32], false);
				socketState.Handshaked = true;
				StartSendingUpdatesAsync(socketState.WebSocket, bestKnownBlockHash, cancellationToken);
			}
		}
		return Task.CompletedTask;
	}

	private async Task StartSendingUpdatesAsync(WebSocket webSocket, uint256 bestKnownBlockHash, CancellationToken cancellationToken)
	{
		// First we send all the filters from the bestknownblockhash until the tip
		await SendMissingFiltersAsync(webSocket, bestKnownBlockHash, cancellationToken);

		_indexBuilderService.Subscribe(this);

		// Subscribe to the filters creation and send filters immediately after they are create.

		// Subscribe to changes in the rounds and send them immediately.

		// Subscribe to changes in the mining fee rates and send them immediately.

		// Am I missing something?
	}

	/// <summary>
	/// SendMissingFiltersAsync sends all the filters since bestknownblockhash to the client.
	/// </summary>
	/// <param name="webSocket">The websocket.</param>
	/// <param name="bestKnownBlockHash">The latest block id known by the client.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	private async Task SendMissingFiltersAsync(WebSocket webSocket, uint256 bestKnownBlockHash, CancellationToken cancellationToken)
	{
		var lastTransmittedFilter = bestKnownBlockHash;
		var getFiltersChunkResult =
			_indexBuilderService.GetFilterLinesExcluding(lastTransmittedFilter, 1_000, out var found); // TODO: do something if found is false

		while (getFiltersChunkResult.filters.Any())
		{
			foreach (var filter in getFiltersChunkResult.filters)
			{
				await webSocket.SendAsync(FilterToBinary(filter), cancellationToken);

				lastTransmittedFilter = filter.Header.BlockHash;
			}

			getFiltersChunkResult = _indexBuilderService.GetFilterLinesExcluding(lastTransmittedFilter, 1_000, out found);
		}
	}

	public void OnCompleted()
	{
	}

	public void OnError(Exception error)
	{
		Logger.LogError(error);
	}

	void IObserver<FilterModel>.OnNext(FilterModel filter)
	{
		SendMessageToAllAsync(FilterToBinary(filter), CancellationToken.None);
	}

	private byte[][] FilterToBinary(FilterModel filter) =>
		[
			[(byte) ResponseMessage.Filter],
			filter.Header.BlockHash.ToBytes(),
			filter.Header.PrevHash.ToBytes(),
			BitConverter.GetBytes(filter.Header.Height),
			BitConverter.GetBytes(filter.Header.EpochBlockTime),
			BitConverter.GetBytes(filter.FilterData.Length),
			filter.FilterData
		];
}
