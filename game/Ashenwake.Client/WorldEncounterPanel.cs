using Godot;

namespace Ashenwake.Client;

public sealed record WorldEncounterEntryDisplay(string Id, string Name, string Region, string Status,
    string Summary, string Reward, string[] Discoveries);
public sealed record WorldEncounterActionDisplay(string Action, string Id, string Label, string Hint = "",
    bool Enabled = true, string Confirm = "");
public sealed record WorldEncounterDisplay(WorldEncounterEntryDisplay[] Entries, string SelectedId,
    string Heading, string Narrative, WorldEncounterActionDisplay[] Actions, long Revision, string Notice = "");

/// <summary>Optional encounters, choices, and earned discoveries. Core owns every action and outcome.</summary>
public partial class WorldEncounterPanel : Control
{
    public event Action<string, string>? ActionRequested;
    public event Action<string>? SelectionRequested, MenuRequested;
    public event Action<bool>? VisibilityChangedByPlayer, ModalChanged;
    private WorldEncounterDisplay? _view;
    private PanelContainer _panel = null!;
    private ColorRect _backdrop = null!;
    private VBoxContainer _column = null!, _details = null!;
    private OptionButton _entries = null!;
    private ScrollContainer _scroll = null!;
    private Label _summary = null!, _notice = null!;
    private Button _close = null!;
    private ConfirmationDialog _confirmation = null!;
    private WorldEncounterActionDisplay? _pending;
    private long _pendingRevision = -1;
    private Sandbox? _sandbox;
    private bool _pauseHeld;
    private int _oldSibling = -1;
    public bool IsOpen => _panel is { Visible: true };
    public string SelectedEntry => _view?.SelectedId ?? "";

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        for (Node? node = GetParent(); node is not null; node = node.GetParent())
            if (node is Sandbox sandbox) { _sandbox = sandbox; break; }
        _backdrop = new ColorRect { Color = new(0, 0, 0, .74f), MouseFilter = MouseFilterEnum.Stop, MouseForcePassScrollEvents = false };
        AddChild(_backdrop); _backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _panel = new PanelContainer { Name = "WorldEncounterPanel", MouseForcePassScrollEvents = false };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new("141d23"),
            BorderColor = new("a49673"),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 16,
            ContentMarginRight = 16,
            ContentMarginTop = 16,
            ContentMarginBottom = 16
        }); AddChild(_panel);
        _column = Stack(10); _panel.AddChild(_column);
        var title = Text("ENCOUNTERS & RESONANCE STORMS", 23); title.AddThemeColorOverride("font_color", new("decba0")); _column.AddChild(title);
        _summary = Text("Your recorded encounters", 13); _summary.Name = "WorldEncounterJournalSummary"; _column.AddChild(_summary);
        _entries = new OptionButton { Name = "WorldEncounterJournalEntries", SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 42), FitToLongestItem = false };
        _entries.ItemSelected += index => { if (_view is not null && index >= 0 && index < _view.Entries.Length) SelectEntry(_view.Entries[(int)index].Id); };
        _column.AddChild(_entries);
        _scroll = new ScrollContainer
        {
            Name = "WorldEncounterDetailsScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _column.AddChild(_scroll); _details = Stack(10); _details.SizeFlagsHorizontal = SizeFlags.ExpandFill; _scroll.AddChild(_details);
        _notice = Text("", 13); _notice.Name = "WorldEncounterNotice"; _notice.MaxLinesVisible = 3; _column.AddChild(_notice);
        _close = Button("Close [Esc]", "WorldEncounterClose", () => SetOpen(false)); _column.AddChild(_close);
        _confirmation = new ConfirmationDialog { Name = "WorldEncounterConfirmation", DialogAutowrap = true, CancelButtonText = "Keep inspecting" };
        AddChild(_confirmation); _confirmation.Canceled += CancelConfirmation;
        _confirmation.Confirmed += () =>
        {
            var pending = _pending; long revision = _pendingRevision; CancelConfirmation();
            if (pending is not null && revision == _view?.Revision && IsOpen && IsVisibleInTree() && Available(pending)) Dispatch(pending);
        };
        _panel.Visible = _backdrop.Visible = false;
        VisibilityChanged += UpdateModal; _panel.VisibilityChanged += UpdateModal;
        GetViewport().SizeChanged += UpdateLayout; SetProcess(true); UpdateLayout();
    }
    public void SetView(WorldEncounterDisplay view)
    {
        bool changed = _view is null || !SamePresentation(_view, view);
        if (_view is not null && (_view.Revision != view.Revision || changed)) CancelConfirmation();
        bool selectionChanged = _view?.SelectedId != view.SelectedId || _view?.Heading != view.Heading;
        _view = view;
        if (!changed) return;
        if (selectionChanged) _scroll.ScrollVertical = 0;
        if (IsOpen) Rebuild();
    }
    public void SetOpen(bool open)
    {
        if (!open) CancelConfirmation();
        _panel.Visible = open;
        if (open) { Rebuild(); UpdateLayout(); _close.GrabFocus(); }
        UpdateModal(); VisibilityChangedByPlayer?.Invoke(open);
    }
    public void SelectEntry(string id)
    {
        if (_view?.Entries.Any(e => e.Id == id) != true) return;
        CancelConfirmation(); _scroll.ScrollVertical = 0; SelectionRequested?.Invoke(id);
    }
    public void SessionRestored() { CancelConfirmation(); _view = null; SetOpen(false); }
    public void Notice(string text) { _notice.Text = text; _notice.TooltipText = text; _notice.Visible = text.Length > 0; }
    public override void _Process(double delta) { if (IsOpen) UpdateLayout(); }
    public override void _Input(InputEvent input)
    {
        if (!IsOpen || !IsVisibleInTree() || _confirmation.Visible || input is not (InputEventKey or InputEventJoypadButton)) return;
        if (input.IsActionPressed("ui_cancel")) { SetOpen(false); GetViewport().SetInputAsHandled(); return; }
        string? menu = new[] { "aw_inventory", "aw_character", "aw_journey", "aw_endgame", "aw_experiment" }.FirstOrDefault(a => input.IsActionPressed(a));
        if (menu is not null) { CancelConfirmation(); MenuRequested?.Invoke(menu); GetViewport().SetInputAsHandled(); return; }
        if (!new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev", "aw_save", "aw_load" }.Any(a => input.IsAction(a))) GetViewport().SetInputAsHandled();
    }
    public override void _Notification(int what) { if (what == NotificationApplicationFocusOut && IsInsideTree()) CancelConfirmation(); }
    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= UpdateLayout; CancelConfirmation();
        if (!_pauseHeld) return;
        _pauseHeld = false; var sandbox = _sandbox;
        Callable.From(() =>
        {
            if (sandbox is not null && GodotObject.IsInstanceValid(sandbox) && sandbox.IsInsideTree() && !sandbox.IsQueuedForDeletion())
                sandbox.SetModalPaused("world-encounter", false);
        }).CallDeferred();
    }
    private void Rebuild()
    {
        if (_view is null || !IsOpen) return;
        string focus = GetViewport().GuiGetFocusOwner()?.Name.ToString() ?? "";
        _entries.Clear(); Clear(_details); Notice(_view.Notice);
        _summary.Text = _view.Entries.Length == 0 ? "No encounters recorded. Explore secured campaign rooms to find travelers, shrines, and storm fronts." : $"{_view.Entries.Length} recorded encounters · select one below";
        _entries.Visible = _view.Entries.Length > 0;
        foreach (var entry in _view.Entries)
        {
            _entries.AddItem(entry.Name + " · " + entry.Status);
            if (entry.Id == _view.SelectedId) _entries.Select(_entries.ItemCount - 1);
        }
        var selected = _view.Entries.FirstOrDefault(e => e.Id == _view.SelectedId);
        var heading = Text(_view.Heading, 22); heading.Name = "WorldEncounterHeading"; heading.AddThemeColorOverride("font_color", new("e6d7b7")); _details.AddChild(heading);
        if (selected is not null) _details.AddChild(Text(selected.Region + " · " + selected.Status, 13));
        var narrative = Text(_view.Narrative, 16); narrative.Name = "WorldEncounterNarrative"; _details.AddChild(narrative);
        if (selected is not null)
        {
            if (selected.Summary.Length > 0 && !_view.Narrative.Contains(selected.Summary, StringComparison.Ordinal)) _details.AddChild(Text(selected.Summary, 14));
            if (selected.Discoveries.Length > 0)
            {
                _details.AddChild(new HSeparator()); _details.AddChild(Text("RECORDED OBSERVATIONS", 12));
                foreach (string discovery in selected.Discoveries) _details.AddChild(Text("• " + discovery, 13));
            }
            if (selected.Reward.Length > 0)
            {
                var reward = Text("DISCOVERED TREASURE · " + selected.Reward, 14); reward.Name = "WorldEncounterReward";
                reward.AddThemeColorOverride("font_color", new("cbb889")); _details.AddChild(reward);
            }
        }
        if (_view.Actions.Length > 0) _details.AddChild(new HSeparator());
        foreach (var option in _view.Actions)
        {
            var choice = option;
            var button = Button(choice.Label, "WorldEncounterAction_" + SafeName(choice.Action) + "_" + SafeName(choice.Id), () => Request(choice));
            button.Disabled = !choice.Enabled; button.TooltipText = choice.Hint; _details.AddChild(button);
            if (choice.Hint.Length > 0) _details.AddChild(Text(choice.Hint, 12));
        }
        UpdateLayout();
        if (focus.Length > 0 && FindChild(focus, true, false) is Control control && control.IsVisibleInTree()) control.GrabFocus();
    }
    private bool Available(WorldEncounterActionDisplay request) => _view?.Actions.Any(a => a == request && a.Enabled) == true;
    private void Request(WorldEncounterActionDisplay request)
    {
        if (!IsOpen || !IsVisibleInTree() || !Available(request)) return;
        CancelConfirmation();
        if (request.Confirm.Length == 0) { Dispatch(request); return; }
        _pending = request; _pendingRevision = _view!.Revision;
        _confirmation.Title = request.Label; _confirmation.OkButtonText = request.Label; _confirmation.DialogText = request.Confirm;
        ModalChanged?.Invoke(true); _confirmation.PopupCentered(new(Math.Min(560, (int)GetViewport().GetVisibleRect().Size.X - 44), 280));
    }
    private void Dispatch(WorldEncounterActionDisplay request) { CancelConfirmation(); SetOpen(false); ActionRequested?.Invoke(request.Action, request.Id); }
    private void CancelConfirmation()
    {
        bool pending = _pending is not null || _confirmation is { Visible: true }; _pending = null; _pendingRevision = -1; _confirmation?.Hide();
        if (pending) ModalChanged?.Invoke(false);
    }
    private void UpdateModal()
    {
        if (_panel is null || _backdrop is null) return;
        bool open = IsVisibleInTree() && IsOpen; _backdrop.Visible = open;
        if (open && _oldSibling < 0) { _oldSibling = GetIndex(); GetParent().MoveChild(this, GetParent().GetChildCount() - 1); }
        else if (!open && _oldSibling >= 0) { GetParent().MoveChild(this, Math.Min(_oldSibling, GetParent().GetChildCount() - 1)); _oldSibling = -1; }
        if (_pauseHeld == open) return; _pauseHeld = open; _sandbox?.SetModalPaused("world-encounter", open);
        if (!open) CancelConfirmation();
    }
    private void UpdateLayout()
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        var size = new Vector2(Math.Min(980, viewport.X - 32), Math.Min(690, viewport.Y - 32));
        if (_panel.Size != size) { _panel.Size = size; _panel.QueueSort(); _column.QueueSort(); }
        _panel.Position = (viewport - size) / 2;
        var content = size - new Vector2(32, 32);
        if (_column.Size != content) { _column.Size = content; _column.QueueSort(); }
        if (_scroll.Size.X != content.X || _close.Size.X != content.X)
        {
            _column.Notification((int)Container.NotificationSortChildren);

        }
    }
    private static bool SamePresentation(WorldEncounterDisplay a, WorldEncounterDisplay b)
    {
        if (a.SelectedId != b.SelectedId || a.Heading != b.Heading || a.Narrative != b.Narrative || a.Notice != b.Notice ||
            !a.Actions.SequenceEqual(b.Actions) || a.Entries.Length != b.Entries.Length) return false;
        for (int i = 0; i < a.Entries.Length; i++)
        {
            var x = a.Entries[i]; var y = b.Entries[i];
            if (x.Id != y.Id || x.Name != y.Name || x.Region != y.Region || x.Status != y.Status || x.Summary != y.Summary ||
                x.Reward != y.Reward || !x.Discoveries.SequenceEqual(y.Discoveries)) return false;
        }
        return true;
    }
    private static void Clear(Node node) { foreach (var child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }
    private static string SafeName(string value) => new(value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    private static VBoxContainer Stack(int gap) { var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", gap); return box; }
    private static Label Text(string text, int size)
    { var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore }; label.AddThemeFontSizeOverride("font_size", size); return label; }
    private static Button Button(string text, string name, Action action)
    { var button = new Button { Name = name, Text = text, CustomMinimumSize = new(0, 38), SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart }; button.AddThemeFontSizeOverride("font_size", 14); button.Pressed += action; return button; }
}
