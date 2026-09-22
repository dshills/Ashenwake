namespace Ashenwake.Core.Combat;

public sealed partial class CombatSession
{
    private EndgameHazardView EndgameHazardCue(EndgameHazard hazard)
    {
        bool temporal = hazard.ContentId is "hunt.orrun.numbered_fault" or "hunt.orrun.oathless_slam" or "hunt.nhal.remembered_rhythm";
        bool lane = hazard.ContentId is "hunt.serath.procession" or "hunt.serath.bell_pulse" or "hunt.nhal.absent_lane";
        int ordinal = hazard.Sequence is >= 1 and <= 3 ? hazard.Sequence : 0;
        return new(hazard.Id, hazard.Kind, hazard.Position, hazard.End, hazard.Radius, Tick < hazard.StartsTick ? "Warning" : "Active",
            Math.Max(0, (Tick < hazard.StartsTick ? hazard.StartsTick : hazard.EndsTick) - Tick), hazard.ContentId, hazard.SourceId, hazard.Sequence)
        {
            SequenceIndex = temporal ? ordinal : 0,
            SequenceCount = temporal && ordinal > 0 ? 3 : 0,
            LaneIndex = lane ? ordinal : 0
        };
    }

    private EndgameBossCueView? EndgameBossCue()
    {
        if (_state.Endgame is not { } endgame || Player.Health <= 0) return null;
        // Mirrorborn copies retain the definition, but are never the arena's
        // objective. Do not promote a surviving copy after the original dies.
        var boss = _state.Actors.FirstOrDefault(actor => IsEndgameBoss(actor) && actor.Health > 0 &&
            _state.Campaign?.Actors.GetValueOrDefault(actor.Id)?.IsEcho == false);
        if (boss is null) return null;
        bool shielded = EndgameShielded(boss);
        string role = EndgamePattern switch
        {
            "Vael2" => "RebuildingLimb",
            "Ilyra2" => "BroodChannel",
            "Ilyra3" => "SeedGuard",
            "Serath2" or "Nhal2" => "MarkedEcho",
            "Orrun3" => "ContractSeal",
            "Nhal1" => "AbsenceAnchor",
            _ => ""
        };
        int[] weakPoints = role == "" ? [] : endgame.ActorMechanics.Where(pair => pair.Value == role &&
            _state.Actors.Any(actor => actor.Id == pair.Key && actor.Health > 0)).Select(pair => pair.Key).Order().ToArray();
        bool permanent = !shielded && (role != "" && weakPoints.Length == 0 || EndgamePattern == "Orrun2" && endgame.DepositedTerms >= 2);
        // An exposure timer outlives the last destroyed weak point. Such a timer
        // no longer controls immunity, so it must not promise that it will close.
        int vulnerable = !shielded && !permanent && (role != "" || EndgamePattern is "Serath3" or "Nhal3")
            ? Remaining(endgame.ExposedUntil) : 0;
        int guarded = Remaining(_state.Campaign?.Actors.GetValueOrDefault(boss.Id)?.GuardedUntil ?? 0);
        bool warnings = endgame.Hazards.Any(hazard => hazard.SourceId == boss.Id) ||
            _state.Campaign!.Hazards.Any(hazard => hazard.SourceId == boss.Id) ||
            _state.Projectiles.Any(projectile => projectile.OwnerId == boss.Id && projectile.ExpiresTick > Tick) ||
            _state.Areas.Any(area => area.OwnerId == boss.Id && area.ExpiresTick > Tick);
        // Arena patterns run independently of the actor's action recovery.
        // Cap the opening at that scheduler's next possible attack as well.
        int recovery = shielded || guarded > 0 || boss.Pending is not null || boss.InvulnerableUntil > Tick || warnings ? 0 :
            Remaining(Math.Min(boss.RecoveryUntil, endgame.NextPatternTick));
        return new(boss.Id, shielded, guarded, vulnerable, permanent, recovery, shielded ? weakPoints : [],
            shielded ? endgame.Mechanisms.Where(MechanismAvailable).Select(mechanism => mechanism.Id).Order().ToArray() : []);
    }

    private int[]? EndgameHastedActors()
    {
        if (!EndgameRule("burning_haste") || Player.Health <= 0) return null;
        int[] ids = _state.Actors.Where(actor => actor.Health > 0 && actor.Faction == CombatFaction.Enemy &&
            actor.State is "Approach" or "Flee" or "Reposition" &&
            !actor.Statuses.Any(status => status.Id is "Rooted" or "Frozen" && status.ExpiresTick > Tick) &&
            (!actor.Statuses.Any(status => status.Id == "Staggered" && status.ExpiresTick > Tick) ||
                actor.Statuses.Any(status => status.Id == "Terrified" && status.ExpiresTick > Tick)) &&
            actor.Statuses.Any(status => status.Id == "Burning" && status.ExpiresTick > Tick)).Select(actor => actor.Id).Order().ToArray();
        return ids.Length > 0 ? ids : null;
    }
}
