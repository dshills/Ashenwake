using System.Reflection;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Production;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Training;
using Godot;
using Position = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Exercises disposable sparring through the shipping director, real attacks, and native report controls.</summary>
public partial class DefensiveTrainingSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [], _skipped = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private string _output = "", _case = "entry";
    private bool _writeReport;
    private int _commands, _replays;
    private EndgameRuntimeSession Journey => Field<EndgameRuntimeSession>(_director, "_session");
    private TrainingSession Practice => Field<TrainingSession>(_director, "_training");
    private TrainingHud Hud => Field<TrainingHud>(_director, "_trainingHud");
    private TrainingReport? Previous => Field<TrainingReport?>(_director, "_previousTrainingReport");

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--defensive-training-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Defensive training smoke requires --defensive-training-smoke --discipline=Vanguard --output=<fresh-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director);
            _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false);
            Call(_director, "Adopt", Call(_director, "Fresh", "Vanguard", null), false);
            WalkToTraining(); await ResumeForPlay();
            Check("world_entry", _sandbox.RequestWorldInteraction(TrainingSession.InteractionId));
            Call(_sandbox, "StepCombat"); await Frames();
            Check("disposable_copy", ReferenceEquals(_sandbox.Session, Practice.Combat) && !ReferenceEquals(Practice.Combat, Journey.Combat));
            string source = Journey.StateHash;
            Check("initial_comparison_empty", Previous is null);
            SelectMode(TrainingTargetMode.Melee); await ResumeForPlay();
            await Sparring("Melee", 190, false);
            var melee = Practice.Report;
            SelectMode(TrainingTargetMode.Ranged); await ResumeForPlay();
            Check("reset_preserves_previous", SameReport(Previous, melee) && Practice.Report.ElapsedTicks == 0);
            await Sparring("Ranged", 280, false);
            var ranged = Practice.Report;
            SelectMode(TrainingTargetMode.Mixed); await ResumeForPlay();
            Check("different_mode_preserved", SameReport(Previous, ranged) && Previous is not null && Previous.Mode != Practice.Mode);
            await Sparring("Mixed", 400, true);
            Check("source_unchanged_after_modes", Journey.StateHash == source);
            await Reports();
            await Defeat(source);
            await Lifecycle(source);
            foreach (var player in _sandbox.AudioDirector.MusicPlayers) { player.Stop(); player.Stream = null; }
            await Frames(); Finish(true, "");
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); Finish(false, ex.Message); }
    }

    private async Task Sparring(string label, int ticks, bool guard)
    {
        _case = label;
        Check("selected_mode", Practice.Mode.ToString() == label);
        bool projectile = false, barrier = false;
        for (int i = 0; i < ticks && !Practice.IsComplete; i++)
        {
            var view = Practice.Combat.View;
            var player = view.Actors.Single(a => a.Id == 1);
            var target = view.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0)
                .OrderBy(a => Position.DistanceSquared(player.Position, a.Position)).First();
            CombatCommand[] commands = [];
            // All skills and resource are those genuinely available on the fresh character.
            // Cleave generates Momentum; Iron Guard spends it through ordinary commands.
            if (guard && !barrier && player.Barrier == 0 && view.Resource >= 15 && view.Skills.Any(s => s.Id == "skill.iron_guard" && s.Available))
                commands = [new(CombatCommandKind.Cast, SkillId: "skill.iron_guard", TargetId: 1)];
            else if (guard && !barrier && view.Resource < 15 && Position.DistanceSquared(player.Position, target.Position) < 2400L * 2400)
                commands = [new(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: target.Id)];
            var events = Step(commands);
            projectile |= events.Any(e => e.Kind.Contains("Projectile", StringComparison.Ordinal));
            barrier |= events.Any(e => e.Kind == "BarrierGranted");
            if (i % 90 == 0) await Frames(1);
        }
        await Frames();
        Check("actual_incoming_health_loss", Practice.Combat.View.Actors.Single(a => a.Id == 1).Health < Practice.Combat.View.Actors.Single(a => a.Id == 1).MaxHealth);
        var defense = Practice.Report.Defense;
        Check("ordinary_attack_evidence", defense.Hits > 0 && defense.Damage.Count > 0);
        Check("defense_partition", defense.RawDamage == defense.MitigatedDamage + defense.ImmuneDamage + defense.BarrierAbsorbed + defense.HealthLost + defense.Overkill);
        Check("defense_rows_reconcile", defense.Damage.Sum(row => row.HealthLost) == defense.HealthLost && defense.Damage.Sum(row => row.BarrierAbsorbed) == defense.BarrierAbsorbed);
        if (guard) Check("ordinary_iron_guard_absorption", barrier && defense.BarrierAbsorbed > 0 && defense.Triggers.Any(t => t.SourceId == "skill.iron_guard"));
        if (label == "Ranged") Check("ranged_source", defense.Damage.Any(row => row.SourceId == "enemy.cinder_acolyte"));
        File.WriteAllText(Path.Combine(_output, label.ToLowerInvariant() + ".events.json"), JsonData.Write(new { projectile, barrier }));
        VerifyPractice(label.ToLowerInvariant());
        await Capture(label.ToLowerInvariant() + "-sparring.png");
    }

    private async Task Reports()
    {
        _case = "report";
        if (!Hud.ReportOpen)
        {
            Input.ActionPress("ui_accept"); Click("TrainingBreakdown"); await Frames();
            Check("held_accept_cannot_resume", !Hud.ReportInputArmed && Find<Button>(Hud, "TrainingResume").Disabled);
            Input.ActionRelease("ui_accept"); await Frames();
            Check("released_accept_arms_report", Hud.ReportInputArmed);
        }
        await Frames();
        Check("report_modal", Hud.ReportOpen && _sandbox.IsPaused);
        string combat = Practice.Combat.StateHash, source = Journey.StateHash;
        long elapsed = Practice.Report.ElapsedTicks;
        var nodes = Descendants(Hud).Select(n => n.GetInstanceId()).ToArray();
        for (int i = 0; i < 8; i++) Call(_director, "Refresh");
        Check("stable_report_nodes", nodes.SequenceEqual(Descendants(Hud).Select(n => n.GetInstanceId())));
        foreach (Key key in new[] { Key.W, Key.F, Key.C, Key.B, Key.J })
            foreach (bool pressed in new[] { true, false })
                GetViewport().PushInput(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed }, true);
        Step([new(CombatCommandKind.Move, X: 1000)]); _sandbox._Process(.2); await Frames();
        Check("report_input_isolated", Hud.ReportOpen && Practice.Combat.StateHash == combat && Practice.Report.ElapsedTicks == elapsed && Journey.StateHash == source);
        for (int i = 0; i < 8; i++)
        {
            foreach (bool pressed in new[] { true, false })
                GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.Tab, Keycode = Key.Tab, Pressed = pressed }, true);
            await Frames(1);
            var focus = GetViewport().GuiGetFocusOwner();
            Check("focus_contained." + i, focus is not null && Find<Control>(Hud, "TrainingReport").IsAncestorOf(focus));
        }
        Check("comparison_available", Previous is not null && Previous.Mode != Practice.Mode && Previous.ElapsedTicks != elapsed);
        string comparison = Find<Label>(Hud, "TrainingComparison").Text;
        Check("comparison_discloses_conditions", comparison.Contains("mode", StringComparison.OrdinalIgnoreCase) && comparison.Contains("duration", StringComparison.OrdinalIgnoreCase));
        ValidateReport(); await Capture("defense-report.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(7);
        ValidateReport(); await Capture("defense-report-minimum.png");
        var reportScroll = Find<ScrollContainer>(Hud, "TrainingReportScroll");
        var comparisonLabel = Find<Label>(Hud, "TrainingComparison");
        reportScroll.ScrollVertical = (int)comparisonLabel.Position.Y; await Frames();
        Check("comparison_visible_minimum", reportScroll.GetGlobalRect().Intersects(comparisonLabel.GetGlobalRect()));
        ValidateReport(); await Capture("defense-comparison-minimum.png");
        reportScroll.ScrollVertical = Math.Max(0, (int)(comparisonLabel.Position.Y + comparisonLabel.Size.Y - reportScroll.Size.Y)); await Frames();
        Check("comparison_caveat_visible_minimum", comparisonLabel.GetGlobalRect().End.Y <= reportScroll.GetGlobalRect().End.Y + 2 && comparisonLabel.GetGlobalRect().End.Y > reportScroll.GetGlobalRect().Position.Y);
        await Capture("defense-comparison-caveat-minimum.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames();
        if (!Practice.IsComplete) { Click("TrainingResume"); await ResumeForPlay(); }
    }

    private async Task Defeat(string source)
    {
        _case = "defeat";
        // Reset through the same report/live action used by players, then let normal sparring attacks land.
        Click(Hud.ReportOpen ? "TrainingReportReset" : "TrainingReset"); await ResumeForPlay();
        for (int i = 0; i < 2500 && !Practice.IsComplete; i++)
        { Step([]); if (i % 120 == 0) await Frames(1); }
        await Frames();
        Check("natural_defeat", Practice.IsComplete && Practice.Combat.View.Actors.Single(a => a.Id == 1).Health == 0);
        Check("defeat_opens_safe_report", Hud.ReportOpen && Find<Button>(Hud, "TrainingResume").Disabled);
        Check("no_journey_death_recap", Journey.LastDeathRecap is null && !Field<DeathRecapHud>(_director, "_deathRecapHud").IsOpen);
        Check("defeat_does_not_mutate_source", Journey.StateHash == source);
        VerifyPractice("defeated"); await Capture("defeated-practice.png");
        var final = Practice.Report;
        Click("TrainingReportReset"); await ResumeForPlay();
        var player = Practice.Combat.View.Actors.Single(a => a.Id == 1);
        Check("reset_full_health_zero_clock", player.Health == player.MaxHealth && Practice.Report.ElapsedTicks == 0 && !Practice.IsComplete);
        Check("reset_retains_defeat_report", SameReport(Previous, final));
        GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(7);
        foreach (string name in new[] { "TrainingSummary", "TrainingSingle", "TrainingGroup", "TrainingSparringMode", "TrainingReset", "TrainingBreakdown", "TrainingLeave" })
        {
            var control = Find<Control>(Hud, name);
            Check("summary_minimum." + name, control.IsVisibleInTree() && GetViewport().GetVisibleRect().Encloses(control.GetGlobalRect()));
        }
        await Capture("sparring-controls-minimum.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames();
        var previous = Previous;
        Click("TrainingReset"); await ResumeForPlay();
        Check("empty_reset_does_not_erase_previous", SameReport(Previous, previous));
    }

    private async Task Lifecycle(string source)
    {
        _case = "lifecycle";
        for (int i = 0; i < 90; i++) Step([]);
        var last = Practice.Report; VerifyPractice("last");
        Click("TrainingLeave"); await Frames();
        Check("leave_unchanged", Field<TrainingSession?>(_director, "_training") is null && Journey.StateHash == source && ReferenceEquals(_sandbox.Session, Journey.Combat));
        Check("leave_captures_attempt", SameReport(Previous, last));
        string oldBuild = Field<string>(_director, "_previousTrainingBuild");
        for (int i = 0; i < 2000 && !Journey.Campaign.Capture().Campaign.RescuedResidents.Contains("Torren Bale"); i++)
            JourneyStep(EndgameRuntimeSmoke.Next(Journey));
        Check("earned_torren_rescue", Journey.Campaign.Capture().Campaign.RescuedResidents.Contains("Torren Bale"));
        JourneyStep(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.ReturnToHub)));
        Walk(new Position(2000, -2000), 1700);
        var slot = Journey.Production.ProgressionView.Equipment.Keys.First();
        Call(_director, "Permanent", new ProductionCommand(ProductionAction.Unequip, Slot: slot)); await Frames();
        Check("ordinary_build_change", !Journey.Production.ProgressionView.Equipment.ContainsKey(slot));
        WalkToTraining(); string changedSource = Journey.StateHash;
        await ResumeForPlay(); Call(_director, "StartTraining"); await Frames();
        Check("same_character_reentry_keeps_comparison", SameReport(Previous, last) && Practice.Report.ElapsedTicks == 0);
        Check("comparison_labels_changed_build", oldBuild == Field<string>(_director, "_previousTrainingBuild") && oldBuild != Field<string>(_director, "_trainingBuild"));
        Call(_director, "Save"); Call(_director, "Load"); await Frames();
        Check("load_clears_ephemeral_comparison", Previous is null && Field<TrainingSession?>(_director, "_training") is null && Journey.StateHash == changedSource);
        VerifyJourney();
        await ResumeForPlay(); Call(_director, "StartTraining"); Step([]);
        Click("TrainingLeave"); await Frames(); Check("fresh_comparison_exists", Previous is not null);
        Call(_director, "Adopt", Call(_director, "Fresh", "Arcanist", null), false); await Frames();
        Check("character_switch_clears_comparison", Previous is null && Field<TrainingSession?>(_director, "_training") is null && Journey.Production.ProgressionView.Discipline == "Arcanist");
    }

    private IReadOnlyList<CombatEvent> Step(CombatCommand[] commands)
    {
        var events = (IReadOnlyList<CombatEvent>)Call(_director, "Advance", (object)commands)!; _commands++;
        _sandbox.PresentCombatEvents(events, Practice.Combat); return events;
    }
    private void JourneyStep(EndgameRuntimeCommand command)
    {
        var result = Journey.Execute(command); _commands++;
        if (!result.Success) throw new InvalidDataException(result.Reason);
        Call(_director, "Observe", result); Call(_director, "Refresh");
        _sandbox.PresentCombatEvents(result.CombatEvents, Journey.Combat);
    }
    private void WalkToTraining() => Walk(TrainingSession.EntryPosition, 2000);
    private void Walk(Position destination, int range)
    {
        Field<ProductionHud>(_director, "_character").Close(); Field<CampaignHud>(_director, "_campaignHud").SetOpen(false);
        Field<EndgameHud>(_director, "_board").SetOpen(false); Call(_director, "CloseExperimentPanel");
        for (int i = 0; i < 300; i++)
        {
            var player = Journey.Combat.View.Actors.Single(a => a.Id == 1);
            bool reached = Position.DistanceSquared(player.Position, destination) <= (long)range * range;
            var direction = CombatProductionSmoke.MovementDirection(player.Position, destination, Journey.Room);
            var result = Journey.Execute(new(EndgameRuntimeAction.Tick, Commands: reached ? [new(CombatCommandKind.Stop)] : [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)])); _commands++;
            if (!result.Success) throw new InvalidDataException(result.Reason);
            Call(_director, "Observe", result); Call(_director, "Refresh");
            if (reached) return;
        }
        throw new InvalidDataException("Could not approach Greyhaven's training ground.");
    }
    private void VerifyPractice(string label)
    {
        var replay = Practice.CaptureReplay();
        Check("replay." + label, TrainingSession.VerifyReplay(Field<string>(_director, "_combatJson"), replay)); _replays++;
        File.WriteAllText(Path.Combine(_output, label + ".training.json"), JsonData.Write(replay));
        File.WriteAllText(Path.Combine(_output, label + ".report.json"), JsonData.Write(Practice.Report));
    }
    private void VerifyJourney()
    {
        string combat = Field<string>(_director, "_combatJson");
        var replay = Journey.CaptureReplay();
        var result = EndgameRuntimeReplayRunner.Run(combat, Field<AdventureContent>(_director, "_adventure"), Field<ProgressionContent>(_director, "_progression"), Field<CampaignContent>(_director, "_campaign"), Field<EndgameContent>(_director, "_endgame"), replay);
        Check("journey_replay", result.Success && result.FinalHash == Journey.StateHash); _replays++;
        File.WriteAllText(Path.Combine(_output, "journey.replay.json"), JsonData.Write(replay));
    }
    private void ValidateReport()
    {
        string suffix = GetWindow().Size.X.ToString();
        var viewport = GetViewport().GetVisibleRect();
        foreach (string name in new[] { "TrainingReport", "TrainingReportScroll", "TrainingResume", "TrainingReportReset", "TrainingReportLeave" })
        {
            var control = Find<Control>(Hud, name);
            Check("viewport." + suffix + "." + name, control.IsVisibleInTree() && viewport.Encloses(control.GetGlobalRect()));
        }
        Check("scroll_usable." + suffix, Find<ScrollContainer>(Hud, "TrainingReportScroll").Size.Y >= 150);
    }
    private void ValidateLive(string filename)
    {
        var viewport = GetViewport().GetVisibleRect();
        var summary = Find<Control>(Hud, "TrainingSummary");
        var summaryRect = summary.GetGlobalRect();
        Check("live_summary_compact." + filename, summary.IsVisibleInTree() && summary.Size.Y <= 180 && viewport.Encloses(summaryRect));
        foreach (string name in new[] { "TrainingSingle", "TrainingGroup", "TrainingSparringMode", "TrainingReset", "TrainingBreakdown", "TrainingLeave" })
        {
            var control = Find<Control>(Hud, name);
            Check("live_control." + filename + "." + name, control.IsVisibleInTree() && viewport.Encloses(control.GetGlobalRect()) && summaryRect.Encloses(control.GetGlobalRect()));
        }
        var vitals = new Control[] { Field<Panel>(_sandbox, "_hudDock"), Field<Label>(_sandbox, "_healthText"), Field<ProgressBar>(_sandbox, "_health"), Field<Label>(_sandbox, "_momentumText"), Field<ProgressBar>(_sandbox, "_momentum") };
        Check("live_vitals_clear." + filename, vitals.All(control => control.IsVisibleInTree() && viewport.Encloses(control.GetGlobalRect()) && !summaryRect.Intersects(control.GetGlobalRect())));
    }
    private async Task Capture(string filename)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            Call(_sandbox, "ResumePlaying"); await Frames(5);
            if (Field<PanelContainer>(_sandbox, "_resumePanel").Visible) continue;
            if (!Hud.ReportOpen) ValidateLive(filename);
            if (DisplayServer.GetName() == "headless" || !OS.GetCmdlineUserArgs().Contains("--capture-defensive-training")) { _skipped.Add(filename); return; }
            RenderingServer.ForceDraw(false); RenderingServer.ForceSync(); using var image = GetViewport().GetTexture().GetImage();
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
            await Frames(4); Call(_sandbox, "ResumePlaying"); await Frames(2); if (!_sandbox.IsPaused) return;
        }
        throw new InvalidDataException("Sparring remained paused after bounded focus/resume attempts.");
    }
    private async Task Frames(int count = 4)
    { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); _sandbox._Process(0); } }
    private static bool SameReport(TrainingReport? left, TrainingReport? right) => JsonData.Hash(left) == JsonData.Hash(right);
    private void SelectMode(TrainingTargetMode mode)
    {
        var selector = Find<OptionButton>(Hud, "TrainingSparringMode");
        Check("mode_control_visible", selector.IsVisibleInTree() && !Hud.ReportOpen);
        int index = selector.GetItemIndex((int)mode); selector.Select(index);
        selector.EmitSignal(OptionButton.SignalName.ItemSelected, index);
    }
    private void Click(string name) { var button = Find<Button>(_sandbox, name); Check("click." + name, button.IsVisibleInTree() && !button.Disabled); button.EmitSignal(Button.SignalName.Pressed); }
    private void Check(string key, bool passed) { _checks[_case + "." + key] = passed; if (!passed) throw new InvalidDataException("Defensive training smoke failed: " + _case + "." + key); }
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static object? Call(object instance, string name, params object?[] args)
    { try { return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static T Find<T>(Node root, string name) where T : Node => Descendants(root).OfType<T>().Single(node => node.Name == name);
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = "DefensiveTrainingClientSmoke",
            passed,
            checks = _checks,
            captures = _captures,
            skipped = _skipped,
            commands = _commands,
            replays = _replays,
            error,
            scope = "Shipping director, disposable fresh Vanguard, ordinary melee/ranged/mixed attacks, earned starting skills, natural training defeat, source hash isolation, replay verification, attempt comparison lifecycle and report keyboard/layout checks. Native captures use button signals; this is not a human combat-feel assessment or an exhaustive legendary-power playthrough."
        };
        if (_writeReport) File.WriteAllText(Path.Combine(_output, "defensive-training-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
