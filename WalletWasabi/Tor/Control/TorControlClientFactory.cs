using NBitcoin;
using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Crypto.Randomness;
using WalletWasabi.Logging;
using WalletWasabi.Tor.Control.Exceptions;
using WalletWasabi.Tor.Control.Messages;

namespace WalletWasabi.Tor.Control
{
	/// <summary>
	/// Class to authenticate to Tor Control.
	/// </summary>
	/// <seealso href="https://gitweb.torproject.org/torspec.git/tree/control-spec.txt"/>
	/// <seealso href="https://tools.ietf.org/html/rfc5234"/>
	public class TorControlClientFactory
	{
		/// <summary>Client HMAC-SHA256 key for AUTHCHALLENGE.</summary>
		/// <seealso href="https://gitweb.torproject.org/torspec.git/tree/control-spec.txt">Section 3.24. AUTHCHALLENGE</seealso>
		private static byte[] ClientHmacKey = Encoding.ASCII.GetBytes("Tor safe cookie authentication controller-to-server hash");

		/// <summary></summary>
		private static byte[] ServerHmacKey = Encoding.ASCII.GetBytes("Tor safe cookie authentication server-to-controller hash");

		private static readonly Regex AuthChallengeRegex = new($"^AUTHCHALLENGE SERVERHASH=([a-fA-F0-9]+) SERVERNONCE=([a-fA-F0-9]+)$", RegexOptions.Compiled);

		public TorControlClientFactory(IRandom? random = null)
		{
			Random = random ?? new InsecureRandom();
		}

		private IRandom Random { get; }

		/// <summary>
		/// Sends <c>AUTHENTICATE</c> command.
		/// </summary>
		/// <returns>TODO.</returns>
		/// <seealso href="https://gitweb.torproject.org/torspec.git/tree/control-spec.txt">See section 3.5</seealso>
		/// <exception cref="TorControlException">If TCP connection cannot be established OR if authentication fails for some reason.</exception>
		public async Task<TorControlClient> ConnectAndAuthenticateAsync(IPEndPoint endPoint, string cookieString, CancellationToken cancellationToken = default)
		{
			TcpClient tcpClient = Connect(endPoint);
			TorControlClient? clientToDispose = null;

			try
			{
				TorControlClient controlClient = clientToDispose = new(tcpClient);

				await AuthSafeCookieOrThrowAsync(controlClient, cookieString, cancellationToken).ConfigureAwait(false);

				// All good, do not dispose.
				clientToDispose = null;

				return controlClient;
			}
			finally
			{
				clientToDispose?.Dispose();
			}
		}

		/// <summary>Authenticates client using SAFE-COOKIE.</summary>
		/// <seealso href="https://gitweb.torproject.org/torspec.git/tree/control-spec.txt">See section 3.24 for SAFECOOKIE authentication.</seealso>
		/// <seealso href="https://github.com/torproject/stem/blob/63a476056017dda5ede35efc4e4f7acfcc1d7d1a/stem/connection.py#L893">Python implementation.</seealso>
		/// <exception cref="TorControlException">If TCP connection cannot be established OR if authentication fails for some reason.</exception>
		internal async Task<TorControlClient> AuthSafeCookieOrThrowAsync(TorControlClient controlClient, string cookieString, CancellationToken cancellationToken = default)
		{
			byte[] nonceBytes = new byte[32];
			Random.GetBytes(nonceBytes);
			string clientNonce = ByteHelpers.ToHex(nonceBytes);

			TorControlReply authChallengeReply = await controlClient.SendCommandAsync($"AUTHCHALLENGE SAFECOOKIE {clientNonce}\r\n", cancellationToken).ConfigureAwait(false);

			if (authChallengeReply)
			{
				if (authChallengeReply.ResponseLines.Count != 1)
				{
					Logger.LogError($"Invalid reply: '{authChallengeReply}'");
					throw new TorControlException("Invalid AUTHCHALLENGE reply.");
				}

				string reply = authChallengeReply.ResponseLines[0];
				Match match = AuthChallengeRegex.Match(reply);

				if (!match.Success)
				{
					Logger.LogError($"Invalid reply: '{reply}'");
					throw new TorControlException("Invalid AUTHCHALLENGE reply.");
				}

				string serverNonce = match.Groups[2].Value;
				string toHash = $"{cookieString}{clientNonce}{serverNonce}";

				using HMACSHA256 hmacSha256 = new(ClientHmacKey);
				byte[] serverHash = hmacSha256.ComputeHash(ByteHelpers.FromHex(toHash));
				string serverHashStr = ByteHelpers.ToHex(serverHash);

				Logger.LogTrace($"Authenticate using server hash: '{serverHashStr}'.");
				TorControlReply authenticationReply = await controlClient.SendCommandAsync($"AUTHENTICATE {serverHashStr}\r\n", cancellationToken).ConfigureAwait(false);

				if (!authenticationReply)
				{
					Logger.LogError($"Invalid reply: '{authenticationReply}'");
					throw new TorControlException("Invalid AUTHENTICATE reply.");
				}

				Logger.LogTrace("Success!");
			}

			return controlClient;
		}

		/// <summary>
		/// Connects to Tor control using a TCP client.
		/// </summary>
		/// <exception cref="TorControlException"/>
		private TcpClient Connect(IPEndPoint endPoint)
		{
			try
			{
				TcpClient tcpClient = new();
				tcpClient.Connect(endPoint);
				return tcpClient;
			}
			catch (Exception e)
			{
				Logger.LogError($"Failed to connect to the Tor control: '{endPoint}'.", e);
				throw new TorControlException($"Failed to connect to the Tor control: '{endPoint}'.", e);
			}
		}
	}
}
