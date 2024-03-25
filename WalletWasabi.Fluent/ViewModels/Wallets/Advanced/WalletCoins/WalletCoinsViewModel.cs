using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Templates;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.TreeDataGrid;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Fluent.ViewModels.Wallets.Send;
using WalletWasabi.Fluent.Views.Wallets.Advanced.WalletCoins.Columns;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Advanced.WalletCoins;

[NavigationMetaData(
	Title = "Wallet Coins (UTXOs)",
	Caption = "Display wallet coins",
	IconName = "nav_wallet_24_regular",
	Order = 0,
	Category = "Wallet",
	Keywords = new[] { "Wallet", "Coins", "UTXO", },
	NavBarPosition = NavBarPosition.None,
	NavigationTarget = NavigationTarget.DialogScreen,
	Searchable = false)]
public partial class WalletCoinsViewModel : RoutableViewModel
{
	private readonly IWalletModel _wallet;
	[AutoNotify] private IObservable<bool> _isAnySelected = Observable.Return(false);

	[AutoNotify]
	private FlatTreeDataGridSource<WalletCoinViewModel> _source = new(Enumerable.Empty<WalletCoinViewModel>());

	private WalletCoinsViewModel(IWalletModel wallet)
	{
		_wallet = wallet;
		SetupCancel(enableCancel: false, enableCancelOnEscape: true, enableCancelOnPressed: true);

		NextCommand = CancelCommand;
		SkipCommand = ReactiveCommand.CreateFromTask(OnSendCoinsAsync);
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Uses DisposeWith()")]
	protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
	{
		var coinChanges =
			_wallet.Coins.List
				.Connect()
				.OnItemAdded(c => c.SubscribeToCoinChanges()) // Subscribe to SmartCoin changes for dynamic updates
				.TransformWithInlineUpdate(x => new WalletCoinViewModel(x), (_, _) => { })
				.DisposeMany()
				.Replay(1)
				.RefCount();

		IsAnySelected =
			coinChanges
				.AutoRefresh(x => x.IsSelected)
				.ToCollection()
				.Select(items => items.Any(t => t.IsSelected))
				.ObserveOn(RxApp.MainThreadScheduler);

		coinChanges
			.ObserveOn(RxApp.MainThreadScheduler)
			.Bind(out var coinsCollection)
			.Subscribe()
			.DisposeWith(disposables);

		coinChanges
			.WhenPropertyChanged(x => x.IsSelected)
			.Select(c => coinsCollection.Where(x => x.Model.IsSameAddress(c.Sender.Model) && x.IsSelected != c.Sender.IsSelected))
			.Do(coins =>
			{
				// Select/deselect all the coins on the same address.
				foreach (var coin in coins)
				{
					coin.IsSelected = !coin.IsSelected;
				}
			})
			.Subscribe()
			.DisposeWith(disposables);

		Source =
			CreateGridSource(coinsCollection)
			.DisposeWith(disposables);

		base.OnNavigatedTo(isInHistory, disposables);
	}

	private static int GetOrderingPriority(WalletCoinViewModel x)
	{
		if (x.Model.IsCoinJoinInProgress)
		{
			return 1;
		}

		if (x.Model.IsBanned)
		{
			return 2;
		}

		if (!x.Model.IsConfirmed)
		{
			return 3;
		}

		return 0;
	}

	private async Task OnSendCoinsAsync()
	{
		// TODO: Leaky abstraction. SmartCoin shouldn't be exposed here.
		// What we need is a TransactionInfo that can operate with ICoinModel instead.
		var selectedSmartCoins =
			Source.Items
				  .Where(x => x.IsSelected)
				  .Select(x => x.Model.GetSmartCoin())
				  .ToImmutableArray();

		var addressResult = await Navigate().To().AddressEntryDialog(_wallet.Network).GetResultAsync();
		if (addressResult is not { } address || address.Address is null)
		{
			return;
		}

		var labelsResult = await Navigate().To().LabelEntryDialog(_wallet, address.Label ?? LabelsArray.Empty).GetResultAsync();
		if (labelsResult is not { } label)
		{
			return;
		}

		var info = new TransactionInfo(address.Address, _wallet.Settings.AnonScoreTarget)
		{
			Coins = selectedSmartCoins,
			Amount = selectedSmartCoins.Sum(x => x.Amount),
			SubtractFee = true,
			Recipient = label,
			IsSelectedCoinModificationEnabled = false,
			IsFixedAmount = true,
		};

		// TODO: Remove this after TransactionPreviewViewModel is decoupled.
		var wallet = MainViewModel.Instance.NavBar.Wallets.First(x => x.Wallet.WalletName == _wallet.Name).WalletViewModel;
		Navigate().To().TransactionPreview(wallet, info);
	}

	private FlatTreeDataGridSource<WalletCoinViewModel> CreateGridSource(IEnumerable<WalletCoinViewModel> coins)
	{
		// [Column]			[View]					[Header]	[Width]		[MinWidth]		[MaxWidth]	[CanUserSort]
		// Selection		SelectionColumnView		-			Auto		-				-			false
		// Indicators		IndicatorsColumnView	-			Auto		-				-			true
		// Amount			AmountColumnView		Amount		Auto		-				-			true
		// AnonymityScore	AnonymityColumnView		<custom>	50			-				-			true
		// Labels			LabelsColumnView		Labels		*			-				-			true
		var source = new FlatTreeDataGridSource<WalletCoinViewModel>(coins)
		{
			Columns =
			{
				SelectionColumn(),
				IndicatorsColumn(),
				AmountColumn(),
				AnonymityScoreColumn(),
				LabelsColumn()
			}
		};

		source.RowSelection!.SingleSelect = true;

		return source;
	}

	private static IColumn<WalletCoinViewModel> SelectionColumn()
	{
		return new TemplateColumn<WalletCoinViewModel>(
			null,
			new FuncDataTemplate<WalletCoinViewModel>((node, ns) => new SelectionColumnView(), true),
			null,
			options: new TemplateColumnOptions<WalletCoinViewModel>
			{
				CanUserResizeColumn = false,
				CanUserSortColumn = false
			},
			width: new GridLength(0, GridUnitType.Auto));
	}

	private static IColumn<WalletCoinViewModel> IndicatorsColumn()
	{
		return new TemplateColumn<WalletCoinViewModel>(
			null,
			new FuncDataTemplate<WalletCoinViewModel>((node, ns) => new IndicatorsColumnView(), true),
			null,
			options: new TemplateColumnOptions<WalletCoinViewModel>
			{
				CanUserResizeColumn = false,
				CanUserSortColumn = true,
				CompareAscending = Sort<WalletCoinViewModel>.Ascending(GetOrderingPriority),
				CompareDescending = Sort<WalletCoinViewModel>.Descending(GetOrderingPriority)
			},
			width: new GridLength(0, GridUnitType.Auto));
	}

	private static IColumn<WalletCoinViewModel> AmountColumn()
	{
		return new PrivacyTextColumn<WalletCoinViewModel>(
			"Amount",
			node => node.Model.Amount.ToFormattedString(),
			options: new ColumnOptions<WalletCoinViewModel>
			{
				CanUserResizeColumn = false,
				CanUserSortColumn = true,
				CompareAscending = Sort<WalletCoinViewModel>.Ascending(x => x.Model.Amount),
				CompareDescending = Sort<WalletCoinViewModel>.Descending(x => x.Model.Amount),
				MinWidth = new GridLength(145, GridUnitType.Pixel)
			},
			width: new GridLength(0, GridUnitType.Auto),
			numberOfPrivacyChars: 9);
	}

	private static IColumn<WalletCoinViewModel> AnonymityScoreColumn()
	{
		return new TemplateColumn<WalletCoinViewModel>(
			new AnonymitySetHeaderView(),
			new FuncDataTemplate<WalletCoinViewModel>((node, ns) => new AnonymitySetColumnView(), true),
			null,
			options: new TemplateColumnOptions<WalletCoinViewModel>
			{
				CanUserResizeColumn = false,
				CanUserSortColumn = true,
				CompareAscending = Sort<WalletCoinViewModel>.Ascending(x => x.Model.AnonScore),
				CompareDescending = Sort<WalletCoinViewModel>.Descending(x => x.Model.AnonScore)
			},
			width: new GridLength(55, GridUnitType.Pixel));
	}

	private static IColumn<WalletCoinViewModel> LabelsColumn()
	{
		return new TemplateColumn<WalletCoinViewModel>(
			"Labels",
			new FuncDataTemplate<WalletCoinViewModel>((node, ns) => new LabelsColumnView(), true),
			null,
			options: new TemplateColumnOptions<WalletCoinViewModel>
			{
				CanUserResizeColumn = false,
				CanUserSortColumn = true,
				CompareAscending = Sort<WalletCoinViewModel>.Ascending(x => x.Model.Labels, LabelsArrayComparer.OrdinalIgnoreCase),
				CompareDescending = Sort<WalletCoinViewModel>.Descending(x => x.Model.Labels, LabelsArrayComparer.OrdinalIgnoreCase),
				MinWidth = new GridLength(100, GridUnitType.Pixel)
			},
			width: new GridLength(1, GridUnitType.Star));
	}
}
