using Ashenwake.Core.Combat;
using Ashenwake.Core.Training;
using Godot;

namespace Ashenwake.Client;

/// <summary>Read-only practice results; the director owns entry, reset, and exit.</summary>
public partial class TrainingHud : Control
{
    public event Action<TrainingTargetMode>? ResetRequested;
    public event Action? LeaveRequested;
    public event Action<bool>? ReportVisibilityChanged;
    public bool ReportOpen => _reportPanel?.Visible == true;
    private TrainingReport? _report;
    private PanelContainer _summary = null!, _reportPanel = null!;
    private ColorRect _backdrop = null!;
    private Label _stats = null!, _state = null!, _reportStats = null!;
    private VBoxContainer _rows = null!;
    private Button _resume = null!;
    private TrainingTargetMode _mode;
    private bool _reportDirty = true;

    public override void _Ready()
    {
        Name = "TrainingHud"; MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _summary = Panel("TrainingSummary"); AddChild(_summary);
        var column = new VBoxContainer(); _summary.AddChild(column);
        column.AddChild(Caption("TORREN’S PROVING GROUND", 17));
        _stats = Caption("", 14); _stats.Name = "TrainingLiveStats"; column.AddChild(_stats);
        _state = Caption("Your current build · no XP, mastery or loot", 12); column.AddChild(_state);
        var controls = new HBoxContainer(); column.AddChild(controls);
        Button(controls, "Single target", "TrainingSingle", () => ResetRequested?.Invoke(TrainingTargetMode.Single));
        Button(controls, "Target group", "TrainingGroup", () => ResetRequested?.Invoke(TrainingTargetMode.Group));
        Button(controls, "Reset", "TrainingReset", () => ResetRequested?.Invoke(_mode));
        Button(controls, "Breakdown", "TrainingBreakdown", () => SetReportOpen(true));
        Button(controls, "Leave training", "TrainingLeave", () => LeaveRequested?.Invoke());

        _backdrop = new ColorRect { Name = "TrainingReportBackdrop", Color = new("050b14e8"), MouseFilter = MouseFilterEnum.Stop, MouseForcePassScrollEvents = false, Visible = false };
        _backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(_backdrop);
        _reportPanel = Panel("TrainingReport"); _reportPanel.Visible = false; AddChild(_reportPanel);
        var reportColumn = new VBoxContainer(); reportColumn.AddThemeConstantOverride("separation", 10); _reportPanel.AddChild(reportColumn);
        reportColumn.AddChild(Caption("PRACTICE BREAKDOWN", 23));
        _reportStats = Caption("", 15); reportColumn.AddChild(_reportStats);
        reportColumn.AddChild(Caption("Time includes movement and waiting; opening this report pauses practice. Damage counts health removed, excluding overkill. These stationary targets have no armor or resistance.", 12));
        reportColumn.AddChild(Caption("Reset refills health and potions, clears cooldowns and starts resource at zero. Fallen effigies supply practice corpses. Defensive powers need incoming attacks and cannot be measured here.", 12));
        var scroll = new ScrollContainer { Name = "TrainingReportScroll", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        reportColumn.AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _rows.AddThemeConstantOverride("separation", 7); scroll.AddChild(_rows);
        reportColumn.AddChild(Caption("Practice changes stay here. Return to Torren or Mara to change your build, then enter again to compare it.", 12));
        var actions = new HBoxContainer(); reportColumn.AddChild(actions);
        _resume = Button(actions, "Resume practice", "TrainingResume", () => SetReportOpen(false));
        Button(actions, "Reset practice", "TrainingReportReset", () => { SetReportOpen(false); ResetRequested?.Invoke(_mode); });
        Button(actions, "Return to Greyhaven", "TrainingReportLeave", () => LeaveRequested?.Invoke());
        GetViewport().SizeChanged += Layout; Layout(); Hide();
    }
    public override void _ExitTree() { GetViewport().SizeChanged -= Layout; ReportVisibilityChanged?.Invoke(false); }
    public void Present(TrainingReport report)
    {
        // Practice is paused while this panel is open. Also avoid rebuilding its
        // controls if another presentation refresh repeats the same measurement.
        _reportDirty |= _report?.ElapsedTicks != report.ElapsedTicks || _report?.Mode != report.Mode ||
            _report?.Complete != report.Complete || _report?.TotalDamage != report.TotalDamage || _report?.ResourceName != report.ResourceName;
        _report = report; _mode = report.Mode;
        _stats.Text = $"{(report.Mode == TrainingTargetMode.Single ? "Single target" : "Target group")} · {report.ElapsedSeconds:0.0}s · {report.TotalDamage:N0} damage · {report.DamagePerSecond:0.0} DPS";
        _state.Text = report.Complete ? "Practice complete · reset to try again, or inspect the breakdown" : "Your current build · no XP, mastery or loot · five-minute practice limit";
        if (ReportOpen) PopulateReport();
    }
    public void SetReportOpen(bool open)
    {
        if (open && (!Visible || _report is null)) return;
        if (ReportOpen == open) return;
        _reportPanel.Visible = _backdrop.Visible = open;
        if (open) { PopulateReport(); Layout(); _resume.GrabFocus(); }
        else GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        ReportVisibilityChanged?.Invoke(open);
    }
    public void Dismiss() { SetReportOpen(false); _reportDirty = true; Hide(); }
    private void PopulateReport()
    {
        if (!_reportDirty || _report is not { } report) return;
        _reportDirty = false;
        _reportStats.Text = $"{report.TotalDamage:N0} damage · {report.ElapsedSeconds:0.0}s · {report.DamagePerSecond:0.0} DPS · {report.TargetsDefeated} defeated";
        _resume.Disabled = report.Complete; _resume.Text = report.Complete ? "Practice complete" : "Resume practice";
        foreach (var child in _rows.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
        Heading("DAMAGE BY SOURCE");
        if (report.Damage.Count == 0) _rows.AddChild(Caption("Strike a target to begin measuring your build.", 13));
        foreach (var row in report.Damage)
            _rows.AddChild(Caption($"{row.Name} · {row.Category} · {Family(row.Family)}\n{row.Damage:N0} damage · {row.Hits:N0} hits · {(report.TotalDamage == 0 ? 0 : 100d * row.Damage / report.TotalDamage):0.0}%", 14));
        Heading("DAMAGE TYPES");
        foreach (var family in report.Damage.GroupBy(r => r.Family).OrderByDescending(g => g.Sum(r => r.Damage)))
            _rows.AddChild(Caption($"{Family(family.Key)} · {family.Sum(r => r.Damage):N0} damage", 13));
        Heading("LEGENDARY & FRAGMENT TRIGGERS");
        if (report.Triggers.Count == 0) _rows.AddChild(Caption("No triggers recorded yet. Trigger counts also include effects that deal no damage.", 13));
        foreach (var trigger in report.Triggers)
            _rows.AddChild(Caption($"{trigger.Name} · {trigger.Kind.Replace("Triggered", " activated", StringComparison.Ordinal).Replace("Readied", " readied", StringComparison.Ordinal)} · {trigger.Count:N0}", 13));
        Heading(report.ResourceName.ToUpperInvariant());
        _rows.AddChild(Caption($"Current {report.CurrentResource} · increased {report.ResourceIncreased:N0} · decreased {report.ResourceDecreased:N0}", 14));
        _rows.AddChild(Caption("Resource changes are measured at each change, including costs, generation, decay and Heat venting; capped overflow is excluded.", 12));
        foreach (var row in report.Resources)
            _rows.AddChild(Caption($"{row.Reason.Replace('.', ' ').Replace('_', ' ')} · +{row.Increased:N0} / −{row.Decreased:N0}", 13));
    }
    private void Heading(string text) { _rows.AddChild(new HSeparator()); var label = Caption(text, 14); label.Modulate = new("e4c592"); _rows.AddChild(label); }
    private static string Family(DamageFamily family) => family.ToString().Replace("Physical", "Physical ", StringComparison.Ordinal);
    private void Layout()
    {
        var size = GetViewportRect().Size;
        _summary.Position = new(22, 88); _summary.Size = new(Math.Min(1000, size.X - 44), 116);
        _reportPanel.Position = new(22, 22); _reportPanel.Size = size - new Vector2(44, 44);
    }
    private static PanelContainer Panel(string name)
    {
        var panel = new PanelContainer { Name = name, MouseFilter = MouseFilterEnum.Stop, MouseForcePassScrollEvents = false };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new("0c1821fa"), BorderColor = new("8a958c"), BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1, ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10 });
        return panel;
    }
    private static Label Caption(string text, int size)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size); return label;
    }
    private static Button Button(Node parent, string text, string name, Action action)
    {
        var button = new Button { Name = name, Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 32) };
        button.AddThemeFontSizeOverride("font_size", 12); button.Pressed += action; parent.AddChild(button); return button;
    }
}
