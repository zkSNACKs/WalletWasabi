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
	private readonly List<WebSocket> _sockets = [];

	/// <summary>
	/// Returns a list of opened WebSocket objects.
	/// </summary>
	public IEnumerable<WebSocket> GetWebSockets()
	{
		lock (_sync)
		{
			return _sockets.Where(x => x.State == WebSocketState.Open).ToList();
		}
	}

	public void AddSocket(WebSocket socket)
	{
		lock (_sync)
		{
			_sockets.Add(socket);
		}
	}

	public void RemoveSocket(WebSocket socket)
	{
		lock (_sync)
		{
			_sockets.Remove(socket);
		}
	}
}
