using System.Buffers;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace WalletWasabi.Backend.Middlewares;

public class WebSocketHandlerMiddleware
{
	private readonly RequestDelegate _next;
	private readonly WebSocketHandlerBase _webSocketHandlerBase;

	public WebSocketHandlerMiddleware(RequestDelegate next, WebSocketHandlerBase webSocketHandlerBase)
	{
		_next = next ?? throw new ArgumentNullException(nameof(next));
		_webSocketHandlerBase = webSocketHandlerBase;
	}

	public async Task InvokeAsync(HttpContext context)
	{
		if (context.WebSockets.IsWebSocketRequest)
		{
			using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
			await _webSocketHandlerBase.OnConnectedAsync(webSocket, CancellationToken.None);

			await StartReceivingAsync(webSocket, CancellationToken.None);
		}
		else
		{
			context.Response.StatusCode = (int) HttpStatusCode.BadRequest;
		}
		await _next(context);
	}

	private async Task StartReceivingAsync(WebSocket socket, CancellationToken cancellationToken)
	{
		var buffer = ArrayPool<byte>.Shared.Rent(1024);
		try
		{
			while (socket.State == WebSocketState.Open)
			{
				var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

				switch (result.MessageType)
				{
					case WebSocketMessageType.Binary:
					case WebSocketMessageType.Text:
						await _webSocketHandlerBase.ReceiveAsync(socket, result, buffer, cancellationToken);
						break;
					case WebSocketMessageType.Close:
						await _webSocketHandlerBase.OnDisconnectedAsync(socket, cancellationToken);
						return;
					default:
						throw new NotSupportedException("Not supported WebSocketMessageType: " + result.MessageType);
				}
			}
		}
		catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
		{
			// The remote party closed the WebSocket connection without completing the close handshake.
			await _webSocketHandlerBase.OnDisconnectedAsync(socket, cancellationToken);
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}
}
