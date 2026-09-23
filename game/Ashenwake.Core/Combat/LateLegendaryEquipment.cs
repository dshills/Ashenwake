using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

public sealed partial class CombatSession
{
    // Derived scratch space only: rebuilt from authoritative attack objects whenever receipts are trimmed.
    private readonly HashSet<long> _liveWitnessActions = new();
    private void RewardUnspokenVerdict(CombatActor target, Hit hit)
    {
        if (!_state.ProgressionBuild.UnspokenVerdict || Player.Health <= 0 || target.Health <= 0 ||
            target.Faction != CombatFaction.Enemy || hit.SourceId != 1 || hit.OwnerId != 1 || hit.Dot || hit.Reflected ||
            _state.Legendary?.VerdictReadyTick > Tick) return;
        // Called only after hard CC has actually been accepted and immediately before its cancellation.
        bool interrupted = target.Pending is not null || _state.Campaign?.Hazards.Any(h => h.SourceId == target.Id && h.ResolveTick > Tick) == true;
        if (!interrupted) return;
        (_state.Legendary ??= new()).VerdictReadyTick = Tick + 90;
        int barrier = Math.Min(40, 200 - Player.Barrier);
        Player.Barrier += barrier;
        Emit("BarrierGranted", 1, 1, barrier, LegendaryEquipment.CrownPower, hit.ActionId);
        Emit("LegendaryTriggered", 1, target.Id, barrier, LegendaryEquipment.CrownPower, hit.ActionId);
    }

    private void WitnessHit(Hit hit, CombatActor target)
    {
        if (!_state.ProgressionBuild.WitnessVow || Player.Health <= 0 || hit.SourceId != 1 || hit.OwnerId != 1 ||
            hit.Dot || hit.Reflected || hit.Depth != 0 || target.Faction != CombatFaction.Enemy ||
            !_content.Skills.Any(s => s.Id == hit.ContentId)) return;
        var state = _state.Legendary ??= new();
        if (state.WitnessActions?.Contains(hit.ActionId) == true) return;
        // Keep receipts while their projectiles/areas are alive, including after a target switch or proc.
        // A tick processes at most 256 effects on top of at most 192 persistent attack objects.
        if (state.WitnessActions is { Count: >= 512 }) return;
        (state.WitnessActions ??= []).Add(hit.ActionId);
        if (target.Health <= 0) { ClearWitness(state); return; }
        if (state.WitnessUntil <= Tick || state.WitnessTargetId != target.Id) ClearWitness(state);
        state.WitnessTargetId = target.Id;
        state.WitnessUntil = Tick + 120;
        state.WitnessStacks++;
        if (state.WitnessStacks < 4)
        {
            Emit("LegendaryCharged", 1, target.Id, state.WitnessStacks, LegendaryEquipment.WitnessPower, hit.ActionId);
            return;
        }
        ClearWitness(state);
        Enqueue(new(1, 1, target.Id, 24, DamageFamily.Void, "effect.witness_vow", hit.ActionId, 1, Reflected: true));
        Emit("LegendaryTriggered", 1, target.Id, 24, LegendaryEquipment.WitnessPower, hit.ActionId, 1);
    }

    private void AcceptBorrowedHour(CombatSkill skill, long action)
    {
        if (!_state.ProgressionBuild.BorrowedHour) return;
        if (IsUltimate(skill))
        {
            var prepared = _state.Legendary ??= new();
            prepared.HourCharges = 3; prepared.HourUntil = Tick + 240;
            Emit("LegendaryReadied", 1, amount: 3, content: LegendaryEquipment.HourPower, action: action);
            return;
        }
        if (skill.Cooldown <= 0 || _state.Legendary is not { HourCharges: > 0 } state || state.HourUntil <= Tick) return;
        long duration = Math.Max(1, (_state.Cooldowns[skill.Id] - Tick + 1) / 2);
        _state.Cooldowns[skill.Id] = Tick + duration;
        state.HourCharges--;
        if (state.HourCharges == 0) state.HourUntil = 0;
        Emit("LegendaryTriggered", 1, amount: state.HourCharges, content: LegendaryEquipment.HourPower, action: action);
    }

    private static void ClearWitness(LegendaryCombatState state)
    { state.WitnessStacks = 0; state.WitnessTargetId = 0; state.WitnessUntil = 0; }

    private void TrimLateLegendaryState(LegendaryCombatState state)
    {
        if (!_state.ProgressionBuild.UnspokenVerdict || Player.Health <= 0 || state.VerdictReadyTick <= Tick) state.VerdictReadyTick = 0;
        if (!_state.ProgressionBuild.WitnessVow || Player.Health <= 0)
        { ClearWitness(state); state.WitnessActions = null; }
        else
        {
            if (state.WitnessUntil <= Tick || !_state.Actors.Any(a => a.Id == state.WitnessTargetId && a.Health > 0)) ClearWitness(state);
            if (state.WitnessActions is not null)
            {
                _liveWitnessActions.Clear();
                foreach (var area in _state.Areas) _liveWitnessActions.Add(area.ActionId);
                foreach (var projectile in _state.Projectiles) _liveWitnessActions.Add(projectile.ActionId);
                foreach (var actor in _state.Actors)
                    if (actor.Pending is { } pending) _liveWitnessActions.Add(pending.ActionId);
                var receipts = state.WitnessActions;
                int retained = 0;
                for (int i = 0; i < receipts.Count; i++)
                    if (_liveWitnessActions.Contains(receipts[i])) receipts[retained++] = receipts[i];
                if (retained == 0) state.WitnessActions = null;
                else if (retained < receipts.Count) receipts.RemoveRange(retained, receipts.Count - retained);
            }
        }
        if (!_state.ProgressionBuild.BorrowedHour || Player.Health <= 0 || state.HourUntil <= Tick)
        { state.HourCharges = 0; state.HourUntil = 0; }
    }

    private void ValidateLateLegendaryState()
    {
        if (_state.Legendary is not { } state) return;
        bool Timer(long until, int duration, bool equipped) => until == 0 || equipped && Player.Health > 0 && until >= Tick && until <= Tick + duration;
        bool activeWitness = state.WitnessStacks > 0;
        if (!Timer(state.VerdictReadyTick, 90, _state.ProgressionBuild.UnspokenVerdict) ||
            !Timer(state.WitnessUntil, 120, _state.ProgressionBuild.WitnessVow) || state.WitnessStacks is < 0 or > 3 ||
            activeWitness != (state.WitnessUntil > 0) || activeWitness != (state.WitnessTargetId > 0) ||
            state.WitnessTargetId < 0 || activeWitness && !_state.Actors.Any(a => a.Id == state.WitnessTargetId && a.Health > 0 && a.Faction == CombatFaction.Enemy) ||
            state.WitnessActions is not null && (!_state.ProgressionBuild.WitnessVow || Player.Health <= 0 || state.WitnessActions.Count is 0 or > 512 ||
                state.WitnessActions.Distinct().Count() != state.WitnessActions.Count || state.WitnessActions.Any(id => id <= 0 || id >= _state.NextActionId)) ||
            !Timer(state.HourUntil, 240, _state.ProgressionBuild.BorrowedHour) || state.HourCharges is < 0 or > 3 ||
            (state.HourCharges > 0) != (state.HourUntil > 0))
            throw new InvalidDataException("Invalid late-game Legendary cooldown, target, receipt, or charges.");
    }
}
