using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AI.Model.Json.ChatGPT;
using ChatGPT.Model.Services;
using ChatGPT.ViewModels.Chat;
using CommunityToolkit.Mvvm.Input;

namespace ChatGPT.ViewModels;

public partial class MainViewModel
{
    private ObservableCollection<ChatViewModel> _chats;
    private ChatViewModel? _currentChat;

    [JsonPropertyName("chats")]
    public ObservableCollection<ChatViewModel> Chats
    {
        get => _chats;
        set => SetProperty(ref _chats, value);
    }

    [JsonPropertyName("currentChat")]
    public ChatViewModel? CurrentChat
    {
        get => _currentChat;
        set => SetProperty(ref _currentChat, value);
    }

    [JsonIgnore]
    public IAsyncRelayCommand AddChatCommand { get; }

    [JsonIgnore]
    public IAsyncRelayCommand DeleteChatCommand { get; }

    [JsonIgnore]
    public IAsyncRelayCommand OpenChatCommand { get; }

    [JsonIgnore]
    public IAsyncRelayCommand SaveChatCommand { get; }

    [JsonIgnore]
    public IAsyncRelayCommand ExportChatCommand { get; }

    [JsonIgnore]
    public IAsyncRelayCommand CopyChatCommand { get; }

    [JsonIgnore]
    public IRelayCommand DefaultChatSettingsCommand { get; }

    [JsonIgnore]
    public IAsyncRelayCommand ImportGtpChatsCommand { get; }

    private ChatSettingsViewModel CreateDefaultChatSettings()
    {
        return new ChatSettingsViewModel
        {
            Temperature = Defaults.DefaultTemperature,
            TopP = Defaults.DefaultTopP,
            PresencePenalty = Defaults.DefaultPresencePenalty,
            FrequencyPenalty = Defaults.DefaultFrequencyPenalty,
            MaxTokens = Defaults.DefaultMaxTokens,
            Model = Defaults.DefaultModel,
            ApiKey = null,
            Directions = Defaults.DefaultDirections,
            Format = Defaults.MarkdownMessageFormat,
        };
    }

    private async Task AddChatActionAsync()
    {
        NewChatCallback();
        await Task.Yield();
    }

    private async Task DeleteChatActionAsync()
    {
        DeleteChatCallback();
        await Task.Yield();
    }

    private async Task OpenChatActionAsync()
    {
        var app = Defaults.Locator.GetService<IApplicationService>();
        if (app is { })
        {
            await app.OpenFileAsync(
                OpenChatCallbackAsync, 
                new List<string>(new[] { "Json", "All" }), 
                "Open");
        }
    }

    private async Task SaveChatActionAsync()
    {
        var app = Defaults.Locator.GetService<IApplicationService>();
        if (app is { } && CurrentChat is { })
        {
            await app.SaveFileAsync(
                SaveChatCallbackAsync, 
                new List<string>(new[] { "Json", "All" }), 
                "Save", 
                CurrentChat.Name ?? "chat", 
                "json");
        }
    }

    private async Task ExportChatActionAsync()
    {
        var app = Defaults.Locator.GetService<IApplicationService>();
        if (app is { } && CurrentChat is { })
        {
            await app.SaveFileAsync(
                ExportChatCallbackAsync, 
                new List<string>(new[] { "Text", "All" }), 
                "Export", 
                CurrentChat.Name ?? "chat",
                "txt");
        }
    }

    private async Task CopyChatActionAsync()
    {
        var app = Defaults.Locator.GetService<IApplicationService>();
        if (app is { } && CurrentChat is { })
        {
            var sb = new StringBuilder();
#if NETFRAMEWORK
            using var writer = new StringWriter(sb);
#else
            await using var writer = new StringWriter(sb);
#endif
            await ExportChatAsync(CurrentChat, writer);
            await app.SetClipboardTextAsync(sb.ToString());
        }
    }

    private void DefaultChatSettingsAction()
    {
        if (CurrentChat is { } chat)
        {
            var apiKey = chat.Settings?.ApiKey;

            chat.Settings = CreateDefaultChatSettings();

            if (apiKey is { })
            {
                chat.Settings.ApiKey = apiKey;
            }
        }
    }

    private async Task ImportGptChatsActionAsync()
    {
        var app = Defaults.Locator.GetService<IApplicationService>();
        if (app is { })
        {
            await app.OpenFileAsync(
                ImportGptChatsCallbackAsync, 
                new List<string>(new[] { "Json", "All" }), 
                "Import");
        }
    }
    
    private void NewChatCallback()
    {
        var chat = new ChatViewModel
        {
            Name = "Chat",
            Settings = CurrentChat?.Settings?.Copy() ?? CreateDefaultChatSettings()
        };

        var welcomeItem = new ChatMessageViewModel
        {
            Role = "system",
            Message = Defaults.WelcomeMessage,
            Format = Defaults.TextMessageFormat,
            IsSent = true,
            CanRemove = false
        };
        chat.SetMessageActions(welcomeItem);
        chat.Messages.Add(welcomeItem);

        var promptItem = new ChatMessageViewModel
        {
            Role = "user",
            Message = "",
            Format = Defaults.TextMessageFormat,
            IsSent = false,
            CanRemove = false
        };
        chat.SetMessageActions(promptItem);
        chat.Messages.Add(promptItem);

        chat.CurrentMessage = promptItem;

        Chats.Add(chat);
        CurrentChat = chat;
    }

    private void DeleteChatCallback()
    {
        if (CurrentChat is { })
        {
            Chats.Remove(CurrentChat);
            CurrentChat = Chats.LastOrDefault();
        }
    }

    private async Task OpenChatCallbackAsync(Stream stream)
    {
        var chat = await JsonSerializer.DeserializeAsync(
            stream, 
            MainViewModelJsonContext.s_instance.ChatViewModel);
        if (chat is { })
        {
            foreach (var message in chat.Messages)
            {
                chat.SetMessageActions(message);
            }

            Chats.Add(chat);
            CurrentChat = chat;
        }
    }

    private async Task SaveChatCallbackAsync(Stream stream)
    {
        if (CurrentChat is null)
        {
            return;
        }

        await JsonSerializer.SerializeAsync(
            stream, 
            CurrentChat, 
            MainViewModelJsonContext.s_instance.ChatViewModel);
    }

    private async Task ExportChatCallbackAsync(Stream stream)
    {
        if (CurrentChat is null)
        {
            return;
        }
#if NETFRAMEWORK
        using var writer = new StreamWriter(stream);
#else
        await using var writer = new StreamWriter(stream);
#endif
        await ExportChatAsync(CurrentChat, writer);
    }

    private async Task ImportGptChatsCallbackAsync(Stream stream)
    {
        var gptChats = await JsonSerializer.DeserializeAsync(
            stream, 
            ChatGptJsonContext.s_instance.ChatGptArray);
        if (gptChats is { })
        {
            await Task.Run(() => ImportChats(gptChats));
        }
    }

    private async Task ExportChatAsync(ChatViewModel chat, TextWriter writer)
    {
        for (var i = 0; i < chat.Messages.Count; i++)
        {
            var message = chat.Messages[i];

            if (i == 0)
            {
                var content = chat.Settings?.Directions;

                if (message.Message != Defaults.WelcomeMessage)
                {
                    content = message.Message;
                }

                if (content is { })
                {
                    await writer.WriteLineAsync($"{message.Role}:");
                    await writer.WriteLineAsync("");

                    await writer.WriteLineAsync(content);
                    await writer.WriteLineAsync("");
                }

                continue;
            }

            if (!string.IsNullOrEmpty(message.Message))
            {
                await writer.WriteLineAsync($"{message.Role}:");
                await writer.WriteLineAsync("");

                await writer.WriteLineAsync(message.Message);
                await writer.WriteLineAsync("");
            }
        }
    }
}
