using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;
using System.Text.Json.Serialization;

namespace Ashenwake.Core.Combat;

public sealed record EndgameArenaDefinition(string Id, string Name, RoomDefinition Room);
public sealed record EndgamePackDefinition(string Id, string Region, string ArenaId, string[] EnemyIds, Position[] Positions);
public sealed record EndgameHuntPhaseDefinition(string HuntId, int Index, string Name, string Pattern, string BossId, string ArenaId, string Counterplay);
public sealed record EndgameCombatDefinition(int SchemaVersion, string Version, CombatEnemy[] Enemies,
    EndgameArenaDefinition[] Arenas, EndgamePackDefinition[] Packs, EndgameHuntPhaseDefinition[] HuntPhases, Ashenwake.Core.Endgame.EndgameDefinition? Policy = null);
public sealed record EndgameManifestSpawn(string EnemyId, Position Position, string[] EliteModifiers);
public sealed record EndgameManifestRoom(int Index, string EncounterId, string RoomId, string Name, ulong Seed,
    string[] EliteModifiers, bool Boss, string Pattern, EndgameManifestSpawn[] Spawns);
public sealed record EndgameInheritanceSource(int RoomIndex, string Candidate, bool Selected, string Reason);
public sealed record EndgameCombatManifest(int SchemaVersion, string RulesVersion, string ContentHash, long RunId,
    string Kind, string ContentId, long SigilId, ulong Seed, int Tier, string Region, string RewardTendency,
    string[] RuleIds, EndgameManifestRoom[] Rooms, EndgameInheritanceSource[] Inheritance);
public sealed record EndgameHazardView(long Id, string Kind, Position Position, Position End, int Radius,
    string Stage, long RemainingTicks, string ContentId, int SourceId, int Sequence = 0)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int SequenceIndex { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int SequenceCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int LaneIndex { get; init; }
}
public sealed record EndgameMechanismView(int Id, string Kind, Position Position, int Radius, bool Available, string Prompt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Used { get; init; }
}
public sealed record EndgameBossCueView(int BossId, bool Shielded, int GuardedTicks, int VulnerableTicks,
    bool PermanentlyVulnerable, int RecoveryTicks, int[] PriorityActorIds, int[] PriorityMechanismIds);
public sealed record CombatEndgameView(string ContextKey, long RunId, int EncounterIndex, int EncounterCount,
    int Attempt, int Tier, string[] RuleIds, string HuntId, string PhaseName, int PhaseIndex, int PhaseCount,
    string Counterplay, bool OverchargeActive, int OverchargeRemainingTicks, int NextOverchargeTicks,
    DamageFamily HighestResistanceFamily, DamageFamily LowestResistanceFamily, int HighestResistanceBasisPoints,
    int LowestResistanceBasisPoints, string[] BossModifiers, EndgameHazardView[] Hazards, EndgameMechanismView[] Mechanisms)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public EndgameBossCueView? BossCue { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int[]? HastedActorIds { get; init; }
}

public sealed record EndgameHazard(long Id, string Kind, Position Position, Position End, int Radius,
    long StartsTick, long EndsTick, long NextTick, string ContentId, int SourceId, int Damage,
    DamageFamily Family, string Status, long ActionId, int Sequence = 0);
public sealed record EndgameMechanism
{
    public int Id { get; init; }
    public string Kind { get; init; } = "";
    public Position Position { get; init; }
    public int Radius { get; init; } = 1500;
    public bool Used { get; set; }
}
public sealed record EndgameCombatState
{
    public int SchemaVersion { get; init; } = 1;
    public string RulesVersion { get; init; } = "endgame-combat.1";
    public EndgameCombatManifest Manifest { get; init; } = null!;
    public int EncounterIndex { get; init; }
    public int Attempt { get; init; }
    public long StartedTick { get; init; }
    public long NextPatternTick { get; set; }
    public int PatternCycle { get; set; }
    public long ExposedUntil { get; set; }
    public int CarriedTerm { get; set; }
    public int DepositedTerms { get; set; }
    public int EchoesCreated { get; set; }
    public int ScarsCreated { get; set; }
    public SortedSet<int> RewardedEliteIds { get; init; } = [];
    public SortedSet<string> RuleEventIds { get; init; } = [];
    public List<EndgameHazard> Hazards { get; init; } = [];
    public List<EndgameMechanism> Mechanisms { get; init; } = [];
    public SortedDictionary<int, string> ActorMechanics { get; init; } = [];
}
