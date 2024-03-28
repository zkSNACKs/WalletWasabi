using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.ViewModels.Dialogs.Base;
using WalletWasabi.Fluent.ViewModels.Wallets.Coins;
using WalletWasabi.Fluent.ViewModels.Wallets.Send;

namespace WalletWasabi.Fluent.ViewModels.CoinControl;

[NavigationMetaData(
	Title = "Coin Control",
	Caption = "",
	IconName = "wallet_action_send",
	NavBarPosition = NavBarPosition.None,
	Searchable = false,
	NavigationTarget = NavigationTarget.DialogScreen)]
public partial class SelectCoinsDialogViewModel : DialogViewModelBase<IEnumerable<SmartCoin>>
{
	public SelectCoinsDialogViewModel(IWalletModel wallet, IList<ICoinModel> selectedCoins, TransactionInfo transactionInfo)
	{
		CoinList = new CoinListViewModel(wallet, selectedCoins);

		EnoughSelected = CoinList.Selection.ToObservableChangeSet()
			.ToCollection()
			.Select(coinSelection => wallet.Coins.AreEnoughToCreateTransaction(transactionInfo, coinSelection));
		
		EnableBack = true;
		NextCommand = ReactiveCommand.Create(OnNext, EnoughSelected);

		SetupCancel(false, true, false);
	}

	public CoinListViewModel CoinList { get; }

	public IObservable<bool> EnoughSelected { get; }

	protected override void OnNavigatedFrom(bool isInHistory)
	{
		CoinList.Dispose();

		base.OnNavigatedFrom(isInHistory);
	}

	private void OnNext()
	{
		Close(DialogResultKind.Normal, CoinList.Selection.GetSmartCoins().ToList());
	}
}
