using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

public sealed partial class CombatSession
{
    private CombatLegendaryView? LegendaryView()
    {
        var state = _state.Legendary;
        var build = _state.ProgressionBuild;
        if (state is null && !build.VirulentWake && !build.RallyingChorus && !build.CinderCycle) return null;
        return new(state?.OathCharge ?? 0, Remaining(state?.OathUntil ?? 0), Remaining(state?.WidowUntil ?? 0))
        {
            VirulentEquipped = build.VirulentWake,
            VirulentRemainingTicks = Remaining(state?.VirulentReadyTick ?? 0),
            ChorusEquipped = build.RallyingChorus,
            ChorusSummons = _state.Actors.Count(a => a.Faction == CombatFaction.Ally && a.OwnerId == 1 && a.Health > 0 && a.ExpiresTick > Tick && !Stunned(a)),
            ChorusRemainingTicks = Remaining(state?.ChorusReadyTick ?? 0),
            CinderEquipped = build.CinderCycle,
            CinderRemainingTicks = Remaining(state?.CinderUntil ?? 0)
        };
    }

    private void PrepareCinderCycle(int paidCost, long action)
    {
        if (!_state.ProgressionBuild.CinderCycle || paidCost < 20) return;
        (_state.Legendary ??= new()).CinderUntil = Tick + 180;
        Emit("LegendaryReadied", 1, amount: 180, content: LegendaryEquipment.FurnacePower, action: action);
    }

    private void ReleaseCinderCycle(long action, bool cooling)
    {
        if (!_state.ProgressionBuild.CinderCycle || Player.Health <= 0 || _state.Legendary is not { CinderUntil: > 0 } state || state.CinderUntil <= Tick) return;
        state.CinderUntil = 0;
        int actual = Math.Min(12, cooling ? _state.Momentum : 100 - _state.Momentum);
        _state.Momentum += cooling ? -actual : actual;
        Emit("LegendaryTriggered", 1, amount: actual, content: LegendaryEquipment.FurnacePower, action: action);
    }

    private void MidgameLegendaryHit(Hit hit, CombatActor target)
    {
        if (Player.Health <= 0 || hit.OwnerId != 1 || hit.SourceId != 1 || hit.Dot || hit.Reflected || hit.Depth != 0 || target.Faction != CombatFaction.Enemy) return;
        var skill = _content.Skills.FirstOrDefault(s => s.Id == hit.ContentId);
        if (skill is null) return;
        if (Discipline is not ("Arcanist" or "Gravecaller") && skill.Generate > 0 && skill.Cost + (Mutation(skill.Id)?.ExtraCost ?? 0) == 0)
            ReleaseCinderCycle(hit.ActionId, cooling: false);
        if (!_state.ProgressionBuild.RallyingChorus || target.Health <= 0 || _state.Legendary?.ChorusReadyTick > Tick) return;
        var summons = _state.Actors.Where(a => a.Faction == CombatFaction.Ally && a.OwnerId == 1 && a.Health > 0 &&
            a.ExpiresTick > Tick && !Stunned(a) && Position.DistanceSquared(a.Position, target.Position) <= 6000L * 6000 &&
            _spatial.HasLineOfSight(a.Position, target.Position)).OrderBy(a => a.Id).Take(3).ToArray();
        if (summons.Length == 0) return;
        (_state.Legendary ??= new()).ChorusReadyTick = Tick + 90;
        foreach (var summon in summons)
            Enqueue(new(summon.Id, 1, target.Id, 8, DamageFamily.Void, "effect.rallying_chorus", hit.ActionId,
                Math.Max(1, summon.Generation), Reflected: true, SourceGeneration: summon.Generation));
        Emit("LegendaryTriggered", 1, target.Id, summons.Length, LegendaryEquipment.MourningPower, hit.ActionId, 1);
    }

    private void SpreadVirulentWake(CombatActor corpse, Hit hit)
    {
        if (!_state.ProgressionBuild.VirulentWake || Player.Health <= 0 || hit.OwnerId != 1 || !hit.Dot || hit.ContentId != "Poisoned" ||
            hit.OriginSkill == "effect.virulent_wake" || hit.Depth >= MaxChainDepth || _state.Legendary?.VirulentReadyTick > Tick || !LeavesCorpse(corpse)) return;
        var targets = Hostiles(Player, corpse.Position, 2600).Where(a => a.InvulnerableUntil <= Tick && !IsRituallyShielded(a) &&
            (a.Statuses.Count < 32 || a.Statuses.Any(s => s.Id == "Poisoned" && s.OwnerId == 1))).Take(3).ToArray();
        if (targets.Length == 0) return;
        (_state.Legendary ??= new()).VirulentReadyTick = Tick + 90;
        foreach (var target in targets)
            ApplyStatus(target, "Poisoned", hit with { TargetId = target.Id, ContentId = "effect.virulent_wake", Depth = hit.Depth + 1 }, "");
        Emit("LegendaryTriggered", 1, corpse.Id, targets.Length, LegendaryEquipment.RotwakePower, hit.ActionId, hit.Depth + 1);
    }

    private void TrimMidgameLegendaryState(LegendaryCombatState state)
    {
        if (!_state.ProgressionBuild.VirulentWake || Player.Health <= 0 || state.VirulentReadyTick <= Tick) state.VirulentReadyTick = 0;
        if (!_state.ProgressionBuild.RallyingChorus || Player.Health <= 0 || state.ChorusReadyTick <= Tick) state.ChorusReadyTick = 0;
        if (!_state.ProgressionBuild.CinderCycle || Player.Health <= 0 || state.CinderUntil <= Tick) state.CinderUntil = 0;
    }

    private void ValidateMidgameLegendaryState()
    {
        bool ValidTimer(long until, int duration, bool equipped) => until == 0 || equipped && Player.Health > 0 && until >= Tick && until <= Tick + duration;
        if (_state.Legendary is { } state && (!ValidTimer(state.VirulentReadyTick, 90, _state.ProgressionBuild.VirulentWake) ||
            !ValidTimer(state.ChorusReadyTick, 90, _state.ProgressionBuild.RallyingChorus) || !ValidTimer(state.CinderUntil, 180, _state.ProgressionBuild.CinderCycle)))
            throw new InvalidDataException("Invalid midgame Legendary cooldown or charge.");
        foreach (var status in _state.Actors.SelectMany(a => a.Statuses).Where(s => s.OriginSkill == "effect.virulent_wake"))
            if (!_state.ProgressionBuild.VirulentWake || Player.Health <= 0 || status.Id != "Poisoned" || status.OwnerId != 1 ||
                status.Depth < 1 || status.ExpiresTick > Tick + 90 || status.FragmentId.Length != 0)
                throw new InvalidDataException("Invalid Legendary poison spread.");
    }
}
