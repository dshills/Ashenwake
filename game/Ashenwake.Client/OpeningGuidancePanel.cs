using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>A quiet optional hint, and a paused field guide that can always be revisited.</summary>
public partial class OpeningGuidancePanel : Control
{
    public event Action<string, string>? ActionRequested;
    public event Action<string>? DismissRequested, MenuRequested;
    public event Action<bool>? EnabledRequested, VisibilityChangedByPlayer;
    private OpeningGuidanceView? _view;
    private Sandbox? _sandbox;
    private PanelContainer _panel = null!, _hint = null!;
    private ColorRect _backdrop = null!;
    private VBoxContainer _column = null!, _rows = null!, _hintRows = null!;
    private ScrollContainer _scroll = null!;
    private CheckButton _toggle = null!;
    private Button _close = null!;
    private bool _enabled, _hintAllowed, _pauseHeld;
    private string _notice = "", _signature = "", _bindings = "";
    private int _layoutFrames;
    private double _bindingRefreshSeconds;
    public bool IsOpen => _panel is { Visible: true };
    public bool IsHintVisible => _hint is { Visible: true } && IsVisibleInTree();
    public Func<bool>? HintAllowed { get; set; }
    public void Attach(Sandbox sandbox) => _sandbox = sandbox;
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _backdrop = new ColorRect { Color = new(0, 0, 0, .74f), MouseFilter = MouseFilterEnum.Stop }; AddChild(_backdrop); _backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _panel = Panel("OpeningGuidePanel", "201e26", 16); AddChild(_panel); _column = Stack(10); _panel.AddChild(_column);
        _column.AddChild(Text("FIND YOUR FOOTING", 23));
        _column.AddChild(Text("A field guide for the road ahead. Read at your own pace; the world is paused.", 14));
        _toggle = new CheckButton { Name = "OpeningGuideEnabled", Text = "Show contextual hints", CustomMinimumSize = new(0, 38) };
        _toggle.Toggled += enabled => EnabledRequested?.Invoke(enabled); _column.AddChild(_toggle);
        _scroll = new ScrollContainer { Name = "OpeningGuideScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _column.AddChild(_scroll); _rows = Stack(12); _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill; _scroll.AddChild(_rows);
        _close = Button("Return to the road", "OpeningGuideClose", () => SetOpen(false)); _column.AddChild(_close);
        _hint = Panel("OpeningHint", "221f28", 12); AddChild(_hint); _hintRows = Stack(7); _hint.AddChild(_hintRows);
        _panel.Visible = _backdrop.Visible = _hint.Visible = false;
        VisibilityChanged += UpdateModal; GetViewport().SizeChanged += RequestLayout; RequestLayout();
    }
    public void SetView(OpeningGuidanceView view, bool enabled, string notice = "")
    {
        _bindings = BindingSignature();
        string signature = Ashenwake.Core.Content.JsonData.Write(view) + enabled + notice + _bindings;
        _view = view; _enabled = enabled; _notice = notice;
        if (_signature == signature) return; _signature = signature;
        _toggle.SetPressedNoSignal(enabled); RebuildHint(); if (IsOpen) Rebuild(); RequestLayout();
    }
    public void SetOpen(bool open)
    {
        if (open && _view is not null) SetView(_view, _enabled, _notice);
        _panel.Visible = open;
        if (open) { Rebuild(); _close.GrabFocus(); GetParent().MoveChild(this, GetParent().GetChildCount() - 1); }
        UpdateModal(); UpdateHintVisibility(); RequestLayout(); VisibilityChangedByPlayer?.Invoke(open);
    }
    public void SetHintVisible(bool visible) { _hintAllowed = visible; UpdateHintVisibility(); }
    public void Reset() { _view = null; _signature = ""; _hintAllowed = false; SetOpen(false); }
    public override void _Process(double delta)
    {
        _bindingRefreshSeconds -= delta;
        if (_bindingRefreshSeconds <= 0)
        {
            _bindingRefreshSeconds = .5;
            if (_view is not null && _bindings != BindingSignature()) SetView(_view, _enabled, _notice);
        }
        UpdateHintVisibility();
        if (_layoutFrames > 0) { _layoutFrames--; UpdateLayout(); }
    }
    public override void _Input(InputEvent input)
    {
        if (!IsOpen || !IsVisibleInTree() || input is not (InputEventKey or InputEventJoypadButton)) return;
        if (input.IsActionPressed("ui_cancel") || input.IsActionPressed("aw_settings")) { SetOpen(false); GetViewport().SetInputAsHandled(); return; }
        string? action = new[] { "aw_inventory", "aw_character", "aw_journey", "aw_endgame", "aw_experiment" }.FirstOrDefault(a => input.IsActionPressed(a));
        if (action is not null) { SetOpen(false); MenuRequested?.Invoke(action); GetViewport().SetInputAsHandled(); return; }
        if (!new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev", "aw_save", "aw_load" }.Any(a => input.IsAction(a))) GetViewport().SetInputAsHandled();
    }
    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= RequestLayout;
        if (!_pauseHeld) return; _pauseHeld = false; var sandbox = _sandbox;
        Callable.From(() => { if (sandbox is not null && GodotObject.IsInstanceValid(sandbox) && sandbox.IsInsideTree() && !sandbox.IsQueuedForDeletion()) sandbox.SetModalPaused("opening-guide", false); }).CallDeferred();
    }
    private void Rebuild()
    {
        if (_view is null) return;
        string focus = GetViewport().GuiGetFocusOwner()?.Name.ToString() ?? ""; Clear(_rows);
        Section("YOUR FIRST BUILD", _view.Steps);
        Section("GREYHAVEN SERVICES", _view.Services);
        Section("THE BASICS", _view.Basics);
        if (_notice.Length > 0) _rows.AddChild(Text(_notice, 13));
        if (focus.Length > 0 && FindChild(focus, true, false) is Control control) control.GrabFocus();
        RequestLayout();
    }
    private void Section(string title, OpeningGuidanceCard[] cards)
    {
        _rows.AddChild(new HSeparator()); var heading = Text(title, 12); heading.AddThemeColorOverride("font_color", new("d4bd87")); _rows.AddChild(heading);
        if (cards.Length == 0) { _rows.AddChild(Text("Keep exploring the Grey March. New opportunities will appear here as you find them.", 14)); return; }
        foreach (var card in cards)
        {
            var block = Stack(6); block.Name = "OpeningGuideCard_" + Safe(card.Id); _rows.AddChild(block);
            block.AddChild(Text((card.Completed ? "✓ " : "") + card.Title, 17)); block.AddChild(Text(Resolve(card.Text), 14));
            string controls = Controls(card.Id); if (controls.Length > 0) block.AddChild(Text(controls, 12));
            if (card.Action.Length > 0) block.AddChild(Button(ActionLabel(card), "OpeningGuideAction_" + Safe(card.Id), () => Dispatch(card)));
        }
    }
    private void RebuildHint()
    {
        Clear(_hintRows);
        if (_view?.Hint is not { } hint) { UpdateHintVisibility(); return; }
        _hintRows.AddChild(Text(hint.Title, 17));
        string copy = Resolve(hint.Text); string controls = Controls(hint.Id);
        if (controls.Length > 0) copy += "\n" + controls;
        var body = Text(copy, 13); body.MaxLinesVisible = 3; body.TooltipText = copy; _hintRows.AddChild(body);
        var actions = new HBoxContainer(); actions.AddThemeConstantOverride("separation", 6); _hintRows.AddChild(actions);
        if (hint.Action.Length > 0) actions.AddChild(Button(ActionLabel(hint), "OpeningHintInspect", () => Dispatch(hint)));
        actions.AddChild(Button("Got it", "OpeningHintDismiss", () => { if (_view?.Hint?.Id == hint.Id) DismissRequested?.Invoke(hint.Id); }));
        actions.AddChild(Button("Hide hints", "OpeningHintDisable", () => EnabledRequested?.Invoke(false)));
        UpdateHintVisibility();
    }
    private void Dispatch(OpeningGuidanceCard card)
    {
        if (_view is null || !new[] { _view.Hint }.Concat(_view.Steps).Concat(_view.Services).Concat(_view.Basics).Any(c => c == card)) return;
        SetOpen(false); ActionRequested?.Invoke(card.Action, card.Target);
    }
    private static string ActionLabel(OpeningGuidanceCard card) => card.Action switch
    {
        "inspect_gear" => "Inspect equipment",
        "inspect_anatomy" => "Inspect anatomy",
        "journey" => "View journey",
        "service" => "Visit specialist",
        "stash" => "Inspect stash",
        "training" => "Visit training ground",
        "hunts" => "Inspect hunt board",
        _ => "Inspect"
    };
    private void UpdateHintVisibility() { if (_hint is not null) _hint.Visible = _hintAllowed && (HintAllowed?.Invoke() ?? true) && _enabled && !IsOpen && _view?.Hint is not null; }
    private void UpdateModal()
    {
        if (_backdrop is null) return; bool open = IsVisibleInTree() && IsOpen; _backdrop.Visible = open;
        if (_pauseHeld != open) { _pauseHeld = open; _sandbox?.SetModalPaused("opening-guide", open); }
    }
    private void RequestLayout() { _layoutFrames = 5; UpdateLayout(); }
    private void UpdateLayout()
    {
        if (_panel is null) return;
        var viewport = GetViewport().GetVisibleRect().Size;
        var size = new Vector2(Math.Min(850, viewport.X - 32), Math.Min(700, viewport.Y - 32));
        _panel.Size = size; _panel.Position = (viewport - size) / 2; _column.Size = size - new Vector2(32, 32);
        float width = Math.Min(420, viewport.X - 44); _hint.Size = new(width, 0); _hintRows.CustomMinimumSize = new(width - 24, 0);
        _hint.Position = new(22, viewport.Y - 210 - _hint.Size.Y);
        _panel.QueueSort(); _column.QueueSort(); _hint.QueueSort();
    }
    private static string Resolve(string text) => text.Replace("{move}", $"{Binding("up")}/{Binding("left")}/{Binding("down")}/{Binding("right")}").Replace("{interact}", Binding("interact")).Replace("{dodge}", Binding("dodge")).Replace("{loot}", Binding("pickup")).Replace("{inventory}", Binding("inventory")).Replace("{journey}", Binding("journey")).Replace("{interrupt}", "an interrupt-capable skill");
    private static string Controls(string id) => id switch
    {
        "hint.move" => $"Move: left-click ground, or {Binding("up")}/{Binding("left")}/{Binding("down")}/{Binding("right")} · Stop: {Binding("stop")}",
        "hint.interact" => $"Interact: {Binding("interact")} near a marker, or click it to approach.",
        "hint.dodge" => $"Dodge: {Binding("dodge")} · Move out of the marked area before impact.",
        "hint.interrupt" => "Check your skill tooltips for Interrupt. Cast before the warning resolves.",
        "hint.loot" => $"Collect nearby loot: {Binding("pickup")} · Inventory: {Binding("inventory")}",
        _ => ""
    };
    private static string Binding(string action)
    {
        var key = InputMap.ActionGetEvents("aw_" + action).OfType<InputEventKey>().FirstOrDefault();
        return key is null ? "Unbound" : OS.GetKeycodeString(key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode);
    }
    private static string BindingSignature() => string.Join('|', new[] { "up", "left", "down", "right", "stop", "interact", "dodge", "pickup", "inventory", "journey" }.Select(Binding));
    private static string Safe(string id) => new(id.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    private static void Clear(Node node) { foreach (var child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }
    private static VBoxContainer Stack(int gap) { var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", gap); return box; }
    private static PanelContainer Panel(string name, string color, int margin)
    {
        var panel = new PanelContainer { Name = name, MouseForcePassScrollEvents = false };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new(color), BorderColor = new("927653"), BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1, ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin, ContentMarginBottom = margin }); return panel;
    }
    private static Label Text(string text, int size) { var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore }; label.AddThemeFontSizeOverride("font_size", size); return label; }
    private static Button Button(string text, string name, Action action) { var button = new Button { Name = name, Text = text, CustomMinimumSize = new(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart }; button.AddThemeFontSizeOverride("font_size", 13); button.Pressed += action; return button; }
}
