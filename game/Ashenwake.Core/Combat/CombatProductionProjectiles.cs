using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

public sealed partial class CombatSession
{
    private void LaunchProductionProjectile(CombatActor actor, CombatPending pending, CombatSkill skill, CombatMutation? mutation, int damage, DamageFamily family)
    {
        if (_state.Projectiles.Count >= MaxProjectiles) { Budget(pending.ActionId); return; }
        bool lance = skill.Id == "skill.fire_lance";
        int pierce = lance && mutation?.Id != "mutation.fire_furnace" ? 2 : 0;
        int fork = mutation?.Id == "mutation.forking_flame" ? 2 : _state.ProgressionBuild.ForkCount;
        int chain = fork > 0 ? 0 : _state.ProgressionBuild.ChainCount;
        Position target = pending.Target;
        if (pierce > 0)
        {
            long dx = (long)target.X - actor.Position.X, dz = (long)target.Z - actor.Position.Z;
            long length = Math.Max(1, (long)Math.Ceiling(Math.Sqrt(dx * dx + dz * dz)));
            target = new(Math.Clamp(actor.Position.X + (int)(dx * skill.Range / length), -_content.Room.HalfWidth + 1, _content.Room.HalfWidth - 1), Math.Clamp(actor.Position.Z + (int)(dz * skill.Range / length), -_content.Room.HalfDepth + 1, _content.Room.HalfDepth - 1));
        }
        for (int attempt = 0; attempt < 256 && !_spatial.CanOccupy(target, 0); attempt++) target = Toward(target, actor.Position, 100);
        _state.Projectiles.Add(new(_state.NextObjectId++, 1, 1, actor.Position, target, pending.TargetId, skill.Id, damage, family, Tick + 90, pending.ActionId, pending.Depth,
            Pierce: pierce, Fork: fork, Chain: chain, HitIds: [], ImpactRadius: mutation?.Radius > 0 ? mutation.Radius : skill.Radius));
    }
    private void UpdateProjectiles()
    {
        foreach (var projectile in _state.Projectiles.ToArray())
        {
            var source = _state.Actors.FirstOrDefault(a => a.Id == projectile.SourceId);
            var next = Toward(projectile.Position, projectile.Target, 650);
            if (source is null || projectile.ExpiresTick <= Tick || !_spatial.HasLineOfSight(projectile.Position, next)) { _state.Projectiles.Remove(projectile); continue; }
            var hitIds = projectile.HitIds ?? [];
            var target = Hostiles(source, next, 700).FirstOrDefault(a => !hitIds.Contains(a.Id));
            if (target is not null)
            {
                _state.Projectiles.Remove(projectile);
                var skill = _content.Skills.FirstOrDefault(s => s.Id == projectile.SkillId);
                int radius = projectile.ImpactRadius > 0 ? projectile.ImpactRadius : skill?.Radius ?? (projectile.SkillId == "effect.ashcleaver_wave" ? 1600 : 0);
                var targets = radius > 0 ? Hostiles(source, target.Position, radius).Where(a => !hitIds.Contains(a.Id)).ToArray() : [target];
                string status = skill?.Status ?? (projectile.SkillId == "effect.ashcleaver_wave" || source.DefinitionId is "summon.fire_spirit" or "summon.flaming_revenant" ? "Burning" : "");
                foreach (var victim in targets) Enqueue(new(projectile.SourceId, projectile.OwnerId, victim.Id, projectile.Damage, projectile.Family, projectile.SkillId, projectile.ActionId, projectile.Depth, Status: status));
                var visited = hitIds.Concat(targets.Select(a => a.Id)).Distinct().ToArray();
                var continuations = Hostiles(source, target.Position, 5000).Where(a => !visited.Contains(a.Id)).OrderBy(a => Position.DistanceSquared(a.Position, target.Position)).ThenBy(a => a.Id).ToArray();
                if (projectile.Fork > 0 && projectile.Depth < MaxChainDepth)
                {
                    foreach (var forkTarget in continuations.Take(projectile.Fork))
                    {
                        if (_state.Projectiles.Count >= MaxProjectiles) { Budget(projectile.ActionId); break; }
                        _state.Projectiles.Add(projectile with { Id = _state.NextObjectId++, Position = next, Target = forkTarget.Position, TargetId = forkTarget.Id, Pierce = 0, Fork = 0, Chain = 0, HitIds = visited, Damage = projectile.Damage * 3 / 4, Depth = projectile.Depth + 1 });
                        Emit("ProjectileForked", projectile.SourceId, forkTarget.Id, content: projectile.SkillId, action: projectile.ActionId, depth: projectile.Depth + 1);
                    }
                }
                else if (projectile.Chain > 0 && continuations.Length > 0 && projectile.Depth < MaxChainDepth)
                {
                    var chained = continuations[0];
                    _state.Projectiles.Add(projectile with { Position = next, Target = chained.Position, TargetId = chained.Id, Pierce = 0, Chain = projectile.Chain - 1, HitIds = visited, Depth = projectile.Depth + 1 });
                    Emit("ProjectileChained", projectile.SourceId, chained.Id, content: projectile.SkillId, action: projectile.ActionId, depth: projectile.Depth + 1);
                }
                else if (projectile.Pierce > 0 && next != projectile.Target) _state.Projectiles.Add(projectile with { Position = next, Pierce = projectile.Pierce - 1, Fork = 0, HitIds = visited });
            }
            else if (next == projectile.Target) _state.Projectiles.Remove(projectile);
            else _state.Projectiles[_state.Projectiles.IndexOf(projectile)] = projectile with { Position = next };
        }
    }
    private bool CanEquip(CombatItem item, string slot)
    {
        var definition = _content.Items.FirstOrDefault(i => i.Id == item.DefinitionId);
        if (definition is null || !(definition.CompatibleSlots ?? [definition.Slot]).Contains(slot) || definition.Disciplines is { Length: > 0 } && !definition.Disciplines.Contains(Discipline)) return false;
        if (definition.Hands == 2 && _state.Equipment.ContainsKey("OffHand")) return false;
        if (slot == "OffHand" && _state.Equipment.TryGetValue("MainHand", out var id) && _content.Items.Single(i => i.Id == _state.Inventory.Single(item => item.Id == id).DefinitionId).Hands == 2) return false;
        return true;
    }
}

/// <summary>A deterministic baseline policy for headless comparisons; it is not a combat-balance acceptance oracle.</summary>
public static class CombatProductionSmoke
{
    public static CombatCommand[] Commands(CombatView view, Ashenwake.Core.Content.RoomDefinition? room = null)
    {
        var player = view.Actors.First(a => a.Id == 1);
        if (player.Health <= 0) return [];
        var target = view.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0).OrderBy(a => a.Role == "Anchor" ? 0 : 1).ThenBy(a => Position.DistanceSquared(player.Position, a.Position)).ThenBy(a => a.Id).FirstOrDefault();
        if (target is null) return [new(CombatCommandKind.Stop)];
        var commands = new List<CombatCommand>();
        if (player.Health < player.MaxHealth / 2 && view.PotionCharges > 0 && view.PotionCooldownTicks == 0) commands.Add(new(CombatCommandKind.Potion));
        bool ranged = view.Discipline is "Arcanist" or "Gravecaller" or "Warden";
        long distance = Position.DistanceSquared(player.Position, target.Position);
        bool obstructed = room is not null && !new SpatialWorld(room).HasLineOfSight(player.Position, target.Position);
        if (distance > (ranged ? 5000L * 5000 : 1900L * 1900) || obstructed)
        {
            var destination = room is null ? target.Position : Waypoint(room, player.Position, target.Position);
            commands.Add(new(CombatCommandKind.Move, X: Math.Sign(destination.X - player.Position.X), Z: Math.Sign(destination.Z - player.Position.Z)));
        }
        else commands.Add(new(CombatCommandKind.Stop));
        var threats = view.Actors.Where(a => a.TelegraphTicks is > 0 and <= 10 && a.TelegraphPosition is not null && a.TelegraphRadius > 0 && Position.DistanceSquared(player.Position, a.TelegraphPosition.Value) <= (long)(a.TelegraphRadius + 400) * (a.TelegraphRadius + 400)).ToArray();
        if (threats.Length > 0 && view.DodgeCooldownTicks == 0) commands.Add(new(CombatCommandKind.Dodge, X: 0, Z: player.Position.Z >= 0 ? -1 : 1));
        if (view.Tick % 3 != 0) return commands.ToArray();
        var skills = view.Skills.Where(s => s.Available && s.RemainingTicks == 0 && (s.ResourceMode == "Heat" ? view.Resource + s.Cost <= 100 : s.Cost <= view.Resource)).ToArray();
        CombatSkillView? skill;
        if (view.Discipline == "Arcanist" && view.Resource >= 65) skill = skills.FirstOrDefault(s => s.Id == "skill.starfall") ?? skills.FirstOrDefault(s => s.Id == "skill.vent");
        else skill = skills.FirstOrDefault(s => s.Cost >= 10 && s.Shape is not ("Summon" or "Command")) ?? skills.FirstOrDefault(s => s.Shape is "Melee" or "Projectile");
        if (skill is not null) commands.Add(new(CombatCommandKind.Cast, SkillId: skill.Id, TargetId: target.Id));
        return commands.ToArray();
    }

    private static Position Waypoint(Ashenwake.Core.Content.RoomDefinition room, Position from, Position target)
    {
        var spatial = new SpatialWorld(room);
        bool Clear(Position a, Position b)
        {
            if (!spatial.HasLineOfSight(a, b)) return false;
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Position.DistanceSquared(a, b)) / 100));
            for (int step = 1; step <= steps; step++)
                if (!spatial.CanOccupy(new(a.X + (b.X - a.X) * step / steps, a.Z + (b.Z - a.Z) * step / steps), CombatSession.ActorRadius)) return false;
            return true;
        }
        if (Clear(from, target)) return target;
        // Visibility graph around authored rectangle corners. Stable vertex order breaks equal-cost ties.
        const int clearance = CombatSession.ActorRadius + 200;
        var points = new List<Position> { from, target };
        foreach (var obstacle in room.Obstacles)
            foreach (var point in new[] { new Position(obstacle.MinX - clearance, obstacle.MinZ - clearance), new Position(obstacle.MinX - clearance, obstacle.MaxZ + clearance), new Position(obstacle.MaxX + clearance, obstacle.MinZ - clearance), new Position(obstacle.MaxX + clearance, obstacle.MaxZ + clearance) })
                if (spatial.CanOccupy(point, CombatSession.ActorRadius)) points.Add(point);
        var costs = Enumerable.Repeat(long.MaxValue, points.Count).ToArray();
        var previous = Enumerable.Repeat(-1, points.Count).ToArray(); var visited = new bool[points.Count]; costs[0] = 0;
        for (int count = 0; count < points.Count; count++)
        {
            int current = -1;
            for (int i = 0; i < points.Count; i++) if (!visited[i] && costs[i] < (current < 0 ? long.MaxValue : costs[current])) current = i;
            if (current < 0 || current == 1) break;
            visited[current] = true;
            for (int next = 0; next < points.Count; next++)
            {
                if (visited[next] || next == current || !Clear(points[current], points[next])) continue;
                long cost = costs[current] + (long)Math.Ceiling(Math.Sqrt(Position.DistanceSquared(points[current], points[next])));
                if (cost < costs[next]) { costs[next] = cost; previous[next] = current; }
            }
        }
        if (previous[1] < 0) return target;
        int waypoint = 1;
        while (previous[waypoint] > 0) waypoint = previous[waypoint];
        return points[waypoint];
    }
}
