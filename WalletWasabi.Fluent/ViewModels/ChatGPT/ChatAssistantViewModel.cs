using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AI.Model.Services;
using AI.Services;
using ChatGPT.Model.Services;
using ChatGPT.Services;
using ChatGPT.ViewModels.Chat;
using ChatGPT.ViewModels.Settings;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;

namespace WalletWasabi.Fluent.ViewModels.ChatGPT;

public partial class ChatAssistantViewModel : ReactiveObject
{
	static ChatAssistantViewModel()
	{
		ConfigureServices();
	}

	private string _initialDirections = """
You are a helpful assistant named Wasabito, you are Wasabi Wallet operator.
I will write text prompts and you will generate appropriate answers only in json format I have provided.

The json format for the answers is as follows:
{
  "status": "",
  "message": "",
}
The status and message properties are of type string.

The json response "status" property value can be one of the following:
- "command":  when wasabi api command is the answer
- "error": when not possible to answer or other problem
- "message": when answer is only text message but not error

When "status"="command" the "message" value can only be set to
 one of the following wasabi api commands available:
- send(address, amount): command requires address and amount, calls send method and displays send dialog
- address receive(): command returns only new BTC address, does not require any params, shows receive dialog with qr code
- value balance(): command returns BTC or USD balance value, does not require any params nor UI interaction
e.g.:
{
  "status": "command",
  "message": "send(address, amount)",
}

If user does not provide valid param to execute api command please set status=error and ask followup question to provide that info:
e.g.:
{
  "status": "error",
  "message": "Please provide valid address for send.",
}

If not enough info is provided to execute command please set status=error and ask user to provide missing information:
e.g.:
{
  "status": "error",
  "message": "Please provide valid address.",
}

If user ask question the answer please set status=message and ask user is in following format:
{
  "status": "message",
  "message": "The address used the following format...",
}

Never write answer as plain text (eg. I can not execute some api command etc.),
always write answers as json response with proper status.
""";

	private ChatViewModel _chat;
	private CancellationTokenSource _cts;

	[AutoNotify] private bool _isChatListVisible;
	[AutoNotify] private string? _inputText = "";

	public ChatAssistantViewModel()
	{
		_chat = new ChatViewModel
		{
			Settings = new ChatSettingsViewModel
			{
				Temperature = 0.7m,
				TopP = 1m,
				MaxTokens = 2000,
				Model = "gpt-3.5-turbo"
			}
		};

		_chat.AddSystemMessage(_initialDirections);

		SendCommand = ReactiveCommand.CreateFromTask(async () =>
		{
			var inputText = InputText;

			if (!string.IsNullOrWhiteSpace(inputText))
			{
				InputText = "";

				await SendAsync(inputText);
			}
		});

		Messages = new ObservableCollection<MessageViewModel>();
	}

	public ICommand? SendCommand { get; protected set; }

	public ObservableCollection<MessageViewModel> Messages { get; }

	private async Task SendAsync(string input)
	{
		try
		{
			Messages.Add(new UserMessageViewModel()
			{
				Message = input
			});

			_chat.AddUserMessage(input);

			_cts = new CancellationTokenSource();
			var result = await _chat.SendAsync(_chat.CreateChatMessages(), _cts.Token);

			_chat.AddAssistantMessage(result?.Message);

			Console.WriteLine(result?.Message);

			// TODO: Deserialize result json message and get message param and command.

			// TODO: AssistantResult

			var resultMessage = result?.Message;

			Messages.Add(new AssistantMessageViewModel
			{
				Message = resultMessage
			});
		}
		catch (Exception ex)
		{
			Console.WriteLine("Error: " + ex.Message);
		}
	}

	private static void ConfigureServices()
	{
		IServiceCollection serviceCollection = new ServiceCollection();

		serviceCollection.AddSingleton<IStorageFactory, IsolatedStorageFactory>();
		serviceCollection.AddSingleton<IChatService, ChatService>();

		serviceCollection.AddTransient<ChatMessageViewModel>();
		serviceCollection.AddTransient<ChatSettingsViewModel>();
		serviceCollection.AddTransient<ChatResultViewModel>();
		serviceCollection.AddTransient<ChatViewModel>();
		serviceCollection.AddTransient<PromptViewModel>();

		Ioc.Default.ConfigureServices(serviceCollection.BuildServiceProvider());
	}
}
