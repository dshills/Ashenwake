using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private bool _frontSettings;
    private ColorRect? _frontSettingsBackdrop;
    private readonly List<BaseButton> _frontDisabledActions = [];
    public bool FrontSettingsVisible => _frontSettings && _settingsPanel.Visible;
    public event Action? FrontSettingsClosed;

    public void ConfigureFrontMenu(Action mainMenu, Action quit)
    {
        if (_resumePanel?.GetChild(0) is not VBoxContainer column) return;
        var menu = AddButton(column, "Save & main menu", mainMenu); menu.Name = "FrontReturnToMenu";
        var exit = AddButton(column, "Save & quit", quit); exit.Name = "FrontSaveQuit";
    }

    public void OpenFrontSettings()
    {
        if (FrontSettingsVisible) return;
        _frontSettings = true;
        // The menu's preview character is not a playable save. Settings may adjust
        // controls and accessibility, but cannot save or load that preview.
        foreach (var button in _settingsPanel.FindChildren("*", "Button", true, false).OfType<Button>())
            if (!button.Disabled && (button.Text.StartsWith("Save character", StringComparison.Ordinal) ||
                button.Text.StartsWith("Load character", StringComparison.Ordinal) || button.Text.StartsWith("Verify & save replay", StringComparison.Ordinal) ||
                button.Text.StartsWith("Inspect ground loot", StringComparison.Ordinal)))
            { button.Disabled = true; _frontDisabledActions.Add(button); }
        if (_frontSettingsBackdrop is null)
        {
            _frontSettingsBackdrop = new ColorRect { Color = new("050b14ba"), MouseFilter = Control.MouseFilterEnum.Stop };
            _hud.AddChild(_frontSettingsBackdrop); _frontSettingsBackdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        }
        _frontSettingsBackdrop.Visible = true;
        _hud.MoveChild(_frontSettingsBackdrop, _hud.GetChildCount() - 1);
        _hud.MoveChild(_settingsPanel, _hud.GetChildCount() - 1);
        if (!_settingsPanel.Visible) TogglePanel(_settingsPanel);
    }

    private void RestoreFrontSettingsActions()
    {
        if (!_frontSettings || _settingsPanel.Visible) return;
        _frontSettings = false;
        if (_frontSettingsBackdrop is not null) _frontSettingsBackdrop.Visible = false;
        foreach (var button in _frontDisabledActions) button.Disabled = false;
        _frontDisabledActions.Clear(); FrontSettingsClosed?.Invoke();
    }

    public void ResumeFromFrontMenu() => ResumePlaying();

    public void FrontMenuFailure(string message)
    {
        if (_resumePanel is { Visible: true } && _resumeReason is not null)
        {
            _resumeReason.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _resumeReason.Text = "Could not save. Your current character remains open.\n" + message;
        }
    }
}
