using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;

namespace Ashenwake.Client;

public partial class ProductionHud
{
    public event Action<ProductionAction, string, string>? EquipmentPresetRequested;
    public event Action? TrainingRequested;
    private EquipmentPresetsPanel _equipmentPresets = null!;
    private IReadOnlyList<EquipmentPresetView> _savedEquipmentPresets = [];
    private Func<string, EquipmentPresetPreview>? _previewEquipmentPreset;

    public void ConfigureEquipmentPresets(IReadOnlyList<EquipmentPresetView> presets, Func<string, EquipmentPresetPreview> preview)
    {
        _savedEquipmentPresets = presets; _previewEquipmentPreset = preview;
        RefreshEquipmentPresets();
    }

    public void ReportEquipmentPresetResult(bool success, string reason) => _equipmentPresets.ReportResult(success, reason);

    public void ShowEquipmentPresets()
    {
        _gearLoadout.CancelDrag(); _gearInspecting = false;
        _tab = "Presets"; _panel.Visible = true; Rebuild(true);
    }

    private void RefreshEquipmentPresets()
    {
        if (_equipmentPresets is null || _state is null || _tab != "Presets" || !_panel.Visible) return;
        _equipmentPresets.SetView(_state, _content, _savedEquipmentPresets, _previewEquipmentPreset, CanChangeGear, _revision);
    }

    private void AddEquipmentPresetControls()
    {
        var presets = Button("Equipment presets · save and switch sets", ShowEquipmentPresets);
        presets.Name = "OpenEquipmentPresets";
        presets.TooltipText = "Inspect eight saved equipment sets. Save, rename, remove, or equip a complete set at Torren.";
        var training = Button("Practice this build · training grounds", () => TrainingRequested?.Invoke());
        training.Name = "OpenBuildTraining"; training.Disabled = !_inTown;
        training.TooltipText = _inTown ? "Walk to Greyhaven's training grounds to practice without risking your character." : "Return to Greyhaven to practice your build.";
    }
}
