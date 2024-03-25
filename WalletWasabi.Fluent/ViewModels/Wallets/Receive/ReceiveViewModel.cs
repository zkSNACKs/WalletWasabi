using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;
using DynamicData.Binding;
using DynamicData.Aggregation;
using ReactiveUI;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Fluent.ViewModels.Wallets.Labels;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Receive;

[NavigationMetaData(
	Title = "Receive",
	Caption = "Display wallet receive dialog",
	IconName = "wallet_action_receive",
	Order = 6,
	Category = "Wallet",
	Keywords = new[] { "Wallet", "Receive", "Action", },
	NavBarPosition = NavBarPosition.None,
	NavigationTarget = NavigationTarget.DialogScreen,
	Searchable = false)]
public partial class ReceiveViewModel : RoutableViewModel, IDisposable
{
	private readonly IWalletModel _wallet;
	private readonly CompositeDisposable _disposables = new();

	private ReceiveViewModel(IWalletModel wallet)
	{
		_wallet = wallet;
		SetupCancel(enableCancel: true, enableCancelOnEscape: true, enableCancelOnPressed: true);

		EnableBack = false;

		SuggestionLabels = new SuggestionLabelsViewModel(wallet, Intent.Receive, 3);

		var nextCommandCanExecute =
			SuggestionLabels
				.WhenAnyValue(x => x.Labels.Count).ToSignal()
				.Merge(SuggestionLabels.WhenAnyValue(x => x.IsCurrentTextValid).ToSignal())
				.Select(_ => SuggestionLabels.Labels.Count > 0 || SuggestionLabels.IsCurrentTextValid);

		NextCommand = ReactiveCommand.Create(OnNext, nextCommandCanExecute);

		ShowExistingAddressesCommand = ReactiveCommand.Create(OnShowExistingAddresses);

		AddressesModel = wallet.Addresses;
	}

	public IAddressesModel AddressesModel { get; }

	public SuggestionLabelsViewModel SuggestionLabels { get; }

	public ICommand ShowExistingAddressesCommand { get; }

	public IObservable<bool> HasUnusedAddresses => _wallet.Addresses.Unused.ToObservableChangeSet().Count().Select(i => i > 0);

	protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
	{
		base.OnNavigatedTo(isInHistory, disposables);
		SuggestionLabels.Activate(disposables);
	}

	private void OnNext()
	{
		SuggestionLabels.ForceAdd = true;
		var address = _wallet.Addresses.NextReceiveAddress(SuggestionLabels.Labels);
		SuggestionLabels.Labels.Clear();

		Navigate().To().ReceiveAddress(_wallet, address, Services.UiConfig.Autocopy);
	}

	private void OnShowExistingAddresses()
	{
		UiContext.Navigate(NavigationTarget.DialogScreen).To().ReceiveAddresses(_wallet);
	}

	public void Dispose() => _disposables.Dispose();
}
