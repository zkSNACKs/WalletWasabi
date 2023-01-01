using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.VisualTree;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Home.Tiles;

public class WalletBalanceTileViewModel : ActivatableViewModel
{
	private readonly ObservableAsPropertyHelper<bool> _isCopyActive;

	public WalletBalanceTileViewModel(WalletViewModel walletVm)
	{
		var wallet = walletVm.Wallet;

		var balance = walletVm.UiTriggers.BalanceUpdateTrigger
			.Select(_ => wallet.Coins.TotalAmount());

		BalanceBtc = balance
			.Select(FormatMoney);

		BalanceFiat = balance
			.Select(money => money.BtcToUsd(wallet.Synchronizer.UsdExchangeRate));

		HasBalance = balance
			.Select(money => money > Money.Zero);

		CopyBalanceCommand = ReactiveCommand.CreateFromObservable<PointerEventArgs, Unit>(eventArgs =>
		{
			if (!eventArgs.GetCurrentPoint((IVisual?)eventArgs.Source).Properties.IsRightButtonPressed)
			{
				return Observable.Empty(Unit.Default);
			}

			return Observable.FromAsync(() => SetTextAsync(wallet))
				.ToSignal()
				.Concat(Observable.Timer(TimeSpan.FromSeconds(2)).ToSignal());
		});

		_isCopyActive = CopyBalanceCommand.IsExecuting.ToProperty(this, x => x.IsCopyRunning);
	}

	public bool IsCopyRunning => _isCopyActive.Value;

	public ReactiveCommand<PointerEventArgs, Unit> CopyBalanceCommand { get; set; }

	public IObservable<bool> HasBalance { get; }

	public IObservable<decimal> BalanceFiat { get; }

	public IObservable<string> BalanceBtc { get; }

	private static string FormatMoney(Money money)
	{
		return $"{money.ToFormattedString()} BTC";
	}

	private static async Task SetTextAsync(Wallet wallet)
	{
		if (Application.Current is { Clipboard: { } clipboard })
		{
			await clipboard.SetTextAsync(FormatMoney(wallet.Coins.TotalAmount()));
		}
	}
}
