using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private bool _trainingPresentation;
    private bool _trainingPassiveTargets;
    private Control? _trainingSummary;
    public void SetTrainingPresentation(bool active, bool passiveTargets = true)
    { _trainingPresentation = active; _trainingPassiveTargets = active && passiveTargets; }
    private float TrainingSummaryBottom()
    {
        if (!_trainingPresentation) return 0;
        _trainingSummary ??= _hud.FindChild("TrainingSummary", true, false) as Control;
        return _trainingSummary?.IsVisibleInTree() == true ? _trainingSummary.GetGlobalRect().End.Y : 0;
    }
}
