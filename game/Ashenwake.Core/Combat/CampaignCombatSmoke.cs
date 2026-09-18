using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

/// <summary>A reproducible baseline that respects campaign warning geometry; it does not bypass combat authority.</summary>
public static class CampaignCombatSmoke
{
    public static CombatCommand[] Commands(CombatView view, RoomDefinition room)
    {
        var player = view.Actors.First(a => a.Id == 1);
        var threats = (view.CampaignHazards ?? []).Where(h => h.RemainingTicks <= 34 && CombatSession.HazardContains(h with { Radius = h.Radius + 350 }, player.Position)).ToArray();
        if (threats.Length == 0) return OrdinaryCommands(view, room);
        var space = new SpatialWorld(room);
        var target = view.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0).OrderBy(a => a.Role == "Anchor" ? 0 : 1).ThenBy(a => Position.DistanceSquared(player.Position, a.Position)).First();
        var directions = new[] { (X: 0, Z: 1), (X: 0, Z: -1), (X: 1, Z: 0), (X: -1, Z: 0), (X: 1, Z: 1), (X: 1, Z: -1), (X: -1, Z: 1), (X: -1, Z: -1) };
        var direction = directions.Select(d => (Direction: d, Position: space.Move(player.Position, new(player.Position.X + d.X * 3000, player.Position.Z + d.Z * 3000), CombatSession.ActorRadius)))
            .OrderBy(p => threats.Count(h => CombatSession.HazardContains(h with { Radius = h.Radius + 350 }, p.Position)))
            .ThenBy(p => Position.DistanceSquared(p.Position, target.Position))
            .ThenByDescending(p => Position.DistanceSquared(player.Position, p.Position)).First().Direction;
        var commands = new List<CombatCommand>();
        if (player.Health < player.MaxHealth / 2 && view.PotionCharges > 0 && view.PotionCooldownTicks == 0) commands.Add(new(CombatCommandKind.Potion));
        commands.Add(new(CombatCommandKind.Move, X: direction.X, Z: direction.Z));
        if (view.DodgeCooldownTicks == 0 && (player.State == "Windup" || threats.Any(h => h.RemainingTicks <= 12))) commands.Add(new(CombatCommandKind.Dodge, X: direction.X, Z: direction.Z));
        return commands.ToArray();
    }
    private static CombatCommand[] OrdinaryCommands(CombatView view, RoomDefinition room)
    {
        var commands = CombatProductionSmoke.Commands(view, room).ToList();
        if (view.Discipline != "Gravecaller") return commands.ToArray();
        var player = view.Actors.First(a => a.Id == 1);
        var target = view.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0).OrderBy(a => a.Role == "Anchor" ? 0 : 1).ThenBy(a => Position.DistanceSquared(player.Position, a.Position)).FirstOrDefault();
        if (target is null) return commands.ToArray();
        long distance = Position.DistanceSquared(player.Position, target.Position);
        if (distance > 3000L * 3000)
        {
            var direction = CombatProductionSmoke.MovementDirection(player.Position, target.Position, room);
            commands.RemoveAll(c => c.Kind is CombatCommandKind.Move or CombatCommandKind.Stop);
            commands.Insert(0, new(CombatCommandKind.Move, X: direction.X, Z: direction.Z));
        }
        var corpse = view.Actors.FirstOrDefault(a => a.Faction == CombatFaction.Enemy && a.Health == 0 && !a.CorpseConsumed && Position.DistanceSquared(a.Position, player.Position) <= 6000L * 6000);
        if (corpse is not null && view.Resource < 20) commands.Add(new(CombatCommandKind.ConsumeCorpse, TargetId: corpse.Id));
        var summon = view.Skills.FirstOrDefault(s => s.Id == "skill.raise_ancestor" && s.Available && s.RemainingTicks == 0 && s.Cost <= view.Resource);
        var siphon = view.Skills.FirstOrDefault(s => s.Id == "skill.soul_siphon" && s.RemainingTicks == 0);
        if (view.Tick % 3 == 0 && corpse is not null && summon is not null && view.Actors.Count(a => a.Faction == CombatFaction.Ally && a.Health > 0) < 2)
        { commands.RemoveAll(c => c.Kind == CombatCommandKind.Cast); commands.Add(new(CombatCommandKind.Cast, SkillId: summon.Id)); }
        else if (view.Tick % 3 == 0 && distance <= 3500L * 3500 && siphon is not null && player.Health < player.MaxHealth)
        { commands.RemoveAll(c => c.Kind == CombatCommandKind.Cast); commands.Add(new(CombatCommandKind.Cast, SkillId: siphon.Id, TargetId: target.Id)); }
        return commands.ToArray();
    }
}
