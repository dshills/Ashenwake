using System.Text.Json.Serialization;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Coop;

public enum CoopInputAction { None, Cast, Dodge, Potion, Ready }
/// <summary>Intent only. The authenticated player ID is supplied separately by the server transport.</summary>
public sealed record CoopInput([property: JsonRequired] long Sequence, [property: JsonRequired] long ClientTick,
    int MoveX = 0, int MoveZ = 0, CoopInputAction Action = CoopInputAction.None, string SkillId = "", int TargetId = 0);
public sealed record CoopInputEnvelope(int PlayerId, CoopInput Input);
public sealed record CoopInputResult(bool Accepted, string Code, long Sequence, long ServerTick, long AcceptedSequence, long ProcessedSequence);
public sealed record CoopEvent(long Tick, string Kind, int ActorId = 0, int TargetId = 0, int Amount = 0, string ContentId = "", long ActionId = 0);
public sealed record CoopRewardReceipt(string Id, int PlayerId, string EncounterId, int Attempt, int Experience, int Ash, CombatItem Item);
public sealed record CoopStatus(string Id, int SourceId, long ExpiresTick, long NextTick, long ActionId);
public sealed record CoopPending(string SkillId, int TargetId, Position Target, long ResolveTick, long ActionId);
public sealed record CoopProjectile(long Id, int SourceId, Position Position, Position Target, int Damage, DamageFamily Family, int Radius, string Status, long ExpiresTick, long ActionId);
public sealed record CoopWarning(long Id, int SourceId, string SkillId, string Shape, Position Position, Position End, int Radius, int Damage, DamageFamily Family, long ResolveTick, long ActionId);
public sealed record CoopQueuedInput(int PlayerId, long ApplyTick, CoopInput Input);
public sealed record CoopInputReceipt(long Sequence, string Hash);
public sealed record CoopActor
{
    public int Id { get; init; }
    public int PlayerId { get; init; }
    public string DefinitionId { get; init; } = "";
    public string Role { get; init; } = "";
    public Position Position { get; set; }
    public int Health { get; set; }
    public int MaxHealth { get; init; }
    public int Armor { get; init; }
    public int Barrier { get; set; }
    public long RecoveryUntil { get; set; }
    public long InvulnerableUntil { get; set; }
    public long StaggerReadyTick { get; set; }
    public int Cycle { get; set; }
    public bool Resurrected { get; set; }
    public bool DeathProcessed { get; set; }
    public CoopPending? Pending { get; set; }
    public List<CoopStatus> Statuses { get; init; } = [];
}
public sealed record CoopPlayer
{
    public int Id { get; init; }
    public bool Connected { get; set; } = true;
    public bool Ready { get; set; }
    public int Momentum { get; set; }
    public int PotionCharges { get; set; } = 3;
    public long PotionReadyTick { get; set; }
    public long DodgeReadyTick { get; set; }
    public int MoveX { get; set; }
    public int MoveZ { get; set; }
    public long LastInputTick { get; set; }
    public long AcceptedSequence { get; set; }
    public long ProcessedSequence { get; set; }
    public SortedDictionary<string, long> Cooldowns { get; init; } = new(StringComparer.Ordinal);
    public List<CoopInputReceipt> Receipts { get; init; } = [];
}
public sealed record CoopSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public string RulesVersion { get; init; } = "coop.1";
    public string ContentHash { get; init; } = "";
    public ulong Seed { get; init; }
    public string MatchId { get; init; } = "";
    public long Tick { get; set; }
    public int EncounterIndex { get; set; }
    public int Attempt { get; set; }
    public bool Cleared { get; set; }
    public bool AwaitingRetry { get; set; }
    public bool Completed { get; set; }
    public int NextActorId { get; set; } = 3;
    public long NextObjectId { get; set; } = 1;
    public long NextActionId { get; set; } = 1;
    public ulong CombatRng { get; set; }
    public ulong LootRng { get; set; }
    public List<CoopActor> Actors { get; init; } = [];
    public List<CoopPlayer> Players { get; init; } = [];
    public List<CoopProjectile> Projectiles { get; init; } = [];
    public List<CoopWarning> Warnings { get; init; } = [];
    public List<CoopQueuedInput> Inputs { get; init; } = [];
    public List<CoopRewardReceipt> Rewards { get; init; } = [];
    public int PeakActors { get; set; }
    public int PeakProjectiles { get; set; }
    public int PeakWarnings { get; set; }
    public int PeakQueuedInputs { get; set; }
}
public sealed record CoopActorView(int Id, int PlayerId, string DefinitionId, string Role, Position Position,
    int Health, int MaxHealth, int Barrier, string State, string[] Statuses, string? PendingSkill, int TelegraphTicks, bool Shielded);
public sealed record CoopSkillView(string Id, string Name, string Shape, int Cost, int Generate, int RemainingTicks, int Range, int Radius);
public sealed record CoopPlayerView(int Id, bool Connected, bool Ready, string Loadout, int Momentum, int PotionCharges,
    int PotionCooldownTicks, int DodgeCooldownTicks, long AcceptedSequence, long ProcessedSequence, CoopSkillView[] Skills);
public sealed record CoopCounters(int Actors, int Projectiles, int Warnings, int QueuedInputs, int PeakActors, int PeakProjectiles, int PeakWarnings, int PeakQueuedInputs);
public sealed record CoopView(long Tick, string ContentHash, string MatchId, string EncounterId, int EncounterIndex, int Attempt,
    string ContextKey, bool Cleared, bool AwaitingRetry, bool Completed, RoomDefinition Room, CoopActorView[] Actors,
    CoopPlayerView[] Players, CoopProjectile[] Projectiles, CoopWarning[] Warnings, CoopRewardReceipt[] Rewards, CoopCounters Counters);
