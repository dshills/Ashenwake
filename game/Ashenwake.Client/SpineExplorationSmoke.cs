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

/// <summary>Native travel through the shipping director earns and revisits the Shattered Spine's real rooms.</summary>
public partial class SpineExplorationSmoke : Node
{
    private const string Archive = "exploration.oathkeeper_archive", ArchiveEvent = "event.oathkeeper_archive";
    private const string Memory = "exploration.first_oath", MemoryEvent = "event.divine_memory";
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
            if (!args.Contains("--spine-exploration-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(path => !path.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Spine exploration smoke requires --spine-exploration-smoke --discipline=Vanguard --output=<fresh-artifact-directory>.");
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
            await ReachShatteredSpine();
            await ArchiveRoute();
            await MemoryRoute();
            await WardenAndBacktracking();
            RecordReplay();
            Check("native_exploration_branch_replays_verify", _replays.Count >= 5 && _replays.All(VerifyReplay));
            System.IO.File.WriteAllText(Path.Combine(_output, "spine-exploration-replays.json"), JsonData.Write(_replays));
            Finish(true, "");
        }
        catch (Exception exception)
        {
            try { await Capture("spine-exploration-failure.png"); } catch (Exception captureError) { GD.PushError(captureError.Message); }
            GD.PushError(exception.ToString()); Finish(false, exception.Message);
        }
    }

    private async Task ReachShatteredSpine()
    {
        for (int index = 0; Session.Campaign.ActiveEncounterId != "campaign.bone_causeway" && index < 24000; index++)
        {
            Execute(new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(Session.Campaign)), refresh: false);
            if (index % 120 == 0) { Refresh(); await Frames(); }
        }
        Refresh(); await CloseJourney(); await Frames();
        Check("real_campaign_unlocks_bone_causeway", Session.Campaign.ActiveEncounterId == "campaign.bone_causeway" &&
            Session.Campaign.Capture().Campaign.CompletedActs.Contains(3) && !Session.EncounterCleared);
        Check("bone_causeway_uses_authored_collision_and_its_own_map", Session.Room.Obstacles.Length > 0 &&
            _sandbox.EnvironmentStyle == "spine_causeway" && _map.RoomId == Map.RoomId && Map.RoomId == "campaign.bone_causeway");
        await Capture("spine-causeway-entered.png");
        await Fight("bone_causeway");
        Check("secured_bone_causeway_reveals_physical_archive_branch", Targets.Any(target => target.Id == "spine.archive.enter") &&
            Session.Combat.View.Loot.Count > 0);
        RecordReplay();
    }

    private async Task ArchiveRoute()
    {
        string causewayRoom = JsonData.Hash(Session.Room), causewayLoot = LootHash;
        var causewayDrops = Session.Combat.View.Loot.Select(loot => loot.Id).ToHashSet();
        await Marker("spine.archive.enter", "archive_enter", Archive);
        int[] causewayCells = Session.Capture().Campaign.ExplorationMap!.Rooms["campaign.bone_causeway"].SeenCells.ToArray();
        Check("archive_entry_starts_separate_authored_elite_room", Session.Campaign.Capture().Campaign.Exploration?.Id == ArchiveEvent &&
            Session.Room.Obstacles.Length > 0 && JsonData.Hash(Session.Room) != causewayRoom && Map.RoomId == Archive &&
            Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0));
        await Capture("spine-oathkeeper-archive-entered.png");
        await Fight("oathkeeper_archive");
        Check("archive_clearance_keeps_testament_unclaimed", !Session.Campaign.Capture().Campaign.CompletedExploration.Contains(ArchiveEvent) &&
            Targets.Any(target => target.Id == "spine.archive.treasure") && Session.Combat.View.Loot.Count > 0 &&
            Session.Combat.View.Loot.All(loot => !causewayDrops.Contains(loot.Id)));
        var testament = _stage.GetInteractionVisual("spine.archive.treasure");
        Check("archive_testament_exposes_visible_body_for_native_picking", testament is not null &&
            Descendants(testament).OfType<MeshInstance3D>().Any(mesh => mesh.IsVisibleInTree() && mesh.Mesh is not null));
        string archiveLoot = LootHash;
        var before = Session.Capture().Campaign.Production.Progression.Character;
        var itemIds = before.Items.Select(item => item.Id).ToHashSet();
        await Marker("spine.archive.treasure", "archive_claim", Archive);
        var claimed = Session.Capture().Campaign.Production.Progression.Character;
        var granted = claimed.Items.Where(item => !itemIds.Contains(item.Id)).ToArray();
        Check("archive_claim_grants_one_rare_vowkeepers_carapace_and_forty_materials", granted.Length == 1 &&
            granted[0].DefinitionId == "item.oath_plate" && granted[0].Rarity == ItemRarity.Rare && claimed.Materials == before.Materials + 40 &&
            claimed.OperationReceipts.ContainsKey("campaign.archive.testament"));
        Check("archive_claim_records_lore_without_collecting_floor_loot", Session.Campaign.Capture().Campaign.CompletedExploration.Contains(ArchiveEvent) &&
            Session.Campaign.Capture().Campaign.Discoveries.Contains("discovery.oathkeeper_archive") && LootHash == archiveLoot &&
            !Targets.Any(target => target.Id == "spine.archive.treasure"));
        RejectStaleArchiveClaim("claimed");
        await MapNavigation("archive");
        await KeyPress(Key.F5);
        Check("shipping_save_writes_claimed_archive_and_cached_causeway", System.IO.File.Exists(SavePath) &&
            Session.Capture().Campaign.ClearedRooms!.ContainsKey("campaign.bone_causeway"));
        string savedHash = Session.StateHash; var savedPlayer = Player; int[] savedCells = Map.SeenCells.ToArray();
        await Marker("spine.archive.return", "archive_return", "campaign.bone_causeway");
        Check("archive_return_restores_causeway_loot_and_exploration", LootHash == causewayLoot && JsonData.Hash(Session.Room) == causewayRoom &&
            causewayCells.All(Map.SeenCells.Contains) && NoIntent);
        RecordReplay();
        await KeyPress(Key.F9); await CloseJourney();
        Check("shipping_load_restores_claimed_archive_exactly", Session.StateHash == savedHash && Player == savedPlayer &&
            Map.RoomId == Archive && Map.SeenCells.SequenceEqual(savedCells) && LootHash == archiveLoot && NoIntent);
        RejectStaleArchiveClaim("loaded");
        await Capture("spine-oathkeeper-archive-claimed.png");
        await Marker("spine.archive.return", "archive_loaded_return", "campaign.bone_causeway");
        await Marker("spine.archive.enter", "archive_revisit", Archive);
        Check("archive_revisit_keeps_clearance_floor_loot_and_single_reward", Session.EncounterCleared && LootHash == archiveLoot &&
            !Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0) &&
            Session.Capture().Campaign.Production.Progression.Character.Items.Count(item => item.Id == granted[0].Id) == 1 &&
            Session.Capture().Campaign.Production.Progression.Character.Materials == claimed.Materials);
        RejectStaleArchiveClaim("revisited");
        await Marker("spine.archive.return", "archive_final_return", "campaign.bone_causeway");
        Check("archive_round_trips_preserve_all_causeway_drops", LootHash == causewayLoot && causewayDrops.All(id =>
            Descendants(_sandbox).OfType<LootVisual>().Any(visual => visual.Name == "Loot" + id && visual.IsVisibleInTree())));
        await Capture("spine-causeway-after-archive.png"); RecordReplay();
        await Marker("spine.forward.hall", "hall_enter", "campaign.contract_hall");
    }

    private async Task MemoryRoute()
    {
        Check("contract_hall_has_distinct_authored_ground_and_map", Session.Room.Obstacles.Length > 0 &&
            _sandbox.EnvironmentStyle == "spine_hall" && Map.RoomId == "campaign.contract_hall");
        await Fight("contract_hall");
        string hallLoot = LootHash, hallRoom = JsonData.Hash(Session.Room);
        await Capture("spine-contract-hall-secured.png");
        CheckChoiceGate();
        await Marker("spine.memory.enter", "memory_first_enter", Memory);
        Check("physical_memory_entrance_starts_authored_untimed_arena", Session.Campaign.Capture().Campaign.Exploration is { Id: MemoryEvent, RemainingTicks: 0 } &&
            Session.Combat.View.CampaignRule == "Memory" && Map.RoomId == Memory && _sandbox.EnvironmentStyle == "spine_memory" &&
            Session.Room.Obstacles.Length > 0 && JsonData.Hash(Session.Room) != hallRoom &&
            Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0));
        await ObserveMemoryWarnings();
        int materialsBeforeLeaving = Session.Capture().Campaign.Production.Progression.Character.Materials;
        await KeyPress(Key.F5);
        string savedHash = Session.StateHash; var savedPlayer = Player; int[] savedCells = Map.SeenCells.ToArray();
        string savedHazards = JsonData.Hash(Session.Combat.View.CampaignHazards);
        Check("shipping_save_writes_live_memory_and_cached_hall", System.IO.File.Exists(SavePath) &&
            Session.Capture().Campaign.ClearedRooms!.ContainsKey("campaign.contract_hall"));
        await Marker("spine.memory.return", "memory_partial_return", "campaign.contract_hall");
        Check("physical_exit_from_unfinished_memory_restores_hall_without_reward", Session.Campaign.Capture().Campaign.Exploration is null &&
            !Session.Campaign.Capture().Campaign.CompletedExploration.Contains(MemoryEvent) && LootHash == hallLoot &&
            Session.Capture().Campaign.Production.Progression.Character.Materials == materialsBeforeLeaving &&
            Session.Combat.View.CampaignRule != "Memory" && !(Session.Combat.View.CampaignHazards ?? []).Any() &&
            !Descendants(_sandbox).OfType<Label3D>().Any(label => label.Name.ToString().StartsWith("FaultSequence_", StringComparison.Ordinal)));
        RecordReplay();
        await Marker("spine.memory.enter", "memory_retry_enter", Memory);
        Check("unfinished_memory_retry_starts_fresh_untimed_population", Session.Campaign.Capture().Campaign.Exploration is { Id: MemoryEvent, RemainingTicks: 0 } &&
            Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0) &&
            Session.Combat.View.Actors.Where(actor => actor.Faction == CombatFaction.Enemy).All(actor => actor.Health == actor.MaxHealth));
        await KeyPress(Key.F9); await CloseJourney();
        Check("shipping_load_restores_live_memory_player_faults_and_fog_exactly", Session.StateHash == savedHash && Player == savedPlayer &&
            Map.RoomId == Memory && Map.SeenCells.SequenceEqual(savedCells) && NoIntent &&
            Session.Campaign.Capture().Campaign.Exploration is { Id: MemoryEvent, RemainingTicks: 0 } &&
            JsonData.Hash(Session.Combat.View.CampaignHazards) == savedHazards);
        await Fight("divine_memory");
        string memoryLoot = LootHash;
        Check("memory_victory_keeps_floor_drops_and_enables_physical_return", Session.Campaign.Capture().Campaign.Exploration is null &&
            Session.Campaign.Capture().Campaign.CompletedExploration.Contains(MemoryEvent) && Session.Combat.View.Loot.Count > 0 &&
            Targets.Any(target => target.Id == "spine.memory.return") && !(Session.Combat.View.CampaignHazards ?? []).Any());
        int materials = Session.Capture().Campaign.Production.Progression.Character.Materials;
        Check("memory_completion_awards_authored_materials_once", materials == materialsBeforeLeaving +
            Campaign.Capture().Exploration.Single(exploration => exploration.Id == MemoryEvent).Materials);
        await MapNavigation("memory"); await Capture("spine-first-oath-secured.png");
        await Marker("spine.memory.return", "memory_won_return", "campaign.contract_hall");
        Check("won_memory_return_restores_hall_loot", LootHash == hallLoot && JsonData.Hash(Session.Room) == hallRoom);
        await Marker("spine.memory.enter", "memory_won_revisit", Memory);
        Check("won_memory_revisit_retains_loot_and_fog_without_restarting_encounter", Session.EncounterCleared && LootHash == memoryLoot && Map.SeenCells.Count > 0 &&
            Session.Campaign.Capture().Campaign.Exploration is null && !Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0) &&
            !(Session.Combat.View.CampaignHazards ?? []).Any());
        await Marker("spine.memory.return", "memory_final_return", "campaign.contract_hall");
        Check("memory_revisit_cannot_duplicate_material_reward", Session.Capture().Campaign.Production.Progression.Character.Materials == materials && LootHash == hallLoot);
        await MapNavigation("contract-hall");
        await Capture("spine-contract-hall-memory-complete.png"); RecordReplay();
    }

    private async Task ObserveMemoryWarnings()
    {
        int deaths = Session.Campaign.Capture().Campaign.Deaths;
        for (int index = 0; index < 100 && !(Session.Combat.View.CampaignHazards ?? []).Any(hazard => hazard.ContentId == "rule.fault.1"); index++)
        {
            Execute(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)]), refresh: false);
        }
        Refresh(); await Frames();
        var faults = (Session.Combat.View.CampaignHazards ?? []).Where(hazard => hazard.ContentId.StartsWith("rule.fault.", StringComparison.Ordinal))
            .OrderBy(hazard => hazard.ContentId, StringComparer.Ordinal).ToArray();
        Check("live_memory_announces_reversed_fault_sequence_without_countdown", faults.Length == 3 &&
            faults.Select(hazard => hazard.Position.Z).SequenceEqual(new[] { 3000, 0, -3000 }) &&
            Session.Campaign.Capture().Campaign.Exploration is { RemainingTicks: 0 } && Session.Campaign.Capture().Campaign.Deaths == deaths);
        var labels = Descendants(_sandbox).OfType<Label3D>().Where(label => label.Name.ToString().StartsWith("FaultSequence_", StringComparison.Ordinal)).ToArray();
        Check("shipping_memory_numbers_match_all_actual_fault_warnings", labels.Length == 3 && faults.All(hazard =>
            labels.SingleOrDefault(label => label.Name == "FaultSequence_" + hazard.Id) is { } label &&
            label.Text == hazard.ContentId[^1..] && label.IsVisibleInTree() && label.NoDepthTest));
        await Capture("spine-first-oath-fault-sequence.png");
        await KeyPress(Key.M); string hash = Session.StateHash; long tick = Session.Tick;
        Ticks(5); await Frames();
        Check("native_memory_map_pauses_actual_faults_without_advancing_simulation", _map.Expanded && _sandbox.IsPaused && Session.StateHash == hash && Session.Tick == tick);
        await KeyPress(Key.M);
        Check("closing_memory_map_restores_play_and_clears_pending_input", !_map.Expanded && !_sandbox.IsPaused && NoIntent);
    }

    private void CheckChoiceGate()
    {
        string hash = Session.StateHash;
        Check("oath_choice_hides_warden_forward_marker", !Targets.Any(target => target.Id == "spine.forward.warden") &&
            !_sandbox.RequestWorldInteraction("spine.forward.warden") && Session.StateHash == hash);
        var rejected = Session.Execute(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.InteractSpine, Id: "spine.forward.warden")));
        Check("unresolved_oath_choice_cannot_bypass_physical_gate", !rejected.Success && Session.StateHash == hash);
        Refresh();
    }

    private async Task WardenAndBacktracking()
    {
        var definition = Field<CampaignContent>(_director, "_campaign").Capture();
        var choice = definition.Choices.Single(candidate => candidate.Id == definition.Acts[3].RequiredChoice);
        if (!Session.Campaign.Capture().Campaign.Choices.ContainsKey(choice.Id))
            Execute(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.Choose, Id: choice.Id, Value: choice.Outcomes[0].Id)));
        await CloseJourney(); await Marker("spine.forward.warden", "covenant_warden_enter", "campaign.covenant_warden");
        Check("covenant_warden_has_authored_sanctum_and_map", Session.Room.Obstacles.Length > 0 && _sandbox.EnvironmentStyle == "spine_warden" && Map.RoomId == "campaign.covenant_warden");
        await Capture("spine-covenant-warden-entered.png");
        await Fight("covenant_warden"); string wardenLoot = LootHash;
        Check("real_covenant_warden_victory_completes_act_four", Session.Campaign.Capture().Campaign.CompletedActs.Contains(4) &&
            Session.Combat.View.Actors.Any(actor => actor.DefinitionId == "boss.covenant_warden" && actor.Health <= 0));
        await Capture("spine-covenant-warden-defeated.png");
        await Marker("spine.back.hall", "covenant_warden_backtrack", "campaign.contract_hall");
        await Marker("spine.back.causeway", "hall_backtrack", "campaign.bone_causeway");
        await Marker("spine.forward.hall", "causeway_revisit_hall", "campaign.contract_hall");
        await Marker("spine.forward.warden", "hall_revisit_warden", "campaign.covenant_warden");
        Check("adjacent_backtracking_preserves_covenant_warden_victory_and_floor_loot", Session.EncounterCleared && LootHash == wardenLoot &&
            !Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0));
        var atlas = Session.Capture().Campaign.ExplorationMap!.Rooms;
        Check("all_five_spine_rooms_remember_discovered_terrain", new[] { "campaign.bone_causeway", "campaign.contract_hall", "campaign.covenant_warden", Archive, Memory }
            .All(id => atlas.TryGetValue(id, out var room) && room.SeenCells.Length > 0));
        await KeyPress(Key.M); await Capture("spine-covenant-warden-local-map.png"); await KeyPress(Key.F5);
        string hash = Session.StateHash;
        var loaded = EndgameRuntimeSaveStore.Load(SavePath, CombatJson, Adventure, Progression, Campaign, Endgame).Session;
        Check("complete_region_save_roundtrip_preserves_maps_and_cached_rooms", loaded.StateHash == hash &&
            loaded.Capture().Campaign.ClearedRooms!.Keys.Count(id => SpineCampaignLayout.Contains(id)) == 4);
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
                frame.Command.Campaign is { Action: CampaignRuntimeAction.InteractSpine } action && action.Id == id));
            Check(label + "_approach_never_attacks_or_collects_floor_loot", !_commands.Skip(commands).Any(command => command.Kind is CombatCommandKind.Cast or CombatCommandKind.Pickup));
            _routes.Add($"{id}: from {start} through {target.Position}, arrived in {Session.Campaign.ActiveEncounterId} at {Player}");
            return;
        }
        throw new InvalidDataException("Repeated native focus interruptions prevented passage " + id + ".");
    }

    private void RejectStaleArchiveClaim(string label)
    {
        string hash = Session.StateHash;
        Check(label + "_stale_archive_marker_is_unavailable", !_sandbox.RequestWorldInteraction("spine.archive.treasure") && NoIntent && Session.StateHash == hash);
        var result = Session.Execute(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.InteractSpine, Id: "spine.archive.treasure")));
        Check(label + "_stale_archive_claim_is_transactionally_rejected", !result.Success && Session.StateHash == hash);
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
        await Capture("spine-" + label + "-map.png");
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
                throw new InvalidDataException("Native Spine movement entered an authoritative solid.");
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
        if (!result.Success) throw new InvalidDataException("Spine setup command failed: " + result.Reason);
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
        if (!_writeReport || !OS.GetCmdlineUserArgs().Contains("--capture-spine-exploration") || DisplayServer.GetName() == "headless") return;
        await Frames(3); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + filename, image.GetWidth() > 0 && image.SavePng(Path.Combine(_output, filename)) == Error.Ok); _captures.Add(filename);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Spine exploration check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "SpineExplorationClientSmokePassed" : "SpineExplorationClientSmokeFailed",
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
            scope = "The shipping EndgameDirector receives native viewport clicks for Spine passages, archive treasure, physical memory exits and local map navigation. Ordinary campaign commands earn Acts I–IV combat victories. F5/F9 exercise shipping persistence, including live reversed fault warnings. Checks verify collision-safe movement, retained floor loot, a single archive reward, untimed Memory departure, retry and victory, the oath choice gate, adjacent backtracking, five room maps, Warden completion and deterministic branch replays. No fabricated character, kills, loot, rewards or victories."
        };
        if (_writeReport && !passed && _director is not null && _sandbox is not null)
            System.IO.File.WriteAllText(Path.Combine(_output, "spine-exploration-failed-state.json"), JsonData.Write(Session.Capture()));
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "spine-exploration-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
