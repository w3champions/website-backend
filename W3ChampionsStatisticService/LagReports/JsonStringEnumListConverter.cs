using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace W3ChampionsStatisticService.LagReports;

public class JsonStringEnumListConverter<TEnum> : JsonConverter<List<TEnum>> where TEnum : struct, Enum
{
    public override List<TEnum> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<TEnum>();
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected start of array");
        }
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var value = reader.GetString();
            if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
            {
                list.Add(parsed);
            }
        }
        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<TEnum> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStringValue(item.ToString());
        }
        writer.WriteEndArray();
    }
}
