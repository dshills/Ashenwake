using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

/// <summary>Input-only reference policy. It reads published warnings, weak points and interaction ranges.</summary>
public static class EndgameCombatSmoke
{
    public static CombatCommand[] Commands(CombatView view, RoomDefinition room)
    {
        if (view.Endgame is not { } endgame) return CampaignCombatSmoke.Commands(view, room);
        var player = view.Actors.Single(a => a.Id == 1);
        if (player.Health <= 0 || !view.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0)) return [new(CombatCommandKind.Stop)];
        var warnings = endgame.Hazards.Select(h => new CombatHazardView(h.Id, h.Kind, h.Position, h.End, h.Radius,
            h.Stage == "Active" ? 0 : h.RemainingTicks, h.ContentId, h.SourceId)).Concat(view.CampaignHazards ?? []).ToArray();
        bool hasRealTargets = view.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0 && a.State != "FalseEcho");
        var interpreted = view with { CampaignHazards = warnings, Actors = hasRealTargets ? view.Actors.Where(a => a.State != "FalseEcho").ToArray() : view.Actors };
        bool threatened = warnings.Any(h => h.RemainingTicks <= 40 && CombatSession.HazardContains(h with { Radius = h.Radius + 350 }, player.Position));
        if (threatened)
        {
            // Reuse the campaign geometry policy, with the longer tell horizon exposed to it.
            interpreted = interpreted with { CampaignHazards = warnings.Select(h => h with { RemainingTicks = Math.Max(0, h.RemainingTicks - 6) }).ToArray() };
            return CampaignCombatSmoke.Commands(interpreted, room);
        }
        var mechanism = endgame.Mechanisms.Where(m => m.Available).OrderBy(m => Position.DistanceSquared(m.Position, player.Position)).ThenBy(m => m.Id).FirstOrDefault();
        if (mechanism is not null)
        {
            var commands = new List<CombatCommand>();
            if (player.Health < player.MaxHealth / 2 && view.PotionCharges > 0 && view.PotionCooldownTicks == 0) commands.Add(new(CombatCommandKind.Potion));
            if (Position.DistanceSquared(player.Position, mechanism.Position) <= (long)mechanism.Radius * mechanism.Radius)
            { commands.Add(new(CombatCommandKind.Stop)); commands.Add(new(CombatCommandKind.InteractMechanism, TargetId: mechanism.Id)); }
            else
            {
                var move = CombatProductionSmoke.MovementDirection(player.Position, mechanism.Position, room);
                commands.Add(new(CombatCommandKind.Move, X: move.X, Z: move.Z));
            }
            return commands.ToArray();
        }
        return CampaignCombatSmoke.Commands(interpreted, room);
    }
}
