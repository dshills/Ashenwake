using Ashenwake.Core.Combat;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Coop;

/// <summary>Ordinary intent-only reference policy for sockets, replay and bounded shared-world diagnostics.</summary>
public static class CoopSmoke
{
    public const int MaximumTicks = 12000;
    public static CoopInput Input(CoopView view, int playerId, long sequence)
    {
        var player = view.Players.Single(p => p.Id == playerId);
        var actor = view.Actors.Single(a => a.PlayerId == playerId);
        CoopInput Command(int x = 0, int z = 0, CoopInputAction action = CoopInputAction.None, string skill = "", int target = 0) => new(sequence, view.Tick + 1, x, z, action, skill, target);
        if (!player.Connected || view.Completed) return Command();
        if (view.Cleared || view.AwaitingRetry) return Command(action: CoopInputAction.Ready);
        if (actor.Health <= 0) return Command();
        if (actor.Health < actor.MaxHealth / 2 && player.PotionCharges > 0 && player.PotionCooldownTicks == 0) return Command(action: CoopInputAction.Potion);
        var target = view.Actors.Where(a => a.PlayerId == 0 && a.Health > 0).OrderBy(a => a.Role == "Anchor" ? 0 : a.Role == "Bell" ? 1 : 2).ThenBy(a => Position.DistanceSquared(actor.Position, a.Position)).ThenBy(a => a.Id).FirstOrDefault();
        if (target is null) return Command();
        var spatial = new SpatialWorld(view.Room);
        var threats = view.Warnings.Where(w => w.ResolveTick - view.Tick <= 36 && CoopCombatSession.WarningContains(w, actor.Position, 300)).ToArray();
        if (threats.Length > 0)
        {
            bool dodge = player.DodgeCooldownTicks == 0 && threats.Any(w => w.ResolveTick - view.Tick <= 12 || actor.TelegraphTicks > 0 && actor.TelegraphTicks + 7 >= w.ResolveTick - view.Tick);
            Position[] directions = [new(0, 1), new(0, -1), new(1, 0), new(-1, 0), new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)];
            Position Project(Position d)
            {
                int amount = dodge ? d.X != 0 && d.Z != 0 ? 1202 : 1700 : 3000;
                var moved = spatial.Move(actor.Position, new(actor.Position.X + d.X * amount, actor.Position.Z + d.Z * amount), CoopCombatSession.ActorRadius);
                return view.Actors.Any(a => a.Id != actor.Id && a.Health > 0 && Position.DistanceSquared(moved, a.Position) < 4L * CoopCombatSession.ActorRadius * CoopCombatSession.ActorRadius) ? actor.Position : moved;
            }
            var escape = directions.Select(d => (Direction: d, Position: Project(d)))
                .OrderBy(p => threats.Count(w => CoopCombatSession.WarningContains(w, p.Position, 300)))
                .ThenByDescending(p => Position.DistanceSquared(p.Position, actor.Position)).ThenBy(p => Position.DistanceSquared(p.Position, target.Position)).First();
            var direction = dodge ? escape.Direction : CombatProductionSmoke.MovementDirection(actor.Position, escape.Position, view.Room, view.Actors.Where(a => a.Id != actor.Id && a.Health > 0).Select(a => a.Position).ToArray());
            return Command(direction.X, direction.Z, dodge ? CoopInputAction.Dodge : CoopInputAction.None);
        }
        long distance = Position.DistanceSquared(actor.Position, target.Position);
        var move = distance > 1700L * 1700 || !spatial.HasLineOfSight(actor.Position, target.Position)
            ? CombatProductionSmoke.MovementDirection(actor.Position, target.Position, view.Room, view.Actors.Where(a => a.Id != actor.Id && a.Id != target.Id && a.Health > 0).Select(a => a.Position).ToArray()) : new Position(0, 0);
        if (actor.PendingSkill is not null || actor.State == "Recover") return Command(move.X, move.Z);
        var available = player.Skills.Where(s => s.RemainingTicks == 0 && s.Cost <= player.Momentum && (s.Shape == "Guard" ? actor.Barrier < 20 && actor.Health < actor.MaxHealth * 2 / 3 : distance <= (long)(s.Shape == "Area" ? s.Radius : s.Range) * (s.Shape == "Area" ? s.Radius : s.Range) && spatial.HasLineOfSight(actor.Position, target.Position))).ToArray();
        var skill = available.OrderBy(s => s.Shape == "Guard" ? 0 : s.Id == "skill.cataclysm" ? 1 : s.Id == "skill.shield_breaker" ? 2 : s.Id == "skill.cleave" ? 3 : s.Id == "skill.seismic_wave" ? 4 : 5).FirstOrDefault();
        return skill is null ? Command(move.X, move.Z) : Command(move.X, move.Z, CoopInputAction.Cast, skill.Id, target.Id);
    }
}
