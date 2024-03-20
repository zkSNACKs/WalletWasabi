using System.Net.WebSockets;

namespace WalletWasabi.Backend.Middlewares;

public class WebSocketConnectionState(WebSocket webSocket, DateTime connectedSince)
{
	public WebSocket WebSocket { get; } = webSocket;
	public DateTime ConnectedSince { get; } = connectedSince;
	public bool Handshaked { get; set; }
}
