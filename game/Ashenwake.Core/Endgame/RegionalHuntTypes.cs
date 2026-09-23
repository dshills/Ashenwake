using Ashenwake.Core.Combat;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Endgame;

public sealed record RegionalHuntContract(string Id, string Name, int Act, string Region, string Description,
    string Counterplay, string RewardItemId, int Materials, string EncounterId, string PrimaryEnemyId, string[] Clues);
public static class RegionalHuntCatalog
{
    public const string BoardInteraction = "regional-hunts.board";
    public static readonly Position BoardPosition = new(3500, 4500);
    public const int BoardRange = 2400;
    public static IReadOnlyList<RegionalHuntContract> Contracts { get; } = Array.AsReadOnly(new[]
    {
        new RegionalHuntContract("hunt.regional.pallbearer", "The Cinder Pallbearer", 1, "Grey March",
            "An armored mourner carries stolen funeral embers through the abandoned procession grounds.",
            "Separate the Pallbearer from its choir. Interrupt the violet dirge before it shields nearby allies, and step between the announced sonic lanes.",
            LegendaryEquipment.Pyre, 12, "regional.pallbearer", "enemy.funeral_guard",
            ["Gather the warm funeral ash", "Examine the dragging coffin tracks", "Follow the stolen bell's resonance"]),
        new RegionalHuntContract("hunt.regional.briarwidow", "Briarwidow of the Maw", 2, "Verdant Maw",
            "An antlered predator has woven a feeding ground around the carcasses of lost caravans.",
            "Kill the bloom carriers away from yourself, keep moving out of poison blooms, and dodge the antler charge before retaliating.",
            LegendaryEquipment.Widow, 18, "regional.briarwidow", "boss.antler",
            ["Inspect the silk-bound carcass", "Trace the thorn-scored roots", "Disturb the trembling egg sac"]),
        new RegionalHuntContract("hunt.regional.kilnmaw", "Kilnmaw the Unquenched", 3, "Cinder Reach",
            "A wandering furnace sentinel consumes cooling stations and leaves a trail of molten teeth.",
            "Interrupt the heat tender's bellows before it empowers Kilnmaw. Leave marked conveyor lanes and retreat from Kilnmaw's martyr burst and the emberlings' death explosions.",
            LegendaryEquipment.Furnace, 24, "regional.kilnmaw", "enemy.forge_sentinel",
            ["Read the scorched cooling gauge", "Inspect the molten tooth marks", "Open the buckled furnace hatch"])
    });
    public static RegionalHuntContract? Find(string id) => Contracts.FirstOrDefault(c => c.Id == id);
    public static Position CluePosition(int index) => index switch { 0 => new(-4200, -2500), 1 => new(0, 4500), 2 => new(4800, -1800), _ => throw new ArgumentOutOfRangeException(nameof(index)) };
}
public sealed record RegionalHuntRunState(long Id, string ContractId, ulong Seed, string Stage, int TrackedClues, bool Away);
public sealed record RegionalHuntReceipt(long RunId, string ContractId, ulong Seed);
public sealed record RegionalHuntState
{
    public int SchemaVersion { get; init; } = 1;
    public long NextRunId { get; init; } = 1;
    public RegionalHuntRunState? Run { get; init; }
    public RegionalHuntReceipt[] Rewards { get; init; } = [];
    public CombatSnapshot? Combat { get; init; }
}
public sealed record RegionalHuntContractView(string Id, string Name, int Act, string Region, string Description,
    string Counterplay, string RewardItemId, int Materials, bool Unlocked, string Requirement, int Completed);
public sealed record RegionalHuntRunView(long Id, string ContractId, string Name, int Act, string Stage, int TrackedClues,
    string NextClue, string Counterplay, bool CanTrack, bool CanClaim, bool CanReturn, bool CanAbandon);
public sealed record RegionalHuntBoardView(RegionalHuntContractView[] Contracts, RegionalHuntRunView? Run, bool NearBoard, bool CanStart);
