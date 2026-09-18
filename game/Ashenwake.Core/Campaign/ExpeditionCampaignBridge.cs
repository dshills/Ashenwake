using Ashenwake.Core.Combat;

namespace Ashenwake.Core.Expedition;

public sealed partial class ExpeditionSession
{
    // Campaign owns the active arena; the dormant hub carries the checked permanent Godwrought projection.
    internal string[] ReconcileExternalGodwrought(IReadOnlyList<CombatActorView> before, IReadOnlyList<CombatEvent> events)
    {
        var messages = new List<string>(); var item = EquippedGodwrought();
        if (item is not null)
            foreach (var death in events.Where(e => e.Kind == "EntityKilled" && e.ActorId == 1))
            {
                var victim = before.FirstOrDefault(a => a.Id == death.TargetId);
                if (victim is null || victim.Faction != CombatFaction.Enemy || victim.CorpseConsumed) continue;
                if (victim.Statuses.Any(s => s.Id == "Burning") || death.ContentId == "Burning" || events.Any(e => e.Kind == "StatusApplied" && e.TargetId == death.TargetId && e.ContentId == "Burning"))
                    messages.AddRange(adventure.RecordBurningKill(nextKillSequence++, item.InstanceId).Events);
            }
        adventure.ElapseCombatTicks(1); SynchronizeBuild(); return messages.ToArray();
    }
    internal void ClearExternalGodwroughtEffects()
    {
        adventure.ElapseCombatTicks(150); SynchronizeBuild();
    }
}
