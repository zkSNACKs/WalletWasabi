using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ChatGPT.Model.Services;
using ChatGPT.ViewModels.Settings;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualBasic.FileIO;

namespace ChatGPT.ViewModels;

public partial class MainViewModel
{
    private static readonly PromptViewModel[] s_defaultPrompts =
    {
        new()
        {
            Act = "Assistant",
            Prompt = Defaults.DefaultDirections
        },
        new()
        {
            Act = "English Translator and Improver",
            Prompt = "I want you to act as an English translator, spelling corrector and improver. I will speak to you in any language and you will detect the language, translate it and answer in the corrected and improved version of my text, in English. I want you to replace my simplified A0-level words and sentences with more beautiful and elegant, upper level English words and sentences. Keep the meaning same, but make them more literary. I want you to only reply the correction, the improvements and nothing else, do not write explanations."
        },
        new()
        {
            Act = "UX/UI Developer",
            Prompt = "I want you to act as a UX/UI developer. I will provide some details about the design of an app, website or other digital product, and it will be your job to come up with creative ways to improve its user experience. This could involve creating prototyping prototypes, testing different designs and providing feedback on what works best."
        },
        new()
        {
            Act = "Tech Writer",
            Prompt = "I want you to act as a tech writer. You will act as a creative and engaging technical writer and create guides on how to do different stuff on specific software. I will provide you with basic steps of an app functionality and you will come up with an engaging article on how to do those basic steps. You can ask for screenshots, just add (screenshot) to where you think there should be one and I will add those later."
        },
    };

    private ObservableCollection<PromptViewModel> _prompts;
    private PromptViewModel? _currentPrompt;

    [JsonPropertyName("prompts")]
    public ObservableCollection<PromptViewModel> Prompts
    {
        get => _prompts;
        set => SetProperty(ref _prompts, value);
    }

    [JsonPropertyName("currentPrompt")]
    public PromptViewModel? CurrentPrompt
    {
        get => _currentPrompt;
        set => SetProperty(ref _currentPrompt, value);
    }

    [JsonIgnore]
    public IAsyncRelayCommand AddPromptCommand { get; }

    [JsonIgnore]
    public IAsyncRelayCommand DeletePromptCommand { get; }

    [JsonIgnore]
    public IAsyncRelayCommand OpenPromptsCommand { get; }

    [JsonIgnore]
    public IAsyncRelayCommand SavePromptsCommand { get; }

    [JsonIgnore]
    public IAsyncRelayCommand ImportPromptsCommand { get; }

    [JsonIgnore]
    public IAsyncRelayCommand CopyPromptCommand { get; }

    [JsonIgnore]
    public IAsyncRelayCommand SetPromptCommand { get; }

    private async Task AddPromptAction()
    {
        NewPromptCallback();
        await Task.Yield();
    }

    private async Task DeletePromptAction()
    {
        DeletePromptCallback();
        await Task.Yield();
    }

    private async Task OpenPromptsAction()
    {
        var app = Defaults.Locator.GetService<IApplicationService>();
        if (app is { })
        {
            await app.OpenFileAsync(
                OpenPromptsCallbackAsync, 
                new List<string>(new[] { "Json", "All" }), 
                "Open");
        }
    }

    private async Task SavePromptsAction()
    {
        var app = Defaults.Locator.GetService<IApplicationService>();
        if (app is { } && CurrentChat is { })
        {
            await app.SaveFileAsync(
                SavePromptsCallbackAsync, 
                new List<string>(new[] { "Json", "All" }), 
                "Save", 
                "prompts", 
                "json");
        }
    }

    private async Task ImportPromptsAction()
    {
        var app = Defaults.Locator.GetService<IApplicationService>();
        if (app is { })
        {
            await app.OpenFileAsync(
                ImportPromptsCallbackAsync, 
                new List<string>(new[] { "Csv", "All" }), 
                "Import");
        }
    }

    private async Task CopyPromptAction()
    {
        var app = Defaults.Locator.GetService<IApplicationService>();
        if (app is { } && CurrentPrompt?.Prompt is { })
        {
            await app.SetClipboardTextAsync(CurrentPrompt.Prompt);
        }
    }

    private async Task SetPromptAction()
    {
        SetPromptCallback();

        if (CurrentLayout is { })
        {
            await CurrentLayout.BackAsync();
        }

        await Task.Yield();
    }

    private void InitPromptCallback()
    {
        foreach (var prompt in s_defaultPrompts)
        {
            _prompts.Add(prompt);
        }

        CurrentPrompt = _prompts.FirstOrDefault();
    }

    private void NewPromptCallback()
    {
        var prompt = new PromptViewModel
        {
            Act = "Assistant",
            Prompt = Defaults.DefaultDirections
        };
        _prompts.Add(prompt);

        CurrentPrompt = prompt;
    }

    private void DeletePromptCallback()
    {
        if (CurrentPrompt is { })
        {
            Prompts.Remove(CurrentPrompt);
            CurrentPrompt = Prompts.LastOrDefault();
        }
    }

    private async Task OpenPromptsCallbackAsync(Stream stream)
    {
        var prompts = await JsonSerializer.DeserializeAsync(
            stream, 
            MainViewModelJsonContext.s_instance.ObservableCollectionPromptViewModel);
        if (prompts is { })
        {
            foreach (var prompt in prompts)
            {
                Prompts.Add(prompt);
            }
        }
    }

    private async Task SavePromptsCallbackAsync(Stream stream)
    {
        await JsonSerializer.SerializeAsync(
            stream, 
            Prompts,
            MainViewModelJsonContext.s_instance.ObservableCollectionPromptViewModel);
    }

    private async Task ImportPromptsCallbackAsync(Stream stream)
    {
        using var streamReader = new StreamReader(stream);
        var csv = await streamReader.ReadToEndAsync();
        using var stringReader = new StringReader(csv);
        using var parser = new TextFieldParser(stringReader);

        parser.HasFieldsEnclosedInQuotes = true;
        parser.Delimiters = new[] { "," };

        var haveHeader = false;

        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields is { } && fields.Length == 2)
            {
                if (haveHeader)
                {
                    var prompt = new PromptViewModel
                    {
                        Act = fields[0],
                        Prompt = fields[1]
                    };
                    Prompts.Add(prompt);
                }
                else
                {
                    // skip
                    haveHeader = true;
                }
            }
        }

        await Task.Yield();
    }

    private void SetPromptCallback()
    {
        if (CurrentPrompt is { } && CurrentChat?.Settings is { })
        {
            CurrentChat.Settings.Directions = CurrentPrompt.Prompt;
        }
    }
}
