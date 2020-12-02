using Nito.AsyncEx;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Tor.Http;
using WalletWasabi.Tor.Http.Extensions;
using WalletWasabi.Tor.Socks5.Exceptions;
using WalletWasabi.Tor.Socks5.Models.Fields.OctetFields;
using WalletWasabi.Tor.Socks5.Pool;
using WalletWasabi.Tor.Socks5.Utils;

namespace WalletWasabi.Tor.Socks5
{
	/// <summary>
	/// The pool represents a set of multiple TCP connections to Tor SOCKS5 endpoint that are stored in <see cref="TorPoolItem"/>s.
	/// <para>
	/// When a new HTTP(s) request comes, <see cref="TorPoolItem"/> (or rather the TCP connection wrapped inside) is selected using these rules:
	/// <list type="number">
	/// <item>An unused <see cref="TorPoolItem"/> is selected, if it exists.</item>
	/// <item>A new <see cref="TorPoolItem"/> is added to the pool, if it would not exceed the maximum limit on the number of connections to Tor SOCKS5 endpoint.</item>
	/// <item>Keep waiting 1 second until any of the previous rules cannot be used.</item>
	/// </list>
	/// </para>
	/// <para><see cref="ClientsAsyncLock"/> is acquired only for <see cref="TorPoolItem"/> selection.</para>
	/// </summary>
	public class TorSocks5ClientPool : IDisposable
	{
		/// <summary>Maximum number of <see cref="TorPoolItem"/>s per URI host.</summary>
		public const int MaxPoolItemsPerHost = 3;

		/// <summary>
		/// Creates a new instance of the object.
		/// </summary>
		public TorSocks5ClientPool(EndPoint endpoint):
			this(new ClearnetHttpClient(), new TorSocks5ClientFactory(endpoint), new TorPoolItemManager(MaxPoolItemsPerHost))
		{
		}

		/// <summary>
		/// Creates a new instance of the object.
		/// </summary>
		/// <remarks>Use this constructor for tests.</remarks>
		public TorSocks5ClientPool(IRelativeHttpClient httpClient, TorSocks5ClientFactory torSocks5ClientFactory, TorPoolItemManager poolItemManager)
		{
			ClearnetHttpClient = httpClient;
			TorSocks5ClientFactory = torSocks5ClientFactory;
			PoolItemManager = poolItemManager;
		}

		private bool _disposedValue;

		/// <remarks>Lock object to guard all access to <see cref="Clients"/>.</remarks>
		private AsyncLock ClientsAsyncLock { get; } = new AsyncLock();

		private TorPoolItemManager PoolItemManager { get; }

		private IRelativeHttpClient ClearnetHttpClient { get; }
		private TorSocks5ClientFactory TorSocks5ClientFactory { get; }

		/// <summary>TODO: Add locking and wrap in a class.</summary>
		public DateTimeOffset? TorDoesntWorkSince { get; private set; }

		/// <summary>TODO: Add locking.</summary>
		public Exception? LatestTorException { get; private set; } = null;

		/// <summary>
		/// This method is called when an HTTP(s) request fails for some reason.
		/// <para>The information is stored to allow <see cref="TorMonitor"/> to restart Tor as deemed fit.</para>
		/// </summary>
		/// <param name="e">Tor exception.</param>
		private void OnTorRequestFailed(Exception e)
		{
			if (TorDoesntWorkSince is null)
			{
				TorDoesntWorkSince = DateTimeOffset.UtcNow;
			}

			LatestTorException = e;
		}

		/// <summary>
		/// Robust sending algorithm. TODO.
		/// </summary>
		/// <param name="request">TODO.</param>
		/// <param name="isolateStream">TODO.</param>
		/// <param name="token">TODO.</param>
		public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, bool isolateStream, CancellationToken token = default)
		{
			// Connecting to loopback's URIs cannot be done via Tor.
			if (request.RequestUri!.IsLoopback)
			{
				return await ClearnetHttpClient.SendAsync(request, token).ConfigureAwait(false);
			}

			int i = 0;
			int attemptsNo = 3;

			try
			{
				do
				{
					i++;
					IPoolItem poolItem = await ObtainFreePoolItemAsync(request, isolateStream, token).ConfigureAwait(false);
					IPoolItem? itemToDispose = poolItem;

					try
					{
						Logger.LogTrace($"['{poolItem}'] About to send request.");
						HttpResponseMessage response = await SendCoreAsync(poolItem.GetTransportStream(), request, token).ConfigureAwait(false);

						// Client works OK, no need to dispose.
						itemToDispose = null;

						// Let others use the client.
						var state = poolItem.Unreserve();
						Logger.LogTrace($"['{poolItem}'] Unreserve. State is: '{state}'.");

						TorDoesntWorkSince = null;
						LatestTorException = null;

						return response;
					}
					catch (TorConnectCommandFailedException ex) when (ex.RepField == RepField.TtlExpired)
					{
						// If we get TTL Expired error then wait and retry again linux often does this.
						Logger.LogTrace(ex);

						await Task.Delay(1000, token).ConfigureAwait(false);

						if (i == attemptsNo)
						{
							Logger.LogDebug($"All {attemptsNo} attempts failed."); // TODO: Improve message.
							throw;
						}
					}
					catch (SocketException ex) when (ex.ErrorCode == (int)SocketError.ConnectionRefused)
					{
						Logger.LogTrace(ex);
						throw new TorConnectionException("Connection was refused.", ex);
					}
					finally
					{
						(itemToDispose as IDisposable)?.Dispose();
					}
				} while (i < attemptsNo);
			}
			catch (Exception ex)
			{
				OnTorRequestFailed(ex);
				throw;
			}

			throw new NotImplementedException("This should never happen.");
		}

		private async Task<IPoolItem> ObtainFreePoolItemAsync(HttpRequestMessage request, bool isolateStream, CancellationToken token)
		{
			Logger.LogTrace($"> request='{request.RequestUri}', isolateStream={isolateStream}");

			string host = GetRequestHost(request);

			do
			{
				using (await ClientsAsyncLock.LockAsync(token).ConfigureAwait(false))
				{
					(bool canBeAdded, IPoolItem? poolItem) = PoolItemManager.GetPoolItem(host, isolateStream);

					if (poolItem is { })
					{
						Logger.LogTrace($"[OLD {poolItem}]['{request.RequestUri}'] Re-use existing Tor SOCKS5 connection.");
						return poolItem;
					}

					if (canBeAdded)
					{
						poolItem = await CreateNewPoolItemNoLockAsync(request, isolateStream, token).ConfigureAwait(false);

						if (poolItem is { })
						{
							Logger.LogTrace($"[NEW {poolItem}]['{request.RequestUri}'] Using new Tor SOCKS5 connection.");
							return poolItem;
						}
					}
				}

				Logger.LogTrace("Wait 1s for a free pool item.");
				await Task.Delay(1000, token).ConfigureAwait(false);
			} while (true);
		}

		/// <remarks>Caller is responsible for acquiring <see cref="ClientsAsyncLock"/>.</remarks>
		private async Task<IPoolItem?> CreateNewPoolItemNoLockAsync(HttpRequestMessage request, bool isolateStream, CancellationToken token)
		{
			string host = GetRequestHost(request);

			TorPoolItem? poolItem = null;

			try
			{
				bool useSsl = request.RequestUri!.Scheme == Uri.UriSchemeHttps;
				bool allowRecycling = !useSsl && !isolateStream;
				int port = request.RequestUri!.Port;

				TorConnection newClient = await TorSocks5ClientFactory.MakeAsync(host, port, useSsl, isolateStream, token).ConfigureAwait(false);
				poolItem = new TorPoolItem(newClient, allowRecycling);

				Logger.LogTrace($"[NEW {poolItem}]['{request.RequestUri}'] Created new Tor SOCKS5 connection.");

				PoolItemManager.AddPoolItem(host, poolItem);
			}
			catch (TorException e)
			{
				Logger.LogDebug($"['{host}'][ERROR] Failed to create a new pool item.");
				Logger.LogError(e);
				throw;
			}
			catch (Exception e)
			{
				Logger.LogTrace($"['{host}'][EXCEPTION] {e}");
				throw;
			}

			Logger.LogTrace($"< poolItem='{poolItem}'; Context: existing hostItems = {string.Join(',', PoolItemManager.GetItemsCopy(host).Select(x => x.ToString()).ToArray())}.");
			return poolItem;
		}

		private async static Task<HttpResponseMessage> SendCoreAsync(Stream transportStream, HttpRequestMessage request, CancellationToken token = default)
		{
			TorHttpRequestPreprocessor.Preprocess(request);
			string requestString = await TorHttpRequestMessageSerializer.ToStringAsync(request, token).ConfigureAwait(false);
			byte[] bytes = Encoding.UTF8.GetBytes(requestString);

			await transportStream.WriteAsync(bytes.AsMemory(0, bytes.Length), token).ConfigureAwait(false);
			await transportStream.FlushAsync(token).ConfigureAwait(false);

			return await HttpResponseMessageExtensions.CreateNewAsync(transportStream, request.Method).ConfigureAwait(false);
		}

		private static string GetRequestHost(HttpRequestMessage request)
		{
			return Guard.NotNullOrEmptyOrWhitespace(nameof(request.RequestUri.DnsSafeHost), request.RequestUri!.DnsSafeHost, trim: true);
		}

		/// <summary>
		/// <list type="bullet">
		/// <item>Unmanaged resources need to be released regardless of the value of the <paramref name="disposing"/> parameter.</item>
		/// <item>Managed resources need to be released if the value of <paramref name="disposing"/> is <c>true</c>.</item>
		/// </list>
		/// </summary>
		/// <param name="disposing">
		/// Indicates whether the method call comes from a <see cref="Dispose()"/> method
		/// (its value is <c>true</c>) or from a finalizer (its value is <c>false</c>).
		/// </param>
		protected virtual void Dispose(bool disposing)
		{
			if (!_disposedValue)
			{
				if (disposing)
				{
					PoolItemManager.Dispose();
				}
				_disposedValue = true;
			}
		}

		/// <summary>
		/// Do not change this code.
		/// </summary>
		public void Dispose()
		{
			// Dispose of unmanaged resources.
			Dispose(true);

			// Suppress finalization.
			GC.SuppressFinalize(this);
		}
	}
}