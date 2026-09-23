using System.Reflection;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Training;
using Godot;
using Position = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Real earned character and viewport inputs. Native dialog decisions use explicit public signals.</summary>
public partial class BuildLoadoutsSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private string _output = "";
    private bool _writeReport;
    private int _commands, _clicks;
    private EndgameRuntimeSession Session => Field<EndgameRuntimeSession>(_director, "_session");
    private ProductionHud Hud => Field<ProductionHud>(_director, "_character");
    private BuildLoadoutsPanel Panel => Find<BuildLoadoutsPanel>("BuildLoadoutsPanel");
    private ProgressionState Character => Session.Capture().Campaign.Production.Progression.Character;
    private static Position Mara => new(-4500, -1800);
    private static Position Torren => new(2000, -2000);

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--build-loadouts-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Build loadout smoke requires --build-loadouts-smoke --discipline=Vanguard --output=<fresh-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director); _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false);
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus(); await Frames(); Call(_sandbox, "ResumePlaying");
            await EarnAndSave(); await SwitchAndCompare(); await GuardsAndPersistence();
            foreach (var player in _sandbox.AudioDirector.MusicPlayers) { player.Stop(); player.Stream = null; }
            await Frames(); Finish(true, "");
        }
        catch (Exception ex) { try { await Capture("failure.png"); } catch { } GD.PushError(ex.ToString()); Finish(false, ex.Message); }
    }

    private async Task EarnAndSave()
    {
        int requiredMaterials = Field<ProgressionContent>(_director, "_progression").Capture().RespecCost * 3;
        for (int i = 0; i < 4000 && (!Session.Campaign.Capture().Campaign.RescuedResidents.Contains("Torren Bale") || Session.Production.ProgressionView.Level < 3 || Character.Materials < requiredMaterials); i++) Step(EndgameRuntimeSmoke.Next(Session));
        Check("torren_earned", Session.Campaign.Capture().Campaign.RescuedResidents.Contains("Torren Bale"));
        Check("earned_points_and_materials", Session.Production.ProgressionView.AvailablePassivePoints >= 2 && Character.Materials >= requiredMaterials);
        Step(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.ReturnToHub)));
        Walk(Mara, 2400);
        Change(new(ProductionAction.Passive, Id: "Offense"));
        Change(new(ProductionAction.Expedition, new(ExpeditionAction.InstallFragment, "Eyes", "fragment.eye_vael")));
        Hud.ShowBuildLoadouts(); await Frames(8);
        Check("panel_open_paused", Panel.IsVisibleInTree() && _sandbox.IsPaused);
        Check("eight_slots", Find<OptionButton>("BuildLoadoutSelector").ItemCount == 8);
        var name = Find<LineEdit>("BuildLoadoutName"); name.GrabFocus(); name.Text = "Ash and Sight"; name.EmitSignal(LineEdit.SignalName.TextChanged, name.Text);
        string before = Session.StateHash;
        foreach (Key key in new[] { Key.C, Key.I, Key.F, Key.J, Key.B, Key.H, Key.P })
            foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventKey { PhysicalKeycode = key, Keycode = key, Unicode = pressed ? (uint)char.ToLowerInvariant((char)key) : 0, Pressed = pressed }, true);
        await Frames();
        Check("typing_shortcuts_do_not_escape", Panel.IsVisibleInTree() && _sandbox.IsPaused && Session.StateHash == before && !Field<EndgameHud>(_director, "_board").IsOpen);
        name.Text = "Ash and Sight"; name.EmitSignal(LineEdit.SignalName.TextChanged, name.Text);
        await Click("BuildLoadoutSave");
        Check("saved_full_build", Session.Production.BuildLoadouts.Single().Name == "Ash and Sight" && Session.Production.BuildLoadouts.Single().Passives.GetValueOrDefault("Offense") > 0 && Session.Production.BuildLoadouts.Single().Fragments.Values.Contains("fragment.eye_vael"));
        CheckLayout("wide"); await Capture("build-loadout-saved.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(8); CheckLayout("minimum"); await Capture("build-loadout-minimum.png");
        before = Session.StateHash; int replayFrames = Session.CaptureReplay().Frames.Length;
        for (int i = 0; i < 6; i++) Session.PreviewBuildLoadout("loadout.1");
        Check("preview_read_only", Session.StateHash == before && Session.CaptureReplay().Frames.Length == replayFrames);
        await Practice("Ash and Sight");
    }

    private async Task SwitchAndCompare()
    {
        Walk(Torren, 2000);
        var slot = Character.Equipment.Keys.First();
        Change(new(ProductionAction.Unequip, Slot: slot));
        Walk(Mara, 2400);
        Change(new(ProductionAction.Respec)); Change(new(ProductionAction.Passive, Id: "Defense"));
        Change(new(ProductionAction.Expedition, new(ExpeditionAction.InstallFragment, "Eyes", "")));
        Hud.ShowBuildLoadouts(); await Frames(); Panel.SelectLoadout("loadout.2"); await Frames();
        var name = Find<LineEdit>("BuildLoadoutName"); name.Text = "Iron Vigil"; name.EmitSignal(LineEdit.SignalName.TextChanged, name.Text);
        await Click("BuildLoadoutSave");
        Check("second_build_saved", Session.Production.BuildLoadouts.Count == 2);
        Panel.SelectLoadout("loadout.1"); await Frames();
        var preview = Session.PreviewBuildLoadout("loadout.1");
        Check("preview_ready_with_exact_respec_cost", preview.Success && preview.MaterialCost > 0 && preview.MaterialCost == preview.RespecCost && preview.FragmentRemovalCost == 0);
        string before = Session.StateHash; await Click("BuildLoadoutApply");
        var dialog = Find<ConfirmationDialog>("BuildLoadoutConfirmation");
        Check("apply_requires_confirmation", dialog.Visible && Session.StateHash == before);
        dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled); await Frames();
        Check("cancel_preserves_full_build", Session.StateHash == before);
        await Capture("build-loadout-comparison.png");
        int materials = Character.Materials; await Click("BuildLoadoutApply");
        dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Frames();
        var saved = Session.Production.BuildLoadouts.First(p => p.Id == "loadout.1");
        Check("whole_build_applied", Character.Equipment.SequenceEqual(saved.Equipment) && Character.Passives.SequenceEqual(saved.Passives) &&
            Session.Capture().Campaign.Production.Expedition.Adventure.Anatomy.SequenceEqual(saved.Fragments));
        Check("materials_charged_once", materials - Character.Materials == preview.MaterialCost);
        string applied = Session.StateHash; dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); Check("duplicate_confirmation_no_second_spend", applied == Session.StateHash);
        Panel.SelectLoadout("loadout.2"); await Frames(); await Click("BuildLoadoutApply"); dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Frames();
        await Practice("Iron Vigil", "Ash and Sight");
    }

    private async Task GuardsAndPersistence()
    {
        Walk(Mara, 2400); Hud.ShowBuildLoadouts(); Panel.SelectLoadout("loadout.1"); await Frames();
        await Click("BuildLoadoutApply"); var dialog = Find<ConfirmationDialog>("BuildLoadoutConfirmation");
        Change(new(ProductionAction.Passive, Id: "Resource")); await Frames();
        Check("state_change_invalidates_confirmation", !dialog.Visible);
        string before = Session.StateHash; dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
        Check("stale_confirmation_no_mutation", Session.StateHash == before);
        Call(_director, "Save"); var replay = Session.CaptureReplay();
        var verified = EndgameRuntimeReplayRunner.Run(Field<string>(_director, "_combatJson"), Field<AdventureContent>(_director, "_adventure"), Field<ProgressionContent>(_director, "_progression"), Field<CampaignContent>(_director, "_campaign"), Field<EndgameContent>(_director, "_endgame"), replay);
        Check("earned_route_replay_exact", verified.Success && verified.FinalHash == Session.StateHash);
        File.WriteAllText(Path.Combine(_output, "loadouts.replay.json"), JsonData.Write(replay));
        Call(_director, "Load"); await Frames();
        Check("save_restores_loadouts_and_build", Session.StateHash == before && Session.Production.BuildLoadouts.Count == 2);
        var target = Session.Production.BuildLoadouts.First(p => p.Id == "loadout.1").Equipment.Values.First(id => !Character.Equipment.Values.Contains(id));
        Walk(Torren, 2000); Change(new(ProductionAction.Discard, ItemId: target, ConfirmPermanent: true));
        Walk(Mara, 2400); Hud.ShowBuildLoadouts(); Panel.SelectLoadout("loadout.1"); await Frames();
        Check("missing_reference_blocks_apply", !Session.PreviewBuildLoadout("loadout.1").Success && Find<Button>("BuildLoadoutApply").Disabled);
        await Capture("build-loadout-missing.png");
        await Click("BuildLoadoutDelete"); Check("delete_requires_confirmation", dialog.Visible);
        dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled); await Frames(); Check("cancel_delete_keeps_loadout", Session.Production.BuildLoadouts.Count == 2);
        _sandbox.SetModalPaused("loadout-smoke-other", true); Hud.Close(); await Frames();
        Check("close_preserves_other_pause_owner", _sandbox.IsPaused); _sandbox.SetModalPaused("loadout-smoke-other", false); Call(_sandbox, "ResumePlaying");
        Check("close_releases_own_pause", !_sandbox.IsPaused);
        Step(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.EnterAct, Act: 1)));
        Hud.ShowBuildLoadouts(); await Frames();
        Check("outside_greyhaven_management_blocked", Find<Button>("BuildLoadoutSave").Disabled && !Session.PreviewBuildLoadout("loadout.2").Success);
        await Capture("build-loadout-away-from-hub.png");
    }

    private async Task Practice(string name, string previous = "")
    {
        // Resume focus-loss pause with no modal open, then exercise the same panel button as a player.
        // ResumePlaying intentionally cannot clear a pause while a modal is still holding it.
        Hud.Close(); if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus(); await Frames();
        Call(_sandbox, "ResumePlaying"); Hud.ShowBuildLoadouts(); await Frames(); await Click("BuildLoadoutPractice");
        Check("practice_button_requests_approach_" + name, !Panel.IsVisibleInTree() && _sandbox.PendingWorldActionId == TrainingSession.InteractionId);
        for (int i = 0; i < 400 && Field<TrainingSession?>(_director, "_training") is null; i++)
        { Call(_sandbox, "StepCombat"); _commands++; if (i % 20 == 0) await Frames(1); }
        await Frames();
        Check("practice_button_enters_training_" + name, Field<TrainingSession?>(_director, "_training") is not null);
        var practice = Field<TrainingSession>(_director, "_training"); string before = Session.StateHash;
        Check("training_name_" + name, Field<string>(_director, "_trainingBuild").StartsWith("Loadout: " + name, StringComparison.Ordinal));
        for (int i = 0; i < 95; i++)
        {
            var view = practice.Combat.View; var player = view.Actors.Single(a => a.Id == 1); var target = view.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
            var direction = CombatProductionSmoke.MovementDirection(player.Position, target.Position, practice.Combat.Room);
            CombatCommand[] commands = Position.DistanceSquared(player.Position, target.Position) > 1000L * 1000
                ? [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)] : [new(CombatCommandKind.Stop), new(CombatCommandKind.Cast, SkillId: view.Skills.First(s => s.Available).Id, TargetId: target.Id)];
            Call(_director, "Advance", (object)commands); _commands++;
        }
        Check("training_damage_" + name, practice.Report.TotalDamage > 0 && Session.StateHash == before);
        var hud = Field<TrainingHud>(_director, "_trainingHud"); hud.SetReportOpen(true); await Frames();
        if (previous.Length > 0)
        {
            Check("training_compares_named_loadouts", Field<string>(_director, "_previousTrainingBuild").StartsWith("Loadout: " + previous, StringComparison.Ordinal) && Field<TrainingReport?>(_director, "_previousTrainingReport") is not null);
            Find<ScrollContainer>("TrainingReportScroll").EnsureControlVisible(Find<Label>("TrainingComparison")); await Frames();
        }
        await Capture("training-" + name.Replace(' ', '-') + ".png");
        Call(_director, "EndTraining", true); await Frames(); Check("training_preserves_character_" + name, Session.StateHash == before);
    }

    private void Change(ProductionCommand command) => Step(new(EndgameRuntimeAction.Production, Production: command));
    private void Step(EndgameRuntimeCommand command)
    {
        var result = Session.Execute(command); _commands++; if (!result.Success) throw new InvalidDataException(result.Reason);
        Call(_director, "Observe", result); Call(_director, "Refresh");
    }
    private void Walk(Position destination, int range)
    {
        Hud.Close(); Field<CampaignHud>(_director, "_campaignHud").SetOpen(false); Field<EndgameHud>(_director, "_board").SetOpen(false); Call(_director, "CloseExperimentPanel");
        for (int i = 0; i < 400; i++)
        {
            var player = Session.Combat.View.Actors.Single(a => a.Id == 1);
            if (Position.DistanceSquared(player.Position, destination) <= (long)(range - 300) * (range - 300)) { Step(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)])); return; }
            var direction = CombatProductionSmoke.MovementDirection(player.Position, destination, Session.Room);
            Step(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]));
        }
        throw new InvalidDataException("Could not approach requested service.");
    }
    private void CheckLayout(string suffix)
    {
        var viewport = GetViewport().GetVisibleRect(); Check("panel_fits_" + suffix, viewport.Encloses(Panel.GetGlobalRect()));
        foreach (string name in new[] { "BuildLoadoutSelector", "BuildLoadoutName", "BuildLoadoutPreviewScroll", "BuildLoadoutSave", "BuildLoadoutApply", "BuildLoadoutDelete", "BuildLoadoutBack" })
            Check(name + "_fits_" + suffix, viewport.Encloses(Find<Control>(name).GetGlobalRect()));
    }
    private async Task Click(string name)
    {
        var button = Find<Button>(name); Check("clickable_" + name + "_" + _clicks, button.IsVisibleInTree() && !button.Disabled);
        var point = button.GetGlobalRect().GetCenter(); GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { Position = point, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
        _clicks++; await Frames();
    }
    private async Task Frames(int count = 4) { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); _sandbox?._Process(0); } }
    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless" || !OS.GetCmdlineUserArgs().Contains("--capture-build-loadouts")) return;
        GetWindow().GrabFocus(); await Frames(); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage(); Check("capture_" + name, image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private void Check(string name, bool value) { _checks[name] = value; if (!value) throw new InvalidDataException("Build loadout check failed: " + name); }
    private T Find<T>(string name) where T : Node => Descendants(_sandbox).OfType<T>().Single(n => n.Name == name);
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static object? Call(object instance, string name, params object?[] args)
    { try { return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = "BuildLoadoutsSmoke",
            passed,
            checks = _checks,
            captures = _captures,
            clicks = _clicks,
            commands = _commands,
            error,
            scope = "Actual earned Vanguard, full-build save/apply through native viewport buttons; native confirmation dialog decisions use explicit public signals. Tests costs, preview isolation, canceled/stale confirmation, missing gear, archive/replay, typing, small-window layout and named training comparisons. Core tests separately cover all mutations and manifestation combinations."
        };
        if (_writeReport) File.WriteAllText(Path.Combine(_output, "build-loadouts-review.json"), JsonData.Write(report)); GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
