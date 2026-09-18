using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Endgame;

/// <summary>Bounded ordinary player commands. No victory, reward, item, or difficulty state is injected.</summary>
public static class EndgameRuntimeSmoke
{
    public const int MaximumCommands = 180000;
    public static bool Complete(EndgameRuntimeSession session, int targetTier = 1, bool allHunts = false)
    {
        CheckTier(targetTier); var view = session.View;
        if (!session.InHub || view.HighestClearedTier < targetTier) return false;
        return !allHunts || session.Content.Capture().Hunts.All(h => session.Capture().Endgame.Rewards.Values.Any(r => r.Kind == "GodHunt" && r.ContentId == h.Id));
    }
    public static EndgameRuntimeCommand Next(EndgameRuntimeSession session, int targetTier = 1, bool allHunts = false)
    {
        CheckTier(targetTier);
        if (!session.View.Unlocked || !session.Campaign.InHub)
            return new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(session.Campaign));
        if (!session.InHub)
        {
            var run = session.RunView!;
            if (run.CanRetry) return new(EndgameRuntimeAction.RetryEncounter);
            if (run.Status is "Failed" or "Abandoned") return new(EndgameRuntimeAction.ReturnToHub);
            if (session.EncounterCleared)
            {
                var loot = NearestLoot(session);
                if (loot is not null && session.Combat.View.Inventory.Count < 512) return Pickup(session, loot);
                return new(run.Status == "Completed" ? EndgameRuntimeAction.ReturnToHub : EndgameRuntimeAction.AdvanceEncounter);
            }
            return new(EndgameRuntimeAction.Tick, Commands: EndgameCombatSmoke.Commands(session.Combat.View, session.Room));
        }
        var permanent = session.Production.Capture().Progression.Character;
        var progress = session.Production.ProgressionView;
        if (progress.AvailablePassivePoints > 0)
        {
            int spent = permanent.Passives.Values.Sum(); string passive = spent % 3 == 0 ? "Defense" : "Offense";
            return At(session, "service.mara", new(EndgameRuntimeAction.Production, Production: new(ProductionAction.Passive, Id: passive)));
        }
        foreach (var skill in session.Production.Content.Capture().Skills.Where(s => s.Discipline == permanent.Discipline && s.Mutations.Length > 0))
            if (permanent.Mastery.GetValueOrDefault(skill.Id) >= 100 && !permanent.SelectedMutations.ContainsKey(skill.Id))
                return At(session, "service.mara", new(EndgameRuntimeAction.Production, Production: new(ProductionAction.Mutation, Id: skill.Id, Value: skill.Mutations[0])));
        var view = session.View;
        if (allHunts)
        {
            var rewards = session.Capture().Endgame.Rewards.Values;
            string? hunt = view.UnlockedHunts.FirstOrDefault(id => !rewards.Any(r => r.Kind == "GodHunt" && r.ContentId == id));
            if (hunt is not null) return AtGate(session, new(EndgameRuntimeAction.StartGodHunt, Id: hunt));
        }
        var sigil = view.AvailableSigils.OrderByDescending(s => s.Tier).ThenBy(s => s.Id).FirstOrDefault();
        return AtGate(session, sigil is null ? new(EndgameRuntimeAction.ClaimRecoverySigil) : new(EndgameRuntimeAction.StartFracture, sigil.Id));
    }
    public static EndgameRuntimeCommand AtGate(EndgameRuntimeSession session, EndgameRuntimeCommand command) => At(session, "endgame.gate", command);
    private static EndgameRuntimeCommand At(EndgameRuntimeSession session, string id, EndgameRuntimeCommand command)
    {
        var interaction = session.Interactions.Single(i => i.ActionId == id); var player = session.Combat.View.Actors.Single(a => a.Id == 1);
        if (Position.DistanceSquared(player.Position, interaction.Position) <= 2000L * 2000) return command;
        var direction = CombatProductionSmoke.MovementDirection(player.Position, interaction.Position, session.Room);
        return new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]);
    }
    private static CombatLoot? NearestLoot(EndgameRuntimeSession session)
    {
        var view = session.Combat.View; var player = view.Actors.Single(a => a.Id == 1);
        return view.Loot.OrderBy(l => Position.DistanceSquared(l.Position, player.Position)).ThenBy(l => l.Id).FirstOrDefault();
    }
    private static EndgameRuntimeCommand Pickup(EndgameRuntimeSession session, CombatLoot loot)
    {
        var player = session.Combat.View.Actors.Single(a => a.Id == 1);
        var direction = CombatProductionSmoke.MovementDirection(player.Position, loot.Position, session.Room);
        return new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z), new(CombatCommandKind.Pickup, ItemId: loot.Id)]);
    }
    private static void CheckTier(int tier)
    { if (tier is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(tier)); }
}
