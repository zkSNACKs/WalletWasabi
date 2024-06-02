using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AI.Model.Json.Completions;
using AI.Model.Services;

namespace AI.Services;

public class SystemTextJsonCompletionsSerializer : ICompletionsSerializer
{
    private static readonly CompletionsJsonContext s_serializerContext;

    static SystemTextJsonCompletionsSerializer()
    {
        s_serializerContext = new(
            new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                IgnoreReadOnlyProperties = true
            });
    }

    public string Serialize<T>(T value)
    {
        var typeInfo = (JsonTypeInfo<T>)s_serializerContext.GetTypeInfo(typeof(T));
        return JsonSerializer.Serialize(value, typeInfo);
    }

    public T? Deserialize<T>(string json)
    {
        var typeInfo = (JsonTypeInfo<T?>)s_serializerContext.GetTypeInfo(typeof(T));
        return JsonSerializer.Deserialize(json, typeInfo);
    }
}
