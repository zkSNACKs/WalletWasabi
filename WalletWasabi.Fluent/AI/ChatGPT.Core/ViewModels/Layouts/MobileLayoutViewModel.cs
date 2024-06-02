using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ChatGPT.ViewModels.Layouts;

public partial class MobileLayoutViewModel : LayoutViewModel
{
    private bool _showMenu;

    [JsonConstructor]
    public MobileLayoutViewModel()
    {
        Name = "Mobile";

        ShowChats = false;
        ShowSettings = false;
        ShowPrompts = false;
        ShowMenu = false;
    }

    [JsonPropertyName("showMenu")]
    public bool ShowMenu
    {
        get => _showMenu;
        set => SetProperty(ref _showMenu, value);
    }

    public override async Task BackAsync()
    {
        HideMenusAction();
        await Task.Yield();
    }

    protected override void ShowSettingsAction()
    {
        if (ShowMenu)
        {
            HideMenusAction();
        }
        else
        {
            ShowSettings = true;
            ShowChats = false;
            ShowPrompts = false;
            ShowMenu = true;
        }
    }

    protected override void ShowChatsAction()
    {
        if (ShowMenu)
        {
            HideMenusAction();
        }
        else
        {
            ShowChats = true;
            ShowSettings = false;
            ShowPrompts = false;
            ShowMenu = true;
        }
    }

    protected override void ShowPromptsAction()
    {
        if (ShowMenu)
        {
            HideMenusAction();
        }
        else
        {
            ShowPrompts = true;
            ShowChats = false;
            ShowSettings = false;
            ShowMenu = true;
        }
    }

    private void HideMenusAction()
    {
        ShowSettings = false;
        ShowChats = false;
        ShowPrompts = false;
        ShowMenu = false;
    }

    public override LayoutViewModel Copy()
    {
        return new MobileLayoutViewModel()
        {
            ShowChats = ShowChats,
            ShowSettings = ShowSettings,
            ShowPrompts = ShowPrompts,
            ShowMenu = _showMenu
        };
    }
}
