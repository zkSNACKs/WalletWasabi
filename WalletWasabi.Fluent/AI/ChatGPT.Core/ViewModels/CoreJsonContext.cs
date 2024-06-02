using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChatGPT.ViewModels.Chat;
using ChatGPT.ViewModels.Layouts;
using ChatGPT.ViewModels.Settings;

namespace ChatGPT.ViewModels;

[JsonSerializable(typeof(ChatMessageViewModel))]
[JsonSerializable(typeof(ChatViewModel))]
[JsonSerializable(typeof(ChatFunctionCallViewModel))]
[JsonSerializable(typeof(ChatFunctionViewModel))]
[JsonSerializable(typeof(ChatMessageFunctionCallViewModel))]
[JsonSerializable(typeof(ObservableCollection<ChatViewModel>))]
[JsonSerializable(typeof(ChatSettingsViewModel))]
[JsonSerializable(typeof(ObservableCollection<ChatMessageViewModel>))]
[JsonSerializable(typeof(PromptViewModel))]
[JsonSerializable(typeof(ObservableCollection<PromptViewModel>))]
[JsonSerializable(typeof(LayoutViewModel))]
[JsonSerializable(typeof(MobileLayoutViewModel))]
[JsonSerializable(typeof(DesktopLayoutViewModel))]
[JsonSerializable(typeof(ObservableCollection<LayoutViewModel>))]
[JsonSerializable(typeof(WorkspaceViewModel))]
public partial class CoreJsonContext : JsonSerializerContext
{
    public static readonly CoreJsonContext s_instance = new(
        new JsonSerializerOptions
        {
            WriteIndented = true,
            ReferenceHandler = ReferenceHandler.Preserve,
            IncludeFields = false,
            IgnoreReadOnlyFields = true,
            IgnoreReadOnlyProperties = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        });
}
