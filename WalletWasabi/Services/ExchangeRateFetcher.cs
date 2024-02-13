using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using WalletWasabi.Backend.Models;
using WalletWasabi.Interfaces;

namespace WalletWasabi.Services;

public class ExchangeRateFetcher : BackgroundService
{
	private readonly IExchangeRateProvider _exchangeRateProvider;
	private readonly EventBus _eventBus;
	private ExchangeRate? _lastBtcUsdRate;

	public ExchangeRateFetcher(IExchangeRateProvider exchangeRateProvider, EventBus eventBus)
	{
		_exchangeRateProvider = exchangeRateProvider;
		_eventBus = eventBus;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
		while (
			!stoppingToken.IsCancellationRequested &&
			await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
		{
			var rates = await _exchangeRateProvider.GetExchangeRateAsync(stoppingToken).ConfigureAwait(false);
			if (rates.FirstOrDefault() is { } btcusd && btcusd != _lastBtcUsdRate)
			{
				_lastBtcUsdRate = btcusd;
				_eventBus.Publish(btcusd);
			}
		}
	}
}
