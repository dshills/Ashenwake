using System.Reflection;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Training;
using Position = Ashenwake.Core.Simulation.Position;
using Godot;

namespace Ashenwake.Client;

/// <summary>Ordinary earned Act I progression and real viewport controls for optional guidance.</summary>
public partial class OpeningGuidanceSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private string _output = "", _lastClick = "";
    private int _commands, _clicks;
    private bool _writeReport;
    private EndgameRuntimeSession Session => Field<EndgameRuntimeSession>(_director, "_session");
    private OpeningGuidancePanel Panel => Field<OpeningGuidancePanel>(_director, "_openingGuide");
    private OpeningGuidanceView Display => Field<OpeningGuidanceView>(Panel, "_view");
    private OpeningGuidanceMemory Memory => Field<OpeningGuidanceMemory>(_director, "_openingGuidanceMemory");
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs(); _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--opening-guidance-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 || Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Guidance smoke requires --opening-guidance-smoke --discipline=Vanguard --output=<fresh-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director); _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false);
            await Frames(); CloseMenus(); Call(_sandbox, "ResumePlaying"); Refresh(); await Frames(8);
            Check("fresh_guidance_enabled", Memory.Enabled);
            Check("fresh_context_hint", Panel.IsHintVisible && Display.Hint is not null);
            CheckHintLayout("wide"); await Capture("first-steps-hint.png");
            string hintId = Display.Hint!.Id, before = Session.StateHash;
            await Click("OpeningHintDismiss"); Check("dismiss_is_character_local_and_nonmutating", Memory.Dismissed.Contains(hintId) && Session.StateHash == before);
            var journey = Field<CampaignHud>(_director, "_campaignHud"); journey.SetOpen(true); await Frames();
            await Click("JourneyOpeningGuide"); Check("journey_footer_opens_guide", Panel.IsOpen && _sandbox.IsPaused);
            Check("manual_guide_has_basics_build_services", Display.Basics.Length == 5 && Display.Steps.Length == 3 && Display.Services.Any(c => c.Id == "service.training"));
            Check("dismissed_basics_revisitable", Display.Basics.Any(c => c.Id == hintId)); CheckLayout("initial_wide");
            Check("inspection_preserves_gameplay", Session.StateHash == before);
            await Click("OpeningGuideEnabled"); Check("hints_disabled", !Memory.Enabled);
            await Click("OpeningGuideClose"); Check("disabled_hint_hidden", !Panel.IsHintVisible);
            RoundTrip("disabled_guidance"); Check("dismiss_and_disable_survive_load", Memory.Dismissed.Contains(hintId) && !Memory.Enabled);
            Open(); await Frames(); Call(_sandbox, "SetKey", "interact", Key.U); Call(_director, "RefreshOpeningGuidance", true); await Frames();
            Check("guide_uses_remapped_binding", Descendants(Panel).OfType<Label>().Any(l => l.Text.Contains("Use U when nearby", StringComparison.Ordinal)));
            Call(_sandbox, "SetKey", "interact", Key.F); Refresh();
            await Click("OpeningGuideEnabled"); Check("hints_reenabled", Memory.Enabled); CloseMenus();
            for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && !Session.Campaign.Capture().Campaign.RescuedResidents.Contains("Torren Bale"); i++)
            { Step(new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(Session.Campaign)), false); if (i % 150 == 0) await Frames(1); }
            Step(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.ReturnToHub))); CloseMenus(); Call(_sandbox, "ResumePlaying"); Refresh();
            Check("torren_earned", Session.Campaign.Capture().Campaign.RescuedResidents.Contains("Torren Bale"));
            Open(); await Frames(); Check("earned_stash_service", Display.Services.Any(c => c.Id == "service.stash"));
            before = Session.StateHash; await Click("OpeningGuideAction_service_stash");
            var stash = Field<PersonalStashPanel>(_director, "_stashPanel"); Check("stash_inspection_nonmutating", stash.IsOpen && Session.StateHash == before);
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus(); await Frames();
            await Click("StashApproach");
            for (int i = 0; i < 500 && !stash.IsOpen; i++) { _sandbox._Process(1d / 30); if (i % 10 == 0) await Frames(1); }
            Check("service_actual_mouse_approach", stash.IsOpen && Field<PersonalStashDisplay>(stash, "_view").AtChest);
            Check("real_movement_observed", Memory.Completed.Contains("hint.move"));
            string memoryPath = OpeningGuidanceStore.PathFor(Field<string>(_director, "_openingGuidancePath"));
            string memoryBytes = File.ReadAllText(memoryPath); Call(_director, "CompleteOpeningGuidance", "hint.move"); Call(_director, "CompleteOpeningGuidance", "hint.move");
            Check("completed_hint_does_not_rewrite_memory", File.ReadAllText(memoryPath) == memoryBytes);
            CloseMenus();
            for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && !Session.Campaign.Capture().Campaign.CompletedActs.Contains(1); i++)
            { Step(new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(Session.Campaign)), false); if (i % 150 == 0) await Frames(1); }
            Step(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.ReturnToHub))); Call(_director, "ClearDeathRecap"); CloseMenus(); Refresh();
            Check("earned_loot_observed", Memory.Completed.Contains("hint.loot"));
            Check("act_one_completed_publicly", Session.Campaign.Capture().Campaign.CompletedActs.Contains(1));
            Open(); await Frames();
            Check("earned_heart_guidance", Display.Steps.Any(c => c.Id == "build.heart" && c.Action == "inspect_anatomy"));
            Check("earned_hunt_board_guidance", Display.Services.Any(c => c.Id == "service.hunts"));
            Check("no_optional_location_spoilers", !JsonData.Write(Display).Contains("champion.", StringComparison.Ordinal) && !JsonData.Write(Display).Contains("secret.", StringComparison.Ordinal));
            Find<ScrollContainer>("OpeningGuideScroll").ScrollVertical = 0; await Frames(); CheckLayout("earned_wide"); await Capture("first-steps-earned.png");
            GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(8); CheckLayout("earned_compact"); await Capture("first-steps-compact.png");
            await Click("OpeningGuideAction_build_heart"); Check("anatomy_inspection_opens", Field<CampaignHud>(_director, "_campaignHud").IsOpen); CloseMenus();
            Open(); await Frames(); before = Session.StateHash; await Click("OpeningGuideAction_service_hunts");
            Check("hunt_inspection_nonmutating", Field<RegionalHuntBoard>(_director, "_huntBoard").IsOpen && Session.StateHash == before); CloseMenus();
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus(); await Frames(); Call(_sandbox, "ResumePlaying");
            Open(); await Frames(); await Click("OpeningGuideAction_build_practice");
            Check("training_invitation_acknowledged", Memory.Dismissed.Contains("service.training"));
            for (int i = 0; i < 500 && Field<TrainingSession?>(_director, "_training") is null; i++) { _sandbox._Process(1d / 30); if (i % 10 == 0) await Frames(1); }
            var practice = Field<TrainingSession?>(_director, "_training"); Check("guide_enters_training_through_real_navigation", practice is not null);
            Check("entry_alone_does_not_complete_practice", !Memory.Completed.Contains("build.practice")); before = Session.StateHash;
            for (int i = 0; i < 95; i++)
            {
                var view = practice!.Combat.View; var target = view.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Health > 0); var hero = view.Actors.Single(a => a.Id == 1);
                var direction = CombatProductionSmoke.MovementDirection(hero.Position, target.Position, practice.Combat.Room); var skill = view.Skills.First(s => s.Available);
                CombatCommand[] commands = Position.DistanceSquared(hero.Position, target.Position) > 1000L * 1000 ? [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)] : [new(CombatCommandKind.Stop), new(CombatCommandKind.Cast, SkillId: skill.Id, TargetId: target.Id)];
                var events = (IReadOnlyList<CombatEvent>)Call(_director, "Advance", (object)commands)!; _commands++; _sandbox.PresentCombatEvents(events, practice.Combat);
            }
            Check("practice_measures_actual_damage", practice!.Report.TotalDamage > 0); Check("practice_preserves_campaign", Session.StateHash == before);
            await Click("TrainingLeave"); Check("earned_practice_lesson_complete", Memory.Completed.Contains("build.practice") && Field<TrainingSession?>(_director, "_training") is null);
            RoundTrip("earned_guidance"); Check("authoritative_replay_unchanged", Replay());
            string originalFile = Field<string>(_director, "_saveName"), originalId = Memory.CharacterId, originalPath = Field<string>(_director, "_openingGuidancePath"); var originalDismissed = Memory.Dismissed.ToArray();
            Open(); await Frames(); if (Memory.Enabled) await Click("OpeningGuideEnabled"); CloseMenus(); Call(_director, "CreateCharacter", "Arcanist"); await Frames();
            Check("new_character_has_fresh_guidance", Field<string>(_director, "_openingGuidancePath") != originalPath && Memory.Enabled && Memory.Dismissed.Length == 0);
            Call(_director, "PlayCharacter", originalFile); await Frames();
            Check("switching_back_preserves_character_guidance", Memory.CharacterId == originalId && !Memory.Enabled && originalDismissed.All(Memory.Dismissed.Contains));
            Open(); await Frames(); await Capture("first-steps-remembered.png");
            foreach (var player in _sandbox.AudioDirector.MusicPlayers) { player.Stop(); player.Stream = null; }
            await Finish(true, "");
        }
        catch (Exception ex) { try { await Capture("failure.png"); } catch { } GD.PushError(ex.ToString()); await Finish(false, ex.Message); }
    }
    private void Open() => Call(_director, "OpenOpeningGuide");
    private void Refresh() { _sandbox.AdoptSession(Session.Combat); Call(_director, "Refresh"); }
    private void CloseMenus()
    {
        Panel.SetOpen(false); Field<ProductionHud>(_director, "_character").Close(); Field<CampaignHud>(_director, "_campaignHud").SetOpen(false); Field<EndgameHud>(_director, "_board").SetOpen(false);
        Field<PersonalStashPanel>(_director, "_stashPanel").SetOpen(false); Field<RegionalHuntBoard>(_director, "_huntBoard").SetOpen(false);
    }
    private void Step(EndgameRuntimeCommand command, bool refresh = true)
    {
        Call(_director, "BeginOpeningGuidanceObservation"); var result = Session.Execute(command); _commands++; if (!result.Success) throw new InvalidDataException(result.Reason + " / " + command.Action);
        Call(_director, "Observe", result); if (refresh) Refresh();
    }
    private bool Replay() => EndgameRuntimeReplayRunner.Run(Field<string>(_director, "_combatJson"), Field<AdventureContent>(_director, "_adventure"), Field<ProgressionContent>(_director, "_progression"), Field<CampaignContent>(_director, "_campaign"), Field<EndgameContent>(_director, "_endgame"), Session.CaptureReplay()).Success;
    private void RoundTrip(string name) { CloseMenus(); string hash = Session.StateHash; Call(_director, "Save"); Call(_director, "Load"); Check(name, Session.StateHash == hash); }
    private async Task Click(string name)
    {
        await Frames(); var button = Find<BaseButton>(name);
        for (Node? parent = button.GetParent(); parent is not null; parent = parent.GetParent()) if (parent is ScrollContainer scroll) scroll.EnsureControlVisible(button);
        await Frames(); Check("clickable_" + name + "_" + _clicks, button.IsVisibleInTree() && !button.Disabled && GetViewport().GetVisibleRect().HasPoint(button.GetGlobalRect().GetCenter()));
        var point = button.GetGlobalRect().GetCenter(); _lastClick = name + " " + button.GetGlobalRect(); bool received = false; void Receipt() => received = true; button.Pressed += Receipt;
        GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { Position = point, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
        _clicks++; await Frames(); if (GodotObject.IsInstanceValid(button)) button.Pressed -= Receipt; Check("click_received_" + name + "_" + _clicks, received);
    }
    private void CheckLayout(string suffix)
    {
        var viewport = GetViewport().GetVisibleRect();
        foreach (string name in new[] { "OpeningGuidePanel", "OpeningGuideScroll", "OpeningGuideEnabled", "OpeningGuideClose" })
        { var control = Find<Control>(name); if (!viewport.Encloses(control.GetGlobalRect())) GD.Print(name + "=" + control.GetGlobalRect()); Check(name + "_fits_" + suffix, viewport.Encloses(control.GetGlobalRect())); }
    }
    private void CheckHintLayout(string suffix)
    {
        var rect = Find<Control>("OpeningHint").GetGlobalRect(); Check("hint_fits_" + suffix, GetViewport().GetVisibleRect().Encloses(rect));
        Check("hint_above_combat_dock_" + suffix, rect.End.Y <= GetViewport().GetVisibleRect().Size.Y - 196);
    }
    private async Task Frames(int count = 4) { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); _sandbox?._Process(0); } }
    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless" || !OS.GetCmdlineUserArgs().Contains("--capture-opening-guidance")) return;
        GetWindow().GrabFocus(); await Frames(); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage(); Check("capture_" + name, image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Guidance check failed: " + name); }
    private T Find<T>(string name) where T : Node => Descendants(_sandbox).OfType<T>().Single(n => n.Name == name);
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static object? Call(object instance, string name, params object?[] args)
    { try { return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
    private async Task Finish(bool passed, string error)
    {
        var report = new { kind = "OpeningGuidanceSmoke", passed, checks = _checks, captures = _captures, clicks = _clicks, commands = _commands, error, lastClick = _lastClick, scope = "Public earned Act I; real viewport dismiss/toggle/footer/service actions; remapped controls; saved character-local memory; nonmutating inspection and exact replay; 1280/780 layouts." };
        if (_writeReport) File.WriteAllText(Path.Combine(_output, "opening-guidance-review.json"), JsonData.Write(report)); GD.Print(JsonData.Write(report));
        if (_director is not null && GodotObject.IsInstanceValid(_director)) _director.QueueFree(); _director = null!; _sandbox = null!;
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); GC.Collect();
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); GetTree().Quit(passed ? 0 : 1);
    }
}
