using Ashenwake.Core.Combat;
using Ashenwake.Core.Diagnostics;
using Godot;

namespace Ashenwake.Client;

/// <summary>Local input recovery and bounded, explicitly requested diagnostics. Enabled by the release integration hook.</summary>
public partial class Sandbox
{
    private readonly DiagnosticBuffer _diagnostics = new();
    private bool _releaseEnabled, _neutralMovementRequired;
    private bool _manualPause, _interruptionPause;
    private readonly HashSet<string> _modalPauses = new(StringComparer.Ordinal);
    private ColorRect? _resumeBackdrop;
    private PanelContainer? _resumePanel;
    private Label? _resumeReason, _releaseSettingsStatus;

    private void InitializeReleaseSupport()
    {
        _releaseEnabled = true;
        CombatAdvanced += RecordDiagnostics;
        Input.JoyConnectionChanged += ControllerConnectionChanged;
        _resumeBackdrop = new ColorRect { Color = new Color(0, 0, 0, .4f), MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _resumeBackdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); _hud.AddChild(_resumeBackdrop);
        _resumePanel = Panel(new(413, 260), new(454, 200));
        var column = new VBoxContainer(); _resumePanel.AddChild(column);
        column.AddChild(TextLabel("PAUSED", 23));
        _resumeReason = TextLabel("", 14); column.AddChild(_resumeReason);
        AddButton(column, "Resume playing", ResumePlaying);
        AddButton(column, "Settings", () => TogglePanel(_settingsPanel));
        if (_settingsRecoveryNotice.Length > 0) ReleaseStatus(_settingsRecoveryNotice);
    }

    public override void _Notification(int what)
    {
        if (!_releaseEnabled || AutomaticStep || _smoke) return;
        if (what == NotificationApplicationFocusOut)
            PauseForInterruption("The game lost focus. Return and choose Resume when ready.");
    }

    public override void _ExitTree()
    {
        ReleaseWorldPointer();
        if (!_releaseEnabled) return;
        CombatAdvanced -= RecordDiagnostics;
        Input.JoyConnectionChanged -= ControllerConnectionChanged;
    }

    private void ControllerConnectionChanged(long device, bool connected)
    {
        if (!_releaseEnabled || AutomaticStep || _smoke) return;
        if (!connected) PauseForInterruption("Controller disconnected. Reconnect it or use the keyboard, then choose Resume.");
        else if (_resumePanel is { Visible: true } && _resumeReason is not null)
            _resumeReason.Text = "Controller connected. Release the stick, then choose Resume when ready.";
    }

    private void PauseForInterruption(string reason)
    {
        CancelSettingsBinding();
        _interruptionPause = true;
        if (_resumeReason is not null) _resumeReason.Text = reason;
        ChangePause(false);
    }

    /// <summary>Each modal releases only its own pause; manual and interruption pauses require explicit resume.</summary>
    public void SetModalPaused(string source, bool paused)
    {
        if (paused) _modalPauses.Add(source); else _modalPauses.Remove(source);
        ChangePause(false);
    }

    private bool HasModalPause => _modalPauses.Count > 0 || _settingsPanel is { Visible: true } ||
        _inventoryPanel is { Visible: true } || _lootPanel is { Visible: true };

    private void ToggleManualPause()
    {
        if (HasModalPause) return;
        if (_manualPause || _interruptionPause) { ResumePlaying(); return; }
        _manualPause = true;
        if (_resumeReason is not null) _resumeReason.Text = $"The world is paused. Press {_keys["pause"]} or choose Resume playing when ready.";
        ChangePause(false);
    }

    private void ResumePlaying()
    {
        if (HasModalPause) return;
        _manualPause = _interruptionPause = false;
        ChangePause(false);
    }

    private void ResetPause()
    {
        _manualPause = _interruptionPause = false; _modalPauses.Clear();
        ChangePause(false);
    }

    private void ChangePause(bool paused)
    {
        CancelMouseMovement(false);
        _clock.Paused = paused || _manualPause || _interruptionPause || HasModalPause;
        _pending.Clear(); _pending.Add(new(CombatCommandKind.Stop));
        _moveX = _moveZ = int.MinValue;
        _awaitingKey = null;
        _neutralMovementRequired = true;
        foreach (string action in _keys.Keys) Input.ActionRelease("aw_" + action);
        if (_resumePanel is null || _resumeBackdrop is null) return;
        bool showResume = (_manualPause || _interruptionPause) && !HasModalPause;
        bool wasVisible = _resumePanel.Visible;
        _resumePanel.Visible = _resumeBackdrop.Visible = showResume;
        if (showResume)
        {
            _hud.MoveChild(_resumeBackdrop, _hud.GetChildCount() - 1);
            _hud.MoveChild(_resumePanel, _hud.GetChildCount() - 1);
            FocusFirstAction(_resumePanel);
        }
        else if (wasVisible) GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
    }

    private void FocusResumeOrRelease()
    {
        if (_resumePanel is { Visible: true }) FocusFirstAction(_resumePanel);
        else GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
    }

    private Vector2 ReadReleaseMovement()
    {
        var direction = Input.GetVector("aw_left", "aw_right", "aw_up", "aw_down", .28f);
        if (!_neutralMovementRequired) return direction;
        if (direction.IsZeroApprox()) _neutralMovementRequired = false;
        return Vector2.Zero;
    }

    private static bool FocusFirstAction(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            if (child is BaseButton { Disabled: false } button && button.IsVisibleInTree()) { button.GrabFocus(); return true; }
            if (FocusFirstAction(child)) return true;
        }
        return false;
    }

    private void RecordDiagnostics(IReadOnlyList<CombatEvent> events)
    { foreach (var item in events) _diagnostics.Record(item); }

    private void BuildReleaseSettings(VBoxContainer column)
    {
        column.AddChild(new HSeparator());
        column.AddChild(TextLabel("LOCAL DIAGNOSTICS", 14));
        column.AddChild(TextLabel("Export recent gameplay event IDs and build details to this device. No replay, personal paths or raw error messages are included. Nothing is uploaded.", 12));
        AddButton(column, "Export local diagnostics", ExportLocalDiagnostics);
        _releaseSettingsStatus = TextLabel("", 12); _releaseSettingsStatus.Name = "DiagnosticStatus"; column.AddChild(_releaseSettingsStatus);
    }

    private void ExportLocalDiagnostics()
    {
        try
        {
            // The MVID identifies the exact loaded Core assembly, independently of a source-control checkout.
            string buildId = typeof(CombatSession).Assembly.ManifestModule.ModuleVersionId.ToString("N");
            var bundle = _diagnostics.Capture(buildId, Session.ContentHash);
            string name = "diagnostic-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json";
            DiagnosticBuffer.WriteLocal(Path.Combine(_output, "diagnostics", name), bundle);
            ReleaseStatus("Local diagnostics saved. Nothing was uploaded.");
            if (_releaseSettingsStatus is not null) _releaseSettingsStatus.Text += "\n" + Path.Combine(_output, "diagnostics", name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { ReleaseStatus("The local diagnostic file could not be written. Choose a writable output folder and try again."); }
    }
    private void ReleaseStatus(string text)
    { Message(text); SettingsNotice(text); if (_releaseSettingsStatus is not null) _releaseSettingsStatus.Text = text; }
}
