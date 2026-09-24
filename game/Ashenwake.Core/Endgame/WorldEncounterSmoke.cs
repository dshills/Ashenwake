using Ashenwake.Core.Production;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Endgame;

/// <summary>Ordinary movement and combat commands after reaching an encounter's secured source room.</summary>
public static class WorldEncounterSmoke
{
    public static EndgameRuntimeCommand Next(EndgameRuntimeSession session, string id, bool caravanCombat = false)
    {
        var d = WorldEncounterCatalog.Find(id) ?? throw new ArgumentException("Unknown world encounter.", nameof(id));
        var run = session.WorldEncounters.Run;
        if (run is null) return Approach(session, d.EntrancePosition, new(EndgameRuntimeAction.EnterWorldEncounter, Id: id));
        return run.Stage switch
        {
            "Foyer" => Approach(session, WorldEncounterCatalog.ChoicePosition, new(EndgameRuntimeAction.ChooseWorldEncounter, Value: id switch
            { "event.lantern" => "rescue", "event.shrine" => "accept", "event.caravan" => caravanCombat ? "escort" : run.PuzzleStep switch { 0 => "extinguish", 1 => "return", _ => "speak" }, _ => "stabilize" })),
            "Combat" => new(EndgameRuntimeAction.Tick, Commands: EndgameCombatSmoke.Commands(session.Combat.View, session.Room)),
            "Victory" => Approach(session, WorldEncounterCatalog.TreasurePosition, new(EndgameRuntimeAction.ClaimWorldEncounterReward)),
            _ => new(EndgameRuntimeAction.ExitWorldEncounter)
        };
    }
    public static EndgameRuntimeCommand Approach(EndgameRuntimeSession session, Position target, EndgameRuntimeCommand action)
    {
        var player = session.Combat.View.Actors.Single(a => a.Id == 1);
        int range = WorldEncounterCatalog.InteractionRange - 100;
        if (Position.DistanceSquared(player.Position, target) <= (long)range * range) return action;
        var direction = CombatProductionSmoke.MovementDirection(player.Position, target, session.Room);
        return new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]);
    }
}
