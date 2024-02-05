using System.Linq;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace WalletWasabi.Backend.Middlewares;

public static class WebSocketManagerExtensions
{
	public static IServiceCollection AddWebSocketHandlers(this IServiceCollection services)
	{
		services.AddSingleton<WebSocketsConnectionTracker>();

		if (Assembly.GetEntryAssembly() is { } assembly)
		{
			foreach (var type in assembly.ExportedTypes.Where(t => !t.IsAbstract && t.IsAssignableTo(typeof(WebSocketHandlerBase))))
			{
				services.AddSingleton(type);
			}
		}
		return services;
	}

	public static IApplicationBuilder MapWebSocketManager(this IApplicationBuilder app, PathString path, WebSocketHandlerBase handlerBase) =>
		app.Map(path, _app => _app.UseMiddleware<WebSocketHandlerMiddleware>(handlerBase));
}

public static class WebSocketExtensions
{
	public static async Task SendAsync(this WebSocket webSocket, byte[][] messageParts, CancellationToken cancellationToken)
	{
		foreach (var messagePart in messageParts.Take(messageParts.Length - 1))
		{
			await webSocket.SendAsync(messagePart, WebSocketMessageType.Binary, false, cancellationToken);
		}
		await webSocket.SendAsync(messageParts[^1], WebSocketMessageType.Binary, true, cancellationToken);
	}
}
