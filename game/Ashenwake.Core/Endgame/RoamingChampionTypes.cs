using Ashenwake.Core.Combat;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Endgame;

public sealed record RoamingChampionDefinition(string Id, string Name, int Act, string[] SourceEncounterIds,
    string PrimaryEnemyId, string EncounterId, string RewardItemId, string Description, string Counterplay);
public static class RoamingChampionCatalog
{
    public static readonly Position SightingPosition = new(8200, -3000), ExitPosition = new(-7000, 0),
        ChallengePosition = new(-2500, 0), TreasurePosition = new(4500, 0);
    public const int InteractionRange = 1800;
    public static IReadOnlyList<RoamingChampionDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        new RoamingChampionDefinition("champion.pilgrim", "The Bell-Torn Pilgrim", 1, ["campaign.road", "campaign.monastery"],
            "enemy.funeral_guard", "championarena.pilgrim", "item.last_toll",
            "A penitent drags a broken bell through the Grey March. Its chains mark a secluded place of challenge.",
            "Sidestep the dragging chains and interrupt the announced bell toll before it rings."),
        new RoamingChampionDefinition("champion.rootwidow", "Widow of the Root", 2, ["campaign.living_ruins", "campaign.plague_village"],
            "enemy.bloom_carrier", "championarena.rootwidow", "item.broodkeepers_knot",
            "A grieving broodmother follows the buried roots of Verdant Maw. Venomous nests surround her refuge.",
            "Destroy the poison nests and leave their announced blooms. Keep an open route around the Widow."),
        new RoamingChampionDefinition("champion.tithekeeper", "The Cinder Tithekeeper", 3, ["campaign.cinder_pack", "campaign.extraction_floor"],
            "enemy.forge_sentinel", "championarena.tithekeeper", "item.tithebreakers_grasp",
            "A furnace-armored collector still demands payment from the abandoned workshops of Cinder Reach.",
            "Evade the furnace blasts, then attack while its open vents expose the collector's weakness.")
    });
    public static RoamingChampionDefinition? Find(string? id) => Definitions.FirstOrDefault(d => d.Id == id);
    public static string SourceEncounter(RoamingChampionDefinition definition, ulong characterSeed)
        => definition.SourceEncounterIds[(int)((characterSeed ^ ((ulong)definition.Act * 0x9E3779B97F4A7C15UL)) % (ulong)definition.SourceEncounterIds.Length)];
}
public sealed record RoamingChampionActive(string Id, ulong Seed, string Stage);
public sealed record RoamingChampionState
{
    public int SchemaVersion { get; init; } = 1;
    public long AttemptSequence { get; init; }
    public string[] Discovered { get; init; } = [];
    public string[] Defeated { get; init; } = [];
    public string[] Claimed { get; init; } = [];
    public RoamingChampionActive? Active { get; init; }
    public CombatSnapshot? Combat { get; init; }
}
public sealed record RoamingChampionEntryView(string Id, string Name, int Act, string SourceEncounterId, bool Here,
    bool Discovered, bool Defeated, bool Claimed, bool InReach, string Description, string Counterplay, string RewardItemId);
public sealed record RoamingChampionRunView(string Id, string Name, int Act, string Stage, string Counterplay,
    bool CanChallenge, bool CanClaim, bool CanExit);
public sealed record RoamingChampionsView(RoamingChampionEntryView[] Entries, RoamingChampionRunView? Run);
