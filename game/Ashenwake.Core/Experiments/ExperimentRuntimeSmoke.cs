using Ashenwake.Core.Combat;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Experiments;

/// <summary>Ordinary input policy that binds one nearby memory, casts it and moves out of the visible Storm warning.</summary>
public static class ExperimentRuntimeSmoke
{
    public const int MaximumCommands = 40000;
    public static bool Complete(ExperimentRuntimeSession session) => session.InHub && session.View.Cosmetics.Count == 1;
    public static ExperimentCommand Next(ExperimentRuntimeSession session)
    {
        var view = session.Combat.View; var player = view.Actors.Single(a => a.Id == 1); var memory = session.View.Memory;
        if (player.Health > 0 && memory is { Status: "Offered" })
        {
            if (memory.CanBind) return new(ExperimentAction.BindMemory, SourceActorId: memory.SourceActorId);
            if (Position.DistanceSquared(player.Position, memory.MemoryPosition) <= (long)memory.BindRadius * memory.BindRadius && new SpatialWorld(session.Endgame.Room).HasLineOfSight(player.Position, memory.MemoryPosition))
                return new(ExperimentAction.Tick, Commands: [new(CombatCommandKind.Stop)]);
            var toward = CombatProductionSmoke.MovementDirection(player.Position, memory.MemoryPosition, session.Endgame.Room);
            return new(ExperimentAction.Tick, Commands: [new(CombatCommandKind.Move, X: toward.X, Z: toward.Z)]);
        }
        if (player.Health > 0 && memory is { Status: "Bound" })
        {
            var target = view.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0 && a.Visible).OrderBy(a => Position.DistanceSquared(a.Position, player.Position)).ThenBy(a => a.Id).FirstOrDefault();
            if (target is not null)
            {
                if (Position.DistanceSquared(target.Position, player.Position) <= 10000L * 10000 && new SpatialWorld(session.Endgame.Room).HasLineOfSight(player.Position, target.Position))
                    return new(ExperimentAction.Tick, Commands: [new(CombatCommandKind.Stop), new(CombatCommandKind.CastEcho, TargetId: target.Id)]);
                var toward = CombatProductionSmoke.MovementDirection(player.Position, target.Position, session.Endgame.Room);
                return new(ExperimentAction.Tick, Commands: [new(CombatCommandKind.Move, X: toward.X, Z: toward.Z)]);
            }
            // The explicit room advance carries the bound memory into the next
            // fight; its existing result discloses any ground loot left behind.
            if (session.Endgame.RunView?.CanAdvance == true)
                return new(ExperimentAction.Endgame, Endgame: new(EndgameRuntimeAction.AdvanceEncounter));
        }
        if (memory is { HazardStage: "Warning" or "Active" } && view.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
        {
            var warning = new CombatHazardView(memory.HazardId, "Circle", memory.HazardPosition, memory.HazardPosition, memory.HazardRadius,
                memory.HazardStage == "Warning" ? memory.HazardRemainingTicks : 0, "enemy.stormbound", 0);
            view = view with { CampaignHazards = [.. view.CampaignHazards ?? [], warning] };
            return new(ExperimentAction.Tick, Commands: EndgameCombatSmoke.Commands(view, session.Endgame.Room));
        }
        var ordinary = EndgameRuntimeSmoke.Next(session.Endgame);
        if (ordinary.Action == EndgameRuntimeAction.StartFracture && !session.View.Cosmetics.Contains(session.Content.Capture().CosmeticId))
            return new(ExperimentAction.StartContract, ordinary.SigilId);
        return ordinary.Action == EndgameRuntimeAction.Tick ? new(ExperimentAction.Tick, Commands: ordinary.Commands) : new(ExperimentAction.Endgame, Endgame: ordinary);
    }
}
