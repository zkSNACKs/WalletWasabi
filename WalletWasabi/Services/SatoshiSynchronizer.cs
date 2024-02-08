using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using WalletWasabi.Backend.Models;
using WalletWasabi.Blockchain.BlockFilters;
using WalletWasabi.Extensions;
using WalletWasabi.Stores;
using WalletWasabi.Synchronizarion;

namespace WalletWasabi.Services;

public class SatoshiSynchronizer : BackgroundService
{
	private readonly BitcoinStore _bitcoinStore;
	private readonly Uri _satoshiEndpointUri;
	private readonly Uri? _socksProxyUri;
	private readonly FilterProcessor _filterProcessor;

	public SatoshiSynchronizer(BitcoinStore bitcoinStore, Uri satoshiEndpointUri, EndPoint? socksEndPoint)
	{
		_bitcoinStore = bitcoinStore;
		_satoshiEndpointUri = satoshiEndpointUri;
		_filterProcessor = new FilterProcessor(bitcoinStore);
		_socksProxyUri = socksEndPoint switch
		{
			DnsEndPoint dns => new UriBuilder("socks5", dns.Host, dns.Port).Uri,
			IPEndPoint ip => new UriBuilder("socks5", ip.Address.ToString(), ip.Port).Uri,
			null => null,
			_ => throw new NotSupportedException("The endpoint type is not supported.")
		};
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var ws = new ClientWebSocket();
		ws.Options.Proxy = _socksProxyUri is not null ? new WebProxy(_socksProxyUri) : ws.Options.Proxy;
		await ws.ConnectAsync(_satoshiEndpointUri, stoppingToken).ConfigureAwait(false);

		while (_bitcoinStore.SmartHeaderChain.TipHash is null)
		{
			await Task.Delay(100, stoppingToken).ConfigureAwait(false);
		}

		var handshake = new HandshakeMessage(_bitcoinStore.SmartHeaderChain.TipHash);
		await ws.SendAsync(handshake.ToByteArray(), WebSocketMessageType.Binary, true, stoppingToken).ConfigureAwait(false);

		while (!stoppingToken.IsCancellationRequested)
		{
			var buffer = new byte[80 * 1024];
			var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), stoppingToken).ConfigureAwait(false);

			if (result.MessageType == WebSocketMessageType.Close)
			{
				break;
			}

			var messageType = (ResponseMessage)buffer[0];
			using var reader = new BinaryReader(new MemoryStream(buffer[1..]));
			switch (messageType)
			{
				case ResponseMessage.Filter:
					var filter = reader.ReadFilterModel();

					await _filterProcessor.ProcessAsync(filter.Header.Height, FiltersResponseState.NewFilters, [filter])
						.ConfigureAwait(false);
					break;
				case ResponseMessage.HandshakeError:
					await _filterProcessor.ProcessAsync(_bitcoinStore.SmartHeaderChain.ServerTipHeight, FiltersResponseState.BestKnownHashNotFound, [])
						.ConfigureAwait(false);
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}
	}
}
