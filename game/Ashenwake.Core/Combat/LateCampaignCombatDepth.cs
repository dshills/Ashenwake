using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

public sealed partial class CombatSession
{
    private const string OathWard = "campaign.oath_ward";
    private const int OathWardRadius = 3000, OathWardWindup = 45, OathWardRecovery = 90, OathWardBarrier = 36;

    private IEnumerable<CombatActor> OathWardAllies(CombatActor source, Position center)
        => DirgeAllies(source, center, OathWardRadius).Where(ally =>
            ally.DefinitionId is "enemy.oath_giant" or "enemy.bone_sentinel" &&
            !EndgameMechanicNoRewards(ally) && ally.Barrier < OathWardBarrier);

    private bool TryOathWard(CombatActor actor, string pattern)
    {
        if (pattern != "OathWard" || actor.SpecialCycle % 2 != 0 || (Tick + actor.Id) % 3 != 0 ||
            _state.Campaign!.Actors[actor.Id].IsEcho || !OathWardAllies(actor, actor.Position).Any()) return false;
        if (_state.Campaign.Hazards.Count >= 32) { Budget(0); return false; }
        Warn(actor, OathWard, actor.Position, OathWardRadius, OathWardWindup, 0, DamageFamily.PhysicalCrush);
        actor.RecoveryUntil = Tick + OathWardWindup + OathWardRecovery;
        actor.State = "Windup"; actor.SpecialCycle++;
        return true;
    }

    private bool ResolveOathWard(CombatActor source, CampaignHazard hazard)
    {
        if (hazard.ContentId != OathWard) return false;
        if (source.Health <= 0 || Stunned(source) || CampaignPattern(source) != "OathWard") return true;
        foreach (var ally in OathWardAllies(source, hazard.Position))
        {
            int granted = OathWardBarrier - ally.Barrier;
            ally.Barrier += granted;
            Emit("BarrierGranted", source.Id, ally.Id, granted, OathWard, hazard.ActionId);
        }
        return true;
    }

    private void ValidateOathWard()
    {
        var campaign = _state.Campaign!;
        foreach (var hazard in campaign.Hazards.Where(h => h.ContentId == OathWard))
        {
            var source = _state.Actors.FirstOrDefault(a => a.Id == hazard.SourceId)
                ?? throw new InvalidDataException("Oath ward source is missing.");
            if (hazard.Kind != "Circle" || hazard.Position != hazard.End || hazard.Radius != OathWardRadius ||
                hazard.Damage != 0 || hazard.Family != DamageFamily.PhysicalCrush || hazard.Status != "" ||
                hazard.ResolveTick > Tick + OathWardWindup || source.Health <= 0 || Stunned(source) ||
                campaign.Actors[source.Id].IsEcho || CampaignPattern(source) != "OathWard")
                throw new InvalidDataException("Invalid oath ward channel.");
        }
    }

    private CombatHazardView CampaignHazardView(CampaignHazard hazard)
    {
        var view = new CombatHazardView(hazard.Id, hazard.Kind, hazard.Position, hazard.End, hazard.Radius,
            Math.Max(0, hazard.ResolveTick - Tick), hazard.ContentId, hazard.SourceId);
        var source = _state.Actors.FirstOrDefault(a => a.Id == hazard.SourceId);
        if (source is null || !IsCampaignBoss(source)) return view;
        int phase = _state.Campaign!.BossPhase;
        // Ordinals belong to the announced attack sequence, not the remaining
        // warnings. Resolving an earlier strike must not renumber a later one.
        (int index, int count) = (source.DefinitionId, hazard.ContentId) switch
        {
            ("boss.covenant_warden", "campaign.oath_mark") => (1, 2),
            ("boss.covenant_warden", "campaign.covenant_fault") => (2, 2),
            ("boss.breach_heart", "campaign.breach_echo") => (1, phase),
            ("boss.breach_heart", "campaign.returning_echo") when phase >= 2 => (phase, phase),
            ("boss.breach_heart", "campaign.seal_sweep") when phase == 3 => (2, 3),
            _ => (0, 0)
        };
        return view with { SequenceIndex = index, SequenceCount = count };
    }
}
