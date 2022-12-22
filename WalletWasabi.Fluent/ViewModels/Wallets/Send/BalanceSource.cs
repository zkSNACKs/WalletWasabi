using System.Reactive.Linq;
using NBitcoin;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Send;

public class BalanceSource
{
	public BalanceSource(IObservable<decimal> exchangeRates, IObservable<Money> balances)
	{
		ExchangeRates = exchangeRates;
		Balances = balances;
		UsdBalances = balances.CombineLatest(exchangeRates, (balance, exchangeRate) => balance.ToDecimal(MoneyUnit.BTC) * exchangeRate);
	}

	public IObservable<Money> Balances { get; }
	public IObservable<decimal> UsdBalances { get; }
	public IObservable<decimal> ExchangeRates { get; }
}
