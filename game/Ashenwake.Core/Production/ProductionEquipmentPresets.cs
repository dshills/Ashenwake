using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Production;

public sealed partial class ProductionSession
{
    public IReadOnlyList<EquipmentPresetView> EquipmentPresets => progression.EquipmentPresets;
    public ProductionResult SaveEquipmentPreset(string id, string name) => Execute(new(ProductionAction.SaveEquipmentPreset, Id: id, Value: name));
    public ProductionResult RenameEquipmentPreset(string id, string name) => Execute(new(ProductionAction.RenameEquipmentPreset, Id: id, Value: name));
    public ProductionResult DeleteEquipmentPreset(string id) => Execute(new(ProductionAction.DeleteEquipmentPreset, Id: id));
    public ProductionResult ApplyEquipmentPreset(string id) => Execute(new(ProductionAction.ApplyEquipmentPreset, Id: id));
    public EquipmentPresetPreview PreviewEquipmentPreset(string id)
    {
        var preview = progression.PreviewEquipmentPreset(id);
        string blocked = EquipmentPresetServiceBlockedReason();
        return blocked.Length == 0 ? preview : preview with { Success = false, Reason = blocked };
    }
    private string EquipmentPresetServiceBlockedReason()
    {
        if (View.RoomId != "room.greyhaven") return "Change equipment presets at Greyhaven's workshops.";
        if (!Near("service.torren")) return "Visit Torren to manage equipment presets.";
        if (!Combat.View.Actors.Any(actor => actor.Id == 1 && actor.Health > 0)) return "Cannot manage equipment presets while defeated.";
        return "";
    }
}
