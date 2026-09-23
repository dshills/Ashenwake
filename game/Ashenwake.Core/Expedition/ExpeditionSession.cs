using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Expedition;

public sealed record ExpeditionInteraction(string ActionId, string Name, Position Position, int Range);
public enum ExpeditionAction { Tick, Travel, Interact, InstallFragment, Manifestation, Temper, Graft, EquipGodwrought }
public sealed record ExpeditionCommand(ExpeditionAction Action, string Id = "", string Value = "", bool Confirm = false, CombatCommand[]? Commands = null);
public sealed record ExpeditionResult(bool Success, string Reason, CombatEvent[] CombatEvents, string[] WorldEvents);
public sealed record ExpeditionSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public string RulesVersion { get; init; } = "expedition.1";
    public string AdventureHash { get; init; } = "";
    public long Tick { get; init; }
    public long NextKillSequence { get; init; }
    public string EncounterId { get; init; } = "hub";
    public AdventureState Adventure { get; init; } = null!;
    public CombatSnapshot Combat { get; init; } = null!;
    public SortedDictionary<string, long> GodwroughtItems { get; init; } = new(StringComparer.Ordinal);
}
public sealed record ExpeditionFrame(ExpeditionCommand Command, string StateHash, string EventHash);
public sealed record ExpeditionReplay(int SchemaVersion, ExpeditionSnapshot Initial, ExpeditionFrame[] Frames);

/// <summary>Joins combat and permanent world transactions. Only authoritative deaths finish encounters.</summary>
public sealed partial class ExpeditionSession
{
    private readonly string combatJson;
    private readonly AdventureContent content;
    private AdventureSession adventure;
    private readonly SortedDictionary<string, long> godwroughtItems = new(StringComparer.Ordinal);
    private readonly List<ExpeditionFrame> frames = [];
    private ExpeditionSnapshot initial = null!;
    private string encounterId = "hub";
    private long nextKillSequence;
    public CombatSession Combat { get; private set; }
    public long Tick { get; private set; }
    public AdventureView View => adventure.View;
    public IReadOnlyList<string> WorldEvents { get; private set; } = [];
    public string StateHash => JsonData.Hash(Capture());
    public IReadOnlyList<ExpeditionInteraction> Interactions => View.RoomId == "room.greyhaven"
        ? [new("npc.mara", "Speak with Mara Vey", new(-4500, -1800), 2400),
           new("service.torren", "Torren · equipment and tempering", new(2000, -2000), 2600),
           new("service.mara", "Mara · anatomy and manifestations", new(-4500, -1800), 2400),
           new("dungeon.replay", "Begin another expedition", new(6500, 0), 2600)]
        : View.BellPhase == 2
            ? [new("ritual.anchor_left", "Attack the west ritual anchor", new(-5500, 0), 2200), new("ritual.anchor_right", "Attack the east ritual anchor", new(5500, 0), 2200)]
            : [];

    private ExpeditionSession(string combatJson, AdventureContent content, AdventureSession adventure, CombatSession combat)
    { this.combatJson = combatJson; this.content = content; this.adventure = adventure; Combat = combat; }

    public static ExpeditionSession Create(string combatJson, AdventureContent content, ulong seed = 42)
    {
        ValidateContent(combatJson, content);
        var session = new ExpeditionSession(combatJson, content, AdventureSession.Create(content, seed), CombatSession.CreateEncounter(combatJson, seed, "hub"));
        session.SynchronizeBuild(); session.initial = session.Capture(); return session;
    }
    public static void ValidateContent(string combatJson, AdventureContent content)
    {
        var combat = CombatContent.Parse(combatJson);
        var world = content.Capture();
        if (!combat.Items.Any(i => i.Id == "item.ashcleaver" && i.Slot == "MainHand")) throw new InvalidDataException("Adventure requires the Ashcleaver item definition.");
        foreach (var fragment in world.Fragments)
            if (!combat.Fragments.Any(f => f.Id == fragment.Id && f.Slot.ToString() == fragment.Slot && f.Resonance == fragment.Resonance))
                throw new InvalidDataException("World/combat fragment definition differs: " + fragment.Id);
        foreach (var encounter in world.Rooms.SelectMany(r => r.Encounters).Distinct())
            CombatSession.CreateEncounter(combatJson, 42, encounter);
    }
    public static ExpeditionSession Restore(string combatJson, AdventureContent content, ExpeditionSnapshot snapshot)
    {
        if (snapshot is null || snapshot.SchemaVersion != 1 || snapshot.RulesVersion != "expedition.1" || snapshot.AdventureHash != content.Hash)
            throw new InvalidDataException("Expedition schema/rules/content mismatch.");
        if (snapshot.Adventure is null || snapshot.Combat is null || snapshot.GodwroughtItems is null || snapshot.Tick is < 0 or > 1000000000 || snapshot.NextKillSequence is < 0 or > 1000000000)
            throw new InvalidDataException("Malformed expedition snapshot.");
        var world = AdventureSession.Restore(content, snapshot.Adventure);
        var combat = CombatSession.Restore(combatJson, snapshot.Combat);
        var expected = world.View.RoomId == "room.greyhaven" ? "hub" : world.View.EncounterId ?? "clear";
        if (snapshot.EncounterId != expected) throw new InvalidDataException("Saved combat encounter disagrees with world progression.");
        if (expected is "hub" or "clear" ? snapshot.Combat.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0) : snapshot.Combat.EncounterId != expected)
            throw new InvalidDataException("Combat population disagrees with the active world encounter.");
        var session = new ExpeditionSession(combatJson, content, world, combat) { Tick = snapshot.Tick, encounterId = expected, nextKillSequence = snapshot.NextKillSequence };
        if (snapshot.Adventure.Godwrought.Any(g => g.LastKillSequence >= snapshot.NextKillSequence)) throw new InvalidDataException("Burning kill sequence would repeat after restore.");
        foreach (var pair in snapshot.GodwroughtItems)
        {
            if (!snapshot.Adventure.Godwrought.Any(g => g.InstanceId == pair.Key) || !snapshot.Combat.Inventory.Any(i => i.Id == pair.Value && i.DefinitionId == "item.ashcleaver"))
                throw new InvalidDataException("Godwrought item mapping is invalid.");
            session.godwroughtItems.Add(pair.Key, pair.Value);
        }
        if (session.godwroughtItems.Values.Distinct().Count() != session.godwroughtItems.Count)
            throw new InvalidDataException("Duplicate Godwrought identity.");
        if (!snapshot.Combat.Fragments.SequenceEqual(snapshot.Adventure.Anatomy)) throw new InvalidDataException("Anatomy differs between combat and world state.");
        if (combat.Build != session.CurrentBuild()) throw new InvalidDataException("Combat bonuses differ from permanent anatomy and equipment.");
        session.initial = session.Capture(); return session;
    }
    public ExpeditionSnapshot Capture() => new()
    {
        AdventureHash = content.Hash,
        Tick = Tick,
        NextKillSequence = nextKillSequence,
        EncounterId = encounterId,
        Adventure = adventure.Capture(),
        Combat = Combat.Capture(),
        GodwroughtItems = new(godwroughtItems, StringComparer.Ordinal)
    };
    internal AdventureResult ApplyBuildAnatomy(SortedDictionary<string, string> fragments, SortedDictionary<int, string> manifestations)
    {
        var result = adventure.ApplyBuildAnatomy(fragments, manifestations);
        if (result.Success) SynchronizeBuild();
        return result;
    }
    internal AdventureState CaptureAdventure() => adventure.Capture();
    public ExpeditionReplay CaptureReplay() => JsonData.Copy(new ExpeditionReplay(1, initial, frames.ToArray()));
    public IReadOnlyList<CombatEvent> Step(params CombatCommand[] commands) => Execute(new(ExpeditionAction.Tick, Commands: commands)).CombatEvents;
    public AdventureResult Travel(string roomId) => Result(Execute(new(ExpeditionAction.Travel, roomId)));
    public AdventureResult Interact(string actionId) => Result(Execute(new(ExpeditionAction.Interact, actionId)));
    public AdventureResult InstallFragment(string slot, string? id) => Result(Execute(new(ExpeditionAction.InstallFragment, slot, id ?? "")));
    public AdventureResult SelectManifestation(string id) => Result(Execute(new(ExpeditionAction.Manifestation, id)));
    public AdventureResult Temper(string instanceId) => Result(Execute(new(ExpeditionAction.Temper, instanceId)));
    public AdventureResult Graft(string instanceId, string lineage, bool confirm) => Result(Execute(new(ExpeditionAction.Graft, instanceId, lineage, confirm)));
    public AdventureResult EquipGodwrought(string instanceId) => Result(Execute(new(ExpeditionAction.EquipGodwrought, instanceId)));
    private static AdventureResult Result(ExpeditionResult result) => new(result.Success, result.Reason, result.WorldEvents);

    public ExpeditionResult Execute(ExpeditionCommand command, bool recordReplay = true)
    {
        if (command is null || !Enum.IsDefined(command.Action)) throw new InvalidDataException("Invalid expedition command.");
        if (recordReplay && frames.Count >= 3600) { initial = Capture(); frames.Clear(); }
        WorldEvents = [];
        ExpeditionResult result;
        if (command.Action == ExpeditionAction.Tick) result = Advance(command.Commands ?? []);
        else if (Combat.View.Actors.Single(a => a.Id == 1).Health <= 0) result = new(false, "The player is dead.", [], []);
        else result = ChangeWorld(command);
        WorldEvents = result.WorldEvents;
        if (recordReplay) frames.Add(new(JsonData.Copy(command), StateHash, JsonData.Hash(result)));
        return result;
    }

    /// <summary>Production owns permanent inventory and currency; this validated projection preserves the active encounter.</summary>
    internal void ApplyPermanentProjection(AdventureState world, CombatSnapshot? combatSnapshot = null, SortedDictionary<string, long>? itemMapping = null)
    {
        if (world.RoomId != View.RoomId || world.Expedition != adventure.Capture().Expedition)
            throw new InvalidDataException("Permanent projection cannot change world travel or expedition identity.");
        adventure = AdventureSession.Restore(content, world);
        if (combatSnapshot is not null) Combat = CombatSession.Restore(combatJson, combatSnapshot);
        if (itemMapping is not null) { godwroughtItems.Clear(); foreach (var pair in itemMapping) godwroughtItems.Add(pair.Key, pair.Value); }
        SynchronizeBuild();
    }

    private ExpeditionResult Advance(CombatCommand[] commands)
    {
        if (commands.Length > 64 || commands.Any(c => c is null)) throw new InvalidDataException("Invalid expedition combat inputs.");
        // Anatomy changes must pass ownership, service and world validation before reaching combat.
        var allowed = commands.Where(c => c.Kind is not (CombatCommandKind.EquipFragment or CombatCommandKind.UnequipFragment)).ToArray();
        var before = Combat.View.Actors.ToDictionary(a => a.Id);
        var events = Combat.Step(allowed).ToArray();
        var worldEvents = new List<string>();
        Tick++;
        foreach (var death in events.Where(e => e.Kind == "EntityKilled" && e.ActorId == 1))
        {
            if (!before.TryGetValue(death.TargetId, out var victim)) continue;
            if (victim.Role == "Anchor")
            {
                var anchor = adventure.Interact(victim.Position.X < 0 ? "ritual.anchor_left" : "ritual.anchor_right");
                worldEvents.AddRange(anchor.Events);
            }
            if (victim.Statuses.Any(s => s.Id == "Burning") || death.ContentId == "Burning" || events.Any(e => e.Kind == "StatusApplied" && e.TargetId == death.TargetId && e.ContentId == "Burning"))
            {
                var item = EquippedGodwrought();
                if (item is not null) worldEvents.AddRange(adventure.RecordBurningKill(nextKillSequence++, item.InstanceId).Events);
            }
        }
        adventure.ElapseCombatTicks(1);
        var view = Combat.View;
        if (view.Actors.Single(a => a.Id == 1).Health <= 0)
        {
            var recovery = adventure.PlayerDied(); worldEvents.AddRange(recovery.Events);
            if (recovery.Success) ResetEncounter(restoreAtAnchor: true);
        }
        else if (View.EncounterId is { } current && !view.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
        {
            var completion = adventure.EncounterCompleted(current); worldEvents.AddRange(completion.Events);
            if (!completion.Success) throw new InvalidDataException("Authoritative encounter completion was rejected: " + completion.Reason);
            if (View.EncounterId is not null) ResetEncounter();
            else encounterId = View.RoomId == "room.greyhaven" ? "hub" : "clear";
        }
        SynchronizeBuild();
        return new(true, "", events, worldEvents.ToArray());
    }

    private ExpeditionResult ChangeWorld(ExpeditionCommand command)
    {
        AdventureResult result;
        switch (command.Action)
        {
            case ExpeditionAction.Travel:
                result = adventure.EnterRoom(command.Id);
                if (result.Success) ResetEncounter(restoreAtAnchor: View.RoomId == "room.greyhaven");
                break;
            case ExpeditionAction.Interact:
                if (!InRange(command.Id)) return new(false, "Move within reach of this interaction.", [], []);
                if (command.Id.StartsWith("ritual.anchor_", StringComparison.Ordinal)) return new(false, "Attack the anchor to break the Bell Saint's ritual protection.", [], []);
                if (command.Id is "service.torren" or "service.mara") result = new(true, "", ["ServiceOpened:" + command.Id]);
                else result = adventure.Interact(command.Id);
                if (result.Success && command.Id == "dungeon.replay") ResetEncounter(restoreAtAnchor: true);
                break;
            case ExpeditionAction.InstallFragment:
                if (!InRange("service.mara")) return new(false, "Visit Mara in Greyhaven to change anatomy.", [], []);
                result = adventure.InstallFragment(command.Id, command.Value == "" ? null : command.Value); break;
            case ExpeditionAction.Manifestation:
                if (!InRange("service.mara")) return new(false, "Visit Mara in Greyhaven to choose a manifestation.", [], []);
                result = adventure.SelectManifestation(command.Id); break;
            case ExpeditionAction.Temper:
                if (!InRange("service.torren")) return new(false, "Visit Torren to temper this weapon.", [], []);
                result = adventure.Temper(command.Id); break;
            case ExpeditionAction.Graft:
                if (!InRange("service.mara")) return new(false, "Visit Mara for divine grafting.", [], []);
                result = adventure.Graft(command.Id, command.Value, command.Confirm); break;
            case ExpeditionAction.EquipGodwrought:
                if (!godwroughtItems.TryGetValue(command.Id, out var itemId)) return new(false, "This item is unowned or retained in the reward stash because inventory is full.", [], []);
                var state = Combat.Capture(); state.Equipment["MainHand"] = itemId;
                Combat = CombatSession.Restore(combatJson, state);
                result = new(true, "", ["GodwroughtEquipped:" + command.Id]); break;
            default: return new(false, "Unknown world interaction.", [], []);
        }
        if (result.Success) SynchronizeBuild();
        return new(result.Success, result.Reason, [], result.Events);
    }
    private bool InRange(string actionId)
    {
        var interaction = Interactions.FirstOrDefault(i => i.ActionId == actionId);
        return interaction is not null && Position.DistanceSquared(Combat.View.Actors.Single(a => a.Id == 1).Position, interaction.Position) <= (long)interaction.Range * interaction.Range;
    }
    private void ResetEncounter(bool restoreAtAnchor = false)
    {
        encounterId = View.RoomId == "room.greyhaven" ? "hub" : View.EncounterId ?? "clear";
        var state = adventure.Capture();
        Combat = CombatSession.CreateEncounter(combatJson, state.Seed, encounterId, Combat.Capture(), restoreAtAnchor);
    }
    private GodwroughtProgress? EquippedGodwrought()
    {
        if (!Combat.View.Equipment.TryGetValue("MainHand", out var id)) return null;
        var instance = godwroughtItems.FirstOrDefault(p => p.Value == id).Key;
        return instance is null ? null : adventure.Capture().Godwrought.FirstOrDefault(g => g.InstanceId == instance);
    }
    private void SynchronizeBuild()
    {
        var permanent = adventure.Capture();
        var state = Combat.Capture();
        if (!state.Fragments.SequenceEqual(permanent.Anatomy))
        {
            Combat.ApplyAnatomy(permanent.Anatomy);
            state = Combat.Capture();
        }
        bool changed = false;
        foreach (var item in permanent.Godwrought.Where(g => !godwroughtItems.ContainsKey(g.InstanceId)))
        {
            if (state.Inventory.Count >= 512) break; // Permanent reward remains owned; never discard another item to make space.
            var definition = CombatContent.Parse(combatJson).Items.Single(i => i.Id == "item.ashcleaver");
            var id = state.NextObjectId++;
            state.Inventory.Add(new(id, definition.Id, "Ashcleaver", "MainHand", "Godwrought", definition.Damage, definition.Armor, definition.CriticalBasisPoints));
            godwroughtItems.Add(item.InstanceId, id); changed = true;
        }
        if (changed) Combat = CombatSession.Restore(combatJson, state);
        Combat.ApplyAdventureBuild(CurrentBuild());
    }
    private CombatBuildModifiers CurrentBuild()
    {
        var equipped = EquippedGodwrought();
        var active = View.ActiveManifestations;
        return new(active.FirstOrDefault() ?? "",
            equipped?.AttackSpeedStacks ?? 0, equipped?.Awakened ?? false, equipped?.Evolution ?? "", equipped?.TemperLevel ?? 0, equipped is not null, active.Skip(1).FirstOrDefault() ?? "");
    }
}
