using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace AutoGenerate.Shared.Converters;

public class DateTimeUtcConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        => reader.GetString() is string s
            ? DateTime.Parse(s, null, DateTimeStyles.RoundtripKind)
            : null;

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions o)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
        else
            writer.WriteNullValue();
    }
}