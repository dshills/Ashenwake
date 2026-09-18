using Ashenwake.Core.Combat;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Coop;

public sealed partial class CoopCombatSession
{
    private void Move(CoopActor actor, Position destination)
    {
        var moved = _spatial.Move(actor.Position, destination, ActorRadius);
        if (!_state.Actors.Any(a => a.Id != actor.Id && a.Health > 0 && Position.DistanceSquared(moved, a.Position) < 4L * ActorRadius * ActorRadius)) actor.Position = moved;
    }
    private static Position Toward(Position from, Position to, int amount)
    {
        long dx = (long)to.X - from.X, dz = (long)to.Z - from.Z;
        long length = Math.Max(1, (long)Math.Ceiling(Math.Sqrt(dx * dx + dz * dz)));
        return length <= amount ? to : new(from.X + (int)(dx * amount / length), from.Z + (int)(dz * amount / length));
    }
    private bool Shielded(CoopActor actor) => _state.EncounterIndex == 3 && actor.Role == "BellSaint" && _state.Actors.Any(a => a.Role == "Anchor" && a.Health > 0);
    private bool Stunned(CoopActor actor) => actor.Statuses.Any(s => s.Id == "Staggered" && s.ExpiresTick > Tick);
    private IEnumerable<CoopActor> Hostiles(CoopActor actor, Position center, int radius) => _state.Actors.Where(a => a.Health > 0 && (a.PlayerId > 0) != (actor.PlayerId > 0) && Position.DistanceSquared(a.Position, center) <= (long)radius * radius && _spatial.HasLineOfSight(center, a.Position)).OrderBy(a => a.Id);
    private void Resolve(CoopActor actor)
    {
        var pending = actor.Pending!; actor.Pending = null;
        if (actor.PlayerId > 0)
        {
            var skill = _content.Skills.Single(s => s.Id == pending.SkillId);
            if (skill.Shape == "Guard") { actor.Barrier = Math.Min(200, actor.Barrier + skill.Damage); Emit("BarrierGranted", actor.Id, actor.Id, skill.Damage, skill.Id, pending.ActionId); }
            else if (skill.Shape == "Projectile") Launch(actor, pending.Target, skill.Damage, skill.Family, skill.Radius, skill.Status, pending.ActionId);
            else
            {
                if (skill.Shape == "Dash")
                {
                    int distance = (int)Math.Sqrt(Position.DistanceSquared(actor.Position, pending.Target));
                    Move(actor, Toward(actor.Position, pending.Target, Math.Max(0, Math.Min(skill.Range, distance - ActorRadius * 2 - 40))));
                }
                int radius = skill.Shape is "Area" or "Dash" ? skill.Radius : skill.Range;
                foreach (var target in Hostiles(actor, actor.Position, radius).ToArray()) Hit(actor, target, skill.Damage, skill.Family, skill.Status, pending.ActionId, skill.Id);
            }
            // Generation is earned by resolving a valid action, never by a rejected or canceled intent.
            var player = Player(actor.Id); player.Momentum = Math.Min(100, player.Momentum + skill.Generate);
        }
        else
        {
            var definition = _content.Enemies.Single(e => e.Id == actor.DefinitionId);
            switch (pending.SkillId)
            {
                case "enemy.projectile": Launch(actor, pending.Target, definition.Damage, DamageFamily.Fire, 0, "Burning", pending.ActionId); break;
                case "enemy.mend":
                    var ally = _state.Actors.FirstOrDefault(a => a.Id == pending.TargetId && a.PlayerId == 0 && a.Health > 0);
                    if (ally is not null && Position.DistanceSquared(actor.Position, ally.Position) <= 9000L * 9000 && _spatial.HasLineOfSight(actor.Position, ally.Position))
                    {
                        int heal = Math.Min(30, ally.MaxHealth - ally.Health); ally.Health += heal; ally.Barrier = Math.Min(200, ally.Barrier + 12); Emit("Healed", actor.Id, ally.Id, heal, pending.SkillId, pending.ActionId);
                    }
                    break;
                case "boss.resurrect":
                    var corpse = _state.Actors.Where(a => a.PlayerId == 0 && a.Role == "Melee" && a.Health == 0 && !a.Resurrected).OrderBy(a => a.Id).FirstOrDefault();
                    if (corpse is not null)
                    {
                        corpse.Resurrected = true; corpse.DeathProcessed = false; corpse.Health = corpse.MaxHealth / 2; corpse.RecoveryUntil = Tick + 15; corpse.Statuses.Clear();
                        Emit("CorpseResurrected", actor.Id, corpse.Id, content: pending.SkillId, action: pending.ActionId);
                    }
                    break;
                case "boss.beast_rush":
                    int distance = (int)Math.Sqrt(Position.DistanceSquared(actor.Position, pending.Target));
                    Move(actor, Toward(actor.Position, pending.Target, Math.Max(0, distance - 650))); break;
            }
        }
        Emit("AbilityResolved", actor.Id, pending.TargetId, content: pending.SkillId, action: pending.ActionId);
    }
    private void Think(CoopActor actor)
    {
        if (actor.Role == "Anchor" || Stunned(actor) || actor.Pending is not null || actor.RecoveryUntil > Tick) return;
        var target = _state.Actors.Where(a => a.PlayerId > 0 && a.Health > 0 && Player(a.PlayerId).Connected).OrderBy(a => Position.DistanceSquared(a.Position, actor.Position)).ThenBy(a => a.Id).FirstOrDefault();
        if (target is null) return;
        var definition = _content.Enemies.Single(e => e.Id == actor.DefinitionId);
        long distance = Position.DistanceSquared(actor.Position, target.Position);
        if ((distance > (long)definition.Range * definition.Range || !_spatial.HasLineOfSight(actor.Position, target.Position)) && actor.Role is not ("Bell" or "Support"))
        {
            // Same visibility-aware policy used by diagnostics, with server-owned authored enemy speed.
            var direction = CombatProductionSmoke.MovementDirection(actor.Position, target.Position, _content.Room);
            int speed = direction.X != 0 && direction.Z != 0 ? definition.Speed * 707 / 1000 : definition.Speed;
            Move(actor, new(actor.Position.X + direction.X * speed, actor.Position.Z + direction.Z * speed)); return;
        }
        string skill = actor.Role switch
        {
            "Ranged" => "enemy.projectile",
            "Support" => "enemy.mend",
            "Rusher" => "enemy.detonate",
            "Beast" => "boss.beast_rush",
            "Bell" => "boss.bell_ring",
            "BellSaint" when _state.EncounterIndex == 3 && _state.Actors.Any(a => a.PlayerId == 0 && a.Role == "Melee" && a.Health == 0 && !a.Resurrected) => "boss.resurrect",
            "BellSaint" => actor.Cycle % 2 == 0 ? "boss.chain" : "boss.sonic",
            _ => "enemy.strike"
        };
        if (skill == "enemy.mend")
        {
            var ally = _state.Actors.Where(a => a.PlayerId == 0 && a.Health > 0 && a.Health < a.MaxHealth && Position.DistanceSquared(a.Position, actor.Position) <= 9000L * 9000 && _spatial.HasLineOfSight(actor.Position, a.Position)).OrderBy(a => (long)a.Health * 100 / a.MaxHealth).ThenBy(a => a.Id).FirstOrDefault();
            if (ally is null) skill = "enemy.projectile"; else target = ally;
        }
        if (skill == "enemy.projectile" && !_spatial.HasLineOfSight(actor.Position, target.Position)) return;
        int windup = skill == "boss.resurrect" ? 60 : definition.Windup;
        long action = _state.NextActionId++; actor.Cycle++;
        var center = skill == "enemy.detonate" ? actor.Position : target.Position;
        actor.Pending = new(skill, target.Id, center, Tick + windup, action); actor.RecoveryUntil = Tick + windup + definition.Recovery;
        switch (skill)
        {
            case "enemy.strike": Warn(actor, skill, "Circle", center, center, definition.Range, definition.Damage, DamageFamily.PhysicalSlash, windup, action); break;
            case "enemy.detonate": Warn(actor, skill, "Circle", center, center, 2200, definition.Damage, DamageFamily.Fire, windup, action); break;
            case "boss.chain":
                // Three locked lashes. Moving off the visible line avoids the entire chain.
                for (int i = 0; i < 3; i++) Warn(actor, skill, "Line", actor.Position, center, 650, definition.Damage, DamageFamily.PhysicalPierce, windup + i * 8, action);
                actor.RecoveryUntil += 16; break;
            case "boss.sonic":
                foreach (var player in _state.Actors.Where(a => a.PlayerId > 0 && a.Health > 0)) Warn(actor, skill, "Circle", player.Position, player.Position, 2100, definition.Damage, DamageFamily.Storm, windup, action);
                break;
            case "boss.beast_rush": Warn(actor, skill, "Circle", center, center, 2200, definition.Damage, DamageFamily.PhysicalCrush, windup, action); break;
            case "boss.bell_ring": Warn(actor, skill, "Circle", center, center, 1900, definition.Damage, DamageFamily.Storm, windup, action); break;
        }
        Emit("AbilityStarted", actor.Id, target.Id, content: skill, action: action);
    }
    private void Warn(CoopActor actor, string skill, string shape, Position center, Position end, int radius, int damage, DamageFamily family, int delay, long action)
    {
        if (_state.Warnings.Count >= MaxWarnings) { Emit("BudgetLimited", actor.Id, content: "warnings"); return; }
        _state.Warnings.Add(new(_state.NextObjectId++, actor.Id, skill, shape, center, end, radius, damage, family, Tick + delay, action));
    }
    private void Launch(CoopActor actor, Position target, int damage, DamageFamily family, int radius, string status, long action)
    {
        if (_state.Projectiles.Count >= MaxProjectiles) { Emit("BudgetLimited", actor.Id, content: "projectiles"); return; }
        _state.Projectiles.Add(new(_state.NextObjectId++, actor.Id, actor.Position, target, damage, family, radius, status, Tick + 90, action));
    }
    private void UpdateProjectiles()
    {
        foreach (var projectile in _state.Projectiles.ToArray())
        {
            var source = _state.Actors.Single(a => a.Id == projectile.SourceId);
            Position next = Toward(projectile.Position, projectile.Target, 650);
            if (source.Health <= 0 || projectile.ExpiresTick <= Tick || !_spatial.HasLineOfSight(projectile.Position, next)) { _state.Projectiles.Remove(projectile); continue; }
            var target = Hostiles(source, next, 700).FirstOrDefault();
            if (target is not null)
            {
                _state.Projectiles.Remove(projectile);
                var hits = projectile.Radius > 0 ? Hostiles(source, target.Position, projectile.Radius).ToArray() : [target];
                foreach (var victim in hits) Hit(source, victim, projectile.Damage, projectile.Family, projectile.Status, projectile.ActionId, "projectile");
            }
            else if (next == projectile.Target) _state.Projectiles.Remove(projectile);
            else _state.Projectiles[_state.Projectiles.IndexOf(projectile)] = projectile with { Position = next };
        }
    }
    public static bool WarningContains(CoopWarning warning, Position position, int padding = 0)
    {
        long radius = warning.Radius + padding;
        if (warning.Shape == "Circle") return Position.DistanceSquared(position, warning.Position) <= radius * radius;
        long dx = (long)warning.End.X - warning.Position.X, dz = (long)warning.End.Z - warning.Position.Z;
        long px = (long)position.X - warning.Position.X, pz = (long)position.Z - warning.Position.Z;
        long length = dx * dx + dz * dz;
        if (length == 0) return Position.DistanceSquared(position, warning.Position) <= radius * radius;
        decimal t = Math.Clamp((decimal)(px * dx + pz * dz) / length, 0, 1);
        decimal x = px - t * dx, z = pz - t * dz;
        return x * x + z * z <= radius * radius;
    }
    private void UpdateWarnings()
    {
        foreach (var warning in _state.Warnings.Where(w => w.ResolveTick <= Tick).OrderBy(w => w.Id).ToArray())
        {
            _state.Warnings.Remove(warning); var source = _state.Actors.Single(a => a.Id == warning.SourceId);
            if (source.Health <= 0) continue;
            foreach (var target in _state.Actors.Where(a => a.PlayerId > 0 && a.Health > 0 && WarningContains(warning, a.Position) && _spatial.HasLineOfSight(warning.Position, a.Position)).ToArray())
                Hit(source, target, warning.Damage, warning.Family, warning.Family == DamageFamily.Fire ? "Burning" : "", warning.ActionId, warning.SkillId);
            Emit("WarningResolved", source.Id, content: warning.SkillId, action: warning.ActionId);
            if (warning.SkillId == "enemy.detonate") { source.Health = 0; Die(source, source, warning.ActionId); }
        }
    }
    private void Hit(CoopActor source, CoopActor target, int damage, DamageFamily family, string status, long action, string content, bool dot = false)
    {
        if (target.Health <= 0 || (source.PlayerId > 0) == (target.PlayerId > 0)) return;
        bool critical = false; int bonus = 0;
        if (source.PlayerId > 0 && !dot)
        {
            var items = Loadouts[source.PlayerId - 1].Select(id => _content.Items.Single(i => i.Id == id)).ToArray(); bonus = items.Sum(i => i.Damage);
            ulong random = _state.CombatRng; critical = SeededRandom.Range(ref random, 10000) < Math.Min(7500, items.Sum(i => i.CriticalBasisPoints)); _state.CombatRng = random;
        }
        bool physical = family is DamageFamily.PhysicalSlash or DamageFamily.PhysicalPierce or DamageFamily.PhysicalCrush;
        var result = DamageRules.Resolve(new(damage, FlatBonus: bonus, Critical: critical, Family: family, DefenseBasisPoints: physical ? target.Armor : 0,
            VulnerabilityBasisPoints: target.Statuses.Any(s => s.Id == "Vulnerable" && s.ExpiresTick > Tick) ? 1800 : 0, Barrier: target.Barrier, Immune: target.InvulnerableUntil > Tick || Shielded(target), DamageOverTime: dot));
        target.Barrier -= result.Absorbed; target.Health = Math.Max(0, target.Health - result.HealthDamage);
        Emit(result.HealthDamage == 0 && result.Absorbed == 0 ? "DamagePrevented" : critical ? "CriticalDamage" : "Damage", source.Id, target.Id, result.HealthDamage, content, action);
        if (target.PlayerId > 0 && result.HealthDamage > 0) Player(target.PlayerId).Momentum = Math.Min(100, Player(target.PlayerId).Momentum + 3);
        if (target.Health == 0) { Die(target, source, action); return; }
        if (result.HealthDamage + result.Absorbed == 0 || status == "") return;
        int duration = status == "Staggered" ? target.Role is "BellSaint" or "Beast" ? 4 : 14 : 150;
        if (status == "Staggered")
        {
            if (target.StaggerReadyTick > Tick) return;
            target.StaggerReadyTick = Tick + (target.Role is "BellSaint" or "Beast" ? 90 : 40);
            if (target.Pending is { } pending) _state.Warnings.RemoveAll(w => w.ActionId == pending.ActionId && w.SourceId == target.Id);
            target.Pending = null; target.RecoveryUntil = Math.Max(target.RecoveryUntil, Tick + duration);
        }
        target.Statuses.RemoveAll(s => s.Id == status);
        target.Statuses.Add(new(status, source.Id, Tick + duration, Tick + 30, action)); Emit("StatusApplied", source.Id, target.Id, content: status, action: action);
    }
    private void Die(CoopActor target, CoopActor source, long action)
    {
        if (target.DeathProcessed) return;
        target.DeathProcessed = true; target.Pending = null; target.Statuses.Clear();
        _state.Warnings.RemoveAll(w => w.SourceId == target.Id); _state.Projectiles.RemoveAll(p => p.SourceId == target.Id);
        if (target.PlayerId > 0) { var player = Player(target.PlayerId); player.MoveX = player.MoveZ = 0; player.Ready = false; }
        Emit("ActorDied", source.Id, target.Id, content: target.DefinitionId, action: action);
    }
    private void UpdateStatuses()
    {
        foreach (var actor in _state.Actors.Where(a => a.Health > 0).OrderBy(a => a.Id).ToArray())
        {
            foreach (var status in actor.Statuses.ToArray())
            {
                if (status.ExpiresTick <= Tick) { actor.Statuses.Remove(status); continue; }
                if (status.Id != "Burning" || status.NextTick > Tick) continue;
                var source = _state.Actors.Single(a => a.Id == status.SourceId);
                actor.Statuses[actor.Statuses.IndexOf(status)] = status with { NextTick = Tick + 30 };
                Hit(source, actor, 5, DamageFamily.Fire, "", status.ActionId, "Burning", true);
                if (actor.Health <= 0) break;
            }
        }
    }
}
