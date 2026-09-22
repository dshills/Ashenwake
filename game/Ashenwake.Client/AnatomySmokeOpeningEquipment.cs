using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

public partial class AnatomySmoke
{
    private ProductionHud OpeningGear => Field<ProductionHud>(_director, "_character");
    private PermanentItem EarnedPyre => Session.Production.Capture().Progression.Character.Items.Single(i => i.DefinitionId == LegendaryEquipment.Pyre);
    private bool PyreEquipped => Session.Production.Capture().Progression.Character.Equipment.GetValueOrDefault(EquipmentSlot.Boots) == EarnedPyre.Id;
    private string OpeningGearLesson => VisibleLabels(OpeningGear).Single(l => l.Name == "OpeningGearLesson").Text;

    private async Task EarnOpeningEquipment()
    {
        Check("fresh_character_has_no_pyre_reward_or_reward_prompt", !Session.Production.Capture().Progression.Character.Items.Any(i => i.DefinitionId == LegendaryEquipment.Pyre) &&
            !_hud.NextStepLabel.Contains("Pyrebound", StringComparison.Ordinal));
        for (int i = 0; !(Session.Campaign.ActiveEncounterId == "campaign.road" && Session.EncounterCleared) && i < 9000; i++)
        {
            var command = Session.Campaign.ActiveEncounterId == "campaign.road"
                ? new EndgameRuntimeCommand(EndgameRuntimeAction.Tick, Commands: CampaignCombatSmoke.Commands(Session.Combat.View, Session.Room).Where(c => c.Kind != CombatCommandKind.Pickup).ToArray())
                : new EndgameRuntimeCommand(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(Session.Campaign));
            Execute(command, refresh: false);
            if (i % 120 == 0) { Refresh(); await Frames(); }
        }
        Execute(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)]));
        await CloseJourney();
        Check("road_is_really_cleared_and_pyre_is_still_a_ground_drop", Session.Campaign.ActiveEncounterId == "campaign.road" && Session.EncounterCleared &&
            Session.Combat.View.Loot.Count(l => l.Item.DefinitionId == LegendaryEquipment.Pyre) == 1 &&
            !Session.Production.Capture().Progression.Character.Items.Any(i => i.DefinitionId == LegendaryEquipment.Pyre));
        string hash = Session.StateHash;
        Check("ground_reward_prompt_requests_review", _hud.NextStepLabel == "Review Pyrebound Treads & the route");
        await ClickNamed("CampaignNextStep");
        string? passageName = Session.Interactions.FirstOrDefault(i => i.ActionId.StartsWith("opening.forward.", StringComparison.Ordinal))?.Name;
        Check("ground_reward_review_describes_power_without_granting_or_equipping", Session.StateHash == hash &&
            VisibleLabels(_hud).Any(l => l.Name == "OpeningPyrePower" && l.Text.Contains("moving dodge", StringComparison.Ordinal)) &&
            Session.Campaign.Capture().Campaign.CompletedEncounters.Contains("campaign.road") &&
            !Session.Campaign.Capture().Campaign.CompletedEncounters.Contains("campaign.monastery") && Session.Campaign.View.EncounterId == "campaign.monastery" &&
            Descendants(_hud).OfType<Button>().Any(b => b.IsVisibleInTree() && !b.Disabled &&
                (b.Text.StartsWith("Continue onward", StringComparison.Ordinal) || passageName is not null && b.Text == passageName)));
        await Capture("opening-pyre-ground-review.png");
        await CloseJourney();
        var drop = Session.Combat.View.Loot.Single(l => l.Item.DefinitionId == LegendaryEquipment.Pyre);
        await Click(_camera.UnprojectPosition(World(drop.Position) + Vector3.Up * .35f));
        await WalkUntilStopped();
        Check("native_ground_click_collects_exact_earned_pyre", !Session.Combat.View.Loot.Any(l => l.Id == drop.Id) && EarnedPyre.DefinitionId == LegendaryEquipment.Pyre && !PyreEquipped);
        Check("owned_reward_prompt_offers_inspection", _hud.NextStepLabel == "Inspect Pyrebound Treads");
        hash = Session.StateHash;
        await ClickNamed("CampaignNextStep");
        Check("inspect_boots_selects_boot_slot_and_preserves_gameplay", Session.StateHash == hash &&
            Field<EquipmentSlot>(OpeningGear, "_gearSlot") == EquipmentSlot.Boots && Field<long>(OpeningGear, "_gearItemId") == EarnedPyre.Id &&
            OpeningGearLesson.Contains("Wake of Embers", StringComparison.Ordinal) && OpeningGearLesson.Contains("Return to Torren", StringComparison.Ordinal));
        Check("equipment_remains_service_gated_away_from_town", NamedButton("EquipItem", OpeningGear).Disabled);
        await CheckOpeningGearLayout(new(1280, 800), "standard");
        await CheckOpeningGearLayout(new(1024, 720), "short");
        await CheckOpeningGearLayout(new(780, 720), "narrow");
        GetWindow().Size = new(1280, 800); GetWindow().ContentScaleSize = new(1280, 800); await Frames(6);
        await OpeningInspectRevealsFilteredReward();
        await Capture("opening-pyre-inspection-away.png");
        Check("road_has_other_ground_drops_for_departure_guard", Session.Combat.View.Loot.Count > 0);
        await ClickNamed("OpeningGearReturn", OpeningGear);
        var departure = Descendants(_hud).OfType<ConfirmationDialog>().Single(d => d.Name == "JourneyTravelConfirmation");
        Check("boot_return_uses_existing_loot_departure_confirmation", departure.Visible && !Session.InHub && Session.StateHash == hash);
        await ClickOpeningConfirmation(departure, departure.GetCancelButton());
        Check("cancel_boot_trip_preserves_reward_and_room", !departure.Visible && !Session.InHub && Session.StateHash == hash && !PyreEquipped);
        await CloseJourney(); await ClickNamed("CampaignNextStep"); await ClickNamed("OpeningGearReturn", OpeningGear);
        await ClickOpeningConfirmation(departure, departure.GetOkButton());
        Check("confirmed_boot_trip_reaches_hub_without_auto_equipping", Session.InHub && !PyreEquipped);
        await CloseJourney();
        await Click(_camera.UnprojectPosition(World(new(-5000, 5000)))); await WalkUntilStopped();
        var torren = Session.Interactions.Single(i => i.ActionId == "service.torren");
        Check("boot_visit_starts_outside_torren_range", CorePosition.DistanceSquared(Player, torren.Position) > (long)torren.Range * torren.Range);
        await ClickNamed("CampaignNextStep");
        Check("distant_hub_boots_explain_the_actual_service_route", OpeningGearLesson.Contains("Walk to Torren", StringComparison.Ordinal) && NamedButton("EquipItem", OpeningGear).Disabled);
        int services = WorldEvents.Count(e => e == "ServiceOpened:service.torren");
        await ClickNamed("OpeningGearVisitTorren", OpeningGear);
        Check("boot_visit_uses_mouse_approach_without_teleporting", _sandbox.PendingWorldActionId == "service.torren" && _sandbox.ClickMoveDestination is not null);
        await WalkUntilStopped();
        Check("torren_arrival_opens_the_real_equipment_service", CorePosition.DistanceSquared(Player, torren.Position) <= (long)torren.Range * torren.Range &&
            WorldEvents.Count(e => e == "ServiceOpened:service.torren") == services + 1 && OpeningGearLesson.Contains("onto Boots", StringComparison.Ordinal));
        await DragOpeningEquipment("GearInventoryItem" + EarnedPyre.Id, "GearEquipmentBoots", "equip");
        Check("native_drop_equips_the_owned_boots_and_updates_the_lesson", PyreEquipped && Session.Combat.ProgressionBuild.PyreTrail &&
            OpeningGearLesson.Contains("Try Dodge", StringComparison.Ordinal));
        await Capture("opening-pyre-equipped-at-torren.png");
        await DragOpeningEquipment("GearEquipmentBoots", "GearBackpack", "unequip");
        Check("native_unequip_restores_optional_upgrade_guidance", !PyreEquipped && !Session.Combat.ProgressionBuild.PyreTrail &&
            OpeningGearLesson.Contains("onto Boots", StringComparison.Ordinal));
        await DragOpeningEquipment("GearInventoryItem" + EarnedPyre.Id, "GearEquipmentBoots", "reequip");
        await ClickText("Close character");
        await CloseJourney();
        await Click(_camera.UnprojectPosition(World(new(-4500, 4000)))); await WalkUntilStopped();
        CorePosition before = Player;
        int cues = _sandbox.LegendaryTriggerCueCount;
        foreach (bool pressed in new[] { true, false })
        {
            using var input = (InputEventKey)InputMap.ActionGetEvents("aw_dodge").OfType<InputEventKey>().First().Duplicate();
            input.Pressed = pressed; GetViewport().PushInput(input, true);
        }
        Ticks(); await Frames();
        Check("native_moving_dodge_activates_the_equipped_reward", Player != before &&
            Session.Combat.View.Areas.Count(a => a.ContentId == "effect.pyre_trail") == 3 && _sandbox.LegendaryTriggerCueCount == cues + 1);
        await Capture("opening-pyre-moving-dodge.png");
        Ticks(65); await Frames();
        Check("earned_pyre_trial_finishes_without_persistent_hazards", Session.Combat.View.Areas.All(a => a.ContentId != "effect.pyre_trail") && PyreEquipped);
    }

    private async Task ClickOpeningConfirmation(ConfirmationDialog dialog, Button button)
    {
        Viewport viewport = button.GetViewport();
        Vector2 position = button.GetGlobalRect().GetCenter();
        bool cancel = button == dialog.GetCancelButton();
        if (dialog.IsEmbedded())
        {
            // The embedder supplies mouse entry and transforms coordinates into the dialog.
            viewport = dialog.GetParent().GetViewport(); position += dialog.Position;
        }
        else viewport.NotifyMouseEntered();
        int mouseEvents = 0, activations = 0;
        void OnGuiInput(InputEvent input) { if (input is InputEventMouseButton) mouseEvents++; }
        void OnPressed() => activations++;
        button.GuiInput += OnGuiInput; button.Pressed += OnPressed;
        try
        {
            viewport.PushInput(new InputEventMouseMotion { Position = position }, true); await Frames(1);
            foreach (bool pressed in new[] { true, false })
                viewport.PushInput(new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left,
                    Position = position,
                    Pressed = pressed,
                    ButtonMask = pressed ? MouseButtonMask.Left : 0
                }, true);
            await Frames();
            Check("opening_return_" + (cancel ? "cancel" : "confirm") + "_native_click_reaches_button", mouseEvents == 2 && activations == 1);
        }
        finally { button.GuiInput -= OnGuiInput; button.Pressed -= OnPressed; }
    }

    private async Task CheckOpeningGearLayout(Vector2I size, string context)
    {
        string hash = Session.StateHash;
        GetWindow().Size = size; GetWindow().ContentScaleSize = size; await Frames(8);
        var bounds = GetViewport().GetVisibleRect().Grow(1);
        var panel = Field<PanelContainer>(OpeningGear, "_panel");
        var details = Field<ScrollContainer>(OpeningGear, "_scroll");
        var rows = Field<VBoxContainer>(OpeningGear, "_rows");
        var close = Descendants(OpeningGear).OfType<Button>().Single(b => b.Text == "Close character");
        var boots = NamedButton("GearEquipmentBoots", OpeningGear);
        System.IO.File.WriteAllText(Path.Combine(_output, "opening-gear-layout-" + context + ".json"), JsonData.Write(new
        {
            width = size.X,
            height = size.Y,
            panel = panel.GetGlobalRect().ToString(),
            panelMinimum = panel.GetCombinedMinimumSize().ToString(),
            details = details.GetGlobalRect().ToString(),
            detailsMinimum = details.GetCombinedMinimumSize().ToString(),
            content = rows.GetGlobalRect().ToString(),
            close = close.GetGlobalRect().ToString(),
            boots = boots.GetGlobalRect().ToString()
        }));
        Check("opening_gear_panel_and_close_fit_" + context, bounds.Encloses(panel.GetGlobalRect()) && close.IsVisibleInTree() &&
            bounds.Encloses(close.GetGlobalRect()) && bounds.Encloses(boots.GetGlobalRect()));
        Check("opening_gear_details_scroll_inside_panel_" + context, bounds.Encloses(details.GetGlobalRect()) && rows.Size.Y > details.Size.Y &&
            details.GetVScrollBar().Visible && details.GetGlobalRect().End.Y <= close.GetGlobalRect().Position.Y && Session.StateHash == hash);
        await Capture("opening-gear-layout-" + context + ".png");
    }

    private async Task OpeningInspectRevealsFilteredReward()
    {
        var search = Descendants(OpeningGear).OfType<LineEdit>().Single(c => c.Name == "GearSearch");
        var type = Descendants(OpeningGear).OfType<OptionButton>().Single(c => c.Name == "GearTypeFilter");
        var rarity = Descendants(OpeningGear).OfType<OptionButton>().Single(c => c.Name == "GearRarityFilter");
        var sort = Descendants(OpeningGear).OfType<OptionButton>().Single(c => c.Name == "GearSort");
        string hash = Session.StateHash;
        int groundRarity = Field<int>(_sandbox, "_minimumLootRarity");
        bool groundCompatible = Field<bool>(_sandbox, "_compatibleLootOnly");
        // Set the real browser controls to a previously filtered state; their ordinary signals
        // update the actual projection. The explicit inspection below receives native input.
        type.Select(1); type.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        rarity.Select(1); rarity.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        sort.Select(2); sort.EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
        search.Text = "missing reward"; search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text); await Frames();
        var card = Descendants(OpeningGear).OfType<Control>().Single(c => c.Name == "GearInventoryItem" + EarnedPyre.Id);
        Check("opening_inspection_fixture_really_hides_owned_reward", !card.Visible && Session.StateHash == hash);
        await ClickText("Close character"); await ClickNamed("CampaignNextStep"); await Frames(3);
        var backpack = Descendants(OpeningGear).OfType<ScrollContainer>().Single(c => c.Name == "GearBackpackScroll");
        Check("explicit_reward_inspection_reveals_and_scrolls_hidden_owned_item", card.IsVisibleInTree() && VerticallyShown(backpack, card) &&
            search.Text.Length == 0 && type.Selected == 0 && rarity.Selected == 0 && sort.Selected == 2);
        Check("reward_inspection_preserves_saved_loot_filters_and_gameplay", Session.StateHash == hash &&
            Field<int>(_sandbox, "_minimumLootRarity") == groundRarity && Field<bool>(_sandbox, "_compatibleLootOnly") == groundCompatible &&
            Field<Label>(OpeningGear, "_notice").Text == "Inventory filters cleared to show Pyrebound Treads.");
    }

    private async Task DragOpeningEquipment(string from, string to, string context)
    {
        var source = Descendants(OpeningGear).OfType<Control>().Single(c => c.Name == from);
        var target = Descendants(OpeningGear).OfType<Control>().Single(c => c.Name == to);
        await Reveal(source);
        Vector2 start = source.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = start }, true);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = start, Pressed = true, ButtonMask = MouseButtonMask.Left }, true);
        await Frames(1);
        for (int i = 1; i <= 3 && !GetViewport().GuiIsDragging(); i++)
        {
            GetViewport().PushInput(new InputEventMouseMotion { Position = start + new Vector2(i * 12, 0), Relative = new(12, 0), ButtonMask = MouseButtonMask.Left }, true);
            await Frames(1);
        }
        string hash = Session.StateHash;
        Check("opening_gear_" + context + "_uses_native_paused_drag", GetViewport().GuiIsDragging() && _sandbox.IsPaused);
        Ticks(3);
        Check("opening_gear_" + context + "_drag_does_not_advance_combat", Session.StateHash == hash);
        Vector2 destination = target.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = destination, ButtonMask = MouseButtonMask.Left }, true); await Frames();
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = destination, Pressed = false, ButtonMask = 0 }, true); await Frames();
        Check("opening_gear_" + context + "_completes_native_drag", !GetViewport().GuiIsDragging());
    }
}
