using Ashenwake.Core.Combat;
using Ashenwake.Core.Training;
using Godot;

namespace Ashenwake.Client;

/// <summary>Read-only practice results; the director owns entry, reset, comparison history, and exit.</summary>
public partial class TrainingHud : Control
{
    public event Action<TrainingTargetMode>? ResetRequested;
    public event Action? LeaveRequested;
    public event Action<bool>? ReportVisibilityChanged;
    public bool ReportOpen => _reportPanel?.Visible == true;
    public bool ReportInputArmed => _reportArmed;
    private TrainingReport? _report, _previous;
    private string _previousBuild = "", _currentBuild = "";
    private PanelContainer _summary = null!, _reportPanel = null!;
    private ColorRect _backdrop = null!;
    private Label _stats = null!, _state = null!, _reportStats = null!, _conditions = null!;
    private Label _defense = null!, _incoming = null!, _defenseTriggers = null!, _comparison = null!;
    private Label _damage = null!, _families = null!, _triggers = null!, _resources = null!, _resourceHeading = null!;
    private Button _resume = null!, _reportReset = null!, _reportLeave = null!;
    private OptionButton _sparring = null!;
    private ScrollContainer _scroll = null!;
    private Sandbox? _sandbox;
    private TrainingTargetMode _mode;
    private bool _reportDirty = true, _reportArmed, _layoutDirty;

    public override void _Ready()
    {
        Name = "TrainingHud"; MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _summary = Panel("TrainingSummary"); AddChild(_summary);
        _summary.MinimumSizeChanged += () => _layoutDirty = true;
        var column = new VBoxContainer(); _summary.AddChild(column);
        column.AddChild(Caption("TORREN’S PROVING GROUND", 15));
        _stats = Caption("", 13); _stats.Name = "TrainingLiveStats"; column.AddChild(_stats);
        _state = Caption("Your current build · no XP, mastery or loot", 11); column.AddChild(_state);
        var controls = new HBoxContainer(); column.AddChild(controls);
        Button(controls, "Single target", "TrainingSingle", () => ResetRequested?.Invoke(TrainingTargetMode.Single));
        Button(controls, "Target group", "TrainingGroup", () => ResetRequested?.Invoke(TrainingTargetMode.Group));
        _sparring = new OptionButton { Name = "TrainingSparringMode", SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 32), TooltipText = "Start a fresh defensive practice attempt. Enemies attack with normal combat rules." };
        _sparring.AddThemeFontSizeOverride("font_size", 12);
        _sparring.AddItem("Sparring…", -1);
        foreach (var mode in new[] { TrainingTargetMode.Melee, TrainingTargetMode.Ranged, TrainingTargetMode.Mixed }) _sparring.AddItem(ModeName(mode), (int)mode);
        _sparring.ItemSelected += index => { if (index > 0 && !ReportOpen) ResetRequested?.Invoke((TrainingTargetMode)_sparring.GetItemId((int)index)); };
        controls.AddChild(_sparring);
        Button(controls, "Reset", "TrainingReset", () => ResetRequested?.Invoke(_mode));
        Button(controls, "Breakdown", "TrainingBreakdown", () => SetReportOpen(true));
        Button(controls, "Leave", "TrainingLeave", () => LeaveRequested?.Invoke());

        _backdrop = new ColorRect { Name = "TrainingReportBackdrop", Color = new("050b14e8"), MouseFilter = MouseFilterEnum.Stop, MouseForcePassScrollEvents = false, Visible = false };
        _backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(_backdrop);
        _reportPanel = Panel("TrainingReport"); _reportPanel.Visible = false; AddChild(_reportPanel);
        _reportPanel.MinimumSizeChanged += () => _layoutDirty = true;
        var reportColumn = new VBoxContainer(); reportColumn.AddThemeConstantOverride("separation", 10); _reportPanel.AddChild(reportColumn);
        reportColumn.AddChild(Caption("PRACTICE BREAKDOWN", 23));
        _reportStats = Caption("", 15); _reportStats.Name = "TrainingReportStats"; reportColumn.AddChild(_reportStats);
        _scroll = new ScrollContainer { Name = "TrainingReportScroll", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new(0, 160), FocusMode = FocusModeEnum.All };
        reportColumn.AddChild(_scroll);
        var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; rows.AddThemeConstantOverride("separation", 9); _scroll.AddChild(rows);
        _conditions = Section(rows, "PRACTICE CONDITIONS", "TrainingConditions");
        _defense = Section(rows, "DEFENSE", "TrainingDefense");
        _incoming = Section(rows, "INCOMING ATTACKS", "TrainingIncomingDamage");
        _defenseTriggers = Section(rows, "DEFENSIVE EFFECTS", "TrainingDefensiveTriggers");
        _comparison = Section(rows, "PREVIOUS ATTEMPT", "TrainingComparison");
        _damage = Section(rows, "DAMAGE BY SOURCE", "TrainingDamageSources");
        _families = Section(rows, "DAMAGE TYPES", "TrainingDamageTypes");
        _triggers = Section(rows, "LEGENDARY & FRAGMENT TRIGGERS", "TrainingTriggers");
        _resourceHeading = Heading(rows, "RESOURCE");
        _resources = Caption("", 14); _resources.Name = "TrainingResources"; rows.AddChild(_resources);
        rows.AddChild(Caption("Practice changes stay here. Return to Torren or Mara to change your build, then enter again. Only the previous attempt for this character is kept during this session; comparison history is not saved.", 12));
        reportColumn.AddChild(new HSeparator());
        var actions = new HBoxContainer(); reportColumn.AddChild(actions);
        _resume = Button(actions, "Resume practice", "TrainingResume", () => SetReportOpen(false), true);
        _reportReset = Button(actions, "Reset practice", "TrainingReportReset", () => { SetReportOpen(false); ResetRequested?.Invoke(_mode); }, true);
        _reportLeave = Button(actions, "Return to Greyhaven", "TrainingReportLeave", () => LeaveRequested?.Invoke(), true);
        for (Node? parent = GetParent(); parent is not null; parent = parent.GetParent())
            if (parent is Sandbox sandbox) { _sandbox = sandbox; break; }
        GetViewport().SizeChanged += Layout; Layout(); Hide();
    }

    public override void _ExitTree() { GetViewport().SizeChanged -= Layout; ReportVisibilityChanged?.Invoke(false); }

    public override void _Process(double delta)
    {
        if (_layoutDirty) { _layoutDirty = false; Layout(); }
        if (!ReportOpen || _reportArmed || _sandbox?.FindChild("SettingsPanel", true, false) is Control { Visible: true } ||
            Input.IsActionPressed("ui_accept") || Input.IsMouseButtonPressed(MouseButton.Left) || Input.IsMouseButtonPressed(MouseButton.Right)) return;
        _reportArmed = true; UpdateActions();
        (_report?.Complete == true ? _reportReset : _resume).GrabFocus();
    }

    public void SetComparison(TrainingReport? previous, string previousBuild, string currentBuild)
    {
        if (ReferenceEquals(previous, _previous) && previousBuild == _previousBuild && currentBuild == _currentBuild) return;
        _previous = previous; _previousBuild = previousBuild; _currentBuild = currentBuild; _reportDirty = true;
        if (ReportOpen) PopulateReport();
    }

    public void Present(TrainingReport report)
    {
        // All result controls are stable. Only their text changes when measurements change.
        _reportDirty |= _report?.ElapsedTicks != report.ElapsedTicks || _report?.Mode != report.Mode ||
            _report?.Complete != report.Complete || _report?.TotalDamage != report.TotalDamage || _report?.ResourceName != report.ResourceName;
        _report = report; _mode = report.Mode;
        _sparring.Select(report.Mode is TrainingTargetMode.Single or TrainingTargetMode.Group ? 0 : _sparring.GetItemIndex((int)report.Mode));
        _stats.Text = $"{ModeName(report.Mode)} · {report.ElapsedSeconds:0.0}s · {report.TotalDamage:N0} damage · {report.DamagePerSecond:0.0} DPS · {report.Defense.HealthLost:N0} health lost";
        _state.Text = report.Complete ? "Practice ended · reset to try again, or inspect the breakdown" : "Your current build · no XP, mastery or loot · five-minute practice limit";
        if (ReportOpen) PopulateReport();
    }

    public void SetReportOpen(bool open)
    {
        if (open && (!Visible || _report is null)) return;
        if (ReportOpen == open) return;
        _reportArmed = false;
        _reportPanel.Visible = _backdrop.Visible = open;
        foreach (var button in _summary.FindChildren("*", "BaseButton", true, false).OfType<BaseButton>()) button.Disabled = open;
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        if (open) { PopulateReport(); UpdateActions(); Layout(); _scroll.ScrollVertical = 0; }
        ReportVisibilityChanged?.Invoke(open);
    }

    public void Dismiss() { SetReportOpen(false); _reportDirty = true; Hide(); }

    private void PopulateReport()
    {
        if (!_reportDirty || _report is not { } report) return;
        _reportDirty = false;
        _reportStats.Text = $"{ModeName(report.Mode)} · {report.ElapsedSeconds:0.0}s · {report.TotalDamage:N0} damage · {report.DamagePerSecond:0.0} DPS · {report.TargetsDefeated} defeated";
        bool passive = report.Mode is TrainingTargetMode.Single or TrainingTargetMode.Group;
        _conditions.Text = "Time includes movement and waiting; this report pauses practice. Outgoing damage counts health removed, excluding overkill. " +
            (passive ? "Stationary effigies have no armor or resistance and do not attack. Choose sparring to test defenses." : "Sparring enemies use normal attacks and defenses. Movement, attacks, enemy defeats, and ability timing affect exposure.") +
            "\nReset refills health and potions, clears cooldowns and starts resource at zero. A defeat ends only this practice attempt.";
        var defense = report.Defense;
        _defense.Text = $"{defense.HealthLost:N0} health lost · {defense.BarrierAbsorbed:N0} absorbed by barriers\n{defense.MitigatedDamage:N0} prevented by armor / resistance · {defense.ImmuneDamage:N0} prevented by immunity\n{defense.RawDamage:N0} incoming damage across {defense.Hits:N0} resolved hits · {defense.Overkill:N0} overkill excluded from health lost\n" +
            "Incoming damage partitions into mitigation, immunity, barrier absorption, health lost, and overkill. Attacks avoided by moving out of range are not measured. Health lost is cumulative and does not subtract healing.";
        _incoming.Text = defense.Damage.Count == 0 ? "No incoming hits recorded." : string.Join("\n\n", defense.Damage.Select(row =>
            $"{row.Name} · {row.AttackName} · {Family(row.Family)}\n{row.Hits:N0} hits · {row.HealthLost:N0} health lost · {row.BarrierAbsorbed:N0} absorbed · {row.MitigatedDamage:N0} mitigated · {row.ImmuneDamage:N0} immune"));
        _defenseTriggers.Text = defense.Triggers.Count == 0 ? "No defensive effects recorded yet. Equip a defensive power or use a barrier ability during sparring to observe it." :
            string.Join("\n", defense.Triggers.Select(TriggerText)) + "\nEffect activations count events, not damage prevented. Some effects also appear in the legendary and fragment section below.";
        _comparison.Text = ComparisonText(report);
        _damage.Text = report.Damage.Count == 0 ? "Strike a target to begin measuring your build." : string.Join("\n\n", report.Damage.Select(row =>
            $"{row.Name} · {row.Category} · {Family(row.Family)}\n{row.Damage:N0} damage · {row.Hits:N0} hits · {(report.TotalDamage == 0 ? 0 : 100d * row.Damage / report.TotalDamage):0.0}%"));
        _families.Text = report.Damage.Count == 0 ? "No outgoing damage recorded." : string.Join("\n", report.Damage.GroupBy(r => r.Family).OrderByDescending(g => g.Sum(r => r.Damage)).Select(family => $"{Family(family.Key)} · {family.Sum(r => r.Damage):N0} damage"));
        _triggers.Text = report.Triggers.Count == 0 ? "No triggers recorded yet. Trigger counts also include effects that deal no damage." : string.Join("\n", report.Triggers.Select(TriggerText));
        _resourceHeading.Text = report.ResourceName.ToUpperInvariant();
        _resources.Text = $"Current {report.CurrentResource} · increased {report.ResourceIncreased:N0} · decreased {report.ResourceDecreased:N0}\nResource changes include costs, generation, decay and Heat venting; capped overflow is excluded." +
            (report.Resources.Count == 0 ? "" : "\n" + string.Join("\n", report.Resources.Select(row => $"{row.Reason.Replace('.', ' ').Replace('_', ' ')} · +{row.Increased:N0} / −{row.Decreased:N0}")));
        UpdateActions();
    }

    private string ComparisonText(TrainingReport current)
    {
        string build = _currentBuild.Length == 0 ? "Current build" : _currentBuild;
        if (_previous is not { } previous) return $"Current: {build}\nNo previous attempt yet. Reset or leave training after practicing to retain this attempt for comparison.";
        var now = current.Defense; var before = previous.Defense;
        return $"CURRENT · {build}\n{ModeName(current.Mode)} · {current.ElapsedSeconds:0.0}s\n" +
            $"PREVIOUS · {(_previousBuild.Length == 0 ? "Previous build" : _previousBuild)}\n{ModeName(previous.Mode)} · {previous.ElapsedSeconds:0.0}s\n\n" +
            $"Current / previous\nDamage: {current.TotalDamage:N0} / {previous.TotalDamage:N0} · DPS: {current.DamagePerSecond:0.0} / {previous.DamagePerSecond:0.0}\n" +
            $"Health lost: {now.HealthLost:N0} / {before.HealthLost:N0} · per second: {Rate(now.HealthLost, current):0.0} / {Rate(before.HealthLost, previous):0.0}\n" +
            $"Barrier absorbed: {now.BarrierAbsorbed:N0} / {before.BarrierAbsorbed:N0}\nArmor / resistance prevented: {now.MitigatedDamage:N0} / {before.MitigatedDamage:N0}\nImmunity prevented: {now.ImmuneDamage:N0} / {before.ImmuneDamage:N0}\n" +
            $"Incoming resolved hits: {now.Hits:N0} / {before.Hits:N0} · defensive effect events: {now.Triggers.Sum(t => t.Count):N0} / {before.Triggers.Sum(t => t.Count):N0}\n\n" +
            (current.Mode != previous.Mode || current.ElapsedTicks != previous.ElapsedTicks ? "Modes or durations differ. " : "Modes and durations match. ") +
            "Movement, skill timing, healing, and enemy exposure can still differ. These are observations, not a controlled ranking of builds.";
    }

    private void UpdateActions()
    {
        _resume.Disabled = !_reportArmed || _report?.Complete == true;
        _resume.Text = _report?.Complete == true ? "Practice ended" : "Resume practice";
        _reportReset.Disabled = _reportLeave.Disabled = !_reportArmed;
        // Explicit traversal keeps keyboard/controller focus inside this modal.
        // Disabled Resume is omitted when practice ends.
        Control[] focus = _resume.Disabled ? [_scroll, _reportReset, _reportLeave] : [_scroll, _resume, _reportReset, _reportLeave];
        for (int i = 0; i < focus.Length; i++)
        {
            var previous = focus[i].GetPathTo(focus[(i + focus.Length - 1) % focus.Length]);
            var next = focus[i].GetPathTo(focus[(i + 1) % focus.Length]);
            focus[i].FocusPrevious = focus[i].FocusNeighborLeft = focus[i].FocusNeighborTop = previous;
            focus[i].FocusNext = focus[i].FocusNeighborRight = focus[i].FocusNeighborBottom = next;
        }
    }

    public static string ModeName(TrainingTargetMode mode) => mode switch
    {
        TrainingTargetMode.Single => "Single target",
        TrainingTargetMode.Group => "Target group",
        TrainingTargetMode.Melee => "Melee sparring",
        TrainingTargetMode.Ranged => "Ranged sparring",
        TrainingTargetMode.Mixed => "Mixed sparring",
        _ => "Practice"
    };
    private static double Rate(long count, TrainingReport report) => report.ElapsedSeconds > 0 ? count / report.ElapsedSeconds : 0;
    private static string TriggerText(TrainingTriggerRow row) => $"{row.Name} · {row.Kind.Replace("Triggered", " activated", StringComparison.Ordinal).Replace("Readied", " readied", StringComparison.Ordinal).Replace("Charged", " charged", StringComparison.Ordinal).Replace("Granted", " granted", StringComparison.Ordinal)} · {row.Count:N0}";
    private static Label Section(VBoxContainer parent, string text, string name)
    {
        Heading(parent, text); var label = Caption("", 14); label.Name = name; parent.AddChild(label); return label;
    }
    private static Label Heading(VBoxContainer parent, string text)
    {
        parent.AddChild(new HSeparator()); var label = Caption(text, 14); label.Modulate = new("e4c592"); parent.AddChild(label); return label;
    }
    private static string Family(DamageFamily family) => family.ToString().Replace("Physical", "Physical ", StringComparison.Ordinal);
    private void Layout()
    {
        var size = GetViewportRect().Size;
        _summary.Position = new(22, 88); _summary.Size = new(Math.Min(1000, size.X - 44), 116);
        var reportSize = new Vector2(Math.Min(980, size.X - 44), size.Y - 44);
        if (_reportPanel.Position != (size - reportSize) / 2) _reportPanel.Position = (size - reportSize) / 2;
        if (_reportPanel.Size != reportSize) _reportPanel.Size = reportSize;
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
    private Button Button(Node parent, string text, string name, Action action, bool reportAction = false)
    {
        var button = new Button { Name = name, Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 32), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        button.AddThemeFontSizeOverride("font_size", 12);
        button.Pressed += () => { if (reportAction ? ReportOpen && _reportArmed && !button.Disabled : !ReportOpen) action(); };
        parent.AddChild(button); return button;
    }
}
