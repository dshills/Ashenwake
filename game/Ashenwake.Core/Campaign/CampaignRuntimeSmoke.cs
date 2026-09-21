using Ashenwake.Core.Combat;
using Ashenwake.Core.Production;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Campaign;

/// <summary>Deterministic player-input policy exercising all acts, choices, exploration, loot, and the final return.</summary>
public static class CampaignRuntimeSmoke
{
    public const int MaximumCommands = 36000;
    public static bool Complete(CampaignRuntimeSession session)
    {
        var state = session.Capture().Campaign;
        return state.CompletedActs.Count == 5 && state.CompletedExploration.Count == session.Content.Capture().Exploration.Length && state.InHub && session.View.Ending is not null;
    }
    public static CampaignRuntimeCommand Next(CampaignRuntimeSession session)
    {
        var snapshot = session.Capture(); var state = snapshot.Campaign; var definition = session.Content.Capture();
        if (state.InHub)
        {
            foreach (var (slot, fragment) in new[] { ("Spine", "fragment.nerve_ilyra"), ("Arms", "fragment.orrun_bone") })
                if (snapshot.Production.Expedition.Adventure.Anatomy.GetValueOrDefault(slot) != fragment)
                    return AtInteraction(session, "service.mara", new(CampaignRuntimeAction.Production, Production: new(ProductionAction.Expedition, new(ExpeditionAction.InstallFragment, slot, fragment))));
            if (!session.Production.View.ActiveManifestations.Contains("manifestation.stone_memory"))
                return AtInteraction(session, "service.mara", new(CampaignRuntimeAction.Production, Production: new(ProductionAction.Expedition, new(ExpeditionAction.Manifestation, "manifestation.stone_memory"))));
            return new(CampaignRuntimeAction.EnterAct, Act: Math.Min(5, state.CompletedActs.Count + 1));
        }
        var view = session.Combat.View; var player = view.Actors.Single(a => a.Id == 1);
        if (session.ActiveEncounterId == CampaignRuntimeSession.CryptEncounter && session.EncounterCleared)
        {
            if (view.Loot.Count > 0 && view.Inventory.Count < 512) return CombatInput(session);
            string target = state.CompletedExploration.Contains(CampaignRuntimeSession.CryptEvent) ? "opening.crypt.return" : "opening.crypt.treasure";
            return AtInteraction(session, target, new(CampaignRuntimeAction.InteractOpening, Id: target));
        }
        bool verdant = definition.Exploration.Any(e => e.Id == CampaignRuntimeSession.ShrineEvent);
        if (verdant && session.ActiveEncounterId is CampaignRuntimeSession.ShrineEncounter or CampaignRuntimeSession.HuntEncounter && session.EncounterCleared)
        {
            if (view.Loot.Count > 0 && view.Inventory.Count < 512) return CombatInput(session);
            string target = session.ActiveEncounterId == CampaignRuntimeSession.HuntEncounter ? "verdant.hunt.return" :
                state.CompletedExploration.Contains(CampaignRuntimeSession.ShrineEvent) ? "verdant.shrine.return" : "verdant.shrine.treasure";
            return AtInteraction(session, target, new(CampaignRuntimeAction.InteractVerdant, Id: target));
        }
        if (state.Exploration is { } exploration)
        {
            var eventDefinition = definition.Exploration.Single(e => e.Id == exploration.Id);
            if (eventDefinition.Kind == "Hunt" && exploration.TrackedClues < eventDefinition.Clues.Length)
            {
                string clue = eventDefinition.Clues[exploration.TrackedClues];
                return AtInteraction(session, clue, new(CampaignRuntimeAction.TrackClue, Id: clue));
            }
            if (view.Inventory.Count >= 512 && !view.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0)) return new(CampaignRuntimeAction.LeaveExploration);
            return CombatInput(session);
        }
        if (!session.EncounterCleared) return CombatInput(session);
        var loot = view.Loot.OrderBy(l => Position.DistanceSquared(l.Position, player.Position)).ThenBy(l => l.Id).FirstOrDefault();
        if (loot is not null && view.Inventory.Count < 512) return CombatInput(session);
        if (verdant && state.CurrentAct == 2)
        {
            string? passage = null;
            if (!state.CompletedExploration.Contains(CampaignRuntimeSession.ShrineEvent))
                passage = session.ActiveEncounterId switch
                {
                    "campaign.plague_village" => "verdant.back.ruins",
                    "campaign.rootheart" => "verdant.back.village",
                    _ => "verdant.shrine.enter"
                };
            else if (!state.CompletedExploration.Contains(CampaignRuntimeSession.HuntEvent))
                passage = session.ActiveEncounterId switch
                {
                    "campaign.plague_village" => "verdant.hunt.enter",
                    "campaign.rootheart" => "verdant.back.village",
                    "campaign.living_ruins" when state.CompletedEncounters.Contains("campaign.plague_village") => "verdant.forward.village",
                    _ => null
                };
            if (passage is not null) return AtInteraction(session, passage, new(CampaignRuntimeAction.InteractVerdant, Id: passage));
        }
        var optional = definition.Exploration.FirstOrDefault(e => e.Act == state.CurrentAct && !state.CompletedExploration.Contains(e.Id) && !(verdant && e.Act == 2));
        if (optional?.Id == CampaignRuntimeSession.CryptEvent)
        {
            string passage = session.ActiveEncounterId switch
            {
                "campaign.monastery" => "opening.back.road",
                "campaign.bell_saint" => "opening.back.monastery",
                _ => "opening.crypt.enter"
            };
            return AtInteraction(session, passage, new(CampaignRuntimeAction.InteractOpening, Id: passage));
        }
        if (optional is not null) return new(CampaignRuntimeAction.BeginExploration, Id: optional.Id);
        var act = definition.Acts[state.CurrentAct - 1]; var choice = definition.Choices.Single(c => c.Id == act.RequiredChoice);
        if (!state.Choices.ContainsKey(choice.Id) && state.CompletedEncounters.Contains(choice.RequiredEncounter))
            return new(CampaignRuntimeAction.Choose, Id: choice.Id, Value: choice.Outcomes[0].Id);
        if (!state.CompletedActs.Contains(state.CurrentAct))
        {
            var forward = session.Interactions.FirstOrDefault(i => i.ActionId.StartsWith("opening.forward.", StringComparison.Ordinal));
            if (forward is not null) return AtInteraction(session, forward.ActionId, new(CampaignRuntimeAction.InteractOpening, Id: forward.ActionId));
            if (verdant && state.CurrentAct == 2)
            {
                forward = session.Interactions.FirstOrDefault(i => i.ActionId.StartsWith("verdant.forward.", StringComparison.Ordinal));
                if (forward is not null) return AtInteraction(session, forward.ActionId, new(CampaignRuntimeAction.InteractVerdant, Id: forward.ActionId));
                if (Position.DistanceSquared(player.Position, VerdantCampaignLayout.ForwardExit) > 2000L * 2000)
                {
                    var direction = CombatProductionSmoke.MovementDirection(player.Position, VerdantCampaignLayout.ForwardExit, session.Room);
                    return new(CampaignRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]);
                }
            }
            return new(CampaignRuntimeAction.AdvanceEncounter);
        }
        return state.CurrentAct == 5 ? new(CampaignRuntimeAction.ReturnToHub) : new(CampaignRuntimeAction.EnterAct, Act: state.CurrentAct + 1);
    }
    private static CampaignRuntimeCommand CombatInput(CampaignRuntimeSession session)
    {
        var view = session.Combat.View; var player = view.Actors.Single(a => a.Id == 1);
        if (!view.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
        {
            var loot = view.Loot.OrderBy(l => Position.DistanceSquared(l.Position, player.Position)).ThenBy(l => l.Id).FirstOrDefault();
            if (loot is not null)
            {
                var direction = CombatProductionSmoke.MovementDirection(player.Position, loot.Position, session.Room);
                return new(CampaignRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z), new(CombatCommandKind.Pickup, ItemId: loot.Id)]);
            }
        }
        return new(CampaignRuntimeAction.Tick, Commands: CampaignCombatSmoke.Commands(view, session.Room));
    }
    public static CampaignRuntimeCommand AtInteraction(CampaignRuntimeSession session, string id, CampaignRuntimeCommand action)
    {
        var interaction = session.Interactions.Single(i => i.ActionId == id); var player = session.Combat.View.Actors.Single(a => a.Id == 1);
        if (Position.DistanceSquared(player.Position, interaction.Position) <= (long)Math.Min(interaction.Range, 2000) * Math.Min(interaction.Range, 2000)) return action;
        var direction = CombatProductionSmoke.MovementDirection(player.Position, interaction.Position, session.Room);
        return new(CampaignRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]);
    }
}
