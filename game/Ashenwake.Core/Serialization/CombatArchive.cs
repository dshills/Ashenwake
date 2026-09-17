using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;

namespace Ashenwake.Core.Serialization;

public sealed record CombatSave(int SchemaVersion, string StateHash, CombatSnapshot State);
public sealed record CombatLoadResult(CombatSnapshot State, bool RecoveredBackup);
public sealed record CombatFrame(long Tick, CombatCommand[] Commands, string StateHash, string EventHash);
public sealed record CombatReplay(int SchemaVersion, CombatSnapshot InitialState, CombatFrame[] Frames);

/// <summary>Logical saves retain the previous valid generation and fail closed on incompatible versions.</summary>
public static class CombatSaveStore
{
    public static CombatSnapshot Read(string json, string contentJson)
    {
        var save = JsonData.Read<CombatSave>(json);
        if (save.SchemaVersion != 1 || save.State is null || save.State.SchemaVersion != 1 || save.State.RulesVersion != "combat.1")
            throw new SaveCompatibilityException("Unsupported combat save version. Keep this file for its matching build.");
        var expected = CombatSession.Create(contentJson).Capture().ContentHash;
        if (save.State.ContentHash != expected)
            throw new SaveCompatibilityException("Combat content changed; this save needs its matching content bundle.");
        if (save.StateHash != JsonData.Hash(save.State)) throw new InvalidDataException("Combat save checksum mismatch.");
        return CombatSession.Restore(contentJson, save.State).Capture();
    }

    public static void Write(string path, string contentJson, CombatSnapshot snapshot)
    {
        var state = CombatSession.Restore(contentJson, snapshot).Capture();
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        if (File.Exists(full))
        {
            var previous = File.ReadAllText(full);
            try { Read(previous, contentJson); AtomicFile.Write(full + ".bak", previous); }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException) { }
        }
        AtomicFile.Write(full, JsonData.Write(new CombatSave(1, JsonData.Hash(state), state)));
    }

    public static CombatLoadResult Load(string path, string contentJson)
    {
        try { return new(Read(File.ReadAllText(path), contentJson), false); }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or FileNotFoundException)
        {
            if (!File.Exists(path + ".bak")) throw new InvalidDataException("Combat save is missing or corrupt and no backup exists.", ex);
            return new(Read(File.ReadAllText(path + ".bak"), contentJson), true);
        }
    }
}

public static class AtomicFile
{
    public static void Write(string path, string contents)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(System.Text.Encoding.UTF8.GetBytes(contents));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class CombatRecorder(CombatSession session)
{
    private readonly CombatSnapshot _initial = session.Capture();
    private readonly List<CombatFrame> _frames = [];
    public int FrameCount => _frames.Count;
    public IReadOnlyList<CombatEvent> Step(CombatSession current, params CombatCommand[] commands)
    {
        if (current.Tick != _initial.Tick + _frames.Count || current.ContentHash != _initial.ContentHash)
            throw new InvalidOperationException("Combat recording continuity changed; start a new recording.");
        var tick = current.Tick;
        var events = current.Step(commands);
        _frames.Add(new(tick, commands.ToArray(), current.StateHash, JsonData.Hash(events)));
        return events;
    }
    public CombatReplay Capture() => new(1, JsonData.Copy(_initial), _frames.ToArray());
}

public static class CombatReplayRunner
{
    public static ReplayResult Run(string contentJson, CombatReplay replay)
    {
        if (replay.SchemaVersion != 1 || replay.InitialState is null || replay.Frames is null)
            throw new InvalidDataException("Unsupported or malformed combat replay.");
        var session = CombatSession.Restore(contentJson, replay.InitialState);
        foreach (var frame in replay.Frames)
        {
            if (frame is null || frame.Tick != session.Tick || frame.Commands is null)
                throw new InvalidDataException("Combat replay frames must be contiguous and commands nonnull.");
            var events = session.Step(frame.Commands);
            if (session.StateHash != frame.StateHash) return new(false, frame.Tick, "Combat state diverged.", session.StateHash);
            if (JsonData.Hash(events) != frame.EventHash) return new(false, frame.Tick, "Combat events diverged.", session.StateHash);
        }
        return new(true, null, "All combat state and event hashes match.", session.StateHash);
    }
}
