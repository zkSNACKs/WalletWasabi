using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using DynamicData;
using DynamicData.Aggregation;
using ReactiveUI;
using WalletWasabi.BuyAnything;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.ViewModels.Wallets.Buy.Messages;
using WalletWasabi.Fluent.ViewModels.Wallets.Buy.Workflows;
using WalletWasabi.Logging;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Buy;

public partial class OrderViewModel : ViewModelBase, IDisposable
{
	private readonly ReadOnlyObservableCollection<MessageViewModel> _messages;
	private readonly SourceList<MessageViewModel> _messagesList;
	private readonly IWalletModel _wallet;
	private readonly IOrderManager _orderManager;

	[AutoNotify] private string _title;
	[AutoNotify] private string? _sibId = "New Order";
	[AutoNotify] private bool _isBusy;
	[AutoNotify] private bool _isCompleted;
	[AutoNotify] private bool _hasUnreadMessages;

	private CancellationTokenSource _cts;
	private readonly CompositeDisposable _disposables = new();

	public OrderViewModel(UiContext uiContext, IWalletModel wallet, Conversation conversation, IOrderManager orderManager, int orderNumber)
	{
		UiContext = uiContext;
		Workflow = wallet.BuyAnything.CreateWorkflow(conversation).DisposeWith(_disposables);

		_wallet = wallet;
		_orderManager = orderManager;
		_title = Workflow.Conversation.MetaData.Title;

		_messagesList = new SourceList<MessageViewModel>()
			.DisposeWith(_disposables);

		_messagesList
			.Connect()
			.Bind(out _messages)
			.Subscribe()
			.DisposeWith(_disposables);

		OrderNumber = orderNumber;

		HasUnreadMessagesObs = _messagesList.Connect()
											.AutoRefresh(x => x.IsUnread)
											.Filter(x => x.IsUnread is true)
											.Count()
											.Select(i => i > 0);

		CanRemoveObs = this.WhenAnyValue(x => x.Workflow.Conversation.Id)
						   .Select(id => id != ConversationId.Empty);

		RemoveOrderCommand = ReactiveCommand.CreateFromTask(RemoveOrderAsync, CanRemoveObs);

		var hasUserMessages =
			_messagesList.CountChanged.Select(_ => _messagesList.Items.Any(x => x is UserMessageViewModel));

		CanResetObs =
			 this.WhenAnyValue(x => x.Workflow.Conversation.Id)
				 .Select(id => id == ConversationId.Empty)
				 .CombineLatest(hasUserMessages, (a, b) => a && b);

		ResetOrderCommand = ReactiveCommand.Create(ResetOrder, CanResetObs);

		// TODO: Remove this once we use newer version of DynamicData
		HasUnreadMessagesObs
			.BindTo(this, x => x.HasUnreadMessages)
			.DisposeWith(_disposables);

		this.WhenAnyValue(x => x.Workflow.Conversation)
			.Do(conversation =>
			{
				Title = conversation.MetaData.Title;
				SibId = conversation.Id.OrderNumber == "" ? null : conversation.Id.OrderNumber;
				RefreshMessageList(conversation);
			})
			.Subscribe()
			.DisposeWith(_disposables);

		this.WhenAnyValue(x => x.Workflow.CurrentStep.IsBusy)
			.BindTo(this, x => x.IsBusy)
			.DisposeWith(_disposables);

		this.WhenAnyValue(x => x.Workflow.IsCompleted)
			.BindTo(this, x => x.IsCompleted)
			.DisposeWith(_disposables);

		_cts = new CancellationTokenSource()
			.DisposeWith(_disposables);

		// Handle Workflow Step Execution Errors and show UI message
		Observable
			.FromEventPattern<Exception>(Workflow, nameof(Workflow.OnStepError))
			.DoAsync(async e => await _orderManager.OnError(e.EventArgs))
			.Subscribe()
			.DisposeWith(_disposables);

		StartWorkflow(_cts.Token);
	}

	public Workflow Workflow { get; }

	public ConversationId ConversationId => Workflow.Conversation.Id;

	public IObservable<bool> HasUnreadMessagesObs { get; }

	public IObservable<bool> CanRemoveObs { get; }

	public IObservable<bool> CanResetObs { get; }

	public ReadOnlyObservableCollection<MessageViewModel> Messages => _messages;

	public ICommand RemoveOrderCommand { get; }

	public ICommand ResetOrderCommand { get; }

	public int OrderNumber { get; }

	public async Task MarkAsReadAsync()
	{
		if (ConversationId == ConversationId.Empty)
		{
			foreach (var message in Messages)
			{
				message.IsUnread = false;
			}
		}
		else
		{
			await Workflow.MarkConversationAsReadAsync(_cts.Token);
		}
	}

	private async Task RemoveOrderAsync()
	{
		var confirmed = await UiContext.Navigate().To().ConfirmDeleteOrderDialog(this).GetResultAsync();

		if (confirmed)
		{
			_cts.Cancel();
			_cts.Dispose();
			await _orderManager.RemoveOrderAsync(OrderNumber);
		}
	}

	private async Task ShowErrorAsync(string message)
	{
		await UiContext.Navigate().To().ShowErrorDialog(message, "Send Failed", "Wasabi was unable to send your message", NavigationTarget.CompactDialogScreen).GetResultAsync();
	}

	private void ResetOrder()
	{
		_cts.Cancel();
		_cts.Dispose();
		_cts = new CancellationTokenSource();
		ClearMessageList();

		Workflow.Conversation = new Conversation(ConversationId.Empty, Chat.Empty, OrderStatus.Open, ConversationStatus.Started, new ConversationMetaData("New Order"));
		StartWorkflow(_cts.Token);
	}

	private void RefreshMessageList(Conversation conversation)
	{
		var messages =
			conversation.ChatMessages
						.Select(CreateMessageViewModel)
						.ToArray();

		_messagesList.Edit(x =>
		{
			x.Clear();
			x.Add(messages);
		});
	}

	private void ClearMessageList() => _messagesList.Edit(x => x.Clear());

	private MessageViewModel CreateMessageViewModel(ChatMessage message)
	{
		if (message.IsMyMessage)
		{
			return new UserMessageViewModel(Workflow, message)
			{
				OriginalText = message.Text,
				IsUnread = message.IsUnread
			};
		}

		return
			message.Data switch
			{
				OfferCarrier => new OfferMessageViewModel(message),
				Invoice => new PayNowAssistantMessageViewModel(UiContext, _wallet, Workflow, message),
				AttachmentLinks => new UrlListMessageViewModel(message, "Download your files:"),
				TrackingCodes => new UrlListMessageViewModel(message, "For shipping updates:"),
				_ => new AssistantMessageViewModel(message)
			};
	}

	/// <summary>
	/// Fire and Forget method to start the workflow, and listen to any exceptions
	/// </summary>
	private void StartWorkflow(CancellationToken token)
	{
		RxApp.MainThreadScheduler.ScheduleAsync(async (_, _) =>
		{
			try
			{
				await Workflow.ExecuteAsync(token);
			}
			catch (OperationCanceledException ex)
			{
				Logger.LogDebug(ex.Message);
			}
			catch (Exception ex)
			{
				await ShowErrorAsync("Error while processing order.");
				Logger.LogError($"Error while processing order: {ex}).");
			}
		});
	}

	public void Dispose()
	{
		Workflow.Dispose();
		_disposables.Dispose();
	}
}
