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

/// <summary>Native travel through the shipping director earns and revisits the Verdant Maw's real rooms.</summary>
public partial class VerdantExplorationSmoke : Node
{
    private const string Shrine = "exploration.briar_shrine", ShrineEvent = "event.briar_shrine";
    private const string Hunt = "exploration.antler_hunt", HuntEvent = "event.wake_hunt";
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [], _routes = [];
    private readonly List<CombatCommand> _commands = [];
    private readonly List<EndgameRuntimeReplay> _replays = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private CampaignStage _stage = null!;
    private CampaignHud _hud = null!;
    private LocalExplorationMap _map = null!;
    private Camera3D _camera = null!;
    private string _output = "";
    private bool _writeReport;
    private int _setupCommands, _completedRoutes, _focusRecoveries;
    private EndgameRuntimeSession Session => Field<EndgameRuntimeSession>(_director, "_session");
    private LocalMapView Map => Session.LocalMap ?? throw new InvalidDataException("The shipping local map was not enabled.");
    private CorePosition Player => Session.Combat.View.Actors.Single(actor => actor.Id == 1).Position;
    private bool NoIntent => _sandbox.ClickMoveDestination is null && _sandbox.PendingWorldActionId is null && !_sandbox.MouseDestinationVisible;
    private string SavePath => Path.Combine(_output, "endgame.save.json");
    private string LootHash => JsonData.Hash(Session.Combat.Capture().Loot);
    private IReadOnlyList<WorldInteractionTarget> Targets => Field<IReadOnlyList<WorldInteractionTarget>>(_sandbox, "_worldInteractions");

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(argument => argument.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--verdant-exploration-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(path => !path.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Verdant exploration smoke requires --verdant-exploration-smoke --discipline=Vanguard --output=<fresh-artifact-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = new(1280, 800); GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director); _director.SetProcess(false);
            _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false); _sandbox.AutomaticStep = false;
            _stage = Field<CampaignStage>(_director, "_stage"); _hud = Field<CampaignHud>(_director, "_campaignHud");
            _map = Descendants(_director).OfType<LocalExplorationMap>().Single();
            _camera = _sandbox.GetChildren().OfType<Camera3D>().Single();
            var advance = _sandbox.AdvanceOverride!;
            _sandbox.AdvanceOverride = commands => { _commands.AddRange(commands); return advance(commands); };
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            await Frames(8); await CloseJourney();
            if (_sandbox.IsPaused) await ClickButton("Resume playing");
            Ticks(2);
            await ReachVerdantMaw();
            await ShrineRoute();
            await HuntRoute();
            await RootheartAndBacktracking();
            RecordReplay();
            Check("native_exploration_branch_replays_verify", _replays.Count >= 5 && _replays.All(VerifyReplay));
            System.IO.File.WriteAllText(Path.Combine(_output, "verdant-exploration-replays.json"), JsonData.Write(_replays));
            Finish(true, "");
        }
        catch (Exception exception)
        {
            try { await Capture("verdant-exploration-failure.png"); } catch (Exception captureError) { GD.PushError(captureError.Message); }
            GD.PushError(exception.ToString()); Finish(false, exception.Message);
        }
    }

    private async Task ReachVerdantMaw()
    {
        for (int index = 0; Session.Campaign.ActiveEncounterId != "campaign.living_ruins" && index < 18000; index++)
        {
            Execute(new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(Session.Campaign)), refresh: false);
            if (index % 120 == 0) { Refresh(); await Frames(); }
        }
        Refresh(); await CloseJourney(); await Frames();
        Check("real_campaign_unlocks_living_ruins", Session.Campaign.ActiveEncounterId == "campaign.living_ruins" &&
            Session.Campaign.Capture().Campaign.CompletedActs.Contains(1) && !Session.EncounterCleared);
        Check("living_ruins_uses_authored_collision_and_its_own_map", Session.Room.Obstacles.Length > 0 &&
            _sandbox.EnvironmentStyle == "verdant_ruins" && _map.RoomId == Map.RoomId && Map.RoomId == "campaign.living_ruins");
        await Capture("verdant-living-ruins-entered.png");
        await Fight("living_ruins");
        Check("secured_living_ruins_reveals_physical_shrine_branch", Targets.Any(target => target.Id == "verdant.shrine.enter") &&
            Session.Combat.View.Loot.Count > 0);
        RecordReplay();
    }

    private async Task ShrineRoute()
    {
        string ruinsRoom = JsonData.Hash(Session.Room), ruinsLoot = LootHash;
        var ruinsDrops = Session.Combat.View.Loot.Select(loot => loot.Id).ToHashSet();
        await Marker("verdant.shrine.enter", "shrine_enter", Shrine);
        int[] ruinsCells = Session.Capture().Campaign.ExplorationMap!.Rooms["campaign.living_ruins"].SeenCells.ToArray();
        Check("shrine_entry_starts_separate_authored_elite_room", Session.Campaign.Capture().Campaign.Exploration?.Id == ShrineEvent &&
            Session.Room.Obstacles.Length > 0 && JsonData.Hash(Session.Room) != ruinsRoom && Map.RoomId == Shrine &&
            Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0));
        await Capture("verdant-briar-shrine-entered.png");
        await Fight("briar_shrine");
        Check("shrine_clearance_keeps_testament_unclaimed", !Session.Campaign.Capture().Campaign.CompletedExploration.Contains(ShrineEvent) &&
            Targets.Any(target => target.Id == "verdant.shrine.treasure") && Session.Combat.View.Loot.Count > 0 &&
            Session.Combat.View.Loot.All(loot => !ruinsDrops.Contains(loot.Id)));
        string shrineLoot = LootHash;
        var before = Session.Capture().Campaign.Production.Progression.Character;
        var itemIds = before.Items.Select(item => item.Id).ToHashSet();
        await Marker("verdant.shrine.treasure", "shrine_claim", Shrine);
        var claimed = Session.Capture().Campaign.Production.Progression.Character;
        var granted = claimed.Items.Where(item => !itemIds.Contains(item.Id)).ToArray();
        Check("shrine_claim_grants_one_rare_oathseal_and_thirty_materials", granted.Length == 1 &&
            granted[0].DefinitionId == "item.stone_seal" && granted[0].Rarity == ItemRarity.Rare && claimed.Materials == before.Materials + 30 &&
            claimed.OperationReceipts.ContainsKey("campaign.briar.testament"));
        Check("shrine_claim_records_lore_without_collecting_floor_loot", Session.Campaign.Capture().Campaign.CompletedExploration.Contains(ShrineEvent) &&
            Session.Campaign.Capture().Campaign.Discoveries.Contains("discovery.briar_shrine") && LootHash == shrineLoot &&
            !Targets.Any(target => target.Id == "verdant.shrine.treasure"));
        RejectStaleShrineClaim("claimed");
        await MapNavigation("shrine");
        await KeyPress(Key.F5);
        Check("shipping_save_writes_claimed_shrine_and_cached_ruins", System.IO.File.Exists(SavePath) &&
            Session.Capture().Campaign.ClearedRooms!.ContainsKey("campaign.living_ruins"));
        string savedHash = Session.StateHash; var savedPlayer = Player; int[] savedCells = Map.SeenCells.ToArray();
        await Marker("verdant.shrine.return", "shrine_return", "campaign.living_ruins");
        Check("shrine_return_restores_ruins_loot_and_exploration", LootHash == ruinsLoot && JsonData.Hash(Session.Room) == ruinsRoom &&
            ruinsCells.All(Map.SeenCells.Contains) && NoIntent);
        RecordReplay();
        await KeyPress(Key.F9); await CloseJourney();
        Check("shipping_load_restores_claimed_shrine_exactly", Session.StateHash == savedHash && Player == savedPlayer &&
            Map.RoomId == Shrine && Map.SeenCells.SequenceEqual(savedCells) && LootHash == shrineLoot && NoIntent);
        RejectStaleShrineClaim("loaded");
        await Capture("verdant-briar-shrine-claimed.png");
        await Marker("verdant.shrine.return", "shrine_loaded_return", "campaign.living_ruins");
        await Marker("verdant.shrine.enter", "shrine_revisit", Shrine);
        Check("shrine_revisit_keeps_clearance_floor_loot_and_single_reward", Session.EncounterCleared && LootHash == shrineLoot &&
            !Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0) &&
            Session.Capture().Campaign.Production.Progression.Character.Items.Count(item => item.Id == granted[0].Id) == 1 &&
            Session.Capture().Campaign.Production.Progression.Character.Materials == claimed.Materials);
        RejectStaleShrineClaim("revisited");
        await Marker("verdant.shrine.return", "shrine_final_return", "campaign.living_ruins");
        Check("shrine_round_trips_preserve_all_ruins_drops", LootHash == ruinsLoot && ruinsDrops.All(id =>
            Descendants(_sandbox).OfType<LootVisual>().Any(visual => visual.Name == "Loot" + id && visual.IsVisibleInTree())));
        await Capture("verdant-ruins-after-shrine.png"); RecordReplay();
        await Marker("verdant.forward.village", "village_enter", "campaign.plague_village");
    }

    private async Task HuntRoute()
    {
        Check("village_has_distinct_authored_ground_and_map", Session.Room.Obstacles.Length > 0 &&
            _sandbox.EnvironmentStyle == "verdant_village" && Map.RoomId == "campaign.plague_village");
        await Fight("plague_village");
        string villageLoot = LootHash, villageRoom = JsonData.Hash(Session.Room);
        await Capture("verdant-plague-village-secured.png");
        await Marker("verdant.hunt.enter", "hunt_enter", "clear");
        Check("physical_hunt_entrance_starts_tracking_in_authored_grove", Session.Campaign.Capture().Campaign.Exploration?.Id == HuntEvent &&
            Map.RoomId == Hunt && _sandbox.EnvironmentStyle == "verdant_hunt" && Session.Room.Obstacles.Length > 0 &&
            !Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0));
        var clues = Field<CampaignContent>(_director, "_campaign").Capture().Exploration.Single(exploration => exploration.Id == HuntEvent).Clues;
        for (int index = 0; index < clues.Length; index++)
        {
            Check("hunt_clue_" + index + "_is_the_only_available_trail_step", clues.Count(clue => Targets.Any(target => target.Id == clue)) == 1 &&
                Targets.Any(target => target.Id == clues[index]));
            var visual = Targets.Single(target => target.Id == clues[index]).Visual;
            Check("hunt_clue_" + index + "_exposes_its_visible_body_for_mouse_picking", visual is VerdantTrailVisual { Tracked: false } &&
                Descendants(visual).OfType<MeshInstance3D>().Any(mesh => mesh.IsVisibleInTree() && mesh.Mesh is not null));
            await Marker(clues[index], "hunt_clue_" + index, index == clues.Length - 1 ? Hunt : "clear");
            Check("hunt_clue_" + index + "_spent_body_is_no_longer_an_interaction", _stage.GetInteractionVisual(clues[index]) is null &&
                !Targets.Any(target => target.Id == clues[index]));
        }
        Check("all_native_tracking_steps_start_the_real_antler_battle", Session.Campaign.Capture().Campaign.Exploration?.TrackedClues == clues.Length &&
            Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0) && Map.RoomId == Hunt);
        await Capture("verdant-antler-hunt-found.png");
        await Fight("antler_hunt");
        string huntLoot = LootHash;
        Check("antler_clearance_records_hunt_and_keeps_real_grove_drops", Session.Combat.View.Loot.Count > 0 &&
            Session.Campaign.Capture().Campaign.CompletedExploration.Contains(HuntEvent) && Targets.Any(target => target.Id == "verdant.hunt.return"));
        await Marker("verdant.hunt.return", "hunt_return", "campaign.plague_village");
        Check("hunt_return_preserves_discovery_and_restores_village", Session.Campaign.Capture().Campaign.CompletedExploration.Contains(HuntEvent) &&
            LootHash == villageLoot && JsonData.Hash(Session.Room) == villageRoom);
        int materials = Session.Capture().Campaign.Production.Progression.Character.Materials;
        await Marker("verdant.hunt.enter", "hunt_revisit", Hunt);
        Check("hunt_revisit_retains_loot_without_restarting_tracking_or_enemies", Session.EncounterCleared && LootHash == huntLoot &&
            !Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0) &&
            clues.All(clue => !Targets.Any(target => target.Id == clue)));
        await Marker("verdant.hunt.return", "hunt_second_return", "campaign.plague_village");
        Check("hunt_revisit_cannot_duplicate_material_rewards", Session.Capture().Campaign.Production.Progression.Character.Materials == materials && LootHash == villageLoot);
        await MapNavigation("village");
        await Capture("verdant-village-hunt-complete.png"); RecordReplay();
    }

    private async Task RootheartAndBacktracking()
    {
        var definition = Field<CampaignContent>(_director, "_campaign").Capture();
        var choice = definition.Choices.Single(candidate => candidate.Id == definition.Acts[1].RequiredChoice);
        if (!Session.Campaign.Capture().Campaign.Choices.ContainsKey(choice.Id))
            Execute(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.Choose, Id: choice.Id, Value: choice.Outcomes[0].Id)));
        await CloseJourney(); await Marker("verdant.forward.rootheart", "rootheart_enter", "campaign.rootheart");
        Check("rootheart_has_authored_sanctum_and_map", Session.Room.Obstacles.Length > 0 && _sandbox.EnvironmentStyle == "verdant_heart" && Map.RoomId == "campaign.rootheart");
        await Capture("verdant-rootheart-entered.png");
        await Fight("rootheart"); string rootheartLoot = LootHash;
        Check("real_rootheart_victory_completes_act_two", Session.Campaign.Capture().Campaign.CompletedActs.Contains(2) &&
            Session.Combat.View.Actors.Any(actor => actor.DefinitionId == "boss.rootheart" && actor.Health <= 0));
        await Capture("verdant-rootheart-defeated.png");
        await Marker("verdant.back.village", "rootheart_backtrack", "campaign.plague_village");
        await Marker("verdant.back.ruins", "village_backtrack", "campaign.living_ruins");
        await Marker("verdant.forward.village", "ruins_revisit_village", "campaign.plague_village");
        await Marker("verdant.forward.rootheart", "village_revisit_rootheart", "campaign.rootheart");
        Check("adjacent_backtracking_preserves_rootheart_victory_and_floor_loot", Session.EncounterCleared && LootHash == rootheartLoot &&
            !Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0));
        var atlas = Session.Capture().Campaign.ExplorationMap!.Rooms;
        Check("all_five_verdant_rooms_remember_discovered_terrain", new[] { "campaign.living_ruins", "campaign.plague_village", "campaign.rootheart", Shrine, Hunt }
            .All(id => atlas.TryGetValue(id, out var room) && room.SeenCells.Length > 0));
        await KeyPress(Key.M); await Capture("verdant-rootheart-local-map.png"); await KeyPress(Key.F5);
        string hash = Session.StateHash;
        var loaded = EndgameRuntimeSaveStore.Load(SavePath, CombatJson, Adventure, Progression, Campaign, Endgame).Session;
        Check("complete_region_save_roundtrip_preserves_maps_and_cached_rooms", loaded.StateHash == hash &&
            loaded.Capture().Campaign.ClearedRooms!.Keys.Count(id => VerdantCampaignLayout.Contains(id)) == 4);
        RecordReplay();
        await KeyPress(Key.F9); await CloseJourney();
        Check("shipping_reload_preserves_complete_region_and_cancels_input", Session.StateHash == hash && NoIntent && !_map.Expanded && !_sandbox.IsPaused);
    }

    private async Task Fight(string label)
    {
        int deaths = Session.Campaign.Capture().Campaign.Deaths;
        for (int index = 0; !Session.EncounterCleared && index < 8000; index++)
        {
            var commands = CampaignCombatSmoke.Commands(Session.Combat.View, Session.Room).Where(command => command.Kind != CombatCommandKind.Pickup).ToArray();
            Execute(new(EndgameRuntimeAction.Tick, Commands: commands), refresh: false);
            if (index % 120 == 0) { Refresh(); await Frames(); }
        }
        Execute(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)])); await CloseJourney(); await Frames();
        Check(label + "_combat_is_won_without_death_or_fabricated_rewards", Session.EncounterCleared &&
            Session.Campaign.Capture().Campaign.Deaths == deaths && Session.Combat.View.Actors.Single(actor => actor.Id == 1).Health > 0);
    }

    private async Task Marker(string id, string label, string expectedEncounter)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            await RecoverFocusPause(); await CloseJourney();
            var target = Targets.Single(candidate => candidate.Id == id);
            bool distant = CorePosition.DistanceSquared(Player, target.Position) > (long)target.Range * target.Range;
            string hash = Session.StateHash; int commands = _commands.Count;
            var point = await FindInteractionPoint(id);
            if (await RecoverFocusPause()) continue;
            Check(label + "_native_hover_identifies_marker_without_mutation", _sandbox.HoveredWorldActionId == id && Session.StateHash == hash);
            var start = Player;
            await Click(point);
            bool interrupted = await RecoverFocusPause();
            bool actionAlreadyCompleted = Session.Campaign.ActiveEncounterId == expectedEncounter && !Targets.Any(candidate => candidate.Id == id);
            if (interrupted && !actionAlreadyCompleted) continue;
            if (distant && !interrupted) Check(label + "_native_click_starts_specific_approach", _sandbox.PendingWorldActionId == id && _sandbox.ClickMoveDestination is not null);
            await WalkUntilStopped();
            // Native windows may lose focus when an external diagnostic is opened. The shipping
            // pause correctly cancels movement; explicitly resume and click the same target again.
            // Other pause owners and navigation failures remain failures.
            if (await RecoverFocusPause() && Session.Campaign.ActiveEncounterId != expectedEncounter) continue;
            Check(label + "_arrival_executes_selected_world_action", Session.Campaign.ActiveEncounterId == expectedEncounter && NoIntent);
            Check(label + "_approach_never_attacks_or_collects_floor_loot", !_commands.Skip(commands).Any(command => command.Kind is CombatCommandKind.Cast or CombatCommandKind.Pickup));
            _routes.Add($"{id}: from {start} through {target.Position}, arrived in {Session.Campaign.ActiveEncounterId} at {Player}");
            return;
        }
        throw new InvalidDataException("Repeated native focus interruptions prevented passage " + id + ".");
    }

    private void RejectStaleShrineClaim(string label)
    {
        string hash = Session.StateHash;
        Check(label + "_stale_shrine_marker_is_unavailable", !_sandbox.RequestWorldInteraction("verdant.shrine.treasure") && NoIntent && Session.StateHash == hash);
        var result = Session.Execute(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.InteractVerdant, Id: "verdant.shrine.treasure")));
        Check(label + "_stale_shrine_claim_is_transactionally_rejected", !result.Success && Session.StateHash == hash);
        Refresh();
    }

    private async Task MapNavigation(string label, int focusAttempt = 0)
    {
        if (focusAttempt >= 4) throw new InvalidDataException("Repeated native focus interruptions prevented map navigation in " + label + ".");
        await RecoverFocusPause();
        var space = new SpatialWorld(Session.Room); var planner = new ClickMovePlanner(Session.Room);
        var target = Enumerable.Range(0, Map.Columns * Map.Rows).Select(Map.CellCenter)
            .Where(position => Map.IsExplored(position) && space.CanOccupy(position, CombatSession.ActorRadius) && CorePosition.DistanceSquared(Player, position) > 1400L * 1400)
            .OrderBy(position => CorePosition.DistanceSquared(Player, position)).ThenBy(position => position.X).ThenBy(position => position.Z)
            .First(position => planner.TrySetDestination(Player, position, []));
        int seen = Map.SeenCells.Count;
        await KeyPress(Key.M); Check(label + "_native_m_opens_local_map", _map.Expanded && _sandbox.IsPaused && _map.RoomId == Map.RoomId);
        await Capture("verdant-" + label + "-map.png");
        if (await RecoverFocusPause()) { await MapNavigation(label, focusAttempt + 1); return; }
        await Click(_map.GlobalMapPosition(target));
        if (await RecoverFocusPause()) { await MapNavigation(label, focusAttempt + 1); return; }
        Check(label + "_discovered_map_click_starts_navigation", !_map.Expanded && !_sandbox.IsPaused && _sandbox.ClickMoveDestination is not null);
        await WalkUntilStopped();
        if (await RecoverFocusPause()) { await MapNavigation(label, focusAttempt + 1); return; }
        Check(label + "_map_route_reaches_clicked_ground_and_keeps_discovery", CorePosition.DistanceSquared(Player, target) <=
            (long)(ClickMovePlanner.ArrivalTolerance + 3) * (ClickMovePlanner.ArrivalTolerance + 3) && Map.SeenCells.Count >= seen);
    }

    private async Task WalkUntilStopped()
    {
        for (int index = 0; index < 1000 && !NoIntent; index++)
        {
            Ticks();
            if (!new SpatialWorld(Session.Room).CanOccupy(Player, CombatSession.ActorRadius))
                throw new InvalidDataException("Native Verdant movement entered an authoritative solid.");
            if (index % 30 == 0) await Frames();
        }
        Check("native_route_completed_" + ++_completedRoutes, NoIntent);
        Ticks(2); await Frames();
    }

    private async Task<Vector2> FindInteractionPoint(string id)
    {
        var visual = Targets.Single(target => target.Id == id).Visual ?? throw new InvalidDataException("Missing interaction visual: " + id);
        for (int zoom = 0; zoom < 20; zoom++)
        {
            foreach (var point in PickPoints(visual))
            {
                await Hover(point);
                if (await RecoverFocusPause()) continue;
                if (_sandbox.HoveredWorldActionId == id) return point;
            }
            if (zoom == 19 || !await ZoomOut()) break;
        }
        throw new InvalidDataException("No native viewport pick selected " + id + "; camera=" + _camera.Size + "; player=" + Player + "; paused=" + _sandbox.IsPaused);
    }
    private IEnumerable<Vector2> PickPoints(Node3D visual)
    {
        yield return _camera.UnprojectPosition(visual.GlobalPosition + Vector3.Up);
        foreach (var mesh in Descendants(visual).OfType<MeshInstance3D>().Where(mesh => mesh.IsVisibleInTree() && mesh.Mesh is not null))
        {
            var bounds = mesh.Mesh.GetAabb();
            yield return _camera.UnprojectPosition(mesh.GlobalTransform * (bounds.Position + bounds.Size * .5f));
        }
    }
    private async Task<bool> ZoomOut()
    {
        var size = GetViewport().GetVisibleRect().Size;
        foreach (var fraction in new[] { new Vector2(.5f, .5f), new(.7f, .5f), new(.5f, .6f), new(.8f, .55f), new(.25f, .6f) })
        {
            var point = size * fraction; await Hover(point);
            if (GetViewport().GuiGetHoveredControl() is not null) continue;
            float before = _camera.Size;
            foreach (bool pressed in new[] { true, false })
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Position = point, Pressed = pressed }, true);
            await Frames(); return _camera.Size > before;
        }
        throw new InvalidDataException("No unoccluded viewport location accepted camera input.");
    }
    private void Execute(EndgameRuntimeCommand command, bool refresh = true)
    {
        var result = Session.Execute(command); _setupCommands++;
        if (!result.Success) throw new InvalidDataException("Verdant setup command failed: " + result.Reason);
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
    private async Task Hover(Vector2 point)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true); Invoke(_sandbox, "UpdateWorldHover", .1d); await Frames();
    }
    private async Task Click(Vector2 point)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = point, Pressed = pressed }, true);
        await Frames();
    }
    private async Task KeyPress(Key key)
    {
        await RecoverFocusPause();
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
        await Frames();
    }
    private async Task<bool> RecoverFocusPause()
    {
        if (DisplayServer.GetName() == "headless" || !Field<bool>(_sandbox, "_interruptionPause") ||
            Field<bool>(_sandbox, "_manualPause") || Field<HashSet<string>>(_sandbox, "_modalPauses").Count != 0 ||
            !Field<PanelContainer>(_sandbox, "_resumePanel").IsVisibleInTree() ||
            Field<Label>(_sandbox, "_resumeReason").Text != "The game lost focus. Return and choose Resume when ready.") return false;
        string hash = Session.StateHash;
        Check("focus_interruption_" + ++_focusRecoveries + "_cancels_pending_world_input", _sandbox.IsPaused && NoIntent);
        GetWindow().GrabFocus(); await Frames(); await ClickButton("Resume playing");
        Check("focus_recovery_" + _focusRecoveries + "_uses_resume_without_mutating_world", !_sandbox.IsPaused && NoIntent && Session.StateHash == hash);
        return true;
    }
    private async Task ClickButton(string text) => await Click(Descendants(_director).OfType<Button>().Single(button => button.Text == text && button.IsVisibleInTree()).GetGlobalRect().GetCenter());
    private async Task CloseJourney()
    {
        var close = Descendants(_hud).OfType<Button>().SingleOrDefault(button => button.Text == "Close" && button.IsVisibleInTree());
        if (close is not null) await Click(close.GetGlobalRect().GetCenter());
        await RecoverFocusPause();
    }
    private async Task Frames(int count = 2)
    {
        for (int index = 0; index < count; index++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_sandbox is not null) { Invoke(_sandbox, "RefreshHud"); Invoke(_sandbox, "RefreshLocalMapPresentation"); }
        }
    }
    private static T Field<T>(object owner, string name) => (T)(owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) ?? throw new MissingFieldException(name));
    private static void Invoke(object owner, string name, params object[] args) =>
        (owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(name)).Invoke(owner, args);
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private async Task Capture(string filename)
    {
        if (!_writeReport || !OS.GetCmdlineUserArgs().Contains("--capture-verdant-exploration") || DisplayServer.GetName() == "headless") return;
        await Frames(3); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + filename, image.GetWidth() > 0 && image.SavePng(Path.Combine(_output, filename)) == Error.Ok); _captures.Add(filename);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Verdant exploration check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "VerdantExplorationClientSmokePassed" : "VerdantExplorationClientSmokeFailed",
            passed,
            checks = _checks,
            captures = _captures,
            inputCommands = _commands.Count,
            campaignSetupCommands = _setupCommands,
            nativeFocusRecoveries = _focusRecoveries,
            replayCount = _replays.Count,
            routes = _routes,
            error,
            scope = "The shipping EndgameDirector receives native viewport clicks for Verdant passages, shrine treasure, hunt clues and local map navigation. Ordinary campaign commands earn Act I and Act II combat victories. F5/F9 exercise shipping persistence. Checks verify collision-safe movement, retained floor loot, single shrine reward, ordered tracking, adjacent backtracking, all five room maps, Rootheart completion and deterministic branch replays. No fabricated character, kills, loot, rewards or victories."
        };
        if (_writeReport && !passed && _director is not null && _sandbox is not null)
            System.IO.File.WriteAllText(Path.Combine(_output, "verdant-exploration-failed-state.json"), JsonData.Write(Session.Capture()));
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "verdant-exploration-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
