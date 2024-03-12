using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia.Controls;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using WalletWasabi.Fluent.Controls.Sorting;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.ViewModels.Navigation;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Receive;

[NavigationMetaData(Title = "Addresses Awaiting Payment")]
public partial class ReceiveAddressesViewModel : RoutableViewModel
{
	private readonly IWalletModel _wallet;

	[AutoNotify] private FlatTreeDataGridSource<AddressViewModel> _source = new(Enumerable.Empty<AddressViewModel>());

	private ReceiveAddressesViewModel(IWalletModel wallet)
	{
		_wallet = wallet;

		EnableBack = true;
		SetupCancel(enableCancel: true, enableCancelOnEscape: true, enableCancelOnPressed: true);
	}

	public IEnumerable<SortableItem>? Sortables { get; private set; }

	protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
	{
		_wallet.Addresses.Unused
			.ToObservableChangeSet()
			.Transform(CreateAddressViewModel)
			.Bind(out var unusedAddresses)
			.Subscribe()
			.DisposeWith(disposables);

		var source = ReceiveAddressesDataGridSource.Create(unusedAddresses);

		Source = source;
		Source.RowSelection!.SingleSelect = true;
		Source.DisposeWith(disposables);

		Sortables =
		[
			new SortableItem("Address") { SortByAscendingCommand = ReactiveCommand.Create(() => ((ITreeDataGridSource) Source).SortBy(Source.Columns[0], ListSortDirection.Ascending)), SortByDescendingCommand = ReactiveCommand.Create(() => ((ITreeDataGridSource) Source).SortBy(Source.Columns[0], ListSortDirection.Descending)) },
			new SortableItem("Label") { SortByAscendingCommand = ReactiveCommand.Create(() => ((ITreeDataGridSource) Source).SortBy(Source.Columns[1], ListSortDirection.Ascending)), SortByDescendingCommand = ReactiveCommand.Create(() => ((ITreeDataGridSource) Source).SortBy(Source.Columns[1], ListSortDirection.Descending)) }
		];

		base.OnNavigatedTo(isInHistory, disposables);
	}

	private AddressViewModel CreateAddressViewModel(IAddress address)
	{
		return new AddressViewModel(UiContext, OnEditAddressAsync, OnShowAddressAsync, address);
	}

	private void OnShowAddressAsync(IAddress a)
	{
		UiContext.Navigate().To().ReceiveAddress(_wallet, a, Services.UiConfig.Autocopy);
	}

	private async Task OnEditAddressAsync(IAddress address)
	{
		var result = await Navigate().To().AddressLabelEdit(_wallet, address).GetResultAsync();
		if (result is { } labels)
		{
			address.SetLabels(labels);
		}
	}
}
