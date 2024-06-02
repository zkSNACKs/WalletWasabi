using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading;
using System.Threading.Tasks;
using DynamicData;
using DynamicData.Binding;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Logging;

namespace WalletWasabi.Fluent.ViewModels.AI;

[NavigationMetaData(
	Title = "Blyss Confidential AI",
	Caption = "Display Blyss Confidential AI dialog",
	IconName = "chat",
	Order = 8,
	Category = "General",
	Keywords = new[] { "AI", "Chat", "ChatGPT", "Confidential", "Blyss" },
	NavBarPosition = NavBarPosition.None,
	NavigationTarget = NavigationTarget.DialogScreen,
	Searchable = true)]
public partial class ChatsViewModel : RoutableViewModel, IChatManager
{
	private readonly CancellationTokenSource _cts;
	private readonly ReadOnlyObservableCollection<ChatViewModel> _chats;
	private readonly SourceCache<ChatViewModel, int> _chatsCache;

	[AutoNotify] private ChatViewModel? _selectedChat;

	public ChatsViewModel(UiContext uiContext)
	{
		IsBusy = true;
		UiContext = uiContext;

		SetupCancel(enableCancel: true, enableCancelOnEscape: true, enableCancelOnPressed: true);

		EnableBack = false;

		_chatsCache = new SourceCache<ChatViewModel, int>(x => x.ChatNumber);

		_chatsCache
			.Connect()
			.Sort(SortExpressionComparer<ChatViewModel>.Descending(x => x.ChatNumber))
			.Bind(out _chats)
			.Subscribe();

		_cts = new CancellationTokenSource();

		Activate();
	}

	public ReadOnlyObservableCollection<ChatViewModel> Chats => _chats;

	void IChatManager.RemoveChat(int id)
	{
		_chatsCache.RemoveKey(id);
		SelectedChat = _chats.FirstOrDefault();
	}

	public void Activate()
	{
		Task.Run(async () => await InitializeAsync(), _cts.Token);
	}

	protected override void OnNavigatedTo(bool inHistory, CompositeDisposable disposables)
	{
		base.OnNavigatedTo(inHistory, disposables);

		SelectNewChatIfAny();
	}

	private void SelectNewChatIfAny()
	{
		SelectedChat = NewEmptyChat();
	}

	private async Task InitializeAsync()
	{
		try
		{
			// TODO:
			await Task.Yield();
		}
		catch (Exception exception)
		{
			Logger.LogError($"Error while initializing chats: {exception}).");
		}
		finally
		{
			IsBusy = false;
		}
	}

	private ChatViewModel NewEmptyChat()
	{
		var nextChatNumber = Chats.Count > 0 ? Chats.Max(x => x.ChatNumber) + 1 : 1;
		var title = "New Chat";
		var chat = new ChatViewModel(UiContext, title, nextChatNumber, _cts.Token);

		_chatsCache.AddOrUpdate(chat);

		return chat;
	}
}
