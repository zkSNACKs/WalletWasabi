using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatGPT.ViewModels.Layouts;

[JsonSerializable(typeof(WindowLayoutViewModel))]
public partial class WindowLayoutViewModelJsonContext : JsonSerializerContext
{
    public static readonly WindowLayoutViewModelJsonContext s_instance = new(
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
