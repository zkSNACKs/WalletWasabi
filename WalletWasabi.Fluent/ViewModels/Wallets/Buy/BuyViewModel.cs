using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using WalletWasabi.BuyAnything;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Fluent.ViewModels.Wallets.Buy.Workflows;
using WalletWasabi.Logging;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Buy;

[NavigationMetaData(
	Title = "Buy Anything",
	Caption = "Display wallet buy dialog",
	IconName = "wallet_action_buy",
	Order = 7,
	Category = "Wallet",
	Keywords = new[] { "Wallet", "Buy", "Action", },
	NavBarPosition = NavBarPosition.None,
	NavigationTarget = NavigationTarget.DialogScreen,
	Searchable = false)]
public partial class BuyViewModel : RoutableViewModel, IOrderManager
{
	private readonly BuyAnythingManager _buyAnythingManager;
	private readonly CancellationTokenSource _cts;
	private readonly ReadOnlyObservableCollection<OrderViewModel> _orders;
	private readonly SourceCache<OrderViewModel, int> _ordersCache;
	private readonly Wallet _wallet;
	[AutoNotify] private OrderViewModel? _emptyOrder; // Used to track the "Empty" order (with empty ConversationId)

	[AutoNotify] private OrderViewModel? _selectedOrder;

	public BuyViewModel(UiContext uiContext, WalletViewModel walletVm)
	{
		IsBusy = true;
		UiContext = uiContext;
		WalletVm = walletVm;

		_wallet = walletVm.Wallet;
		_buyAnythingManager = Services.HostedServices.Get<BuyAnythingManager>();

		SetupCancel(enableCancel: true, enableCancelOnEscape: true, enableCancelOnPressed: true);

		EnableBack = false;

		_ordersCache = new SourceCache<OrderViewModel, int>(x => x.OrderNumber);

		_ordersCache
			.Connect()
			.Sort(SortExpressionComparer<OrderViewModel>.Descending(x => x.OrderNumber))
			.DisposeMany()
			.Bind(out _orders)
			.Subscribe();

		_cts = new CancellationTokenSource();

		// When the Empty Order stops being empty, create a new Empty Order
		this.WhenAnyValue(x => x.EmptyOrder.Workflow.Conversation)
			.Where(x => x.Id != ConversationId.Empty)
			.Do(_ => EmptyOrder = NewEmptyOrder())
			.Subscribe();

		HasNonEmptyOrder = this.WhenAnyValue(x => x.Orders.Count)
			.Select(_ => Orders.Any(x => x.ConversationId != ConversationId.Empty));

		Activate();
	}

	public IObservable<bool> HasNonEmptyOrder { get; }

	public ReadOnlyObservableCollection<OrderViewModel> Orders => _orders;

	public WalletViewModel WalletVm { get; }

	async Task IOrderManager.RemoveOrderAsync(int id)
	{
		if (Orders.FirstOrDefault(x => x.OrderNumber == id) is { } orderToRemove)
		{
			await _buyAnythingManager.RemoveConversationsByIdsAsync(new[] { orderToRemove.ConversationId }, _cts.Token);
		}

		_ordersCache.RemoveKey(id);
		SelectedOrder = _orders.FirstOrDefault();
	}

	public async Task OnError(Exception ex)
	{
		if (!IsActive)
		{
			return;
		}

		await ShowErrorAsync(
			"Buy Anything",
			ex.ToUserFriendlyString(),
			"",
			NavigationTarget.CompactDialogScreen);
	}

	public void Activate()
	{
		Task.Run(async () => await InitializeAsync(), _cts.Token);
	}

	protected override void OnNavigatedTo(bool inHistory, CompositeDisposable disposables)
	{
		base.OnNavigatedTo(inHistory, disposables);

		// Mark Conversation as read for selected order
		this.WhenAnyValue(x => x.SelectedOrder, x => x.SelectedOrder.Workflow.Conversation, (order, _) => order)
			.WhereNotNull()
			.DoAsync(order => order.MarkAsReadAsync())
			.Subscribe()
			.DisposeWith(disposables);

		MarkNewMessagesFromSelectedOrderAsRead().DisposeWith(disposables);
	}

	protected override void OnNavigatedFrom(bool isInHistory)
	{
		base.OnNavigatedFrom(isInHistory);

		SelectNewOrderIfAny();
	}

	private void SelectNewOrderIfAny()
	{
		// We should always have an empty order, but this condition is for safety, just in case we change this axiom.
		if (EmptyOrder is { } emptyOrder)
		{
			SelectedOrder = emptyOrder;
		}
	}

	private IDisposable MarkNewMessagesFromSelectedOrderAsRead()
	{
		return this
			.WhenAnyValue(x => x.SelectedOrder)
			.WhereNotNull()
			.Select(x => x.Messages.ToObservableChangeSet())
			.Switch()
			.OnItemAdded(x => x.IsUnread = false)
			.Subscribe();
	}

	private async Task InitializeAsync()
	{
		try
		{
			var currentConversations = await _buyAnythingManager.GetConversationsAsync(_wallet, _cts.Token);

			var orders =
				currentConversations.Select((conversation, index) =>
				{
					var workflow = Workflow.Create(_wallet, conversation);
					var order = new OrderViewModel(UiContext, WalletVm.WalletModel, workflow, this, index, _cts.Token);
					return order;
				})
				.ToArray();

			_ordersCache.AddOrUpdate(orders);

			if (_orders.Count == 0 || _orders.All(x => x.ConversationId != ConversationId.Empty))
			{
				EmptyOrder = NewEmptyOrder();
			}

			SelectNewOrderIfAny();
		}
		catch (Exception exception)
		{
			Logger.LogError($"Error while initializing orders: {exception}).");
		}
		finally
		{
			IsBusy = false;
		}
	}

	private OrderViewModel NewEmptyOrder()
	{
		var nextOrderNumber = Orders.Count > 0 ? Orders.Max(x => x.OrderNumber) + 1 : 1;
		var title = "New Order";

		var conversation = new Conversation(ConversationId.Empty, Chat.Empty, OrderStatus.Open, ConversationStatus.Started, new ConversationMetaData(title));

		var workflow = Workflow.Create(_wallet, conversation);

		var order = new OrderViewModel(UiContext, WalletVm.WalletModel, workflow, this, nextOrderNumber, _cts.Token);

		_ordersCache.AddOrUpdate(order);

		return order;
	}
}
