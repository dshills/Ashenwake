using Ashenwake.Core.Combat;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Endgame;

public sealed record WorldEncounterDefinition(string Id, string Name, string Kind, int Act, string SourceEncounterId,
    string EncounterId, string RewardItemId, string Description, string Counterplay, Position EntrancePosition);
public static class WorldEncounterCatalog
{
    public const int InteractionRange = 1800;
    public static readonly Position ExitPosition = new(-7000, 0), ChoicePosition = new(-2500, 0), TreasurePosition = new(4500, 0);
    public static IReadOnlyList<WorldEncounterDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        new WorldEncounterDefinition("event.lantern", "The Last Lantern", "World event", 1, "campaign.road", "worldarena.lantern", "item.griefs_reprieve",
            "A stranded traveler keeps a dying lantern between them and the creatures gathering in its light. Defeat the captors to reveal their hidden supply cache.",
            "Clear the creatures surrounding the lantern. Evade ghoul rushes and interrupt the guard before committing to damage.", new Position(-4200, 3500)),
        new WorldEncounterDefinition("event.caravan", "The Mourning Caravan", "World event", 1, "campaign.monastery", "worldarena.caravan_challenge", "item.mourning_choir",
            "An abandoned procession cannot finish its last journey. Lay its three keepsakes to rest, or challenge the spectral escort for 20 additional materials.",
            "The inscription reads: First extinguish the candle; next return the coin; finally speak the farewell. The escort offers a harder combat alternative.", new Position(4000, -3500)),
        new WorldEncounterDefinition("event.shrine", "The Hungry Shrine", "World event", 1, "campaign.monastery", "worldarena.shrine", "item.oathkeeper_reprisal",
            "Accept the shrine's hunger: you take 25% more damage during this optional fight. Victory earns Oathkeeper’s Reprisal and 30 materials. Leaving ends the disadvantage.",
            "Incoming damage is increased by 25% in this fight only. Dodge announced attacks and interrupt support enemies before committing to damage.", new Position(-4200, 3500)),
        Storm("grey_march", "The Tolling Squall", 1, "campaign.road", "Sonic lanes divide the road while mourners gather under the storm. Cross the lanes after their warnings fade."),
        Storm("verdant", "The Briar Tempest", 2, "campaign.living_ruins", "Poison blooms spread beneath a feeding brood. Move out of the blooms and defeat support creatures first."),
        Storm("cinder", "The Ember Cyclone", 3, "campaign.extraction_floor", "Furnace winds drive fire through the arena. Interrupt the heat tender and retreat from dying emberlings."),
        Storm("spine", "The Oathbreaker Gale", 4, "campaign.bone_causeway", "Staggered fault lanes cut across the crossing while a Dirgebound contractkeeper guards the oathbound defenders. Move between the fault warnings and break the wards."),
        Storm("hollow", "The Unmaking Front", 5, "campaign.repeating_rooms", "Void pulses unsettle the breach while fractured creatures close in. Leave marked pulses before they resolve and pressure their support.")
    });
    private static WorldEncounterDefinition Storm(string id, string name, int act, string source, string counterplay) => new("storm." + id, name, "Resonance storm", act, source,
        "worldarena.storm_" + id, id switch { "grey_march" => "item.pyrebound_treads", "verdant" => "item.rotwake_signet", "cinder" => "item.furnaceheart_cinch", "spine" => "item.crown_unsworn", _ => "item.stolen_hour" }, "An optional Resonance Storm has gathered beyond this room. Your fragment effects deal 25% more damage while the storm battle is active. Stabilize it to earn a regional Legendary and 25 materials. You may leave at any time.", counterplay, id == "cinder" ? new(5400, -4400) : new(8000, -6000));
    public static WorldEncounterDefinition? Find(string? id) => Definitions.FirstOrDefault(d => d.Id == id);
    public static int Materials(string id, string outcome) => id == "event.caravan" ? outcome == "Puzzle" ? 10 : 30 : id == "event.shrine" ? 30 : id.StartsWith("storm.", StringComparison.Ordinal) ? 25 : 10;
}
public sealed record WorldEncounterActive(string Id, ulong Seed, string Stage);
public sealed record WorldEncounterState
{
    public int SchemaVersion { get; init; } = 1;
    public long AttemptSequence { get; init; }
    public string[] Discovered { get; init; } = [];
    public int CaravanPuzzleStep { get; init; }
    public SortedDictionary<string, string> Completed { get; init; } = new(StringComparer.Ordinal);
    public string[] Claimed { get; init; } = [];
    public WorldEncounterActive? Active { get; init; }
    public CombatSnapshot? Combat { get; init; }
}
public sealed record WorldEncounterChoiceView(string Id, string Label);
public sealed record WorldEncounterEntryView(string Id, string Name, string Kind, int Act, string SourceEncounterId, bool Here, bool Completed, bool Claimed, bool CanEnter);
public sealed record WorldEncounterRunView(string Id, string Name, string Kind, int Act, string Stage, string Description, string Counterplay,
    int PuzzleStep, WorldEncounterChoiceView[] Choices, bool CanChoose, bool CanClaim, bool CanExit, string Outcome);
public sealed record WorldEncountersView(WorldEncounterEntryView[] Entries, WorldEncounterRunView? Run);
