using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AI.Model.Services;
using AI.Services;
using DynamicData;
using ReactiveUI;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.ViewModels.AI.Messages;
using WalletWasabi.Logging;

namespace WalletWasabi.Fluent.ViewModels.AI;

public partial class ChatViewModel : ViewModelBase
{
	private readonly ReadOnlyObservableCollection<MessageViewModel> _messages;
	private readonly SourceList<MessageViewModel> _messagesList;
	private readonly IChatManager _chatManager;

	[AutoNotify] private string _title;
	[AutoNotify] private int _chatNumber;
	[AutoNotify] private bool _isBusy;
	[AutoNotify] private bool _isSelected;
	[AutoNotify] private UserMessageViewModel _currentUserMessage;

	private CancellationTokenSource _cts;

	private ChatGPT.ViewModels.Chat.ChatViewModel _chat;
	private IChatSerializer _chatSerializer;
	private IChatService _chatService;

	public ChatViewModel(UiContext uiContext, string title, int chatNumber, CancellationToken cancellationToken)
	{
		_title = title;

		_messagesList = new SourceList<MessageViewModel>();

		_messagesList
			.Connect()
			.Bind(out _messages)
			.Subscribe();

		UiContext = uiContext;
		ChatNumber = chatNumber;

		RemoveChatCommand = ReactiveCommand.CreateFromTask(RemoveChatAsync);

		ResetChatCommand = ReactiveCommand.Create(ResetChat);

		SendMessageCommand = ReactiveCommand.CreateFromTask(SendMessageAsync);

		_cts = new CancellationTokenSource();

		_currentUserMessage = new UserMessageViewModel("");

		InitializeChat();
	}

	public ReadOnlyObservableCollection<MessageViewModel> Messages => _messages;

	public ICommand RemoveChatCommand { get; }

	public ICommand ResetChatCommand { get; }

	public ICommand SendMessageCommand { get; set; }

	private void InitializeChat()
	{
		_chatSerializer = new SystemTextJsonChatSerializer();
		_chatService = new ChatService(_chatSerializer);

		_chat = new ChatGPT.ViewModels.Chat.ChatViewModel(
			_chatService,
			_chatSerializer,
			new ChatGPT.ViewModels.Chat.ChatSettingsViewModel
			{
				Temperature = 0.7m,
				MaxTokens = 8000,
				Model = "mistralai/Mistral-7B-Instruct-v0.2",
				ApiUrl = "https://enclave.blyss.dev/v1/chat/completions",
				// TODO: No api key for now
				ApiKey = null,
				Format = ChatGPT.Defaults.TextMessageFormat,
			});

		// TODO: No api key for now
		_chat.RequireApiKey = false;

		// TODO: System message hack
		_chat.AddUserMessage("You are a Wasabi Wallet application helpdesk assistant.");
		_chat.AddAssistantMessage("I'm Wasabi Wallet application helpdesk assistant, how can I help you.");
	}

	private async Task RemoveChatAsync()
	{
		var confirmed = await UiContext.Navigate().To().ConfirmDeleteChatDialog(this).GetResultAsync();

		if (confirmed)
		{
			_cts.Cancel();
			_cts.Dispose();
			_chatManager.RemoveChat(ChatNumber);
		}
	}

	private async Task ShowErrorAsync(string message)
	{
		await UiContext.Navigate().To().ShowErrorDialog(message, "Send Failed", "Wasabi was unable to send your message", NavigationTarget.CompactDialogScreen).GetResultAsync();
	}

	private async Task SendMessageAsync()
	{
		try
		{
			IsBusy = true;

			var input = _currentUserMessage.Message;

			_chat.AddUserMessage(input);

			var result = await _chat.SendAsync(_chat.CreateChatMessages(), _cts.Token);

			if (result?.Message is not null)
			{
				_chat.AddAssistantMessage(result.Message);

				AddMessage(_currentUserMessage);
				AddMessage(new AssistantMessageViewModel(result.Message));
			}

			CurrentUserMessage = new UserMessageViewModel("");
		}
		catch (Exception exception)
		{
			Logger.LogError($"Error while sending chat message: {exception}).");

			await ShowErrorAsync(exception.Message);
		}
		finally
		{
			IsBusy = false;
		}
	}

	private void ResetChat()
	{
		_cts.Cancel();
		_cts.Dispose();
		_cts = new CancellationTokenSource();
		ClearMessageList();
	}

	private void AddMessage(MessageViewModel messageViewModel)
	{
		_messagesList.Edit(x =>
		{
			x.Add(messageViewModel);
		});
	}

	private void ClearMessageList()
	{
		_messagesList.Edit(x => x.Clear());
	}
}
