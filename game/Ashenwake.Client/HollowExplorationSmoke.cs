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

/// <summary>Native travel through the shipping director earns and revisits the Hollow Night's real rooms.</summary>
public partial class HollowExplorationSmoke : Node
{
    private const string Vault = "exploration.unremembered_vault", VaultEvent = "event.unremembered_vault";
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [], _routes = [];
    private readonly List<CombatCommand> _commands = [];
    private readonly List<EndgameRuntimeReplay> _replays = [];
    private readonly List<EndingEvidence> _endings = [];
    private sealed record EndingEvidence(string Choice, string EndingId, string StateHash, int LivingSeals, bool FracturesUnlocked);
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
            if (!args.Contains("--hollow-exploration-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(path => !path.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Hollow exploration smoke requires --hollow-exploration-smoke --discipline=Vanguard --output=<fresh-artifact-directory>.");
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
            await ReachHollowNight();
            await VaultRoute();
            await IdentityAndEndings();
            RecordReplay();
            Check("native_exploration_branch_replays_verify", _replays.Count >= 5 && _replays.All(VerifyReplay));
            System.IO.File.WriteAllText(Path.Combine(_output, "hollow-exploration-replays.json"), JsonData.Write(_replays));
            Finish(true, "");
        }
        catch (Exception exception)
        {
            try { await Capture("hollow-exploration-failure.png"); } catch (Exception captureError) { GD.PushError(captureError.Message); }
            GD.PushError(exception.ToString()); Finish(false, exception.Message);
        }
    }

    private async Task ReachHollowNight()
    {
        for (int index = 0; Session.Campaign.ActiveEncounterId != "campaign.repeating_rooms" && index < 32000; index++)
        {
            Execute(new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(Session.Campaign)), refresh: false);
            if (index % 120 == 0) { Refresh(); await Frames(); }
        }
        Refresh(); await CloseJourney(); await Frames();
        Check("real_campaign_unlocks_repeating_rooms", Session.Campaign.ActiveEncounterId == "campaign.repeating_rooms" &&
            Session.Campaign.Capture().Campaign.CompletedActs.Contains(4) && !Session.EncounterCleared);
        Check("repeating_rooms_uses_authored_collision_and_its_own_map", Session.Room.Obstacles.Length > 0 &&
            _sandbox.EnvironmentStyle == "hollow_rooms" && _map.RoomId == Map.RoomId && Map.RoomId == "campaign.repeating_rooms");
        await Capture("hollow-rooms-entered.png");
        await Fight("repeating_rooms");
        Check("secured_repeating_rooms_reveals_physical_vault_branch", Targets.Any(target => target.Id == "hollow.vault.enter") &&
            Session.Combat.View.Loot.Count > 0);
        RecordReplay();
    }

    private async Task VaultRoute()
    {
        string roomsRoom = JsonData.Hash(Session.Room), roomsLoot = LootHash;
        var roomsDrops = Session.Combat.View.Loot.Select(loot => loot.Id).ToHashSet();
        await Marker("hollow.vault.enter", "vault_enter", Vault);
        int[] roomsCells = Session.Capture().Campaign.ExplorationMap!.Rooms["campaign.repeating_rooms"].SeenCells.ToArray();
        Check("vault_entry_starts_separate_authored_elite_room", Session.Campaign.Capture().Campaign.Exploration?.Id == VaultEvent &&
            Session.Room.Obstacles.Length > 0 && JsonData.Hash(Session.Room) != roomsRoom && Map.RoomId == Vault &&
            Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0));
        await Capture("hollow-unremembered-vault-entered.png");
        await Fight("unremembered_vault");
        Check("vault_clearance_keeps_testament_unclaimed", !Session.Campaign.Capture().Campaign.CompletedExploration.Contains(VaultEvent) &&
            Targets.Any(target => target.Id == "hollow.vault.treasure") && Session.Combat.View.Loot.Count > 0 &&
            Session.Combat.View.Loot.All(loot => !roomsDrops.Contains(loot.Id)));
        var testament = _stage.GetInteractionVisual("hollow.vault.treasure");
        Check("vault_testament_exposes_visible_body_for_native_picking", testament is not null &&
            Descendants(testament).OfType<MeshInstance3D>().Any(mesh => mesh.IsVisibleInTree() && mesh.Mesh is not null));
        string vaultLoot = LootHash;
        var before = Session.Capture().Campaign.Production.Progression.Character;
        var itemIds = before.Items.Select(item => item.Id).ToHashSet();
        await Marker("hollow.vault.treasure", "vault_claim", Vault);
        var claimed = Session.Capture().Campaign.Production.Progression.Character;
        var granted = claimed.Items.Where(item => !itemIds.Contains(item.Id)).ToArray();
        Check("vault_claim_grants_one_legendary_choir_of_the_unburied_and_forty_five_materials", granted.Length == 1 &&
            granted[0].DefinitionId == "item.echo_ring" && granted[0].Rarity == ItemRarity.Legendary && claimed.Materials == before.Materials + 45 &&
            claimed.OperationReceipts.ContainsKey("campaign.vault.testament"));
        Check("vault_claim_records_lore_without_collecting_floor_loot", Session.Campaign.Capture().Campaign.CompletedExploration.Contains(VaultEvent) &&
            Session.Campaign.Capture().Campaign.Discoveries.Contains("discovery.unremembered_vault") && LootHash == vaultLoot &&
            !Targets.Any(target => target.Id == "hollow.vault.treasure"));
        RejectStaleVaultClaim("claimed");
        await MapNavigation("vault");
        await KeyPress(Key.F5);
        Check("shipping_save_writes_claimed_vault_and_cached_rooms", System.IO.File.Exists(SavePath) &&
            Session.Capture().Campaign.ClearedRooms!.ContainsKey("campaign.repeating_rooms"));
        string savedHash = Session.StateHash; var savedPlayer = Player; int[] savedCells = Map.SeenCells.ToArray();
        await Marker("hollow.vault.return", "vault_return", "campaign.repeating_rooms");
        Check("vault_return_restores_rooms_loot_and_exploration", LootHash == roomsLoot && JsonData.Hash(Session.Room) == roomsRoom &&
            roomsCells.All(Map.SeenCells.Contains) && NoIntent);
        RecordReplay();
        await KeyPress(Key.F9); await CloseJourney();
        Check("shipping_load_restores_claimed_vault_exactly", Session.StateHash == savedHash && Player == savedPlayer &&
            Map.RoomId == Vault && Map.SeenCells.SequenceEqual(savedCells) && LootHash == vaultLoot && NoIntent);
        RejectStaleVaultClaim("loaded");
        await Capture("hollow-unremembered-vault-claimed.png");
        await Marker("hollow.vault.return", "vault_loaded_return", "campaign.repeating_rooms");
        await Marker("hollow.vault.enter", "vault_revisit", Vault);
        Check("vault_revisit_keeps_clearance_floor_loot_and_single_reward", Session.EncounterCleared && LootHash == vaultLoot &&
            !Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0) &&
            Session.Capture().Campaign.Production.Progression.Character.Items.Count(item => item.Id == granted[0].Id) == 1 &&
            Session.Capture().Campaign.Production.Progression.Character.Materials == claimed.Materials);
        RejectStaleVaultClaim("revisited");
        await Marker("hollow.vault.return", "vault_final_return", "campaign.repeating_rooms");
        Check("vault_round_trips_preserve_all_rooms_drops", LootHash == roomsLoot && roomsDrops.All(id =>
            Descendants(_sandbox).OfType<LootVisual>().Any(visual => visual.Name == "Loot" + id && visual.IsVisibleInTree())));
        await Capture("hollow-rooms-after-vault.png"); RecordReplay();
        await Marker("hollow.forward.memory", "identity_memory_enter", "campaign.identity_memory");
    }

    private async Task IdentityAndEndings()
    {
        Check("identity_memory_has_authored_ground_and_map", Session.Room.Obstacles.Length > 0 &&
            _sandbox.EnvironmentStyle == "hollow_memory" && Map.RoomId == "campaign.identity_memory");
        await Fight("identity_memory");
        string identityLoot = LootHash;
        await MapNavigation("identity-memory"); await Capture("hollow-identity-memory-secured.png");
        CheckChoiceGate("initial");
        await KeyPress(Key.F5); string choiceHash = Session.StateHash;
        Check("shipping_save_preserves_unresolved_final_choice", System.IO.File.Exists(SavePath) &&
            !Session.Campaign.Capture().Campaign.Choices.ContainsKey("choice.future") && Session.Campaign.View.Ending is null && !Session.View.Unlocked);
        var choice = Campaign.Capture().Choices.Single(candidate => candidate.Id == "choice.future");
        Check("both_authored_final_outcomes_remain_available", choice.Outcomes.Length == 2);
        await EarnEnding(choice.Outcomes[0].Id);
        await ReturnToFractures("first_ending");
        RecordReplay();
        await KeyPress(Key.F9); await CloseJourney();
        Check("shipping_load_restores_unresolved_choice_and_identity_loot_exactly", Session.StateHash == choiceHash &&
            Session.Campaign.ActiveEncounterId == "campaign.identity_memory" && LootHash == identityLoot &&
            Session.Campaign.View.Ending is null && !Session.View.Unlocked && NoIntent);
        CheckChoiceGate("restored");
        await EarnEnding(choice.Outcomes[1].Id);
        Check("both_final_choices_produce_distinct_earned_endings", _endings.Count == 2 && _endings.Select(ending => ending.Choice).Distinct().Count() == 2 &&
            _endings.Select(ending => ending.EndingId).Distinct().Count() == 2 && _endings.All(ending => ending.FracturesUnlocked && ending.LivingSeals == 0));
        await BacktrackCompletedRegion();
        await ReturnToFractures("second_ending");
        await KeyPress(Key.F5); string hubHash = Session.StateHash;
        var saved = EndgameRuntimeSaveStore.Load(SavePath, CombatJson, Adventure, Progression, Campaign, Endgame).Session;
        Check("completed_campaign_save_keeps_both_unlock_and_all_final_region_rooms", saved.StateHash == hubHash && saved.View.Unlocked && saved.InHub &&
            saved.Capture().Campaign.ClearedRooms!.Keys.Count(HollowCampaignLayout.Contains) == 4);
        RecordReplay();
        await KeyPress(Key.F9); await CloseEndgameBoard(); await CloseJourney();
        Check("shipping_reload_preserves_final_hub_gate_and_cancels_input", Session.StateHash == hubHash && Session.InHub && Session.View.Unlocked &&
            Targets.Any(target => target.Id == "endgame.gate") && NoIntent && !_sandbox.IsPaused);
    }

    private async Task EarnEnding(string outcome)
    {
        Execute(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.Choose, Id: "choice.future", Value: outcome)));
        await CloseJourney(); await Marker("hollow.forward.breach", outcome + "_breach_enter", "campaign.breach_heart");
        Check(outcome + "_breach_heart_starts_authored_sanctum_and_three_seals", Session.Room.Obstacles.Length > 0 && _sandbox.EnvironmentStyle == "hollow_breach" &&
            Map.RoomId == "campaign.breach_heart" && Session.Combat.View.Actors.Count(actor => actor.DefinitionId == "enemy.seal_channel" && actor.Health > 0) == 3 &&
            Session.Combat.View.Actors.Any(actor => actor.DefinitionId == "boss.breach_heart" && actor.Health > 0 && actor.Shielded));
        await Capture("hollow-breach-" + outcome + "-entered.png");
        await Fight("breach_heart_" + outcome, keepJourneyOpen: true);
        var ending = Session.Campaign.View.Ending;
        Check(outcome + "_real_breach_victory_completes_campaign_and_unlocks_fractures", Session.Campaign.Capture().Campaign.CompletedActs.Count == 5 &&
            Session.Campaign.Capture().Campaign.Choices.GetValueOrDefault("choice.future") == outcome && ending is { FracturesUnlocked: true } && Session.View.Unlocked &&
            Session.Combat.View.Actors.Any(actor => actor.DefinitionId == "boss.breach_heart" && actor.Health <= 0) &&
            !Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0));
        Check(outcome + "_ending_story_and_enabled_return_are_visible", Descendants(_hud).OfType<Label>().Any(label => label.IsVisibleInTree() && label.Text == "THE BREACH IS STABLE") &&
            Descendants(_hud).OfType<Label>().Any(label => label.IsVisibleInTree() && label.Text == "RESONANCE FRACTURES UNLOCKED") &&
            Descendants(_hud).OfType<Button>().Any(button => button.IsVisibleInTree() && button.Text == "Return to the people of Greyhaven" && !button.Disabled));
        _endings.Add(new(outcome, ending!.Id, Session.StateHash, Session.Combat.View.Actors.Count(actor => actor.DefinitionId == "enemy.seal_channel" && actor.Health > 0), Session.View.Unlocked));
        await Capture("hollow-ending-" + outcome + ".png");
        await CloseJourney(); await Capture("hollow-breach-" + outcome + "-defeated.png"); RecordReplay();
    }

    private async Task ReturnToFractures(string label)
    {
        string breachLoot = LootHash;
        Execute(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.ReturnToHub)));
        await Frames();
        var board = Field<EndgameHud>(_director, "_board");
        Check(label + "_return_opens_unlocked_fracture_board", Session.InHub && Session.View.Unlocked && board.IsOpen &&
            Targets.Any(target => target.Id == "endgame.gate" && target.Visual is not null));
        Check(label + "_hub_return_retains_final_boss_floor_loot", Session.Capture().Campaign.ClearedRooms!.TryGetValue("campaign.breach_heart", out var saved) &&
            JsonData.Hash(saved.Loot) == breachLoot);
        await Capture("hollow-" + label + "-fractures.png");
        await CloseEndgameBoard(); await CloseJourney();
        // The expedition gate is a client menu action. A real native approach must reach
        // its ordinary marker and open the same board without consuming a Sigil.
        int sigils = Session.View.AvailableSigils.Length;
        for (int attempt = 0; attempt < 4 && !board.IsOpen; attempt++)
        {
            await RecoverFocusPause();
            var point = await FindInteractionPoint("endgame.gate");
            if (await RecoverFocusPause()) continue;
            await Click(point); await WalkUntilStopped();
            if (await RecoverFocusPause() && !board.IsOpen) continue;
            Check(label + "_native_gate_approach_reaches_selected_marker", board.IsOpen);
        }
        Check(label + "_native_gate_opens_board_without_starting_expedition", board.IsOpen && Session.InHub && Session.View.Run is null &&
            Session.View.AvailableSigils.Length == sigils && NoIntent);
        await CloseEndgameBoard(); await CloseJourney();
    }

    private async Task CloseEndgameBoard()
    {
        var board = Field<EndgameHud>(_director, "_board");
        if (board.IsOpen)
        {
            var close = Descendants(board).OfType<Button>().Single(button => button.Name == "ExpeditionClose" && button.IsVisibleInTree());
            await Click(close.GetGlobalRect().GetCenter());
        }
        await RecoverFocusPause();
    }

    private void CheckChoiceGate(string label)
    {
        string hash = Session.StateHash;
        Check(label + "_future_choice_hides_breach_forward_marker", !Targets.Any(target => target.Id == "hollow.forward.breach") &&
            !_sandbox.RequestWorldInteraction("hollow.forward.breach") && Session.StateHash == hash);
        var rejected = Session.Execute(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.InteractHollow, Id: "hollow.forward.breach")));
        Check(label + "_unresolved_future_choice_cannot_bypass_physical_gate", !rejected.Success && Session.StateHash == hash);
        Refresh();
    }

    private async Task BacktrackCompletedRegion()
    {
        string breachLoot = LootHash, endingId = Session.Campaign.View.Ending!.Id;
        await Marker("hollow.back.memory", "breach_backtrack", "campaign.identity_memory");
        await Marker("hollow.back.rooms", "memory_backtrack", "campaign.repeating_rooms");
        await Marker("hollow.forward.memory", "rooms_revisit_memory", "campaign.identity_memory");
        await Marker("hollow.forward.breach", "memory_revisit_breach", "campaign.breach_heart");
        Check("adjacent_backtracking_keeps_breach_victory_floor_loot_and_ending", Session.EncounterCleared && LootHash == breachLoot &&
            Session.Campaign.View.Ending?.Id == endingId && Session.View.Unlocked &&
            !Session.Combat.View.Actors.Any(actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0));
        var atlas = Session.Capture().Campaign.ExplorationMap!.Rooms;
        Check("all_four_hollow_rooms_remember_discovered_terrain", new[] { "campaign.repeating_rooms", "campaign.identity_memory", "campaign.breach_heart", Vault }
            .All(id => atlas.TryGetValue(id, out var room) && room.SeenCells.Length > 0));
        await MapNavigation("breach-heart"); await Capture("hollow-breach-backtracking-complete.png"); RecordReplay();
    }

    private async Task Fight(string label, bool keepJourneyOpen = false)
    {
        int deaths = Session.Campaign.Capture().Campaign.Deaths;
        for (int index = 0; !Session.EncounterCleared && index < 8000; index++)
        {
            var commands = CampaignCombatSmoke.Commands(Session.Combat.View, Session.Room).Where(command => command.Kind != CombatCommandKind.Pickup).ToArray();
            Execute(new(EndgameRuntimeAction.Tick, Commands: commands), refresh: false);
            if (index % 120 == 0) { Refresh(); await Frames(); }
        }
        Execute(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)]));
        if (!keepJourneyOpen) await CloseJourney();
        await Frames();
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
                frame.Command.Campaign is { Action: CampaignRuntimeAction.InteractHollow } action && action.Id == id));
            Check(label + "_approach_never_attacks_or_collects_floor_loot", !_commands.Skip(commands).Any(command => command.Kind is CombatCommandKind.Cast or CombatCommandKind.Pickup));
            _routes.Add($"{id}: from {start} through {target.Position}, arrived in {Session.Campaign.ActiveEncounterId} at {Player}");
            return;
        }
        throw new InvalidDataException("Repeated native focus interruptions prevented passage " + id + ".");
    }

    private void RejectStaleVaultClaim(string label)
    {
        string hash = Session.StateHash;
        Check(label + "_stale_vault_marker_is_unavailable", !_sandbox.RequestWorldInteraction("hollow.vault.treasure") && NoIntent && Session.StateHash == hash);
        var result = Session.Execute(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.InteractHollow, Id: "hollow.vault.treasure")));
        Check(label + "_stale_vault_claim_is_transactionally_rejected", !result.Success && Session.StateHash == hash);
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
        await Capture("hollow-" + label + "-map.png");
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
                throw new InvalidDataException("Native Hollow movement entered an authoritative solid.");
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
        if (!result.Success) throw new InvalidDataException("Hollow setup command failed: " + result.Reason);
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
        if (!_writeReport || !OS.GetCmdlineUserArgs().Contains("--capture-hollow-exploration") || DisplayServer.GetName() == "headless") return;
        await Frames(3); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + filename, image.GetWidth() > 0 && image.SavePng(Path.Combine(_output, filename)) == Error.Ok); _captures.Add(filename);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Hollow exploration check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "HollowExplorationClientSmokePassed" : "HollowExplorationClientSmokeFailed",
            passed,
            checks = _checks,
            captures = _captures,
            inputCommands = _commands.Count,
            campaignSetupCommands = _setupCommands,
            nativeFocusRecoveries = _focusRecoveries,
            focusEvidence = _focusEvidence,
            replayCount = _replays.Count,
            endings = _endings,
            routes = _routes,
            error,
            scope = "The shipping EndgameDirector receives native clicks for Hollow passages, Vault Testament, local map movement and the Fracture gate. Ordinary campaign inputs earn all Acts I–V victories. F5/F9 preserve the vault reward and branch from an unchanged saved choice.future checkpoint to earn both final outcomes through the actual three-seal Breach fight. Checks cover one named vault reward, retained floor loot and discovery, choice gating, physical backtracking, four room maps, visible ending stories, Fracture unlock and deterministic branch replays. No fabricated character, kills, progression, rewards or victories."
        };
        if (_writeReport && !passed && _director is not null && _sandbox is not null)
            System.IO.File.WriteAllText(Path.Combine(_output, "hollow-exploration-failed-state.json"), JsonData.Write(Session.Capture()));
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "hollow-exploration-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
