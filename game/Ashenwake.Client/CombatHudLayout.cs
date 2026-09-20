using Godot;

namespace Ashenwake.Client;

/// <summary>Shared screen space for the solo HUD's navigation and current objective.</summary>
internal static class CombatHudLayout
{
    public static void Navigation(Control control, int row)
    {
        control.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        control.OffsetLeft = -278; control.OffsetRight = -22;
        control.OffsetTop = 18 + row * 38; control.OffsetBottom = 50 + row * 38;
        if (control is Button button) { button.AddThemeFontSizeOverride("font_size", 12); button.ClipText = true; }
    }

    public static void Objective(Control control, Vector2 viewport)
    {
        control.Position = new(22, 88);
        control.Size = new(Math.Min(565, viewport.X - 322), 104);
    }
}
