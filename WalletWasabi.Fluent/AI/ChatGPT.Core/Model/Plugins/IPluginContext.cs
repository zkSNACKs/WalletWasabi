using System.Collections.ObjectModel;
using ChatGPT.ViewModels.Chat;

namespace ChatGPT.Model.Plugins;

public interface IPluginContext
{
    ObservableCollection<ChatViewModel> Chats { get; set; }
    ChatViewModel? CurrentChat { get; set; }
}
