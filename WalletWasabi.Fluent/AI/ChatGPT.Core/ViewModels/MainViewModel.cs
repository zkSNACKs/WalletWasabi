using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ChatGPT.Model.Plugins;
using ChatGPT.Model.Services;
using ChatGPT.ViewModels.Chat;
using ChatGPT.ViewModels.Layouts;
using ChatGPT.ViewModels.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChatGPT.ViewModels;

public partial class MainViewModel : ObservableObject, IPluginContext
{
    public MainViewModel()
    {
        _chats = new ObservableCollection<ChatViewModel>();
        _prompts = new ObservableCollection<PromptViewModel>();

        MobileLayout = new MobileLayoutViewModel();

        DesktopLayout = new DesktopLayoutViewModel();

        _layouts = new ObservableCollection<LayoutViewModel>
        {
            MobileLayout,
            DesktopLayout
        };

        CurrentLayout = MobileLayout;

        Topmost = true;

        InitPromptCallback();

        NewChatCallback();

        // Chats
        
        AddChatCommand = new AsyncRelayCommand(AddChatActionAsync);

        DeleteChatCommand = new AsyncRelayCommand(DeleteChatActionAsync);

        OpenChatCommand = new AsyncRelayCommand(OpenChatActionAsync);

        SaveChatCommand = new AsyncRelayCommand(SaveChatActionAsync);

        ExportChatCommand = new AsyncRelayCommand(ExportChatActionAsync);

        CopyChatCommand = new AsyncRelayCommand(CopyChatActionAsync);

        DefaultChatSettingsCommand = new RelayCommand(DefaultChatSettingsAction);

        ImportGtpChatsCommand = new AsyncRelayCommand(ImportGptChatsActionAsync);

        // Prompts

        AddPromptCommand = new AsyncRelayCommand(AddPromptAction);

        DeletePromptCommand = new AsyncRelayCommand(DeletePromptAction);

        OpenPromptsCommand = new AsyncRelayCommand(OpenPromptsAction);

        SavePromptsCommand = new AsyncRelayCommand(SavePromptsAction);

        ImportPromptsCommand = new AsyncRelayCommand(ImportPromptsAction);

        CopyPromptCommand = new AsyncRelayCommand(CopyPromptAction);

        SetPromptCommand = new AsyncRelayCommand(SetPromptAction);

        // Workspace

        LoadWorkspaceCommand = new AsyncRelayCommand(LoadWorkspaceAction);

        SaveWorkspaceCommand = new AsyncRelayCommand(SaveWorkspaceAction);

        ExportWorkspaceCommand = new AsyncRelayCommand(ExportWorkspaceAction);

        // Actions
    
        ExitCommand = new RelayCommand(ExitAction);

        ChangeThemeCommand = new RelayCommand(ChangeThemeAction);

        ChangeDesktopMobileCommand = new RelayCommand(ChangeDesktopMobileAction);

        ChangeTopmostCommand = new RelayCommand(ChangeTopmostAction);
    }

    private async Task LoadWorkspaceAction()
    {
        var app = Defaults.Locator.GetService<IApplicationService>();
        if (app is { })
        {
            await app.OpenFileAsync(
                LoadWorkspaceCallbackAsync, 
                new List<string>(new[] { "Json", "All" }), 
                "Open");
        }
    }

    private async Task SaveWorkspaceAction()
    {
        await SaveSettingsAsync();
    }

    private async Task ExportWorkspaceAction()
    {
        var app = Defaults.Locator.GetService<IApplicationService>();
        if (app is { } && CurrentChat is { })
        {
            await app.SaveFileAsync(
                ExportWorkspaceCallbackAsync, 
                new List<string>(new[] { "Json", "All" }), 
                "Export", 
                "workspace",
                "json");
        }
    }

    private async Task LoadWorkspaceCallbackAsync(Stream stream)
    {
        var workspace = await JsonSerializer.DeserializeAsync(
            stream, 
            MainViewModelJsonContext.s_instance.WorkspaceViewModel);
        if (workspace is { })
        {
            LoadWorkspace(workspace);
        }
    }

    private async Task ExportWorkspaceCallbackAsync(Stream stream)
    {
        var workspace = CreateWorkspace();
        await JsonSerializer.SerializeAsync(
            stream, 
            workspace, 
            MainViewModelJsonContext.s_instance.WorkspaceViewModel);
    }

    private WorkspaceViewModel CreateWorkspace()
    {
        var workspace = new WorkspaceViewModel
        {
            Chats = Chats,
            CurrentChat = CurrentChat,
            Prompts = Prompts,
            CurrentPrompt = CurrentPrompt,
            Layouts = Layouts,
            CurrentLayout = CurrentLayout,
            Theme = Theme,
            Topmost = Topmost,
        };
        return workspace;
    }

    private void LoadWorkspace(WorkspaceViewModel workspace)
    {
        if (workspace.Chats is { })
        {
            foreach (var chat in workspace.Chats)
            {
                foreach (var message in chat.Messages)
                {
                    chat.SetMessageActions(message);
                }
            }

            Chats = workspace.Chats;
            CurrentChat = workspace.CurrentChat;
        }

        if (workspace.Prompts is { })
        {
            Prompts = workspace.Prompts;
            CurrentPrompt = workspace.CurrentPrompt;
        }

        if (workspace.Layouts is { })
        {
            Layouts = workspace.Layouts;
            MobileLayout = Layouts.OfType<MobileLayoutViewModel>().FirstOrDefault();
            DesktopLayout = Layouts.OfType<DesktopLayoutViewModel>().FirstOrDefault();
            CurrentLayout = workspace.CurrentLayout;
        }

        if (workspace.Theme is { })
        {
            Theme = workspace.Theme;
        }

        Topmost = workspace.Topmost;
    }

    public async Task LoadSettingsAsync()
    {
        var factory = Defaults.Locator.GetService<IStorageFactory>();
        var storage = factory?.CreateStorageService<WorkspaceViewModel>();
        if (storage is null)
        {
            return;
        }
        var workspace = await storage.LoadObjectAsync(
            "Settings",
            MainViewModelJsonContext.s_instance.WorkspaceViewModel);
        if (workspace is { })
        {
            LoadWorkspace(workspace);
        }
    }

    public async Task SaveSettingsAsync()
    {
        var workspace = CreateWorkspace();
        var factory = Defaults.Locator.GetService<IStorageFactory>();
        var storage = factory?.CreateStorageService<WorkspaceViewModel>();
        if (storage is { })
        {
            await storage.SaveObjectAsync(
                workspace, 
                "Settings", 
                MainViewModelJsonContext.s_instance.WorkspaceViewModel);
        }
    }

    public void LoadSettings()
    {
        var factory = Defaults.Locator.GetService<IStorageFactory>();
        var storage = factory?.CreateStorageService<WorkspaceViewModel>();
        if (storage is null)
        {
            return;
        }
        var workspace = storage.LoadObject(
            "Settings", 
            MainViewModelJsonContext.s_instance.WorkspaceViewModel);
        if (workspace is { })
        {
            LoadWorkspace(workspace);
        }
    }

    public void SaveSettings()
    {
        var workspace = CreateWorkspace();
        var factory = Defaults.Locator.GetService<IStorageFactory>();
        var storage = factory?.CreateStorageService<WorkspaceViewModel>();
        if (storage is { })
        {
            storage.SaveObject(
                workspace, 
                "Settings", 
                MainViewModelJsonContext.s_instance.WorkspaceViewModel);
        }
    }
}
