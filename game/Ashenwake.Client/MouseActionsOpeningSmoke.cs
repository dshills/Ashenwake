using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

public partial class MouseActionsSmoke
{
    private readonly List<OpeningEnvironmentChecks.Evidence> _openingLayouts = [];

    private async Task OpeningExploration()
    {
        await CloseJourney();
        Check("opening_mouse_branch_starts_on_secured_road", Session.Campaign.ActiveEncounterId == "campaign.road" && Session.EncounterCleared && !_sandbox.IsPaused);
        string roadRoom = JsonData.Hash(Session.Room), roadLoot = JsonData.Hash(Session.Combat.Capture().Loot);
        var roadLootIds = Session.Combat.View.Loot.Select(l => l.Id).ToHashSet();
        Check("opening_mouse_branch_retains_real_uncollected_road_loot", roadLootIds.Count > 0);
        var entrance = Session.Interactions.Single(i => i.ActionId == "opening.crypt.enter");
        await ReachVisibleGroundCheckpoint(p => CorePosition.DistanceSquared(p, entrance.Position), entrance.Range, "crypt_entrance");
        await ClickOpeningMarker("opening.crypt.enter", "crypt_enter", "exploration.widow_crypt");
        Check("crypt_mouse_entry_uses_real_room_enemies_and_style", Session.Campaign.ActiveEncounterId == CampaignRuntimeSession.CryptEncounter &&
            Session.Campaign.Capture().Campaign.Exploration?.Id == CampaignRuntimeSession.CryptEvent && _sandbox.EnvironmentStyle == "crypt" &&
            _stage.PresentedEncounter == CampaignRuntimeSession.CryptEncounter && JsonData.Hash(Session.Room) != roadRoom &&
            Session.Room.Obstacles.Length == 4 && Session.Combat.View.Actors.Count(a => a.Faction == CombatFaction.Enemy && a.Health > 0) == 3);
        InspectCrypt("crypt");
        await Capture("mouse-crypt-entered.png");

        int deaths = Session.Campaign.Capture().Campaign.Deaths;
        for (int tick = 0; !Session.EncounterCleared && tick < 6000; tick++)
        {
            var commands = CampaignCombatSmoke.Commands(Session.Combat.View, Session.Room).Where(c => c.Kind != CombatCommandKind.Pickup).ToArray();
            Execute(new(EndgameRuntimeAction.Tick, Commands: commands), refresh: false);
            if (tick % 90 == 0) { Refresh(); await Frames(); }
        }
        Execute(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)]));
        await CloseJourney(); await Frames();
        Check("crypt_authored_battle_is_won_without_death_or_automatic_claim", Session.EncounterCleared &&
            Session.Campaign.Capture().Campaign.Deaths == deaths && Session.Combat.View.Actors.Single(a => a.Id == 1).Health > 0 &&
            !Session.Campaign.Capture().Campaign.CompletedExploration.Contains(CampaignRuntimeSession.CryptEvent) &&
            Session.Interactions.Any(i => i.ActionId == "opening.crypt.treasure"));
        string cryptLoot = JsonData.Hash(Session.Combat.Capture().Loot);
        Check("crypt_battle_leaves_its_own_real_floor_rewards", Session.Combat.View.Loot.Count > 0 &&
            !Session.Combat.View.Loot.Any(l => roadLootIds.Contains(l.Id)));
        var beforeClaim = Session.Capture().Campaign.Production.Progression.Character;
        var existingItems = beforeClaim.Items.Select(i => i.Id).ToHashSet();
        await ClickOpeningMarker("opening.crypt.treasure", "crypt_testament", CampaignRuntimeSession.CryptEncounter);
        var claimed = Session.Capture().Campaign.Production.Progression.Character;
        var additions = claimed.Items.Where(i => !existingItems.Contains(i.Id)).ToArray();
        Check("crypt_mouse_testament_grants_one_rare_armor_and_authored_materials", additions.Length == 1 &&
            additions[0].DefinitionId == "item.serath_shroud" && additions[0].Rarity == ItemRarity.Rare &&
            claimed.Materials == beforeClaim.Materials + 25 && claimed.OperationReceipts.ContainsKey("campaign.crypt.testament"));
        Check("crypt_testament_keeps_floor_loot_and_consumes_world_marker", JsonData.Hash(Session.Combat.Capture().Loot) == cryptLoot &&
            Session.Campaign.Capture().Campaign.CompletedExploration.Contains(CampaignRuntimeSession.CryptEvent) &&
            _stage.GetInteractionVisual("opening.crypt.treasure") is null && !Session.Interactions.Any(i => i.ActionId == "opening.crypt.treasure"));
        CheckStaleTestament("after_claim");
        await Capture("mouse-crypt-testament-claimed.png");

        await ClickOpeningMarker("opening.crypt.return", "crypt_return", "campaign.road");
        Check("crypt_return_restores_exact_road_layout_and_floor_loot", Session.Campaign.ActiveEncounterId == "campaign.road" &&
            Session.EncounterCleared && JsonData.Hash(Session.Room) == roadRoom && JsonData.Hash(Session.Combat.Capture().Loot) == roadLoot &&
            roadLootIds.All(id => Descendants(_sandbox).OfType<LootVisual>().Any(visual => visual.Name == "Loot" + id && visual.IsVisibleInTree())));
        Check("crypt_return_clears_old_room_actions", NoIntent && _sandbox.EnvironmentStyle == "road" &&
            _stage.GetInteractionVisual("opening.crypt.return") is null && _stage.GetInteractionVisual("opening.crypt.treasure") is null);
        await Capture("mouse-road-loot-after-crypt.png");

        await ClickOpeningMarker("opening.crypt.enter", "crypt_revisit", CampaignRuntimeSession.CryptEncounter);
        Check("crypt_revisit_keeps_clearance_loot_and_consumed_treasure", Session.EncounterCleared &&
            !Session.Combat.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0) &&
            JsonData.Hash(Session.Combat.Capture().Loot) == cryptLoot && !Session.Interactions.Any(i => i.ActionId == "opening.crypt.treasure") &&
            Session.Capture().Campaign.Production.Progression.Character.Items.Any(i => i.Id == additions[0].Id));
        CheckStaleTestament("after_revisit");
        Check("crypt_testament_claim_event_occurs_exactly_once", Field<List<string>>(_director, "_worldEvents").Count(e => e == "CryptTestamentClaimed") == 1);
        await ClickOpeningMarker("opening.crypt.return", "crypt_second_return", "campaign.road");
        Check("crypt_round_trip_preserves_unclaimed_rewards_and_materials", JsonData.Hash(Session.Combat.Capture().Loot) == roadLoot &&
            Session.Capture().Campaign.Production.Progression.Character.Materials == claimed.Materials && NoIntent);
    }

    private async Task ClickOpeningMarker(string id, string label, string expectedEncounter)
    {
        await CloseJourney();
        var target = Session.Interactions.Single(i => i.ActionId == id);
        bool distant = CorePosition.DistanceSquared(Player, target.Position) > (long)target.Range * target.Range;
        string beforeHover = Session.StateHash;
        var point = await FindInteractionPoint(id);
        Check(label + "_hover_selects_authored_marker_without_mutation", _sandbox.HoveredWorldActionId == id && Session.StateHash == beforeHover);
        int commands = _commands.Count;
        await Click(point);
        if (distant) Check(label + "_mouse_click_starts_specific_approach", _sandbox.PendingWorldActionId == id && _sandbox.ClickMoveDestination is not null);
        await WalkUntilStopped();
        Check(label + "_mouse_arrival_executes_selected_action", Session.Campaign.ActiveEncounterId == expectedEncounter && NoIntent);
        Check(label + "_mouse_approach_never_casts_or_collects_floor_loot", !_commands.Skip(commands).Any(c => c.Kind is CombatCommandKind.Cast or CombatCommandKind.Pickup));
    }

    private void InspectCrypt(string style)
    {
        var architecture = Descendants(_stage).OfType<Node3D>().Single(n => n.Name == "GreyMarchArchitecture" && n.IsVisibleInTree());
        var targets = Session.Interactions.Select(i => new WorldInteractionTarget(i.ActionId, i.Name, i.Position, i.Range, _stage.GetInteractionVisual(i.ActionId))).ToArray();
        _openingLayouts.Add(OpeningEnvironmentChecks.Inspect(_sandbox, architecture, Session.Room, style, targets, Check));
    }

    private void CheckStaleTestament(string suffix)
    {
        string hash = Session.StateHash;
        Check("crypt_" + suffix + "_stale_ui_action_cannot_start", !_sandbox.RequestWorldInteraction("opening.crypt.treasure") && NoIntent && Session.StateHash == hash);
        var result = Session.Execute(new(EndgameRuntimeAction.Campaign,
            Campaign: new(CampaignRuntimeAction.InteractOpening, Id: "opening.crypt.treasure")));
        Check("crypt_" + suffix + "_stale_core_claim_is_rejected_without_mutation", !result.Success && Session.StateHash == hash);
        // Rejected transactional commands may replace the restored Core session object.
        // Project that current authoritative object before the next viewport input.
        Refresh();
    }
}
