using Ashenwake.Core.Combat;
using Ashenwake.Core.Production;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Endgame;

/// <summary>Ordinary inputs after reaching a chamber's secured campaign source; does not inject discoveries or victories.</summary>
public static class SecretChamberSmoke
{
    public static EndgameRuntimeCommand Next(EndgameRuntimeSession session, string id)
    {
        var definition = SecretChamberCatalog.Find(id) ?? throw new ArgumentException("Unknown chamber.", nameof(id));
        var run = session.SecretChambers.Run;
        if (run is null)
        {
            var entry = session.SecretChambers.Entries.Single(e => e.Id == id && e.Here);
            if (entry.Revealed) return Approach(session, definition.EntrancePosition, new(EndgameRuntimeAction.EnterSecretChamber, Id: id));
            var clue = definition.Clues[entry.PuzzleStep];
            return Approach(session, clue.Position, new(EndgameRuntimeAction.ResolveSecretClue, Id: clue.Id, Value: clue.Solution));
        }
        return run.Stage switch
        {
            "Foyer" => Approach(session, SecretChamberCatalog.ChallengePosition, new(EndgameRuntimeAction.ChallengeSecretGuardian)),
            "Combat" => new(EndgameRuntimeAction.Tick, Commands: EndgameCombatSmoke.Commands(session.Combat.View, session.Room)),
            "Victory" => Approach(session, SecretChamberCatalog.TreasurePosition, new(EndgameRuntimeAction.ClaimSecretTreasure)),
            _ => new(EndgameRuntimeAction.ExitSecretChamber)
        };
    }
    public static EndgameRuntimeCommand Approach(EndgameRuntimeSession session, Position target, EndgameRuntimeCommand command)
    {
        var player = session.Combat.View.Actors.Single(a => a.Id == 1);
        int range = SecretChamberCatalog.InteractionRange - 100;
        if (Position.DistanceSquared(player.Position, target) <= (long)range * range) return command;
        var direction = CombatProductionSmoke.MovementDirection(player.Position, target, session.Room);
        return new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]);
    }
}
