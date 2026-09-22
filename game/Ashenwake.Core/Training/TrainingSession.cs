using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Training;

/// <summary>Bounded, disposable combat copy. It owns no production session, wallet, profile, or reward callback.</summary>
public sealed class TrainingSession
{
    public const string InteractionId = "hub.training";
    public static Position EntryPosition { get; } = new(-4500, 4500);
    public const int InteractionRange = 2400;
    public const int MaximumTicks = 9000;
    private readonly string combatJson;
    private readonly CombatSnapshot source;
    private CombatSnapshot initial = null!;
    private readonly List<TrainingFrame> frames = [];
    private readonly Dictionary<(string Category, string Id, string Name, DamageFamily Family), (long Damage, long Hits)> damage = [];
    private readonly Dictionary<(string Kind, string Id, string Name), long> triggers = [];
    private readonly Dictionary<string, (long Increased, long Decreased)> resources = new(StringComparer.Ordinal);
    public CombatSession Combat { get; private set; } = null!;
    public TrainingTargetMode Mode { get; private set; }
    public bool IsClosed { get; private set; }
    public bool IsComplete => IsClosed || ElapsedTicks >= MaximumTicks || Combat.Tick >= 1000000000 || Combat.View.Actors.Single(a => a.Id == 1).Health <= 0 ||
        !Combat.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
    public long ElapsedTicks => Combat.Tick - initial.Tick;
    public TrainingReport Report
    {
        get
        {
            var rows = damage.Select(p => new TrainingDamageRow(p.Key.Category, p.Key.Id, p.Key.Name, p.Key.Family, p.Value.Damage, p.Value.Hits))
                .OrderByDescending(r => r.Damage).ThenBy(r => r.SourceId, StringComparer.Ordinal).ThenBy(r => r.Family).ToArray();
            long total = rows.Sum(r => r.Damage); double seconds = ElapsedTicks * FixedStepClock.SecondsPerTick;
            var view = Combat.View;
            return new(Mode, ElapsedTicks, seconds, total, seconds == 0 ? 0 : total / seconds,
                resources.Values.Sum(v => v.Increased), resources.Values.Sum(v => v.Decreased), view.Resource, view.ResourceName,
                view.Actors.Count(a => a.Faction == CombatFaction.Enemy && a.Health == 0 && !initial.Actors.Any(i => i.Id == a.Id && i.Health == 0)), IsComplete,
                rows, triggers.Select(p => new TrainingTriggerRow(p.Key.Kind, p.Key.Id, p.Key.Name, p.Value)).OrderBy(r => r.SourceId, StringComparer.Ordinal).ThenBy(r => r.Kind, StringComparer.Ordinal).ToArray(),
                resources.Select(p => new TrainingResourceRow(p.Key, p.Value.Increased, p.Value.Decreased)).OrderBy(r => r.Reason, StringComparer.Ordinal).ToArray());
        }
    }
    internal TrainingSession(string combatJson, CombatSnapshot earned, TrainingTargetMode mode)
    { this.combatJson = combatJson; source = JsonData.Copy(earned); Reset(mode); }
    public void Reset(TrainingTargetMode mode)
    {
        if (IsClosed) throw new InvalidOperationException("This training session has ended.");
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode; Combat = CombatSession.CreateTraining(combatJson, source, mode);
        initial = Combat.Capture(); damage.Clear(); triggers.Clear(); resources.Clear(); frames.Clear();
        Combat.TrainingDamage = hit =>
        {
            var key = (hit.Category, hit.SourceId, hit.Name, hit.Family); var old = damage.GetValueOrDefault(key);
            damage[key] = (old.Damage + hit.Damage, old.Hits + 1);
        };
        Combat.TrainingResource = (reason, delta) =>
        {
            var old = resources.GetValueOrDefault(reason);
            resources[reason] = (old.Increased + Math.Max(0, delta), old.Decreased + Math.Max(0, -delta));
        };
    }
    public IReadOnlyList<CombatEvent> Step(params CombatCommand[] commands)
    {
        if (IsComplete) return [];
        if (commands is null || commands.Length > 64 || commands.Any(c => c is null)) throw new InvalidDataException("Invalid training input batch.");
        var events = Combat.Step(commands).ToArray();
        foreach (var ev in events.Where(e => e.Kind is "FragmentTriggered" or "LegendaryTriggered" or "LegendaryReadied"))
        {
            var key = (ev.Kind, ev.ContentId, Combat.TrainingContentName(ev.ContentId)); triggers[key] = triggers.GetValueOrDefault(key) + 1;
        }
        frames.Add(new(JsonData.Copy(commands), Combat.StateHash, JsonData.Hash(Report)));
        return events;
    }
    public void Close() { IsClosed = true; Combat.TrainingDamage = null; Combat.TrainingResource = null; }
    public TrainingReplay CaptureReplay() => new(1, JsonData.Copy(initial), Mode, JsonData.Copy(frames.ToArray()));
    public static bool VerifyReplay(string combatJson, TrainingReplay replay)
    {
        if (replay is null || replay.SchemaVersion != 1 || replay.Frames is null || replay.Frames.Length > MaximumTicks || !Enum.IsDefined(replay.Mode))
            throw new InvalidDataException("Invalid training replay.");
        // Recreate from the captured clean starting state, preserving the build and deterministic seed.
        var run = new TrainingSession(combatJson, replay.Initial, replay.Mode);
        if (JsonData.Hash(run.initial) != JsonData.Hash(replay.Initial)) return false;
        foreach (var frame in replay.Frames)
        {
            if (frame is null || run.IsComplete) return false;
            run.Step(frame.Commands);
            if (run.Combat.StateHash != frame.StateHash || JsonData.Hash(run.Report) != frame.ReportHash) return false;
        }
        return true;
    }
}
