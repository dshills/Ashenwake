using Godot;

namespace Ashenwake.Client;

/// <summary>All facts and recovery permissions come from the authoritative director.</summary>
public sealed record DeathRecapPresentation(string Title, string KillingBlow, string RecentDamage,
    string Conditions, string Counterplay, string Recovery, string PrimaryLabel, bool CanPrimary, bool CanReturnToHub);

/// <summary>A read-only death summary. Recovery commands belong to the director.</summary>
public partial class DeathRecapHud : Control
{
    public event Action? PrimaryRequested;
    public event Action? HubRequested;
    public event Action? CloseRequested;
    public bool IsOpen => _open && IsVisibleInTree();
    private DeathRecapPresentation? _view;
    private PanelContainer _panel = null!;
    private ScrollContainer _scroll = null!;
    private Label _title = null!, _killingBlow = null!, _recent = null!, _conditions = null!, _counterplay = null!, _recovery = null!;
    private Button _primary = null!, _hub = null!, _close = null!;
    private Sandbox? _sandbox;
    private bool _open, _armed, _pauseHeld, _layoutDirty;
    private static readonly string[] NavigationActions = ["ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev"];

    public override void _Ready()
    {
        Name = "DeathRecapHud";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var backdrop = new ColorRect
        {
            Name = "DeathRecapBackdrop",
            Color = new("06090fee"),
            MouseFilter = MouseFilterEnum.Stop,
            MouseForcePassScrollEvents = false
        };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(backdrop);
        _panel = new PanelContainer { Name = "DeathRecapPanel", MouseFilter = MouseFilterEnum.Stop, MouseForcePassScrollEvents = false };
        // Wrapped text initially measures before its container receives a width.
        // Reapply the viewport bounds once that minimum size settles; otherwise
        // PanelContainer retains the oversized first measurement indefinitely.
        _panel.MinimumSizeChanged += () => _layoutDirty = true;
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new("111a22fc"),
            BorderColor = new("ab8557"),
            BorderWidthBottom = 1,
            BorderWidthTop = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            ContentMarginLeft = 20,
            ContentMarginRight = 20,
            ContentMarginTop = 18,
            ContentMarginBottom = 18
        });
        AddChild(_panel);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 12); _panel.AddChild(column);
        _title = Caption("You fell", "DeathRecapTitle", 27, "edcf9e"); column.AddChild(_title);
        column.AddChild(Caption("Review the encounter before continuing.", "DeathRecapSubtitle", 13, "aebac4"));
        _scroll = new ScrollContainer
        {
            Name = "DeathRecapScroll",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new(0, 160),
            FocusMode = FocusModeEnum.All
        };
        column.AddChild(_scroll);
        var sections = new VBoxContainer { Name = "DeathRecapSections", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        sections.AddThemeConstantOverride("separation", 10); _scroll.AddChild(sections);
        _killingBlow = Section(sections, "KILLING BLOW", "DeathRecapKillingBlow");
        _recent = Section(sections, "RECENT DAMAGE", "DeathRecapRecentDamage");
        _conditions = Section(sections, "HARMFUL CONDITIONS", "DeathRecapConditions");
        _counterplay = Section(sections, "NEXT ATTEMPT", "DeathRecapCounterplay");
        _recovery = Section(sections, "RECOVERY", "DeathRecapRecovery");
        column.AddChild(new HSeparator());
        var actions = new HBoxContainer { Name = "DeathRecapActions" }; actions.AddThemeConstantOverride("separation", 8); column.AddChild(actions);
        _primary = ActionButton("DeathRecapPrimary", "Continue", () => { if (_view?.CanPrimary == true) PrimaryRequested?.Invoke(); }); actions.AddChild(_primary);
        _hub = ActionButton("DeathRecapHub", "Return to Greyhaven", () => { if (_view?.CanReturnToHub == true) HubRequested?.Invoke(); }); actions.AddChild(_hub);
        _close = ActionButton("DeathRecapClose", "Close recap", () => CloseRequested?.Invoke()); actions.AddChild(_close);
        for (Node? parent = GetParent(); parent is not null; parent = parent.GetParent())
            if (parent is Sandbox sandbox) { _sandbox = sandbox; break; }
        VisibilityChanged += SynchronizePause;
        GetViewport().SizeChanged += Layout;
        Render(); Layout(); Hide();
    }

    public void SetView(DeathRecapPresentation view)
    {
        if (_view == view) return;
        _view = view;
        if (IsNodeReady()) Render();
    }

    public void Open()
    {
        if (_view is null || !IsNodeReady() || IsOpen) return;
        _open = true; _armed = false;
        Show(); Render(); Layout(); _scroll.ScrollVertical = 0;
        SynchronizePause();
        // Focus is assigned only after held accept/attack input is released.
    }

    public void Close()
    {
        _open = false; _armed = false;
        // Directors can dismiss after this child has left the tree during shutdown.
        var focus = IsInsideTree() ? GetViewport()?.GuiGetFocusOwner() : null;
        if (focus is not null && IsAncestorOf(focus)) focus.ReleaseFocus();
        Hide(); SynchronizePause();
    }

    public override void _Process(double delta)
    {
        if (_layoutDirty) { _layoutDirty = false; Layout(); }
        if (!IsOpen || _armed || SettingsVisible() || Input.IsActionPressed("ui_accept") ||
            Input.IsMouseButtonPressed(MouseButton.Left) || Input.IsMouseButtonPressed(MouseButton.Right)) return;
        _armed = true; UpdateButtons();
        (_view?.CanPrimary == true ? _primary : _close).GrabFocus();
    }

    public override void _Input(InputEvent input)
    {
        if (!IsOpen || SettingsVisible()) return;
        if (input.IsActionPressed("ui_cancel"))
        {
            if (_armed && !input.IsEcho()) CloseRequested?.Invoke();
            GetViewport().SetInputAsHandled(); return;
        }
        if (input is not (InputEventKey or InputEventJoypadButton)) return;
        if (input.IsAction("aw_settings")) return;
        if (input is InputEventKey key && (key.PhysicalKeycode is Key.Home or Key.End or Key.Pageup or Key.Pagedown ||
            key.Keycode is Key.Home or Key.End or Key.Pageup or Key.Pagedown)) return;
        if (!_armed || !NavigationActions.Any(action => input.IsAction(action))) GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (IsOpen && !SettingsVisible() && input is (InputEventKey or InputEventJoypadButton) && !input.IsAction("aw_settings"))
            GetViewport().SetInputAsHandled();
    }

    private bool SettingsVisible() => _sandbox?.FindChild("SettingsPanel", true, false) is Control { Visible: true };

    private void Render()
    {
        if (_view is not { } view || _title is null) return;
        _title.Text = view.Title; _killingBlow.Text = view.KillingBlow; _recent.Text = view.RecentDamage;
        _conditions.Text = view.Conditions; _counterplay.Text = view.Counterplay; _recovery.Text = view.Recovery;
        _primary.Text = view.PrimaryLabel; _hub.Visible = view.CanReturnToHub;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _primary.Disabled = !_armed || _view?.CanPrimary != true;
        _hub.Disabled = !_armed || _view?.CanReturnToHub != true;
        _close.Disabled = !_armed;
    }

    private void SynchronizePause()
    {
        bool visible = IsOpen;
        if (!visible) _armed = false;
        if (_sandbox is not null && GodotObject.IsInstanceValid(_sandbox) && (visible || _pauseHeld))
            _sandbox.SetModalPaused("death-recap", visible);
        _pauseHeld = visible;
    }

    public override void _ExitTree()
    {
        if (GetViewport() is { } viewport) viewport.SizeChanged -= Layout;
        if (_pauseHeld && _sandbox is not null && GodotObject.IsInstanceValid(_sandbox))
            _sandbox.SetModalPaused("death-recap", false);
        _pauseHeld = false; _open = false; _armed = false;
    }

    private void Layout()
    {
        var viewport = GetViewportRect().Size;
        var size = new Vector2(Math.Min(860, viewport.X - 40), Math.Min(760, viewport.Y - 40));
        var position = (viewport - size) / 2;
        if (_panel.Position != position) _panel.Position = position;
        if (_panel.Size != size) _panel.Size = size;
    }

    private static Label Section(VBoxContainer parent, string heading, string name)
    {
        parent.AddChild(Caption(heading, name + "Heading", 13, "dec99a"));
        var text = Caption("", name, 14, "d4dde3"); parent.AddChild(text); return text;
    }

    private static Label Caption(string text, string name, int size, string color)
    {
        var label = new Label
        {
            Name = name,
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new(color)
        };
        label.AddThemeFontSizeOverride("font_size", size); return label;
    }

    private Button ActionButton(string name, string text, Action action)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            CustomMinimumSize = new(0, 42),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        button.AddThemeFontSizeOverride("font_size", 13);
        button.Pressed += () => { if (IsOpen && _armed) action(); }; return button;
    }
}
