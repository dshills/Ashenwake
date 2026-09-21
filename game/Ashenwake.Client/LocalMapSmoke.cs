using System.Reflection;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Exploration;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Native input through the shipping solo director verifies map visibility, navigation and persistence.</summary>
public partial class LocalMapSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private readonly List<CombatCommand> _commands = [];
    private readonly List<EndgameRuntimeReplay> _replays = [];
    private readonly List<string> _routes = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private CampaignHud _hud = null!;
    private LocalExplorationMap _map = null!;
    private string _output = "";
    private bool _writeReport;
    private int _setupCommands, _completedRoutes;
    private EndgameRuntimeSession Session => Field<EndgameRuntimeSession>(_director, "_session");
    private LocalMapView Map => Session.LocalMap ?? throw new InvalidDataException("The shipping map was not enabled.");
    private CorePosition Player => Session.Combat.View.Actors.Single(a => a.Id == 1).Position;
    private bool NoIntent => _sandbox.ClickMoveDestination is null && _sandbox.PendingWorldActionId is null && !_sandbox.MouseDestinationVisible;
    private string SavePath => Path.Combine(_output, "endgame.save.json");

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--local-map-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Local map smoke requires --local-map-smoke --discipline=Vanguard --output=<fresh-artifact-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = new(1280, 800); GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director); _director.SetProcess(false);
            _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false); _sandbox.AutomaticStep = false;
            _hud = Field<CampaignHud>(_director, "_campaignHud");
            _map = Descendants(_director).OfType<LocalExplorationMap>().Single();
            var advance = _sandbox.AdvanceOverride!;
            _sandbox.AdvanceOverride = commands => { _commands.AddRange(commands); return advance(commands); };
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            await Frames(8); await CloseJourney();
            if (_sandbox.IsPaused) await ClickButton("Resume playing");
            Ticks(2);
            Check("shipping_solo_minimap_is_visible", _map.IsVisibleInTree() && !_map.Expanded && _map.MapGlobalRect.Size.X > 0);
            Check("fresh_character_map_initializes_with_recorded_command", Map.SeenCells.Count > 0 && Session.CaptureReplay().Frames.Any(f => f.Command.Action == EndgameRuntimeAction.EnableExplorationMap));
            Check("minimap_projects_authoritative_room_and_player", _map.RoomId == Map.RoomId && _map.LayoutHash == Map.LayoutHash && _map.IsDiscovered(Player));
            await Capture("local-map-minimap.png");
            await SmallWindowLayout();
            LegacyMapBinding();
            await MapInputAndPause();
            await HubNavigation();
            await RoadExploration();
            await SaveAndLegacyLoad();
            await RoomTransition();
            RecordReplay();
            Check("all_native_branch_replays_match", _replays.Count >= 3 && _replays.All(VerifyReplay));
            System.IO.File.WriteAllText(Path.Combine(_output, "local-map-replays.json"), JsonData.Write(_replays));
            Finish(true, "");
        }
        catch (Exception ex)
        {
            try { await Capture("local-map-failure.png"); } catch (Exception captureError) { GD.PushError(captureError.Message); }
            GD.PushError(ex.ToString()); Finish(false, ex.Message);
        }
    }

    private async Task SmallWindowLayout()
    {
        GetWindow().Size = new(780, 720); GetWindow().ContentScaleSize = new(780, 720); await Frames(5);
        var viewport = GetViewport().GetVisibleRect();
        Check("small_window_minimap_stays_inside_viewport", viewport.Encloses(_map.GetGlobalRect()) && _map.MapGlobalRect.Size.X > 100);
        await Capture("local-map-minimap-780.png");
        await KeyPress(Key.M);
        Check("small_window_expanded_map_and_close_fit_viewport", _map.Expanded && viewport.Encloses(_map.GetGlobalRect()) &&
            Descendants(_map).OfType<Button>().Where(button => button.Name == "LocalMapClose").All(button => viewport.Encloses(button.GetGlobalRect())));
        await Capture("local-map-expanded-780.png"); await KeyPress(Key.Escape);
        GetWindow().Size = new(1280, 800); GetWindow().ContentScaleSize = new(1280, 800); await Frames(5);
    }

    private void LegacyMapBinding()
    {
        string path = Path.Combine(_output, "legacy-custom-m-settings.json");
        string original = JsonData.Write(new { reducedEffects = false, reducedShake = true, keys = new Dictionary<string, long> { ["inventory"] = (long)Key.M } });
        System.IO.File.WriteAllText(path, original);
        string hash = Session.StateHash;
        Dictionary<string, long> ReadKeys(string filename)
        {
            object preferences = (typeof(Sandbox).GetMethod("ReadValidatedPreferences", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException("ReadValidatedPreferences")).Invoke(_sandbox, [filename])!;
            return (Dictionary<string, long>)(preferences.GetType().GetProperty("Keys")?.GetValue(preferences) ?? throw new MissingMemberException("Preferences.Keys"));
        }
        var keys = ReadKeys(path);
        Check("legacy_custom_m_binding_is_preserved_with_nonconflicting_map_key", keys["inventory"] == (long)Key.M && keys["localmap"] != (long)Key.M &&
            keys.Values.Distinct().Count() == keys.Count && Enum.IsDefined((Key)keys["localmap"]));
        Check("legacy_key_upgrade_is_read_only_until_preferences_are_saved", System.IO.File.ReadAllText(path) == original && Session.StateHash == hash &&
            Field<Dictionary<string, Key>>(_sandbox, "_keys")["localmap"] == Key.M);
        string crowdedPath = Path.Combine(_output, "legacy-custom-many-keys-settings.json");
        var crowded = new Dictionary<string, long>
        {
            ["inventory"] = (long)Key.M,
            ["left"] = (long)Key.L,
            ["right"] = (long)Key.N,
            ["up"] = (long)Key.K,
            ["down"] = (long)Key.U,
            ["target"] = (long)Key.O,
            ["dodge"] = (long)Key.T,
            ["potion"] = (long)Key.Y,
            ["pickup"] = (long)Key.Z,
            ["stop"] = (long)Key.F7,
            ["reset"] = (long)Key.F8
        };
        System.IO.File.WriteAllText(crowdedPath, JsonData.Write(new { reducedEffects = false, reducedShake = false, keys = crowded }));
        keys = ReadKeys(crowdedPath);
        Check("legacy_map_key_fallback_handles_many_existing_custom_bindings", crowded.All(pair => keys[pair.Key] == pair.Value) && keys.Values.Distinct().Count() == keys.Count);
    }

    private async Task MapInputAndPause()
    {
        string hash = Session.StateHash; long tick = Session.Tick; int commands = _commands.Count;
        await KeyPress(Key.M);
        Check("m_opens_expanded_room_map", _map.Expanded && _map.IsVisibleInTree() && _map.MapGlobalRect.Size.X > 400);
        Check("expanded_map_pauses_solo_game", _sandbox.IsPaused);
        Ticks(15);
        Check("map_paused_ticks_preserve_authoritative_state", Session.StateHash == hash && Session.Tick == tick);
        await Capture("local-map-expanded.png");
        await KeyPress(Key.Tab); await KeyPress(Key.Tab, shift: true);
        Check("expanded_map_keeps_keyboard_focus_on_its_own_close_action", GetViewport().GuiGetFocusOwner()?.Name == "LocalMapClose");
        await KeyPress(Key.Enter);
        Check("keyboard_accept_closes_map_without_world_action", !_map.Expanded && !_sandbox.IsPaused && NoIntent && Session.StateHash == hash);
        await KeyPress(Key.M);
        var unknown = Cells().First(position => !Map.IsExplored(position) && new SpatialWorld(Session.Room).CanOccupy(position, CombatSession.ActorRadius));
        await Click(_map.GlobalMapPosition(unknown));
        Check("undiscovered_ground_click_keeps_map_open_without_route", _map.Expanded && _sandbox.IsPaused && NoIntent && Session.StateHash == hash);
        await KeyPress(Key.Escape);
        Check("escape_closes_map_without_opening_settings", !_map.Expanded && !_sandbox.IsPaused && !Field<PanelContainer>(_sandbox, "_settingsPanel").Visible);
        await KeyPress(Key.M); await KeyPress(Key.M);
        Check("second_m_closes_map", !_map.Expanded && !_sandbox.IsPaused && NoIntent);
        Check("map_open_close_and_unknown_click_never_attack", !_commands.Skip(commands).Any(command => command.Kind == CombatCommandKind.Cast));

        await KeyPress(Key.P);
        Check("manual_pause_is_active_before_map", _sandbox.IsPaused);
        await KeyPress(Key.M);
        Check("map_can_open_over_independent_manual_pause", _map.Expanded && _sandbox.IsPaused);
        await KeyPress(Key.Escape);
        Check("closing_map_preserves_independent_manual_pause", !_map.Expanded && _sandbox.IsPaused);
        var pausedAt = Player; Ticks(10);
        Check("manual_pause_after_map_prevents_movement", Player == pausedAt);
        await KeyPress(Key.P);
        Check("manual_pause_requires_own_resume", !_sandbox.IsPaused && NoIntent);

        commands = _commands.Count;
        var open = Descendants(_map).OfType<Button>().Single(button => button.Name == "LocalMapOpen" && button.IsVisibleInTree());
        await Click(open.GetGlobalRect().GetCenter());
        Check("minimap_header_opens_map_through_gui", _map.Expanded && _sandbox.IsPaused);
        var close = Descendants(_map).OfType<Button>().Single(button => button.Name == "LocalMapClose" && button.IsVisibleInTree());
        await Click(close.GetGlobalRect().GetCenter()); Ticks(2);
        Check("map_gui_header_and_close_never_start_world_action", !_map.Expanded && NoIntent && !_commands.Skip(commands).Any(command => command.Kind == CombatCommandKind.Cast));
    }

    private async Task HubNavigation()
    {
        int seen = Map.SeenCells.Count;
        var target = ReachableDiscoveredTarget(1900);
        int commands = _commands.Count;
        await KeyPress(Key.M); await Click(_map.GlobalMapPosition(target));
        Check("explored_map_click_closes_only_map_and_starts_mouse_route", !_map.Expanded && !_sandbox.IsPaused &&
            _sandbox.PendingWorldActionId is null && _sandbox.ClickMoveDestination is { } destination && CorePosition.DistanceSquared(target, destination) <= 4);
        await WalkUntilStopped();
        Check("map_navigation_reaches_actual_clicked_ground", CorePosition.DistanceSquared(Player, target) <= ArrivalSquared);
        Check("actual_movement_reveals_additional_cells", Map.SeenCells.Count > seen && _map.IsDiscovered(Player));
        Check("exploration_click_does_not_cast_primary", !_commands.Skip(commands).Any(command => command.Kind == CombatCommandKind.Cast));
        _routes.Add($"hub map: {target}, arrived {Player}; seen {seen}->{Map.SeenCells.Count}");
        await Capture("local-map-discovered-route.png");

        // A separate pause owner may veto navigation. The map must not release it or enqueue a latent route.
        await KeyPress(Key.P); await KeyPress(Key.M);
        target = ReachableDiscoveredTarget(1200);
        await Click(_map.GlobalMapPosition(target));
        Check("map_destination_does_not_override_manual_pause", _sandbox.IsPaused && NoIntent);
        if (_map.Expanded) await KeyPress(Key.M);
        await KeyPress(Key.P); Ticks(4);
        Check("manual_resume_does_not_activate_a_map_click_rejected_while_paused", !_sandbox.IsPaused && NoIntent);
        RecordReplay();
    }

    private async Task RoadExploration()
    {
        for (int index = 0; Session.InHub && index < 600; index++)
            Execute(new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(Session.Campaign)), refresh: false);
        Refresh(); await CloseJourney(); await Frames();
        Check("native_map_route_enters_authored_road", !Session.InHub && Session.Campaign.Capture().ActiveEncounterId == "campaign.road");
        Check("road_map_uses_road_obstacles_and_fresh_discovery", _map.RoomId == Map.RoomId && Session.Room.Obstacles.Length > 0 && !Map.IsExplored(new(1000, 0)));
        // Take the unoccupied northern approach; the initial ghoul blocks the road's direct center line.
        var target = new CorePosition(-2200, -2100);
        Check("road_checkpoint_is_initially_discovered_legal_ground", Map.IsExplored(target) && new SpatialWorld(Session.Room).CanOccupy(target, CombatSession.ActorRadius));
        await KeyPress(Key.M); await Click(_map.GlobalMapPosition(target));
        Check("road_map_checkpoint_uses_existing_click_movement", !_map.Expanded && _sandbox.ClickMoveDestination is not null);
        await WalkUntilStopped();
        _routes.Add($"road wall checkpoint: {target}, arrived {Player}; hidden east={Map.IsExplored(new(1000, -2100))}");
        Check("road_map_checkpoint_reaches_its_unoccupied_ground", CorePosition.DistanceSquared(Player, target) <= ArrivalSquared);
        Check("nearby_floor_behind_road_wall_remains_unexplored", CorePosition.DistanceSquared(Player, new(1000, -2100)) < 3600L * 3600 && !Map.IsExplored(new(1000, -2100)));
        await KeyPress(Key.M);
        string hash = Session.StateHash;
        await Click(_map.GlobalMapPosition(new(0, 0)));
        Check("wall_click_cannot_queue_a_map_destination", _map.Expanded && NoIntent && Session.StateHash == hash);
        await Capture("local-map-road-fog.png"); await KeyPress(Key.M);

        // Real combat earns the cleared room and loot; no actor, health or reward snapshots are fabricated.
        for (int index = 0; !Session.EncounterCleared && index < 6500; index++)
        {
            var commands = CampaignCombatSmoke.Commands(Session.Combat.View, Session.Room).Where(command => command.Kind != CombatCommandKind.Pickup).ToArray();
            Execute(new(EndgameRuntimeAction.Tick, Commands: commands), refresh: false);
            if (index % 120 == 0) { Refresh(); await Frames(); }
        }
        Execute(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)])); await CloseJourney(); await Frames();
        Check("normal_combat_secures_road_for_transition_and_loot_map", Session.EncounterCleared && Session.Campaign.Capture().Campaign.Deaths == 0 && Session.Combat.View.Loot.Count > 0);
        Check("cleared_room_map_draws_discovered_world_markers", _map.DrawnMarkerCount > 0);
        var targets = Field<IReadOnlyList<WorldInteractionTarget>>(_sandbox, "_worldInteractions");
        string[] visibleLoot = Session.Combat.View.Loot.Where(loot => Map.IsExplored(loot.Position)).Select(loot => "loot." + loot.Id).ToArray();
        Check("remaining_loot_and_discovered_exits_match_actual_world_targets", visibleLoot.Length > 0 &&
            _map.DrawnMarkerIds.ToHashSet().SetEquals(targets.Where(target => Map.IsExplored(target.Position)).Select(target => target.Id).Concat(visibleLoot)));
        Check("undiscovered_passage_markers_remain_hidden", targets.Where(target => !Map.IsExplored(target.Position)).All(target => !_map.DrawnMarkerIds.Contains(target.Id)));
        await KeyPress(Key.M); await Capture("local-map-road-secured.png");
        await PausedLootFilters();
        await KeyPress(Key.M);
        RecordReplay();
    }

    private async Task PausedLootFilters()
    {
        int minimum = Field<int>(_sandbox, "_minimumLootRarity");
        string hash = Session.StateHash; long tick = Session.Tick;
        string[] all = Session.Combat.View.Loot.Where(loot => Map.IsExplored(loot.Position)).Select(loot => "loot." + loot.Id).ToArray();
        string[] highest = Session.Combat.View.Loot.Where(loot => loot.Item.Rarity == "Godwrought" && Map.IsExplored(loot.Position)).Select(loot => "loot." + loot.Id).ToArray();
        HashSet<string> ShownLoot() => _map.DrawnMarkerIds.Where(id => id.StartsWith("loot.", StringComparison.Ordinal)).ToHashSet();
        void SetMinimum(int value) => (typeof(Sandbox).GetField("_minimumLootRarity", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("_minimumLootRarity")).SetValue(_sandbox, value);
        void Alt(bool pressed) => Input.ParseInputEvent(new InputEventKey { Keycode = Key.Alt, PhysicalKeycode = Key.Alt, Pressed = pressed, AltPressed = pressed });
        try
        {
            // Isolate the presentation preference from opening another modal. All held-key changes
            // still enter Godot's native input pipeline, including its InputMap action state.
            SetMinimum(5); await Frames();
            Check("paused_map_refilters_loot_without_a_combat_tick", _map.Expanded && _sandbox.IsPaused && all.Length > highest.Length && ShownLoot().SetEquals(highest));
            Alt(true); await Frames();
            Check("held_show_loot_updates_expanded_paused_map", Input.IsActionPressed("aw_showloot") && _map.Expanded && _sandbox.IsPaused && ShownLoot().SetEquals(all));
            Alt(false); await Frames();
            Check("released_show_loot_restores_paused_map_filter", !Input.IsActionPressed("aw_showloot") && _map.Expanded && ShownLoot().SetEquals(highest));
        }
        finally { Alt(false); SetMinimum(minimum); await Frames(); }
        Check("paused_map_filter_input_preserves_core_state_and_original_preference", Session.StateHash == hash && Session.Tick == tick &&
            Field<int>(_sandbox, "_minimumLootRarity") == minimum && ShownLoot().SetEquals(all));
    }

    private async Task SaveAndLegacyLoad()
    {
        await KeyPress(Key.M); await KeyPress(Key.F5);
        Check("f5_from_expanded_map_writes_shipping_character_save", System.IO.File.Exists(SavePath) && !_map.Expanded && !_sandbox.IsPaused);
        var saved = Session.Capture();
        int[] savedCells = Map.SeenCells.ToArray(); string savedRoom = Map.RoomId; var savedPlayer = Player;
        var target = ReachableDiscoveredTarget(1600);
        await KeyPress(Key.M); await Click(_map.GlobalMapPosition(target)); await WalkUntilStopped();
        RecordReplay(); await KeyPress(Key.M); await KeyPress(Key.F9); await CloseJourney();
        Check("f9_restores_saved_room_position_and_exploration", Player == savedPlayer && Map.RoomId == savedRoom && Map.SeenCells.SequenceEqual(savedCells));
        Check("f9_cancels_previous_map_navigation", NoIntent && !_map.Expanded);

        // This is the exact pre-feature archive shape, retaining the earned character and authored room.
        // Read must preserve its hash; only the shipping load handler may explicitly enable exploration.
        var legacy = saved with { ExplorationMap = null, Campaign = saved.Campaign with { ExplorationMap = null } };
        string legacyJson = JsonData.Write(new EndgameRuntimeSave(1, JsonData.Hash(legacy), legacy));
        Check("legacy_fixture_omits_optional_map_fields", !legacyJson.Contains("explorationMap", StringComparison.Ordinal));
        var read = EndgameRuntimeSaveStore.Read(CombatJson, Adventure, Progression, Campaign, Endgame, legacyJson);
        Check("previous_save_load_preserves_unenabled_map_state_and_hash", read.LocalMap is null && read.StateHash == JsonData.Hash(legacy));
        System.IO.File.WriteAllText(Path.Combine(_output, "legacy-no-map.save.json"), legacyJson);
        System.IO.File.WriteAllText(SavePath, legacyJson);
        RecordReplay(); await KeyPress(Key.F9); await CloseJourney();
        Check("shipping_legacy_load_initializes_map_through_explicit_replay_command", Session.LocalMap is not null &&
            Session.CaptureReplay().Initial.ExplorationMap is null && Session.CaptureReplay().Initial.Campaign.ExplorationMap is null &&
            Session.CaptureReplay().Frames.Any(frame => frame.Command.Action == EndgameRuntimeAction.EnableExplorationMap));
        Check("legacy_map_initialization_preserves_character_and_room", Player == savedPlayer && Session.Combat.View.Inventory.Select(item => item.Id).SequenceEqual(saved.Campaign.Combat.Inventory.Select(item => item.Id)) && Map.RoomId == savedRoom);
        await Capture("local-map-legacy-loaded.png");
        RecordReplay();
    }

    private async Task RoomTransition()
    {
        string road = Map.RoomId;
        int[] roadCells = Map.SeenCells.ToArray();
        var target = ReachableDiscoveredTarget(1400);
        await KeyPress(Key.M); await Click(_map.GlobalMapPosition(target));
        Check("transition_checkpoint_has_pending_map_destination", _sandbox.ClickMoveDestination is not null);
        Execute(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.AdvanceEncounter)));
        await Frames(); await CloseJourney();
        Check("actual_campaign_transition_replaces_local_room_map", Session.Campaign.Capture().ActiveEncounterId == "campaign.monastery" && Map.RoomId != road && _map.RoomId == Map.RoomId);
        Check("room_transition_cancels_stale_map_destination", NoIntent && !_map.Expanded);
        Check("new_room_discovery_does_not_reveal_entire_room", Map.SeenCells.Count > 0 && Map.SeenCells.Count < Map.Columns * Map.Rows / 2);
        await Capture("local-map-monastery.png");
        var snapshot = Session.Capture();
        EndgameRuntimeSaveStore.Write(Path.Combine(_output, "room-transition.save.json"), CombatJson, Adventure, Progression, Campaign, Endgame, snapshot);
        var loaded = EndgameRuntimeSaveStore.Load(Path.Combine(_output, "room-transition.save.json"), CombatJson, Adventure, Progression, Campaign, Endgame).Session;
        Check("multiroom_exploration_save_roundtrip_preserves_hash", loaded.StateHash == Session.StateHash);
        var remembered = loaded.Capture().Campaign.ExplorationMap?.Rooms.GetValueOrDefault(road);
        Check("new_room_preserves_previous_room_discovery", remembered is not null && remembered.SeenCells.SequenceEqual(roadCells));
        await KeyPress(Key.M);
        Check("transition_checkpoint_has_expanded_map", _map.Expanded && _sandbox.IsPaused);
        Execute(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.ReturnToHub)));
        await Frames();
        Check("travel_closes_expanded_map_and_releases_only_map_pause", Session.InHub && !_map.Expanded &&
            !Field<HashSet<string>>(_sandbox, "_modalPauses").Contains("local-map") && NoIntent);
        await CloseJourney();
        Check("closing_arrival_journey_resumes_after_map_transition", !_sandbox.IsPaused && NoIntent);
    }

    private IEnumerable<CorePosition> Cells() => Enumerable.Range(0, Map.Columns * Map.Rows).Select(Map.CellCenter);
    private CorePosition ReachableDiscoveredTarget(int minimumDistance)
    {
        var space = new SpatialWorld(Session.Room); var planner = new ClickMovePlanner(Session.Room);
        var occupied = Session.Combat.View.Actors.Where(actor => actor.Id != 1 && actor.Health > 0).Select(actor => actor.Position).ToArray();
        foreach (var target in Cells().Where(position => Map.IsExplored(position) &&
            CorePosition.DistanceSquared(Player, position) >= (long)minimumDistance * minimumDistance && space.CanOccupy(position, CombatSession.ActorRadius))
            .OrderBy(position => CorePosition.DistanceSquared(Player, position)).ThenBy(position => position.X).ThenBy(position => position.Z))
            if (planner.TrySetDestination(Player, target, occupied)) return target;
        throw new InvalidDataException("No reachable discovered map destination beyond " + minimumDistance + ".");
    }
    private static long ArrivalSquared => (long)(ClickMovePlanner.ArrivalTolerance + 3) * (ClickMovePlanner.ArrivalTolerance + 3);
    private async Task WalkUntilStopped()
    {
        var space = new SpatialWorld(Session.Room);
        for (int index = 0; index < 720 && !NoIntent; index++)
        {
            Ticks();
            if (!space.CanOccupy(Player, CombatSession.ActorRadius)) throw new InvalidDataException("Map movement crossed an authoritative solid.");
            if (index % 30 == 0) await Frames();
        }
        Check("map_route_completed_" + ++_completedRoutes, NoIntent);
        Ticks(2); await Frames();
    }
    private void Execute(EndgameRuntimeCommand command, bool refresh = true)
    {
        var result = Session.Execute(command); _setupCommands++;
        if (!result.Success) throw new InvalidDataException("Map setup command failed: " + result.Reason);
        Invoke(_director, "Observe", result); if (refresh) Refresh();
    }
    private void Refresh() { _sandbox.AdoptSession(Session.Combat); Invoke(_director, "Refresh"); }
    private string CombatJson => Field<string>(_director, "_combatJson");
    private AdventureContent Adventure => Field<AdventureContent>(_director, "_adventure");
    private ProgressionContent Progression => Field<ProgressionContent>(_director, "_progression");
    private CampaignContent Campaign => Field<CampaignContent>(_director, "_campaign");
    private EndgameContent Endgame => Field<EndgameContent>(_director, "_endgame");
    private bool VerifyReplay(EndgameRuntimeReplay replay) => EndgameRuntimeReplayRunner.Run(CombatJson, Adventure, Progression, Campaign, Endgame, replay).Success;
    private void RecordReplay() { if (Session.CaptureReplay().Frames.Length > 0) _replays.Add(Session.CaptureReplay()); }
    private void Ticks(int count = 1) { for (int index = 0; index < count; index++) _sandbox._Process(FixedStepClock.SecondsPerTick); }
    private async Task Click(Vector2 position)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = position }, true);
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = position, Pressed = pressed }, true);
        await Frames();
    }
    private async Task KeyPress(Key key, bool shift = false)
    {
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed, ShiftPressed = shift }, true);
        await Frames();
    }
    private async Task ClickButton(string text)
    {
        var button = Descendants(_director).OfType<Button>().Single(candidate => candidate.Text == text && candidate.IsVisibleInTree());
        await Click(button.GetGlobalRect().GetCenter());
    }
    private async Task CloseJourney()
    {
        var close = Descendants(_hud).OfType<Button>().SingleOrDefault(button => button.Text == "Close" && button.IsVisibleInTree());
        if (close is not null) await Click(close.GetGlobalRect().GetCenter());
    }
    private async Task Frames(int count = 2)
    {
        for (int index = 0; index < count; index++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // Simulation is manually stepped above; retain the shipping per-frame map layout refresh.
            if (_sandbox is not null) { Invoke(_sandbox, "RefreshHud"); Invoke(_sandbox, "RefreshLocalMapPresentation"); }
        }
    }
    private static T Field<T>(object owner, string name) => (T)(owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) ?? throw new MissingFieldException(name));
    private static void Invoke(object owner, string name, params object[] args) =>
        (owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(name)).Invoke(owner, args);
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private async Task Capture(string filename)
    {
        if (!_writeReport || !OS.GetCmdlineUserArgs().Contains("--capture-local-map") || DisplayServer.GetName() == "headless") return;
        await Frames(3); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + filename, image.GetWidth() > 0 && image.SavePng(Path.Combine(_output, filename)) == Error.Ok); _captures.Add(filename);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Local map check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "LocalMapClientSmokePassed" : "LocalMapClientSmokeFailed",
            passed,
            checks = _checks,
            captures = _captures,
            inputCommands = _commands.Count,
            campaignSetupCommands = _setupCommands,
            replayCount = _replays.Count,
            routes = _routes,
            error,
            scope = "The shipping EndgameDirector receives native M/Escape/pause keys and actual GUI mouse clicks on its minimap, expanded map and controls. The Sandbox advances at its fixed interval for movement, while ordinary Core campaign commands prepare the character and earn road combat rewards. Checks cover discovery growth, wall occlusion, inadmissible map clicks, independent pause ownership, actual click movement, scene invalidation and shipping F5/F9 persistence. A checksum-valid pre-feature archive shape proves read-only legacy restoration and explicit replayed map initialization; branch replays must verify. No fabricated character, combat victories, loot or rewards."
        };
        if (_writeReport && !passed && _director is not null && _sandbox is not null)
            System.IO.File.WriteAllText(Path.Combine(_output, "local-map-failed-state.json"), JsonData.Write(Session.Capture()));
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "local-map-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); if (_director is not null && !_director.IsInsideTree()) _director.Free(); GetTree().Quit(passed ? 0 : 1);
    }
}
