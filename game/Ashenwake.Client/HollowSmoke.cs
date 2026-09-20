using System.Buffers.Binary;
using System.Globalization;
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

/// <summary>Real campaign input exercises Act V, causal warnings, Breach Heart phases and the final return.</summary>
public partial class HollowSmoke : Node
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
    private int _commands, _deathBranchTicks;
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _skippedChecks = [];
    private readonly List<EnvironmentEvidence> _environments = [];
    private readonly HashSet<string> _contexts = [], _captures = [], _echoTypes = [], _reducedSignalStates = [];
    private readonly HashSet<int> _phases = [], _channels = [], _phaseBranches = [];
    private readonly Dictionary<int, int> _phaseBranchTicks = [];
    private readonly HashSet<bool> _shields = [];
    private readonly List<string> _audioFingerprints = [];
    private bool _breachObserved, _victoryObserved, _cleanupChecked, _returnOnlyObserved, _sweepObserved, _zeroEchoObserved, _endingObserved, _mirrorCopyObserved;
    private CombatView? _liveBreach;
    private sealed record EnvironmentEvidence(string Encounter, string Style, int ArchitectureMeshes, int ArchitectureMaterials,
        int ArchitectureVertices, int GroundMeshes, float GroundTop, string ArchitectureFingerprint, string GroundFingerprint);

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--hollow-smoke") || _output.Length == 0)
                throw new InvalidDataException("Hollow smoke requires --hollow-smoke --output=<isolated-directory>.");
            Directory.CreateDirectory(_output); Engine.MaxFps = 60;
            _combatJson = CampaignCombatContent.Parse(Read("combat"), Read("campaign-combat")).CombatJson;
            _campaign = CampaignContent.Parse(Read("campaign"));
            _adventure = AdventureContent.Parse(Read("adventure"));
            _progression = ProgressionContent.Parse(Read("progression"));
            _session = CampaignRuntimeSession.Create(_combatJson, _adventure, _progression, _campaign);
            _sandbox = new Sandbox { ContentJsonOverride = _combatJson, AutomaticStep = true, AdvanceOverride = _ => [], SessionOverride = () => _session.Combat };
            AddChild(_sandbox); _sandbox.EnableCampaign(); _sandbox.SetSession(_session.Combat);
            Check("ambience_preloaded_before_first_room_entry", HollowAmbience.CachedStreamCount == HollowAmbience.CueNames.Count);
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
            while (_commands < CampaignRuntimeSmoke.MaximumCommands && !CampaignRuntimeSmoke.Complete(_session))
            {
                var command = CampaignRuntimeSmoke.Next(_session);
                var result = _session.Execute(command);
                if (!result.Success) throw new InvalidDataException($"Campaign command {command.Action} failed: {result.Reason}");
                _commands++;
                var state = _session.Capture().Campaign;
                if (state.Deaths != 0) throw new InvalidDataException("The deterministic Act V route died.");
                if (state.CurrentAct != 5 || state.InHub) continue;
                string signature = Signature();
                if (signature != _signature)
                {
                    _signature = signature; Refresh();
                    _sandbox.PresentCombatEvents(result.CombatEvents, _session.Combat);
                    await ObserveState();
                }
            }
            Check("real_route_completes_campaign_and_returns_to_hub", CampaignRuntimeSmoke.Complete(_session));
            Check("final_choice_and_ending_recorded", _session.Capture().Campaign.Choices.ContainsKey("choice.future") && _session.View.Ending is { FracturesUnlocked: true });
            Check("all_three_distinct_act_five_contexts_observed", _contexts.SetEquals(new[] { "hollow_rooms", "hollow_memory", "hollow_breach" }));
            Check("architecture_is_distinct_in_every_context", _environments.Select(e => e.ArchitectureFingerprint).Distinct().Count() == 3);
            Check("ground_is_distinct_in_every_context", _environments.Select(e => e.GroundFingerprint).Distinct().Count() == 3);
            Check("all_actual_breach_phases_observed", _phases.SetEquals(new[] { 1, 2, 3 }));
            Check("seal_channels_all_living_counts_observed", _channels.SetEquals(new[] { 0, 1, 2, 3 }));
            Check("shield_changes_at_actual_channel_threshold", _shields.SetEquals(new[] { true, false }));
            Check("causal_first_and_returning_echoes_observed", _echoTypes.Contains("rule.causalechoes") && _echoTypes.Contains("campaign.causalecho") &&
                _echoTypes.Contains("campaign.breach_echo") && _echoTypes.Contains("campaign.returning_echo"));
            Check("returning_echo_outlives_first_echo", _returnOnlyObserved);
            Check("phase_three_sweep_observed", _sweepObserved);
            Check("zero_countdown_label_lifetime_checked", _zeroEchoObserved);
            Check("death_and_departure_cleanup_checked", _cleanupChecked);
            Check("final_victory_observed", _breachObserved && _victoryObserved);
            Check("actual_ending_story_presented", _endingObserved);
            Check("mirrorborn_copies_distinguished_from_final_boss", _mirrorCopyObserved);
            string hash = _session.StateHash; CheckAudioSamples();
            Check("audio_generation_does_not_change_core_state", _session.StateHash == hash);
            Refresh(); await Settle();
            CheckHubCleanup("final_return");
            Check("hub_style_takes_precedence_over_stale_region", EnvironmentGround.Style(true, "campaign.breach_heart", "", 5) == "greyhaven");
            Check("completed_act_five_can_be_revisited", _session.EnterAct(5).Success && _session.ActiveEncounterId == "clear"); _commands++;
            Refresh(); await Settle();
            Check("completed_act_five_retains_regional_floor_and_atmosphere", _sandbox.EnvironmentStyle == "hollow_rooms" && _sandbox.AmbienceCue == "hollow_rooms" &&
                Descendants(_stage).OfType<Node3D>().Any(n => n.Name == "HollowNightArchitecture" && n.IsVisibleInTree()));
            Check("completed_revisit_does_not_respawn_breach_heart", !Descendants(_stage).Any(n => n is BreachHeartVisual));
            Check("completed_revisit_retains_ending", _session.View.Ending is not null);
            Check("completed_revisit_can_return_to_hub", _session.ReturnToHub().Success); _commands++;
            Refresh(); await Settle(); CheckHubCleanup("revisit_return");
            var replay = CampaignRuntimeReplayRunner.Run(_combatJson, _adventure, _progression, _campaign, _session.CaptureReplay());
            Check("checkpointed_command_replay_matches", replay.Success);
            Finish(true, "");
        }
        catch (Exception ex)
        {
            try { await Capture("hollow-failure.png"); }
            catch (Exception captureError) { GD.PushError("Failure capture: " + captureError.Message); }
            GD.PushError(ex.ToString()); Finish(false, ex.Message);
        }
    }

    private string Signature()
    {
        var view = _session.Combat.View;
        var boss = Boss(view);
        string hazards = string.Join(',', (view.CampaignHazards ?? []).Select(h => $"{h.Id}:{h.RemainingTicks == 0}"));
        int copies = view.Actors.Count(a => a.DefinitionId == "boss.breach_heart" && a.Id != boss?.Id && a.Health > 0);
        return $"{_session.ActiveEncounterId}:{view.BossPhase}:{ChannelCount(view)}:{boss?.Health <= 0}:{copies}:{hazards}:{_session.EncounterCleared}";
    }
    private static CombatActorView? Boss(CombatView view) => view.Actors.Where(a => a.DefinitionId == "boss.breach_heart").OrderBy(a => a.Id).FirstOrDefault();
    private static int ChannelCount(CombatView view) => view.Actors.Count(a => a.DefinitionId == "enemy.seal_channel" && a.Health > 0);
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
        _sandbox.PresentAuthoredRoom(_session.Room, $"hollow:{_session.InHub}:{_session.ActiveEncounterId}:{snapshot.Campaign.Deaths}", style);
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
        // Keep the earned ending open until its native presentation check has observed it.
        // Other public-command environment checks observe the world with menus closed.
        if (_session.View.Ending is null || _endingObserved) _hud.SetOpen(false);
    }

    private async Task ObserveState()
    {
        string style = Style();
        var architecture = Descendants(_stage).OfType<Node3D>().Single(n => n.Name == "HollowNightArchitecture" && n.IsVisibleInTree());
        ulong architectureId = architecture.GetInstanceId(); string hash = _session.StateHash; Refresh();
        Check("repeat_show_preserves_" + style, Descendants(_stage).OfType<Node3D>().Any(n => n.GetInstanceId() == architectureId && n.IsVisibleInTree()));
        Check("presentation_does_not_mutate_" + style, _session.StateHash == hash);
        if (_contexts.Add(style))
        {
            ObserveGeometry(architecture, style); CheckMouseRoutes(style);
            await Settle();
            Check("regional_ambience_" + style, _sandbox.AmbienceCue == HollowAmbience.CueForStyle(style) && _sandbox.AmbiencePlaying);
            Check("bounded_motes_" + style, _sandbox.AmbientMoteCount == 24);
            await Capture(style + ".png"); await CheckMouseDestination(style);
            if (style == "hollow_rooms") await CheckAtmosphere();
        }
        if (_session.EncounterCleared)
            Check("cleared_arena_keeps_current_architecture_" + style, _stage.PresentedEncounter == _session.ActiveEncounterId &&
                Descendants(_stage).OfType<Node3D>().Any(n => n.GetInstanceId() == architectureId));
        if (!_endingObserved && _session.View.Ending is not null) await CheckEndingPresentation();
        await ObserveEchoes(style);
        if (style == "hollow_memory" && !_cleanupChecked && Echoes(_session.Combat.View).Length > 0) await CheckCleanupBranches();
        if (style != "hollow_breach") return;
        await ObserveBreach();
        int phase = _session.Combat.View.BossPhase;
        if (Boss(_session.Combat.View) is { Health: > 0 } && _phaseBranches.Add(phase)) await CheckBreachCycle(phase);
    }

    private async Task CheckEndingPresentation()
    {
        await Settle();
        Check("ending_story_opens_after_final_encounter", Descendants(_hud).OfType<Label>().Any(n => n.IsVisibleInTree() && n.Text == "THE BREACH IS STABLE") &&
            Descendants(_hud).OfType<Label>().Any(n => n.IsVisibleInTree() && n.Text == "RESONANCE FRACTURES UNLOCKED"));
        Check("ending_offers_enabled_return_to_greyhaven", Descendants(_hud).OfType<Button>().Any(n => n.IsVisibleInTree() &&
            n.Text == "Return to the people of Greyhaven" && !n.Disabled));
        await Capture("hollow-ending-story.png");
        var close = Descendants(_hud).OfType<Button>().Single(n => n.IsVisibleInTree() && n.Text == "Close");
        var at = close.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = at }, true);
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = at, Pressed = pressed }, true);
        await Settle();
        Check("ending_story_can_be_closed_for_arena_view", !Descendants(_hud).OfType<Label>().Any(n => n.IsVisibleInTree() && n.Text == "THE BREACH IS STABLE"));
        _endingObserved = true;
    }

    private static CombatHazardView[] Echoes(CombatView view) => (view.CampaignHazards ?? []).Where(h =>
        h.ContentId is "rule.causalechoes" or "campaign.causalecho" or "campaign.breach_echo" or "campaign.returning_echo").ToArray();
    private static string EchoText(CombatHazardView hazard)
    {
        string prefix = hazard.ContentId == "campaign.breach_echo" ? "FIRST" : hazard.ContentId == "campaign.returning_echo" ? "RETURN" : "ECHO";
        double seconds = Math.Ceiling(hazard.RemainingTicks * FixedStepClock.SecondsPerTick * 10) / 10;
        return prefix + "\n" + seconds.ToString("F1", CultureInfo.InvariantCulture) + "s";
    }
    private async Task ObserveEchoes(string style)
    {
        await Settle();
        var echoes = Echoes(_session.Combat.View);
        var labels = Descendants(_sandbox).OfType<Label3D>().Where(n => n.Name.ToString().StartsWith("EchoWarning_", StringComparison.Ordinal)).ToArray();
        Check("resolved_echo_labels_are_removed_" + style, labels.Length == echoes.Length);
        Check("echo_labels_match_live_hazard_geometry_and_countdown_" + style, LabelsMatch());
        _zeroEchoObserved |= echoes.Any(h => h.RemainingTicks == 0);
        bool firstType = false;
        foreach (var echo in echoes) firstType |= _echoTypes.Add(echo.ContentId);
        if (!firstType) return;
        await Capture(style + "-echo-warnings.png");
        var effects = Descendants(_sandbox).OfType<CheckButton>().Single(b => b.Text == "Reduced visual effects");
        string hash = _session.StateHash;
        effects.ButtonPressed = true; await Settle();
        Check("reduced_effects_preserve_echo_warnings_" + style, _sandbox.ReducedEffects && LabelsMatch());
        effects.ButtonPressed = false; await Settle();
        Check("echo_presentation_does_not_change_core_" + style, _session.StateHash == hash);

        bool LabelsMatch() => echoes.All(h => labels.SingleOrDefault(n => n.Name == "EchoWarning_" + h.Id) is { } label &&
            label.Text == EchoText(h) && label.IsVisibleInTree() && label.NoDepthTest && label.OutlineSize > 0 && label.GlobalPosition.Y > .08f &&
            CombatSession.HazardContains(h, new((int)(label.GlobalPosition.X * 1000), (int)(label.GlobalPosition.Z * 1000))) &&
            label.GetParent() is MeshInstance3D { Mesh: TorusMesh } ring && ring.Position.Y >= .074f &&
            ring.MaterialOverride is StandardMaterial3D material && material.AlbedoColor.A >= .35f);
    }

    private void CheckHubCleanup(string context)
    {
        Check("hub_stops_hollow_ambience_" + context, _sandbox.AmbienceCue.Length == 0 && !_sandbox.AmbiencePlaying);
        Check("hub_hides_breach_and_hollow_architecture_" + context, !Descendants(_stage).OfType<BreachHeartVisual>().Any(n => n.IsVisibleInTree()) &&
            !Descendants(_stage).OfType<Node3D>().Any(n => n.Name == "HollowNightArchitecture" && n.IsVisibleInTree()));
        Check("hub_cleans_echo_hazards_and_labels_" + context, Echoes(_session.Combat.View).Length == 0 &&
            !Descendants(_sandbox).OfType<Label3D>().Any(n => n.Name.ToString().StartsWith("EchoWarning_", StringComparison.Ordinal)));
    }

    private async Task CheckCleanupBranches()
    {
        var live = _session; var snapshot = live.Capture(); string hash = live.StateHash;
        try
        {
            var leave = CampaignRuntimeSession.Restore(_combatJson, _adventure, _progression, _campaign, snapshot);
            Check("active_echo_snapshot_restores_exactly", leave.StateHash == hash);
            Check("active_identity_memory_accepts_return_to_hub", leave.ReturnToHub().Success);
            _session = leave; Refresh(); await Settle(); CheckHubCleanup("active_departure");
            Check("active_departure_replay_matches", CampaignRuntimeReplayRunner.Run(_combatJson, _adventure, _progression, _campaign, leave.CaptureReplay()).Success);
            var death = CampaignRuntimeSession.Restore(_combatJson, _adventure, _progression, _campaign, snapshot);
            for (_deathBranchTicks = 0; _deathBranchTicks < 6000 && death.Capture().Campaign.Deaths == snapshot.Campaign.Deaths; _deathBranchTicks++)
            {
                var view = death.Combat.View; var player = view.Actors.Single(a => a.Id == 1);
                var enemy = view.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0)
                    .OrderBy(a => CorePosition.DistanceSquared(player.Position, a.Position)).FirstOrDefault();
                CorePosition direction = enemy is null ? default : CombatProductionSmoke.MovementDirection(player.Position, enemy.Position, death.Room);
                Check("death_branch_accepts_ordinary_movement", death.Step(new CombatCommand(CombatCommandKind.Move, X: direction.X, Z: direction.Z)).Success);
            }
            Check("identity_memory_death_branch_reaches_real_death", death.Capture().Campaign.Deaths == snapshot.Campaign.Deaths + 1);
            _session = death; Refresh(); await Settle();
            Check("identity_memory_death_recovers_at_actual_anchor", death.ActiveEncounterId == snapshot.ActiveEncounterId &&
                death.Combat.View.Actors.Single(a => a.Id == 1) is { } recovered && recovered.Health == recovered.MaxHealth);
            Check("death_cleans_stale_echo_warnings", Echoes(death.Combat.View).Length == 0 &&
                !Descendants(_sandbox).OfType<Label3D>().Any(n => n.Name.ToString().StartsWith("EchoWarning_", StringComparison.Ordinal)));
            Check("death_rebuilds_current_region", _sandbox.EnvironmentStyle == "hollow_memory" && _sandbox.AmbienceCue == "hollow_memory" &&
                Descendants(_stage).OfType<Node3D>().Any(n => n.Name == "HollowNightArchitecture" && n.IsVisibleInTree()));
            Check("death_branch_replay_matches", CampaignRuntimeReplayRunner.Run(_combatJson, _adventure, _progression, _campaign, death.CaptureReplay()).Success);
        }
        finally { _session = live; Refresh(); await Settle(); }
        Check("cleanup_branches_do_not_modify_live_campaign", live.StateHash == hash && _sandbox.EnvironmentStyle == "hollow_memory");
        _cleanupChecked = true;
    }

    private async Task CheckBreachCycle(int phase)
    {
        // Preserve the route's real phase-entry checkpoint. Movement, dodge and potion
        // inputs let boss AI finish a complete warning cycle without attacking it.
        var live = _session; var snapshot = live.Capture(); string hash = live.StateHash;
        var branch = CampaignRuntimeSession.Restore(_combatJson, _adventure, _progression, _campaign, snapshot);
        Check("breach_phase_branch_restores_exactly_" + phase, branch.StateHash == hash);
        string signature = ""; int ticks = 0; long firstId = 0; bool returnSeen = false, returnOnly = false, sweepSeen = false, resolved = false, copiesSeen = false, copiesExpired = false;
        try
        {
            _session = branch;
            for (; ticks < 800 && (!resolved || phase == 1 && !copiesExpired); ticks++)
            {
                var input = CampaignCombatSmoke.Commands(branch.Combat.View, branch.Room)
                    .Where(c => c.Kind is CombatCommandKind.Move or CombatCommandKind.Stop or CombatCommandKind.Dodge or CombatCommandKind.Potion).ToArray();
                var result = branch.Step(input);
                Check("breach_branch_accepts_ordinary_defensive_input_" + phase, result.Success);
                Check("breach_branch_survives_without_changing_phase_" + phase, branch.Capture().Campaign.Deaths == snapshot.Campaign.Deaths &&
                    Boss(branch.Combat.View) is { Health: > 0 } && branch.Combat.View.BossPhase == phase);
                var boss = Boss(branch.Combat.View)!;
                bool liveCopies = branch.Combat.View.Actors.Any(a => a.DefinitionId == boss.DefinitionId && a.Id != boss.Id && a.Health > 0);
                copiesSeen |= liveCopies; copiesExpired |= copiesSeen && !liveCopies;
                var hazards = (branch.Combat.View.CampaignHazards ?? []).Where(h => h.SourceId == boss.Id).ToArray();
                var first = hazards.FirstOrDefault(h => h.ContentId == "campaign.breach_echo");
                if (firstId == 0 && first is not null) firstId = first.Id;
                returnSeen |= hazards.Any(h => h.ContentId == "campaign.returning_echo" && h.RemainingTicks > 0);
                returnOnly |= firstId != 0 && !hazards.Any(h => h.Id == firstId) && hazards.Any(h => h.ContentId == "campaign.returning_echo" && h.RemainingTicks > 0);
                sweepSeen |= hazards.Any(h => h.ContentId == "campaign.seal_sweep" && h.RemainingTicks > 0);
                resolved = firstId != 0 && !hazards.Any(h => h.Id == firstId || h.ContentId == "campaign.returning_echo");
                string next = Signature();
                if (signature != next)
                {
                    signature = next; Refresh(); _sandbox.PresentCombatEvents(result.CombatEvents, branch.Combat);
                    await ObserveEchoes("hollow_breach"); await ObserveBreach();
                }
            }
            _phaseBranchTicks.Add(phase, ticks);
            Check("breach_branch_resolves_actual_first_echo_" + phase, firstId != 0 && resolved);
            if (phase == 1) Check("mirrorborn_expiry_does_not_defeat_original_boss", copiesSeen && copiesExpired && Boss(branch.Combat.View) is { Health: > 0 } &&
                !Descendants(_stage).OfType<BreachHeartVisual>().Single().Defeated);
            Check("breach_branch_observes_returning_echo_only_after_phase_one_" + phase, returnSeen == (phase >= 2) && returnOnly == (phase >= 2));
            Check("breach_branch_observes_sweep_only_in_phase_three_" + phase, sweepSeen == (phase == 3));
            Check("breach_branch_replay_matches_" + phase, CampaignRuntimeReplayRunner.Run(_combatJson, _adventure, _progression, _campaign, branch.CaptureReplay()).Success);
        }
        finally { _session = live; Refresh(); await Settle(); }
        Check("breach_branch_does_not_modify_live_campaign_" + phase, live.StateHash == hash && _sandbox.EnvironmentStyle == "hollow_breach");
    }

    private Label3D ActorLabel(int id) => _sandbox.GetNode<Node3D>("Actor" + id).GetChildren().OfType<Label3D>().Single();

    private async Task ObserveBreach()
    {
        var breach = Descendants(_stage).OfType<BreachHeartVisual>().Single();
        var view = _session.Combat.View; var boss = Boss(view)!;
        var hazards = (view.CampaignHazards ?? []).Where(h => h.SourceId == boss.Id && h.RemainingTicks > 0).ToArray();
        bool echo = boss.Health > 0 && hazards.Any(h => h.ContentId == "campaign.breach_echo");
        bool returning = boss.Health > 0 && hazards.Any(h => h.ContentId == "campaign.returning_echo");
        bool sweep = boss.Health > 0 && hazards.Any(h => h.ContentId == "campaign.seal_sweep");
        int channels = ChannelCount(view); bool shield = boss.Shielded;
        if (boss.Health > 0) { _phases.Add(view.BossPhase); _channels.Add(channels); _shields.Add(shield); _liveBreach = view; }
        _returnOnlyObserved |= returning && !echo; _sweepObserved |= sweep;
        var livingCopies = view.Actors.Where(a => a.DefinitionId == "boss.breach_heart" && a.Id != boss.Id && a.Health > 0).ToArray();
        if (livingCopies.Length > 0)
        {
            _mirrorCopyObserved = true;
            Check("mirrorborn_copies_keep_distinct_actor_labels", livingCopies.All(a => ActorLabel(a.Id).Text.Contains("BREACH ECHO", StringComparison.Ordinal)));
            Check("mirrorborn_copies_do_not_replace_canonical_boss", breach.Defeated == (boss.Health <= 0));
        }
        Check("living_channels_keep_readable_mechanic_labels", view.Actors.Where(a => a.DefinitionId == "enemy.seal_channel" && a.Health > 0)
            .All(a => ActorLabel(a.Id) is { } label && label.IsVisibleInTree() && label.Text.Contains("BREAK SEAL", StringComparison.Ordinal)));
        if (boss.Health > 0)
            Check("canonical_boss_label_matches_channel_threshold", ActorLabel(boss.Id) is { } label && label.IsVisibleInTree() &&
                label.Text.Contains(shield ? "SEALED · 3 CHANNELS" : "BREACH EXPOSED", StringComparison.Ordinal));
        Check("breach_phase_matches_core", breach.Phase == view.BossPhase);
        Check("breach_channels_match_living_actors", breach.LivingChannels == channels);
        Check("breach_shield_matches_actual_threshold", breach.Shielded == shield);
        Check("breach_first_echo_matches_boss_owned_warning", breach.EchoWarning == echo);
        Check("breach_returning_echo_matches_boss_owned_warning", breach.ReturningEchoWarning == returning);
        Check("breach_sweep_matches_boss_owned_warning", breach.SweepWarning == sweep);
        Check("breach_has_bounded_articulation_and_meshes", breach.ArticulatedPartCount == 13 && breach.TransientCapacity == 16 && Meshes(breach).Length <= 100);
        Check("breach_active_fragments_never_exceed_pool", breach.ActiveTransientCount >= 0 && breach.ActiveTransientCount <= breach.TransientCapacity);
        if (!breach.Defeated) Check("live_breach_has_no_victory_fragments", breach.ActiveTransientCount == 0);
        Check("breach_cannot_change_collision", !Descendants(breach).Any(n => n is CollisionObject3D or CollisionShape3D or NavigationRegion3D));
        Check("breach_stays_beyond_back_wall", Meshes(breach).SelectMany(Vertices).All(p => p.Z < -_session.Room.HalfDepth * .001f + .001f));
        string signalState = $"{view.BossPhase}_{channels}_{echo}_{returning}_{sweep}";
        if (boss.Health > 0 && _reducedSignalStates.Add(signalState))
        {
            var reduced = BreachHeartVisual.Create(_session.Room.HalfWidth * .001f, _session.Room.HalfDepth * .001f, view);
            try
            {
                reduced.Animate(0, false, true); var pose = Pose(reduced);
                for (int i = 0; i < 10; i++) reduced.Animate(.1, false, true);
                Check("reduced_breach_preserves_exact_signals_" + signalState, SamePose(reduced, pose) && reduced.Phase == view.BossPhase &&
                    reduced.LivingChannels == channels && reduced.Shielded == shield && reduced.EchoWarning == echo && reduced.ReturningEchoWarning == returning &&
                    reduced.SweepWarning == sweep && reduced.ActiveTransientCount == 0);
            }
            finally { reduced.Free(); }
        }
        if (!_breachObserved)
        {
            _breachObserved = true;
            _sandbox.SetPaused(true); await Settle(); var pose = Pose(breach); string hash = _session.StateHash;
            for (int i = 0; i < 6; i++) breach.Animate(.1, true, false);
            await Settle(); Check("breach_pause_freezes_every_joint", SamePose(breach, pose) && _session.StateHash == hash);
            _sandbox.SetPaused(false);
        }
        if (!breach.Defeated)
        {
            await Capture("breach-phase-" + view.BossPhase + ".png");
            if (echo && returning) await Capture("breach-first-and-returning-echoes.png");
            if (returning && !echo) await Capture("breach-returning-echo.png");
            if (sweep) await Capture("breach-seal-sweep.png");
        }
        if (!breach.Defeated || _victoryObserved) return;
        _victoryObserved = true;
        Check("real_final_defeat_starts_finite_stabilization", boss.Health <= 0 && view.BossPhase == 3 && breach.IsTransitioning);
        await Capture("breach-victory-opening.png");
        _sandbox.SetPaused(true); await Settle(); var pausedPose = Pose(breach); float progress = breach.VictoryProgress;
        breach.Animate(10, true, false); await Settle();
        Check("victory_stabilization_freezes_with_pause", SamePose(breach, pausedPose) && breach.VictoryProgress == progress);
        _sandbox.SetPaused(false);
        for (int i = 0; i < 40; i++) breach.Animate(.1, false, false);
        Check("victory_stabilization_settles_with_bounded_fragments", !breach.IsTransitioning && breach.VictoryProgress == 1 && breach.ActiveTransientCount == 0);
        var settled = Pose(breach);
        for (int i = 0; i < 50; i++) breach.Animate(.1, false, false);
        Refresh(); Check("victory_does_not_repeat_on_refresh", SamePose(breach, settled) && !breach.IsTransitioning);
        if (DisplayServer.GetName() != "headless") await Frames(210);
        await Settle(); await Capture("breach-victory-settled.png"); CheckRestoreAndReducedEffects();
    }

    private void CheckRestoreAndReducedEffects()
    {
        var snapshot = _session.Capture(); string hash = _session.StateHash;
        var restored = CampaignRuntimeSession.Restore(_combatJson, _adventure, _progression, _campaign, snapshot);
        Check("breach_defeat_snapshot_restores_exactly", restored.StateHash == hash);
        var stage = new CampaignStage { Visible = false }; AddChild(stage);
        try
        {
            var player = restored.Combat.View.Actors.Single(a => a.Id == 1);
            stage.Show(snapshot.Campaign, restored.View, restored.Room, restored.Interactions, restored.Production.View.ActiveManifestations,
                restored.Production.ProgressionView.HubStage, player.Position, combat: restored.Combat.View, activeEncounterId: restored.ActiveEncounterId);
            var breach = Descendants(stage).OfType<BreachHeartVisual>().Single();
            Check("restored_victory_starts_settled", breach.Defeated && !breach.IsTransitioning && breach.VictoryProgress == 1 &&
                breach.ActiveTransientCount == 0 && stage.PresentedEncounter == restored.ActiveEncounterId);
        }
        finally { stage.Free(); }
        var reduced = BreachHeartVisual.Create(_session.Room.HalfWidth * .001f, _session.Room.HalfDepth * .001f, _liveBreach!);
        try
        {
            reduced.Animate(0, false, true); var pose = Pose(reduced);
            for (int i = 0; i < 10; i++) reduced.Animate(.1, false, true);
            Check("reduced_breach_retains_static_signals_without_motion", SamePose(reduced, pose));
            reduced.SetState(_session.Combat.View, true);
            Check("reduced_effects_skip_stabilization_transition", reduced.Defeated && !reduced.IsTransitioning && reduced.VictoryProgress == 1 && reduced.ActiveTransientCount == 0);
            var victoryPose = Pose(reduced);
            for (int i = 0; i < 10; i++) reduced.Animate(.1, false, false);
            Check("reenabling_effects_does_not_replay_victory", SamePose(reduced, victoryPose) && !reduced.IsTransitioning);
        }
        finally { reduced.Free(); }
        Check("restoration_and_reduced_effects_leave_live_core_unchanged", _session.StateHash == hash);
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
        Check("ambient_audio_respects_master_volume", audio.Bus == "Master" && audio.VolumeDb <= -20);
        Check("fog_begins_behind_nearby_combat", environment.FogEnabled && environment.FogDepthBegin >= 35 && environment.FogDepthEnd > environment.FogDepthBegin);
        if (DisplayServer.GetName() != "headless")
        {
            var before = motes.Multimesh.GetInstanceTransform(0); await Frames(8);
            Check("hollow_motes_move_when_playing", !motes.Multimesh.GetInstanceTransform(0).IsEqualApprox(before));
            float x = _session.Room.HalfWidth * .001f, z = _session.Room.HalfDepth * .001f;
            Check("hollow_motes_stay_outside_combat", Enumerable.Range(0, 24).Select(i => motes.Multimesh.GetInstanceTransform(i).Origin)
                .All(p => Math.Abs(p.X) > x || Math.Abs(p.Z) > z));
            _sandbox.SetPaused(true); await Settle();
            var frozen = motes.Multimesh.GetInstanceTransform(0); await Frames(8);
            Check("hollow_motes_freeze_with_pause", motes.Multimesh.GetInstanceTransform(0).IsEqualApprox(frozen));
            var expanded = _session.Room with { HalfWidth = 18000, HalfDepth = 15000 };
            _sandbox.PresentAuthoredRoom(expanded, "hollow:bounds-check", "hollow_rooms");
            Check("paused_same_style_resize_places_motes_immediately", Outside(expanded));
            expanded = expanded with { HalfWidth = 20000, HalfDepth = 18000 };
            _sandbox.PresentAuthoredRoom(expanded, "hollow:style-bounds-check", "hollow_breach");
            Check("paused_new_style_uses_current_bounds_immediately", Outside(expanded));
            Refresh();

            bool Outside(RoomDefinition room) => Enumerable.Range(0, 24).Select(i => motes.Multimesh.GetInstanceTransform(i).Origin)
                .All(p => Math.Abs(p.X) > room.HalfWidth * .001f || Math.Abs(p.Z) > room.HalfDepth * .001f);
        }
        else
        {
            _skippedChecks.AddRange(["hollow_motes_move_when_playing", "hollow_motes_stay_outside_combat", "hollow_motes_freeze_with_pause",
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
        foreach (string cue in HollowAmbience.CueNames)
        {
            var a = HollowAmbience.GetStream(cue); var b = HollowAmbience.GetStream(cue);
            byte[] data = a.Data;
            Check("ambient_" + cue + "_cached_loop", a.GetInstanceId() == b.GetInstanceId() && a.LoopMode == AudioStreamWav.LoopModeEnum.Forward &&
                a.LoopBegin == 0 && a.LoopEnd == HollowAmbience.SampleCount && a.MixRate == HollowAmbience.SampleRate && data.Length == HollowAmbience.SampleCount * 2);
            int peak = 0; long energy = 0;
            for (int i = 0; i < data.Length; i += 2)
            {
                int sample = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(i, 2));
                peak = Math.Max(peak, Math.Abs(sample)); energy += (long)sample * sample;
            }
            Check("ambient_" + cue + "_non_silent_and_unclipped", peak is > 100 and < 32000 && energy > 0);
            Check("ambient_" + cue + "_deterministic_pcm", data.SequenceEqual(HollowAmbience.CreateSamples(cue)));
            _audioFingerprints.Add(Convert.ToHexString(SHA256.HashData(data)));
        }
        Check("ambient_regions_have_distinct_audio", _audioFingerprints.Distinct().Count() == 3);
        bool rejected = false;
        try { HollowAmbience.GetStream("unrecognized-room"); } catch (ArgumentException) { rejected = true; }
        Check("ambient_cache_has_only_three_allowed_cues", rejected && HollowAmbience.CachedStreamCount == 3);
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
        if (!OS.GetCmdlineUserArgs().Contains("--capture-hollow") || DisplayServer.GetName() == "headless" || _captures.Contains(filename)) return;
        await Settle();
        // Occluded native windows can stop emitting FramePostDraw. Force the diagnostic
        // viewport to render, then validate the actual image and save result.
        RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var frame = GetViewport().GetTexture().GetImage();
        if (frame is null || frame.IsEmpty() || frame.GetWidth() <= 0 || frame.GetHeight() <= 0 || frame.SavePng(Path.Combine(_output, filename)) != Error.Ok)
            throw new IOException("Could not capture Hollow scene: " + filename);
        _captures.Add(filename);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Hollow check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "HollowClientSmokePassed" : "HollowClientSmokeFailed",
            passed,
            checks = _checks,
            skippedChecks = _skippedChecks,
            commands = _commands,
            finalStateHash = _session?.StateHash,
            environments = _environments,
            phases = _phases.Order().ToArray(),
            livingChannelCounts = _channels.Order().ToArray(),
            shieldStates = _shields.Order().ToArray(),
            echoTypes = _echoTypes.Order().ToArray(),
            deathBranchTicks = _deathBranchTicks,
            phaseBranchTicks = _phaseBranchTicks,
            audioFingerprints = _audioFingerprints,
            captures = _captures.Order().ToArray(),
            error,
            scope = "Real CampaignRuntimeSmoke commands unlock and complete Act V, its final choice, ending and return. Independent snapshots at each Breach Heart phase use ordinary movement, dodge and potion inputs without attacking so actual boss AI completes first/returning echo and phase-three sweep cycles. Another real identity-memory checkpoint independently returns to Greyhaven and takes ordinary incoming attacks until death to verify cleanup. Every branch is replayed and the unchanged live route restored. Echo labels are matched to Core warning identity, geometry, remaining time and lifetime including tick zero. Mesh vertices establish safe scenery/ground placement, shipping mouse planner detours and viewport clicks verify mint destination rings and X cancellation. Boss-owned warnings, exact living seal threshold, all phases, finite victory, restore, pause, reduced effects, audio cache, resource bounds, completed-region revisit and final replay are verified without fabricating combat state."
        };
        if (_output.Length > 0) System.IO.File.WriteAllText(Path.Combine(_output, "hollow-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
