using NBitcoin;
using ReactiveUI;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;
using WalletWasabi.Fluent.Models.Currency;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.Models.Wallets;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Send.CurrencyConversion;

public partial class CurrencyInputClipboardListener: ActivatableViewModel
{
	private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(0.2);

	private static readonly IObservable<long> PollingTimer =
		Observable.Timer(PollingInterval)
				  .Repeat();

	private readonly IWalletModel _wallet;
	private readonly CurrencyInputViewModel _parent;

	[AutoNotify] private decimal _minValue;
	[AutoNotify] private decimal? _maxValue;
	[AutoNotify] private string? _text;

	public CurrencyInputClipboardListener(UiContext uiContext, IWalletModel wallet, CurrencyInputViewModel parent)
	{
		_wallet = wallet;
		_parent = parent;

		UiContext = uiContext;

		ApplyCommand = ReactiveCommand.Create<string>(parent.InsertRawFullText);
	}

	public ICommand ApplyCommand { get; }

	protected override void OnActivated(CompositeDisposable disposables)
	{
		base.OnActivated(disposables);

		var currencyFormat = _parent.CurrencyFormat;

		// TODO: hardcoded exchange rate selection,
		// this can be improved to have other currencies than BTC and USD.
		if (currencyFormat == CurrencyFormat.Btc)
		{
			// Bind BTC balance to MaxValue
			_wallet.Balances
				  .Select(x => x.Btc.ToDecimal(MoneyUnit.BTC))
				  .BindTo(this, x => x.MaxValue);
		}
		else if (currencyFormat == CurrencyFormat.Usd)
		{
			// Bind USD converted balances to MaxValue
			_wallet.Balances
				  .Select(x => x.Usd)
				  .Switch()
				  .BindTo(this, x => x.MaxValue);

			MinValue = 1;
		}

		PollingTimer
			.Select(_ => Observable.FromAsync(UiContext.Clipboard.TryGetTextAsync))
			.Merge(1)
			.WhereNotNull()
			.ObserveOn(RxApp.MainThreadScheduler)
			.DistinctUntilChanged()
			.Do(text =>
			{
				// Validate that value can be parsed with current CurrencyFormat
				var vm = new CurrencyInputViewModel(UiContext, currencyFormat, _wallet);
				vm.InsertRaw(text);

				if (vm.Value is not CurrencyValue.Valid v)
				{
					Text = null;
					return;
				}

				var value = v.Value;

				var isValidValue = value >= MinValue && (MaxValue is null || MaxValue >= value);
				if (isValidValue)
				{
					Text = vm.Text;
				}
				else
				{
					Text = null;
				}
			})
			.Subscribe()
			.DisposeWith(disposables);
	}
}