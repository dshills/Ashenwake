using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Serialization;

public sealed record SaveDocument(int SchemaVersion, string RulesVersion, string ContentVersion, string ContentHash, string StateHash, WorldState State);
public sealed record SaveLoadResult(WorldState State, bool RecoveredBackup);
public sealed class SaveCompatibilityException(string message) : IOException(message);

public static class SaveStore
{
    public static SaveDocument Create(ContentBundle bundle, WorldState state)
    {
        SimulationWorld.ValidateState(state, bundle);
        return new(BuildIdentity.SaveSchemaVersion, BuildIdentity.RulesVersion, bundle.Content.ContentVersion, bundle.Hash, JsonData.Hash(state), state);
    }

    public static WorldState Read(string json, ContentBundle bundle)
    {
        using var headers = System.Text.Json.JsonDocument.Parse(json);
        ArchiveHeaders.Require(headers.RootElement, BuildIdentity.SaveSchemaVersion, BuildIdentity.RulesVersion);
        ArchiveHeaders.Identity(headers.RootElement, "contentHash", bundle.Hash);
        ArchiveHeaders.Identity(headers.RootElement, "contentVersion", bundle.Content.ContentVersion);
        var save = JsonData.Read<SaveDocument>(json);
        if (save.SchemaVersion != BuildIdentity.SaveSchemaVersion || save.RulesVersion != BuildIdentity.RulesVersion ||
            save.ContentHash != bundle.Hash || save.ContentVersion != bundle.Content.ContentVersion)
            throw new SaveCompatibilityException("Save schema/rules/content version is incompatible; preserve this save and use its matching build. Phase 0 has no legacy migrations.");
        if (save.StateHash != JsonData.Hash(save.State)) throw new InvalidDataException("Save checksum mismatch.");
        SimulationWorld.ValidateState(save.State, bundle);
        return save.State;
    }

    public static void Write(string path, ContentBundle bundle, WorldState state)
    {
        var json = JsonData.Write(Create(bundle, state));
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        // Never replace a newer/incompatible save, or replace a good backup with a corrupt primary.
        if (File.Exists(full))
        {
            try { Read(File.ReadAllText(full), bundle); AtomicWrite(full + ".bak", File.ReadAllText(full)); }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException) { }
        }
        AtomicWrite(full, json);
    }

    public static SaveLoadResult Load(string path, ContentBundle bundle)
    {
        try { return new(Read(File.ReadAllText(path), bundle), false); }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or FileNotFoundException)
        {
            if (!File.Exists(path + ".bak")) throw new InvalidDataException("Primary save is missing or corrupt and no backup exists.", ex);
            return new(Read(File.ReadAllText(path + ".bak"), bundle), true);
        }
    }

    private static void AtomicWrite(string path, string value)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(value);
                stream.Write(bytes); stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
