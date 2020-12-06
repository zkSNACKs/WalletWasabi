using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Tor.Socks5;
using WalletWasabi.Tor.Socks5.Exceptions;
using WalletWasabi.Tor.Socks5.Models.Fields.ByteArrayFields;
using WalletWasabi.Tor.Socks5.Models.Fields.OctetFields;
using WalletWasabi.Tor.Socks5.Models.Messages;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.Tor.Socks5
{
	/// <summary>
	/// Tests for <see cref="TorSocks5ClientFactory"/> class.
	/// </summary>
	[Collection("Serial unit tests collection")]
	public class TorSocks5ClientFactoryTests
	{
		private static readonly TimeSpan TimeoutLimit = TimeSpan.FromMinutes(5);

		/// <summary>
		/// <list type="bullet">
		/// <item>Client sends an HTTP request to Tor SOCKS5.</item>
		/// <item>Server verifies that the data received by the Tor SOCKS5 side is correct.</item>
		/// <item>Server responds with <see cref="MethodField.NoAcceptableMethods"/> to the client's handshake.</item>
		/// <item><see cref="TorAuthenticationException"/> is expected to be thrown on the client side.</item>
		/// </list>
		/// </summary>
		[Fact]
		public async Task AuthenticationErrorScenarioAsync()
		{
			using CancellationTokenSource timeoutCts = new(TimeoutLimit);
			CancellationToken timeoutToken = timeoutCts.Token;

			TcpListener? listener = null;

			Uri uri = new("http://postman-echo.com");
			string httpRequestHost = uri.DnsSafeHost;
			int httpRequestPort = 80;

			try
			{
				listener = new(IPAddress.Loopback, port: 0);
				listener.Start();
				int serverPort = ((IPEndPoint)listener.LocalEndpoint).Port;

				Debug.WriteLine("[server] Wait for TCP client.");
				Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync().WithAwaitCancellationAsync(timeoutToken);

				Task clientTask = Task.Run(async () =>
				{
					TorSocks5ClientFactory factory = new(new IPEndPoint(IPAddress.Loopback, serverPort));

					Debug.WriteLine("[client] About to make connection.");
					TorConnection torConnection = await factory.EstablishConnectionAsync(httpRequestHost, httpRequestPort, useSsl: false, isolateStream: false, timeoutToken);
					Debug.WriteLine("[client] Connection established.");
				});

				using TcpClient client = await acceptTask;

				Debug.WriteLine("[server] Connected!");
				using NetworkStream stream = client.GetStream();
				stream.ReadTimeout = (int)TimeoutLimit.TotalMilliseconds;

				// Read SOCKS version.
				int versionByte = stream.ReadByte();
				Assert.Equal(VerField.Socks5.Value, versionByte);

				// Read "NMethods" version.
				int nmethodsByte = stream.ReadByte();
				Assert.Equal(1, nmethodsByte);

				// Read SOCKS version.
				int methodByte = stream.ReadByte();
				Assert.Equal(MethodField.NoAuthenticationRequired.ToByte(), methodByte);

				// Write response: version + method selected.
				stream.WriteByte(VerField.Socks5.Value);
				stream.WriteByte(MethodField.NoAcceptableMethods.ToByte());

				Debug.WriteLine("[server] Expecting exception.");
				await Assert.ThrowsAsync<TorAuthenticationException>(async () => await clientTask.WithAwaitCancellationAsync(timeoutToken));
			}
			finally
			{
				listener?.Stop();
			}
		}

		/// <summary>
		/// <list type="bullet">
		/// <item>Client sends an HTTP request to Tor SOCKS5.</item>
		/// <item>Server verifies that the data received by the Tor SOCKS5 side is correct.</item>
		/// <item>Server responds with <see cref="RepField.TtlExpired"/> to the client's CONNECT command.</item>
		/// <item><see cref="TorConnectCommandException"/> is expected to be thrown on the client side.</item>
		/// </list>
		/// </summary>
		[Fact]
		public async Task TtlExpiredScenarioAsync()
		{
			using CancellationTokenSource timeoutCts = new(TimeoutLimit);
			CancellationToken timeoutToken = timeoutCts.Token;

			TcpListener? listener = null;

			Uri uri = new("http://postman-echo.com");
			string httpRequestHost = uri.DnsSafeHost;
			int httpRequestPort = 80;

			try
			{
				listener = new(IPAddress.Loopback, port: 0);
				listener.Start();
				int serverPort = ((IPEndPoint)listener.LocalEndpoint).Port;

				Debug.WriteLine("[server] Wait for TCP client.");
				Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync().WithAwaitCancellationAsync(timeoutToken);

				Task clientTask = Task.Run(async () =>
				{
					TorSocks5ClientFactory factory = new(new IPEndPoint(IPAddress.Loopback, serverPort));

					Debug.WriteLine("[client] About to make connection.");
					TorConnection torConnection = await factory.EstablishConnectionAsync(httpRequestHost, httpRequestPort, useSsl: false, isolateStream: false, timeoutToken);
					Debug.WriteLine("[client] Connection established.");
				});

				using TcpClient client = await acceptTask;

				Debug.WriteLine("[server] Connected!");
				using NetworkStream stream = client.GetStream();
				stream.ReadTimeout = stream.ReadTimeout = (int)TimeoutLimit.TotalMilliseconds;

				// Read SOCKS version.
				int versionByte = stream.ReadByte();
				Assert.Equal(VerField.Socks5.Value, versionByte);

				// Read "NMethods" version.
				int nmethodsByte = stream.ReadByte();
				Assert.Equal(1, nmethodsByte);

				// Read SOCKS version.
				int methodByte = stream.ReadByte();
				Assert.Equal(MethodField.NoAuthenticationRequired.ToByte(), methodByte);

				// Write response: version + method selected.
				stream.WriteByte(VerField.Socks5.Value);
				stream.WriteByte(MethodField.NoAuthenticationRequired.ToByte());

				TorSocks5Request expectedConnectionRequest = new(cmd: CmdField.Connect, new AddrField(httpRequestHost), new PortField(httpRequestPort));

				int i = 0;
				foreach (byte byteValue in expectedConnectionRequest.ToBytes())
				{
					i++;
					Debug.WriteLine($"[server] Reading request byte #{i}.");
					int readByte = stream.ReadByte();
					Assert.Equal(byteValue, readByte);
				}

				// Tor SOCKS5 response reporting error.
				// Note: RepField.Succeeded is the only OK code.
				byte[] torSocks5Response = new byte[] {
					VerField.Socks5.Value, RepField.TtlExpired.ToByte(), RsvField.X00.ToByte(), AtypField.DomainName.ToByte(),
					0x00, 0x00, 0x00, 0x00, // BndAddr
					0x00, 0x00 // BndPort
				};

				Debug.WriteLine("[server] Respond with RepField.TtlExpired result.");
				await stream.WriteAsync(torSocks5Response, timeoutToken);

				Debug.WriteLine("[server] Expecting exception.");
				await Assert.ThrowsAsync<TorConnectCommandException>(async () => await clientTask.WithAwaitCancellationAsync(timeoutToken));
			}
			finally
			{
				listener?.Stop();
			}
		}
	}
}