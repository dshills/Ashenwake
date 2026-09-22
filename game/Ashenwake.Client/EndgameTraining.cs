using Ashenwake.Core.Combat;
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
        var training = _session.CreateTrainingSession();
        _campaignHud.SetOpen(false); _board.SetOpen(false); _character.Close(); CloseExperimentPanel();
        // Preserve each overlay's visibility, including the experiment navigation button.
        // Sandbox combat controls remain live, while the permanent journey is untouched.
        _trainingHidden.Clear();
        foreach (var child in _trainingHud.GetParent().GetChildren().OfType<CanvasItem>())
        {
            if (child == _trainingHud || !child.Visible || child is not (ProductionHud or CampaignHud or EndgameHud or EchoesBoard or EchoesMemoryHud) && child.Name != "EchoesNavigation") continue;
            _trainingHidden.Add(child); child.Hide();
        }
        _training = training; _trainingReset = 0; _trainingCompletionShown = false;
        _stage.Hide(); _effects.Hide(); _memoryPresentation.Hide();
        _trainingScenery = TrainingGroundVisual.Create(_training.Combat.Room); AddChild(_trainingScenery);
        _sandbox.SetTrainingPresentation(true); _sandbox.SetSession(_training.Combat);
        _sandbox.SetWorldInteractions([], _ => { }); _sandbox.SetMechanismVisuals(_ => null);
        _trainingHud.Show(); RefreshTraining();
        _sandbox.Notify("Practice your current build. Reset refills health and potions, clears cooldowns and starts with zero resource. Leave training to return unchanged.");
    }
    private IReadOnlyList<CombatEvent> AdvanceTraining(CombatCommand[] commands)
    {
        if (_training is null) return [];
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
        _trainingHud.SetReportOpen(false); _training.Reset(mode); _trainingReset++; _trainingCompletionShown = false;
        _sandbox.SetSession(_training.Combat); RefreshTraining();
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
    }
    private void RefreshTraining()
    {
        if (_training is null) return;
        var combat = _training.Combat.View;
        _trainingHud.Present(_training.Report);
        _sandbox.PresentAuthoredRoom(_training.Combat.Room, "training:" + _trainingReset, "greyhaven");
        _sandbox.PresentLocalMap(null, "Training ground", combat);
        _sandbox.SetWorldSubtitle("GREYHAVEN / TORREN’S PROVING GROUND");
    }
    private void EndTraining(bool refresh = true)
    {
        if (_training is null) return;
        _training.Close(); _training = null;
        _trainingHud.Dismiss();
        if (_trainingScenery is not null) { RemoveChild(_trainingScenery); _trainingScenery.QueueFree(); _trainingScenery = null; }
        foreach (var control in _trainingHidden) if (GodotObject.IsInstanceValid(control)) control.Show();
        _trainingHidden.Clear(); _stage.Visible = true; _effects.Visible = true; _memoryPresentation.Visible = true;
        _sandbox.SetTrainingPresentation(false); _sandbox.SetSession(_session.Combat);
        if (refresh) { Refresh(); _sandbox.Notify("Returned to Greyhaven. Your character, equipment, health and progression are unchanged by practice."); }
    }
    private bool HandleTrainingInput(InputEvent input)
    {
        if (_training is null) return false;
        if (_trainingHud.ReportOpen && input is InputEventKey or InputEventJoypadButton)
        {
            if (input.IsActionPressed("ui_cancel") || input.IsActionPressed("aw_inventory")) _trainingHud.SetReportOpen(false);
            else if (new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev" }.Any(action => input.IsAction(action))) return false;
            GetViewport().SetInputAsHandled(); return true;
        }
        if (new[] { "aw_character", "aw_inventory", "aw_journey", "aw_endgame", "aw_experiment", "aw_interact" }.Any(action => input.IsActionPressed(action)))
        { _trainingHud.SetReportOpen(true); GetViewport().SetInputAsHandled(); return true; }
        return false;
    }
}
