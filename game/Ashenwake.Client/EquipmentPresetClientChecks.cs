using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Read-only checks called by the training smoke after the real preset panel finishes layout.</summary>
public static class EquipmentPresetClientChecks
{
    public static void Validate(ProductionHud hud, Action<string, bool> check)
    {
        var panel = hud.FindChild("EquipmentPresetsPanel", true, false) as EquipmentPresetsPanel;
        check("presets.panel.visible", panel?.IsVisibleInTree() == true);
        if (panel is null || !panel.IsVisibleInTree()) return;
        Rect2 viewport = panel.GetViewportRect();
        check("presets.panel.viewport", Inside(panel.GetGlobalRect(), viewport));
        foreach (string name in new[] { "EquipmentPresetSelector", "EquipmentPresetName", "EquipmentPresetPreviewScroll", "EquipmentPresetStatus", "EquipmentPresetSave", "EquipmentPresetRename", "EquipmentPresetApply", "EquipmentPresetDelete", "EquipmentPresetBack" })
        {
            var control = panel.FindChild(name, true, false) as Control;
            check("presets.control." + name, control?.IsVisibleInTree() == true && Inside(control.GetGlobalRect(), viewport) && Inside(control.GetGlobalRect(), panel.GetGlobalRect()));
        }
        var selector = panel.FindChild("EquipmentPresetSelector", true, false) as OptionButton;
        check("presets.eight.slots", selector?.ItemCount == ProgressionSession.MaximumEquipmentPresets);
        var nameEntry = panel.FindChild("EquipmentPresetName", true, false) as LineEdit;
        check("presets.name.limit", nameEntry?.MaxLength == ProgressionSession.MaximumEquipmentPresetNameLength);
        var preview = panel.FindChild("EquipmentPresetPreview", true, false) as VBoxContainer;
        check("presets.every.slot.previewed", preview is not null && preview.GetChildren().OfType<Label>().Count() >= Enum.GetValues<EquipmentSlot>().Length);
        var scroll = panel.FindChild("EquipmentPresetPreviewScroll", true, false) as ScrollContainer;
        check("presets.details.scroll", scroll?.HorizontalScrollMode == ScrollContainer.ScrollMode.Disabled && scroll.Size.Y >= 100);
        var actions = new[] { "EquipmentPresetSave", "EquipmentPresetRename", "EquipmentPresetApply", "EquipmentPresetDelete" }
            .Select(name => panel.FindChild(name, true, false)).OfType<Control>().ToArray();
        check("presets.actions.distinct", actions.Length == 4 && actions.SelectMany((a, index) => actions.Skip(index + 1).Select(b => !a.GetGlobalRect().Intersects(b.GetGlobalRect()))).All(value => value));
    }

    private static bool Inside(Rect2 inner, Rect2 outer) => inner.Size.X > 0 && inner.Size.Y > 0 &&
        inner.Position.X >= outer.Position.X - 1 && inner.Position.Y >= outer.Position.Y - 1 &&
        inner.End.X <= outer.End.X + 1 && inner.End.Y <= outer.End.Y + 1;
}
