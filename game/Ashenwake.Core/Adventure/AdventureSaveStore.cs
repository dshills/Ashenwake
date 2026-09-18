using System.Text.Json;
using System.Text.Json.Serialization;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Core.Adventure;

public sealed record AdventureSave([property: JsonRequired] int SchemaVersion, [property: JsonRequired] string ContentHash,
    [property: JsonRequired] string StateHash, [property: JsonRequired] AdventureState State);
public sealed record AdventureLegacyState([property: JsonRequired] ulong Seed, [property: JsonRequired] int Materials,
    [property: JsonRequired] bool QuestAccepted);
public sealed record AdventureLegacySave([property: JsonRequired] int SchemaVersion, [property: JsonRequired] string ContentHash,
    [property: JsonRequired] string StateHash, [property: JsonRequired] AdventureLegacyState State);
public sealed record AdventureLoadResult(AdventureSession Session, bool RecoveredBackup);

public static class AdventureSaveStore
{
    public const int SchemaVersion = 2;
    public static string Serialize(AdventureContent content, AdventureSession session)
    {
        var state = session.Capture(); AdventureSession.ValidateState(content, state);
        return JsonData.Write(new AdventureSave(SchemaVersion, content.Hash, JsonData.Hash(state), state));
    }
    public static AdventureSession Deserialize(AdventureContent content, string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version))
            throw new InvalidDataException("Adventure save schema is missing.");
        if (version is < 1 or > SchemaVersion) throw new SaveCompatibilityException("Unsupported adventure save version; preserve this file and open its matching build.");
        ArchiveHeaders.Identity(document.RootElement, "contentHash", content.Hash);
        if (version == 1)
        {
            var legacy = JsonData.Read<AdventureLegacySave>(json);
            if (legacy.ContentHash != content.Hash) throw new SaveCompatibilityException("Legacy save content is incompatible.");
            if (legacy.State is null || legacy.StateHash != JsonData.Hash(legacy.State)) throw new InvalidDataException("Legacy save checksum mismatch.");
            var migrated = AdventureSession.Create(content, legacy.State.Seed).Capture();
            migrated.Materials = legacy.State.Materials; migrated.QuestAccepted = legacy.State.QuestAccepted;
            if (migrated.QuestAccepted) migrated.Journal.Add("quest.bell_saint");
            return AdventureSession.Restore(content, migrated);
        }
        var save = JsonData.Read<AdventureSave>(json);
        if (save.ContentHash != content.Hash) throw new SaveCompatibilityException("Adventure content changed; preserve this save for its matching bundle. Unknown IDs are never silently removed.");
        if (save.State is null || save.StateHash != JsonData.Hash(save.State)) throw new InvalidDataException("Adventure save checksum mismatch.");
        return AdventureSession.Restore(content, save.State);
    }
    public static void Write(string path, AdventureContent content, AdventureSession session)
    {
        string json = Serialize(content, session);
        string full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        if (File.Exists(full))
        {
            string previous = File.ReadAllText(full);
            try { Deserialize(content, previous); AtomicWrite(full + ".bak", previous); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException) { }
        }
        AtomicWrite(full, json);
    }
    public static AdventureLoadResult Load(string path, AdventureContent content)
    {
        try { return new(Deserialize(content, File.ReadAllText(path)), false); }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or FileNotFoundException)
        {
            if (!File.Exists(path + ".bak")) throw new InvalidDataException("Adventure save is corrupt/missing and no backup exists.", ex);
            return new(Deserialize(content, File.ReadAllText(path + ".bak")), true);
        }
    }
    private static void AtomicWrite(string path, string json)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(System.Text.Encoding.UTF8.GetBytes(json)); file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
