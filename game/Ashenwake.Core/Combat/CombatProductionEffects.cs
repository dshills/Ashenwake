using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

public sealed partial class CombatSession
{
    private static bool LeavesCorpse(CombatActor actor) => actor.Faction == CombatFaction.Enemy && actor.Role is not ("Anchor" or "Bell");
    private CombatActor? AvailableCorpse(int targetId = 0) => _state.Actors.Where(a => LeavesCorpse(a) && a.Health == 0 && !_state.ConsumedCorpseIds.Contains(a.Id) && (targetId == 0 || a.Id == targetId) && Position.DistanceSquared(a.Position, Player.Position) <= 6000L * 6000).OrderBy(a => Position.DistanceSquared(a.Position, Player.Position)).ThenBy(a => a.Id).FirstOrDefault();
    private bool ClaimCorpse(CombatActor corpse, string reason, long action)
    {
        if (corpse.Health != 0 || !LeavesCorpse(corpse) || !_state.ConsumedCorpseIds.Add(corpse.Id)) return false;
        Emit("CorpseConsumed", 1, corpse.Id, content: reason, action: action); return true;
    }
    private bool HandleProductionCommand(CombatCommand command)
    {
        if (command.Kind == CombatCommandKind.ConsumeCorpse)
        {
            if (Discipline != "Gravecaller" && !HasManifestation("manifestation.voracious_renewal")) { Reject(command, "corpse_consumption_unavailable"); return true; }
            var corpse = AvailableCorpse(command.TargetId);
            if (corpse is null) { Reject(command, "corpse_unavailable"); return true; }
            long action = _state.NextActionId++;
            ClaimCorpse(corpse, "effect.corpse_consumption", action);
            if (Discipline == "Gravecaller") _state.Momentum = Math.Min(100, _state.Momentum + GenerationAmount(25));
            if (HasManifestation("manifestation.voracious_renewal"))
            {
                int bonus = Math.Min(60 - _state.TemporaryLife, 20); var player = Player;
                if (bonus > 0) { _state.Actors[_state.Actors.IndexOf(player)] = player with { MaxHealth = player.MaxHealth + bonus }; _state.TemporaryLife += bonus; }
                _state.TemporaryLifeUntil = Tick + 180; HealPlayer(50, "manifestation.voracious_renewal", action);
            }
            return true;
        }
        if (command.Kind == CombatCommandKind.CastEcho)
        {
            var target = _state.Actors.FirstOrDefault(a => a.Id == command.TargetId && a.Faction == CombatFaction.Enemy && a.Health > 0);
            if (_state.CapturedSkillId != "skill.echo_storm" || _state.CapturedUntil <= Tick || target is null || !ActorVisible(target) || Position.DistanceSquared(Player.Position, target.Position) > 10000L * 10000 || !_spatial.HasLineOfSight(Player.Position, target.Position) || Player.Pending is not null || Player.RecoveryUntil > Tick)
            { Reject(command, "captured_echo_unavailable"); return true; }
            long action = _state.NextActionId++; _state.CapturedSkillId = "";
            foreach (var enemy in Hostiles(Player, target.Position, 2500)) Enqueue(new(1, 1, enemy.Id, 42, DamageFamily.Storm, "skill.echo_storm", action, 1, Status: "Shocked"));
            Player.RecoveryUntil = Tick + 15; Emit("CapturedAbilityUsed", 1, target.Id, content: "skill.echo_storm", action: action); return true;
        }
        if (command.Kind == CombatCommandKind.ReleaseCharge)
        {
            if (Player.Pending is not { } pending || pending.SkillId != "skill.shield_breaker" || Mutation(pending.SkillId)?.Id != "mutation.orruns_patience") { Reject(command, "no_charged_ability"); return true; }
            int ticks = (int)Math.Clamp(Tick - pending.StartTick, 0, 60);
            Player.Pending = pending with { ResolveTick = Tick, ChargeTicks = ticks }; Player.RecoveryUntil = Tick + 9; Emit("ChargeReleased", 1, pending.TargetId, ticks, pending.SkillId, pending.ActionId); return true;
        }
        return false;
    }
    private string? ProductionCastRejection(CombatSkill skill)
    {
        if (!SkillAvailable(skill)) return "skill_locked_or_wrong_discipline";
        if (skill.Shape == "Summon" && skill.Behavior != "Companion" && AvailableCorpse() is null) return "corpse_required";
        if (skill.Shape == "Summon" && _state.Actors.Count(a => a.Faction == CombatFaction.Ally && a.Health > 0) >= MaxSummons) return "summon_limit";
        if (skill.Behavior == "Companion" && _state.Actors.Any(a => a.Role == "Companion" && a.Health > 0)) return "companion_already_active";
        if (Mutation(skill.Id)?.Id == "mutation.forking_flame" && _state.ProgressionBuild.ChainCount > 0) return "fork_and_chain_incompatible";
        return null;
    }
    private bool ResolveProductionAbility(CombatActor actor, CombatPending pending)
    {
        if (actor.Id != 1) return false;
        var skill = _content.Skills.FirstOrDefault(s => s.Id == pending.SkillId);
        if (skill is null) return false;
        if (skill.Shape == "Summon")
        {
            if (skill.Behavior == "Companion") SummonOwned("summon.companion", Player.Position, pending.ActionId, 1);
            else
            {
                int count = skill.Behavior == "Procession" ? 3 : 1;
                for (int i = 0; i < count && _state.Actors.Count(a => a.Faction == CombatFaction.Ally && a.Health > 0) < MaxSummons; i++)
                {
                    var corpse = AvailableCorpse(); if (corpse is null) break;
                    if (ClaimCorpse(corpse, skill.Id, pending.ActionId)) SummonOwned("summon.ancestor", corpse.Position, pending.ActionId, 1);
                }
            }
            Emit("AbilityResolved", 1, content: skill.Id, action: pending.ActionId); return true;
        }
        if (skill.Shape == "Command")
        {
            _state.MinionTargetId = pending.TargetId; Emit("MinionCommanded", 1, pending.TargetId, content: skill.Id, action: pending.ActionId);
            foreach (var ally in _state.Actors.Where(a => a.Faction == CombatFaction.Ally)) ally.RecoveryUntil = Math.Min(ally.RecoveryUntil, Tick + 3);
            return true;
        }
        return false;
    }
    private void AfterSkillResolved(CombatActor actor, CombatPending pending)
    {
        if (actor.Id != 1) return;
        var skill = _content.Skills.FirstOrDefault(s => s.Id == pending.SkillId);
        if (skill?.Behavior == "Vent") { _state.Momentum = Math.Max(0, _state.Momentum - 40); Player.Statuses.RemoveAll(s => s.Id == "Burning"); Emit("InstabilityVented", 1, amount: 40, content: skill.Id, action: pending.ActionId); }
        if (skill?.Behavior == "Vanish") { Player.InvulnerableUntil = Tick + 12; Emit("Vanished", 1, content: skill.Id, action: pending.ActionId); }
        if (skill?.Behavior == "Regenerate") HealPlayer(40, skill.Id, pending.ActionId);
    }
    private void SummonOwned(string definition, Position position, long action, int generation)
    {
        if (_state.Actors.Count >= MaxActors || _state.Actors.Count(a => a.Faction == CombatFaction.Ally && a.Health > 0) >= MaxSummons) { Budget(action); return; }
        var at = position;
        if (position == Player.Position) at = _spatial.Move(position, new(position.X + 650, position.Z), ActorRadius);
        var summon = new CombatActor
        {
            Id = _state.NextActorId++,
            DefinitionId = definition,
            Position = at,
            Faction = CombatFaction.Ally,
            Role = definition == "summon.companion" ? "Companion" : "Spirit",
            Health = 60,
            MaxHealth = 60,
            OwnerId = 1,
            Generation = generation,
            ExpiresTick = Tick + (definition == "summon.companion" ? 600 : 240)
        };
        _state.Actors.Add(summon); Emit("SummonSpawned", 1, summon.Id, content: definition, action: action, depth: generation);
    }
    private void OnProductionKill(CombatActor target, Hit hit)
    {
        if (target.Faction != CombatFaction.Enemy || hit.OwnerId != 1) return;
        if (Discipline == "Gravecaller") _state.Momentum = Math.Min(100, _state.Momentum + GenerationAmount(25));
        if (target.Elite && ActiveFragments().Any(f => f.Effect == "CaptureEcho"))
        {
            _state.CapturedSkillId = "skill.echo_storm"; _state.CapturedUntil = Tick + 450;
            Emit("EliteAbilityCaptured", 1, target.Id, content: _state.CapturedSkillId, action: hit.ActionId);
        }
        if (target.Statuses.Any(s => s.Id == "Staggered") && Mutation("skill.shield_breaker")?.Id == "mutation.executioners_pace")
        { _state.Cooldowns.Remove("skill.shield_breaker"); Emit("CooldownReset", 1, content: "skill.shield_breaker", action: hit.ActionId); }
        bool livingFlame = hit.ContentId == "skill.fire_lance" && Mutation("skill.fire_lance")?.Id == "mutation.living_flame";
        bool revenant = AshcleaverActive && _state.Build.AshcleaverEvolution == "Serath" && target.Statuses.Any(s => s.Id == "Burning");
        if (!livingFlame && !revenant) return;
        if (livingFlame && !revenant)
        {
            var rng = _state.Rng.Combat; bool succeeds = SeededRandom.Range(ref rng, 100) < 50; _state.Rng = _state.Rng with { Combat = rng };
            if (!succeeds) return;
        }
        if (hit.Depth >= MaxChainDepth || _state.Actors.Count(a => a.Faction == CombatFaction.Ally) >= MaxSummons) { Budget(hit.ActionId); return; }
        int generation = (_state.Actors.FirstOrDefault(a => a.Id == hit.SourceId)?.Generation ?? hit.SourceGeneration) + 1;
        if (generation > 2) { Budget(hit.ActionId); return; }
        if (ClaimCorpse(target, revenant ? "evolution.serath" : "mutation.living_flame", hit.ActionId)) SummonOwned(revenant ? "summon.flaming_revenant" : "summon.fire_spirit", target.Position, hit.ActionId, generation);
    }
    private void ProductionHitEffects(Hit hit, CombatActor? source, CombatActor target, int healthDamage, bool critical)
    {
        if (target.Id == 1 && healthDamage > 0 && Discipline == "Warden")
        {
            _state.Momentum = Math.Min(100, _state.Momentum + GenerationAmount(8));
            _state.ThreatStacks = _state.ThreatFamily == hit.Family && _state.ThreatUntil > Tick ? Math.Min(4, _state.ThreatStacks + 1) : 1;
            _state.ThreatFamily = hit.Family; _state.ThreatUntil = Tick + 150;
        }
        if (hit.Dot && hit.OwnerId == 1 && hit.OriginSkill == "skill.fire_lance" && Mutation("skill.fire_lance")?.Id == "mutation.cauterize" && healthDamage > 0) HealPlayer(Math.Max(1, healthDamage / 2), "mutation.cauterize", hit.ActionId);
        if (hit.OwnerId != 1 || source?.Id != 1 || hit.Dot || hit.Reflected) return;
        if (critical && ActiveFragments().Any(f => f.Effect == "Heat"))
        { _state.FragmentHeat = Math.Min(100, _state.FragmentHeat + 25); Emit("FragmentTriggered", 1, target.Id, _state.FragmentHeat, "fragment.heart_vael", hit.ActionId, hit.Depth + 1); }
        if (_state.OverheatedActionId == hit.ActionId && _triggers.Add($"{hit.ActionId}:overheated"))
        {
            _state.OverheatedActionId = 0;
            foreach (var nearby in Hostiles(source, target.Position, 2300)) Enqueue(new(1, 1, nearby.Id, 35, DamageFamily.Fire, "effect.overheated", hit.ActionId, hit.Depth + 1, Reflected: true, Status: "Burning"));
        }
        var skill = _content.Skills.FirstOrDefault(s => s.Id == hit.ContentId);
        if (skill?.Behavior == "Leech" && healthDamage > 0) HealPlayer(Math.Max(1, healthDamage / 3), skill.Id, hit.ActionId);
        if (skill?.Behavior == "ConsumeMarked") target.Statuses.RemoveAll(s => s.Id == "Marked");
        if (skill?.Behavior == "DetonatePoison")
        {
            int stacks = target.Statuses.Where(s => s.Id == "Poisoned").Sum(s => s.Stacks);
            target.Statuses.RemoveAll(s => s.Id == "Poisoned");
            if (stacks > 0) Enqueue(new(1, 1, target.Id, stacks * 15, DamageFamily.Venom, "effect.poison_detonation", hit.ActionId, hit.Depth + 1, Reflected: true));
        }
        if (hit.Family == DamageFamily.Storm && target.Statuses.Any(s => s.Id == "Shocked") && _triggers.Add($"{hit.ActionId}:shock:{target.Id}"))
        {
            var next = Hostiles(source, target.Position, 3000).FirstOrDefault(a => a.Id != target.Id);
            if (next is not null) Enqueue(new(1, 1, next.Id, Math.Max(1, hit.Damage / 3), DamageFamily.Storm, "effect.shock_chain", hit.ActionId, hit.Depth + 1, Reflected: true));
        }
    }
}
