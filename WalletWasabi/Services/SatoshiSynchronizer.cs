using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NBitcoin;
using WalletWasabi.Backend.Models;
using WalletWasabi.Extensions;
using WalletWasabi.Logging;
using WalletWasabi.Stores;
using WalletWasabi.Synchronizarion;

namespace WalletWasabi.Services;

public class SatoshiSynchronizer : BackgroundService
{
	private readonly BitcoinStore _bitcoinStore;
	private readonly Uri _satoshiEndpointUri;
	private readonly Uri? _socksProxyUri;

	public SatoshiSynchronizer(BitcoinStore bitcoinStore, Uri satoshiEndpointUri, EndPoint? socksEndPoint)
	{
		_bitcoinStore = bitcoinStore;
		_satoshiEndpointUri = satoshiEndpointUri;
		_socksProxyUri = socksEndPoint switch
		{
			DnsEndPoint dns => new UriBuilder("socks5", dns.Host, dns.Port).Uri,
			IPEndPoint ip => new UriBuilder("socks5", ip.Address.ToString(), ip.Port).Uri,
			null => null,
			_ => throw new NotSupportedException("The endpoint type is not supported.")
		};
	}

	protected override async Task ExecuteAsync(CancellationToken cancellationToken)
	{
		var localChain = _bitcoinStore.SmartHeaderChain;
		while (!cancellationToken.IsCancellationRequested)
		{
			using var ws = new ClientWebSocket();
			try
			{
				await ConnectToSatoshiEndpointAsync(ws).ConfigureAwait(false);
				await StartReceivingMessagesAsync(ws).ConfigureAwait(false);
			}
			catch (WebSocketException wse) when (wse.InnerException is HttpRequestException re &&
			                                     re.HttpRequestError == HttpRequestError.ConnectionError)
			{
				await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
			}
			catch (Exception e)
			{
				Logger.LogError(e);
			}
			finally
			{
				await DisconnectFromSatoshiEndpointAsync(ws).ConfigureAwait(false);
			}
		}

		return;

		async Task ConnectToSatoshiEndpointAsync(ClientWebSocket ws)
		{
			ws.Options.Proxy = _socksProxyUri is not null ? new WebProxy(_socksProxyUri) : ws.Options.Proxy;
			await ws.ConnectAsync(_satoshiEndpointUri, cancellationToken).ConfigureAwait(false);
			await WaitForTipHashAsync().ConfigureAwait(false);
			await HandshakeAsync(localChain.TipHash).ConfigureAwait(false);

			async Task WaitForTipHashAsync()
			{
				while (localChain.TipHash is null) // Just another hidden communication/synchronization channel
				{
					await Task.Delay(100, cancellationToken).ConfigureAwait(false);
				}
			}

			async Task HandshakeAsync(uint256 tipHash)
			{
				var handshake = new HandshakeMessage(tipHash);
				await ws.SendAsync(handshake.ToByteArray(), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
			}
		}

		async Task DisconnectFromSatoshiEndpointAsync(WebSocket ws)
		{
			if (ws.State == WebSocketState.Open)
			{
				await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", cancellationToken).ConfigureAwait(false);
			}
		}

		async Task StartReceivingMessagesAsync(WebSocket ws)
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				var buffer = new byte[80 * 1024];
				var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

				if (result.MessageType == WebSocketMessageType.Close)
				{
					return;
				}

				var messageType = (ResponseMessage)buffer[0];
				using var reader = new BinaryReader(new MemoryStream(buffer[1..]));
				switch (messageType)
				{
					case ResponseMessage.BlockHeight:
						var height = reader.ReadUInt32();
						localChain.SetServerTipHeight(height);
						break;

					case ResponseMessage.Filter:
						var filter = reader.ReadFilterModel();
						if (localChain.TipHeight + 1 != filter.Header.Height)
						{
							Logger.LogError(ChainHeightMismatchError(filter));
							await RewindAsync(1).ConfigureAwait(false);
							return;
						}

						await _bitcoinStore.IndexStore.AddNewFiltersAsync([filter]).ConfigureAwait(false);
						break;

					case ResponseMessage.HandshakeError:
						await RewindAsync(144).ConfigureAwait(false);
						return;

					case ResponseMessage.ExchangeRate:
						var exchangeRates = reader.ReadDecimal();
						break;

					case ResponseMessage.MiningFeeRates:
						var allFeeEstimate = reader.ReadMiningFeeRates();
						break;

					default:
						throw new ArgumentOutOfRangeException();
				}
			}
		}

		async Task RewindAsync(int count)
		{
			var rewindCount = Math.Min(count, localChain.HashCount);
			var rewindTipHeight = localChain.TipHeight - rewindCount;
			await _bitcoinStore.IndexStore.RemoveAllNewerThanAsync((uint) rewindTipHeight).ConfigureAwait(false);
		}

		string ChainHeightMismatchError(FilterModel filter)
		{
			var sb = new StringBuilder();
			sb.AppendLine("Inconsistent index state detected.");
			sb.Append($"Local chain: {localChain.TipHeight}/{localChain.ServerTipHeight} ({localChain.HashesLeft} left");
			sb.AppendLine($"- best known block hash: {localChain.TipHash}");
			sb.AppendLine($"Received filter: {filter.Header.BlockHash} height: {filter.Header.Height}");
			return sb.ToString();
		}
	}
}
