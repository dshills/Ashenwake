using Ashenwake.Core.Progression;
using Ashenwake.Core.Simulation;
using System.Text.Json.Serialization;

namespace Ashenwake.Core.Combat;

public sealed record EquipmentSetCombatState
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long VigilUntil { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long VigilReadyTick { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long BriarReadyTick { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long AshrunnerDodgeUntil { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long AshrunnerUntil { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long AshrunnerReadyTick { get; set; }
}

public sealed record EquipmentSetCombatView(bool LastVigilActive, bool BriarboundActive, bool AshrunnerActive,
    int VigilReadyTicks, int VigilCooldownTicks, int BriarCooldownTicks, int AshrunnerReadyTicks, int AshrunnerCooldownTicks);

public sealed partial class CombatSession
{
    private const string VigilStrike = "effect.set_vigil_strike", BriarThorns = "effect.set_briar_thorns", AshrunnerTrail = "effect.set_ashrunner_trail";
    private HashSet<string>? _equipmentSetSkillIds;
    private static bool IsEquipmentSetEffect(string id) => id is VigilStrike or BriarThorns or AshrunnerTrail;
    private EquipmentSetCombatView? EquipmentSetView() => !_state.ProgressionBuild.LastVigilSet && !_state.ProgressionBuild.BriarboundSet && !_state.ProgressionBuild.AshrunnerSet ? null :
        new(_state.ProgressionBuild.LastVigilSet, _state.ProgressionBuild.BriarboundSet, _state.ProgressionBuild.AshrunnerSet,
            Remaining(_state.EquipmentSets?.VigilUntil ?? 0), Remaining(_state.EquipmentSets?.VigilReadyTick ?? 0),
            Remaining(_state.EquipmentSets?.BriarReadyTick ?? 0), Remaining(_state.EquipmentSets?.AshrunnerUntil ?? 0), Remaining(_state.EquipmentSets?.AshrunnerReadyTick ?? 0));

    private void BeginEquipmentSetDodge()
    {
        if (_state.ProgressionBuild.AshrunnerSet) (_state.EquipmentSets ??= new()).AshrunnerDodgeUntil = Tick + 7;
    }

    private void ObserveEquipmentSetDefense(Hit hit, CombatActor? source, CombatActor target, DamageInput input, DamageResult result)
    {
        if (target.Id != 1 || Player.Health <= 0 || source?.Faction != CombatFaction.Enemy || source.Health <= 0) return;
        if (_state.ProgressionBuild.LastVigilSet && result.Absorbed > 0 &&
            (_state.EquipmentSets?.VigilUntil ?? 0) <= Tick && (_state.EquipmentSets?.VigilReadyTick ?? 0) <= Tick)
        {
            (_state.EquipmentSets ??= new()).VigilUntil = Tick + 120;
            Emit("EquipmentSetReadied", 1, amount: 120, content: EquipmentSets.LastVigil);
        }
        if (!_state.ProgressionBuild.AshrunnerSet || hit.Dot || hit.Reflected || hit.Damage <= 0 ||
            Player.InvulnerableUntil <= Tick || _state.EquipmentSets is not { } state || state.AshrunnerDodgeUntil <= Tick ||
            state.AshrunnerUntil > Tick || state.AshrunnerReadyTick > Tick || DamageRules.Resolve(input with { Immune = false }).BeforeBarrier <= 0) return;
        state.AshrunnerUntil = Tick + 120;
        Emit("EquipmentSetReadied", 1, amount: 120, content: EquipmentSets.Ashrunner);
    }

    private void ReleaseEquipmentSetAttack(Hit hit, CombatActor target)
    {
        if (Player.Health <= 0 || target.Faction != CombatFaction.Enemy || hit.SourceId != 1 || hit.OwnerId != 1 ||
            hit.Dot || hit.Reflected || hit.Depth != 0 || _state.EquipmentSets is not { } state ||
            state.VigilUntil <= Tick && state.AshrunnerUntil <= Tick ||
            !(_equipmentSetSkillIds ??= _content.Skills.Select(s => s.Id).ToHashSet(StringComparer.Ordinal)).Contains(hit.ContentId)) return;
        if (_state.ProgressionBuild.LastVigilSet && state.VigilUntil > Tick)
        {
            state.VigilUntil = 0; state.VigilReadyTick = Tick + 90;
            if (target.Health > 0) Enqueue(new(1, 1, target.Id, 36, DamageFamily.Void, VigilStrike, hit.ActionId, 1, Reflected: true));
            Emit("EquipmentSetTriggered", 1, target.Id, 36, EquipmentSets.LastVigil, hit.ActionId, 1);
        }
        if (!_state.ProgressionBuild.AshrunnerSet || state.AshrunnerUntil <= Tick) return;
        state.AshrunnerUntil = 0; state.AshrunnerReadyTick = Tick + 90;
        var start = Player.Position;
        var end = Toward(start, target.Position, 2400);
        int patches = 0;
        for (int i = 0; i < 3; i++)
        {
            if (_state.Areas.Count >= MaxAreas) { Budget(hit.ActionId); break; }
            var position = new Position(start.X + (end.X - start.X) * i / 2, start.Z + (end.Z - start.Z) * i / 2);
            if (!_spatial.CanOccupy(position, 0) || !_spatial.HasLineOfSight(start, position)) continue;
            _state.Areas.Add(new(_state.NextObjectId++, 1, 1, position, 650, AshrunnerTrail, 6, DamageFamily.Fire, Tick, Tick + 60, hit.ActionId, 1));
            patches++;
        }
        Emit("EquipmentSetTriggered", 1, target.Id, patches, EquipmentSets.Ashrunner, hit.ActionId, 1);
    }

    private void GrowBriarbound(CombatActor corpse, Hit hit)
    {
        if (!_state.ProgressionBuild.BriarboundSet || Player.Health <= 0 || hit.OwnerId != 1 || !hit.Dot || hit.Reflected ||
            hit.ContentId != "Poisoned" || IsEquipmentSetEffect(hit.OriginSkill) || hit.Depth >= MaxChainDepth ||
            (_state.EquipmentSets?.BriarReadyTick ?? 0) > Tick || !LeavesCorpse(corpse)) return;
        (_state.EquipmentSets ??= new()).BriarReadyTick = Tick + 90;
        int healed = 0;
        foreach (var ally in _state.Actors.Where(a => a.Faction == CombatFaction.Ally && a.OwnerId == 1 && a.Health > 0 &&
            a.ExpiresTick > Tick && Position.DistanceSquared(a.Position, corpse.Position) <= 3500L * 3500 &&
            _spatial.HasLineOfSight(corpse.Position, a.Position)).OrderBy(a => a.Id).Take(8))
        {
            int amount = Math.Min(20, ally.MaxHealth - ally.Health); ally.Health += amount; healed += amount;
            if (amount > 0) Emit("Healed", 1, ally.Id, amount, EquipmentSets.Briarbound, hit.ActionId, hit.Depth + 1);
        }
        if (_state.Areas.Count < MaxAreas)
            _state.Areas.Add(new(_state.NextObjectId++, 1, 1, corpse.Position, 1100, BriarThorns, 8, DamageFamily.PhysicalPierce, Tick, Tick + 60, hit.ActionId, 1));
        else Budget(hit.ActionId);
        Emit("EquipmentSetTriggered", 1, corpse.Id, healed, EquipmentSets.Briarbound, hit.ActionId, hit.Depth + 1);
    }

    private void TrimEquipmentSetState()
    {
        if (_state.EquipmentSets is { } state)
        {
            if (!_state.ProgressionBuild.LastVigilSet || Player.Health <= 0) { state.VigilUntil = 0; state.VigilReadyTick = 0; }
            if (!_state.ProgressionBuild.BriarboundSet || Player.Health <= 0) state.BriarReadyTick = 0;
            if (!_state.ProgressionBuild.AshrunnerSet || Player.Health <= 0) { state.AshrunnerUntil = 0; state.AshrunnerReadyTick = 0; state.AshrunnerDodgeUntil = 0; }
            if (state.VigilUntil <= Tick) state.VigilUntil = 0;
            if (state.VigilReadyTick <= Tick) state.VigilReadyTick = 0;
            if (state.BriarReadyTick <= Tick) state.BriarReadyTick = 0;
            if (state.AshrunnerUntil <= Tick) state.AshrunnerUntil = 0;
            if (state.AshrunnerReadyTick <= Tick) state.AshrunnerReadyTick = 0;
            if (state.AshrunnerDodgeUntil <= Tick) state.AshrunnerDodgeUntil = 0;
            if (state.VigilUntil == 0 && state.VigilReadyTick == 0 && state.BriarReadyTick == 0 && state.AshrunnerUntil == 0 && state.AshrunnerReadyTick == 0 && state.AshrunnerDodgeUntil == 0)
                _state.EquipmentSets = null;
        }
        _state.Areas.RemoveAll(a => a.SkillId == BriarThorns && (!_state.ProgressionBuild.BriarboundSet || Player.Health <= 0) ||
            a.SkillId == AshrunnerTrail && (!_state.ProgressionBuild.AshrunnerSet || Player.Health <= 0));
    }

    private void ValidateEquipmentSetState()
    {
        bool Timer(long until, int duration, bool active) => until == 0 || active && Player.Health > 0 && until >= Tick && until <= Tick + duration;
        if (_state.EquipmentSets is { } state &&
            (!Timer(state.VigilUntil, 120, _state.ProgressionBuild.LastVigilSet) || !Timer(state.VigilReadyTick, 90, _state.ProgressionBuild.LastVigilSet) ||
             !Timer(state.BriarReadyTick, 90, _state.ProgressionBuild.BriarboundSet) || !Timer(state.AshrunnerUntil, 120, _state.ProgressionBuild.AshrunnerSet) ||
             !Timer(state.AshrunnerReadyTick, 90, _state.ProgressionBuild.AshrunnerSet) || !Timer(state.AshrunnerDodgeUntil, 7, _state.ProgressionBuild.AshrunnerSet) ||
             state.AshrunnerDodgeUntil > Player.InvulnerableUntil || state.VigilUntil > Tick && state.VigilReadyTick > Tick || state.AshrunnerUntil > Tick && state.AshrunnerReadyTick > Tick))
            throw new InvalidDataException("Invalid equipment set readiness or cooldown.");
        foreach (var area in _state.Areas.Where(a => IsEquipmentSetEffect(a.SkillId)))
        {
            bool thorn = area.SkillId == BriarThorns;
            if (area.SkillId is not (BriarThorns or AshrunnerTrail) || Player.Health <= 0 ||
                !(thorn ? _state.ProgressionBuild.BriarboundSet : _state.ProgressionBuild.AshrunnerSet) ||
                area.SourceId != 1 || area.OwnerId != 1 || area.Depth != 1 || area.Radius != (thorn ? 1100 : 650) ||
                area.Damage != (thorn ? 8 : 6) || area.Family != (thorn ? DamageFamily.PhysicalPierce : DamageFamily.Fire) ||
                area.ExpiresTick > Tick + 60 || area.NextTick > Tick + 20)
                throw new InvalidDataException("Invalid equipment set ground effect.");
        }
        if (_state.Areas.Count(a => a.SkillId == BriarThorns) > 1 || _state.Areas.Count(a => a.SkillId == AshrunnerTrail) > 3 ||
            _state.Projectiles.Any(p => IsEquipmentSetEffect(p.SkillId)))
            throw new InvalidDataException("Equipment set effects exceeded their fixed budget.");
    }
}
