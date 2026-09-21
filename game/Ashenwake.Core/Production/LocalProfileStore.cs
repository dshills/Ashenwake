using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Core.Production;

public sealed record LocalProfileEnvelope([property: JsonRequired] int SchemaVersion, [property: JsonRequired] string ContentHash,
    [property: JsonRequired] string StateHash, [property: JsonRequired] LocalProfileState Profile);
public sealed record LocalProfileLoadResult(LocalProfileState Profile, bool RecoveredBackup);

/// <summary>A separate monotonic local ledger. Merging shares discoveries and mode unlocks, never character items, experience, or currency.</summary>
public static class LocalProfileStore
{
    public static LocalProfileLoadResult Load(string path, ProgressionContent content)
    {
        try { return new(Read(content, File.ReadAllText(path)), false); }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or FileNotFoundException)
        {
            if (!File.Exists(path + ".bak")) throw new InvalidDataException("Local profile is missing or corrupt and has no valid backup.", ex);
            return new(Read(content, File.ReadAllText(path + ".bak")), true);
        }
    }

    public static LocalProfileState Read(ProgressionContent content, string json)
    {
        try { return ReadWithLegendaryUpgrade(content, json); }
        catch (SaveCompatibilityException)
        {
            var previous = OpeningCatalogMigration.PreviousPolicy(content);
            if (previous.Hash == content.Hash) throw;
            var profile = ReadWithLegendaryUpgrade(previous, json);
            ProgressionSession.ValidateProfile(content, profile);
            return profile;
        }
    }

    private static LocalProfileState ReadWithLegendaryUpgrade(ProgressionContent content, string json)
    {
        try { return ReadExact(content, json); }
        catch (SaveCompatibilityException)
        {
            var previous = LegendaryCatalogMigration.PreviousPolicy(content);
            if (previous.Hash == content.Hash) throw;
            var profile = ReadExact(previous, json);
            ProgressionSession.ValidateProfile(content, profile);
            return profile;
        }
    }

    private static LocalProfileState ReadExact(ProgressionContent content, string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("schemaVersion", out var schema) ||
            schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int version))
            throw new InvalidDataException("Local profile schema is missing or invalid.");
        if (version != 1) throw new SaveCompatibilityException("Unsupported local profile schema; preserve the original file.");
        ArchiveHeaders.Identity(document.RootElement, "contentHash", content.Hash);
        ArchiveHeaders.Checksum(document.RootElement, "profile");
        var envelope = JsonData.Read<LocalProfileEnvelope>(json);
        if (envelope.ContentHash != content.Hash) throw new SaveCompatibilityException("Local profile content identity changed; use an explicit catalog migration.");
        if (envelope.Profile is null || envelope.StateHash != JsonData.Hash(envelope.Profile)) throw new InvalidDataException("Local profile checksum mismatch.");
        ProgressionSession.ValidateProfile(content, envelope.Profile); return JsonData.Copy(envelope.Profile);
    }

    public static LocalProfileState Merge(string path, ProgressionContent content, LocalProfileState incoming)
    {
        ProgressionSession.ValidateProfile(content, incoming);
        string full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        // A concurrent writer fails explicitly and can retry from the latest ledger; no last-writer-wins overwrite.
        using var lease = new FileStream(full + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var merged = JsonData.Copy(incoming);
        if (File.Exists(full) || File.Exists(full + ".bak"))
        {
            var previous = Load(full, content);
            if (previous.Profile.ProfileId != incoming.ProfileId) throw new InvalidDataException("Cannot merge different local profile identities.");
            merged.Unlocks.UnionWith(previous.Profile.Unlocks); merged.Discoveries.UnionWith(previous.Profile.Discoveries);
            ProgressionSession.ValidateProfile(content, merged);
            // Preserve the validated original bytes, including its previous catalog identity.
            if (!previous.RecoveredBackup) AtomicWrite(full + ".bak", File.ReadAllText(full));
        }
        AtomicWrite(full, Serialize(content, merged)); return JsonData.Copy(merged);
    }

    private static string Serialize(ProgressionContent content, LocalProfileState profile) => JsonData.Write(new LocalProfileEnvelope(1, content.Hash, JsonData.Hash(profile), profile));
    private static void AtomicWrite(string path, string json)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(Encoding.UTF8.GetBytes(json)); stream.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
