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

/// <summary>Actual campaign commands exercise Act III's scenery, timed storm and state-driven furnace.</summary>
public partial class CinderSmoke : Node
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
    private int _commands, _stormTicks;
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _skippedChecks = [];
    private readonly List<EnvironmentEvidence> _environments = [];
    private readonly HashSet<string> _contexts = [], _captures = [], _orientations = [];
    private readonly HashSet<bool> _guards = [];
    private readonly List<string> _audioFingerprints = [];
    private readonly List<CinderDepthChecks.Evidence> _depthEvidence = [];
    private readonly List<object> _frameSamples = [];
    private readonly HashSet<string> _furnaceDepthStates = [];
    private bool _stormObserved, _stormCleaned, _furnaceObserved, _victoryObserved, _stormWarned;
    private CombatView? _liveFurnace;
    private sealed record EnvironmentEvidence(string Encounter, string Style, int ArchitectureMeshes, int ArchitectureMaterials,
        int ArchitectureVertices, int GroundMeshes, float GroundTop, string ArchitectureFingerprint, string GroundFingerprint);

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--cinder-smoke") || _output.Length == 0)
                throw new InvalidDataException("Cinder smoke requires --cinder-smoke --output=<isolated-directory>.");
            Directory.CreateDirectory(_output); Engine.MaxFps = 60;
            CinderDepthChecks.Detached(Check);
            CreatureMotionChecks.Run(Check);
            FurnaceSpindleMotionChecks.Run(Check);
            _combatJson = CampaignCombatContent.Parse(Read("combat"), Read("campaign-combat")).CombatJson;
            _campaign = CampaignContent.Parse(Read("campaign"));
            _adventure = AdventureContent.Parse(Read("adventure"));
            _progression = ProgressionContent.Parse(Read("progression"));
            _session = CampaignRuntimeSession.Create(_combatJson, _adventure, _progression, _campaign);
            _sandbox = new Sandbox { ContentJsonOverride = _combatJson, AutomaticStep = true, AdvanceOverride = _ => [], SessionOverride = () => _session.Combat };
            AddChild(_sandbox); _sandbox.EnableCampaign(); _sandbox.SetSession(_session.Combat);
            Check("ambience_preloaded_before_first_room_entry", CinderAmbience.CachedStreamCount == CinderAmbience.CueNames.Count);
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
            int emberlingObservationTicks = 0;
            while (_commands < CampaignRuntimeSmoke.MaximumCommands)
            {
                var before = _session.Capture().Campaign;
                if (before.CompletedActs.Contains(3)) break;
                var command = CampaignRuntimeSmoke.Next(_session);
                // Give the first emberling time to resolve a real attack before the normal
                // combat policy kills it. These ordinary idle inputs remain in the replay.
                if (before.CurrentAct == 3 && _session.ActiveEncounterId == "campaign.cinder_pack" &&
                    !_creatureAttacks.Contains("enemy.emberling") && emberlingObservationTicks < 180)
                {
                    command = new(CampaignRuntimeAction.Tick, Commands: []);
                    emberlingObservationTicks++;
                }
                var result = _session.Execute(command);
                if (!result.Success) throw new InvalidDataException($"Campaign command {command.Action} failed: {result.Reason}");
                _commands++;
                var state = _session.Capture().Campaign;
                if (state.Deaths != 0) throw new InvalidDataException("The deterministic Act III route died.");
                if (state.CurrentAct != 3) continue;
                if (state.Exploration?.Id == "event.resonance_storm")
                {
                    if (command.Action == CampaignRuntimeAction.Tick) _stormTicks++;
                    _stormWarned |= (_session.Combat.View.CampaignHazards ?? []).Any(h => h.ContentId == "rule.storm" && h.RemainingTicks > 0);
                }
                var boss = _session.Combat.View.Actors.FirstOrDefault(a => a.DefinitionId == "boss.furnace_spindle");
                var vent = (_session.Combat.View.CampaignHazards ?? []).FirstOrDefault(h => h.ContentId == "campaign.furnace_vent");
                string signature = $"{_session.ActiveEncounterId}:{state.Exploration?.Id}:{boss?.Guarded}:{boss?.Health <= 0}:{vent?.Id}:{_session.EncounterCleared}:{_stormWarned}";
                bool changed = signature != _signature;
                if (changed) { _signature = signature; Refresh(); }
                _sandbox.PresentCombatEvents(result.CombatEvents, _session.Combat);
                await ObserveCreatureEvents(result.CombatEvents);
                if (changed) await ObserveState();
            }
            Check("real_route_completed_act_three", _session.Capture().Campaign.CompletedActs.Contains(3));
            Check("all_five_distinct_act_three_contexts_observed", _contexts.SetEquals(new[] { "cinder_fields", "cinder_extraction", "cinder_furnace", "cinder_storm", "cinder_foundry" }));
            Check("architecture_is_distinct_in_every_context", _environments.Select(e => e.ArchitectureFingerprint).Distinct().Count() == 5);
            Check("ground_is_distinct_in_every_context", _environments.Select(e => e.GroundFingerprint).Distinct().Count() == 5);
            Check("real_storm_announced_fire_lanes_observed", _stormObserved && _stormWarned && _stormTicks > 36);
            Check("leaving_storm_restores_region_and_removes_scoped_hazards", _stormCleaned);
            Check("furnace_guarded_and_exposed_states_observed", _guards.SetEquals(new[] { true, false }));
            Check("furnace_both_authoritative_vent_orientations_observed", _orientations.Contains("Horizontal") && _orientations.Contains("Vertical"));
            Check("furnace_victory_observed", _furnaceObserved && _victoryObserved);
            Check("real_cinder_attacks_reach_creature_rigs", new[] { "enemy.emberling", "enemy.furnace_brute", "enemy.forge_sentinel", "boss.furnace_spindle" }.All(_creatureAttacks.Contains));
            string hash = _session.StateHash; CheckAudioSamples();
            Check("audio_generation_does_not_change_core_state", _session.StateHash == hash);
            var departedAtmosphere = _sandbox.CinderMotion;
            Check("can_return_to_hub_after_act_three", _session.ReturnToHub().Success); _commands++;
            Refresh(); await Settle();
            Check("hub_releases_cinder_motion_and_lights", _sandbox.CinderMotion is null && !GodotObject.IsInstanceValid(departedAtmosphere) &&
                !Descendants(_sandbox).Any(n => n is CinderAtmosphere));
            Check("hub_stops_cinder_ambience", _sandbox.AmbienceCue.Length == 0 && !_sandbox.AmbiencePlaying);
            Check("hub_hides_furnace_and_cinder_architecture", !Descendants(_stage).OfType<FurnaceSpindleVisual>().Any(n => n.IsVisibleInTree()) &&
                !Descendants(_stage).OfType<Node3D>().Any(n => n.Name == "CinderReachArchitecture" && n.IsVisibleInTree()));
            Check("hub_style_takes_precedence_over_stale_region", EnvironmentGround.Style(true, "clear", "event.resonance_storm", 3) == "greyhaven");
            Check("completed_act_three_can_be_revisited", _session.EnterAct(3).Success && _session.ActiveEncounterId == "campaign.cinder_pack"); _commands++;
            Refresh(); await Settle();
            Check("completed_act_three_retains_regional_floor_and_atmosphere", _sandbox.EnvironmentStyle == "cinder_fields" && _sandbox.AmbienceCue == "cinder_wind" &&
                Descendants(_stage).OfType<Node3D>().Any(n => n.Name == "CinderReachArchitecture" && n.IsVisibleInTree()));
            Check("completed_revisit_does_not_respawn_furnace", !Descendants(_stage).Any(n => n is FurnaceSpindleVisual));
            InspectDepth("revisit", "cinder_fields");
            Check("completed_revisit_can_return_to_hub", _session.ReturnToHub().Success); _commands++;
            Refresh(); await Settle();
            var replay = CampaignRuntimeReplayRunner.Run(_combatJson, _adventure, _progression, _campaign, _session.CaptureReplay());
            Check("checkpointed_command_replay_matches", replay.Success);
            Finish(true, "");
        }
        catch (Exception ex)
        {
            try { await Capture("cinder-failure.png"); }
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
        _sandbox.PresentAuthoredRoom(_session.Room, $"cinder:{_session.InHub}:{_session.ActiveEncounterId}:{snapshot.Campaign.Deaths}", style);
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
        var architecture = Descendants(_stage).OfType<Node3D>().Single(n => n.Name == "CinderReachArchitecture" && n.IsVisibleInTree());
        ulong architectureId = architecture.GetInstanceId(); string hash = _session.StateHash; Refresh();
        Check("repeat_show_preserves_" + style, Descendants(_stage).OfType<Node3D>().Any(n => n.GetInstanceId() == architectureId && n.IsVisibleInTree()));
        Check("presentation_does_not_mutate_" + style, _session.StateHash == hash);
        if (_contexts.Add(style))
        {
            ObserveGeometry(architecture, style); CheckMouseRoutes(style);
            await Settle();
            Check("regional_ambience_" + style, _sandbox.AmbienceCue == CinderAmbience.CueForStyle(style) && _sandbox.AmbiencePlaying);
            Check("bounded_motes_" + style, _sandbox.AmbientMoteCount == 24);
            await Capture(style + ".png");
            await CheckDepthQuality(style);
            await CheckMouseDestination(style);
            if (style == "cinder_fields") await CheckAtmosphere();
            if (style == "cinder_storm")
            {
                _stormObserved = true;
                Check("storm_uses_authored_finite_duration", _session.Capture().Campaign.Exploration?.RemainingTicks ==
                    _campaign.Capture().Exploration.Single(e => e.Id == "event.resonance_storm").DurationTicks);
                CheckStormExpiry();
            }
        }
        if (_stormObserved && style != "cinder_storm")
        {
            Check("storm_scoped_effects_absent_on_exit", _session.Capture().Campaign.Exploration is null && _session.Combat.View.CampaignRule != "Storm" &&
                !(_session.Combat.View.CampaignHazards ?? []).Any(h => h.ContentId == "rule.storm"));
            _stormCleaned = true;
        }
        if (_session.EncounterCleared && _session.Capture().Campaign.Exploration is null)
            Check("cleared_arena_keeps_current_architecture_" + style, _stage.PresentedEncounter == _session.ActiveEncounterId &&
                Descendants(_stage).OfType<Node3D>().Any(n => n.GetInstanceId() == architectureId));
        if (style == "cinder_furnace") await ObserveFurnace();
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

    private void CheckStormExpiry()
    {
        var live = _session; string hash = live.StateHash;
        var timed = CampaignRuntimeSession.Restore(_combatJson, _adventure, _progression, _campaign, live.Capture());
        int duration = timed.Capture().Campaign.Exploration!.RemainingTicks;
        bool warnings = false;
        for (int i = 0; i < duration; i++)
        {
            // Follow a wide circuit with ordinary movement and potion inputs, preserving the
            // enemy population so this independent restored branch reaches the actual timer.
            var view = timed.Combat.View; var player = view.Actors.Single(a => a.Id == 1); var room = timed.Room;
            CorePosition[] corners = [new(-room.HalfWidth + 1300, -room.HalfDepth + 1300), new(room.HalfWidth - 1300, -room.HalfDepth + 1300),
                new(room.HalfWidth - 1300, room.HalfDepth - 1300), new(-room.HalfWidth + 1300, room.HalfDepth - 1300)];
            var target = corners[(i / 160) % corners.Length];
            var occupied = view.Actors.Where(a => a.Id != 1 && a.Health > 0).Select(a => a.Position).ToArray();
            var direction = CombatProductionSmoke.MovementDirection(player.Position, target, room, occupied);
            var commands = new List<CombatCommand> { new(CombatCommandKind.Move, X: direction.X, Z: direction.Z) };
            if (player.Health < player.MaxHealth / 2 && view.PotionCharges > 0 && view.PotionCooldownTicks == 0) commands.Add(new(CombatCommandKind.Potion));
            if (view.DodgeCooldownTicks == 0 && (view.CampaignHazards ?? []).Any(h => h.RemainingTicks <= 12 && CombatSession.HazardContains(h, player.Position)))
                commands.Add(new(CombatCommandKind.Dodge, X: direction.X, Z: direction.Z));
            var result = timed.Execute(new(CampaignRuntimeAction.Tick, Commands: commands.ToArray()));
            Check("storm_timer_branch_accepts_ordinary_commands", result.Success);
            warnings |= (timed.Combat.View.CampaignHazards ?? []).Any(h => h.ContentId == "rule.storm");
            if (i < duration - 1)
                Check("storm_remains_active_until_deadline", timed.Capture().Campaign.Exploration?.RemainingTicks == duration - i - 1);
        }
        Check("storm_timer_expires_without_death_or_combat_victory", timed.Capture().Campaign.Exploration is null && timed.Capture().Campaign.Deaths == live.Capture().Campaign.Deaths &&
            !timed.Capture().Campaign.CompletedExploration.Contains("event.resonance_storm") && warnings);
        _session = timed; Refresh();
        Check("expired_storm_cleans_visible_environment_immediately", _sandbox.EnvironmentStyle == "cinder_extraction" && _sandbox.AmbienceCue == "cinder_machinery" &&
            !(_session.Combat.View.CampaignHazards ?? []).Any(h => h.ContentId == "rule.storm"));
        _session = live; Refresh();
        Check("storm_timer_branch_does_not_modify_live_campaign", live.StateHash == hash);
        Check("storm_expiry_replay_matches", CampaignRuntimeReplayRunner.Run(_combatJson, _adventure, _progression, _campaign, timed.CaptureReplay()).Success);
    }

    private async Task ObserveFurnace()
    {
        var furnace = Descendants(_stage).OfType<FurnaceSpindleVisual>().Single();
        var view = _session.Combat.View; var boss = view.Actors.Single(a => a.DefinitionId == "boss.furnace_spindle");
        var vent = (view.CampaignHazards ?? []).FirstOrDefault(h => h.ContentId == "campaign.furnace_vent" && h.SourceId == boss.Id && h.RemainingTicks > 0);
        string orientation = vent is null ? "None" : Math.Abs(vent.End.X - vent.Position.X) >= Math.Abs(vent.End.Z - vent.Position.Z) ? "Horizontal" : "Vertical";
        if (boss.Health > 0 && _furnaceDepthStates.Add($"{boss.Guarded}:{orientation}"))
            CinderFurnaceDepthChecks.Detached(view, Check, fullRotation: _furnaceDepthStates.Count == 1);
        if (boss.Health > 0) { _guards.Add(boss.Guarded); _liveFurnace = view; }
        _orientations.Add(orientation);
        Check("furnace_guard_matches_authoritative_defense", furnace.Guarded == (boss.Health > 0 && boss.Guarded));
        Check("furnace_vents_match_authoritative_warning_" + orientation, furnace.VentOrientation == orientation);
        Check("furnace_active_embers_never_exceed_pool", furnace.ActiveTransientCount >= 0 && furnace.ActiveTransientCount <= furnace.TransientCapacity);
        if (!furnace.Defeated) Check("live_furnace_has_no_cooling_embers", furnace.ActiveTransientCount == 0);
        Check("furnace_has_bounded_articulation_and_meshes", furnace.ArticulatedPartCount == 11 && furnace.TransientCapacity == 12 && Meshes(furnace).Length <= 100);
        Check("furnace_cannot_change_collision", !Descendants(furnace).Any(n => n is CollisionObject3D or CollisionShape3D or NavigationRegion3D));
        Check("furnace_stays_beyond_back_wall", Meshes(furnace).SelectMany(Vertices).All(p => p.Z < -_session.Room.HalfDepth * .001f + .001f));
        if (!_furnaceObserved)
        {
            _furnaceObserved = true;
            _sandbox.SetPaused(true); await Settle();
            var pose = Pose(furnace); string hash = _session.StateHash;
            for (int i = 0; i < 6; i++) furnace.Animate(.1, true, false);
            await Settle();
            Check("furnace_pause_freezes_every_joint", SamePose(furnace, pose) && _session.StateHash == hash);
            _sandbox.SetPaused(false);
        }
        if (!furnace.Defeated)
        {
            await Capture(furnace.Guarded ? "furnace-guarded.png" : "furnace-exposed.png");
            if (orientation != "None") await Capture("furnace-vent-" + orientation.ToLowerInvariant() + ".png");
        }
        if (!furnace.Defeated || _victoryObserved) return;
        _victoryObserved = true;
        Check("real_boss_defeat_starts_finite_shutdown", boss.Health <= 0 && furnace.IsTransitioning);
        await Capture("furnace-victory-opening.png");
        _sandbox.SetPaused(true); await Settle();
        var pausedPose = Pose(furnace); float progress = furnace.VictoryProgress;
        furnace.Animate(10, true, false); await Settle();
        Check("victory_shutdown_freezes_with_pause", SamePose(furnace, pausedPose) && furnace.VictoryProgress == progress);
        _sandbox.SetPaused(false);
        for (int i = 0; i < 40; i++) furnace.Animate(.1, false, false);
        Check("victory_shutdown_settles_with_bounded_embers", !furnace.IsTransitioning && furnace.VictoryProgress == 1 && furnace.TransientCapacity == 12);
        Check("settled_victory_has_no_active_embers", furnace.ActiveTransientCount == 0);
        var settled = Pose(furnace);
        for (int i = 0; i < 50; i++) furnace.Animate(.1, false, false);
        Refresh();
        Check("victory_does_not_repeat_on_refresh", SamePose(furnace, settled) && !furnace.IsTransitioning);
        if (DisplayServer.GetName() != "headless") await Frames(210);
        await Settle(); await Capture("furnace-victory-settled.png");
        CheckRestoreAndReducedEffects();
    }

    private void CheckRestoreAndReducedEffects()
    {
        var snapshot = _session.Capture(); string hash = _session.StateHash;
        var restored = CampaignRuntimeSession.Restore(_combatJson, _adventure, _progression, _campaign, snapshot);
        Check("furnace_defeat_snapshot_restores_exactly", restored.StateHash == hash);
        var stage = new CampaignStage { Visible = false }; AddChild(stage);
        try
        {
            var player = restored.Combat.View.Actors.Single(a => a.Id == 1);
            stage.Show(snapshot.Campaign, restored.View, restored.Room, restored.Interactions, restored.Production.View.ActiveManifestations,
                restored.Production.ProgressionView.HubStage, player.Position, combat: restored.Combat.View, activeEncounterId: restored.ActiveEncounterId);
            var furnace = Descendants(stage).OfType<FurnaceSpindleVisual>().Single();
            Check("restored_victory_has_no_active_embers", furnace.ActiveTransientCount == 0);
            Check("restored_victory_starts_settled", furnace.Defeated && !furnace.IsTransitioning && furnace.VictoryProgress == 1 && stage.PresentedEncounter == restored.ActiveEncounterId);
        }
        finally { stage.Free(); }
        var reduced = FurnaceSpindleVisual.Create(_session.Room.HalfWidth * .001f, _session.Room.HalfDepth * .001f, _liveFurnace!);
        try
        {
            reduced.Animate(0, false, true);
            bool guarded = reduced.Guarded; string orientation = reduced.VentOrientation;
            var reducedPose = Pose(reduced);
            for (int i = 0; i < 10; i++) reduced.Animate(.1, false, true);
            Check("reduced_furnace_retains_gameplay_signals_without_motion", SamePose(reduced, reducedPose) && reduced.Guarded == guarded && reduced.VentOrientation == orientation);
            reduced.SetState(_session.Combat.View, true);
            Check("reduced_effects_skip_shutdown_transition", reduced.Defeated && !reduced.IsTransitioning && reduced.VictoryProgress == 1);
            Check("reduced_victory_has_no_active_embers", reduced.ActiveTransientCount == 0);
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
        Check("ground_below_combat_warnings_" + style, float.IsFinite(top) && top < 0 && groundVertices.All(p => float.IsFinite(p.X) && float.IsFinite(p.Z)));
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
            Check("cinder_motes_move_when_playing", !motes.Multimesh.GetInstanceTransform(0).IsEqualApprox(before));
            float x = _session.Room.HalfWidth * .001f, z = _session.Room.HalfDepth * .001f;
            Check("cinder_motes_stay_outside_combat", Enumerable.Range(0, 24).Select(i => motes.Multimesh.GetInstanceTransform(i).Origin)
                .All(p => Math.Abs(p.X) > x || Math.Abs(p.Z) > z));
            _sandbox.SetPaused(true); await Settle();
            var frozen = motes.Multimesh.GetInstanceTransform(0); await Frames(8);
            Check("cinder_motes_freeze_with_pause", motes.Multimesh.GetInstanceTransform(0).IsEqualApprox(frozen));
            var expanded = _session.Room with { HalfWidth = 18000, HalfDepth = 15000 };
            var departed = _sandbox.CinderMotion;
            _sandbox.PresentAuthoredRoom(expanded, "cinder:bounds-check", "cinder_fields");
            Check("paused_same_style_resize_places_motes_immediately", Outside(expanded));
            _depthEvidence.Add(CinderDepthChecks.Inspect(_sandbox, "paused-resize", "cinder_fields", 18, 15, Check));
            await Settle();
            Check("resize_releases_previous_cinder_resources", !GodotObject.IsInstanceValid(departed));
            departed = _sandbox.CinderMotion;
            expanded = expanded with { HalfWidth = 20000, HalfDepth = 18000 };
            _sandbox.PresentAuthoredRoom(expanded, "cinder:style-bounds-check", "cinder_furnace");
            Check("paused_new_style_uses_current_bounds_immediately", Outside(expanded));
            _depthEvidence.Add(CinderDepthChecks.Inspect(_sandbox, "paused-style-change", "cinder_furnace", 20, 18, Check));
            await Settle();
            Check("style_change_releases_previous_cinder_resources", !GodotObject.IsInstanceValid(departed));
            Refresh();

            bool Outside(RoomDefinition room) => Enumerable.Range(0, 24).Select(i => motes.Multimesh.GetInstanceTransform(i).Origin)
                .All(p => Math.Abs(p.X) > room.HalfWidth * .001f || Math.Abs(p.Z) > room.HalfDepth * .001f);
        }
        else
        {
            _skippedChecks.AddRange(["cinder_motes_move_when_playing", "cinder_motes_stay_outside_combat", "cinder_motes_freeze_with_pause",
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

    private void InspectDepth(string context, string style) => _depthEvidence.Add(CinderDepthChecks.Inspect(_sandbox, context, style,
        _session.Room.HalfWidth * .001f, _session.Room.HalfDepth * .001f, Check));

    private async Task CheckDepthQuality(string style)
    {
        string hash = _session.StateHash;
        var atmosphere = _sandbox.CinderMotion!;
        ulong id = atmosphere.GetInstanceId();
        InspectDepth(style + "-high", style);
        await SampleFrames(style, "High");
        _sandbox.SetPaused(true); await Settle();
        double frozen = atmosphere.MotionTime;
        var selector = Descendants(_sandbox).OfType<OptionButton>().Single(n => n.Name == "SettingsGraphicsQuality");
        selector.Select(1); selector.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        InspectDepth(style + "-performance", style);
        Check("paused_quality_keeps_room_and_clock_" + style, atmosphere.GetInstanceId() == id && atmosphere.MotionTime == frozen);
        _sandbox.SetPaused(false); await Settle();
        await Capture(style + "-performance.png");
        await SampleFrames(style, "Performance");
        _sandbox.SetPaused(true); await Settle();
        var effects = Descendants(_sandbox).OfType<CheckButton>().Single(b => b.Text == "Reduced visual effects");
        effects.ButtonPressed = true; await Settle();
        Check("paused_reduced_effects_settles_live_machinery_" + style, _sandbox.ReducedEffects && atmosphere.MotionTime == 0);
        InspectDepth(style + "-reduced", style);
        if (style is "cinder_fields" or "cinder_furnace") await Capture(style + "-reduced.png");
        effects.ButtonPressed = false;
        selector.Select(0); selector.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
        Check("restoring_cinder_preferences_preserves_paused_room_" + style,
            atmosphere.GetInstanceId() == id && atmosphere.MotionTime == 0 && atmosphere.ActiveLightCount == 4);
        _sandbox.SetPaused(false); await Settle();
        Check("cinder_quality_and_effects_do_not_mutate_core_" + style, _session.StateHash == hash);
    }

    private async Task SampleFrames(string style, string quality)
    {
        if (DisplayServer.GetName() == "headless") return;
        var samples = new List<double>();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (int frame = 0; frame < 30; frame++)
        {
            double before = timer.Elapsed.TotalMilliseconds;
            await Frames(1);
            samples.Add(timer.Elapsed.TotalMilliseconds - before);
        }
        samples.Sort();
        _frameSamples.Add(new { style, quality, frames = samples.Count, medianMs = samples[15], p95Ms = samples[28] });
    }

    private void CheckAudioSamples()
    {
        foreach (string cue in CinderAmbience.CueNames)
        {
            var a = CinderAmbience.GetStream(cue); var b = CinderAmbience.GetStream(cue);
            byte[] data = a.Data;
            Check("ambient_" + cue + "_cached_loop", a.GetInstanceId() == b.GetInstanceId() && a.LoopMode == AudioStreamWav.LoopModeEnum.Forward &&
                a.LoopBegin == 0 && a.LoopEnd == CinderAmbience.SampleCount && a.MixRate == CinderAmbience.SampleRate && data.Length == CinderAmbience.SampleCount * 2);
            int peak = 0; long energy = 0;
            for (int i = 0; i < data.Length; i += 2)
            {
                int sample = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(i, 2));
                peak = Math.Max(peak, Math.Abs(sample)); energy += (long)sample * sample;
            }
            Check("ambient_" + cue + "_non_silent_and_unclipped", peak is > 100 and < 32000 && energy > 0);
            Check("ambient_" + cue + "_deterministic_pcm", data.SequenceEqual(CinderAmbience.CreateSamples(cue)));
            _audioFingerprints.Add(Convert.ToHexString(SHA256.HashData(data)));
        }
        Check("ambient_regions_have_distinct_audio", _audioFingerprints.Distinct().Count() == 4);
        bool rejected = false;
        try { CinderAmbience.GetStream("unrecognized-room"); } catch (ArgumentException) { rejected = true; }
        Check("ambient_cache_has_only_four_allowed_cues", rejected && CinderAmbience.CachedStreamCount == 4);
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
        if (!OS.GetCmdlineUserArgs().Contains("--capture-cinder") || DisplayServer.GetName() == "headless" || _captures.Contains(filename)) return;
        await Settle();
        // Occluded native windows can stop emitting FramePostDraw. Force the diagnostic
        // viewport to render, then validate the actual image and save result.
        RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var frame = GetViewport().GetTexture().GetImage();
        if (frame is null || frame.IsEmpty() || frame.GetWidth() <= 0 || frame.GetHeight() <= 0 || frame.SavePng(Path.Combine(_output, filename)) != Error.Ok)
            throw new IOException("Could not capture Cinder scene: " + filename);
        _captures.Add(filename);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Cinder check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "CinderClientSmokePassed" : "CinderClientSmokeFailed",
            passed,
            checks = _checks,
            skippedChecks = _skippedChecks,
            commands = _commands,
            finalStateHash = _session?.StateHash,
            environments = _environments,
            stormTicks = _stormTicks,
            ventOrientations = _orientations.Order().ToArray(),
            guardedStates = _guards.Order().ToArray(),
            audioFingerprints = _audioFingerprints,
            depthEvidence = _depthEvidence,
            frameSamples = _frameSamples,
            captures = _captures.Order().ToArray(),
            error,
            scope = "Real CampaignRuntimeSmoke commands unlock and complete Act III, including the Sealed Foundry and timed Resonance Storm, Core Furnace guard and vent windows, and victory. An independently restored storm branch exercises timer expiry using ordinary movement and potion inputs. Mesh vertices establish safe scenery and ground placement; shipping mouse planner routes around each room's obstacles. Real viewport clicks show the mint destination ring in each context and X cancels it while AdvanceOverride keeps campaign movement stationary. Runtime state, finite animations, restore, pause, reduced effects, regional audio, resource bounds and deterministic command replay are checked without changing gameplay state for presentation."
        };
        if (_output.Length > 0) System.IO.File.WriteAllText(Path.Combine(_output, "cinder-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
