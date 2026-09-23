using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Training;
using Godot;

namespace Ashenwake.Client;

public partial class EndgameDirector
{
    private TrainingSession? _training;
    private TrainingHud _trainingHud = null!;
    private Node3D? _trainingScenery;
    private readonly List<CanvasItem> _trainingHidden = [];
    private int _trainingReset;
    private bool _trainingCompletionShown;
    private TrainingReport? _previousTrainingReport;
    private string _previousTrainingBuild = "", _trainingBuild = "";
    private TrainingTargetMode _lastTrainingMode = TrainingTargetMode.Single;

    private void InitializeTraining()
    {
        _trainingHud = new TrainingHud(); _sandbox.AddOverlay(_trainingHud);
        _trainingHud.ResetRequested += mode => Safely(() => ResetTraining(mode));
        _trainingHud.LeaveRequested += () => Safely(() => EndTraining());
        _trainingHud.ReportVisibilityChanged += open => _sandbox.SetModalPaused("training-report", open);
    }
    private void StartTraining()
    {
        if (_training is not null || !_hasActiveCharacter || _frontMenu.IsOpen || _sandbox.IsPaused) return;
        var training = _session.CreateTrainingSession(_lastTrainingMode);
        _stashPanel?.SetOpen(false);
        _campaignHud.SetOpen(false); _board.SetOpen(false); _character.Close(); CloseExperimentPanel();
        // Preserve each overlay's visibility, including the experiment navigation button.
        // Sandbox combat controls remain live, while the permanent journey is untouched.
        _trainingHidden.Clear();
        foreach (var child in _trainingHud.GetParent().GetChildren().OfType<CanvasItem>())
        {
            if (child == _trainingHud || !child.Visible || child is not (ProductionHud or CampaignHud or EndgameHud or EchoesBoard or EchoesMemoryHud or RegionalHuntBoard or SecretChamberPanel or PersonalStashPanel) && child.Name != "EchoesNavigation" && child.Name != "RegionalHuntNavigation" && child.Name != "RegionalHuntObjective" && child.Name != "SecretChamberObjective") continue;
            _trainingHidden.Add(child); child.Hide();
        }
        _training = training; _trainingReset = 0; _trainingCompletionShown = false;
        string loadoutName = MatchingBuildLoadoutName();
        _trainingBuild = (loadoutName.Length == 0 ? "Custom build" : "Loadout: " + loadoutName) + "\n" + DescribeTrainingBuild(training.Combat);
        _stage.Hide(); _effects.Hide(); _memoryPresentation.Hide(); _huntPresentation?.Hide(); _secretPresentation?.Hide();
        _trainingScenery = TrainingGroundVisual.Create(_training.Combat.Room); AddChild(_trainingScenery);
        _sandbox.SetTrainingPresentation(true, _training.Mode is TrainingTargetMode.Single or TrainingTargetMode.Group); _sandbox.SetSession(_training.Combat);
        _sandbox.SetWorldInteractions([], _ => { }); _sandbox.SetMechanismVisuals(_ => null);
        _trainingHud.Show(); RefreshTraining();
        _sandbox.Notify("Practice your current build. Reset refills health and potions, clears cooldowns and starts with zero resource. Leave training to return unchanged.");
    }
    private IReadOnlyList<CombatEvent> AdvanceTraining(CombatCommand[] commands)
    {
        if (_training is null || _trainingHud.ReportOpen) return [];
        var events = _training.Step(commands);
        RefreshTraining();
        if (_training.IsComplete && !_trainingCompletionShown)
        {
            _trainingCompletionShown = true;
            Callable.From(() => { if (_training?.IsComplete == true) _trainingHud.SetReportOpen(true); }).CallDeferred();
        }
        return events;
    }
    private void ResetTraining(TrainingTargetMode mode)
    {
        if (_training is null) return;
        RememberTrainingAttempt();
        _trainingHud.SetReportOpen(false); _training.Reset(mode); _trainingReset++; _trainingCompletionShown = false;
        _lastTrainingMode = mode;
        _sandbox.SetTrainingPresentation(true, mode is TrainingTargetMode.Single or TrainingTargetMode.Group);
        _sandbox.SetSession(_training.Combat); RefreshTraining();
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
    }
    private void RefreshTraining()
    {
        if (_training is null) return;
        var combat = _training.Combat.View;
        _trainingHud.SetComparison(_previousTrainingReport, _previousTrainingBuild, _trainingBuild);
        _trainingHud.Present(_training.Report);
        _sandbox.PresentAuthoredRoom(_training.Combat.Room, "training:" + _trainingReset, "greyhaven");
        _sandbox.PresentLocalMap(null, "Training ground", combat);
        _sandbox.SetWorldSubtitle("GREYHAVEN / TORREN’S PROVING GROUND");
    }
    private void EndTraining(bool refresh = true)
    {
        if (_training is null) return;
        RememberTrainingAttempt();
        _training.Close(); _training = null;
        _trainingHud.Dismiss();
        if (_trainingScenery is not null) { RemoveChild(_trainingScenery); _trainingScenery.QueueFree(); _trainingScenery = null; }
        foreach (var control in _trainingHidden) if (GodotObject.IsInstanceValid(control)) control.Show();
        _trainingHidden.Clear(); _stage.Visible = true; _effects.Visible = true; _memoryPresentation.Visible = true; _huntPresentation?.Show(); _secretPresentation?.Show();
        _sandbox.SetTrainingPresentation(false); _sandbox.SetSession(_session.Combat);
        if (refresh) { Refresh(); _sandbox.Notify("Returned to Greyhaven. Your character, equipment, health and progression are unchanged by practice."); }
    }
    private void RememberTrainingAttempt()
    {
        if (_training is null || _training.ElapsedTicks == 0) return;
        _previousTrainingReport = JsonData.Copy(_training.Report);
        _previousTrainingBuild = _trainingBuild;
    }
    private void ClearTrainingComparison()
    {
        _previousTrainingReport = null; _previousTrainingBuild = _trainingBuild = "";
        _lastTrainingMode = TrainingTargetMode.Single;
    }
    private static string DescribeTrainingBuild(CombatSession combat)
    {
        var view = combat.View; var build = combat.ProgressionBuild;
        var gear = view.Equipment.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => view.Inventory.FirstOrDefault(item => item.Id == pair.Value))
            .Where(item => item is not null).Select(item => EquipmentNames.For(item!.DefinitionId)).ToArray();
        var fragments = view.Fragments.Where(fragment => fragment.Equipped).Select(fragment => fragment.Name).ToArray();
        var mutations = view.Skills.Where(skill => !string.IsNullOrEmpty(skill.Mutation))
            .Select(skill => view.Mutations.FirstOrDefault(mutation => mutation.Id == skill.Mutation)?.Name ?? skill.Mutation).ToArray();
        return $"{view.Discipline} · level {build.Level}\nGear: {(gear.Length == 0 ? "None" : string.Join(", ", gear))}\n" +
            $"Fragments: {(fragments.Length == 0 ? "None" : string.Join(", ", fragments))}\n" +
            $"Mutations: {(mutations.Length == 0 ? "None" : string.Join(", ", mutations))}\n" +
            $"Passive ranks: Offense {build.Offense} · Defense {build.Defense}\n" +
            $"Build bonuses: damage {build.FlatDamage} · armor {build.Armor} · resource {build.ResourceBonus}";
    }
    private string MatchingBuildLoadoutName()
    {
        var snapshot = _session.Capture().Campaign.Production;
        var character = snapshot.Progression.Character; var anatomy = snapshot.Expedition.Adventure;
        static bool Same<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> first, IReadOnlyDictionary<TKey, TValue> second) where TKey : notnull
            => first.Count == second.Count && first.All(pair => second.TryGetValue(pair.Key, out var value) && EqualityComparer<TValue>.Default.Equals(pair.Value, value));
        return _session.Production.BuildLoadouts.FirstOrDefault(build => build.Discipline == character.Discipline &&
            Same(build.Equipment, character.Equipment) && Same(build.Passives, character.Passives) &&
            Same(build.Mutations, character.SelectedMutations) && Same(build.Fragments, anatomy.Anatomy) &&
            Same(build.Manifestations, anatomy.Manifestations))?.Name ?? "";
    }
    private bool HandleTrainingInput(InputEvent input)
    {
        if (_training is null) return false;
        if (_trainingHud.ReportOpen && input is InputEventKey or InputEventJoypadButton)
        {
            if (!_trainingHud.ReportInputArmed) { GetViewport().SetInputAsHandled(); return true; }
            if (input.IsActionPressed("ui_cancel") || input.IsActionPressed("aw_inventory"))
            { if (_trainingHud.ReportInputArmed && !input.IsEcho()) _trainingHud.SetReportOpen(false); }
            else if (new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev" }.Any(action => input.IsAction(action))) return false;
            GetViewport().SetInputAsHandled(); return true;
        }
        if (new[] { "aw_character", "aw_inventory", "aw_journey", "aw_endgame", "aw_experiment", "aw_interact" }.Any(action => input.IsActionPressed(action)))
        { _trainingHud.SetReportOpen(true); GetViewport().SetInputAsHandled(); return true; }
        return false;
    }
}
