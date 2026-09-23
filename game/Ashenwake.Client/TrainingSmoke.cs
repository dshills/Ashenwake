using System.Reflection;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Training;
using Godot;
using Position = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Exercises the shipping director and UI using disposable fresh characters and public commands.</summary>
public partial class TrainingSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [], _skipped = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private string _output = "", _case = "";
    private int _commands, _replays;
    private bool _writeReport;
    private EndgameRuntimeSession Journey => Field<EndgameRuntimeSession>(_director, "_session");
    private TrainingSession Practice => Field<TrainingSession>(_director, "_training");
    private TrainingHud Hud => Field<TrainingHud>(_director, "_trainingHud");
    private ProductionHud Gear => Field<ProductionHud>(_director, "_character");
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--training-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Training smoke requires --training-smoke --discipline=Vanguard --output=<fresh-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director);
            _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false);
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            foreach (string discipline in new[] { "Vanguard", "Veilwalker", "Arcanist", "Gravecaller", "Warden" }) await Discipline(discipline);
            await Presets();
            // Let the native mixer release diagnostic streams before immediate quit.
            foreach (var player in _sandbox.AudioDirector.MusicPlayers) { player.Stop(); player.Stream = null; }
            await Frames(); Finish(true, "");
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); Finish(false, ex.Message); }
    }
    private async Task Discipline(string discipline)
    {
        _case = discipline;
        var fresh = (EndgameRuntimeSession)Call(_director, "Fresh", discipline, null)!;
        Call(_director, "Adopt", fresh, false); await Frames();
        Walk(TrainingSession.EntryPosition, TrainingSession.InteractionRange);
        Call(_director, "Refresh");
        await ResumeForPlay();
        Check("entry_is_clickable", _sandbox.RequestWorldInteraction(TrainingSession.InteractionId));
        Call(_sandbox, "StepCombat"); await Frames();
        Check("director_entered_training", Field<TrainingSession?>(_director, "_training") is not null);
        string before = Journey.StateHash;
        Check("sandbox_uses_copy", ReferenceEquals(_sandbox.Session, Practice.Combat) && !ReferenceEquals(Practice.Combat, Journey.Combat));
        Check("combat_controls_visible", Find<Button>(_sandbox, "TrainingBreakdown").IsVisibleInTree() && Field<Button>(_sandbox, "_settingsHudButton").IsVisibleInTree() && Field<List<Button>>(_sandbox, "_skillButtons").All(b => b.IsVisibleInTree()));
        Check("campaign_navigation_hidden", !Gear.Visible && !Field<CampaignHud>(_director, "_campaignHud").Visible);
        for (int i = 0; i < 95; i++)
        {
            var view = Practice.Combat.View; var target = view.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
            var hero = view.Actors.Single(a => a.Id == 1);
            var direction = CombatProductionSmoke.MovementDirection(hero.Position, target.Position, Practice.Combat.Room);
            var skill = view.Skills.First(s => s.Available);
            PracticeStep(Position.DistanceSquared(hero.Position, target.Position) > 1000L * 1000 ?
                [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)] :
                [new(CombatCommandKind.Stop), new(CombatCommandKind.Cast, SkillId: skill.Id, TargetId: target.Id)]);
        }
        Check("actual_damage_measured", Practice.Report.TotalDamage > 0 && Practice.Report.Damage.Sum(r => r.Damage) == Practice.Report.TotalDamage);
        Check("source_unchanged_during_practice", Journey.StateHash == before);
        var replay = Practice.CaptureReplay();
        Check("training_replay", TrainingSession.VerifyReplay(Field<string>(_director, "_combatJson"), replay)); _replays++;
        File.WriteAllText(Path.Combine(_output, discipline + ".training.json"), JsonData.Write(replay));
        await Capture(discipline + "-single.png");
        Click("TrainingBreakdown"); await Frames();
        long frozen = Practice.Report.ElapsedTicks; string combatHash = Practice.Combat.StateHash;
        var reportNodes = Descendants(Hud).Select(n => n.GetInstanceId()).ToArray();
        for (int refresh = 0; refresh < 12; refresh++) Call(_director, "Refresh");
        Check("report_retains_controls", reportNodes.SequenceEqual(Descendants(Hud).Select(n => n.GetInstanceId())));
        _sandbox._Process(.1); await Frames();
        Check("report_pauses_clock", Hud.ReportOpen && _sandbox.IsPaused && Practice.Report.ElapsedTicks == frozen && Practice.Combat.StateHash == combatHash && Journey.StateHash == before);
        await Capture(discipline + "-report.png");
        Click("TrainingResume"); await ResumeForPlay();
        Click("TrainingGroup"); await Frames();
        Check("group_reset", Practice.Mode == TrainingTargetMode.Group && Practice.Report.TotalDamage == 0 && Practice.Report.ElapsedTicks == 0 && Practice.Combat.View.Actors.Count(a => a.Faction == CombatFaction.Enemy && a.Health > 0) == 5);
        for (int i = 0; i < 30; i++) PracticeStep([new(CombatCommandKind.Cast, SkillId: Practice.Combat.View.Skills.First(s => s.Available).Id, TargetId: 2)]);
        if (discipline == "Vanguard")
        {
            GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); _sandbox.SetGraphicsQuality("Performance");
            Find<CheckButton>(_sandbox, b => b.Text == "Reduced visual effects").ButtonPressed = true;
            await Frames(7); await Capture("training-group-minimum.png");
            Click("TrainingBreakdown"); await Frames(); await Capture("training-report-minimum.png"); Click("TrainingResume"); await ResumeForPlay();
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); _sandbox.SetGraphicsQuality("High");
            Find<CheckButton>(_sandbox, b => b.Text == "Reduced visual effects").ButtonPressed = false;
            // Presentation-only terminal-state fixture; actual completion is exercised in Core tests.
            Hud.Present(Practice.Report with { Complete = true }); Hud.SetReportOpen(true); await Frames();
            Check("completed_report_requires_reset_or_leave", Find<Button>(Hud, "TrainingResume").Disabled);
            Hud.SetReportOpen(false); Call(_director, "Refresh");
        }
        Click("TrainingLeave"); await Frames();
        Check("leave_restores_original", Field<TrainingSession?>(_director, "_training") is null && ReferenceEquals(_sandbox.Session, Journey.Combat) && Journey.StateHash == before);
        Check("leave_restores_navigation", Gear.Visible && Journey.Interactions.Any(i => i.ActionId == TrainingSession.InteractionId) && _sandbox.PendingWorldActionId is null);
        VerifyJourney(discipline);
    }
    private async Task Presets()
    {
        _case = "presets";
        var fresh = (EndgameRuntimeSession)Call(_director, "Fresh", "Vanguard", null)!; Call(_director, "Adopt", fresh, false);
        for (int i = 0; i < 2000 && !Journey.Campaign.Capture().Campaign.RescuedResidents.Contains("Torren Bale"); i++) JourneyStep(EndgameRuntimeSmoke.Next(Journey));
        Check("earned_torren_rescue", Journey.Campaign.Capture().Campaign.RescuedResidents.Contains("Torren Bale"));
        JourneyStep(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.ReturnToHub)));
        Walk(new(2000, -2000), 2000); Call(_director, "Refresh");
        Gear.ShowEquipmentPresets(); await Frames(7); EquipmentPresetClientChecks.Validate(Gear, Check);
        var name = Find<LineEdit>(Gear, "EquipmentPresetName");
        name.GrabFocus(); name.Text = "Ash and Iron"; name.EmitSignal(LineEdit.SignalName.TextChanged, name.Text);
        string beforeTyping = Journey.StateHash;
        foreach (Key key in new[] { Key.C, Key.I, Key.F, Key.J, Key.B, Key.P })
            foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventKey { PhysicalKeycode = key, Keycode = key, Unicode = pressed ? (uint)char.ToLowerInvariant((char)key) : 0, Pressed = pressed }, true);
        await Frames();
        Check("name_shortcuts_do_not_escape", Journey.StateHash == beforeTyping && Gear.FindChild("EquipmentPresetsPanel", true, false) is Control { Visible: true } && _sandbox.IsPaused && !Field<EndgameHud>(_director, "_board").IsOpen);
        name.Text = "Ash and Iron"; name.EmitSignal(LineEdit.SignalName.TextChanged, name.Text);
        Click("EquipmentPresetSave"); await Frames();
        Check("saved_preset", Journey.Production.EquipmentPresets.Count == 1 && Journey.Production.EquipmentPresets[0].Name == "Ash and Iron");
        var equipment = Journey.Production.ProgressionView.Equipment.ToDictionary(p => p.Key, p => p.Value);
        var slot = equipment.Keys.First();
        Call(_director, "Permanent", new ProductionCommand(ProductionAction.Unequip, Slot: slot)); await Frames();
        Click("EquipmentPresetApply"); await Frames();
        Check("whole_set_restored", JsonData.Hash(equipment.OrderBy(p => p.Key)) == JsonData.Hash(Journey.Production.ProgressionView.Equipment.OrderBy(p => p.Key)));
        await Capture("equipment-presets.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(8);
        EquipmentPresetClientChecks.Validate(Gear, (key, value) => Check("minimum." + key, value)); await Capture("equipment-presets-minimum.png");
        Click("EquipmentPresetSave"); await Frames();
        var confirmation = Find<ConfirmationDialog>(Gear, "EquipmentPresetConfirmation");
        Check("overwrite_requires_review", confirmation.Visible);
        Call(_director, "Permanent", new ProductionCommand(ProductionAction.Unequip, Slot: slot)); await Frames();
        Check("stale_confirmation_invalidated", !confirmation.Visible);
        string unchanged = Journey.StateHash; confirmation.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
        Check("stale_confirmation_no_mutation", unchanged == Journey.StateHash);
        Click("EquipmentPresetApply"); await Frames();
        Call(_director, "Permanent", new ProductionCommand(ProductionAction.Unequip, Slot: slot));
        Call(_director, "Permanent", new ProductionCommand(ProductionAction.Discard, ItemId: equipment[slot], ConfirmPermanent: true)); await Frames();
        Check("missing_item_explained", !Journey.Production.PreviewEquipmentPreset("preset.1").Success && Find<Button>(Gear, "EquipmentPresetApply").Disabled);
        await Capture("equipment-presets-missing.png");
        Gear.Close(); await Frames(); Call(_sandbox, "ResumePlaying");
        Check("preset_close_releases_pause", !_sandbox.IsPaused);
        VerifyJourney("presets");
        Walk(TrainingSession.EntryPosition, TrainingSession.InteractionRange); await ResumeForPlay(); Call(_director, "StartTraining"); await Frames();
        string sourceHash = Journey.StateHash;
        Call(_director, "Save"); Check("save_during_training_preserves_source", sourceHash == Journey.StateHash);
        Call(_director, "Load"); await Frames();
        Check("load_exits_training", Field<TrainingSession?>(_director, "_training") is null && Journey.StateHash == sourceHash && ReferenceEquals(_sandbox.Session, Journey.Combat));
        await ResumeForPlay(); Call(_director, "StartTraining"); await Frames();
        Call(_director, "ShowFrontMenu"); await Frames();
        Check("menu_exits_training", Field<TrainingSession?>(_director, "_training") is null && Field<FrontMenu>(_director, "_frontMenu").IsOpen && Journey.StateHash == sourceHash);
        Call(_director, "ResumeFromFrontMenu");
    }
    private void PracticeStep(CombatCommand[] commands)
    {
        var events = (IReadOnlyList<CombatEvent>)Call(_director, "Advance", (object)commands)!; _commands++;
        _sandbox.PresentCombatEvents(events, Practice.Combat);
    }
    private void JourneyStep(EndgameRuntimeCommand command)
    {
        var result = Journey.Execute(command); _commands++;
        if (!result.Success) throw new InvalidDataException(result.Reason);
        Call(_director, "Observe", result); Call(_director, "Refresh"); _sandbox.PresentCombatEvents(result.CombatEvents, Journey.Combat);
    }
    private void Walk(Position destination, int range)
    {
        Gear.Close(); Field<CampaignHud>(_director, "_campaignHud").SetOpen(false); Field<EndgameHud>(_director, "_board").SetOpen(false); Call(_director, "CloseExperimentPanel");
        for (int i = 0; i < 300; i++)
        {
            var player = Journey.Combat.View.Actors.Single(a => a.Id == 1);
            if (Position.DistanceSquared(player.Position, destination) <= (long)(range - 300) * (range - 300))
            { JourneyStep(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)])); return; }
            var direction = CombatProductionSmoke.MovementDirection(player.Position, destination, Journey.Room);
            JourneyStep(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]));
        }
        throw new InvalidDataException("Could not approach the requested Greyhaven service.");
    }
    private void VerifyJourney(string label)
    {
        string combat = Field<string>(_director, "_combatJson");
        var adventure = Field<Ashenwake.Core.Adventure.AdventureContent>(_director, "_adventure");
        var progression = Field<ProgressionContent>(_director, "_progression");
        var campaign = Field<CampaignContent>(_director, "_campaign"); var endgame = Field<EndgameContent>(_director, "_endgame");
        var replay = Journey.CaptureReplay(); var result = EndgameRuntimeReplayRunner.Run(combat, adventure, progression, campaign, endgame, replay);
        Check("journey_replay", result.Success && result.FinalHash == Journey.StateHash); _replays++;
        File.WriteAllText(Path.Combine(_output, label + ".journey.json"), JsonData.Write(replay));
        string path = Path.Combine(_output, label, "endgame.save.json");
        EndgameRuntimeSaveStore.Write(path, combat, adventure, progression, campaign, endgame, Journey.Capture());
        Check("save_load_exact", EndgameRuntimeSaveStore.Load(path, combat, adventure, progression, campaign, endgame).Session.StateHash == Journey.StateHash);
    }
    private async Task Capture(string filename)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            Call(_sandbox, "ResumePlaying"); await Frames(5);
            if (Field<PanelContainer>(_sandbox, "_resumePanel").Visible) continue;
            if (Field<TrainingSession?>(_director, "_training") is { } training && !Hud.ReportOpen)
            {
                var viewport = GetViewport().GetVisibleRect();
                Check("hud_bounds." + filename, viewport.Encloses(Field<Panel>(_sandbox, "_hudDock").GetGlobalRect()) &&
                    Field<List<Button>>(_sandbox, "_skillButtons").All(b => b.IsVisibleInTree() && viewport.Encloses(b.GetGlobalRect())));
                Check("resource_matches." + filename, Field<Label>(_sandbox, "_momentumText").Text == $"{training.Combat.View.ResourceName.ToUpperInvariant()}  {training.Report.CurrentResource}/{training.Combat.View.MaxResource}");
            }
            if (DisplayServer.GetName() == "headless" || !OS.GetCmdlineUserArgs().Contains("--capture-training")) { _skipped.Add(filename); return; }
            RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
            using var image = GetViewport().GetTexture().GetImage();
            Check("capture." + filename, image.GetWidth() == GetWindow().Size.X && image.GetHeight() == GetWindow().Size.Y && image.SavePng(Path.Combine(_output, filename)) == Error.Ok);
            _captures.Add(filename); return;
        }
        throw new InvalidDataException("Native capture remained obscured by interruption pause.");
    }
    private async Task ResumeForPlay()
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            await Frames(4); Call(_sandbox, "ResumePlaying"); await Frames(2);
            if (!_sandbox.IsPaused) return;
        }
        throw new InvalidDataException("Native practice remained paused after bounded focus/resume attempts.");
    }
    private void Click(string name) { var button = Find<Button>(_sandbox, name); Check("click." + name, button.IsVisibleInTree() && !button.Disabled); button.EmitSignal(Button.SignalName.Pressed); }
    private async Task Frames(int count = 4)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // Keep normal HUD layout and current values fresh without advancing
            // the diagnostic's exclusively command-driven simulation clock.
            _sandbox._Process(0);
        }
    }
    private void Check(string key, bool okay) { _checks[_case + "." + key] = okay; if (!okay) throw new InvalidDataException("Training smoke failed: " + _case + "." + key); }
    private static T Field<T>(object instance, string name) => (T)(instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance))!;
    private static object? Call(object instance, string name, params object?[] args)
    {
        try { return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
    }
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static T Find<T>(Node root, string name) where T : Node => Descendants(root).OfType<T>().Single(n => n.Name == name);
    private static T Find<T>(Node root, Func<T, bool> predicate) where T : Node => Descendants(root).OfType<T>().Single(predicate);
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = "TrainingClientSmoke",
            passed,
            checks = _checks,
            captures = _captures,
            skipped = _skipped,
            commands = _commands,
            replays = _replays,
            error,
            scope = "Shipping director and HUD, five fresh disciplines, real practice commands and unchanged source saves/replays. Vanguard earns Torren rescue before native preset operations. No injected items or progression. Native button signals and text input inspect minimum/default windows; passive targets do not measure defensive powers or human combat feel."
        };
        if (_writeReport) File.WriteAllText(Path.Combine(_output, "training-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
