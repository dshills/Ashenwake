using Ashenwake.Core.Combat;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Production;

/// <summary>Public input policy for fresh-character dungeon/progression validation; never mutates saved state.</summary>
public static class ProductionSmoke
{
    public const int MaximumCommands = 12000;
    public static bool Complete(ProductionSession session) => session.Capture().Expedition.Adventure.ReturnedToMara;
    private static ProductionCommand World(ExpeditionAction action, string id = "", string value = "", CombatCommand[]? commands = null)
        => new(ProductionAction.Expedition, new(action, id, value, Commands: commands));
    public static ProductionCommand Next(ProductionSession session)
    {
        var state = session.Capture().Expedition.Adventure; var view = session.View;
        if (state.BellVictories > 0)
            return view.RoomId != "room.greyhaven" ? World(ExpeditionAction.Travel, "room.greyhaven") : AtInteraction(session, "npc.mara", World(ExpeditionAction.Interact, "npc.mara"));
        if (!state.QuestAccepted) return AtInteraction(session, "npc.mara", World(ExpeditionAction.Interact, "npc.mara"));
        if (view.RoomId == "room.greyhaven")
        {
            foreach (var (slot, fragment) in new[] { ("Spine", "fragment.nerve_ilyra"), ("Arms", "fragment.orrun_bone") })
                if (state.Anatomy.GetValueOrDefault(slot) != fragment)
                    return AtInteraction(session, "service.mara", World(ExpeditionAction.InstallFragment, slot, fragment));
            if (!view.ActiveManifestations.Contains("manifestation.stone_memory"))
                return AtInteraction(session, "service.mara", World(ExpeditionAction.Manifestation, "manifestation.stone_memory"));
            return World(ExpeditionAction.Travel, "room.ossuary");
        }
        if (view.EncounterId is not null) return World(ExpeditionAction.Tick, commands: CombatProductionSmoke.Commands(session.Combat.View));
        var combat = session.Combat.View; var player = combat.Actors.Single(a => a.Id == 1);
        var loot = combat.Loot.OrderBy(l => Position.DistanceSquared(l.Position, player.Position)).ThenBy(l => l.Id).FirstOrDefault();
        if (loot is not null) return World(ExpeditionAction.Tick, commands: [new(CombatCommandKind.Move, X: Math.Sign(loot.Position.X - player.Position.X), Z: Math.Sign(loot.Position.Z - player.Position.Z)), new(CombatCommandKind.Pickup, ItemId: loot.Id)]);
        return World(ExpeditionAction.Travel, view.RoomId == "room.ossuary" ? "room.cloister" : "room.bell_sanctum");
    }
    public static ProductionCommand AtInteraction(ProductionSession session, string id, ProductionCommand action)
    {
        var interaction = session.Interactions.Single(i => i.ActionId == id); var player = session.Combat.View.Actors.Single(a => a.Id == 1);
        int range = Math.Min(interaction.Range, 2200);
        return Position.DistanceSquared(player.Position, interaction.Position) <= (long)range * range ? action
            : World(ExpeditionAction.Tick, commands: [new(CombatCommandKind.Move, X: Math.Sign(interaction.Position.X - player.Position.X), Z: Math.Sign(interaction.Position.Z - player.Position.Z))]);
    }
}
