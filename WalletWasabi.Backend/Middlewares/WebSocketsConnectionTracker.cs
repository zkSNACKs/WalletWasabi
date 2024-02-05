using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;

namespace WalletWasabi.Backend.Middlewares;

/// <summary>
/// WebSocketsConnectionTracker is a collection of WebSockets with access protected by a lock.
/// </summary>
public class WebSocketsConnectionTracker
{
	private readonly object _sync = new();
	private readonly List<WebSocketConnectionState> _sockets = [];

	/// <summary>
	/// Returns a list of opened WebSocket objects.
	/// </summary>
	public IEnumerable<WebSocketConnectionState> GetWebSocketConnectionStates()
	{
		lock (_sync)
		{
			return _sockets
				.Where(x => x.WebSocket.State == WebSocketState.Open)
				.ToList();
		}
	}

	public void AddSocket(WebSocket socket)
	{
		lock (_sync)
		{
			_sockets.Add(new WebSocketConnectionState(socket, DateTime.UtcNow));
		}
	}

	public void RemoveSocket(WebSocket socket)
	{
		lock (_sync)
		{
			_sockets.RemoveAll(x => x.WebSocket == socket);
		}
	}

	public WebSocketConnectionState GetWebSocketConnectionState(WebSocket socket)
	{
		lock (_sync)
		{
			return _sockets.First(x => x.WebSocket == socket);
		}
	}
}
