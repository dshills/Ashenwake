using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Production;

namespace Ashenwake.Core.Endgame;

public sealed record EndgameBalanceObservation(long RunId, int Tier, int RoomIndex, string RoomId, string Name,
    bool InHub, bool LiveEnemies, bool HasLoot, bool Cleared, int Deaths, int Level, long Experience, int Materials)
{
    public string Key => InHub ? "hub.after." + Tier : RunId + ":" + RoomIndex;
    public static EndgameBalanceObservation Capture(EndgameRuntimeSession session)
    {
        var combat = session.Combat.View; var run = session.RunView; var progress = session.Production.ProgressionView;
        var context = combat.Endgame;
        return new(run?.Id ?? 0, run?.Tier ?? 0, context?.EncounterIndex ?? 0,
            context is null ? "hub" : "run." + context.RunId + ".encounter." + context.EncounterIndex,
            context?.PhaseName ?? "Greyhaven", session.InHub,
            combat.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0), combat.Loot.Count > 0,
            session.EncounterCleared, run?.Deaths ?? 0, progress.Level, progress.Experience, progress.Materials);
    }
}

public sealed class EndgameBalanceRoom
{
    public required EndgameBalanceObservation Entry { get; init; }
    public required EndgameBalanceObservation Exit { get; set; }
    public long EntryCommand { get; init; }
    public long ExitCommand { get; set; }
    public long CombatTicks { get; set; }
    public long LootTicks { get; set; }
    public long TravelTicks { get; set; }
    public long MenuCommands { get; set; }
    public long RejectedOperations { get; set; }
    public int Attempts { get; set; }
    public int Deaths { get; set; }
    public int Potions { get; set; }
    public long DamageTaken { get; set; }
    public bool Cleared { get; set; }
    public bool RewardCommitted { get; set; }
    public long ExperienceEarned { get; set; }
    public long MaterialsEarned { get; set; }
    public long MaterialsSpent { get; set; }
    public SortedDictionary<string, int> SkillsActivated { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> RejectedCommands { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> WorldActions { get; } = new(StringComparer.Ordinal);
    public double CombatSeconds => CombatTicks / 30.0;
}

/// <summary>Read-only measurements. World/menu operations are never reported as simulated combat duration.</summary>
public sealed class EndgameBalanceMeasurement
{
    private readonly SortedDictionary<string, EndgameBalanceRoom> rooms = new(StringComparer.Ordinal);
    private readonly HashSet<long> ownedIds;
    public long Commands { get; private set; }
    public IReadOnlyList<EndgameBalanceRoom> Rooms => rooms.Values.OrderBy(r => r.EntryCommand).ToArray();
    public List<object> GearGrants { get; } = [];
    public List<object> BuildChanges { get; } = [];

    public EndgameBalanceMeasurement(EndgameRuntimeSession session) =>
        ownedIds = session.Production.Capture().Progression.Character.Items.Select(i => i.Id).ToHashSet();

    public EndgameBalanceRoom Observe(EndgameBalanceObservation before, EndgameRuntimeCommand command,
        EndgameRuntimeResult result, EndgameRuntimeSession session)
    {
        Commands++;
        if (!rooms.TryGetValue(before.Key, out var row))
        {
            row = new() { Entry = before, Exit = before, EntryCommand = Commands, Attempts = before.LiveEnemies ? 1 : 0 };
            rooms.Add(before.Key, row);
        }
        bool tick = command.Action == EndgameRuntimeAction.Tick ||
            command.Action == EndgameRuntimeAction.Campaign && command.Campaign?.Action == CampaignRuntimeAction.Tick;
        var inputs = command.Action == EndgameRuntimeAction.Tick ? command.Commands : command.Campaign?.Commands;
        if (!result.Success) row.RejectedOperations++;
        else if (!tick) row.MenuCommands++;
        else if (!before.InHub && before.LiveEnemies) row.CombatTicks++;
        else if (before.HasLoot && inputs?.Any(c => c.Kind == CombatCommandKind.Pickup) == true) row.LootTicks++;
        else row.TravelTicks++;
        foreach (var e in result.CombatEvents)
        {
            if (e.Kind == "DamageApplied" && e.TargetId == 1) row.DamageTaken += e.Amount;
            if (e.Kind == "Healed" && e.TargetId == 1 && e.ContentId == "potion") row.Potions++;
            if (e.Kind == "AbilityStarted" && e.ActorId == 1) Increment(row.SkillsActivated, e.ContentId);
            if (e.Kind == "CommandRejected") Increment(row.RejectedCommands, e.ContentId);
        }
        foreach (string e in result.WorldEvents)
        {
            string kind = e.Split(':')[0]; Increment(row.WorldActions, kind);
            if (kind is "ExpeditionAttemptConsumed" or "ExpeditionFailed") row.Deaths++;
            if (kind == "EndgameAttemptRestarted") row.Attempts++;
            if (kind == "EndgameRoomCleared") row.Cleared = true;
            if (kind == "EndgameEncounterCompleted") row.RewardCommitted = true;
        }
        var after = EndgameBalanceObservation.Capture(session);
        row.ExitCommand = Commands;
        // Preserve the outgoing room's identity on a transition while recording its final progression.
        row.Exit = before.Key == after.Key ? after : before with
        { Level = after.Level, Experience = after.Experience, Materials = after.Materials, Cleared = row.Cleared };
        row.ExperienceEarned += Math.Max(0, after.Experience - before.Experience);
        row.MaterialsEarned += Math.Max(0, after.Materials - before.Materials);
        row.MaterialsSpent += Math.Max(0, before.Materials - after.Materials);
        if (result.CombatEvents.Any(e => e.Kind == "LootPickedUp") || result.WorldEvents.Any(e => e.StartsWith("ItemGranted:", StringComparison.Ordinal)))
            foreach (var item in session.Production.Capture().Progression.Character.Items.OrderBy(i => i.Id))
                if (ownedIds.Add(item.Id)) GearGrants.Add(new { command = Commands, before.RunId, before.Tier, before.RoomIndex, item });
        var production = command.Action == EndgameRuntimeAction.Production ? command.Production : command.Campaign?.Production;
        if (production?.Action is ProductionAction.Equip or ProductionAction.Passive)
            BuildChanges.Add(new { command = Commands, before.Tier, production, result.Success });
        return row;
    }

    private static void Increment(SortedDictionary<string, int> counts, string key) => counts[key] = counts.GetValueOrDefault(key) + 1;
}
