using System.Globalization;

namespace Ashenwake.Core.Combat;

/// <summary>Actual health removed after mitigation and barriers; overkill is excluded.</summary>
public sealed record DeathDamageEntry(long Tick, int SourceId, string SourceName, string AttackId, string AttackName,
    DamageFamily Family, int Damage)
{
    public int OwnerId { get; init; }
    public string SourceDefinitionId { get; init; } = "";
    public bool DamageOverTime { get; init; }
    public string EffectId { get; init; } = "";
}

/// <summary>A transient observation of a death, never included in saves, replay hashes, or combat rules.</summary>
public sealed record DeathRecap(long Tick, string RoomId, DeathDamageEntry KillingBlow,
    IReadOnlyList<DeathDamageEntry> RecentDamage, IReadOnlyList<CombatStatusView> Conditions);

public sealed partial class CombatSession
{
    public const int DeathRecapWindowTicks = 240;
    public const int DeathRecapMaximumHits = 64;
    private readonly List<DeathDamageEntry> recentIncomingDamage = [];
    public DeathRecap? LastDeathRecap { get; private set; }

    // Permanent-build projection replaces CombatSession without starting a new encounter. Its transient
    // observations follow that replacement explicitly, while snapshot restore intentionally starts empty.
    internal void PreserveDeathRecapFrom(CombatSession previous)
    {
        if (EncounterId != previous.EncounterId || Tick != previous.Tick) return;
        recentIncomingDamage.Clear(); recentIncomingDamage.AddRange(previous.recentIncomingDamage);
        LastDeathRecap = previous.LastDeathRecap;
    }

    private void ObserveIncomingDamage(Hit hit, CombatActor? source, CombatActor target, int amount)
    {
        if (training || target.Id != 1 || amount <= 0) return;
        string attackId = hit.OriginSkill.Length > 0 ? hit.OriginSkill : hit.ContentId;
        string definition = source?.DefinitionId ?? "";
        string sourceName = source is null ? "Unknown source" : source.Id == 1 ? "You" : RecapContentLabel(definition);
        var entry = new DeathDamageEntry(Tick, hit.SourceId, sourceName, attackId,
            _content.Skills.FirstOrDefault(s => s.Id == attackId)?.Name ?? RecapContentLabel(attackId), hit.Family, amount)
        {
            OwnerId = hit.OwnerId,
            SourceDefinitionId = definition,
            DamageOverTime = hit.Dot,
            EffectId = hit.ContentId
        };
        recentIncomingDamage.RemoveAll(row => row.Tick < Tick - DeathRecapWindowTicks);
        if (recentIncomingDamage.Count == DeathRecapMaximumHits) recentIncomingDamage.RemoveAt(0);
        recentIncomingDamage.Add(entry);
        if (target.Health > 0) return;
        LastDeathRecap = new(Tick, _state.RoomEncounterId ?? EncounterId, entry,
            Array.AsReadOnly(recentIncomingDamage.ToArray()),
            Array.AsReadOnly(target.Statuses.Where(s => s.ExpiresTick > Tick)
                .Select(s => new CombatStatusView(s.Id, s.SourceId, s.ExpiresTick - Tick, s.Stacks)).ToArray()));
    }

    private static string RecapContentLabel(string id)
    {
        string label = id[(id.LastIndexOf('.') + 1)..].Replace('_', ' ');
        return label.Length == 0 ? "Unknown attack" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(label);
    }
}
