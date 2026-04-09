using System;
using Newtonsoft.Json;

namespace Utils.JsonConverters;

public class BigIntStringConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(long) || 
               objectType == typeof(ulong) || 
               objectType == typeof(long?) || 
               objectType == typeof(ulong?);
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteValue(value.ToString());
        }
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }

        var value = reader.Value?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (objectType == typeof(long) || objectType == typeof(long?))
        {
            return long.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        if (objectType == typeof(ulong) || objectType == typeof(ulong?))
        {
            return ulong.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        return null;
    }
}
