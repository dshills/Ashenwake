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

/// <summary>Native travel through the shipping director earns and revisits the Cinder Reach's real rooms.</summary>
public partial class CinderExplorationSmoke : Node
{
    private const string Foundry = "exploration.sealed_foundry", FoundryEvent = "event.sealed_foundry";
    private const string Storm = "exploration.burning_rain", StormEvent = "event.resonance_storm";
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [], _routes = [];
    private readonly List<CombatCommand> _commands = [];
    private readonly List<EndgameRuntimeReplay> _replays = [];
    private readonly List<FocusEvidence> _focusEvidence = [];
    private sealed record FocusEvidence(int Recovery, long BeforeTick, long AfterTick, bool CoreUnchanged, bool Paused, bool NoPendingIntent, int AdvanceCalls);
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
            if (!args.Contains("--cinder-exploration-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(path => !path.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Cinder exploration smoke requires --cinder-exploration-smoke --discipline=Vanguard --output=<fresh-artifact-directory>.");
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
            await ReachCinderReach();
            await FoundryRoute();
            await StormRoute();
            await FurnaceAndBacktracking();
            RecordReplay();
            Check("native_exploration_branch_replays_verify", _replays.Count >= 5 && _replays.All(VerifyReplay));
            System.IO.File.WriteAllText(Path.Combine(_output, "cinder-exploration-replays.json"), JsonData.Write(_replays));
            Finish(true, "");
        }
        catch (Exception exception)
        {
            try { await Capture("cinder-exploration-failure.png"); } catch (Exception captureError) { GD.PushError(captureError.Message); }
            GD.PushError(exception.ToString()); Finish(false, exception.Message);
        }
    }

    private async Task ReachCinderReach()
    {
        for (int index = 0; Session.Campaign.ActiveEncounterId != "campaign.cinder_pack" && index < 18000; index++)
        {
            Execute(new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(Session.Campaign)), refresh: false);
            if (index % 120 == 0) { Refresh(); await Frames(); }
        }
        Refresh(); await CloseJourney(); await Frames();
        Check("real_campaign_unlocks_cinder_pack", Session.Campaign.ActiveEncounterId == "campaign.cinder_pack" &&
            Session.Campaign.Capture().Campaign.CompletedActs.Contains(2) && !Session.EncounterCleared);
        Check("cinder_pack_uses_authored_collision_and_its_own_map", Session.Room.Obstacles.Length > 0 &&
            _sandbox.EnvironmentStyle == "cinder_fields" && _map.RoomId == Map.RoomId && Map.RoomId == "campaign.cinder_pack");
        await Capture("cinder-fields-entered.png");
        await Fight("cinder_pack");
        Check("secured_cinder_pack_reveals_physical_foundry_branch", Targets.Any(target => target.Id == "cinder.foundry.enter") &&
            Session.Combat.View.Loot.Count > 0);
        RecordReplay();
    }

    private async Task FoundryRoute()
    {
        string fieldsRoom = JsonData.Hash(Session.Room), fieldsLoot = LootHash;
        var fieldsDrops = Session.Combat.View.Loot.Select(loot => loot.Id).ToHashSet();
        await Marker("cinder.foundry.enter", "foundry_enter", Foundry);
        int[] fieldsCells = Session.Capture().Campaign.ExplorationMap!.Rooms["campaign.cinder_pack"].SeenCells.ToArray();
        Check("foundry_entry_starts_separate_authored_elite_room", Session.Campaign.Capture().Campaign.Exploration?.Id == FoundryEvent &&
            Session.Room.Obstacles.Length > 0 && JsonData.Hash(Session.Room) != fieldsRoom && Map.RoomId == Foundry &&
            Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0));
        await Capture("cinder-sealed-foundry-entered.png");
        await Fight("sealed_foundry");
        Check("foundry_clearance_keeps_testament_unclaimed", !Session.Campaign.Capture().Campaign.CompletedExploration.Contains(FoundryEvent) &&
            Targets.Any(target => target.Id == "cinder.foundry.treasure") && Session.Combat.View.Loot.Count > 0 &&
            Session.Combat.View.Loot.All(loot => !fieldsDrops.Contains(loot.Id)));
        var testament = _stage.GetInteractionVisual("cinder.foundry.treasure");
        Check("foundry_testament_exposes_visible_body_for_native_picking", testament is not null &&
            Descendants(testament).OfType<MeshInstance3D>().Any(mesh => mesh.IsVisibleInTree() && mesh.Mesh is not null));
        string foundryLoot = LootHash;
        var before = Session.Capture().Campaign.Production.Progression.Character;
        var itemIds = before.Items.Select(item => item.Id).ToHashSet();
        await Marker("cinder.foundry.treasure", "foundry_claim", Foundry);
        var claimed = Session.Capture().Campaign.Production.Progression.Character;
        var granted = claimed.Items.Where(item => !itemIds.Contains(item.Id)).ToArray();
        Check("foundry_claim_grants_one_rare_cinderwake_saber_and_thirty_five_materials", granted.Length == 1 &&
            granted[0].DefinitionId == "item.cinder_edge" && granted[0].Rarity == ItemRarity.Rare && claimed.Materials == before.Materials + 35 &&
            claimed.OperationReceipts.ContainsKey("campaign.foundry.testament"));
        Check("foundry_claim_records_lore_without_collecting_floor_loot", Session.Campaign.Capture().Campaign.CompletedExploration.Contains(FoundryEvent) &&
            Session.Campaign.Capture().Campaign.Discoveries.Contains("discovery.sealed_foundry") && LootHash == foundryLoot &&
            !Targets.Any(target => target.Id == "cinder.foundry.treasure"));
        RejectStaleFoundryClaim("claimed");
        await MapNavigation("foundry");
        await KeyPress(Key.F5);
        Check("shipping_save_writes_claimed_foundry_and_cached_fields", System.IO.File.Exists(SavePath) &&
            Session.Capture().Campaign.ClearedRooms!.ContainsKey("campaign.cinder_pack"));
        string savedHash = Session.StateHash; var savedPlayer = Player; int[] savedCells = Map.SeenCells.ToArray();
        await Marker("cinder.foundry.return", "foundry_return", "campaign.cinder_pack");
        Check("foundry_return_restores_fields_loot_and_exploration", LootHash == fieldsLoot && JsonData.Hash(Session.Room) == fieldsRoom &&
            fieldsCells.All(Map.SeenCells.Contains) && NoIntent);
        RecordReplay();
        await KeyPress(Key.F9); await CloseJourney();
        Check("shipping_load_restores_claimed_foundry_exactly", Session.StateHash == savedHash && Player == savedPlayer &&
            Map.RoomId == Foundry && Map.SeenCells.SequenceEqual(savedCells) && LootHash == foundryLoot && NoIntent);
        RejectStaleFoundryClaim("loaded");
        await Capture("cinder-sealed-foundry-claimed.png");
        await Marker("cinder.foundry.return", "foundry_loaded_return", "campaign.cinder_pack");
        await Marker("cinder.foundry.enter", "foundry_revisit", Foundry);
        Check("foundry_revisit_keeps_clearance_floor_loot_and_single_reward", Session.EncounterCleared && LootHash == foundryLoot &&
            !Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0) &&
            Session.Capture().Campaign.Production.Progression.Character.Items.Count(item => item.Id == granted[0].Id) == 1 &&
            Session.Capture().Campaign.Production.Progression.Character.Materials == claimed.Materials);
        RejectStaleFoundryClaim("revisited");
        await Marker("cinder.foundry.return", "foundry_final_return", "campaign.cinder_pack");
        Check("foundry_round_trips_preserve_all_fields_drops", LootHash == fieldsLoot && fieldsDrops.All(id =>
            Descendants(_sandbox).OfType<LootVisual>().Any(visual => visual.Name == "Loot" + id && visual.IsVisibleInTree())));
        await Capture("cinder-fields-after-foundry.png"); RecordReplay();
        await Marker("cinder.forward.floor", "floor_enter", "campaign.extraction_floor");
    }

    private async Task StormRoute()
    {
        Check("extraction_floor_has_distinct_authored_ground_and_map", Session.Room.Obstacles.Length > 0 &&
            _sandbox.EnvironmentStyle == "cinder_extraction" && Map.RoomId == "campaign.extraction_floor");
        await Fight("extraction_floor");
        string floorLoot = LootHash, floorRoom = JsonData.Hash(Session.Room);
        await Capture("cinder-extraction-floor-secured.png");
        CheckChoiceGate();
        await Marker("cinder.storm.enter", "storm_first_enter", Storm);
        Check("physical_storm_entrance_starts_authored_timed_arena", Session.Campaign.Capture().Campaign.Exploration is { Id: StormEvent, RemainingTicks: > 0 } &&
            Map.RoomId == Storm && _sandbox.EnvironmentStyle == "cinder_storm" && Session.Room.Obstacles.Length > 0 &&
            JsonData.Hash(Session.Room) != floorRoom && Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0));
        await Capture("cinder-resonance-storm-entered.png");
        int materialsBeforeLeaving = Session.Capture().Campaign.Production.Progression.Character.Materials;
        await Marker("cinder.storm.return", "storm_partial_return", "campaign.extraction_floor");
        Check("physical_exit_from_unfinished_storm_restores_floor_without_reward", Session.Campaign.Capture().Campaign.Exploration is null &&
            !Session.Campaign.Capture().Campaign.CompletedExploration.Contains(StormEvent) && LootHash == floorLoot &&
            Session.Capture().Campaign.Production.Progression.Character.Materials == materialsBeforeLeaving);
        await Marker("cinder.storm.enter", "storm_retry_enter", Storm);
        Check("unfinished_storm_retry_starts_fresh_timer_and_population", Session.Campaign.Capture().Campaign.Exploration is { RemainingTicks: > 850 } &&
            Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0));
        await KeyPress(Key.F5);
        string savedHash = Session.StateHash; var savedPlayer = Player; int[] savedCells = Map.SeenCells.ToArray();
        int savedTimer = Session.Campaign.Capture().Campaign.Exploration!.RemainingTicks;
        Check("shipping_save_writes_live_storm_and_cached_floor", System.IO.File.Exists(SavePath) && Session.Capture().Campaign.ClearedRooms!.ContainsKey("campaign.extraction_floor"));
        await ExpireStorm();
        Check("timer_expiry_restores_exact_parent_room_and_loot_without_reward", Session.Campaign.ActiveEncounterId == "campaign.extraction_floor" &&
            JsonData.Hash(Session.Room) == floorRoom && LootHash == floorLoot && _sandbox.EnvironmentStyle == "cinder_extraction" &&
            Session.Capture().Campaign.Production.Progression.Character.Materials == materialsBeforeLeaving &&
            !Session.Campaign.Capture().Campaign.CompletedExploration.Contains(StormEvent) && !(Session.Combat.View.CampaignHazards ?? []).Any());
        await Capture("cinder-extraction-after-storm-expiry.png"); RecordReplay();
        await KeyPress(Key.F9); await CloseJourney();
        Check("shipping_load_restores_live_storm_timer_player_and_fog_exactly", Session.StateHash == savedHash && Player == savedPlayer &&
            Map.RoomId == Storm && Map.SeenCells.SequenceEqual(savedCells) && NoIntent &&
            Session.Campaign.Capture().Campaign.Exploration?.RemainingTicks == savedTimer);
        await Fight("resonance_storm");
        string stormLoot = LootHash;
        Check("storm_victory_stops_timer_keeps_floor_drops_and_enables_physical_return", Session.Campaign.Capture().Campaign.Exploration is null &&
            Session.Campaign.Capture().Campaign.CompletedExploration.Contains(StormEvent) && Session.Combat.View.Loot.Count > 0 &&
            Targets.Any(target => target.Id == "cinder.storm.return") && !(Session.Combat.View.CampaignHazards ?? []).Any());
        int materials = Session.Capture().Campaign.Production.Progression.Character.Materials;
        Check("storm_completion_awards_authored_materials_once", materials == materialsBeforeLeaving +
            Campaign.Capture().Exploration.Single(exploration => exploration.Id == StormEvent).Materials);
        await MapNavigation("storm"); await Capture("cinder-resonance-storm-secured.png");
        await Marker("cinder.storm.return", "storm_won_return", "campaign.extraction_floor");
        Check("won_storm_return_restores_floor_loot", LootHash == floorLoot && JsonData.Hash(Session.Room) == floorRoom);
        await Marker("cinder.storm.enter", "storm_won_revisit", Storm);
        Check("won_storm_revisit_retains_loot_and_fog_without_timer_or_enemies", Session.EncounterCleared && LootHash == stormLoot && Map.SeenCells.Count > 0 &&
            Session.Campaign.Capture().Campaign.Exploration is null && !Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0) &&
            !(Session.Combat.View.CampaignHazards ?? []).Any());
        await Marker("cinder.storm.return", "storm_final_return", "campaign.extraction_floor");
        Check("storm_revisit_cannot_duplicate_material_reward", Session.Capture().Campaign.Production.Progression.Character.Materials == materials && LootHash == floorLoot);
        await MapNavigation("extraction-floor");
        await Capture("cinder-extraction-storm-complete.png"); RecordReplay();
    }

    private void CheckChoiceGate()
    {
        string hash = Session.StateHash;
        Check("extraction_choice_hides_furnace_forward_marker", !Targets.Any(target => target.Id == "cinder.forward.spindle") &&
            !_sandbox.RequestWorldInteraction("cinder.forward.spindle") && Session.StateHash == hash);
        var rejected = Session.Execute(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.InteractCinder, Id: "cinder.forward.spindle")));
        Check("unresolved_extraction_choice_cannot_bypass_physical_gate", !rejected.Success && Session.StateHash == hash);
        Refresh();
    }

    private async Task ExpireStorm()
    {
        int remaining = Session.Campaign.Capture().Campaign.Exploration!.RemainingTicks;
        int deaths = Session.Campaign.Capture().Campaign.Deaths;
        bool announcedLane = false;
        for (int index = 0; index < remaining; index++)
        {
            // Real movement/potion/dodge inputs preserve the enemy population so this saved
            // branch exercises the runtime timer, not an early victory or fabricated state.
            var view = Session.Combat.View; var player = view.Actors.Single(actor => actor.Id == 1); var room = Session.Room;
            CorePosition[] corners = [new(-room.HalfWidth + 1300, -room.HalfDepth + 1300), new(room.HalfWidth - 1300, -room.HalfDepth + 1300),
                new(room.HalfWidth - 1300, room.HalfDepth - 1300), new(-room.HalfWidth + 1300, room.HalfDepth - 1300)];
            var target = corners[(index / 160) % corners.Length];
            var occupied = view.Actors.Where(actor => actor.Id != 1 && actor.Health > 0).Select(actor => actor.Position).ToArray();
            var direction = CombatProductionSmoke.MovementDirection(player.Position, target, room, occupied);
            var commands = new List<CombatCommand> { new(CombatCommandKind.Move, X: direction.X, Z: direction.Z) };
            if (player.Health < player.MaxHealth / 2 && view.PotionCharges > 0 && view.PotionCooldownTicks == 0) commands.Add(new(CombatCommandKind.Potion));
            if (view.DodgeCooldownTicks == 0 && (view.CampaignHazards ?? []).Any(hazard => hazard.RemainingTicks <= 12 && CombatSession.HazardContains(hazard, player.Position)))
                commands.Add(new(CombatCommandKind.Dodge, X: direction.X, Z: direction.Z));
            announcedLane |= (view.CampaignHazards ?? []).Any(hazard => hazard.ContentId == "rule.storm" && hazard.RemainingTicks > 0);
            Execute(new(EndgameRuntimeAction.Tick, Commands: commands.ToArray()), refresh: false);
            if (index < remaining - 1)
                Check("storm_timer_retains_live_arena_until_deadline", Session.Campaign.Capture().Campaign.Exploration?.RemainingTicks == remaining - index - 1);
            if (index % 120 == 0) { Refresh(); await Frames(); }
        }
        Refresh(); await CloseJourney();
        Check("storm_expires_after_real_ticks_without_death_or_victory", announcedLane && Session.Campaign.Capture().Campaign.Deaths == deaths &&
            Session.Campaign.Capture().Campaign.Exploration is null && Session.Campaign.ActiveEncounterId == "campaign.extraction_floor" &&
            !Session.Campaign.Capture().Campaign.CompletedExploration.Contains(StormEvent) && NoIntent);
    }

    private async Task FurnaceAndBacktracking()
    {
        var definition = Field<CampaignContent>(_director, "_campaign").Capture();
        var choice = definition.Choices.Single(candidate => candidate.Id == definition.Acts[2].RequiredChoice);
        if (!Session.Campaign.Capture().Campaign.Choices.ContainsKey(choice.Id))
            Execute(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.Choose, Id: choice.Id, Value: choice.Outcomes[0].Id)));
        await CloseJourney(); await Marker("cinder.forward.spindle", "furnace_spindle_enter", "campaign.furnace_spindle");
        Check("furnace_spindle_has_authored_sanctum_and_map", Session.Room.Obstacles.Length > 0 && _sandbox.EnvironmentStyle == "cinder_furnace" && Map.RoomId == "campaign.furnace_spindle");
        await Capture("cinder-furnace-spindle-entered.png");
        await Fight("furnace_spindle"); string spindleLoot = LootHash;
        Check("real_furnace_spindle_victory_completes_act_three", Session.Campaign.Capture().Campaign.CompletedActs.Contains(3) &&
            Session.Combat.View.Actors.Any(actor => actor.DefinitionId == "boss.furnace_spindle" && actor.Health <= 0));
        await Capture("cinder-furnace-spindle-defeated.png");
        await Marker("cinder.back.floor", "furnace_spindle_backtrack", "campaign.extraction_floor");
        await Marker("cinder.back.fields", "floor_backtrack", "campaign.cinder_pack");
        await Marker("cinder.forward.floor", "fields_revisit_floor", "campaign.extraction_floor");
        await Marker("cinder.forward.spindle", "floor_revisit_spindle", "campaign.furnace_spindle");
        Check("adjacent_backtracking_preserves_furnace_spindle_victory_and_floor_loot", Session.EncounterCleared && LootHash == spindleLoot &&
            !Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0));
        var atlas = Session.Capture().Campaign.ExplorationMap!.Rooms;
        Check("all_five_cinder_rooms_remember_discovered_terrain", new[] { "campaign.cinder_pack", "campaign.extraction_floor", "campaign.furnace_spindle", Foundry, Storm }
            .All(id => atlas.TryGetValue(id, out var room) && room.SeenCells.Length > 0));
        await KeyPress(Key.M); await Capture("cinder-furnace-spindle-local-map.png"); await KeyPress(Key.F5);
        string hash = Session.StateHash;
        var loaded = EndgameRuntimeSaveStore.Load(SavePath, CombatJson, Adventure, Progression, Campaign, Endgame).Session;
        Check("complete_region_save_roundtrip_preserves_maps_and_cached_rooms", loaded.StateHash == hash &&
            loaded.Capture().Campaign.ClearedRooms!.Keys.Count(id => CinderCampaignLayout.Contains(id)) == 4);
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
            var replayBefore = Session.CaptureReplay();
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
            var replayAfter = Session.CaptureReplay();
            // The runtime checkpoints after 1,800 frames. If travel crossed that boundary,
            // the new checkpoint's frames all belong to this approach.
            var arrivalFrames = replayAfter.Initial.OperationSequence == replayBefore.Initial.OperationSequence
                ? replayAfter.Frames.Skip(replayBefore.Frames.Length) : replayAfter.Frames;
            Check(label + "_arrival_records_exact_physical_action", arrivalFrames.Any(frame =>
                frame.Command.Campaign is { Action: CampaignRuntimeAction.InteractCinder } action && action.Id == id));
            Check(label + "_approach_never_attacks_or_collects_floor_loot", !_commands.Skip(commands).Any(command => command.Kind is CombatCommandKind.Cast or CombatCommandKind.Pickup));
            _routes.Add($"{id}: from {start} through {target.Position}, arrived in {Session.Campaign.ActiveEncounterId} at {Player}");
            return;
        }
        throw new InvalidDataException("Repeated native focus interruptions prevented passage " + id + ".");
    }

    private void RejectStaleFoundryClaim(string label)
    {
        string hash = Session.StateHash;
        Check(label + "_stale_foundry_marker_is_unavailable", !_sandbox.RequestWorldInteraction("cinder.foundry.treasure") && NoIntent && Session.StateHash == hash);
        var result = Session.Execute(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.InteractCinder, Id: "cinder.foundry.treasure")));
        Check(label + "_stale_foundry_claim_is_transactionally_rejected", !result.Success && Session.StateHash == hash);
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
        await Capture("cinder-" + label + "-map.png");
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
                throw new InvalidDataException("Native Cinder movement entered an authoritative solid.");
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
        if (!result.Success) throw new InvalidDataException("Cinder setup command failed: " + result.Reason);
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
        string hash = Session.StateHash; long tick = Session.Tick;
        Check("focus_interruption_" + ++_focusRecoveries + "_cancels_pending_world_input", _sandbox.IsPaused && NoIntent);
        var advance = _sandbox.AdvanceOverride; bool processing = _sandbox.IsProcessing(), automatic = _sandbox.AutomaticStep;
        int advanceCalls = 0;
        _sandbox.SetProcess(false); _sandbox.AutomaticStep = false;
        _sandbox.AdvanceOverride = _ => { advanceCalls++; return []; };
        try
        {
            GetWindow().GrabFocus(); await Frames();
            // Focus recovery is housekeeping between explicit simulation ticks. Activate the
            // real Resume control through its keyboard focus: a synthetic mouse click can
            // hit newly exposed world ground while native focus events are being drained.
            var resume = Descendants(_director).OfType<Button>().Single(button => button.Text == "Resume playing" && button.IsVisibleInTree());
            resume.GrabFocus();
            foreach (bool pressed in new[] { true, false })
                GetViewport().PushInput(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = pressed }, true);
            await Frames();
            _focusEvidence.Add(new(_focusRecoveries, tick, Session.Tick, Session.StateHash == hash, _sandbox.IsPaused, NoIntent, advanceCalls));
            Check("focus_recovery_" + _focusRecoveries + "_does_not_request_a_simulation_step", advanceCalls == 0);
            Check("focus_recovery_" + _focusRecoveries + "_uses_resume_without_mutating_world", !_sandbox.IsPaused && NoIntent && Session.StateHash == hash);
        }
        finally { _sandbox.AdvanceOverride = advance; _sandbox.AutomaticStep = automatic; _sandbox.SetProcess(processing); }
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
        if (!_writeReport || !OS.GetCmdlineUserArgs().Contains("--capture-cinder-exploration") || DisplayServer.GetName() == "headless") return;
        await Frames(3); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + filename, image.GetWidth() > 0 && image.SavePng(Path.Combine(_output, filename)) == Error.Ok); _captures.Add(filename);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Cinder exploration check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "CinderExplorationClientSmokePassed" : "CinderExplorationClientSmokeFailed",
            passed,
            checks = _checks,
            captures = _captures,
            inputCommands = _commands.Count,
            campaignSetupCommands = _setupCommands,
            nativeFocusRecoveries = _focusRecoveries,
            focusEvidence = _focusEvidence,
            replayCount = _replays.Count,
            routes = _routes,
            error,
            scope = "The shipping EndgameDirector receives native viewport clicks for Cinder passages, foundry treasure, physical storm exits and local map navigation. Ordinary campaign commands earn Acts I–III combat victories. F5/F9 exercise shipping persistence, including a live storm timer. Checks verify collision-safe movement, retained floor loot, a single foundry reward, real storm expiry and victory, the extraction choice gate, adjacent backtracking, five room maps, Furnace completion and deterministic branch replays. No fabricated character, kills, loot, rewards or victories."
        };
        if (_writeReport && !passed && _director is not null && _sandbox is not null)
            System.IO.File.WriteAllText(Path.Combine(_output, "cinder-exploration-failed-state.json"), JsonData.Write(Session.Capture()));
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "cinder-exploration-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
