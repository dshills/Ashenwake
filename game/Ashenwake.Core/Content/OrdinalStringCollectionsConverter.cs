using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ashenwake.Core.Content;

/// <summary>JSON round trips must retain deterministic ordering instead of replacing
/// sorted collection comparers with the operating system's current culture.</summary>
internal sealed class OrdinalStringCollectionsConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(SortedSet<string>) ||
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(SortedDictionary<,>) &&
        typeToConvert.GetGenericArguments()[0] == typeof(string);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => typeToConvert == typeof(SortedSet<string>) ? new StringSetConverter() :
            (JsonConverter)Activator.CreateInstance(typeof(StringDictionaryConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[1]))!;

    private sealed class StringDictionaryConverter<TValue> : JsonConverter<SortedDictionary<string, TValue>>
    {
        public override SortedDictionary<string, TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected a string-keyed object.");
            var result = new SortedDictionary<string, TValue>(StringComparer.Ordinal);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) return result;
                if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a string key.");
                string key = reader.GetString()!;
                if (!reader.Read()) throw new JsonException("Missing object value.");
                result[key] = JsonSerializer.Deserialize<TValue>(ref reader, options)!;
            }
            throw new JsonException("Incomplete string-keyed object.");
        }

        public override void Write(Utf8JsonWriter writer, SortedDictionary<string, TValue> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            var entries = ReferenceEquals(value.Comparer, StringComparer.Ordinal) ? value.AsEnumerable() : value.OrderBy(pair => pair.Key, StringComparer.Ordinal);
            foreach (var pair in entries)
            {
                writer.WritePropertyName(pair.Key);
                JsonSerializer.Serialize(writer, pair.Value, options);
            }
            writer.WriteEndObject();
        }
    }

    private sealed class StringSetConverter : JsonConverter<SortedSet<string>>
    {
        public override SortedSet<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected a string set.");
            var result = new SortedSet<string>(StringComparer.Ordinal);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray) return result;
                if (reader.TokenType != JsonTokenType.String) throw new JsonException("Expected a string set member.");
                result.Add(reader.GetString()!);
            }
            throw new JsonException("Incomplete string set.");
        }

        public override void Write(Utf8JsonWriter writer, SortedSet<string> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            var entries = ReferenceEquals(value.Comparer, StringComparer.Ordinal) ? value.AsEnumerable() : value.Order(StringComparer.Ordinal);
            foreach (string item in entries) writer.WriteStringValue(item);
            writer.WriteEndArray();
        }
    }
}
