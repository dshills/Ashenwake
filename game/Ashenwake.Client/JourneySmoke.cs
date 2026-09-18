using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Ashenwake.Client;

/// <summary>Real Core encounters supply the HUD; navigation and choice controls receive viewport input.</summary>
public partial class JourneySmoke : Node
{
    private CampaignRuntimeSession _session = null!;
    private CampaignHud _hud = null!;
    private Sandbox _sandbox = null!;
    private CampaignStage _stage = null!;
    private string _combatJson = "", _output = "";
    private CampaignContent _campaign = null!;
    private AdventureContent _adventure = null!;
    private ProgressionContent _progression = null!;
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _skippedChecks = [];
    private readonly List<EnvironmentEvidence> _environments = [];
    private long _revision;

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--journey-smoke") || _output.Length == 0) throw new InvalidDataException("Journey smoke requires --journey-smoke --output=<isolated-directory>.");
            Directory.CreateDirectory(_output); Engine.MaxFps = 60;
            _combatJson = CampaignCombatContent.Parse(Read("combat"), Read("campaign-combat")).CombatJson;
            _campaign = CampaignContent.Parse(Read("campaign")); _adventure = AdventureContent.Parse(Read("adventure")); _progression = ProgressionContent.Parse(Read("progression"));
            _session = CampaignRuntimeSession.Create(_combatJson, _adventure, _progression, _campaign);
            _sandbox = new Sandbox { ContentJsonOverride = _combatJson, AutomaticStep = true, AdvanceOverride = _ => [], SessionOverride = () => _session.Combat };
            AddChild(_sandbox); _sandbox.EnableCampaign(); _sandbox.SetSession(_session.Combat);
            _stage = new CampaignStage(); AddChild(_stage);
            _hud = new CampaignHud(); _sandbox.AddOverlay(_hud);
            _hud.ActRequested += act => Execute(new(CampaignRuntimeAction.EnterAct, Act: act));
            _hud.ContinueRequested += () => Execute(new(CampaignRuntimeAction.AdvanceEncounter));
            _hud.ChoiceRequested += (id, value) => Execute(new(CampaignRuntimeAction.Choose, Id: id, Value: value));
            _hud.HubRequested += () => Execute(new(CampaignRuntimeAction.ReturnToHub));
            Refresh(); await Settle();
            Check("greyhaven_environment_is_visible", VisibleArchitecture("GreyhavenArchitecture") is not null && VisibleArchitecture("GreyMarchArchitecture") is null);
            ObserveEnvironment("greyhaven", "GreyhavenArchitecture");
            await Click("Close"); await CheckAtmosphere(); await Capture("greyhaven-environment.png"); await Click("Journey map & anatomy");
            await Click("Act 1 ·");
            Check("map_starts_first_encounter", _session.ActiveEncounterId == "campaign.road" && !VisibleLabel("EDRATH ·"));
            var roadArchitecture = VisibleArchitecture("GreyMarchArchitecture");
            Check("road_replaces_hub_environment", roadArchitecture is not null && VisibleArchitecture("GreyhavenArchitecture") is null);
            ulong roadArchitectureId = roadArchitecture!.GetInstanceId();
            ObserveEnvironment("road", "GreyMarchArchitecture");
            Check("no_continue_prompt_during_combat", !NextStep().Visible);
            await Capture("first-encounter-characters.png");
            await FightUntil(() => _session.EncounterCleared);
            Check("clear_prompts_loot_and_next_step", NextStep().Visible && _session.Combat.View.Loot.Count > 0 && VisibleLabel($"{_session.Combat.View.Loot.Count} dropped"));
            await Capture("first-encounter-cleared.png");
            await ClickNode(NextStep());
            var continueButton = FindButton("Continue onward");
            Check("continue_is_visible_without_scrolling", continueButton.GetGlobalRect().Position.Y < 350 && continueButton.Text.Contains("uncollected drops", StringComparison.Ordinal));
            await ClickNode(continueButton);
            Check("continue_enters_monastery_and_closes_map", _session.ActiveEncounterId == "campaign.monastery" && !VisibleLabel("EDRATH ·"));
            var monasteryArchitecture = VisibleArchitecture("GreyMarchArchitecture");
            Check("monastery_replaces_road_environment", monasteryArchitecture is not null && monasteryArchitecture.GetInstanceId() != roadArchitectureId);
            ulong monasteryArchitectureId = monasteryArchitecture!.GetInstanceId();
            ObserveEnvironment("monastery", "GreyMarchArchitecture");
            await Capture("monastery-environment.png");
            await FightUntil(() => _session.EncounterCleared);
            Check("choice_is_explained_before_boss", NextStep().Text.StartsWith("Choose", StringComparison.Ordinal));
            await ClickNode(NextStep());
            var choice = _campaign.Capture().Choices.Single(c => c.Act == 1);
            await Click(choice.Outcomes[0].Text);
            var confirmation = Descendants(this).OfType<ConfirmationDialog>().Single();
            Check("story_choice_requires_confirmation", confirmation.Visible);
            confirmation.GetOkButton().GrabFocus();
            foreach (bool pressed in new[] { true, false })
            { confirmation.PushInput(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = pressed }, true); await Settle(); }
            Check("story_choice_commits_through_confirmation", _session.Capture().Campaign.Choices.GetValueOrDefault(choice.Id) == choice.Outcomes[0].Id);
            await ClickNode(NextStep()); await Click("Continue onward");
            Check("continue_enters_bell_saint", _session.ActiveEncounterId == "campaign.bell_saint");
            var sanctuaryArchitecture = VisibleArchitecture("GreyMarchArchitecture");
            Check("sanctuary_replaces_monastery_environment", sanctuaryArchitecture is not null && sanctuaryArchitecture.GetInstanceId() != monasteryArchitectureId);
            ObserveEnvironment("sanctum", "GreyMarchArchitecture");
            await Capture("bell-saint-character.png");
            await FightUntil(() => _session.Combat.View.BossPhase == 2);
            Check("bell_shield_explains_two_anchors", VisibleLabel("BELL SAINT PROTECTED · Destroy 2 ritual anchors"));
            Check("both_anchors_have_persistent_labels", Descendants(this).OfType<Label3D>().Count(l => l.IsVisibleInTree() && l.Visible && l.Text.StartsWith("RITUAL ANCHOR\n", StringComparison.Ordinal) && l.Text.Contains("DESTROY", StringComparison.Ordinal)) == 2);
            await Capture("bell-saint-anchors.png");
            await FightUntil(() => _session.Combat.View.Actors.Count(a => a.DefinitionId == "enemy.ritual_anchor" && a.Health > 0) == 1);
            Check("anchor_guidance_updates_after_first_kill", VisibleLabel("BELL SAINT PROTECTED · Destroy 1 ritual anchor."));
            await FightUntil(() => !_session.Combat.View.Actors.Any(a => a.DefinitionId == "enemy.ritual_anchor" && a.Health > 0));
            Check("broken_ritual_exposes_damage_window", VisibleLabel("RITUAL BROKEN ·"));
            await FightUntil(() => _session.Combat.View.BossPhase == 3 || _session.EncounterCleared);
            ObserveEnvironment("sanctum_unbound", "GreyMarchArchitecture");
            await Capture("bell-saint-unbound.png");
            await FightUntil(() => _session.EncounterCleared);
            Check("boss_victory_points_to_next_region", NextStep().Text.StartsWith("Region complete", StringComparison.Ordinal));
            await ClickNode(NextStep());
            Check("region_complete_opens_map", VisibleLabel("EDRATH ·"));
            Check("completed_region_has_no_dead_end_continue", !Descendants(this).OfType<Button>().Any(b => b.IsVisibleInTree() && b.Text.StartsWith("Continue onward", StringComparison.Ordinal)));
            Check("opening_decorations_preserve_authoritative_collision", _environments.All(e => e.CosmeticOnly));
            Check("opening_ground_stays_below_combat_tells", _environments.All(e => e.GroundMeshes > 0 && e.GroundTop is { } top && float.IsFinite(top) && top < .04f));
            Check("opening_architecture_has_bounded_material_batches", _environments.All(e => e.ArchitectureMeshes > 0 && e.ArchitectureMaterials is > 0 and <= 32));
            await Click("Travel to Act 2 ·");
            Check("next_region_button_enters_act_two", _session.Capture().Campaign.CurrentAct == 2 && !_session.InHub);
            var replay = CampaignRuntimeReplayRunner.Run(_combatJson, _adventure, _progression, _campaign, _session.CaptureReplay());
            Check("navigation_and_combat_replay", replay.Success);
            Finish(true, "");
        }
        catch (Exception ex) { await Capture("journey-failure.png"); GD.PushError(ex.ToString()); Finish(false, ex.Message); }
    }

    private static string Read(string name) => FileAccess.GetFileAsString("res://" + name + ".json");
    private void Execute(CampaignRuntimeCommand command)
    { var result = _session.Execute(command); if (!result.Success) throw new InvalidDataException(result.Reason); _revision++; Refresh(); }
    private void Refresh()
    {
        var snapshot = _session.Capture(); var player = _session.Combat.View.Actors.Single(a => a.Id == 1);
        var interactions = _session.Interactions.Select(i => new InteractionDisplay(i.ActionId, i.Name,
            (int)Math.Sqrt(Ashenwake.Core.Simulation.Position.DistanceSquared(player.Position, i.Position)), i.Range)).ToArray();
        _sandbox.AdoptSession(_session.Combat);
        string style = _session.InHub ? "greyhaven" : _session.ActiveEncounterId switch
        { "campaign.road" => "road", "campaign.monastery" => "monastery", "campaign.bell_saint" => "sanctum", _ => "default" };
        _sandbox.PresentAuthoredRoom(_session.Room, $"journey:{_session.InHub}:{_session.ActiveEncounterId}:{snapshot.Campaign.Deaths}", style);
        _sandbox.SetEnvironmentStyle(style);
        var manifestations = _session.Production.View.ActiveManifestations;
        _stage.Show(snapshot.Campaign, _session.View, _session.Room, _session.Interactions, manifestations,
            _session.Production.ProgressionView.HubStage, player.Position, _session.Combat.View.BossPhase);
        _sandbox.SetManifestationPresentation(manifestations);
        _sandbox.SetWorldSubtitle($"CAMPAIGN / {_session.View.Region.ToUpperInvariant()}");
        _hud.SetView(_session.View, snapshot.Campaign, _campaign.Capture(), _session.Production.View, snapshot.Production.Expedition.Adventure,
            _session.Production.AdventureContent.Capture(), _session.Combat.View, interactions, _revision);
    }
    private Node3D? VisibleArchitecture(string name) => Descendants(_stage).OfType<Node3D>().SingleOrDefault(n => n.Name == name && n.IsVisibleInTree());
    private void ObserveEnvironment(string district, string architectureName)
    {
        var architecture = VisibleArchitecture(architectureName)!;
        var meshes = Descendants(architecture).OfType<MeshInstance3D>().Where(m => m.Mesh is not null && m.IsVisibleInTree()).ToArray();
        var materials = meshes.SelectMany(m => Enumerable.Range(0, m.Mesh.GetSurfaceCount()).Select(m.GetActiveMaterial))
            .Where(m => m is not null).Select(m => m.GetInstanceId()).Distinct().Count();
        var ground = Descendants(_sandbox).OfType<Node3D>().SingleOrDefault(n => n.Name == "AuthoredGround");
        var groundMeshes = ground is null ? [] : Descendants(ground).OfType<MeshInstance3D>().Where(m => m.Mesh is not null).ToArray();
        float? top = groundMeshes.Length == 0 ? null : groundMeshes.SelectMany(m => Enumerable.Range(0, 8).Select(i => (m.GlobalTransform * m.Mesh.GetAabb().GetEndpoint(i)).Y)).Max();
        if (top is { } value && !float.IsFinite(value)) top = null;
        bool cosmeticOnly = !Descendants(_stage).Any(n => n is CollisionObject3D or CollisionShape3D or NavigationRegion3D);
        _environments.Add(new(district, meshes.Length, materials, groundMeshes.Length, top, cosmeticOnly));
    }
    private sealed record EnvironmentEvidence(string District, int ArchitectureMeshes, int ArchitectureMaterials, int GroundMeshes, float? GroundTop, bool CosmeticOnly);
    private async Task CheckAtmosphere()
    {
        var motes = Descendants(_sandbox).OfType<MultiMeshInstance3D>().Single(n => n.Name == "AmbientMotes");
        var environment = Descendants(_sandbox).OfType<WorldEnvironment>().Single().Environment;
        var effects = Descendants(_sandbox).OfType<CheckButton>().Single(b => b.Text == "Reduced visual effects");
        bool originalReducedEffects = effects.ButtonPressed;
        if (originalReducedEffects)
        { await Click("Settings [Esc]"); await ClickNode(effects); await Click("Close settings"); }
        bool rendered = DisplayServer.GetName() != "headless";
        if (rendered)
        {
            var movingPose = motes.Multimesh.GetInstanceTransform(0);
            for (int i = 0; i < 4; i++) await Settle();
            Check("ambient_motes_animate_when_playing", !_sandbox.IsPaused && motes.IsVisibleInTree() && !motes.Multimesh.GetInstanceTransform(0).IsEqualApprox(movingPose));
            _sandbox.SetPaused(true); var frozenPose = motes.Multimesh.GetInstanceTransform(0);
            for (int i = 0; i < 4; i++) await Settle();
            bool frozen = _sandbox.IsPaused && motes.Multimesh.GetInstanceTransform(0).IsEqualApprox(frozenPose);
            _sandbox.SetPaused(false);
            for (int i = 0; i < 4; i++) await Settle();
            Check("ambient_motes_pause_and_resume_with_game", frozen && !_sandbox.IsPaused && !motes.Multimesh.GetInstanceTransform(0).IsEqualApprox(frozenPose));
        }
        else
        {
            // Godot's dummy renderer returns identity for MultiMesh transform readback.
            // Record these as skipped; the rendered journey must establish actual animation and freezing.
            _skippedChecks.Add("ambient_motes_animate_when_playing");
            _skippedChecks.Add("ambient_motes_pause_and_resume_with_game");
        }
        await Click("Settings [Esc]"); await ClickNode(effects);
        Check("reduced_effects_disable_environment_atmosphere", effects.ButtonPressed && !motes.IsVisibleInTree() && !environment.FogEnabled);
        await ClickNode(effects); await Click("Close settings");
        var pose = motes.Multimesh.GetInstanceTransform(0);
        for (int i = 0; i < 4; i++) await Settle();
        Check("restoring_effects_restores_environment_atmosphere", !effects.ButtonPressed && !_sandbox.IsPaused && motes.IsVisibleInTree() && environment.FogEnabled && (!rendered || !motes.Multimesh.GetInstanceTransform(0).IsEqualApprox(pose)));
        if (originalReducedEffects)
        { await Click("Settings [Esc]"); await ClickNode(effects); await Click("Close settings"); }
    }
    private async Task FightUntil(Func<bool> complete)
    {
        for (int i = 0; i < 6000 && !complete(); i++)
        {
            var result = _session.Step(CampaignCombatSmoke.Commands(_session.Combat.View, _session.Room));
            if (!result.Success || _session.Capture().Campaign.Deaths > 0) throw new InvalidDataException("Journey combat route failed.");
            if (i % 60 == 0) { _revision++; Refresh(); await Settle(); }
        }
        if (!complete()) throw new InvalidDataException("Journey combat route exceeded its bound.");
        _revision++; Refresh(); await Settle();
    }
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private bool VisibleLabel(string prefix) => Descendants(this).OfType<Label>().Any(l => l.IsVisibleInTree() && l.Text.StartsWith(prefix, StringComparison.Ordinal));
    private Button NextStep() => Descendants(this).OfType<Button>().Single(b => b.Name == "CampaignNextStep");
    private Button FindButton(string text) => Descendants(this).OfType<Button>().First(b => b.IsVisibleInTree() && b.Text.Contains(text, StringComparison.Ordinal));
    private Task Click(string text) => ClickNode(FindButton(text));
    private async Task ClickNode(Button button)
    {
        if (button.Disabled || !button.IsVisibleInTree()) throw new InvalidDataException("Requested journey control is unavailable.");
        for (Node? parent = button.GetParent(); parent is not null; parent = parent.GetParent())
            if (parent is ScrollContainer scroll) { scroll.EnsureControlVisible(button); await Settle(); break; }
        Vector2 position = button.GetGlobalRect().GetCenter();
        var viewport = button.GetViewport();
        viewport.PushInput(new InputEventMouseMotion { Position = position }, true);
        // Deliver a complete viewport click without holding it across frames of native pointer input.
        foreach (bool pressed in new[] { true, false })
            viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = position, Pressed = pressed }, true);
        await Settle();
    }
    private async Task Settle() { for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Capture(string filename)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--capture-journey") || DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_output, filename));
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Journey check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "JourneyClientSmokePassed" : "JourneyClientSmokeFailed",
            passed,
            checks = _checks,
            skippedChecks = _skippedChecks,
            environments = _environments,
            error,
            scope = "Real Core combat commands generate encounter states; viewport input exercises HUD navigation and story confirmation. Production stage builders and authored floors are inspected across Greyhaven and all three opening encounters."
        };
        if (_output.Length > 0) System.IO.File.WriteAllText(Path.Combine(_output, "journey-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
