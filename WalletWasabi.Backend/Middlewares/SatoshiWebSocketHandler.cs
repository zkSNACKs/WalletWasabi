using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.Backend.Models;
using WalletWasabi.Blockchain.BlockFilters;
using WalletWasabi.Extensions;
using WalletWasabi.Logging;
using WalletWasabi.Synchronizarion;

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
	private readonly IndexBuilderService _indexBuilderService;

	public SatoshiWebSocketHandler(WebSocketsConnectionTracker connectionTracker, IndexBuilderService indexBuilderService)
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
	public override async Task ReceiveAsync(WebSocketConnectionState socketState, WebSocketReceiveResult result, byte[] buffer, CancellationToken cancellationToken)
	{
		if (!socketState.Handshaked && result.MessageType == WebSocketMessageType.Binary)
		{
			switch ((RequestMessage)buffer[0])
			{
				case RequestMessage.BestKnowBlockHash:
					try
					{
						using var reader = new BinaryReader(new MemoryStream(buffer[1..]));
						var bestKnownBlockHash = reader.ReadUInt256();
						socketState.Handshaked = true;
						await StartSendingUpdatesAsync(socketState.WebSocket, bestKnownBlockHash, cancellationToken);
					}
					catch (Exception e) when (e is FormatException or InvalidOperationException)
					{
						await SendHandshakeError(socketState.WebSocket, cancellationToken);
					}
					break;
				default:
					await SendHandshakeError(socketState.WebSocket, cancellationToken);
					break;
			}
		}
	}

	private static Task SendHandshakeError(WebSocket webSocket, CancellationToken cancellationToken) =>
		 webSocket.SendAsync(new[] { (byte)ResponseMessage.HandshakeError }, WebSocketMessageType.Binary, true, cancellationToken);

	private async Task StartSendingUpdatesAsync(WebSocket webSocket, uint256 bestKnownBlockHash, CancellationToken cancellationToken)
	{
		// First we send all the filters from the bestknownblockhash until the tip
		await SendMissingFiltersAsync(webSocket, bestKnownBlockHash, cancellationToken);

		_indexBuilderService.Subscribe(this);

		// Subscribe to the filters creation and send filters immediately after they are create.

		// Subscribe to changes in the rounds and send them immediately.

		// Subscribe to changes in the mining fee rates and send them immediately.

		// Subscribe to changes in the exchange rate rates and send them immediately.

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
		var getFiltersChunk = GetFiltersBucketStartingFrom(lastTransmittedFilter);

		while (getFiltersChunk.Any())
		{
			foreach (var filter in getFiltersChunk)
			{
				var message = new FilterMessage(filter);
				await webSocket.SendAsync(message.ToByteArray(), WebSocketMessageType.Binary, true, cancellationToken);

				lastTransmittedFilter = filter.Header.BlockHash;
			}

			getFiltersChunk = GetFiltersBucketStartingFrom(lastTransmittedFilter);
		}
	}

	private IEnumerable<FilterModel> GetFiltersBucketStartingFrom(uint256 startingBlockHash)
	{
		var (_, filters) = _indexBuilderService.GetFilterLinesExcluding(startingBlockHash, 1_000, out var found);
		if (!found)
		{
			throw new InvalidOperationException($"Filter {startingBlockHash} not found");
		}
		return filters;
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
		var message = new FilterMessage(filter);
		SendMessageToAllAsync(message.ToByteArray(), CancellationToken.None);
	}
}
