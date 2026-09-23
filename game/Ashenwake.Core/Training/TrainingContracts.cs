using Ashenwake.Core.Combat;

namespace Ashenwake.Core.Training;

public enum TrainingTargetMode { Single, Group, Melee, Ranged, Mixed }
public sealed record TrainingDamageRow(string Category, string SourceId, string Name, DamageFamily Family, long Damage, long Hits);
public sealed record TrainingTriggerRow(string Kind, string SourceId, string Name, long Count);
public sealed record TrainingResourceRow(string Reason, long Increased, long Decreased);
public sealed record TrainingReport(TrainingTargetMode Mode, long ElapsedTicks, double ElapsedSeconds, long TotalDamage,
    double DamagePerSecond, long ResourceIncreased, long ResourceDecreased, int CurrentResource, string ResourceName,
    int TargetsDefeated, bool Complete, IReadOnlyList<TrainingDamageRow> Damage,
    IReadOnlyList<TrainingTriggerRow> Triggers, IReadOnlyList<TrainingResourceRow> Resources)
{
    public TrainingDefenseReport Defense { get; init; } = new(0, 0, 0, 0, 0, 0, 0, [], []);
}
/// <summary>For resolved incoming hits: raw = mitigation + immunity + barrier + health + overkill.
/// Raw retains the attack's modifiers and vulnerability, but excludes armor/resistance, immunity and barriers.
/// Dodged or out-of-range attacks that never resolve against the player have no measurable damage here.</summary>
public sealed record TrainingDefenseReport(long RawDamage, long MitigatedDamage, long ImmuneDamage,
    long BarrierAbsorbed, long HealthLost, long Overkill, long Hits,
    IReadOnlyList<TrainingDefenseRow> Damage, IReadOnlyList<TrainingTriggerRow> Triggers);
public sealed record TrainingDefenseRow(string SourceId, string Name, string AttackId, string AttackName,
    DamageFamily Family, long RawDamage, long MitigatedDamage, long ImmuneDamage,
    long BarrierAbsorbed, long HealthLost, long Overkill, long Hits);
public sealed record TrainingFrame(CombatCommand[] Commands, string StateHash, string ReportHash);
public sealed record TrainingReplay(int SchemaVersion, CombatSnapshot Initial, TrainingTargetMode Mode, TrainingFrame[] Frames);
internal sealed record TrainingHit(string Category, string SourceId, string Name, DamageFamily Family, int Damage);
