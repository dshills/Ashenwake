using System.Text.Json;
using Ashenwake.Core.Content;

namespace Ashenwake.Core.Serialization;

/// <summary>Inspect version headers before strict typed deserialization. Future fields must
/// not turn an incompatible save into apparent corruption eligible for overwrite/recovery.</summary>
public static class ArchiveHeaders
{
    internal static void Checksum(JsonElement envelope, string stateField)
    {
        var state = Object(envelope, stateField);
        if (!envelope.TryGetProperty("stateHash", out var hash) || hash.ValueKind != JsonValueKind.String ||
            hash.GetString() != JsonData.Hash(state)) throw new InvalidDataException("Archive original-state checksum mismatch.");
    }

    public static void Require(JsonElement value, int schemaVersion, string? rulesVersion = null)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int version))
            throw new InvalidDataException("Archive object is missing a valid schema header.");
        if (version != schemaVersion) throw new SaveCompatibilityException("Unsupported archive schema; preserve this save for its matching build.");
        if (rulesVersion is null) return;
        if (!value.TryGetProperty("rulesVersion", out var rules) || rules.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Archive object is missing its rules header.");
        if (rules.GetString() != rulesVersion) throw new SaveCompatibilityException("Unsupported archive rules; preserve this save for its matching build.");
    }
    public static JsonElement Object(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Archive is missing object: " + name);
        return value;
    }
    public static void Identity(JsonElement value, string field, string expected)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(field, out var identity) || identity.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Archive is missing identity: " + field);
        if (identity.GetString() != expected) throw new SaveCompatibilityException("Archive content identity differs; preserve it for an explicit migration.");
    }
}
