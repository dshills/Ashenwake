using Ashenwake.Core.Combat;

namespace Ashenwake.Core.Training;

public enum TrainingTargetMode { Single, Group }
public sealed record TrainingDamageRow(string Category, string SourceId, string Name, DamageFamily Family, long Damage, long Hits);
public sealed record TrainingTriggerRow(string Kind, string SourceId, string Name, long Count);
public sealed record TrainingResourceRow(string Reason, long Increased, long Decreased);
public sealed record TrainingReport(TrainingTargetMode Mode, long ElapsedTicks, double ElapsedSeconds, long TotalDamage,
    double DamagePerSecond, long ResourceIncreased, long ResourceDecreased, int CurrentResource, string ResourceName,
    int TargetsDefeated, bool Complete, IReadOnlyList<TrainingDamageRow> Damage,
    IReadOnlyList<TrainingTriggerRow> Triggers, IReadOnlyList<TrainingResourceRow> Resources);
public sealed record TrainingFrame(CombatCommand[] Commands, string StateHash, string ReportHash);
public sealed record TrainingReplay(int SchemaVersion, CombatSnapshot Initial, TrainingTargetMode Mode, TrainingFrame[] Frames);
internal sealed record TrainingHit(string Category, string SourceId, string Name, DamageFamily Family, int Damage);
