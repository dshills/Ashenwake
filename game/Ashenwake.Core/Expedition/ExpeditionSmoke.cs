using Ashenwake.Core.Combat;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Expedition;

/// <summary>A deterministic validation input policy. It uses the same public commands as a player.</summary>
public static class ExpeditionSmoke
{
    public const int MaximumCommands = 12000;
    public static bool Complete(ExpeditionSession session) => session.Capture().Adventure.ReturnedToMara;
    public static ExpeditionCommand Next(ExpeditionSession session)
    {
        var state = session.Capture();
        var view = session.View;
        if (state.Adventure.BellVictories > 0)
        {
            if (view.RoomId != "room.greyhaven") return new(ExpeditionAction.Travel, "room.greyhaven");
            return AtInteraction(session, "npc.mara", new(ExpeditionAction.Interact, "npc.mara"));
        }
        if (!state.Adventure.QuestAccepted) return AtInteraction(session, "npc.mara", new(ExpeditionAction.Interact, "npc.mara"));
        if (view.RoomId == "room.greyhaven")
        {
            foreach (var (slot, fragment) in new[] { ("Spine", "fragment.nerve_ilyra"), ("Arms", "fragment.orrun_bone") })
                if (state.Adventure.Anatomy.GetValueOrDefault(slot) != fragment)
                    return AtInteraction(session, "service.mara", new(ExpeditionAction.InstallFragment, slot, fragment));
            if (!view.ActiveManifestations.Contains("manifestation.stone_memory"))
                return AtInteraction(session, "service.mara", new(ExpeditionAction.Manifestation, "manifestation.stone_memory"));
            var ashcleaver = state.Adventure.Godwrought.FirstOrDefault();
            if (ashcleaver is not null && state.Combat.Equipment.GetValueOrDefault("MainHand") != state.GodwroughtItems[ashcleaver.InstanceId])
                return new(ExpeditionAction.EquipGodwrought, ashcleaver.InstanceId);
            return new(ExpeditionAction.Travel, "room.ossuary");
        }
        if (view.EncounterId is not null) return new(ExpeditionAction.Tick, Commands: CombatInputs(session.Combat.View));
        var combat = session.Combat.View;
        if (combat.Loot.Count > 0) return new(ExpeditionAction.Tick, Commands: CombatInputs(combat));
        return new(ExpeditionAction.Travel, view.RoomId == "room.ossuary" ? "room.cloister" : "room.bell_sanctum");
    }
    public static ExpeditionCommand AtInteraction(ExpeditionSession session, string id, ExpeditionCommand action)
    {
        var interaction = session.Interactions.Single(i => i.ActionId == id);
        var player = session.Combat.View.Actors.Single(a => a.Id == 1);
        return Position.DistanceSquared(player.Position, interaction.Position) <= (long)interaction.Range * interaction.Range
            ? action : new(ExpeditionAction.Tick, Commands: [new(CombatCommandKind.Move, X: Math.Sign(interaction.Position.X - player.Position.X), Z: Math.Sign(interaction.Position.Z - player.Position.Z))]);
    }
    public static CombatCommand[] CombatInputs(CombatView view)
    {
        var player = view.Actors.Single(a => a.Id == 1);
        if (player.Health <= 0) return [];
        var target = view.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0)
            .OrderBy(a => a.Role == "Anchor" ? 0 : a.Role.Contains("Support", StringComparison.Ordinal) ? 1 : 2)
            .ThenBy(a => Position.DistanceSquared(player.Position, a.Position)).ThenBy(a => a.Id).FirstOrDefault();
        if (target is null)
        {
            var loot = view.Loot.OrderBy(l => Position.DistanceSquared(player.Position, l.Position)).ThenBy(l => l.Id).FirstOrDefault();
            return loot is null ? [new(CombatCommandKind.Stop)] :
                [new(CombatCommandKind.Move, X: Math.Sign(loot.Position.X - player.Position.X), Z: Math.Sign(loot.Position.Z - player.Position.Z)), new(CombatCommandKind.Pickup, ItemId: loot.Id)];
        }
        var commands = new List<CombatCommand>();
        if (player.Health < player.MaxHealth * 2 / 3 && view.PotionCharges > 0 && view.PotionCooldownTicks == 0) commands.Add(new(CombatCommandKind.Potion));
        var delta = new Position(Math.Sign(target.Position.X - player.Position.X), Math.Sign(target.Position.Z - player.Position.Z));
        var distance = Position.DistanceSquared(player.Position, target.Position);
        bool danger = view.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0 && a.TelegraphTicks is > 0 and <= 4 && Position.DistanceSquared(a.Position, player.Position) < 3000L * 3000);
        if (danger && view.DodgeCooldownTicks == 0)
        {
            commands.Add(new(CombatCommandKind.Dodge, X: delta.Z == 0 && delta.X == 0 ? 1 : -delta.Z, Z: delta.X));
            return commands.ToArray();
        }
        commands.Add(new(CombatCommandKind.Move, X: distance > 1700L * 1700 ? delta.X : 0, Z: distance > 1700L * 1700 ? delta.Z : 0));
        if (player.TelegraphTicks > 0 || player.State == "Recover") return commands.ToArray();
        bool Ready(string id) => view.Skills.Any(s => s.Id == id && s.RemainingTicks == 0 && s.Cost <= view.Momentum);
        string? skill = distance > 2600L * 2600 && distance < 6400L * 6400 && Ready("skill.charge") ? "skill.charge"
            : player.Barrier < 20 && danger && Ready("skill.iron_guard") ? "skill.iron_guard"
            : distance < 2500L * 2500 && Ready("skill.shield_breaker") ? "skill.shield_breaker"
            : distance < 4200L * 4200 && Ready("skill.cataclysm") ? "skill.cataclysm"
            : distance < 2300L * 2300 && Ready("skill.cleave") ? "skill.cleave"
            : distance < 10000L * 10000 && Ready("skill.seismic_wave") ? "skill.seismic_wave" : null;
        if (skill is not null) commands.Add(new(CombatCommandKind.Cast, SkillId: skill, TargetId: target.Id));
        return commands.ToArray();
    }
}
