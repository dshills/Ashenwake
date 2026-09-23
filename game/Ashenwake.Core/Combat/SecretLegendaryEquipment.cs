using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

public sealed partial class CombatSession
{
    private void RewardGriefsReprieve(CombatActor target, Hit hit)
    {
        if (!_state.ProgressionBuild.GriefsReprieve || Player.Health <= 0 || target.Health <= 0 ||
            target.Faction != CombatFaction.Enemy || hit.SourceId != 1 || hit.OwnerId != 1 || hit.Dot || hit.Reflected ||
            _state.Legendary?.GriefReadyTick > Tick) return;
        bool interrupted = target.Pending is not null || _state.Campaign?.Hazards.Any(h => h.SourceId == target.Id && h.ResolveTick > Tick) == true;
        if (!interrupted) return;
        (_state.Legendary ??= new()).GriefReadyTick = Tick + 90;
        int before = Player.Health;
        HealPlayer(20, LegendaryEquipment.GriefPower, hit.ActionId);
        Emit("LegendaryTriggered", 1, target.Id, Player.Health - before, LegendaryEquipment.GriefPower, hit.ActionId);
    }

    private void SnareWidowthorn(CombatActor corpse, Hit hit)
    {
        if (!_state.ProgressionBuild.Widowthorn || Player.Health <= 0 || hit.OwnerId != 1 || !hit.Dot || hit.ContentId != "Poisoned" ||
            hit.Depth >= MaxChainDepth || _state.Legendary?.WidowthornReadyTick > Tick || !LeavesCorpse(corpse)) return;
        var targets = Hostiles(Player, corpse.Position, 2600).Where(a => a.InvulnerableUntil <= Tick && !IsRituallyShielded(a) &&
            a.Role is not ("BellSaint" or "Beast" or "Bell" or "Anchor") && !HasElite(a, "Hunter") &&
            (a.Statuses.Count < 32 || a.Statuses.Any(s => s.Id == "Rooted" && s.OwnerId == 1))).Take(3).ToArray();
        if (targets.Length == 0) return;
        (_state.Legendary ??= new()).WidowthornReadyTick = Tick + 90;
        foreach (var target in targets)
            ApplyStatus(target, "Rooted", hit with { TargetId = target.Id, ContentId = "effect.widowthorn", Depth = hit.Depth + 1 }, "");
        Emit("LegendaryTriggered", 1, corpse.Id, targets.Length, LegendaryEquipment.WidowthornPower, hit.ActionId, hit.Depth + 1);
    }

    private void ObserveEmberwakeDodge(Hit hit, CombatActor? source, CombatActor target)
    {
        if (!_state.ProgressionBuild.Emberwake || target.Id != 1 || Player.Health <= 0 || source?.Faction != CombatFaction.Enemy ||
            hit.Dot || hit.Reflected || hit.Family != DamageFamily.Fire || hit.Damage <= 0 ||
            Player.InvulnerableUntil <= Tick || _state.Legendary is not { } state || state.EmberwakeDodgeUntil <= Tick ||
            state.EmberwakeReadyTick > Tick) return;
        state.EmberwakeUntil = Tick + 120; state.EmberwakeReadyTick = Tick + 90;
        Emit("LegendaryTriggered", 1, source.Id, 120, LegendaryEquipment.EmberwakePower, hit.ActionId);
    }

    private int EmberwakeDamageBonus(Hit hit)
        => _state.ProgressionBuild.Emberwake && _state.Legendary?.EmberwakeUntil > Tick &&
            hit.SourceId == 1 && hit.OwnerId == 1 && !hit.Dot && !hit.Reflected && hit.Depth == 0 &&
            _content.Skills.Any(s => s.Id == hit.ContentId) ? 2000 : 0;

    private void TrimSecretLegendaryState(LegendaryCombatState state)
    {
        if (!_state.ProgressionBuild.GriefsReprieve || Player.Health <= 0 || state.GriefReadyTick <= Tick) state.GriefReadyTick = 0;
        if (!_state.ProgressionBuild.Widowthorn || Player.Health <= 0 || state.WidowthornReadyTick <= Tick) state.WidowthornReadyTick = 0;
        if (!_state.ProgressionBuild.Emberwake || Player.Health <= 0)
        { state.EmberwakeDodgeUntil = 0; state.EmberwakeUntil = 0; state.EmberwakeReadyTick = 0; }
        else
        {
            if (state.EmberwakeDodgeUntil <= Tick) state.EmberwakeDodgeUntil = 0;
            if (state.EmberwakeUntil <= Tick) state.EmberwakeUntil = 0;
            if (state.EmberwakeReadyTick <= Tick) state.EmberwakeReadyTick = 0;
        }
    }

    private void ValidateSecretLegendaryState()
    {
        bool Timer(long until, int duration, bool equipped) => until == 0 || equipped && Player.Health > 0 && until >= Tick && until <= Tick + duration;
        if (_state.Legendary is { } state && (!Timer(state.GriefReadyTick, 90, _state.ProgressionBuild.GriefsReprieve) ||
            !Timer(state.WidowthornReadyTick, 90, _state.ProgressionBuild.Widowthorn) ||
            !Timer(state.EmberwakeDodgeUntil, 7, _state.ProgressionBuild.Emberwake) || state.EmberwakeDodgeUntil > Player.InvulnerableUntil ||
            !Timer(state.EmberwakeUntil, 120, _state.ProgressionBuild.Emberwake) || !Timer(state.EmberwakeReadyTick, 90, _state.ProgressionBuild.Emberwake)))
            throw new InvalidDataException("Invalid secret Legendary readiness or dodge window.");
        foreach (var status in _state.Actors.SelectMany(a => a.Statuses).Where(s => s.OriginSkill == "effect.widowthorn"))
            if (!_state.ProgressionBuild.Widowthorn || Player.Health <= 0 || status.Id != "Rooted" || status.OwnerId != 1 ||
                status.Depth < 1 || status.ExpiresTick > Tick + 45 || status.FragmentId.Length != 0)
                throw new InvalidDataException("Invalid Widowthorn snare.");
    }
}
