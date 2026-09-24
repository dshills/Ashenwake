using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Core.Progression;

public sealed record BestiaryLoadResult(BestiaryMemory Memory, bool RecoveredBackup, bool CanWrite, string Notice);

/// <summary>Bounded optional sidecar. Reads never repair files or affect character archive compatibility.</summary>
public static class BestiaryStore
{
    private const int MaximumBytes = 4 * 1024 * 1024;
    private sealed record Envelope([property: JsonRequired] int SchemaVersion, [property: JsonRequired] string SaveIdentity,
        [property: JsonRequired] string StateHash, [property: JsonRequired] BestiaryMemory Memory);
    private sealed record Candidate(BestiaryMemory? Memory, bool Exists, bool Protected);

    public static string PathFor(string savePath) => Path.GetFullPath(savePath) + ".bestiary.json";

    public static BestiaryLoadResult Load(string savePath, string characterId, BestiaryCatalog catalog)
    {
        var empty = Bestiary.Empty(characterId);
        string path = PathFor(savePath), identity = Identity(savePath);
        var primary = Inspect(path, identity, characterId, catalog);
        var backup = Inspect(path + ".bak", identity, characterId, catalog);
        if (primary.Protected)
            return new(empty, false, false, "Bestiary memory is from another character or build, or cannot be read. Its files are preserved; discoveries remain available this session.");
        if (primary.Memory is { } current)
            return new(current, false, !backup.Protected, backup.Protected ? "Bestiary backup is protected. Changes remain available this session." : "");
        if (backup.Memory is { } recovered)
            return new(recovered, true, true, "Bestiary memory recovered from its backup.");
        bool missing = !primary.Exists && !backup.Exists;
        return new(empty, false, missing, missing ? "" : "Bestiary memory could not be read. Its files are preserved; discoveries remain available this session.");
    }

    /// <summary>Merge discoveries and deduplicated defeat records under a write lease. Returns the merged memory.</summary>
    public static BestiaryMemory Write(string savePath, BestiaryMemory memory, BestiaryCatalog catalog)
    {
        Bestiary.Validate(memory, catalog);
        string path = PathFor(savePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        RejectLink(path + ".lock");
        using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var previous = Load(savePath, memory.CharacterId, catalog);
        if (!previous.CanWrite) throw new IOException(previous.Notice);
        var merged = Bestiary.Observe(previous.Memory, catalog, memory.DiscoveredEntries, memory.Defeats);
        if (memory.RecordLimitReached) merged = merged with { RecordLimitReached = true };
        Bestiary.Validate(merged, catalog);
        if (!previous.RecoveredBackup && File.Exists(path) && JsonData.Hash(previous.Memory) == JsonData.Hash(merged)) return merged;
        string json = JsonData.Write(new Envelope(1, Identity(savePath), JsonData.Hash(merged), merged));
        if (!previous.RecoveredBackup && File.Exists(path))
        {
            // The primary was validated above. Never replace a good backup with corrupt primary bytes.
            RejectLink(path + ".bak");
            AtomicFile.Write(path + ".bak", ReadBounded(path));
        }
        RejectLink(path);
        AtomicFile.Write(path, json);
        return merged;
    }

    private static Candidate Inspect(string path, string identity, string characterId, BestiaryCatalog catalog)
    {
        try
        {
            RejectLink(path);
            if (!File.Exists(path)) return new(null, false, false);
            string json = ReadBounded(path);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (HasDuplicateMembers(root)) return new(null, true, true);
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int version))
                return new(null, true, false);
            // Do not fall back or overwrite a newer schema, even when a readable old backup exists.
            if (version != 1) return new(null, true, true);
            var envelope = root.Deserialize<Envelope>(JsonData.Options) ?? throw new InvalidDataException("Missing bestiary envelope.");
            if (envelope.SaveIdentity != identity || envelope.Memory is { } bound && bound.CharacterId != characterId)
                return new(null, true, true);
            // A renamed/removed catalog entry belongs to another build; preserve it instead of falling back.
            if (envelope.Memory?.DiscoveredEntries?.Any(id => id is not null && catalog.Find(id) is null) == true ||
                envelope.Memory?.Defeats?.Any(d => d is not null && d.EntryId is not null && catalog.Find(d.EntryId) is null) == true)
                return new(null, true, true);
            var memory = envelope.Memory ?? throw new InvalidDataException("Missing bestiary memory.");
            Bestiary.Validate(memory, catalog);
            if (envelope.StateHash != JsonData.Hash(memory)) return new(null, true, false);
            return new(memory, true, false);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or DecoderFallbackException)
        { return new(null, true, false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new(null, true, true); }
    }

    private static string ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Bestiary memory exceeds its size limit.");
        byte[] bytes = new byte[(int)stream.Length + 1];
        int length = 0, count;
        while (length < bytes.Length && (count = stream.Read(bytes, length, bytes.Length - length)) > 0) length += count;
        if (length == bytes.Length) throw new InvalidDataException("Bestiary memory changed while reading or exceeds its size limit.");
        return new UTF8Encoding(false, true).GetString(bytes, 0, length);
    }

    // Character slots have stable filenames. Bind to that name rather than the installation's
    // absolute directory so backing up or moving the entire save folder preserves discoveries.
    private static string Identity(string savePath) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFileName(Path.GetFullPath(savePath)))));

    private static bool HasDuplicateMembers(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
                if (!names.Add(property.Name) || HasDuplicateMembers(property.Value)) return true;
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray())
                if (HasDuplicateMembers(child)) return true;
        return false;
    }

    private static void RejectLink(string path)
    {
        var info = new FileInfo(path);
        // LinkTarget also catches dangling links; the attribute guard covers other reparse aliases.
        if (info.LinkTarget is not null || info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Symbolic-link bestiary aliases are unsupported.");
    }
}
