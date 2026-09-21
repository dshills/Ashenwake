using System.Reflection;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Shipping director, viewport picks, fixed input ticks and real campaign rewards exercise mouse actions.</summary>
public partial class MouseActionsSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private readonly List<CombatCommand> _commands = [];
    private readonly List<CombatEvent> _events = [];
    private readonly List<EndgameRuntimeReplay> _replays = [];
    private readonly List<string> _groundCheckpoints = [];
    private readonly List<string> _cameraPicks = [];
    private int _cameraZoomInputs;
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private CampaignStage _stage = null!;
    private CampaignHud _hud = null!;
    private Camera3D _camera = null!;
    private string _output = "";
    private bool _writeReport;
    private int _campaignCommands;
    private EndgameRuntimeSession Session => Field<EndgameRuntimeSession>(_director, "_session");
    private CorePosition Player => Session.Combat.View.Actors.Single(a => a.Id == 1).Position;
    private int DialogueCount => Field<List<string>>(_director, "_worldEvents").Count(e => e.StartsWith("Dialogue:mara.", StringComparison.Ordinal));

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--mouse-actions-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Mouse actions smoke requires --mouse-actions-smoke --discipline=Vanguard --output=<fresh-artifact-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = new(1280, 800); GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director); _director.SetProcess(false);
            _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false); _sandbox.AutomaticStep = false;
            _stage = Field<CampaignStage>(_director, "_stage"); _hud = Field<CampaignHud>(_director, "_campaignHud");
            _camera = _sandbox.GetChildren().OfType<Camera3D>().Single();
            var advance = _sandbox.AdvanceOverride!;
            _sandbox.AdvanceOverride = commands => { _commands.AddRange(commands); var events = advance(commands); _events.AddRange(events); return events; };
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            await Frames(8);
            await CloseJourney();
            if (_sandbox.IsPaused) await ClickButton("Resume playing");
            Ticks(2);
            await InventoryRouting();
            await Ground(new(-5000, 5000)); await WalkUntilStopped();
            Check("distant_hub_checkpoint_is_reached_by_real_mouse_movement", CorePosition.DistanceSquared(Player, Session.Interactions.Single(i => i.ActionId == "npc.mara").Position) > 6000L * 6000);
            await KeyPress(Key.F5);
            Check("checkpoint_saved_through_shipping_save_handler", System.IO.File.Exists(Path.Combine(_output, "endgame.save.json")));
            await MaraInteraction();
            await Interruptions();
            await CampaignLoot();
            await OpeningExploration();
            RecordReplay();
            Check("all_branch_command_replays_match", _replays.Count >= 3 && _replays.All(replay => EndgameRuntimeReplayRunner.Run(
                Field<string>(_director, "_combatJson"), Field<AdventureContent>(_director, "_adventure"), Field<ProgressionContent>(_director, "_progression"),
                Field<CampaignContent>(_director, "_campaign"), Field<EndgameContent>(_director, "_endgame"), replay).Success));
            System.IO.File.WriteAllText(Path.Combine(_output, "mouse-action-replays.json"), JsonData.Write(_replays));
            await MechanismActions();
            Finish(true, "");
        }
        catch (Exception ex)
        {
            try { await Capture("mouse-actions-failure.png"); } catch (Exception captureError) { GD.PushError(captureError.Message); }
            GD.PushError(ex.ToString()); Finish(false, ex.Message);
        }
    }

    private async Task InventoryRouting()
    {
        var character = Field<ProductionHud>(_director, "_character");
        var panel = Field<PanelContainer>(character, "_panel");
        var loadout = Descendants(character).OfType<GearLoadout>().Single();
        string hash = Session.StateHash;
        int history = Session.CaptureReplay().Frames.Length;
        var equipment = Session.Capture().Campaign.Production.Progression.Character.Equipment.ToArray();
        long[] owned = Session.Capture().Campaign.Production.Progression.Character.Items.Select(item => item.Id).ToArray();

        await KeyPress(Key.I);
        Check("shipping_inventory_key_opens_gear_grid", panel.IsVisibleInTree() && Field<string>(character, "_tab") == "Gear" && loadout.IsVisibleInTree() &&
            Descendants(loadout).OfType<GearDragCard>().Count(card => card.Name.ToString().StartsWith("GearEquipment", StringComparison.Ordinal)) == Enum.GetValues<EquipmentSlot>().Length);
        await KeyPress(Key.I);
        Check("shipping_second_inventory_key_closes_gear", !panel.IsVisibleInTree() && !loadout.IsVisibleInTree());

        await KeyPress(Key.C);
        var characterTab = Descendants(character).OfType<Button>().Single(button => button.Text == "Character" && button.IsVisibleInTree());
        await Click(characterTab.GetGlobalRect().GetCenter());
        Check("shipping_character_tab_checkpoint_is_open", panel.IsVisibleInTree() && Field<string>(character, "_tab") == "Character" && !loadout.IsVisibleInTree());
        await KeyPress(Key.I);
        Check("shipping_inventory_key_switches_character_tab_to_gear", panel.IsVisibleInTree() && Field<string>(character, "_tab") == "Gear" && loadout.IsVisibleInTree());
        await KeyPress(Key.I);
        var state = Session.Capture().Campaign.Production.Progression.Character;
        Check("shipping_inventory_routing_preserves_owned_equipment_and_history", !panel.IsVisibleInTree() && Session.StateHash == hash &&
            Session.CaptureReplay().Frames.Length == history && state.Equipment.SequenceEqual(equipment) && state.Items.Select(item => item.Id).SequenceEqual(owned));
    }

    private async Task MaraInteraction()
    {
        var mara = Session.Interactions.Single(i => i.ActionId == "npc.mara");
        var point = await FindInteractionPoint("npc.mara");
        string beforeHover = Session.StateHash;
        await Hover(point);
        Check("hover_identifies_mara_without_mutating_gameplay", _sandbox.HoveredWorldActionId == "npc.mara" && Session.StateHash == beforeHover);
        await Capture("mouse-mara-hover.png");
        int commands = _commands.Count, events = _events.Count, dialogue = DialogueCount;
        await Click(point);
        Check("distant_npc_click_sets_specific_pending_action", _sandbox.PendingWorldActionId == "npc.mara" && _sandbox.ClickMoveDestination is not null && _sandbox.MouseDestinationVisible);
        Check("distant_npc_does_not_interact_early", DialogueCount == dialogue);
        await Capture("mouse-mara-approach.png");
        await WalkUntilStopped(); await Frames();
        Check("npc_approach_finishes_inside_authoritative_range", CorePosition.DistanceSquared(Player, mara.Position) <= (long)mara.Range * mara.Range);
        Check("npc_approach_opens_actual_mara_conversation", DialogueCount == dialogue + 1 && Descendants(_hud).OfType<Label>().Any(l => l.IsVisibleInTree() && l.Text.StartsWith("Mara: The wound will hold.", StringComparison.Ordinal)));
        Check("npc_arrival_consumes_pending_action_and_marker", _sandbox.PendingWorldActionId is null && _sandbox.ClickMoveDestination is null && !_sandbox.MouseDestinationVisible);
        Check("npc_click_never_casts_primary", !_commands.Skip(commands).Any(c => c.Kind == CombatCommandKind.Cast) && !_events.Skip(events).Any(e => e.Kind == "AbilityStarted" && e.ActorId == 1));
        await Capture("mouse-mara-conversation.png");
        await CloseJourney(); var stopped = Player; Ticks(24);
        Check("completed_npc_action_never_repeats_on_later_ticks", DialogueCount == dialogue + 1 && Player == stopped);
        RecordReplay();
    }

    private async Task Interruptions()
    {
        await ResetHub(); await BeginMara();
        int dialogue = DialogueCount; await KeyPress(Key.X); Ticks(24);
        Check("stop_cancels_interaction_without_firing", NoIntent && DialogueCount == dialogue);

        await ResetHub(); await BeginMara(); dialogue = DialogueCount;
        Input.ActionPress("aw_down"); Ticks(4); Input.ActionRelease("aw_down"); Ticks(24);
        Check("keyboard_movement_cancels_interaction_without_firing", NoIntent && DialogueCount == dialogue);

        await ResetHub(); await BeginMara(); dialogue = DialogueCount;
        await ClickButton("Settings [Esc]"); var paused = Player; Ticks(16);
        Check("settings_cancels_interaction_and_pauses_movement", _sandbox.IsPaused && NoIntent && Player == paused && DialogueCount == dialogue);
        await KeyPress(Key.Escape); Ticks(12);
        Check("closing_settings_does_not_resume_old_interaction", !_sandbox.IsPaused && NoIntent && Player == paused && DialogueCount == dialogue);

        await ResetHub(); await BeginMara(); dialogue = DialogueCount;
        await KeyPress(Key.P); paused = Player; Ticks(12);
        Check("manual_pause_cancels_interaction", _sandbox.IsPaused && NoIntent && Player == paused && DialogueCount == dialogue);
        await KeyPress(Key.P); Ticks(12);
        Check("manual_resume_does_not_resume_interaction", !_sandbox.IsPaused && NoIntent && DialogueCount == dialogue);

        await ResetHub(); await BeginMara(); dialogue = DialogueCount;
        await KeyPress(Key.J); Ticks(12);
        Check("journey_menu_cancels_interaction", NoIntent && DialogueCount == dialogue);
        await CloseJourney(); Ticks(12);
        Check("closing_journey_does_not_resume_interaction", NoIntent && DialogueCount == dialogue);

        await ResetHub(); await BeginMara(); dialogue = DialogueCount; int commands = _commands.Count;
        await Click(_camera.UnprojectPosition(World(new CorePosition(Player.X - 500, Player.Z))), shift: true); Ticks(12);
        Check("explicit_attack_cancels_interaction_and_keeps_attack_control", NoIntent && DialogueCount == dialogue && _commands.Skip(commands).Any(c => c.Kind == CombatCommandKind.Cast));

        await ResetHub(); var saved = Player; await BeginMara(); Ticks(4); dialogue = DialogueCount;
        await KeyPress(Key.F9); await CloseJourney(); Ticks(12);
        Check("load_cancels_interaction_and_restores_saved_position", NoIntent && Player == saved && DialogueCount == dialogue);

        await ResetHub(); await BeginMara(); dialogue = DialogueCount;
        _sandbox.Notification((int)NotificationApplicationFocusOut); Ticks(8);
        Check("focus_loss_cancels_interaction", _sandbox.IsPaused && NoIntent && DialogueCount == dialogue);
        await ClickButton("Resume playing"); Ticks(12);
        Check("focus_resume_does_not_resume_interaction", !_sandbox.IsPaused && NoIntent && DialogueCount == dialogue);

        await ResetHub(); await BeginMara(); dialogue = DialogueCount;
        _sandbox.SetWorldInteractions([], _ => throw new InvalidDataException("A vanished target must never activate."));
        Ticks(); Check("vanished_target_cancels_pending_action", NoIntent);
        Ticks(20);
        Check("restored_projection_does_not_revive_vanished_intent", NoIntent && DialogueCount == dialogue);

        await ResetHub(); await BeginMara(); dialogue = DialogueCount;
        Execute(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.EnterAct, Act: 1)));
        Check("scene_transition_cancels_hub_interaction", !Session.InHub && NoIntent && _sandbox.HoveredWorldActionId is null && DialogueCount == dialogue);
        RecordReplay(); await ResetHub();
    }

    private async Task CampaignLoot()
    {
        // The ordinary campaign policy prepares the same starting build and enters Act I.
        // Kills come from normal combat commands; suppress pickup so the viewport can select its own reward.
        for (int i = 0; Session.InHub && i < 600; i++) Execute(new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(Session.Campaign)), refresh: false);
        Refresh(); await Frames();
        Check("loot_route_enters_real_act_one", !Session.InHub && Session.Campaign.Capture().Campaign.CurrentAct == 1);
        await CombatReadability();
        for (int i = 0; !Session.EncounterCleared && i < 6000; i++)
        {
            var commands = CampaignCombatSmoke.Commands(Session.Combat.View, Session.Room).Where(c => c.Kind != CombatCommandKind.Pickup).ToArray();
            Execute(new(EndgameRuntimeAction.Tick, Commands: commands), refresh: false);
            if (i % 120 == 0) { Refresh(); await Frames(); }
        }
        Execute(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)])); await CloseJourney(); await Frames();
        Check("real_campaign_battle_creates_multiple_uncollected_drops", Session.EncounterCleared && Session.Combat.View.Loot.Count >= 2 && Session.Campaign.Capture().Campaign.Deaths == 0);
        var rewardIds = Session.Combat.View.Loot.Select(l => l.Id).ToHashSet();
        await MoveAwayFromLoot();
        var picked = await FindLootPoint(rewardIds);
        var selected = Session.Combat.View.Loot.Single(l => l.Id == picked.Id);
        string hoverHash = Session.StateHash;
        await Hover(picked.Point);
        Check("loot_hover_identifies_actual_drop_without_mutation", _sandbox.HoveredLootId == picked.Id && Session.StateHash == hoverHash);
        Check("hover_highlights_selected_loot_visual", Descendants(_sandbox).OfType<LootVisual>().Single(v => v.Name == "Loot" + picked.Id).GetNode<Node3D>("LootSelection").Visible);
        await Capture("mouse-loot-hover.png");
        int commandsBefore = _commands.Count; int inventoryBefore = Session.Combat.View.Inventory.Count;
        await Click(picked.Point);
        Check("distant_loot_click_starts_approach_without_early_pickup", _sandbox.PendingWorldActionId is not null && _sandbox.ClickMoveDestination is not null && Session.Combat.View.Loot.Any(l => l.Id == selected.Id));
        await Capture("mouse-loot-approach.png");
        await WalkUntilStopped();
        Check("loot_approach_collects_only_selected_drop", Session.Combat.View.Loot.Select(l => l.Id).ToHashSet().SetEquals(rewardIds.Where(id => id != selected.Id)) && Session.Combat.View.Inventory.Count == inventoryBefore + 1);
        Check("loot_pickup_occurs_in_authoritative_range", CorePosition.DistanceSquared(Player, selected.Position) <= (long)CombatSession.PickupRange * CombatSession.PickupRange);
        Check("loot_click_enqueues_one_selected_pickup_and_no_primary", _commands.Skip(commandsBefore).Count(c => c.Kind == CombatCommandKind.Pickup && c.ItemId == selected.Id) == 1 && !_commands.Skip(commandsBefore).Any(c => c.Kind == CombatCommandKind.Cast));
        Ticks(20);
        Check("completed_loot_action_stays_consumed", NoIntent && Session.Combat.View.Loot.Count == rewardIds.Count - 1);
        await Capture("mouse-loot-collected.png");
        await WayForwardAction();

        await MoveAwayFromLoot();
        var remaining = Session.Combat.View.Loot.Where(l => l.Item.Rarity != "Godwrought").Select(l => l.Id).ToHashSet();
        Check("real_reward_available_for_visibility_filter_check", remaining.Count > 0);
        picked = await FindLootPoint(remaining); await Click(picked.Point);
        SetLootRarity(5); Ticks();
        Check("hiding_selected_drop_cancels_pending_pickup", NoIntent && Session.Combat.View.Loot.Any(l => l.Id == picked.Id));
        await Hover(picked.Point);
        Check("filtered_drop_cannot_be_hovered_or_picked", _sandbox.HoveredLootId == 0 && !Descendants(_sandbox).OfType<LootVisual>().Any(v => v.Name == "Loot" + picked.Id));
        Input.ActionPress("aw_showloot"); Ticks();
        picked = await FindLootPoint(remaining);
        Check("held_show_loot_reveals_filtered_drop_for_mouse_pick", _sandbox.HoveredLootId == picked.Id);
        var before = Session.Combat.View.Loot.Select(l => l.Id).ToHashSet(); commandsBefore = _commands.Count;
        await Click(picked.Point); await WalkUntilStopped();
        Check("show_loot_pick_collects_only_selected_filtered_drop", Session.Combat.View.Loot.Select(l => l.Id).ToHashSet().SetEquals(before.Where(id => id != picked.Id)) &&
            _commands.Skip(commandsBefore).Count(c => c.Kind == CombatCommandKind.Pickup && c.ItemId == picked.Id) == 1);
        Input.ActionRelease("aw_showloot"); Ticks(); SetLootRarity(0);
        Check("loot_mouse_actions_preserve_campaign_survival", Session.Campaign.Capture().Campaign.Deaths == 0 && Session.Combat.View.Actors.Single(a => a.Id == 1).Health > 0);
    }

    private bool NoIntent => _sandbox.PendingWorldActionId is null && _sandbox.ClickMoveDestination is null && !_sandbox.MouseDestinationVisible;
    private async Task BeginMara()
    {
        var point = await FindInteractionPoint("npc.mara"); await Click(point); Ticks(2);
        if (_sandbox.PendingWorldActionId != "npc.mara") throw new InvalidDataException("Distant Mara request failed to start.");
    }
    private async Task ResetHub()
    {
        RecordReplay(); await KeyPress(Key.F9); await CloseJourney(); Ticks(2);
        if (!Session.InHub || _sandbox.IsPaused || !NoIntent) throw new InvalidDataException("Saved hub branch did not restore cleanly.");
    }
    private async Task MoveAwayFromLoot()
    {
        await ReachVisibleGroundCheckpoint(p => Session.Combat.View.Loot.Min(l => CorePosition.DistanceSquared(p, l.Position)),
            CombatSession.PickupRange, "loot_" + Session.Combat.View.Loot.Count);
        Check("loot_checkpoint_stands_outside_pickup_range_" + Session.Combat.View.Loot.Count, Session.Combat.View.Loot.All(l => CorePosition.DistanceSquared(Player, l.Position) > (long)CombatSession.PickupRange * CombatSession.PickupRange));
    }
    private async Task ReachVisibleGroundCheckpoint(Func<CorePosition, long> separationSquared, int requiredRange, string label)
    {
        var room = Session.Room;
        var space = new SpatialWorld(room);
        var planner = new ClickMovePlanner(room);
        var occupied = Session.Combat.View.Actors.Where(a => a.Id != 1 && a.Health > 0).Select(a => a.Position).ToArray();
        int margin = requiredRange + ClickMovePlanner.ArrivalTolerance + 300;
        var candidates = (from x in Enumerable.Range(-3, 7)
                          from z in Enumerable.Range(-3, 7)
                          select new CorePosition(x * (room.HalfWidth - 1400) / 3, z * (room.HalfDepth - 1400) / 3))
            .Where(p => separationSquared(p) > (long)margin * margin && space.CanOccupy(p, CombatSession.ActorRadius) &&
                CorePosition.DistanceSquared(Player, p) > 1000L * 1000)
            // A checkpoint needs a real approach, not the farthest corner. Prefer the closest
            // safe separation so following the character does not push every target behind the HUD.
            .OrderBy(separationSquared).ThenBy(p => CorePosition.DistanceSquared(Player, p)).ThenBy(p => p.X).ThenBy(p => p.Z).ToArray();
        foreach (var target in candidates)
        {
            var point = _camera.UnprojectPosition(World(target));
            if (!GetViewport().GetVisibleRect().Grow(-8).HasPoint(point) || !planner.TrySetDestination(Player, target, occupied)) continue;
            await Hover(point);
            var hovered = GetViewport().GuiGetHoveredControl();
            if (hovered is not null || _sandbox.HoveredWorldActionId is not null || _sandbox.HoveredLootId != 0)
            {
                if (_groundCheckpoints.Count < 24) _groundCheckpoints.Add($"{label}: skipped {target} at {point}; gui={hovered?.GetPath()}; action={_sandbox.HoveredWorldActionId}; loot={_sandbox.HoveredLootId}");
                continue;
            }
            // The HUD may cover a perfectly legal floor point. Select through native input only
            // after checking actual UI occlusion; never let a consumed click pass as completed movement.
            await Click(point);
            Check(label + "_checkpoint_native_ground_click_starts_route", _sandbox.PendingWorldActionId is null &&
                _sandbox.ClickMoveDestination is { } destination && CorePosition.DistanceSquared(destination, target) <= 4);
            await WalkUntilStopped();
            Check(label + "_checkpoint_reaches_clicked_ground", CorePosition.DistanceSquared(Player, target) <=
                (long)(ClickMovePlanner.ArrivalTolerance + 2) * (ClickMovePlanner.ArrivalTolerance + 2));
            _groundCheckpoints.Add($"{label}: reached {Player}; clicked {target} at {point}");
            return;
        }
        throw new InvalidDataException("No reachable, unoccluded native ground checkpoint for " + label + ". " + string.Join("; ", _groundCheckpoints));
    }
    private async Task WalkUntilStopped()
    {
        for (int i = 0; i < 720 && (_sandbox.ClickMoveDestination is not null || _sandbox.PendingWorldActionId is not null); i++)
        {
            Ticks();
            if (Session.Room.Obstacles.Any(o => Player.X >= o.MinX && Player.X <= o.MaxX && Player.Z >= o.MinZ && Player.Z <= o.MaxZ))
                throw new InvalidDataException("Mouse action movement entered an authoritative obstacle.");
            if (i % 30 == 0) await Frames();
        }
        if (_sandbox.ClickMoveDestination is not null || _sandbox.PendingWorldActionId is not null) throw new InvalidDataException("Mouse action exceeded its bounded approach time.");
        Ticks(2); await Frames();
    }
    private async Task<Vector2> FindInteractionPoint(string id)
    {
        var visual = _stage.GetInteractionVisual(id) ?? Field<IReadOnlyList<WorldInteractionTarget>>(_sandbox, "_worldInteractions").FirstOrDefault(t => t.Id == id)?.Visual
            ?? throw new InvalidDataException("Missing interaction visual: " + id);
        string evidence = "";
        for (int zoom = 0; zoom < 20; zoom++)
        {
            foreach (var point in PickPoints(visual))
            {
                await Hover(point);
                if (_sandbox.HoveredWorldActionId == id) return point;
                if (evidence.Length == 0) evidence = $"requested={point}; viewport={GetViewport().GetMousePosition()}; gui={GetViewport().GuiGetHoveredControl()?.GetPath()}; paused={_sandbox.IsPaused}; targets={string.Join(',', Field<IReadOnlyList<WorldInteractionTarget>>(_sandbox, "_worldInteractions").Select(t => t.Id))}";
            }
            if (zoom == 19 || !await ZoomOutThroughWorldInput("interaction:" + id)) break;
        }
        throw new InvalidDataException("No visible body point selected interaction " + id + ": " + evidence);
    }
    private async Task<(long Id, Vector2 Point)> FindLootPoint(IReadOnlySet<long> ids)
    {
        for (int zoom = 0; zoom < 20; zoom++)
        {
            foreach (var visual in Descendants(_sandbox).OfType<LootVisual>().Where(v => v.IsVisibleInTree()).ToArray())
            {
                bool described = false;
                foreach (var point in PickPoints(visual))
                {
                    await Hover(point);
                    if (ids.Contains(_sandbox.HoveredLootId)) return (_sandbox.HoveredLootId, point);
                    if (!described && _cameraPicks.Count < 64)
                    {
                        _cameraPicks.Add($"loot:{visual.Name}; camera={_camera.Size}; point={point}; gui={GetViewport().GuiGetHoveredControl()?.GetPath()}; paused={_sandbox.IsPaused}");
                        described = true;
                    }
                }
            }
            if (zoom == 19 || !await ZoomOutThroughWorldInput("loot")) break;
        }
        throw new InvalidDataException("No actual visible reward could be picked after native zoom. " + string.Join("; ", _cameraPicks));
    }
    private async Task<bool> ZoomOutThroughWorldInput(string reason)
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        foreach (var fraction in new[] { new Vector2(.5f, .5f), new(.7f, .5f), new(.5f, .6f), new(.8f, .55f), new(.25f, .6f) })
        {
            Vector2 point = viewport * fraction;
            await Hover(point);
            if (GetViewport().GuiGetHoveredControl() is not null) continue;
            float before = _camera.Size;
            string hash = _sandbox.Session.StateHash;
            var destination = _sandbox.ClickMoveDestination;
            string? action = _sandbox.PendingWorldActionId;
            foreach (bool pressed in new[] { true, false })
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Position = point, Pressed = pressed }, true);
            await Frames(); _cameraZoomInputs++;
            if (_sandbox.Session.StateHash != hash || _sandbox.ClickMoveDestination != destination || _sandbox.PendingWorldActionId != action)
                throw new InvalidDataException("A diagnostic camera wheel input changed gameplay or pending movement.");
            if (_cameraPicks.Count < 64) _cameraPicks.Add($"{reason}: native wheel at {point}; camera {before} -> {_camera.Size}");
            return _camera.Size > before;
        }
        throw new InvalidDataException("No unoccluded world point accepted camera input for " + reason);
    }
    private IEnumerable<Vector2> PickPoints(Node3D visual)
    {
        yield return _camera.UnprojectPosition(visual.GlobalPosition + Vector3.Up * (visual is LootVisual ? .2f : 1f));
        foreach (var mesh in Descendants(visual).OfType<MeshInstance3D>().Where(m => m.IsVisibleInTree() && m.Mesh is not null))
        {
            var bounds = mesh.Mesh.GetAabb();
            yield return _camera.UnprojectPosition(mesh.GlobalTransform * (bounds.Position + bounds.Size * .5f));
        }
    }
    private void Execute(EndgameRuntimeCommand command, bool refresh = true)
    {
        var result = Session.Execute(command); _campaignCommands++;
        if (!result.Success) throw new InvalidDataException("Campaign setup command failed: " + result.Reason);
        Invoke(_director, "Observe", result);
        if (refresh) Refresh();
    }
    private void Refresh()
    { _sandbox.AdoptSession(Session.Combat); Invoke(_director, "Refresh"); }
    private void SetLootRarity(int index)
    {
        // Drive the real settings widget's selection signal without opening a modal, so this
        // isolates filter invalidation from the independently tested menu interruption path.
        var filter = Descendants(_sandbox).OfType<OptionButton>().Single(n => n.Name == "LootRarityFilter");
        filter.Select(index); filter.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
    }
    private void RecordReplay()
    { if (_director is not null && Session.CaptureReplay().Frames.Length > 0) _replays.Add(Session.CaptureReplay()); }
    private void Ticks(int count = 1)
    { for (int i = 0; i < count; i++) _sandbox._Process(FixedStepClock.SecondsPerTick); }
    private static T Field<T>(object owner, string name) => (T)(owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) ?? throw new MissingFieldException(name));
    private static void Invoke(object owner, string name, params object[] args)
        => (owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(name)).Invoke(owner, args);
    private static Vector3 World(CorePosition point) => new(point.X * .001f, 0, point.Z * .001f);
    private Task Ground(CorePosition point) => Click(_camera.UnprojectPosition(World(point)));
    private async Task Hover(Vector2 position)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = position }, true);
        Invoke(_sandbox, "UpdateWorldHover", .1d);
        await Frames();
    }
    private async Task Click(Vector2 position, bool shift = false)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = position }, true);
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = position, Pressed = pressed, ShiftPressed = shift }, true);
        await Frames();
    }
    private async Task KeyPress(Key key)
    {
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
        await Frames();
    }
    private async Task ClickButton(string text)
    {
        var button = Descendants(_director).OfType<Button>().Single(b => b.Text == text && b.IsVisibleInTree());
        await Click(button.GetGlobalRect().GetCenter());
    }
    private async Task CloseJourney()
    {
        var close = Descendants(_hud).OfType<Button>().SingleOrDefault(b => b.Text == "Close" && b.IsVisibleInTree());
        if (close is not null) await Click(close.GetGlobalRect().GetCenter());
    }
    private async Task Frames(int count = 2)
    { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
    private async Task Capture(string filename)
    {
        if (!_writeReport || !OS.GetCmdlineUserArgs().Contains("--capture-mouse-actions") || DisplayServer.GetName() == "headless") return;
        await Frames(3); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + filename, image.GetWidth() > 0 && image.SavePng(Path.Combine(_output, filename)) == Error.Ok);
        _captures.Add(filename);
    }
    private void Check(string name, bool passed)
    { _checks[name] = passed; if (!passed) throw new InvalidDataException("Mouse actions check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "MouseActionsClientSmokePassed" : "MouseActionsClientSmokeFailed",
            passed,
            checks = _checks,
            captures = _captures,
            inputCommands = _commands.Count,
            campaignSetupCommands = _campaignCommands,
            replayCount = _replays.Count,
            groundCheckpoints = _groundCheckpoints,
            cameraZoomInputs = _cameraZoomInputs,
            cameraPicks = _cameraPicks,
            openingLayouts = _openingLayouts,
            error,
            scope = "The shipping EndgameDirector receives actual viewport NPC, loot, crypt passage and testament clicks, UI/key interruption and save/load input; its Sandbox is stepped at the real fixed interval. Actual campaign commands prepare a build and earn the drops. The opening side-room route fights the authored crypt, claims its rare testament once, returns with road loot retained, and revisits without respawning enemies or rewards. Native structural checks inspect crypt scenery, floor, obstacles and clickable targets. The real filter widget signal isolates disappearance from modal cancellation. Public presentation invalidation tests vanished and stale targets; a rejected Core command verifies spent-treasure protection. No fabricated combat rewards or progression; branch command replays must match. An isolated authored Orrun hunt phase uses the earned character, real mechanism availability and a separately verified combat replay; it does not claim a full endgame unlock journey. Full road-to-Bell-Saint objectives remain covered by the journey diagnostic."
        };
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "mouse-actions-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report));
        if (_director is not null && !_director.IsInsideTree()) _director.Free();
        GetTree().Quit(passed ? 0 : 1);
    }
}
