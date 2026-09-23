using Ashenwake.Core.Combat;
using Ashenwake.Core.Production;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Endgame;

/// <summary>Ordinary public inputs for exercising a single regional contract without injecting rewards or victory.</summary>
public static class RegionalHuntSmoke
{
    public static EndgameRuntimeCommand Next(EndgameRuntimeSession session, string contractId)
    {
        var run = session.RegionalHunts.Run;
        if (run is null || run.Stage is "Claimed" or "Abandoned")
            return AtBoard(session, new(EndgameRuntimeAction.StartRegionalHunt, Id: contractId));
        if (run.Stage == "Tracking")
        {
            var clue = session.Interactions.Single();
            return Approach(session, clue.Position, clue.Range, new(EndgameRuntimeAction.TrackRegionalHuntClue, Id: clue.ActionId));
        }
        if (run.Stage == "Combat")
            return new(EndgameRuntimeAction.Tick, Commands: EndgameCombatSmoke.Commands(session.Combat.View, session.Room));
        if (session.InRegionalHunt) return new(EndgameRuntimeAction.ReturnRegionalHunt);
        return run.Stage == "Victory" ? AtBoard(session, new(EndgameRuntimeAction.ClaimRegionalHuntReward)) : new(EndgameRuntimeAction.AbandonRegionalHunt);
    }
    public static EndgameRuntimeCommand AtBoard(EndgameRuntimeSession session, EndgameRuntimeCommand command)
        => Approach(session, RegionalHuntCatalog.BoardPosition, RegionalHuntCatalog.BoardRange, command);
    private static EndgameRuntimeCommand Approach(EndgameRuntimeSession session, Position target, int range, EndgameRuntimeCommand command)
    {
        var player = session.Combat.View.Actors.Single(a => a.Id == 1);
        if (Position.DistanceSquared(player.Position, target) <= (long)(range - 100) * (range - 100)) return command;
        var direction = CombatProductionSmoke.MovementDirection(player.Position, target, session.Room);
        return new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]);
    }
}
