using System.Buffers.Binary;
using System.Security.Cryptography;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Ashenwake.Client;

/// <summary>Actual campaign commands exercise Act II's scenery, tracking clues, root state and victory.</summary>
public partial class VerdantSmoke : Node
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
    private int _commands;
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _skippedChecks = [];
    private readonly List<EnvironmentEvidence> _environments = [];
    private readonly HashSet<string> _contexts = [], _captures = [], _seenClues = [];
    private readonly HashSet<int> _rootCounts = [];
    private readonly List<string> _audioFingerprints = [];
    private readonly List<VerdantDepthChecks.Evidence> _depthEvidence = [];
    private readonly List<object> _frameSamples = [];
    private bool _huntObserved, _huntCleaned, _heartObserved, _victoryObserved;
    private sealed record EnvironmentEvidence(string Encounter, string Style, int ArchitectureMeshes, int ArchitectureMaterials,
        int ArchitectureVertices, int GroundMeshes, float GroundTop, string ArchitectureFingerprint, string GroundFingerprint);

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--verdant-smoke") || _output.Length == 0)
                throw new InvalidDataException("Verdant smoke requires --verdant-smoke --output=<isolated-directory>.");
            Directory.CreateDirectory(_output); Engine.MaxFps = 60;
            VerdantDepthChecks.Detached(Check);
            CreatureMotionChecks.Run(Check);
            VerdantHeartMotionChecks.Run(Check);
            _combatJson = CampaignCombatContent.Parse(Read("combat"), Read("campaign-combat")).CombatJson;
            _campaign = CampaignContent.Parse(Read("campaign"));
            _adventure = AdventureContent.Parse(Read("adventure"));
            _progression = ProgressionContent.Parse(Read("progression"));
            _session = CampaignRuntimeSession.Create(_combatJson, _adventure, _progression, _campaign);
            _sandbox = new Sandbox { ContentJsonOverride = _combatJson, AutomaticStep = true, AdvanceOverride = _ => [], SessionOverride = () => _session.Combat };
            AddChild(_sandbox); _sandbox.EnableCampaign(); _sandbox.SetSession(_session.Combat);
            Check("ambience_preloaded_before_first_room_entry", VerdantAmbience.CachedStreamCount == VerdantAmbience.CueNames.Count);
            _stage = new CampaignStage(); AddChild(_stage);
            _hud = new CampaignHud(); _sandbox.AddOverlay(_hud);
            Refresh(); await Settle();
            var reduced = Descendants(_sandbox).OfType<CheckButton>().Single(b => b.Text == "Reduced visual effects");
            reduced.ButtonPressed = false;
            while (_commands < CampaignRuntimeSmoke.MaximumCommands)
            {
                var before = _session.Capture().Campaign;
                if (before.CompletedActs.Contains(2)) break;
                var command = CampaignRuntimeSmoke.Next(_session);
                if (before.CurrentAct == 2 && command.Action == CampaignRuntimeAction.TrackClue)
                {
                    Refresh(); await Settle();
                    await Capture("hunt-" + command.Id.Replace("clue.", "", StringComparison.Ordinal) + ".png");
                }
                var result = _session.Execute(command);
                if (!result.Success) throw new InvalidDataException($"Campaign command {command.Action} failed: {result.Reason}");
                _commands++;
                var state = _session.Capture().Campaign;
                if (state.Deaths != 0) throw new InvalidDataException("The deterministic Act II route died.");
                if (state.CurrentAct != 2) continue;
                int roots = LivingRoots();
                string signature = $"{_session.ActiveEncounterId}:{state.Exploration?.Id}:{state.Exploration?.TrackedClues}:{roots}:{_session.Combat.View.BossPhase}:{_session.EncounterCleared}";
                bool changed = signature != _signature;
                if (changed)
                {
                    _signature = signature;
                    Refresh();
                }
                _sandbox.PresentCombatEvents(result.CombatEvents, _session.Combat);
                await ObserveCreatureEvents(result.CombatEvents);
                if (changed) await ObserveState();
            }
            Check("real_route_completed_act_two", _session.Capture().Campaign.CompletedActs.Contains(2));
            Check("all_five_distinct_act_two_contexts_observed", _contexts.SetEquals(new[] { "verdant_ruins", "verdant_village", "verdant_hunt", "verdant_heart", "verdant_shrine" }));
            Check("architecture_is_distinct_in_every_context", _environments.Select(e => e.ArchitectureFingerprint).Distinct().Count() == 5);
            Check("ground_is_distinct_in_every_context", _environments.Select(e => e.GroundFingerprint).Distinct().Count() == 5);
            Check("all_three_authoritative_tracking_clues_seen", _seenClues.SetEquals(new[] { "clue.shed_bark", "clue.reversed_tracks", "clue.heartwood_nest" }));
            Check("leaving_hunt_cleans_up_all_clues", _huntObserved && _huntCleaned);
            Check("real_root_damage_reaches_heart", _rootCounts.Contains(3) && _rootCounts.Any(count => count < 3));
            Check("rootheart_victory_observed", _heartObserved && _victoryObserved);
            Check("real_verdant_attacks_reach_creature_rigs", new[] { "enemy.carnivorous_vine", "enemy.needle_swarm", "enemy.bloom_carrier", "boss.rootheart", "boss.antler" }.All(_creatureAttacks.Contains));
            string hash = _session.StateHash;
            CheckAudioSamples();
            Check("audio_generation_does_not_change_core_state", _session.StateHash == hash);
            var departedAtmosphere = _sandbox.VerdantMotion;
            var returned = _session.ReturnToHub(); _commands++;
            Check("can_return_to_hub_after_act_two", returned.Success);
            Refresh(); await Settle();
            Check("hub_stops_verdant_ambience", _sandbox.AmbienceCue.Length == 0 && !_sandbox.AmbiencePlaying);
            Check("hub_releases_verdant_motion_and_lights", _sandbox.VerdantMotion is null && !GodotObject.IsInstanceValid(departedAtmosphere) &&
                !Descendants(_sandbox).Any(n => n is VerdantAtmosphere));
            Check("hub_hides_act_two_architecture", !Descendants(_stage).OfType<Node3D>().Any(n => n.Name == "VerdantMawArchitecture" && n.IsVisibleInTree()));
            Check("hub_style_takes_precedence_over_stale_region", EnvironmentGround.Style(true, "clear", "event.wake_hunt", 2) == "greyhaven");
            var revisit = _session.EnterAct(2); _commands++;
            Check("completed_act_two_can_be_revisited", revisit.Success && _session.ActiveEncounterId == "campaign.living_ruins" && _session.EncounterCleared);
            Refresh(); await Settle();
            Check("completed_act_two_retains_regional_floor_and_atmosphere", _sandbox.EnvironmentStyle == "verdant_ruins" && _sandbox.AmbienceCue == "forest" &&
                Descendants(_stage).OfType<Node3D>().Any(n => n.Name == "VerdantMawArchitecture" && n.IsVisibleInTree()));
            InspectDepth("revisit", "verdant_ruins");
            Check("completed_act_revisit_does_not_respawn_victory_or_clues", !Descendants(_stage).Any(n => n is VerdantHeartVisual or VerdantTrailVisual));
            Check("completed_revisit_can_return_to_hub", _session.ReturnToHub().Success); _commands++;
            Refresh(); await Settle();
            var replay = CampaignRuntimeReplayRunner.Run(_combatJson, _adventure, _progression, _campaign, _session.CaptureReplay());
            Check("checkpointed_command_replay_matches", replay.Success);
            Finish(true, "");
        }
        catch (Exception ex)
        {
            await Capture("verdant-failure.png"); GD.PushError(ex.ToString()); Finish(false, ex.Message);
        }
    }

    private static string Read(string name) => FileAccess.GetFileAsString("res://" + name + ".json");
    private int LivingRoots() => _session.Combat.View.Actors.Count(a => a.DefinitionId == "enemy.feeding_root" && a.Health > 0);
    private string Style()
    {
        var state = _session.Capture().Campaign;
        return EnvironmentGround.Style(_session.InHub, _session.ActiveEncounterId, state.Exploration?.Id, state.CurrentAct);
    }
    private void Refresh()
    {
        var snapshot = _session.Capture(); var player = _session.Combat.View.Actors.Single(a => a.Id == 1);
        var interactions = _session.Interactions.Select(i => new InteractionDisplay(i.ActionId, i.Name,
            (int)Math.Sqrt(Ashenwake.Core.Simulation.Position.DistanceSquared(player.Position, i.Position)), i.Range)).ToArray();
        _sandbox.AdoptSession(_session.Combat);
        string style = Style();
        _sandbox.PresentAuthoredRoom(_session.Room, $"verdant:{_session.InHub}:{_session.ActiveEncounterId}:{snapshot.Campaign.Deaths}", style);
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
        var architecture = Descendants(_stage).OfType<Node3D>().Single(n => n.Name == "VerdantMawArchitecture" && n.IsVisibleInTree());
        ulong architectureId = architecture.GetInstanceId();
        string hash = _session.StateHash;
        Refresh();
        Check("repeat_show_preserves_" + style, Descendants(_stage).OfType<Node3D>().Any(n => n.GetInstanceId() == architectureId && n.IsVisibleInTree()));
        Check("presentation_does_not_mutate_" + style, _session.StateHash == hash);
        if (_contexts.Add(style))
        {
            ObserveGeometry(architecture, style);
            await Settle();
            Check("regional_ambience_" + style, _sandbox.AmbienceCue == VerdantAmbience.CueForStyle(style) && _sandbox.AmbiencePlaying);
            Check("bounded_motes_" + style, _sandbox.AmbientMoteCount == 24);
            await Capture(style + ".png");
            await CheckDepthQuality(style);
            if (style == "verdant_ruins") await CheckAtmosphere();
        }
        if (_session.EncounterCleared && _session.Capture().Campaign.Exploration is null)
        {
            Check("cleared_arena_keeps_current_architecture_" + style, _stage.PresentedEncounter == _session.ActiveEncounterId &&
                Descendants(_stage).OfType<Node3D>().Any(n => n.GetInstanceId() == architectureId));
        }
        ObserveClues();
        if (style == "verdant_heart") await ObserveHeart();
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

    private void ObserveClues()
    {
        var state = _session.Capture().Campaign;
        var trails = Descendants(_stage).OfType<VerdantTrailVisual>().Where(t => t.IsVisibleInTree()).ToArray();
        if (state.Exploration?.Id != "event.wake_hunt")
        {
            if (_huntObserved && _session.ActiveEncounterId == "exploration.antler_hunt")
                Check("completed_grove_keeps_only_quiet_spent_clues", trails.All(trail => trail.Tracked && !trail.Focused) &&
                    !_session.Interactions.Any(interaction => interaction.ActionId.StartsWith("clue.", StringComparison.Ordinal)));
            else if (_huntObserved) { Check("clues_absent_after_leaving_grove", trails.Length == 0); _huntCleaned = true; }
            return;
        }
        _huntObserved = true;
        int tracked = state.Exploration.TrackedClues;
        Check("tracked_clues_keep_quiet_remains_" + tracked, trails.Count(t => t.Tracked) == tracked);
        foreach (var interaction in _session.Interactions.Where(interaction => interaction.ActionId.StartsWith("clue.", StringComparison.Ordinal)))
        {
            var clue = trails.Single(t => t.ClueId == interaction.ActionId);
            _seenClues.Add(interaction.ActionId);
            Check(interaction.ActionId + "_matches_core_anchor", clue.Position.IsEqualApprox(new(interaction.Position.X * .001f, 0, interaction.Position.Z * .001f)));
            var player = _session.Combat.View.Actors.Single(actor => actor.Id == 1).Position;
            bool nearest = _session.Interactions.MinBy(candidate => Ashenwake.Core.Simulation.Position.DistanceSquared(player, candidate.Position))!.ActionId == interaction.ActionId;
            Check(interaction.ActionId + "_focus_matches_nearest_interaction_without_future_clues", clue.Focused == nearest && !clue.Tracked && trails.Count(t => !t.Tracked) == 1);
            Check(interaction.ActionId + "_bounded_profile", Meshes(clue).SelectMany(Vertices).All(p =>
                p.Y <= VerdantTrailVisual.MaximumPropHeight + .001f && new Vector2(p.X - clue.GlobalPosition.X, p.Z - clue.GlobalPosition.Z).Length() <= VerdantTrailVisual.MaximumRadius));
            Check(interaction.ActionId + "_bounded_meshes", Meshes(clue).Length <= 9 && !Descendants(clue).Any(n => n is CollisionObject3D or CollisionShape3D));
            Check(interaction.ActionId + "_guidance_uses_only_previous_known_anchor", clue.TrailMarkCount == (tracked == 0 ? 0 : 4));
            ulong id = clue.GetInstanceId(); Refresh();
            Check(interaction.ActionId + "_refresh_keeps_instance", Descendants(_stage).OfType<VerdantTrailVisual>().Any(t => t.GetInstanceId() == id));
        }
        Check("spent_clues_have_no_focus_" + tracked, trails.Where(t => t.Tracked).All(t => !t.Focused));
    }

    private async Task ObserveHeart()
    {
        var heart = Descendants(_stage).OfType<VerdantHeartVisual>().Single();
        _rootCounts.Add(LivingRoots());
        Check("heart_root_count_" + LivingRoots() + "_matches_core", heart.LivingRoots == LivingRoots());
        Check("heart_has_bounded_articulation_and_meshes", heart.ArticulatedPartCount == 13 && heart.TransientCapacity == 0 && Meshes(heart).Length <= 80);
        Check("heart_cannot_change_collision", !Descendants(heart).Any(n => n is CollisionObject3D or CollisionShape3D or NavigationRegion3D));
        float z = _session.Room.HalfDepth * .001f;
        Check("heart_stays_beyond_back_wall", Meshes(heart).SelectMany(Vertices).All(p => p.Z < -z + .001f));
        if (!_heartObserved)
        {
            _heartObserved = true;
            Check("heart_begins_guarded_by_three_roots", heart.LivingRoots == 3 && !heart.Defeated);
            _sandbox.SetPaused(true); await Settle();
            var pose = Pose(heart); string hash = _session.StateHash;
            for (int i = 0; i < 6; i++) heart.Animate(.1, true, false);
            await Settle();
            Check("heart_pause_freezes_every_joint", SamePose(heart, pose) && _session.StateHash == hash);
            _sandbox.SetPaused(false);
            await Capture("rootheart-guarded.png");
        }
        if (heart.LivingRoots < 3 && !heart.Defeated) await Capture("rootheart-exposed.png");
        if (!heart.Defeated || _victoryObserved) return;
        _victoryObserved = true;
        Check("real_boss_defeat_starts_finite_bloom", _session.Combat.View.Actors.Any(a => a.DefinitionId == "boss.rootheart" && a.Health <= 0) && heart.IsTransitioning);
        await Capture("rootheart-victory-opening.png");
        _sandbox.SetPaused(true); await Settle();
        var pausedPose = Pose(heart); float progress = heart.VictoryProgress;
        heart.Animate(10, true, false); await Settle();
        Check("victory_bloom_freezes_with_pause", SamePose(heart, pausedPose) && heart.VictoryProgress == progress);
        _sandbox.SetPaused(false);
        for (int i = 0; i < 36; i++) heart.Animate(.1, false, false);
        Check("victory_bloom_settles_without_particles", !heart.IsTransitioning && heart.VictoryProgress == 1 && heart.TransientCapacity == 0);
        var settled = Pose(heart);
        for (int i = 0; i < 50; i++) heart.Animate(.1, false, false);
        Refresh();
        Check("victory_does_not_repeat_on_refresh", SamePose(heart, settled) && !heart.IsTransitioning);
        // Give the ordinary actor death clips time to finish as well as the directly exercised
        // backdrop transition, so the rendered settled view matches what a player sees.
        if (DisplayServer.GetName() != "headless") await Frames(210);
        await Settle(); await Capture("rootheart-victory-settled.png");
        CheckRestoreAndReducedEffects();
    }

    private void CheckRestoreAndReducedEffects()
    {
        var snapshot = _session.Capture(); string hash = _session.StateHash;
        var restored = CampaignRuntimeSession.Restore(_combatJson, _adventure, _progression, _campaign, snapshot);
        Check("completed_act_two_snapshot_restores_exactly", restored.StateHash == hash);
        var stage = new CampaignStage { Visible = false }; AddChild(stage);
        try
        {
            var player = restored.Combat.View.Actors.Single(a => a.Id == 1);
            stage.Show(snapshot.Campaign, restored.View, restored.Room, restored.Interactions, restored.Production.View.ActiveManifestations,
                restored.Production.ProgressionView.HubStage, player.Position, combat: restored.Combat.View, activeEncounterId: restored.ActiveEncounterId);
            var heart = Descendants(stage).OfType<VerdantHeartVisual>().Single();
            Check("restored_victory_starts_settled", heart.Defeated && !heart.IsTransitioning && heart.VictoryProgress == 1 && stage.PresentedEncounter == restored.ActiveEncounterId);
        }
        finally { stage.Free(); }
        var reduced = VerdantHeartVisual.Create(_session.Room.HalfWidth * .001f, _session.Room.HalfDepth * .001f);
        try
        {
            reduced.Animate(0, false, true); reduced.SetState(0, true);
            Check("reduced_effects_skip_bloom_transition", reduced.Defeated && !reduced.IsTransitioning && reduced.VictoryProgress == 1);
            var pose = Pose(reduced);
            for (int i = 0; i < 10; i++) reduced.Animate(.1, false, false);
            Check("reenabling_effects_does_not_replay_victory", SamePose(reduced, pose) && !reduced.IsTransitioning);
        }
        finally { reduced.Free(); }
        Check("restoration_and_reduced_effects_leave_live_core_unchanged", _session.StateHash == hash);
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
            Check("verdant_motes_move_when_playing", !motes.Multimesh.GetInstanceTransform(0).IsEqualApprox(before));
            float x = _session.Room.HalfWidth * .001f, z = _session.Room.HalfDepth * .001f;
            Check("verdant_motes_stay_outside_combat", Enumerable.Range(0, 24).Select(i => motes.Multimesh.GetInstanceTransform(i).Origin)
                .All(p => Math.Abs(p.X) > x || Math.Abs(p.Z) > z));
            _sandbox.SetPaused(true); await Settle();
            var frozen = motes.Multimesh.GetInstanceTransform(0); await Frames(8);
            Check("verdant_motes_freeze_with_pause", motes.Multimesh.GetInstanceTransform(0).IsEqualApprox(frozen));
            var expanded = _session.Room with { HalfWidth = 18000, HalfDepth = 15000 };
            var departed = _sandbox.VerdantMotion;
            _sandbox.PresentAuthoredRoom(expanded, "verdant:bounds-check", "verdant_ruins");
            Check("paused_same_style_resize_places_motes_immediately", Outside(expanded));
            _depthEvidence.Add(VerdantDepthChecks.Inspect(_sandbox, "paused-resize", "verdant_ruins", 18, 15, Check));
            await Settle();
            Check("resize_releases_previous_verdant_resources", !GodotObject.IsInstanceValid(departed));
            departed = _sandbox.VerdantMotion;
            expanded = expanded with { HalfWidth = 20000, HalfDepth = 18000 };
            _sandbox.PresentAuthoredRoom(expanded, "verdant:style-bounds-check", "verdant_heart");
            Check("paused_new_style_uses_current_bounds_immediately", Outside(expanded));
            _depthEvidence.Add(VerdantDepthChecks.Inspect(_sandbox, "paused-style-change", "verdant_heart", 20, 18, Check));
            await Settle();
            Check("style_change_releases_previous_verdant_resources", !GodotObject.IsInstanceValid(departed));
            Refresh();

            bool Outside(RoomDefinition room) => Enumerable.Range(0, 24).Select(i => motes.Multimesh.GetInstanceTransform(i).Origin)
                .All(p => Math.Abs(p.X) > room.HalfWidth * .001f || Math.Abs(p.Z) > room.HalfDepth * .001f);
        }
        else
        {
            _skippedChecks.AddRange(["verdant_motes_move_when_playing", "verdant_motes_stay_outside_combat", "verdant_motes_freeze_with_pause",
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

    private void InspectDepth(string context, string style) => _depthEvidence.Add(VerdantDepthChecks.Inspect(_sandbox, context, style,
        _session.Room.HalfWidth * .001f, _session.Room.HalfDepth * .001f, Check));

    private async Task CheckDepthQuality(string style)
    {
        string hash = _session.StateHash;
        var atmosphere = _sandbox.VerdantMotion!;
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
        Check("paused_reduced_effects_settles_live_foliage_" + style, _sandbox.ReducedEffects && atmosphere.MotionTime == 0);
        InspectDepth(style + "-reduced", style);
        if (style is "verdant_ruins" or "verdant_heart") await Capture(style + "-reduced.png");
        effects.ButtonPressed = false;
        selector.Select(0); selector.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
        Check("restoring_verdant_preferences_preserves_paused_room_" + style,
            atmosphere.GetInstanceId() == id && atmosphere.MotionTime == 0 && atmosphere.ActiveLightCount == 4 && atmosphere.ActiveFoliageCount == 72);
        _sandbox.SetPaused(false); await Settle();
        Check("verdant_quality_and_effects_do_not_mutate_core_" + style, _session.StateHash == hash);
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
        foreach (string cue in VerdantAmbience.CueNames)
        {
            var a = VerdantAmbience.GetStream(cue); var b = VerdantAmbience.GetStream(cue);
            byte[] data = a.Data;
            Check("ambient_" + cue + "_cached_loop", a.GetInstanceId() == b.GetInstanceId() && a.LoopMode == AudioStreamWav.LoopModeEnum.Forward &&
                a.LoopBegin == 0 && a.LoopEnd == VerdantAmbience.SampleCount && a.MixRate == VerdantAmbience.SampleRate && data.Length == VerdantAmbience.SampleCount * 2);
            int peak = 0; long energy = 0;
            for (int i = 0; i < data.Length; i += 2)
            {
                int sample = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(i, 2));
                peak = Math.Max(peak, Math.Abs(sample)); energy += (long)sample * sample;
            }
            Check("ambient_" + cue + "_non_silent_and_unclipped", peak is > 100 and < 32000 && energy > 0);
            Check("ambient_" + cue + "_deterministic_pcm", data.SequenceEqual(VerdantAmbience.CreateSamples(cue)));
            _audioFingerprints.Add(Convert.ToHexString(SHA256.HashData(data)));
        }
        Check("ambient_regions_have_distinct_audio", _audioFingerprints.Distinct().Count() == 3);
        bool rejected = false;
        try { VerdantAmbience.GetStream("unrecognized-room"); } catch (ArgumentException) { rejected = true; }
        Check("ambient_cache_has_only_three_allowed_cues", rejected && VerdantAmbience.CachedStreamCount == 3);
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
        if (!OS.GetCmdlineUserArgs().Contains("--capture-verdant") || DisplayServer.GetName() == "headless" || !_captures.Add(filename)) return;
        await Frames(3); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + filename, image.GetWidth() > 0 && image.SavePng(Path.Combine(_output, filename)) == Error.Ok);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Verdant check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "VerdantClientSmokePassed" : "VerdantClientSmokeFailed",
            passed,
            checks = _checks,
            skippedChecks = _skippedChecks,
            commands = _commands,
            finalStateHash = _session?.StateHash,
            environments = _environments,
            clues = _seenClues.Order().ToArray(),
            rootCounts = _rootCounts.Order().ToArray(),
            audioFingerprints = _audioFingerprints,
            depthEvidence = _depthEvidence,
            frameSamples = _frameSamples,
            captures = _captures.Order().ToArray(),
            error,
            scope = "Real CampaignRuntimeSmoke commands unlock and complete Act II, including the tracking hunt, Core feeding roots and Rootheart victory. Mesh vertices establish safe scenery and ground placement. Runtime state, finite animations, restore, pause, reduced effects, regional audio, resource bounds and deterministic command replay are checked without changing gameplay state for presentation."
        };
        if (_output.Length > 0) System.IO.File.WriteAllText(Path.Combine(_output, "verdant-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
