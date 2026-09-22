using System.Globalization;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Simulation;
using Godot;

namespace Ashenwake.Client;

/// <summary>Earns the opening through public commands, then presents exact live chant geometry
/// and a separately replayed uninterrupted branch. No actors, gear or deadlines are injected.</summary>
public partial class OpeningCombatSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [], _skipped = [];
    private readonly List<object> _evidence = [];
    private readonly HashSet<string> _rooms = [];
    private readonly HashSet<int> _phases = [];
    private string _output = "", _combat = "", _finalHash = "", _replayHash = "";
    private int _commands, _branchCommands, _revision;
    private bool _writeReport, _rallyObserved;
    private CampaignContent _campaign = null!;
    private AdventureContent _adventure = null!;
    private ProgressionContent _progression = null!;
    private CampaignRuntimeSession _session = null!;
    private Sandbox _sandbox = null!;
    private CampaignStage _stage = null!;
    private CampaignHud _hud = null!;
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://" + name + ".json");

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--opening-combat-smoke") || _output.Length == 0)
                throw new InvalidDataException("Opening combat smoke requires --opening-combat-smoke --output=<fresh-artifact-directory>.");
            if (Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Opening combat smoke requires a fresh artifact directory.");
            Directory.CreateDirectory(_output); _writeReport = true;
            Engine.MaxFps = 60; GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _combat = CampaignCombatContent.Parse(Read("combat"), Read("campaign-combat")).CombatJson;
            _campaign = CampaignContent.Parse(Read("campaign")); _adventure = AdventureContent.Parse(Read("adventure"));
            _progression = ProgressionContent.Parse(Read("progression"));
            _session = CampaignRuntimeSession.Create(_combat, _adventure, _progression, _campaign);
            _sandbox = new Sandbox { ContentJsonOverride = _combat, AutomaticStep = true, AdvanceOverride = _ => [], SessionOverride = () => _session.Combat };
            AddChild(_sandbox); _sandbox.SetProcess(false); _sandbox.EnableCampaign(); _sandbox.SetSession(_session.Combat);
            _stage = new CampaignStage(); AddChild(_stage); _hud = new CampaignHud(); _sandbox.AddOverlay(_hud);
            _sandbox.SetGraphicsQuality("High"); Refresh();
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            await Frames(4);
            var reduced = Descendants(_sandbox).OfType<CheckButton>().Single(b => b.Text == "Reduced visual effects");
            reduced.ButtonPressed = false;
            for (int i = 0; i < 14000 && !_session.Capture().Campaign.CompletedActs.Contains(1); i++)
            {
                var command = CampaignRuntimeSmoke.Next(_session);
                var result = _session.Execute(command); _commands++;
                Check("earned_commands_are_accepted", result.Success);
                Check("earned_opening_has_no_deaths", _session.Capture().Campaign.Deaths == 0);
                string hash = _session.StateHash;
                _sandbox.PresentCombatEvents(result.CombatEvents, _session.Combat); Refresh(); _sandbox._Process(1d / 30);
                Check("presentation_preserves_authoritative_hash", _session.StateHash == hash);
                if (_session.ActiveEncounterId == "campaign.bell_saint") _phases.Add(_session.Combat.View.BossPhase);
                if (_rooms.Add(_session.ActiveEncounterId))
                {
                    _evidence.Add(new
                    {
                        encounter = _session.ActiveEncounterId,
                        level = _session.Production.ProgressionView.Level,
                        enemies = _session.Combat.View.Actors.Where(a => a.Faction == CombatFaction.Enemy).Select(a => new { a.Id, a.DefinitionId, a.Position, a.EliteModifiers }).ToArray()
                    });
                    await Capture(_session.ActiveEncounterId.Replace('.', '-') + ".png");
                }
                if (!_rallyObserved && _session.ActiveEncounterId == "campaign.monastery") await WitnessMonasteryChant(reduced);
                if (i % 30 == 0) await Frames(1);
            }
            Check("earned_all_main_opening_encounters_and_crypt", _session.Capture().Campaign.CompletedActs.Contains(1) &&
                new[] { "campaign.road", "campaign.monastery", "campaign.bell_saint", "exploration.widow_crypt" }.All(_rooms.Contains));
            Check("all_three_bell_phases_witnessed", new[] { 1, 2, 3 }.All(_phases.Contains));
            Check("earned_actual_dirge_warning_and_resolution", _rallyObserved);
            await Capture("opening-complete.png");
            var replay = _session.CaptureReplay(); var verified = CampaignRuntimeReplayRunner.Run(_combat, _adventure, _progression, _campaign, replay);
            Check("opening_command_replay_matches", verified.Success && verified.FinalHash == _session.StateHash);
            _finalHash = _session.StateHash; _replayHash = verified.FinalHash;
            System.IO.File.WriteAllText(Path.Combine(_output, "opening-combat.awcampaign"), JsonData.Write(replay));
            string save = JsonData.Write(new CampaignRuntimeSave(1, _session.StateHash, _session.Capture()));
            System.IO.File.WriteAllText(Path.Combine(_output, "opening-combat.save.json"), save);
            Check("earned_save_restores_identically", CampaignRuntimeSaveStore.Read(_combat, _adventure, _progression, _campaign, save).StateHash == _session.StateHash);
            _sandbox.QueueFree(); _stage.QueueFree(); await Frames(3); Finish(true, "");
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); Finish(false, ex.Message); }
    }

    private async Task WitnessMonasteryChant(CheckButton reduced)
    {
        // A normal fast kill can prevent this support ability altogether. Deliberately
        // hold position on earned room entry to witness the telegraph, paying actual
        // incoming damage and recording every public command in the accepted route.
        for (int tick = 0; tick < 180; tick++)
        {
            var result = _session.Step([new(CombatCommandKind.Stop)]); _commands++;
            Check("monastery_witness_commands_are_accepted", result.Success);
            Check("monastery_witness_remains_alive", _session.Combat.View.Actors.Single(a => a.Id == 1).Health > 0 && _session.Capture().Campaign.Deaths == 0);
            _sandbox.PresentCombatEvents(result.CombatEvents, _session.Combat); Refresh(); _sandbox._Process(1d / 30);
            if (_session.Combat.View.CampaignHazards?.Any(h => h.ContentId == "elite.dirgebound" && h.RemainingTicks > 0) == true)
            {
                Check("earned_chant_has_no_pending_player_attack", _session.Combat.View.Actors.Single(a => a.Id == 1).TelegraphTicks == 0);
                await CheckRally(reduced); return;
            }
            if (tick % 30 == 0) await Frames(1);
        }
        Check("monastery_exposes_dirge_within_bounded_wait", false);
    }

    private async Task CheckRally(CheckButton reduced)
    {
        var original = _session; string originalHash = original.StateHash;
        var warning = original.Combat.View.CampaignHazards!.Single(h => h.ContentId == "elite.dirgebound");
        void Inspect(string context)
        {
            var rim = Descendants(_sandbox).OfType<MeshInstance3D>().Single(n => n.Name == "DirgeWarning_" + warning.Id);
            var countdown = rim.GetNode<Label3D>("DirgeCountdown");
            var actor = _sandbox.GetNode<Node3D>("Actor" + warning.SourceId);
            var actorLabel = actor.GetChildren().OfType<Label3D>().Single();
            double seconds = Math.Ceiling(warning.RemainingTicks * FixedStepClock.SecondsPerTick * 10) / 10;
            Check(context + "_uses_exact_hollow_geometry", rim.Mesh is TorusMesh mesh &&
                Mathf.IsEqualApprox(mesh.OuterRadius, warning.Radius * .001f) &&
                rim.Position.IsEqualApprox(new(warning.Position.X * .001f, .075f, warning.Position.Z * .001f)));
            Check(context + "_uses_exact_authoritative_countdown", countdown.Text == "ENEMY WARD\n" + seconds.ToString("F1", CultureInfo.InvariantCulture) + "s");
            Check(context + "_interrupt_label_remains_visible", actorLabel.IsVisibleInTree() && actorLabel.Text.Contains("DIRGE · INTERRUPT", StringComparison.Ordinal) && !actorLabel.Text.Contains("ATTACK INCOMING", StringComparison.Ordinal));
            Check(context + "_support_is_not_a_filled_damage_warning", !Descendants(_sandbox).OfType<MeshInstance3D>().Any(n =>
                n.Mesh is CylinderMesh && n.Position.IsEqualApprox(new(warning.Position.X * .001f, .04f, warning.Position.Z * .001f)) &&
                n.MaterialOverride is StandardMaterial3D mat && mat.AlbedoColor.R > .95f && mat.AlbedoColor.G < .65f));
            Check(context + "_warning_is_independent_of_effect_preset", rim.IsVisibleInTree() && countdown.IsVisibleInTree());
            Check(context + "_core_is_unchanged", _session.StateHash == originalHash);
        }
        Inspect("high"); await Capture("dirge-chant-high.png");
        _sandbox.SetModalPaused("opening-combat-check", true);
        _sandbox.SetGraphicsQuality("Performance"); reduced.ButtonPressed = true; _sandbox._Process(1d / 30);
        Inspect("reduced_performance"); await Capture("dirge-chant-reduced.png");
        _sandbox.SetGraphicsQuality("High"); reduced.ButtonPressed = false;
        _sandbox.SetModalPaused("opening-combat-check", false);
        _session = CampaignRuntimeSession.Restore(_combat, _adventure, _progression, _campaign, original.Capture());
        _sandbox.SetSession(_session.Combat); Refresh();
        var granted = new List<CombatEvent>();
        for (int i = 0; i <= warning.RemainingTicks; i++)
        {
            var result = _session.Step([]); _branchCommands++;
            Check("uninterrupted_branch_steps_are_accepted", result.Success);
            granted.AddRange(result.CombatEvents.Where(e => e.Kind == "BarrierGranted" && e.ContentId == "elite.dirgebound"));
            _sandbox.PresentCombatEvents(result.CombatEvents, _session.Combat); Refresh(); _sandbox._Process(1d / 30);
        }
        Check("actual_formation_receives_bounded_ally_barriers", granted.Count > 0 && granted.All(e => e.TargetId != warning.SourceId && e.Amount is > 0 and <= 24));
        _sandbox.SetHoveredActor(0); _sandbox._Process(0);
        var warded = granted.Select(e => _session.Combat.View.Actors.Single(a => a.Id == e.TargetId)).ToArray();
        foreach (var e in granted)
        {
            var ally = _session.Combat.View.Actors.Single(a => a.Id == e.TargetId);
            var strip = _sandbox.GetNode<Sprite3D>("Actor" + ally.Id + "/ActorHealthBar/BarrierStrip");
            Check("actual_barrier_strip_" + ally.Id, ally.Barrier is > 0 and <= 24 && strip.IsVisibleInTree() &&
                strip.Modulate == new Color("a1ecda") && strip.RegionRect.Size.X is >= 12 and <= 96 && strip.RegionRect.Size.Y == 3 &&
                strip.Texture is not null && strip.NoDepthTest && !strip.Shaded);
        }
        Check("unfocused_wards_do_not_force_full_names", warded.Count(ally => _sandbox.GetNode<Node3D>("Actor" + ally.Id).GetChildren().OfType<Label3D>().Single().IsVisibleInTree()) <= 1);
        Check("ward_resolution_does_not_spawn_large_duplicate_text", !Descendants(_sandbox).OfType<Label3D>().Any(label => label.Text.StartsWith("WARD +", StringComparison.Ordinal)));
        Check("resolved_warning_visual_is_removed", !Descendants(_sandbox).OfType<MeshInstance3D>().Any(n => n.Name == "DirgeWarning_" + warning.Id && !n.IsQueuedForDeletion()));
        Check("caster_receives_no_self_ward", _session.Combat.View.Actors.Single(a => a.Id == warning.SourceId).Barrier == 0);
        Check("unwarded_caster_has_no_barrier_strip", !_sandbox.GetNode<Sprite3D>("Actor" + warning.SourceId + "/ActorHealthBar/BarrierStrip").IsVisibleInTree());
        string wardHash = _session.StateHash;
        _sandbox.SetHoveredActor(warded[0].Id); _sandbox._Process(0);
        var focus = Descendants(_sandbox).OfType<PanelContainer>().Single(node => node.Name == "CombatTargetDetail");
        var focusedLabels = Descendants(focus).OfType<Label>().ToArray();
        Check("focused_ward_displays_exact_current_barrier", focus.IsVisibleInTree() &&
            focusedLabels.Single(label => label.Name == "TargetName").Text == _sandbox.GetNode<Node3D>("Actor" + warded[0].Id).GetChildren().OfType<Label3D>().Single().Text.Split('\n')[0] &&
            focusedLabels.Single(label => label.Name == "TargetConditions").Text.Contains("BARRIER " + warded[0].Barrier, StringComparison.Ordinal));
        Check("ward_focus_does_not_change_combat", _session.StateHash == wardHash);
        await Capture("dirge-allies-warded.png");
        _sandbox.SetGraphicsQuality("Performance"); reduced.ButtonPressed = true; _sandbox.SetHoveredActor(warded[0].Id); _sandbox._Process(0);
        Check("reduced_performance_preserves_compact_ward_strips", warded.All(ally => _sandbox.GetNode<Sprite3D>("Actor" + ally.Id + "/ActorHealthBar/BarrierStrip").IsVisibleInTree()) && _session.StateHash == wardHash);
        _sandbox.SetGraphicsQuality("High"); reduced.ButtonPressed = false;
        _evidence.Add(new { kind = "uninterrupted-dirge", warning, granted, finalHash = _session.StateHash });
        var branchReplay = _session.CaptureReplay();
        var verified = CampaignRuntimeReplayRunner.Run(_combat, _adventure, _progression, _campaign, branchReplay);
        Check("uninterrupted_branch_replays_identically", verified.Success && verified.FinalHash == _session.StateHash);
        System.IO.File.WriteAllText(Path.Combine(_output, "dirge-uninterrupted.awcampaign"), JsonData.Write(branchReplay));
        _session = original; _sandbox.SetSession(_session.Combat); Refresh(); _sandbox._Process(0);
        Check("branch_never_changes_earned_route", _session.StateHash == originalHash);
        _rallyObserved = true;
    }

    private void Refresh()
    {
        var snapshot = _session.Capture(); var state = snapshot.Campaign; var view = _session.Combat.View;
        var player = view.Actors.Single(a => a.Id == 1);
        string style = EnvironmentGround.Style(_session.InHub, _session.ActiveEncounterId, state.Exploration?.Id, state.CurrentAct);
        _sandbox.AdoptSession(_session.Combat); _sandbox.PresentAuthoredRoom(_session.Room, "opening-combat:" + _session.ActiveEncounterId, style);
        _stage.Show(state, _session.View, _session.Room, _session.Interactions, _session.Production.View.ActiveManifestations,
            _session.Production.ProgressionView.HubStage, player.Position, view.BossPhase,
            _session.ActiveEncounterId == "campaign.bell_saint" && _session.EncounterCleared, view, _session.ActiveEncounterId);
        var targets = _session.Interactions.Select(i => new WorldInteractionTarget(i.ActionId, i.Name, i.Position, i.Range, _stage.GetInteractionVisual(i.ActionId))).ToArray();
        _sandbox.SetWorldInteractions(targets, _ => { });
        var interactions = _session.Interactions.Select(i => new InteractionDisplay(i.ActionId, i.Name,
            (int)Math.Sqrt(Position.DistanceSquared(player.Position, i.Position)), i.Range)).ToArray();
        _hud.SetView(_session.View, state, _campaign.Capture(), _session.Production.View, snapshot.Production.Expedition.Adventure,
            _session.Production.AdventureContent.Capture(), view, interactions, ++_revision, snapshot.Production.Progression);
        _hud.SetOpen(false); _sandbox.SetWorldSubtitle("ACT I / " + _session.View.Region.ToUpperInvariant());
    }
    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private async Task Capture(string name)
    {
        await Frames(2);
        if (!OS.GetCmdlineUserArgs().Contains("--capture-opening-combat")) return;
        if (DisplayServer.GetName() == "headless") { if (!_skipped.Contains("native screenshots")) _skipped.Add("native screenshots"); return; }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + name, image.GetWidth() > 0 && image.GetHeight() > 0 && image.SavePng(Path.Combine(_output, name)) == Error.Ok);
        _captures.Add(name);
    }
    private async Task Frames(int count)
    { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void Check(string name, bool passed)
    { _checks[name] = passed; if (!passed) throw new InvalidDataException("Opening combat check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "OpeningCombatClientSmokePassed" : "OpeningCombatClientSmokeFailed",
            passed,
            checks = _checks,
            commands = _commands,
            branchCommands = _branchCommands,
            rooms = _rooms.Order().ToArray(),
            phases = _phases.Order().ToArray(),
            evidence = _evidence,
            finalHash = _finalHash,
            replayHash = _replayHash,
            captures = _captures,
            skippedChecks = _skipped,
            error,
            scope = "Actual earned Act I commands, authoritative geometry/countdowns, native High/Reduced Performance labels and bounded ally wards, separately replayed uninterrupted branch, final save/command replay. Scripted Vanguard play does not substitute for human playtesting."
        };
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "opening-combat-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
