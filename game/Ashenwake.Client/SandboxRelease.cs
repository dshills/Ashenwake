using Ashenwake.Core.Combat;
using Ashenwake.Core.Diagnostics;
using Godot;

namespace Ashenwake.Client;

/// <summary>Local input recovery and bounded, explicitly requested diagnostics. Enabled by the release integration hook.</summary>
public partial class Sandbox
{
    private readonly DiagnosticBuffer _diagnostics = new();
    private bool _releaseEnabled, _neutralMovementRequired;
    private PanelContainer? _resumePanel;
    private Label? _resumeReason, _releaseSettingsStatus;

    private void InitializeReleaseSupport()
    {
        _releaseEnabled = true;
        CombatAdvanced += RecordDiagnostics;
        Input.JoyConnectionChanged += ControllerConnectionChanged;
        _resumePanel = Panel(new(413, 280), new(454, 157));
        var column = new VBoxContainer(); _resumePanel.AddChild(column);
        column.AddChild(TextLabel("PAUSED", 23));
        _resumeReason = TextLabel("", 14); column.AddChild(_resumeReason);
        AddButton(column, "Resume playing", () => ChangePause(false));
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
        ChangePause(true);
        if (_resumePanel is null || _resumeReason is null) return;
        _resumeReason.Text = reason; _resumePanel.Visible = true;
        _resumePanel.GetChild<VBoxContainer>(0).GetChildren().OfType<Button>().First().GrabFocus();
    }

    private void ChangePause(bool paused)
    {
        _clock.Paused = paused;
        _pending.Clear(); _pending.Add(new(CombatCommandKind.Stop));
        _moveX = _moveZ = int.MinValue;
        _awaitingKey = null;
        _neutralMovementRequired = true;
        foreach (string action in _keys.Keys) Input.ActionRelease("aw_" + action);
        if (!paused && _resumePanel is not null) _resumePanel.Visible = false;
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
    { Message(text); if (_releaseSettingsStatus is not null) _releaseSettingsStatus.Text = text; }
}
