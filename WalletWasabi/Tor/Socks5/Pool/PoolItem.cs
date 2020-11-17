using System;
using System.Diagnostics;
using System.Threading;

namespace WalletWasabi.Tor.Socks5.Pool
{
	public enum PoolItemState
	{
		InUse,
		FreeToUse,
		ToDispose
	}

	/// <summary>
	/// Pool item represents a single TCP connection to Tor SOCKS5 endpoint.
	/// <para>Once a pool item is created, it is in <see cref="PoolItemState.InUse"/> state and the internal TCP connection is used immediately
	/// to send an HTTP(s) request.</para>
	/// <para>Then it is decided whether the pool item can be re-used for a next HTTP(s) request or not.</para>
	/// </summary>
	/// <remarks>Currently we re-use TCP connection to Tor SOCKS5 endpoint for HTTP requests but not for HTTPS requests.</remarks>
	public class PoolItem
	{
		private static long Counter;

		private bool _disposedValue;

		/// <summary>
		/// Creates a new instance of the object.
		/// </summary>
		/// <param name="client">TCP client connected to Tor SOCKS5 endpoint.</param>
		/// <param name="allowRecycling">Whether it is allowed to re-use this Tor pool item.</param>
		public PoolItem(TorConnection client, bool allowRecycling)
		{
			Id = Interlocked.Increment(ref Counter);
			State = PoolItemState.InUse;
			Client = client;
			AllowRecycling = allowRecycling;
		}

		private object StateLock { get; } = new object();

		public PoolItemState State { get; private set; }
		private TorConnection Client { get; set; }
		private bool AllowRecycling { get; }
		private long Id { get; }

		public TorConnection GetClient()
		{
			lock (StateLock)
			{
				Debug.Assert(State == PoolItemState.InUse);
			}

			return Client;
		}

		/// <summary>
		/// Gets whether internal <see cref="TorConnection"/> can be re-used for a new HTTP(s) request.
		/// </summary>
		/// <returns><c>true</c> when <see cref="PoolItem"/> must be disposed, <c>false</c> otherwise.</returns>
		public bool NeedRecycling()
		{
			lock (StateLock)
			{
				return (State == PoolItemState.ToDispose) || (AllowRecycling && (State == PoolItemState.FreeToUse) && !Client.IsConnected);
			}
		}

		public bool TryReserve()
		{
			lock (StateLock)
			{
				if (State == PoolItemState.FreeToUse && Client.IsConnected)
				{
					State = PoolItemState.InUse;
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// After the <see cref="PoolItem"/> is used to send an HTTP(s) request, it needs to be un-reserved
		/// so that the pool item can be used again.
		/// </summary>
		/// <returns>Pool item state after un-reserve operation.</returns>
		public PoolItemState Unreserve()
		{
			lock (StateLock)
			{
				Debug.Assert(State == PoolItemState.InUse);
				State = AllowRecycling ? PoolItemState.FreeToUse : PoolItemState.ToDispose;
				return State;
			}
		}

		public override string? ToString()
		{
			return $"PoolItem#{Id}";
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
					Client?.Dispose();
					lock (StateLock)
					{
						State = PoolItemState.ToDispose;
					}
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