using Ashenwake.Core.Training;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

public sealed partial class CombatSession
{
    // Runtime-only training instrumentation: never serialized and absent from ordinary combat.
    private bool training;
    private bool trainingSparring;
    internal Action<TrainingHit>? TrainingDamage { get; set; }
    internal Action<TrainingDefenseRow>? TrainingDefense { get; set; }
    internal Action<string, int>? TrainingResource { get; set; }
    internal static CombatSession CreateTraining(string json, CombatSnapshot source, TrainingTargetMode mode)
    {
        var clean = CreateEncounter(json, source.Seed, "hub", source, restoreAtAnchor: true);
        var state = clean.Capture(); state.NextActorId = 2; state.Momentum = 0;
        state.ResourceActions.Clear(); state.SeismicCharge = 0; state.FragmentHeat = 0;
        state.ThreatFamily = null; state.ThreatStacks = 0; state.ThreatUntil = state.Tick;
        state.CapturedSkillId = ""; state.CapturedUntil = state.Tick;
        var result = new CombatSession(clean._content, state) { training = true, trainingSparring = mode is TrainingTargetMode.Melee or TrainingTargetMode.Ranged or TrainingTargetMode.Mixed };
        result.Player.Position = new(-1500, 0);
        if (result.trainingSparring)
        {
            if (mode is TrainingTargetMode.Melee or TrainingTargetMode.Mixed)
                result.AddEncounterActor("enemy.ash_ghoul", new Position(800, -400));
            if (mode is TrainingTargetMode.Ranged or TrainingTargetMode.Mixed)
                result.AddEncounterActor("enemy.cinder_acolyte", new Position(3400, 1400));
        }
        else
        {
            var positions = mode == TrainingTargetMode.Single ? new[] { new Position(800, 0) } :
                [new Position(800, 0), new Position(800, -1000), new Position(800, 1000), new Position(2200, -600), new Position(2200, 600)];
            foreach (var position in positions)
            {
                var target = result.AddEncounterActor("enemy.ash_ghoul", position);
                state.Actors[state.Actors.IndexOf(target)] = target with { Health = mode == TrainingTargetMode.Single ? 10000 : 1500, MaxHealth = mode == TrainingTargetMode.Single ? 10000 : 1500, Armor = 0, State = "Training" };
            }
        }
        // Practice corpses enable the ordinary Gravecaller rules without injecting resources or summons.
        foreach (var position in new[] { new Position(-2400, -1400), new Position(-2400, 1400), new Position(-3300, 0) })
            result.AddEncounterActor("enemy.ash_ghoul", position, corpse: true);
        result.ValidateSnapshot(); return result;
    }
    private bool TrainingCommandRejected(CombatCommand command)
    {
        if (!training || command.ActorId == 1 && command.Kind is CombatCommandKind.Move or CombatCommandKind.Stop or CombatCommandKind.Cast or CombatCommandKind.Dodge or CombatCommandKind.Potion or CombatCommandKind.ConsumeCorpse or CombatCommandKind.CastEcho or CombatCommandKind.ReleaseCharge) return false;
        Reject(command, "training_build_is_read_only"); return true;
    }
    private void SetResource(int value, string reason)
    {
        int previous = _state.Momentum; _state.Momentum = value;
        if (value != previous) TrainingResource?.Invoke(reason, value - previous);
    }
    private void ObserveTrainingHit(Hit hit, CombatActor? source, CombatActor target, int amount)
    {
        if (TrainingDamage is null || hit.OwnerId != 1 || target.Faction != CombatFaction.Enemy || amount <= 0) return;
        string id = hit.FragmentId.Length > 0 ? hit.FragmentId : hit.OriginSkill.Length > 0 ? hit.OriginSkill : hit.ContentId;
        if (source?.FragmentId.Length > 0) id = source.FragmentId;
        if (id == "effect.seismic_release") id = "fragment.orrun_knuckle";
        string category = id.StartsWith("fragment.", StringComparison.Ordinal) ? "Fragment" : LegendaryEquipment.IsEffect(id) ? "Legendary" :
            source?.Faction == CombatFaction.Ally ? "Summon" : id.StartsWith("skill.", StringComparison.Ordinal) ? "Ability" : "Effect";
        TrainingDamage(new(category, id, TrainingContentName(id), hit.Family, amount));
    }
    private void ObserveTrainingDefense(Hit hit, CombatActor? source, CombatActor target, DamageInput input,
        DamageResult result, int healthDamage)
    {
        if (TrainingDefense is null || target.Id != 1) return;
        // Reuse the authoritative integer pipeline. This observer neither draws RNG nor mutates combat.
        var unprotected = DamageRules.Resolve(input with
        {
            DefenseBasisPoints = 0,
            PenetrationBasisPoints = 0,
            MinimumDefenseBasisPoints = 0,
            Barrier = 0,
            Immune = false
        });
        var defended = input.Immune ? DamageRules.Resolve(input with { Barrier = 0, Immune = false }) : result;
        string sourceId = source?.DefinitionId ?? "unknown";
        string attackId = hit.OriginSkill.Length > 0 ? hit.OriginSkill : hit.ContentId;
        TrainingDefense(new(sourceId, TrainingContentName(sourceId), attackId, TrainingContentName(attackId),
            result.Family, unprotected.BeforeBarrier, unprotected.BeforeBarrier - defended.BeforeBarrier,
            defended.BeforeBarrier - result.BeforeBarrier, result.Absorbed, healthDamage,
            result.HealthDamage - healthDamage, 1));
    }
    internal bool IsTrainingDefensiveTrigger(CombatEvent ev) => ev.ActorId == 1 &&
        (ev.Kind == "BarrierGranted" && ev.TargetId == 1 ||
         ev.Kind == "FragmentTriggered" && _content.Fragments.Any(f => f.Id == ev.ContentId && f.Effect == "Barrier") ||
         ev.Kind is "LegendaryCharged" or "LegendaryTriggered" && ev.ContentId == LegendaryEquipment.OathPower ||
         ev.Kind == "ManifestationTriggered" && ev.ContentId == "manifestation.stone_memory");
    internal string TrainingContentName(string id) => _content.Skills.FirstOrDefault(s => s.Id == id)?.Name ??
        _content.Fragments.FirstOrDefault(f => f.Id == id)?.Name ?? id switch
        {
            "enemy.ash_ghoul" => "Ash Ghoul",
            "enemy.cinder_acolyte" => "Cinder Acolyte",
            "enemy.strike" => "Melee strike",
            "enemy.projectile" => "Cinder projectile",
            "rune.guard" => "Guard rune",
            "manifestation.stone_memory" => "Stone Memory",
            "effect.pyre_trail" or "property.pyre_trail" => "Pyrebound Treads",
            "effect.oath_reprisal" or "property.oath_reprisal" => "Oathkeeper's Reprisal",
            "effect.widow_echo" or "property.widow_echo" => "Widow's Last Echo",
            "effect.virulent_wake" or "property.virulent_wake" => "Rotwake Signet",
            "effect.rallying_chorus" or "property.rallying_chorus" => "Mourning Choir",
            "effect.cinder_cycle" or "property.cinder_cycle" => "Furnaceheart Cinch",
            "property.unspoken_verdict" => "Crown of the Unsworn",
            "effect.witness_vow" or "property.witness_vow" => "Vow of the Last Witness",
            "property.borrowed_hour" => "Greaves of the Stolen Hour",
            "property.griefs_reprieve" => "Grief’s Reprieve",
            "effect.widowthorn" or "property.widowthorn" => "Widowthorn",
            "property.emberwake" => "Emberwake Mantle",
            "summon.companion_bite" => "Companion bite",
            "summon.spirit_bolt" => "Summon spirit bolt",
            _ => id
        };
}
