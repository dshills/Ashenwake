using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Serialization;

public sealed record ReplayFrame(long Tick, GameCommand[] Commands, string StateHash, string EventHash);
public sealed record ReplayDocument(int SchemaVersion, string RulesVersion, string ContentHash, WorldState InitialState, ReplayFrame[] Frames);
public sealed record ReplayResult(bool Success, long? DivergentTick, string Detail, string FinalHash);

public sealed class ReplayRecorder
{
    private readonly WorldState _initial;
    private readonly string _contentHash;
    private readonly List<ReplayFrame> _frames = [];
    public int FrameCount => _frames.Count;
    public ReplayRecorder(SimulationWorld world) { _initial = world.Capture(); _contentHash = world.ContentHash; }
    public IReadOnlyList<SimulationEvent> Step(SimulationWorld world, params GameCommand[] commands)
    {
        if (world.Tick != _initial.Tick + _frames.Count || world.ContentHash != _contentHash)
            throw new InvalidOperationException("Replay world continuity changed; start a new recording.");
        var tick = world.Tick;
        var events = world.Step(commands);
        _frames.Add(new(tick, commands.ToArray(), world.StateHash, JsonData.Hash(events)));
        return events;
    }
    public ReplayDocument Capture() => new(1, BuildIdentity.RulesVersion, _contentHash, JsonData.Copy(_initial), _frames.ToArray());
}

public static class ReplayRunner
{
    public static ReplayResult Run(ContentBundle bundle, ReplayDocument replay)
    {
        if (replay.SchemaVersion != 1 || replay.RulesVersion != BuildIdentity.RulesVersion || replay.ContentHash != bundle.Hash)
            throw new InvalidDataException("Replay schema/rules/content is incompatible.");
        if (replay.Frames is null) throw new InvalidDataException("Replay frames are required.");
        var world = new SimulationWorld(bundle, replay.InitialState);
        foreach (var frame in replay.Frames)
        {
            if (frame is null || frame.Tick != world.Tick || frame.Commands is null)
                throw new InvalidDataException("Replay frames must be contiguous with nonnull commands.");
            var events = world.Step(frame.Commands);
            if (world.StateHash != frame.StateHash)
                return new(false, frame.Tick, "Authoritative state diverged.", world.StateHash);
            if (JsonData.Hash(events) != frame.EventHash)
                return new(false, frame.Tick, "Semantic events diverged.", world.StateHash);
        }
        return new(true, null, "All state and event hashes match.", world.StateHash);
    }
}
