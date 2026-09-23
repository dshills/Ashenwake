using Ashenwake.Core.Combat;
using Ashenwake.Core.Production;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Endgame;

/// <summary>Ordinary inputs after reaching a champion's secured source; never injects discovery, victory, or rewards.</summary>
public static class RoamingChampionSmoke
{
    public static EndgameRuntimeCommand Next(EndgameRuntimeSession session, string id)
    {
        _ = RoamingChampionCatalog.Find(id) ?? throw new ArgumentException("Unknown roaming champion.", nameof(id));
        var run = session.RoamingChampions.Run;
        if (run is null) return Approach(session, RoamingChampionCatalog.SightingPosition, new(EndgameRuntimeAction.EnterRoamingChampion, Id: id));
        return run.Stage switch
        {
            "Foyer" => Approach(session, RoamingChampionCatalog.ChallengePosition, new(EndgameRuntimeAction.ChallengeRoamingChampion)),
            "Combat" => new(EndgameRuntimeAction.Tick, Commands: EndgameCombatSmoke.Commands(session.Combat.View, session.Room)),
            "Victory" => Approach(session, RoamingChampionCatalog.TreasurePosition, new(EndgameRuntimeAction.ClaimRoamingChampionReward)),
            _ => new(EndgameRuntimeAction.ExitRoamingChampion)
        };
    }
    public static EndgameRuntimeCommand Approach(EndgameRuntimeSession session, Position target, EndgameRuntimeCommand command)
    {
        var player = session.Combat.View.Actors.Single(a => a.Id == 1);
        int range = RoamingChampionCatalog.InteractionRange - 100;
        if (Position.DistanceSquared(player.Position, target) <= (long)range * range) return command;
        var direction = CombatProductionSmoke.MovementDirection(player.Position, target, session.Room);
        return new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]);
    }
}
