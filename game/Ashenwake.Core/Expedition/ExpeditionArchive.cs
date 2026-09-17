using System.Text.Json.Serialization;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Core.Expedition;

public sealed record ExpeditionSave([property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string StateHash, [property: JsonRequired] ExpeditionSnapshot State);
public sealed record ExpeditionLoadResult(ExpeditionSession Session, bool RecoveredBackup);

public static class ExpeditionSaveStore
{
    public static ExpeditionSession Read(string combatJson, AdventureContent content, string json)
    {
        var document = JsonData.Read<ExpeditionSave>(json);
        if (document.SchemaVersion != 1)
            throw new SaveCompatibilityException("Unsupported expedition save version; preserve this file for its matching build.");
        if (document.State is null || document.State.Combat is null) throw new InvalidDataException("Expedition save is missing its logical state.");
        if (document.State.SchemaVersion != 1 || document.State.RulesVersion != "expedition.1")
            throw new SaveCompatibilityException("Unsupported expedition state version; preserve this file for its matching build.");
        if (document.State.AdventureHash != content.Hash || document.State.Combat.ContentHash != JsonData.Hash(CombatContent.Parse(combatJson)))
            throw new SaveCompatibilityException("Expedition content differs; unknown IDs are never silently discarded.");
        if (document.StateHash != JsonData.Hash(document.State)) throw new InvalidDataException("Expedition save checksum mismatch.");
        return ExpeditionSession.Restore(combatJson, content, document.State);
    }
    public static void Write(string path, string combatJson, AdventureContent content, ExpeditionSnapshot snapshot)
    {
        var state = ExpeditionSession.Restore(combatJson, content, snapshot).Capture();
        string full = Path.GetFullPath(path);
        if (File.Exists(full))
        {
            string previous = File.ReadAllText(full);
            try { Read(combatJson, content, previous); AtomicFile.Write(full + ".bak", previous); }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException) { }
        }
        AtomicFile.Write(full, JsonData.Write(new ExpeditionSave(1, JsonData.Hash(state), state)));
    }
    public static ExpeditionLoadResult Load(string path, string combatJson, AdventureContent content)
    {
        try { return new(Read(combatJson, content, File.ReadAllText(path)), false); }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or FileNotFoundException)
        {
            if (!File.Exists(path + ".bak")) throw new InvalidDataException("Expedition save is missing or corrupt and no backup exists.", ex);
            return new(Read(combatJson, content, File.ReadAllText(path + ".bak")), true);
        }
    }
}

public static class ExpeditionReplayRunner
{
    public static ReplayResult Run(string combatJson, AdventureContent content, ExpeditionReplay replay)
    {
        if (replay is null || replay.SchemaVersion != 1 || replay.Initial is null || replay.Frames is null)
            throw new InvalidDataException("Malformed or incompatible expedition replay.");
        var session = ExpeditionSession.Restore(combatJson, content, replay.Initial);
        long index = 0;
        foreach (var frame in replay.Frames)
        {
            if (frame is null || frame.Command is null) throw new InvalidDataException("Null expedition replay frame.");
            var result = session.Execute(frame.Command);
            if (session.StateHash != frame.StateHash) return new(false, index, "Expedition state diverged at operation index.", session.StateHash);
            if (JsonData.Hash(result) != frame.EventHash) return new(false, index, "Expedition events diverged at operation index.", session.StateHash);
            index++;
        }
        return new(true, null, "All expedition operation, combat and world hashes match.", session.StateHash);
    }
}
