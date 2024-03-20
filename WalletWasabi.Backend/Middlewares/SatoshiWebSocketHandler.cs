using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.Backend.Models;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Blockchain.BlockFilters;
using WalletWasabi.Extensions;
using WalletWasabi.Logging;
using WalletWasabi.Services;
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
public class SatoshiWebSocketHandler : WebSocketHandlerBase
{
	private readonly IndexBuilderService _indexBuilderService;
	private readonly EventBus _eventBus;
	private readonly Dictionary<WebSocket, List<IDisposable>> _socketResources = new();
	private readonly object _synObj = new();

	public SatoshiWebSocketHandler(
		WebSocketsConnectionTracker connectionTracker,
		EventBus eventBus,
		IndexBuilderService indexBuilderService)
		: base(connectionTracker)
	{
		_eventBus = eventBus;
		_indexBuilderService = indexBuilderService;
	}

	public override Task OnConnectedAsync(WebSocket socket, CancellationToken cancellationToken)
	{
		// Subscribe to changes in the exchange rate rates and send them immediately.
		Subscribe(socket, NotifyExchangeRate);

		// Subscribe to changes in the mining fee rates and send them immediately.
		Subscribe(socket, NotifyFeeEstimations);

		// Subscribe to changes in the rounds and send them immediately.
		// _eventBus.Subscribe();

		return base.OnConnectedAsync(socket, cancellationToken);
	}

	private void Subscribe<T>(WebSocket socket, Func<WebSocket, Action<T>> builder) where T : notnull
	{
		lock (_synObj)
		{
			var notification = _eventBus.Subscribe(builder(socket));
			var resources = _socketResources.TryGetValue(socket, out var r) ? r : [];
			resources.Add(notification);
			_socketResources[socket] = resources;
		}
	}

	public override Task OnDisconnectedAsync(WebSocket socket, CancellationToken cancellationToken)
	{
		lock (_synObj)
		{
			foreach (var disposable in _socketResources[socket])
			{
				disposable.Dispose();
			}
		}
		return base.OnDisconnectedAsync(socket, cancellationToken);
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
	public override async Task ReceiveAsync(
		WebSocketConnectionState socketState,
		WebSocketReceiveResult result,
		byte[] buffer,
		CancellationToken cancellationToken)
	{
		if (!socketState.Handshaked && result.MessageType == WebSocketMessageType.Binary)
		{
			switch ((RequestMessage) buffer[0])
			{
				case RequestMessage.BestKnowBlockHash:
					try
					{
						using var reader = new BinaryReader(new MemoryStream(buffer[1..]));
						var bestKnownBlockHash = reader.ReadUInt256();
						socketState.Handshaked = true;

						// Send the best block height
						await SendBlockHeightAsync(socketState.WebSocket, cancellationToken);
						await StartSendingFiltersAsync(socketState.WebSocket, bestKnownBlockHash, cancellationToken);
					}
					catch (Exception e) when (e is FormatException or InvalidOperationException)
					{
						await SendHandshakeErrorAsync(socketState.WebSocket, cancellationToken);
					}

					break;
				default:
					await SendHandshakeErrorAsync(socketState.WebSocket, cancellationToken);
					break;
			}
		}
	}

	private static Task SendHandshakeErrorAsync(WebSocket webSocket, CancellationToken cancellationToken) =>
		webSocket.SendAsync(
			new[] { (byte) ResponseMessage.HandshakeError },
			WebSocketMessageType.Binary,
			true,
			cancellationToken);

	private async Task StartSendingFiltersAsync(
		WebSocket webSocket,
		uint256 bestKnownBlockHash,
		CancellationToken cancellationToken)
	{
		// First we send all the filters from the bestknownblockhash until the tip
		await SendMissingFiltersAsync(webSocket, bestKnownBlockHash, cancellationToken);

		// Subscribe to the filters creation and send filters immediately after they are create.
		Subscribe(webSocket, SendFilter);
	}

	private Task SendBlockHeightAsync(WebSocket webSocket, CancellationToken cancellationToken)
	{
		var lastFilter = _indexBuilderService.GetLastFilter();
		var bestBlockHight = lastFilter.Header.Height;
		var message = new BlockHeightMessage(bestBlockHight);
		return webSocket.SendAsync(message.ToByteArray(), WebSocketMessageType.Binary, true, cancellationToken);
	}

	/// <summary>
	/// SendMissingFiltersAsync sends all the filters since bestknownblockhash to the client.
	/// </summary>
	/// <param name="webSocket">The websocket.</param>
	/// <param name="bestKnownBlockHash">The latest block id known by the client.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	private async Task SendMissingFiltersAsync(
		WebSocket webSocket,
		uint256 bestKnownBlockHash,
		CancellationToken cancellationToken)
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

	private Action<AllFeeEstimate> NotifyFeeEstimations(WebSocket ws) =>
		allFeeEstimate =>
		{
			var message = new MiningFeeRatesMessage(allFeeEstimate);
			ws.SendAsync(message.ToByteArray(), WebSocketMessageType.Binary, true, CancellationToken.None);
		};

	private Action<ExchangeRate> NotifyExchangeRate(WebSocket ws) =>
		exchangeRate =>
		{
			var message = new ExchangeRateMessage(exchangeRate);
			ws.SendAsync(message.ToByteArray(), WebSocketMessageType.Binary, true, CancellationToken.None);
		};

	private Action<FilterModel> SendFilter(WebSocket ws) =>
		filter =>
		{
			var message = new FilterMessage(filter);
			ws.SendAsync(message.ToByteArray(), WebSocketMessageType.Binary, true, CancellationToken.None);
		};
}
