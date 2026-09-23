using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private string _displayMode = "Windowed";
    public string DisplayMode => _displayMode;
    private static string NormalizeDisplayMode(string? mode) => mode == "Fullscreen" ? "Fullscreen" : "Windowed";

    private void InitializeDisplay()
    {
        // Diagnostics own their window dimensions, so baseline captures and compact UI
        // checks remain repeatable. Explicit settings selections still exercise this path.
        if (OS.GetCmdlineUserArgs().Any(a => a.EndsWith("-smoke", StringComparison.Ordinal))) return;
        ApplyDisplayMode();
    }

    public void SetDisplayMode(string value)
    {
        _displayMode = NormalizeDisplayMode(value);
        _settingsDisplayMode?.Select(_displayMode == "Fullscreen" ? 1 : 0);
        ApplyDisplayMode();
    }

    private void ApplyDisplayMode()
    {
        if (DisplayServer.GetName() == "headless") return;
        var window = GetWindow();
        if (_displayMode == "Fullscreen") { window.Mode = Window.ModeEnum.Fullscreen; return; }
        window.Mode = Window.ModeEnum.Windowed;
        Rect2I usable = DisplayServer.ScreenGetUsableRect(window.CurrentScreen);
        if (usable.Size.X <= 0 || usable.Size.Y <= 0) return;
        // Leave space for the OS window frame and menu bar, even on small displays.
        window.Size = FitWindowSize(usable.Size);
        window.Position = usable.Position + (usable.Size - window.Size) / 2;
    }

    internal static Vector2I FitWindowSize(Vector2I usable)
    {
        float fit = Math.Min(1f, Math.Min(Math.Max(1, usable.X - 80) / 1600f, Math.Max(1, usable.Y - 100) / 1000f));
        return new(Math.Max(1, (int)(1600 * fit)), Math.Max(1, (int)(1000 * fit)));
    }
}
