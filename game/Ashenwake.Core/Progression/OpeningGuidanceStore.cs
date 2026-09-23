using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Core.Progression;

public sealed record OpeningGuidanceLoadResult(OpeningGuidanceMemory Memory, bool RecoveredBackup, bool CanWrite, string Notice);

/// <summary>Bounded optional sidecar. Reads never repair files or affect character archive compatibility.</summary>
public static class OpeningGuidanceStore
{
    private const int MaximumBytes = 16 * 1024;
    private sealed record Envelope([property: JsonRequired] int SchemaVersion, [property: JsonRequired] string SaveIdentity,
        [property: JsonRequired] string StateHash, [property: JsonRequired] OpeningGuidanceMemory Memory);
    private sealed record Candidate(OpeningGuidanceMemory? Memory, bool Exists, bool Protected);

    public static string PathFor(string savePath) => Path.GetFullPath(savePath) + ".guidance.json";

    public static OpeningGuidanceLoadResult Load(string savePath, string characterId)
    {
        var empty = OpeningGuidance.Empty(characterId);
        string path = PathFor(savePath), identity = Identity(savePath);
        var primary = Inspect(path, identity, characterId);
        var backup = Inspect(path + ".bak", identity, characterId);
        if (primary.Protected)
            return new(empty, false, false, "Guidance memory is from another character or build, or cannot be read. Its files are preserved; guidance remains available this session.");
        if (primary.Memory is { } current)
            return new(current, false, !backup.Protected, backup.Protected ? "Guidance backup is protected. Changes remain available this session." : "");
        if (backup.Memory is { } recovered)
            return new(recovered, true, true, "Guidance memory recovered from its backup.");
        bool missing = !primary.Exists && !backup.Exists;
        return new(empty, false, missing, missing ? "" : "Guidance memory could not be read. Its files are preserved; guidance remains available this session.");
    }

    /// <summary>Merge dismissals and completed lessons under a write lease; enabled uses the incoming choice. Returns the merged memory.</summary>
    public static OpeningGuidanceMemory Write(string savePath, OpeningGuidanceMemory memory)
    {
        OpeningGuidance.Validate(memory);
        string path = PathFor(savePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        RejectLink(path + ".lock");
        using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var previous = Load(savePath, memory.CharacterId);
        if (!previous.CanWrite) throw new IOException(previous.Notice);
        var dismissed = new SortedSet<string>(previous.Memory.Dismissed, StringComparer.Ordinal);
        dismissed.UnionWith(memory.Dismissed);
        var completed = new SortedSet<string>(previous.Memory.Completed, StringComparer.Ordinal);
        completed.UnionWith(memory.Completed);
        var merged = memory with { Dismissed = dismissed.ToArray(), Completed = completed.ToArray() };
        OpeningGuidance.Validate(merged);
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

    private static Candidate Inspect(string path, string identity, string characterId)
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
            var envelope = JsonData.Read<Envelope>(json);
            OpeningGuidance.Validate(envelope.Memory);
            if (envelope.SaveIdentity != identity || envelope.Memory.CharacterId != characterId)
                return new(null, true, true);
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
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Opening guidance memory exceeds its size limit.");
        byte[] bytes = new byte[MaximumBytes + 1];
        int length = 0, count;
        while (length < bytes.Length && (count = stream.Read(bytes, length, bytes.Length - length)) > 0) length += count;
        if (length > MaximumBytes) throw new InvalidDataException("Opening guidance memory exceeds its size limit.");
        return new UTF8Encoding(false, true).GetString(bytes, 0, length);
    }

    // Character slots have stable filenames. Bind to that name rather than the installation's
    // absolute directory so backing up or moving the entire save folder preserves guidance.
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
            throw new IOException("Symbolic-link guidance aliases are unsupported.");
    }
}
