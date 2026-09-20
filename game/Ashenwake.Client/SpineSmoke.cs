using System.Buffers.Binary;
using System.Security.Cryptography;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Godot;
using FileAccess = Godot.FileAccess;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Actual campaign commands exercise Act IV's scenery, Divine Memory and state-driven Covenant Warden.</summary>
public partial class SpineSmoke : Node
{
    private CampaignRuntimeSession _session = null!;
    private Sandbox _sandbox = null!;
    private CampaignStage _stage = null!;
    private CampaignHud _hud = null!;
    private CampaignContent _campaign = null!;
    private AdventureContent _adventure = null!;
    private ProgressionContent _progression = null!;
    private string _combatJson = "", _output = "", _signature = "";
    private long _revision;
    private int _commands, _memoryTicks, _deathBranchTicks, _wardenBranchTicks;
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _skippedChecks = [];
    private readonly List<EnvironmentEvidence> _environments = [];
    private readonly HashSet<string> _contexts = [], _captures = [], _orientations = [];
    private readonly HashSet<string> _reducedSignalStates = [];
    private readonly HashSet<string> _wardenBranchLanes = [];
    private readonly Dictionary<string, int[]> _faultOrders = [];
    private readonly HashSet<bool> _guards = [];
    private readonly List<string> _audioFingerprints = [];
    private bool _memoryObserved, _memoryCleaned, _wardenObserved, _victoryObserved, _memoryBranchesChecked, _oathObserved, _wardenBranchChecked;
    private CombatView? _liveWarden;
    private sealed record EnvironmentEvidence(string Encounter, string Style, int ArchitectureMeshes, int ArchitectureMaterials,
        int ArchitectureVertices, int GroundMeshes, float GroundTop, string ArchitectureFingerprint, string GroundFingerprint);

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--spine-smoke") || _output.Length == 0)
                throw new InvalidDataException("Spine smoke requires --spine-smoke --output=<isolated-directory>.");
            Directory.CreateDirectory(_output); Engine.MaxFps = 60;
            _combatJson = CampaignCombatContent.Parse(Read("combat"), Read("campaign-combat")).CombatJson;
            _campaign = CampaignContent.Parse(Read("campaign"));
            _adventure = AdventureContent.Parse(Read("adventure"));
            _progression = ProgressionContent.Parse(Read("progression"));
            _session = CampaignRuntimeSession.Create(_combatJson, _adventure, _progression, _campaign);
            _sandbox = new Sandbox { ContentJsonOverride = _combatJson, AutomaticStep = true, AdvanceOverride = _ => [], SessionOverride = () => _session.Combat };
            AddChild(_sandbox); _sandbox.EnableCampaign(); _sandbox.SetSession(_session.Combat);
            Check("ambience_preloaded_before_first_room_entry", SpineAmbience.CachedStreamCount == SpineAmbience.CueNames.Count);
            _stage = new CampaignStage(); AddChild(_stage);
            _hud = new CampaignHud(); _sandbox.AddOverlay(_hud);
            Refresh();
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            await Frames(8);
            if (_sandbox.IsPaused)
            {
                var button = Descendants(_sandbox).OfType<Button>().Single(b => b.Text == "Resume playing" && b.IsVisibleInTree());
                var position = button.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseMotion { Position = position }, true);
                foreach (bool pressed in new[] { true, false })
                    GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = position, Pressed = pressed }, true);
                await Settle();
            }
            var reduced = Descendants(_sandbox).OfType<CheckButton>().Single(b => b.Text == "Reduced visual effects");
            reduced.ButtonPressed = false;
            while (_commands < CampaignRuntimeSmoke.MaximumCommands)
            {
                var before = _session.Capture().Campaign;
                if (before.CompletedActs.Contains(4)) break;
                var command = CampaignRuntimeSmoke.Next(_session);
                var result = _session.Execute(command);
                if (!result.Success) throw new InvalidDataException($"Campaign command {command.Action} failed: {result.Reason}");
                _commands++;
                var state = _session.Capture().Campaign;
                if (state.Deaths != 0) throw new InvalidDataException("The deterministic Act IV route died.");
                if (state.CurrentAct != 4) continue;
                if (state.Exploration?.Id == "event.divine_memory")
                {
                    if (command.Action == CampaignRuntimeAction.Tick) _memoryTicks++;
                }
                var boss = _session.Combat.View.Actors.FirstOrDefault(a => a.DefinitionId == "boss.covenant_warden");
                string hazards = string.Join(',', (_session.Combat.View.CampaignHazards ?? []).Where(h => h.RemainingTicks > 0).Select(h => h.Id));
                string signature = $"{_session.ActiveEncounterId}:{state.Exploration?.Id}:{boss?.Guarded}:{boss?.Health <= 0}:{hazards}:{_session.EncounterCleared}";
                if (signature != _signature)
                {
                    _signature = signature; Refresh();
                    _sandbox.PresentCombatEvents(result.CombatEvents, _session.Combat);
                    await ObserveState();
                }
            }
            Check("real_route_completed_act_four", _session.Capture().Campaign.CompletedActs.Contains(4));
            Check("all_four_distinct_act_four_contexts_observed", _contexts.SetEquals(new[] { "spine_causeway", "spine_hall", "spine_warden", "spine_memory" }));
            Check("architecture_is_distinct_in_every_context", _environments.Select(e => e.ArchitectureFingerprint).Distinct().Count() == 4);
            Check("ground_is_distinct_in_every_context", _environments.Select(e => e.GroundFingerprint).Distinct().Count() == 4);
            Check("memory_reverses_actual_fault_sequence", _memoryObserved && _memoryTicks > 45 &&
                _faultOrders.TryGetValue("spine_causeway", out var causeway) && _faultOrders.TryGetValue("spine_memory", out var memory) && causeway.SequenceEqual(memory.Reverse()));
            Check("real_memory_completed_and_returned_to_anchor", _session.Capture().Campaign.CompletedExploration.Contains("event.divine_memory") && _memoryCleaned);
            Check("memory_departure_and_death_cleanup_checked", _memoryBranchesChecked);
            Check("leaving_memory_restores_region_and_removes_scoped_hazards", _memoryCleaned);
            Check("warden_guarded_and_exposed_states_observed", _guards.SetEquals(new[] { true, false }));
            Check("warden_both_authoritative_fault_lanes_observed", _orientations.Contains("North") && _orientations.Contains("South"));
            Check("warden_boss_owned_oath_mark_observed", _oathObserved);
            Check("warden_victory_observed", _wardenObserved && _victoryObserved);
            string hash = _session.StateHash; CheckAudioSamples();
            Check("audio_generation_does_not_change_core_state", _session.StateHash == hash);
            Check("can_return_to_hub_after_act_four", _session.ReturnToHub().Success); _commands++;
            Refresh(); await Settle();
            Check("hub_stops_spine_ambience", _sandbox.AmbienceCue.Length == 0 && !_sandbox.AmbiencePlaying);
            Check("hub_hides_warden_and_spine_architecture", !Descendants(_stage).OfType<CovenantWardenVisual>().Any(n => n.IsVisibleInTree()) &&
                !Descendants(_stage).OfType<Node3D>().Any(n => n.Name == "ShatteredSpineArchitecture" && n.IsVisibleInTree()));
            Check("hub_style_takes_precedence_over_stale_region", EnvironmentGround.Style(true, "clear", "event.divine_memory", 4) == "greyhaven");
            Check("completed_act_four_can_be_revisited", _session.EnterAct(4).Success && _session.ActiveEncounterId == "clear"); _commands++;
            Refresh(); await Settle();
            Check("completed_act_four_retains_regional_floor_and_atmosphere", _sandbox.EnvironmentStyle == "spine_causeway" && _sandbox.AmbienceCue == "spine_wind" &&
                Descendants(_stage).OfType<Node3D>().Any(n => n.Name == "ShatteredSpineArchitecture" && n.IsVisibleInTree()));
            Check("completed_revisit_does_not_respawn_warden", !Descendants(_stage).Any(n => n is CovenantWardenVisual));
            Check("completed_revisit_can_return_to_hub", _session.ReturnToHub().Success); _commands++;
            Refresh(); await Settle();
            var replay = CampaignRuntimeReplayRunner.Run(_combatJson, _adventure, _progression, _campaign, _session.CaptureReplay());
            Check("checkpointed_command_replay_matches", replay.Success);
            Finish(true, "");
        }
        catch (Exception ex)
        {
            try { await Capture("spine-failure.png"); }
            catch (Exception captureError) { GD.PushError("Failure capture: " + captureError.Message); }
            GD.PushError(ex.ToString()); Finish(false, ex.Message);
        }
    }

    private static string Read(string name) => FileAccess.GetFileAsString("res://" + name + ".json");
    private string Style()
    {
        var state = _session.Capture().Campaign;
        return EnvironmentGround.Style(_session.InHub, _session.ActiveEncounterId, state.Exploration?.Id, state.CurrentAct);
    }
    private void Refresh()
    {
        var snapshot = _session.Capture(); var player = _session.Combat.View.Actors.Single(a => a.Id == 1);
        var interactions = _session.Interactions.Select(i => new InteractionDisplay(i.ActionId, i.Name,
            (int)Math.Sqrt(CorePosition.DistanceSquared(player.Position, i.Position)), i.Range)).ToArray();
        _sandbox.AdoptSession(_session.Combat);
        string style = Style();
        _sandbox.PresentAuthoredRoom(_session.Room, $"spine:{_session.InHub}:{_session.ActiveEncounterId}:{snapshot.Campaign.Deaths}", style);
        _sandbox.SetEnvironmentStyle(style);
        var manifestations = _session.Production.View.ActiveManifestations;
        _stage.Show(snapshot.Campaign, _session.View, _session.Room, _session.Interactions, manifestations,
            _session.Production.ProgressionView.HubStage, player.Position, _session.Combat.View.BossPhase,
            _session.ActiveEncounterId == "campaign.bell_saint" && _session.EncounterCleared,
            _session.Combat.View, _session.ActiveEncounterId);
        _sandbox.SetManifestationPresentation(manifestations);
        _sandbox.SetWorldSubtitle($"CAMPAIGN / {_session.View.Region.ToUpperInvariant()}");
        _hud.SetView(_session.View, snapshot.Campaign, _campaign.Capture(), _session.Production.View, snapshot.Production.Expedition.Adventure,
            _session.Production.AdventureContent.Capture(), _session.Combat.View, interactions, ++_revision);
        // This public-command environment diagnostic observes the world, with menus closed.
        _hud.SetOpen(false);
    }

    private async Task ObserveState()
    {
        string style = Style();
        var architecture = Descendants(_stage).OfType<Node3D>().Single(n => n.Name == "ShatteredSpineArchitecture" && n.IsVisibleInTree());
        ulong architectureId = architecture.GetInstanceId(); string hash = _session.StateHash; Refresh();
        Check("repeat_show_preserves_" + style, Descendants(_stage).OfType<Node3D>().Any(n => n.GetInstanceId() == architectureId && n.IsVisibleInTree()));
        Check("presentation_does_not_mutate_" + style, _session.StateHash == hash);
        if (_contexts.Add(style))
        {
            ObserveGeometry(architecture, style); CheckMouseRoutes(style);
            await Settle();
            Check("regional_ambience_" + style, _sandbox.AmbienceCue == SpineAmbience.CueForStyle(style) && _sandbox.AmbiencePlaying);
            Check("bounded_motes_" + style, _sandbox.AmbientMoteCount == 24);
            await Capture(style + ".png");
            await CheckMouseDestination(style);
            if (style == "spine_causeway") await CheckAtmosphere();
            if (style == "spine_memory")
            {
                _memoryObserved = true;
                Check("memory_is_scoped_until_completion_or_departure", _session.Capture().Campaign.Exploration?.RemainingTicks == 0 &&
                    _session.Combat.View.CampaignRule == "Memory");
            }
        }
        if (_memoryObserved && style != "spine_memory")
        {
            Check("memory_scoped_effects_absent_on_exit", _session.Capture().Campaign.Exploration is null && _session.Combat.View.CampaignRule != "Memory" &&
                !(_session.Combat.View.CampaignHazards ?? []).Any(h => h.ContentId.StartsWith("rule.fault.", StringComparison.Ordinal)));
            _memoryCleaned = true;
        }
        if (_session.EncounterCleared && _session.Capture().Campaign.Exploration is null)
            Check("cleared_arena_keeps_current_architecture_" + style, _stage.PresentedEncounter == _session.ActiveEncounterId &&
                Descendants(_stage).OfType<Node3D>().Any(n => n.GetInstanceId() == architectureId));
        await ObserveFaults(style);
        if (style == "spine_warden")
        {
            await ObserveWarden();
            if (!_wardenBranchChecked) await CheckWardenCycles();
        }
    }

    private async Task CheckMouseDestination(string style)
    {
        // AutomaticStep and the empty AdvanceOverride keep the campaign stationary. These
        // are real viewport inputs testing floor picking and the ring; traversal is checked
        // separately by CheckMouseRoutes against the authoritative room geometry.
        string hash = _session.StateHash;
        var camera = _sandbox.GetChildren().OfType<Camera3D>().Single();
        var player = _session.Combat.View.Actors.Single(a => a.Id == 1).Position;
        var target = new CorePosition(player.X, player.Z + 2000);
        var screen = camera.UnprojectPosition(new(target.X * .001f, 0, target.Z * .001f));
        Check("mouse_destination_projects_inside_viewport_" + style, GetViewport().GetVisibleRect().HasPoint(screen));
        GetViewport().PushInput(new InputEventMouseMotion { Position = screen }, true);
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = screen, Pressed = pressed }, true);
        await Settle();
        Check("viewport_floor_click_shows_destination_" + style, _sandbox.MouseDestinationVisible &&
            _sandbox.ClickMoveDestination is { } destination && CorePosition.DistanceSquared(destination, target) <= 35L * 35);
        var marker = Descendants(_sandbox).OfType<MeshInstance3D>().Single(n => n.Name == "MouseMoveDestination");
        Check("destination_ring_readable_above_regional_ground_" + style, marker.IsVisibleInTree() && marker.Mesh is TorusMesh && marker.Position.Y > .001f &&
            marker.MaterialOverride is StandardMaterial3D { ShadingMode: BaseMaterial3D.ShadingModeEnum.Unshaded } material && material.AlbedoColor == new Color("8ddab6"));
        await Capture(style + "-mouse-destination.png");
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventKey { Keycode = Key.X, PhysicalKeycode = Key.X, Pressed = pressed }, true);
        await Settle();
        Check("stop_key_clears_regional_destination_" + style, _sandbox.ClickMoveDestination is null && !_sandbox.MouseDestinationVisible);
        Check("destination_presentation_does_not_advance_campaign_" + style, _session.StateHash == hash);
    }

    private void CheckMouseRoutes(string style)
    {
        // Exercise the shipping input planner with each room's authoritative obstacle layout.
        // Presentation remains attached during the test; no synthetic physics is introduced.
        var room = _session.Room; var spatial = new SpatialWorld(room); int completed = 0;
        foreach (var obstacle in room.Obstacles)
        {
            int z = (obstacle.MinZ + obstacle.MaxZ) / 2;
            var start = new CorePosition(obstacle.MinX - 1400, z); var target = new CorePosition(obstacle.MaxX + 1400, z);
            if (!spatial.CanOccupy(start, CombatSession.ActorRadius) || !spatial.CanOccupy(target, CombatSession.ActorRadius)) continue;
            var planner = new ClickMovePlanner(room); var position = start; var directions = new HashSet<CorePosition>();
            Check($"mouse_obstacle_{completed}_accepts_floor_{style}", planner.TrySetDestination(start, target) && !spatial.HasLineOfSight(start, target));
            for (int i = 0; i < 500 && planner.Destination is not null; i++)
            {
                var direction = planner.NextDirection(position); directions.Add(direction);
                int step = direction.X != 0 && direction.Z != 0 ? 106 : 150;
                position = spatial.Move(position, new(position.X + direction.X * step, position.Z + direction.Z * step), CombatSession.ActorRadius);
                Check($"mouse_obstacle_{completed}_always_legal_{style}", spatial.CanOccupy(position, CombatSession.ActorRadius));
            }
            Check($"mouse_obstacle_{completed}_arrives_by_detour_{style}", planner.Destination is null && directions.Count >= 2 &&
                CorePosition.DistanceSquared(position, target) <= (long)ClickMovePlanner.ArrivalTolerance * ClickMovePlanner.ArrivalTolerance);
            Check($"mouse_obstacle_{completed}_rejects_blocked_target_{style}", !planner.TrySetDestination(start, new((obstacle.MinX + obstacle.MaxX) / 2, z)));
            completed++;
        }
        Check("mouse_routes_exercise_authored_obstacles_" + style, completed > 0);
    }

    private async Task ObserveFaults(string style)
    {
        if (style is not ("spine_causeway" or "spine_memory")) return;
        await Settle();
        // A zero-countdown warning remains in the authoritative view until the next
        // simulation step resolves it; its number must share that same lifetime.
        var faults = (_session.Combat.View.CampaignHazards ?? []).Where(h => h.ContentId.StartsWith("rule.fault.", StringComparison.Ordinal))
            .OrderBy(h => h.ContentId, StringComparer.Ordinal).ToArray();
        var labels = Descendants(_sandbox).OfType<Label3D>().Where(n => n.Name.ToString().StartsWith("FaultSequence_", StringComparison.Ordinal)).ToArray();
        Check("resolved_fault_labels_are_removed_" + style, labels.Length == faults.Length);
        if (faults.Length != 3 || _faultOrders.ContainsKey(style)) return;
        int[] expected = style == "spine_memory" ? [3000, 0, -3000] : [-3000, 0, 3000];
        Check("numbered_fault_order_matches_core_" + style, faults.Select(h => h.ContentId).SequenceEqual(new[] { "rule.fault.1", "rule.fault.2", "rule.fault.3" }) &&
            faults.Select(h => h.Position.Z).SequenceEqual(expected) && faults.All(h => h.End.Z == h.Position.Z && h.Kind == "Line") &&
            faults[1].RemainingTicks - faults[0].RemainingTicks == 20 && faults[2].RemainingTicks - faults[1].RemainingTicks == 20);
        _faultOrders.Add(style, faults.Select(h => h.Position.Z).ToArray());
        Check("numbered_fault_labels_match_live_lanes_" + style, LabelsMatch());
        await Capture(style + "-fault-sequence.png");
        var effects = Descendants(_sandbox).OfType<CheckButton>().Single(b => b.Text == "Reduced visual effects");
        string hash = _session.StateHash;
        effects.ButtonPressed = true; await Settle();
        Check("reduced_effects_preserve_numbered_fault_warnings_" + style, _sandbox.ReducedEffects && LabelsMatch());
        effects.ButtonPressed = false; await Settle();
        Check("fault_presentation_does_not_change_core_" + style, _session.StateHash == hash);
        if (style == "spine_memory" && !_memoryBranchesChecked) await CheckMemoryBranches();

        bool LabelsMatch() => faults.All(h => labels.SingleOrDefault(n => n.Name == "FaultSequence_" + h.Id) is { } label &&
            label.Text == h.ContentId[^1..] && label.IsVisibleInTree() && label.NoDepthTest && label.OutlineSize > 0 && label.GlobalPosition.Y > .08f &&
            CombatSession.HazardContains(h, new((int)(label.GlobalPosition.X * 1000), (int)(label.GlobalPosition.Z * 1000))) &&
            label.GetParent() is MeshInstance3D { Mesh: BoxMesh box } line && Math.Abs(box.Size.X - h.Radius * .002f) < .001f &&
            line.Position.Y >= .08f && line.MaterialOverride is StandardMaterial3D material && material.AlbedoColor.A >= .35f);
    }

    private async Task CheckMemoryBranches()
    {
        var live = _session; var snapshot = live.Capture(); string hash = live.StateHash;
        var leave = CampaignRuntimeSession.Restore(_combatJson, _adventure, _progression, _campaign, snapshot);
        Check("memory_restore_preserves_active_faults_exactly", leave.StateHash == hash);
        Check("memory_accepts_explicit_departure", leave.LeaveExploration().Success);
        _session = leave; Refresh(); await Settle();
        Check("memory_departure_restores_recorded_arena", leave.ActiveEncounterId == snapshot.ExplorationReturnEncounter &&
            leave.Capture().Campaign.Deaths == snapshot.Campaign.Deaths && !leave.Capture().Campaign.CompletedExploration.Contains("event.divine_memory"));
        Check("memory_departure_cleans_rules_labels_and_ambience", MemoryCleaned());
        Check("memory_departure_replay_matches", CampaignRuntimeReplayRunner.Run(_combatJson, _adventure, _progression, _campaign, leave.CaptureReplay()).Success);

        var death = CampaignRuntimeSession.Restore(_combatJson, _adventure, _progression, _campaign, snapshot);
        for (_deathBranchTicks = 0; _deathBranchTicks < 6000 && death.Capture().Campaign.Deaths == snapshot.Campaign.Deaths; _deathBranchTicks++)
        {
            // No invulnerability, forced health edits, attack, potion or dodge: normal
            // incoming enemy attacks must produce the actual campaign death transition.
            var view = death.Combat.View; var player = view.Actors.Single(a => a.Id == 1);
            var enemy = view.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0)
                .OrderBy(a => CorePosition.DistanceSquared(player.Position, a.Position)).FirstOrDefault();
            CorePosition direction = enemy is null ? default : CombatProductionSmoke.MovementDirection(player.Position, enemy.Position, death.Room);
            Check("memory_death_branch_accepts_ordinary_movement", death.Step(new CombatCommand(CombatCommandKind.Move, X: direction.X, Z: direction.Z)).Success);
        }
        Check("memory_death_branch_reaches_real_death", death.Capture().Campaign.Deaths == snapshot.Campaign.Deaths + 1 &&
            !death.Capture().Campaign.CompletedExploration.Contains("event.divine_memory"));
        _session = death; Refresh(); await Settle();
        Check("memory_death_cleans_rules_labels_and_ambience", MemoryCleaned());
        Check("memory_death_recovers_at_actual_anchor", death.Capture().Campaign.Exploration is null &&
            death.Combat.View.Actors.Single(a => a.Id == 1) is { } recovered && recovered.Health == recovered.MaxHealth && death.ActiveEncounterId != "exploration.first_oath");
        Check("memory_death_replay_matches", CampaignRuntimeReplayRunner.Run(_combatJson, _adventure, _progression, _campaign, death.CaptureReplay()).Success);
        _session = live; Refresh(); await Settle();
        Check("memory_cleanup_branches_do_not_modify_live_campaign", live.StateHash == hash && _sandbox.EnvironmentStyle == "spine_memory" && _sandbox.AmbienceCue == "spine_memory");
        _memoryBranchesChecked = true;

        bool MemoryCleaned() => _session.Capture().Campaign.Exploration is null && _session.Combat.View.CampaignRule != "Memory" &&
            !(_session.Combat.View.CampaignHazards ?? []).Any(h => h.ContentId.StartsWith("rule.fault.", StringComparison.Ordinal)) &&
            !Descendants(_sandbox).OfType<Label3D>().Any(n => n.Name.ToString().StartsWith("FaultSequence_", StringComparison.Ordinal)) &&
            _sandbox.EnvironmentStyle is "spine_causeway" or "spine_hall" && _sandbox.AmbienceCue is "spine_wind" or "spine_hall" && _sandbox.AmbiencePlaying;
    }

    private async Task CheckWardenCycles()
    {
        // The normal combat policy can win before the second fault. Preserve its first
        // Warden checkpoint and let actual boss AI execute both cycles without attacking.
        var live = _session; var snapshot = live.Capture(); string hash = live.StateHash;
        var branch = CampaignRuntimeSession.Restore(_combatJson, _adventure, _progression, _campaign, snapshot);
        Check("warden_cycle_branch_restores_first_arena_entry", branch.StateHash == hash && branch.ActiveEncounterId == "campaign.covenant_warden" &&
            !snapshot.Campaign.CompletedEncounters.Contains("campaign.covenant_warden"));
        string signature = "";
        try
        {
            _session = branch;
            for (_wardenBranchTicks = 0; _wardenBranchTicks < 1500 && _wardenBranchLanes.Count < 2;)
            {
                var view = branch.Combat.View; var player = view.Actors.Single(a => a.Id == 1);
                var commands = new List<CombatCommand> { new(CombatCommandKind.Move, X: 0, Z: 0) };
                if (player.Health < player.MaxHealth / 2 && view.PotionCharges > 0 && view.PotionCooldownTicks == 0)
                    commands.Add(new(CombatCommandKind.Potion));
                var result = branch.Step(commands.ToArray()); _wardenBranchTicks++;
                Check("warden_cycle_branch_accepts_ordinary_stationary_input", result.Success);
                Check("warden_cycle_branch_survives_without_killing_boss", branch.Capture().Campaign.Deaths == snapshot.Campaign.Deaths &&
                    branch.Combat.View.Actors.Any(a => a.DefinitionId == "boss.covenant_warden" && a.Health > 0));
                var boss = branch.Combat.View.Actors.Single(a => a.DefinitionId == "boss.covenant_warden");
                var hazards = (branch.Combat.View.CampaignHazards ?? []).Where(h => h.SourceId == boss.Id && h.RemainingTicks > 0).ToArray();
                foreach (var fault in hazards.Where(h => h.ContentId == "campaign.covenant_fault"))
                    _wardenBranchLanes.Add(fault.Position.Z + fault.End.Z < 0 ? "North" : "South");
                string next = $"{boss.Guarded}:{string.Join(',', hazards.Select(h => h.Id))}";
                if (signature == next) continue;
                signature = next; Refresh();
                _sandbox.PresentCombatEvents(result.CombatEvents, branch.Combat);
                await ObserveWarden();
            }
            Check("warden_cycle_branch_observes_both_actual_fault_lanes", _wardenBranchLanes.SetEquals(new[] { "North", "South" }));
            Check("warden_cycle_branch_replay_matches", CampaignRuntimeReplayRunner.Run(_combatJson, _adventure, _progression, _campaign, branch.CaptureReplay()).Success);
        }
        finally { _session = live; Refresh(); await Settle(); }
        Check("warden_cycle_branch_does_not_modify_live_campaign", live.StateHash == hash && _sandbox.EnvironmentStyle == "spine_warden");
        _wardenBranchChecked = true;
    }

    private async Task ObserveWarden()
    {
        var warden = Descendants(_stage).OfType<CovenantWardenVisual>().Single();
        var view = _session.Combat.View; var boss = view.Actors.Single(a => a.DefinitionId == "boss.covenant_warden");
        var fault = (view.CampaignHazards ?? []).FirstOrDefault(h => h.ContentId == "campaign.covenant_fault" && h.SourceId == boss.Id && h.RemainingTicks > 0);
        string orientation = boss.Health <= 0 || fault is null ? "None" : fault.Position.Z + fault.End.Z < 0 ? "North" : "South";
        bool oath = boss.Health > 0 && (view.CampaignHazards ?? []).Any(h => h.ContentId == "campaign.oath_mark" && h.SourceId == boss.Id && h.RemainingTicks > 0);
        _oathObserved |= oath;
        if (boss.Health > 0) { _guards.Add(boss.Guarded); _liveWarden = view; }
        _orientations.Add(orientation);
        Check("warden_oath_mark_matches_authoritative_warning", warden.OathMarkWarning == oath);
        Check("warden_guard_matches_authoritative_defense", warden.Guarded == (boss.Health > 0 && boss.Guarded));
        Check("warden_faults_match_authoritative_warning_" + orientation, warden.FaultLane == orientation);
        Check("warden_active_shards_never_exceed_pool", warden.ActiveTransientCount >= 0 && warden.ActiveTransientCount <= warden.TransientCapacity);
        if (!warden.Defeated) Check("live_warden_has_no_oath_shards", warden.ActiveTransientCount == 0);
        Check("warden_has_bounded_articulation_and_meshes", warden.ArticulatedPartCount == 14 && warden.TransientCapacity == 16 && Meshes(warden).Length <= 100);
        Check("warden_cannot_change_collision", !Descendants(warden).Any(n => n is CollisionObject3D or CollisionShape3D or NavigationRegion3D));
        Check("warden_stays_beyond_back_wall", Meshes(warden).SelectMany(Vertices).All(p => p.Z < -_session.Room.HalfDepth * .001f + .001f));
        string signalState = $"{boss.Guarded}_{orientation}_{oath}";
        if (boss.Health > 0 && _reducedSignalStates.Add(signalState))
        {
            var reduced = CovenantWardenVisual.Create(_session.Room.HalfWidth * .001f, _session.Room.HalfDepth * .001f, view);
            try
            {
                reduced.Animate(0, false, true); var pose = Pose(reduced);
                for (int i = 0; i < 10; i++) reduced.Animate(.1, false, true);
                Check("reduced_warden_preserves_exact_signals_" + signalState, SamePose(reduced, pose) &&
                    reduced.Guarded == boss.Guarded && reduced.FaultLane == orientation && reduced.OathMarkWarning == oath && reduced.ActiveTransientCount == 0);
            }
            finally { reduced.Free(); }
        }
        if (!_wardenObserved)
        {
            _wardenObserved = true;
            _sandbox.SetPaused(true); await Settle();
            var pose = Pose(warden); string hash = _session.StateHash;
            for (int i = 0; i < 6; i++) warden.Animate(.1, true, false);
            await Settle();
            Check("warden_pause_freezes_every_joint", SamePose(warden, pose) && _session.StateHash == hash);
            _sandbox.SetPaused(false);
        }
        if (!warden.Defeated)
        {
            await Capture(warden.Guarded ? "warden-guarded.png" : "warden-exposed.png");
            if (orientation != "None") await Capture("warden-fault-" + orientation.ToLowerInvariant() + ".png");
        }
        if (!warden.Defeated || _victoryObserved) return;
        _victoryObserved = true;
        Check("real_boss_defeat_starts_finite_release", boss.Health <= 0 && warden.IsTransitioning);
        await Capture("warden-victory-opening.png");
        _sandbox.SetPaused(true); await Settle();
        var pausedPose = Pose(warden); float progress = warden.VictoryProgress;
        warden.Animate(10, true, false); await Settle();
        Check("victory_release_freezes_with_pause", SamePose(warden, pausedPose) && warden.VictoryProgress == progress);
        _sandbox.SetPaused(false);
        for (int i = 0; i < 40; i++) warden.Animate(.1, false, false);
        Check("victory_release_settles_with_bounded_shards", !warden.IsTransitioning && warden.VictoryProgress == 1 && warden.TransientCapacity == 16);
        Check("settled_victory_has_no_active_shards", warden.ActiveTransientCount == 0);
        var settled = Pose(warden);
        for (int i = 0; i < 50; i++) warden.Animate(.1, false, false);
        Refresh();
        Check("victory_does_not_repeat_on_refresh", SamePose(warden, settled) && !warden.IsTransitioning);
        if (DisplayServer.GetName() != "headless") await Frames(210);
        await Settle(); await Capture("warden-victory-settled.png");
        CheckRestoreAndReducedEffects();
    }

    private void CheckRestoreAndReducedEffects()
    {
        var snapshot = _session.Capture(); string hash = _session.StateHash;
        var restored = CampaignRuntimeSession.Restore(_combatJson, _adventure, _progression, _campaign, snapshot);
        Check("warden_defeat_snapshot_restores_exactly", restored.StateHash == hash);
        var stage = new CampaignStage { Visible = false }; AddChild(stage);
        try
        {
            var player = restored.Combat.View.Actors.Single(a => a.Id == 1);
            stage.Show(snapshot.Campaign, restored.View, restored.Room, restored.Interactions, restored.Production.View.ActiveManifestations,
                restored.Production.ProgressionView.HubStage, player.Position, combat: restored.Combat.View, activeEncounterId: restored.ActiveEncounterId);
            var warden = Descendants(stage).OfType<CovenantWardenVisual>().Single();
            Check("restored_victory_has_no_active_shards", warden.ActiveTransientCount == 0);
            Check("restored_victory_starts_settled", warden.Defeated && !warden.IsTransitioning && warden.VictoryProgress == 1 && stage.PresentedEncounter == restored.ActiveEncounterId);
        }
        finally { stage.Free(); }
        var reduced = CovenantWardenVisual.Create(_session.Room.HalfWidth * .001f, _session.Room.HalfDepth * .001f, _liveWarden!);
        try
        {
            reduced.Animate(0, false, true);
            bool guarded = reduced.Guarded; string orientation = reduced.FaultLane; bool oath = reduced.OathMarkWarning;
            var reducedPose = Pose(reduced);
            for (int i = 0; i < 10; i++) reduced.Animate(.1, false, true);
            Check("reduced_warden_retains_gameplay_signals_without_motion", SamePose(reduced, reducedPose) && reduced.Guarded == guarded && reduced.FaultLane == orientation && reduced.OathMarkWarning == oath);
            reduced.SetState(_session.Combat.View, true);
            Check("reduced_effects_skip_release_transition", reduced.Defeated && !reduced.IsTransitioning && reduced.VictoryProgress == 1);
            Check("reduced_victory_has_no_active_shards", reduced.ActiveTransientCount == 0);
            var pose = Pose(reduced);
            for (int i = 0; i < 10; i++) reduced.Animate(.1, false, false);
            Check("reenabling_effects_does_not_replay_victory", SamePose(reduced, pose) && !reduced.IsTransitioning);
        }
        finally { reduced.Free(); }
        Check("restoration_and_reduced_effects_leave_live_core_unchanged", _session.StateHash == hash);
    }
    private void ObserveGeometry(Node3D architecture, string style)
    {
        var meshes = Meshes(architecture);
        var vertices = meshes.SelectMany(Vertices).ToArray();
        float x = _session.Room.HalfWidth * .001f, z = _session.Room.HalfDepth * .001f;
        Check("raised_architecture_outside_combat_" + style, vertices.Length > 0 && vertices.All(p => p.Y <= .65f || Math.Abs(p.X) >= x - .001f || Math.Abs(p.Z) >= z - .001f));
        Check("architecture_has_no_physics_" + style, !Descendants(architecture).Any(n => n is CollisionObject3D or CollisionShape3D or NavigationRegion3D));
        int materials = MaterialCount(meshes);
        Check("bounded_architecture_batches_" + style, meshes.Length is > 0 and <= 32 && materials is > 0 and <= 32);
        var ground = Descendants(_sandbox).OfType<Node3D>().Single(n => n.Name == "AuthoredGround");
        var groundMeshes = Meshes(ground); var groundVertices = groundMeshes.SelectMany(Vertices).ToArray();
        float top = groundVertices.Max(p => p.Y);
        Check("ground_below_combat_warnings_" + style, float.IsFinite(top) && top <= .001f && groundVertices.All(p => float.IsFinite(p.X) && float.IsFinite(p.Z)));
        Check("bounded_ground_batches_" + style, groundMeshes.Length is > 0 and <= 32 && MaterialCount(groundMeshes) <= 32);
        Check("ground_has_no_physics_" + style, !Descendants(ground).Any(n => n is CollisionObject3D or CollisionShape3D or NavigationRegion3D));
        var room = _session.Room;
        var obstacles = Descendants(_sandbox).OfType<Node3D>().Where(n => n.Name.ToString().StartsWith("AuthoritativeObstacle_", StringComparison.Ordinal)).ToArray();
        Check("obstacle_count_matches_core_" + style, obstacles.Length == room.Obstacles.Length);
        for (int i = 0; i < room.Obstacles.Length; i++)
        {
            var bounds = room.Obstacles[i];
            var obstacle = obstacles.Single(n => n.Name == "AuthoritativeObstacle_" + i);
            Check($"obstacle_{i}_preserves_core_footprint_{style}", Meshes(obstacle).SelectMany(Vertices).All(p =>
                p.X >= bounds.MinX * .001f - .001f && p.X <= bounds.MaxX * .001f + .001f &&
                p.Z >= bounds.MinZ * .001f - .001f && p.Z <= bounds.MaxZ * .001f + .001f));
        }
        _environments.Add(new(_stage.PresentedEncounter, style, meshes.Length, materials, vertices.Length, groundMeshes.Length, top, Fingerprint(vertices), Fingerprint(groundVertices)));
    }

    private async Task CheckAtmosphere()
    {
        var motes = Descendants(_sandbox).OfType<MultiMeshInstance3D>().Single(n => n.Name == "AmbientMotes");
        var environment = _sandbox.GetChildren().OfType<WorldEnvironment>().Single().Environment;
        var audio = Descendants(_sandbox).OfType<AudioStreamPlayer>().Single(n => n.Name == "RegionalAmbience");
        Check("ambient_audio_respects_master_volume", audio.Bus == ClientAudio.MusicBus && audio.VolumeDb <= -20 &&
            AudioServer.GetBusSend(AudioServer.GetBusIndex(ClientAudio.MusicBus)) == ClientAudio.MasterBus);
        Check("fog_begins_behind_nearby_combat", environment.FogEnabled && environment.FogDepthBegin >= 35 && environment.FogDepthEnd > environment.FogDepthBegin);
        if (DisplayServer.GetName() != "headless")
        {
            var before = motes.Multimesh.GetInstanceTransform(0); await Frames(8);
            Check("spine_motes_move_when_playing", !motes.Multimesh.GetInstanceTransform(0).IsEqualApprox(before));
            float x = _session.Room.HalfWidth * .001f, z = _session.Room.HalfDepth * .001f;
            Check("spine_motes_stay_outside_combat", Enumerable.Range(0, 24).Select(i => motes.Multimesh.GetInstanceTransform(i).Origin)
                .All(p => Math.Abs(p.X) > x || Math.Abs(p.Z) > z));
            _sandbox.SetPaused(true); await Settle();
            var frozen = motes.Multimesh.GetInstanceTransform(0); await Frames(8);
            Check("spine_motes_freeze_with_pause", motes.Multimesh.GetInstanceTransform(0).IsEqualApprox(frozen));
            var expanded = _session.Room with { HalfWidth = 18000, HalfDepth = 15000 };
            _sandbox.PresentAuthoredRoom(expanded, "spine:bounds-check", "spine_causeway");
            Check("paused_same_style_resize_places_motes_immediately", Outside(expanded));
            expanded = expanded with { HalfWidth = 20000, HalfDepth = 18000 };
            _sandbox.PresentAuthoredRoom(expanded, "spine:style-bounds-check", "spine_warden");
            Check("paused_new_style_uses_current_bounds_immediately", Outside(expanded));
            Refresh();

            bool Outside(RoomDefinition room) => Enumerable.Range(0, 24).Select(i => motes.Multimesh.GetInstanceTransform(i).Origin)
                .All(p => Math.Abs(p.X) > room.HalfWidth * .001f || Math.Abs(p.Z) > room.HalfDepth * .001f);
        }
        else
        {
            _skippedChecks.AddRange(["spine_motes_move_when_playing", "spine_motes_stay_outside_combat", "spine_motes_freeze_with_pause",
                "paused_same_style_resize_places_motes_immediately", "paused_new_style_uses_current_bounds_immediately"]);
            _sandbox.SetPaused(true); await Settle();
        }
        Check("regional_ambience_pauses_with_game", _sandbox.AmbiencePaused);
        _sandbox.SetPaused(false); await Settle();
        Check("regional_ambience_resumes_with_game", !_sandbox.AmbiencePaused && _sandbox.AmbiencePlaying);
        var effects = Descendants(_sandbox).OfType<CheckButton>().Single(b => b.Text == "Reduced visual effects");
        effects.ButtonPressed = true; await Settle();
        Check("reduced_effects_disable_fog_and_motes", _sandbox.ReducedEffects && !environment.FogEnabled && !motes.IsVisibleInTree());
        effects.ButtonPressed = false; await Settle();
        Check("restoring_effects_restores_atmosphere", !_sandbox.ReducedEffects && environment.FogEnabled && motes.IsVisibleInTree());
    }

    private void CheckAudioSamples()
    {
        foreach (string cue in SpineAmbience.CueNames)
        {
            var a = SpineAmbience.GetStream(cue); var b = SpineAmbience.GetStream(cue);
            byte[] data = a.Data;
            Check("ambient_" + cue + "_cached_loop", a.GetInstanceId() == b.GetInstanceId() && a.LoopMode == AudioStreamWav.LoopModeEnum.Forward &&
                a.LoopBegin == 0 && a.LoopEnd == SpineAmbience.SampleCount && a.MixRate == SpineAmbience.SampleRate && data.Length == SpineAmbience.SampleCount * 2);
            int peak = 0; long energy = 0;
            for (int i = 0; i < data.Length; i += 2)
            {
                int sample = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(i, 2));
                peak = Math.Max(peak, Math.Abs(sample)); energy += (long)sample * sample;
            }
            Check("ambient_" + cue + "_non_silent_and_unclipped", peak is > 100 and < 32000 && energy > 0);
            Check("ambient_" + cue + "_deterministic_pcm", data.SequenceEqual(SpineAmbience.CreateSamples(cue)));
            _audioFingerprints.Add(Convert.ToHexString(SHA256.HashData(data)));
        }
        Check("ambient_regions_have_distinct_audio", _audioFingerprints.Distinct().Count() == 4);
        bool rejected = false;
        try { SpineAmbience.GetStream("unrecognized-room"); } catch (ArgumentException) { rejected = true; }
        Check("ambient_cache_has_only_four_allowed_cues", rejected && SpineAmbience.CachedStreamCount == 4);
    }

    private static MeshInstance3D[] Meshes(Node root) => Descendants(root).OfType<MeshInstance3D>().Where(m => m.Mesh is not null).ToArray();
    private static IEnumerable<Vector3> Vertices(MeshInstance3D mesh)
    {
        for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            foreach (var vertex in mesh.Mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                yield return mesh.GlobalTransform * vertex;
    }
    private static int MaterialCount(IEnumerable<MeshInstance3D> meshes) => meshes.SelectMany(m => Enumerable.Range(0, m.Mesh.GetSurfaceCount()).Select(m.GetActiveMaterial))
        .Where(m => m is not null).Select(m => m.GetInstanceId()).Distinct().Count();
    private static string Fingerprint(IEnumerable<Vector3> vertices)
    {
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        foreach (var p in vertices) { writer.Write(p.X); writer.Write(p.Y); writer.Write(p.Z); }
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }
    private static Dictionary<ulong, Transform3D> Pose(Node root) => Descendants(root).OfType<Node3D>().ToDictionary(n => n.GetInstanceId(), n => n.Transform);
    private static bool SamePose(Node root, Dictionary<ulong, Transform3D> pose) => Descendants(root).OfType<Node3D>().All(n => pose.TryGetValue(n.GetInstanceId(), out var before) && before.IsEqualApprox(n.Transform));
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private Task Settle() => Frames(3);
    private async Task Capture(string filename)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--capture-spine") || DisplayServer.GetName() == "headless" || _captures.Contains(filename)) return;
        await Settle();
        // Occluded native windows can stop emitting FramePostDraw. Force the diagnostic
        // viewport to render, then validate the actual image and save result.
        RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var frame = GetViewport().GetTexture().GetImage();
        if (frame is null || frame.IsEmpty() || frame.GetWidth() <= 0 || frame.GetHeight() <= 0 || frame.SavePng(Path.Combine(_output, filename)) != Error.Ok)
            throw new IOException("Could not capture Spine scene: " + filename);
        _captures.Add(filename);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Spine check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "SpineClientSmokePassed" : "SpineClientSmokeFailed",
            passed,
            checks = _checks,
            skippedChecks = _skippedChecks,
            commands = _commands,
            finalStateHash = _session?.StateHash,
            environments = _environments,
            memoryTicks = _memoryTicks,
            faultLanes = _orientations.Order().ToArray(),
            faultOrders = _faultOrders,
            deathBranchTicks = _deathBranchTicks,
            wardenBranchTicks = _wardenBranchTicks,
            wardenBranchLanes = _wardenBranchLanes.Order().ToArray(),
            guardedStates = _guards.Order().ToArray(),
            audioFingerprints = _audioFingerprints,
            captures = _captures.Order().ToArray(),
            error,
            scope = "Real CampaignRuntimeSmoke commands unlock and complete Act IV and Divine Memory. Actual rule.fault.1/2/3 warning positions and displayed numbers establish the reversed sequence. Independently restored branches leave Memory and take real incoming damage until death to verify scoped cleanup and replay. A separate restored first-Warden checkpoint uses ordinary stationary movement and potion inputs without attacking so actual boss AI announces both north and south faults before the normal route defeats it; the branch is replayed and the untouched live checkpoint restored. Mesh vertices establish safe scenery and ground placement; shipping mouse planner routes around each room's obstacles. Real viewport clicks show the mint destination ring in each context and X cancels it while AdvanceOverride keeps campaign movement stationary. Core Warden guard, boss-owned oath marks and fault lanes, finite victory animation, restore, pause, reduced effects, regional audio, resource bounds and deterministic replay are checked without fabricating gameplay state."
        };
        if (_output.Length > 0) System.IO.File.WriteAllText(Path.Combine(_output, "spine-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
