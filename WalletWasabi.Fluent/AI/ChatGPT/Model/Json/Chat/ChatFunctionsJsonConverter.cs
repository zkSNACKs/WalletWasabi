using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI.Model.Json.Chat;

internal class ChatFunctionsJsonConverter : JsonConverter<object>
{
    public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
            writer.WriteRawValue(json);
        }
    }
}
