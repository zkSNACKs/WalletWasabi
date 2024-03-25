using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ReactiveUI;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.Infrastructure;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.ViewModels.Navigation;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Settings;

[AppLifetime]
[NavigationMetaData(
	Title = "Wallet Settings",
	Caption = "Display wallet settings",
	IconName = "nav_wallet_24_regular",
	Order = 2,
	Category = "Wallet",
	Keywords = new[] { "Wallet", "Settings", },
	NavBarPosition = NavBarPosition.None,
	NavigationTarget = NavigationTarget.DialogScreen,
	Searchable = false)]
public partial class WalletSettingsViewModel : RoutableViewModel
{
	private readonly IWalletModel _wallet;
	[AutoNotify] private bool _preferPsbtWorkflow;
	[AutoNotify] private string _walletName;
	[AutoNotify] private int _selectedTab;

	public WalletSettingsViewModel(UiContext uiContext, IWalletModel walletModel)
	{
		UiContext = uiContext;
		_wallet = walletModel;
		_walletName = walletModel.Name;
		_preferPsbtWorkflow = walletModel.Settings.PreferPsbtWorkflow;
		_selectedTab = 0;
		IsHardwareWallet = walletModel.IsHardwareWallet;
		IsWatchOnly = walletModel.IsWatchOnlyWallet;

		WalletCoinJoinSettings = new WalletCoinJoinSettingsViewModel(UiContext, walletModel);

		SetupCancel(enableCancel: false, enableCancelOnEscape: true, enableCancelOnPressed: true);

		NextCommand = CancelCommand;

		VerifyRecoveryWordsCommand = ReactiveCommand.Create(() => Navigate().To().WalletVerifyRecoveryWords(walletModel));

		this.WhenAnyValue(x => x.PreferPsbtWorkflow)
			.Skip(1)
			.Subscribe(value =>
			{
				walletModel.Settings.PreferPsbtWorkflow = value;
				walletModel.Settings.Save();
			});

		this.WhenAnyValue(x => x._wallet.Name).BindTo(this, x => x.WalletName);

		RenameCommand = ReactiveCommand.CreateFromTask(OnRenameWalletAsync);
	}

	public ICommand RenameCommand { get; set; }

	public bool IsHardwareWallet { get; }

	public bool IsWatchOnly { get; }

	public WalletCoinJoinSettingsViewModel WalletCoinJoinSettings { get; private set; }

	public ICommand VerifyRecoveryWordsCommand { get; }

	private async Task OnRenameWalletAsync()
	{
		await Navigate().To().WalletRename(_wallet).GetResultAsync();
		UiContext.WalletRepository.StoreLastSelectedWallet(_wallet);
	}
}
