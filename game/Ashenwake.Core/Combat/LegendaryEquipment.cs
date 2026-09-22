using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

/// <summary>Stable identifiers and bounded, non-recursive equipment triggers.</summary>
public static class LegendaryEquipment
{
    public const string Pyre = "item.pyrebound_treads", Oath = "item.oathkeeper_reprisal", Widow = "item.widows_last_echo";
    public const string PyrePower = "property.pyre_trail", OathPower = "property.oath_reprisal", WidowPower = "property.widow_echo";
    public const string Rotwake = "item.rotwake_signet", Mourning = "item.mourning_choir", Furnace = "item.furnaceheart_cinch";
    public const string RotwakePower = "property.virulent_wake", MourningPower = "property.rallying_chorus", FurnacePower = "property.cinder_cycle";
    public static bool IsItem(string id) => id is Pyre or Oath or Widow or Rotwake or Mourning or Furnace;
    public static bool IsPower(string id) => id is PyrePower or OathPower or WidowPower or RotwakePower or MourningPower or FurnacePower;
    public static bool IsEffect(string id) => id is "effect.pyre_trail" or "effect.oath_reprisal" or "effect.widow_echo" or "effect.virulent_wake" or "effect.rallying_chorus" or "effect.cinder_cycle";
    public static string EncounterReward(string encounter) => encounter switch
    {
        "campaign.road" => Pyre,
        "campaign.rootheart" => Widow,
        "campaign.covenant_warden" => Oath,
        "campaign.plague_village" => Rotwake,
        "campaign.extraction_floor" => Mourning,
        "campaign.furnace_spindle" => Furnace,
        _ => ""
    };
}

public sealed record LegendaryCombatState
{
    public int OathCharge { get; set; }
    public long OathUntil { get; set; }
    public long WidowUntil { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public long VirulentReadyTick { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public long ChorusReadyTick { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public long CinderUntil { get; set; }
}

public sealed partial class CombatSession
{
    private string LegendaryReward()
    {
        if (_state.Endgame is { } run && run.Manifest.Kind == "Fracture")
        {
            if (run.EncounterIndex < run.Manifest.Rooms.Length - 1 && run.Manifest.Region == "act.verdant_maw")
                return run.EncounterIndex switch { 0 => LegendaryEquipment.Rotwake, 1 => LegendaryEquipment.Mourning, _ => "" };
            if (run.EncounterIndex != run.Manifest.Rooms.Length - 1) return "";
            return run.Manifest.Region switch
            {
                "act.grey_march" => LegendaryEquipment.Pyre,
                "act.verdant_maw" => LegendaryEquipment.Widow,
                "act.shattered_spine" => LegendaryEquipment.Oath,
                "act.cinder_reach" => LegendaryEquipment.Furnace,
                _ => ""
            };
        }
        return LegendaryEquipment.EncounterReward(_state.EncounterId);
    }

    private void LegendaryDodge(Position start)
    {
        if (_state.ProgressionBuild.WidowEcho)
        {
            (_state.Legendary ??= new()).WidowUntil = Tick + 150;
            Emit("LegendaryReadied", 1, amount: 150, content: LegendaryEquipment.WidowPower);
        }
        if (!_state.ProgressionBuild.PyreTrail || start == Player.Position) return;
        long action = _state.NextActionId++;
        for (int i = 0; i < 3; i++)
        {
            if (_state.Areas.Count >= MaxAreas) { Budget(action); break; }
            var at = new Position(start.X + (Player.Position.X - start.X) * i / 2, start.Z + (Player.Position.Z - start.Z) * i / 2);
            if (!_spatial.CanOccupy(at, 0) || !_spatial.HasLineOfSight(start, at)) continue;
            _state.Areas.Add(new(_state.NextObjectId++, 1, 1, at, 650, "effect.pyre_trail", 6, DamageFamily.Fire, Tick, Tick + 60, action, 1));
        }
        Emit("LegendaryTriggered", 1, amount: 60, content: LegendaryEquipment.PyrePower, action: action);
    }

    private void ChargeOath(CombatActor? source, CombatActor target, int absorbed)
    {
        if (!_state.ProgressionBuild.OathReprisal || target.Id != 1 || absorbed <= 0 || source?.Faction != CombatFaction.Enemy) return;
        var state = _state.Legendary ??= new();
        state.OathCharge = Math.Min(60, state.OathCharge + absorbed); state.OathUntil = Tick + 240;
        Emit("LegendaryCharged", 1, amount: state.OathCharge, content: LegendaryEquipment.OathPower);
    }

    private void ReleaseOath(Hit hit)
    {
        if (!_state.ProgressionBuild.OathReprisal || _state.Legendary is not { OathCharge: > 0 } state || state.OathUntil <= Tick ||
            hit.SourceId != 1 || hit.OwnerId != 1 || hit.Dot || hit.Reflected || hit.Depth != 0) return;
        var skill = _content.Skills.FirstOrDefault(s => s.Id == hit.ContentId);
        if (skill is null || (Mutation(skill.Id)?.Shape ?? skill.Shape) is not ("Melee" or "Dash")) return;
        int damage = state.OathCharge; state.OathCharge = 0; state.OathUntil = 0;
        foreach (var enemy in Hostiles(Player, Player.Position, 2400))
            Enqueue(new(1, 1, enemy.Id, damage, DamageFamily.PhysicalCrush, "effect.oath_reprisal", hit.ActionId, 1, Reflected: true));
        Emit("LegendaryTriggered", 1, amount: damage, content: LegendaryEquipment.OathPower, action: hit.ActionId);
    }

    private void LaunchWidow(CombatPending pending, Position target, int damage)
    {
        if (!_state.ProgressionBuild.WidowEcho || _state.Legendary is not { WidowUntil: > 0 } state || state.WidowUntil <= Tick) return;
        state.WidowUntil = 0;
        if (_state.Projectiles.Count >= MaxProjectiles) { Budget(pending.ActionId); return; }
        _state.Projectiles.Add(new CombatProjectile(_state.NextObjectId++, 1, 1, Player.Position, target, pending.TargetId,
            "effect.widow_echo", Math.Max(1, damage / 2), DamageFamily.Void, Tick + 96, pending.ActionId, 1)
        { LaunchTick = Tick + 6 });
        Emit("LegendaryTriggered", 1, pending.TargetId, damage / 2, LegendaryEquipment.WidowPower, pending.ActionId, 1);
    }

    private void TrimLegendaryState()
    {
        if (_state.Legendary is not { } state) return;
        if (!_state.ProgressionBuild.OathReprisal || state.OathUntil <= Tick || Player.Health <= 0) { state.OathCharge = 0; state.OathUntil = 0; }
        if (!_state.ProgressionBuild.WidowEcho || state.WidowUntil <= Tick || Player.Health <= 0) state.WidowUntil = 0;
        TrimMidgameLegendaryState(state);
        if (state.OathCharge == 0 && state.WidowUntil == 0 && state.VirulentReadyTick == 0 && state.ChorusReadyTick == 0 && state.CinderUntil == 0) _state.Legendary = null;
    }

    private void ClearInactiveLegendaryEffects()
    {
        TrimLegendaryState();
        if (!_state.ProgressionBuild.PyreTrail || Player.Health <= 0) _state.Areas.RemoveAll(a => a.SkillId == "effect.pyre_trail");
        if (!_state.ProgressionBuild.WidowEcho || Player.Health <= 0) _state.Projectiles.RemoveAll(p => p.SkillId == "effect.widow_echo");
        if (!_state.ProgressionBuild.VirulentWake || Player.Health <= 0)
            foreach (var actor in _state.Actors) actor.Statuses.RemoveAll(s => s.OriginSkill == "effect.virulent_wake");
    }

    private void ValidateLegendaryState()
    {
        ValidateMidgameLegendaryState();
        if (_state.Legendary is { } state && (Player.Health <= 0 || state.OathCharge is < 0 or > 60 ||
            (state.OathCharge == 0) != (state.OathUntil == 0) || state.OathUntil != 0 && (state.OathUntil < Tick || state.OathUntil > Tick + 240) ||
            state.WidowUntil != 0 && (state.WidowUntil < Tick || state.WidowUntil > Tick + 150) || !_state.ProgressionBuild.OathReprisal && (state.OathCharge != 0 || state.OathUntil != 0) ||
            !_state.ProgressionBuild.WidowEcho && state.WidowUntil != 0)) throw new InvalidDataException("Invalid Legendary equipment state.");
        foreach (var area in _state.Areas.Where(a => LegendaryEquipment.IsEffect(a.SkillId)))
            if (area.SkillId != "effect.pyre_trail" || !_state.ProgressionBuild.PyreTrail || Player.Health <= 0 ||
                area.Depth != 1 || area.Radius != 650 || area.Damage != 6 || area.Family != DamageFamily.Fire || area.ExpiresTick > Tick + 60)
                throw new InvalidDataException("Invalid Legendary burning ground.");
        foreach (var projectile in _state.Projectiles.Where(p => LegendaryEquipment.IsEffect(p.SkillId)))
            if (projectile.SkillId != "effect.widow_echo" || !_state.ProgressionBuild.WidowEcho || Player.Health <= 0 ||
                projectile.Depth != 1 || projectile.Family != DamageFamily.Void || projectile.LaunchTick <= 0 ||
                projectile.ExpiresTick != projectile.LaunchTick + 90 || projectile.Pierce != 0 || projectile.Fork != 0 || projectile.Chain != 0 ||
                projectile.ImpactRadius != 0 || projectile.FragmentId != "" || projectile.HitIds is { Length: > 0 })
                throw new InvalidDataException("Invalid Legendary echo projectile.");
    }
}
