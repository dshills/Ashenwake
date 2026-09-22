using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

public sealed partial class CombatSession
{
    private const string SporeMend = "campaign.spore_mend", ForgeBellows = "campaign.forge_bellows";
    private const int SupportRadius = 3000, SupportWindup = 42, SupportRecovery = 90, ForgeDuration = 90;

    public static bool IsSupportHazard(string contentId) => contentId is "elite.dirgebound" or SporeMend or ForgeBellows or OathWard;

    private IEnumerable<CombatActor> MidgameSupportAllies(CombatActor source, Position center, bool forge)
        => DirgeAllies(source, center, SupportRadius).Where(ally => !EndgameMechanicNoRewards(ally) &&
            (forge ? ally.DefinitionId == "enemy.forge_sentinel" : ally.Health < ally.MaxHealth));

    private bool TryMidgameSupport(CombatActor actor, string pattern)
    {
        if (pattern is not ("SporeMend" or "ForgeBellows") || actor.SpecialCycle % 2 != 0 || (Tick + actor.Id) % 3 != 0 ||
            _state.Campaign!.Actors[actor.Id].IsEcho || !MidgameSupportAllies(actor, actor.Position, pattern == "ForgeBellows").Any()) return false;
        // One fixed warning owns the whole channel. Existing death and hard-control
        // cleanup cancel it, while normal recovery prevents immediately restarting.
        if (_state.Campaign.Hazards.Count >= 32) { Budget(0); return false; }
        Warn(actor, pattern == "SporeMend" ? SporeMend : ForgeBellows, actor.Position, SupportRadius, SupportWindup, 0,
            pattern == "SporeMend" ? DamageFamily.Venom : DamageFamily.Fire);
        actor.RecoveryUntil = Tick + SupportWindup + SupportRecovery;
        actor.State = "Windup"; actor.SpecialCycle++;
        return true;
    }

    private bool ResolveMidgameSupport(CombatActor source, CampaignHazard hazard)
    {
        if (hazard.ContentId is not (SporeMend or ForgeBellows)) return false;
        bool forge = hazard.ContentId == ForgeBellows;
        if (source.Health <= 0 || Stunned(source) || CampaignPattern(source) != (forge ? "ForgeBellows" : "SporeMend")) return true;
        foreach (var ally in MidgameSupportAllies(source, hazard.Position, forge))
        {
            if (forge)
            {
                _state.Campaign!.Actors[ally.Id].ForgeOverchargeUntil = Tick + ForgeDuration;
                Emit("EnemyOvercharged", source.Id, ally.Id, ForgeDuration, ForgeBellows, hazard.ActionId);
            }
            else
            {
                int healed = Math.Min(18, ally.MaxHealth - ally.Health);
                ally.Health += healed;
                Emit("Healed", source.Id, ally.Id, healed, SporeMend, hazard.ActionId);
            }
        }
        return true;
    }

    private int ForgeOverchargeTicks(CombatActor actor)
        => actor.Health <= 0 ? 0 : Remaining(_state.Campaign?.Actors.GetValueOrDefault(actor.Id)?.ForgeOverchargeUntil ?? 0);

    private void ClearCampaignSupport(CombatActor actor)
    {
        if (_state.Campaign is not { } campaign) return;
        if (campaign.Actors.TryGetValue(actor.Id, out var state)) state.ForgeOverchargeUntil = 0;
        campaign.Hazards.RemoveAll(h => h.SourceId == actor.Id && h.ContentId is SporeMend or ForgeBellows or OathWard);
    }

    private int BossGuardedTicks(CombatActor actor)
        => actor.Health <= 0 || actor.DefinitionId is not ("boss.furnace_spindle" or "boss.covenant_warden") ? 0 :
            Remaining(_state.Campaign?.Actors.GetValueOrDefault(actor.Id)?.GuardedUntil ?? 0);

    private int BossRecoveryTicks(CombatActor actor)
    {
        if (actor.Health <= 0 || actor.DefinitionId is not ("boss.rootheart" or "boss.furnace_spindle" or "boss.covenant_warden" or "boss.breach_heart") ||
            !IsCampaignBoss(actor) || CampaignShielded(actor) || CampaignDefenseBonus(actor) > 0 ||
            actor.Pending is not null || _state.Campaign!.Hazards.Any(h => h.SourceId == actor.Id)) return 0;
        return Remaining(actor.RecoveryUntil);
    }

    private void ValidateMidgameSupport()
    {
        var campaign = _state.Campaign!;
        foreach (var pair in campaign.Actors)
        {
            long until = pair.Value.ForgeOverchargeUntil;
            if (until == 0) continue;
            var actor = _state.Actors.Single(a => a.Id == pair.Key);
            if (until < Tick || until > Tick + ForgeDuration || actor.Health <= 0 || actor.DefinitionId != "enemy.forge_sentinel" ||
                pair.Value.IsEcho || _content.Campaign?.Behaviors.Any(b => b.Pattern == "ForgeBellows") != true)
                throw new InvalidDataException("Invalid forge overcharge ownership or duration.");
        }
        foreach (var hazard in campaign.Hazards.Where(h => h.ContentId is SporeMend or ForgeBellows))
        {
            bool forge = hazard.ContentId == ForgeBellows;
            var source = _state.Actors.Single(a => a.Id == hazard.SourceId);
            if (hazard.Kind != "Circle" || hazard.Position != hazard.End || hazard.Radius != SupportRadius || hazard.Damage != 0 ||
                hazard.Family != (forge ? DamageFamily.Fire : DamageFamily.Venom) || hazard.Status != "" ||
                hazard.ResolveTick > Tick + SupportWindup || source.Health <= 0 || Stunned(source) || campaign.Actors[source.Id].IsEcho ||
                CampaignPattern(source) != (forge ? "ForgeBellows" : "SporeMend"))
                throw new InvalidDataException("Invalid midgame support channel.");
        }
    }
}
