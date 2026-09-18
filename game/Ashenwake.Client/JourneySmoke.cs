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
    private string _combatJson = "", _output = "";
    private CampaignContent _campaign = null!;
    private AdventureContent _adventure = null!;
    private ProgressionContent _progression = null!;
    private readonly Dictionary<string, bool> _checks = [];
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
            _hud = new CampaignHud(); _sandbox.AddOverlay(_hud);
            _hud.ActRequested += act => Execute(new(CampaignRuntimeAction.EnterAct, Act: act));
            _hud.ContinueRequested += () => Execute(new(CampaignRuntimeAction.AdvanceEncounter));
            _hud.ChoiceRequested += (id, value) => Execute(new(CampaignRuntimeAction.Choose, Id: id, Value: value));
            _hud.HubRequested += () => Execute(new(CampaignRuntimeAction.ReturnToHub));
            Refresh(); await Settle();
            await Click("Act 1 ·");
            Check("map_starts_first_encounter", _session.ActiveEncounterId == "campaign.road" && !VisibleLabel("EDRATH ·"));
            Check("no_continue_prompt_during_combat", !NextStep().Visible);
            await FightUntil(() => _session.EncounterCleared);
            Check("clear_prompts_loot_and_next_step", NextStep().Visible && _session.Combat.View.Loot.Count > 0 && VisibleLabel($"{_session.Combat.View.Loot.Count} dropped"));
            await Capture("first-encounter-cleared.png");
            await ClickNode(NextStep());
            var continueButton = FindButton("Continue onward");
            Check("continue_is_visible_without_scrolling", continueButton.GetGlobalRect().Position.Y < 350 && continueButton.Text.Contains("uncollected drops", StringComparison.Ordinal));
            await ClickNode(continueButton);
            Check("continue_enters_monastery_and_closes_map", _session.ActiveEncounterId == "campaign.monastery" && !VisibleLabel("EDRATH ·"));
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
            await FightUntil(() => _session.Combat.View.BossPhase == 2);
            Check("bell_shield_explains_two_anchors", VisibleLabel("BELL SAINT PROTECTED · Destroy 2 ritual anchors"));
            Check("both_anchors_have_persistent_labels", Descendants(this).OfType<Label3D>().Count(l => l.IsVisibleInTree() && l.Visible && l.Text.StartsWith("RITUAL ANCHOR\n", StringComparison.Ordinal) && l.Text.Contains("DESTROY", StringComparison.Ordinal)) == 2);
            await Capture("bell-saint-anchors.png");
            await FightUntil(() => _session.Combat.View.Actors.Count(a => a.DefinitionId == "enemy.ritual_anchor" && a.Health > 0) == 1);
            Check("anchor_guidance_updates_after_first_kill", VisibleLabel("BELL SAINT PROTECTED · Destroy 1 ritual anchor."));
            await FightUntil(() => !_session.Combat.View.Actors.Any(a => a.DefinitionId == "enemy.ritual_anchor" && a.Health > 0));
            Check("broken_ritual_exposes_damage_window", VisibleLabel("RITUAL BROKEN ·"));
            await FightUntil(() => _session.EncounterCleared);
            Check("boss_victory_points_to_next_region", NextStep().Text.StartsWith("Region complete", StringComparison.Ordinal));
            await ClickNode(NextStep());
            Check("completed_region_has_no_dead_end_continue", !Descendants(this).OfType<Button>().Any(b => b.IsVisibleInTree() && b.Text.StartsWith("Continue onward", StringComparison.Ordinal)));
            await Click("Travel to Act 2 ·");
            Check("next_region_button_enters_act_two", _session.Capture().Campaign.CurrentAct == 2 && !_session.InHub);
            var replay = CampaignRuntimeReplayRunner.Run(_combatJson, _adventure, _progression, _campaign, _session.CaptureReplay());
            Check("navigation_and_combat_replay", replay.Success);
            Finish(true, "");
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); Finish(false, ex.Message); }
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
        _hud.SetView(_session.View, snapshot.Campaign, _campaign.Capture(), _session.Production.View, snapshot.Production.Expedition.Adventure,
            _session.Production.AdventureContent.Capture(), _session.Combat.View, interactions, _revision);
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
        foreach (bool pressed in new[] { true, false })
        { button.GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = position, Pressed = pressed }, true); await Settle(); }
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
            error,
            scope = "Real Core combat commands generate encounter states; viewport mouse and keyboard input exercises HUD navigation and the story confirmation."
        };
        if (_output.Length > 0) System.IO.File.WriteAllText(Path.Combine(_output, "journey-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
