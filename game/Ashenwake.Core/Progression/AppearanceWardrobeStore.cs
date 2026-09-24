using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Core.Progression;

public sealed record AppearanceWardrobeLoadResult(AppearanceWardrobeMemory Memory, bool RecoveredBackup, bool CanWrite, string Notice);

/// <summary>Bounded optional sidecar. Reads never repair files or affect character archive compatibility.</summary>
public static class AppearanceWardrobeStore
{
    private const int MaximumBytes = 256 * 1024;
    private sealed record Envelope([property: JsonRequired] int SchemaVersion, [property: JsonRequired] string SaveIdentity,
        [property: JsonRequired] string StateHash, [property: JsonRequired] AppearanceWardrobeMemory Memory);
    private sealed record Candidate(AppearanceWardrobeMemory? Memory, bool Exists, bool Protected);

    public static string PathFor(string savePath) => Path.GetFullPath(savePath) + ".wardrobe.json";

    public static AppearanceWardrobeLoadResult Load(string savePath, string characterId, ProgressionDefinition definition)
    {
        var empty = AppearanceWardrobe.Empty(characterId);
        string path = PathFor(savePath), identity = Identity(savePath);
        var primary = Inspect(path, identity, characterId, definition);
        var backup = Inspect(path + ".bak", identity, characterId, definition);
        if (primary.Protected)
            return new(empty, false, false, "Wardrobe memory is from another character or build, or cannot be read. Its files are preserved; discoveries remain available this session.");
        if (primary.Memory is { } current)
            return new(current, false, !backup.Protected, backup.Protected ? "Wardrobe backup is protected. Changes remain available this session." : "");
        if (backup.Memory is { } recovered)
            return new(recovered, true, true, "Wardrobe memory recovered from its backup.");
        bool missing = !primary.Exists && !backup.Exists;
        return new(empty, false, missing, missing ? "" : "Wardrobe memory could not be read. Its files are preserved; discoveries remain available this session.");
    }

    /// <summary>Union observed appearances under a write lease. Stale choices are rejected; observation-only updates may merge when choices still agree.</summary>
    public static AppearanceWardrobeMemory Write(string savePath, AppearanceWardrobeMemory memory, ProgressionDefinition definition)
    {
        AppearanceWardrobe.Validate(memory, definition);
        string path = PathFor(savePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        RejectLink(path + ".lock");
        using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var previous = Load(savePath, memory.CharacterId, definition);
        if (!previous.CanWrite) throw new IOException(previous.Notice);
        if (memory.Revision > previous.Memory.Revision || memory.Revision < previous.Memory.Revision &&
            AppearanceWardrobe.ChoicesHash(memory) != AppearanceWardrobe.ChoicesHash(previous.Memory))
            throw new IOException("Wardrobe choices changed in another session. Reload the wardrobe before applying this change.");
        var unlocked = previous.Memory.Unlocks.ToDictionary(u => u.ItemId, u => u.Rarity, StringComparer.Ordinal);
        foreach (var unlock in memory.Unlocks)
            if (!unlocked.TryGetValue(unlock.ItemId, out var rarity) || unlock.Rarity > rarity) unlocked[unlock.ItemId] = unlock.Rarity;
        var merged = AppearanceWardrobe.Copy(memory) with
        {
            Revision = previous.Memory.Revision,
            Unlocks = unlocked.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new WardrobeUnlock(p.Key, p.Value)).ToArray()
        };
        AppearanceWardrobe.Validate(merged, definition);
        if (!previous.RecoveredBackup && JsonData.Hash(merged) == JsonData.Hash(previous.Memory)) return merged;
        merged = merged with { Revision = checked(merged.Revision + 1) };
        AppearanceWardrobe.Validate(merged, definition);
        string json = JsonData.Write(new Envelope(1, Identity(savePath), JsonData.Hash(merged), merged));
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("Wardrobe memory exceeds its size limit.");
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

    private static Candidate Inspect(string path, string identity, string characterId, ProgressionDefinition definition)
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
            var envelope = root.Deserialize<Envelope>(JsonData.Options)
                ?? throw new InvalidDataException("Wardrobe document is null.");
            if (envelope.SaveIdentity != identity || envelope.Memory is not null && envelope.Memory.CharacterId != characterId)
                return new(null, true, true);
            // A removed/newer catalog appearance is protected, not silently replaced by an older backup.
            if (envelope.Memory?.Unlocks is { } unlocks && unlocks.Any(u => u is not null && !definition.Items.Any(i => i.Id == u.ItemId)))
                return new(null, true, true);
            AppearanceWardrobe.Validate(envelope.Memory!, definition);
            if (envelope.StateHash != JsonData.Hash(envelope.Memory)) return new(null, true, false);
            return new(envelope.Memory, true, false);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or DecoderFallbackException)
        { return new(null, true, false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new(null, true, true); }
    }

    private static string ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Wardrobe memory exceeds its size limit.");
        byte[] bytes = new byte[MaximumBytes + 1];
        int length = 0, count;
        while (length < bytes.Length && (count = stream.Read(bytes, length, bytes.Length - length)) > 0) length += count;
        if (length > MaximumBytes) throw new InvalidDataException("Wardrobe memory exceeds its size limit.");
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
            throw new IOException("Symbolic-link wardrobe aliases are unsupported.");
    }
}
