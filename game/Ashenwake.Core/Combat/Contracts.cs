using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

public enum CombatCommandKind { Move, Stop, Cast, Dodge, Potion, Pickup, Equip, EquipFragment, UnequipFragment, SetMutation }
public enum DamageFamily { PhysicalSlash, PhysicalPierce, PhysicalCrush, Fire, Frost, Storm, Decay, Venom, Void }
public enum AnatomySlot { Mind, Eyes, Heart, Spine, Arms, Legs }
public enum CombatFaction { Player, Enemy, Ally }
public sealed record CombatCommand(CombatCommandKind Kind, int ActorId = 1, string SkillId = "", int TargetId = 0, int X = 0, int Z = 0, string ContentId = "", long ItemId = 0);
public sealed record CombatEvent(long Tick, string Kind, int ActorId = 0, int TargetId = 0, int Amount = 0, string ContentId = "", long ActionId = 0, int Depth = 0);
public sealed record CombatStatusView(string Id, int SourceId, long RemainingTicks, int Stacks);
public sealed record CombatActorView(int Id, Position Position, int Health, int MaxHealth, CombatFaction Faction, string Role, int TelegraphTicks, string State, int Barrier, IReadOnlyList<CombatStatusView> Statuses);
public sealed record CombatProjectileView(long Id, Position Position, Position Target, string ContentId, int OwnerId);
public sealed record CombatAreaView(long Id, Position Position, int Radius, string ContentId, long RemainingTicks, int OwnerId);
public sealed record CombatItem(long Id, string DefinitionId, string Name, string Slot, string Rarity, int Damage, int Armor, int CriticalBasisPoints);
public sealed record CombatLoot(long Id, Position Position, CombatItem Item);
public sealed record CombatSkillView(string Id, string Name, string Shape, int Cost, int Generate, int CooldownTicks, int RemainingTicks, string Mutation);
public sealed record CombatFragmentView(string Id, string Name, AnatomySlot Slot, string Lineage, int Resonance, string Description, bool Equipped);
public sealed record CombatMutationView(string Id, string SkillId, string Name, string Description);
public sealed record CombatView(long Tick, string Preset, IReadOnlyList<CombatActorView> Actors, IReadOnlyList<CombatProjectileView> Projectiles,
    IReadOnlyList<CombatAreaView> Areas, IReadOnlyList<CombatLoot> Loot, IReadOnlyList<CombatItem> Inventory,
    IReadOnlyList<CombatSkillView> Skills, IReadOnlyList<CombatFragmentView> Fragments, IReadOnlyList<CombatMutationView> Mutations,
    IReadOnlyDictionary<string, long> Equipment, int Momentum, int MaxMomentum, int Barrier, int PotionCharges, int PotionCooldownTicks,
    int DodgeCooldownTicks, int Resonance, int PendingEffects, int PeakEffects, int RejectedEffects, string ContentVersion);

public sealed record CombatStatus
{
    public string Id { get; set; } = "";
    public int SourceId { get; set; }
    public int OwnerId { get; set; }
    public string FragmentId { get; set; } = "";
    public long NextTick { get; set; }
    public long ExpiresTick { get; set; }
    public int Stacks { get; set; } = 1;
    public long ActionId { get; set; }
    public int Depth { get; set; }
}
public sealed record CombatPending(string SkillId, int TargetId, Position Target, long ResolveTick, long ActionId, int Depth = 0);
public sealed record CombatActor
{
    public int Id { get; init; }
    public string DefinitionId { get; init; } = "";
    public Position Position { get; set; }
    public CombatFaction Faction { get; init; }
    public string Role { get; init; } = "";
    public int Health { get; set; }
    public int MaxHealth { get; init; }
    public int Barrier { get; set; }
    public int Armor { get; init; }
    public int Resistance { get; init; }
    public int MoveX { get; set; }
    public int MoveZ { get; set; }
    public string State { get; set; } = "Acquire";
    public long RecoveryUntil { get; set; }
    public long InvulnerableUntil { get; set; }
    public long ExpiresTick { get; set; }
    public int OwnerId { get; init; }
    public int Generation { get; init; }
    public string FragmentId { get; init; } = "";
    public bool Elite { get; init; }
    public bool DeathProcessed { get; set; }
    public CombatPending? Pending { get; set; }
    public List<CombatStatus> Statuses { get; init; } = [];
}
public sealed record CombatProjectile(long Id, int OwnerId, int SourceId, Position Position, Position Target, int TargetId, string SkillId, int Damage, DamageFamily Family, long ExpiresTick, long ActionId, int Depth, string FragmentId = "");
public sealed record CombatArea(long Id, int OwnerId, int SourceId, Position Position, int Radius, string SkillId, int Damage, DamageFamily Family, long NextTick, long ExpiresTick, long ActionId, int Depth);
public sealed record CombatSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public string RulesVersion { get; init; } = "combat.1";
    public string ContentHash { get; init; } = "";
    public string Preset { get; init; } = "standard";
    public long Tick { get; set; }
    public ulong Seed { get; init; }
    public RngStates Rng { get; set; } = new(1, 2, 3, 4);
    public List<CombatActor> Actors { get; init; } = [];
    public List<CombatProjectile> Projectiles { get; init; } = [];
    public List<CombatArea> Areas { get; init; } = [];
    public List<CombatLoot> Loot { get; init; } = [];
    public List<CombatItem> Inventory { get; init; } = [];
    public SortedDictionary<string, long> Equipment { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, string> Fragments { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, string> Mutations { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> Cooldowns { get; init; } = new(StringComparer.Ordinal);
    public int Momentum { get; set; }
    public long LastAggressionTick { get; set; }
    public int PotionCharges { get; set; } = 3;
    public long PotionReadyTick { get; set; }
    public long DodgeReadyTick { get; set; }
    public CombatCommand? BufferedCommand { get; set; }
    public long BufferExpiresTick { get; set; }
    public long NextObjectId { get; set; } = 1;
    public int NextActorId { get; set; } = 2;
    public long NextActionId { get; set; } = 1;
    public int PeakEffects { get; set; }
    public int RejectedEffects { get; set; }
}
