using Godot;

namespace Ashenwake.Client;

public sealed record RoamingChampionEntryDisplay(string Id, string Name, string Region, string Status,
    string Summary, string Counterplay, string Reward);
public sealed record RoamingChampionActionDisplay(string Action, string Id, string Label, string Hint = "",
    bool Enabled = true, string Confirm = "");
public sealed record RoamingChampionDisplay(RoamingChampionEntryDisplay[] Entries, string SelectedId,
    string Heading, string Narrative, RoamingChampionActionDisplay[] Actions, long Revision, string Notice = "", string LockedSelectionId = "");

/// <summary>Optional encounters, observed threats and explicit challenges. All progression comes from the authoritative view.</summary>
public partial class RoamingChampionPanel : Control
{
    public event Action<string, string>? ActionRequested;
    public event Action<string>? SelectionRequested, MenuRequested;
    public event Action<bool>? VisibilityChangedByPlayer, ModalChanged;
    private RoamingChampionDisplay? _view;
    private PanelContainer _panel = null!;
    private ColorRect _backdrop = null!;
    private VBoxContainer _column = null!, _details = null!;
    private HBoxContainer _entries = null!;
    private ScrollContainer _scroll = null!;
    private Label _summary = null!, _notice = null!;
    private Button _close = null!;
    private ConfirmationDialog _confirmation = null!;
    private RoamingChampionActionDisplay? _pending;
    private long _pendingRevision = -1;
    private Sandbox? _sandbox;
    private bool _pauseHeld;
    private int _oldSibling = -1, _layoutFrames;
    public bool IsOpen => _panel is { Visible: true };
    public string SelectedEntry => _view?.SelectedId ?? "";

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        for (Node? node = GetParent(); node is not null; node = node.GetParent())
            if (node is Sandbox sandbox) { _sandbox = sandbox; break; }
        _backdrop = new ColorRect { Color = new(0, 0, 0, .74f), MouseFilter = MouseFilterEnum.Stop, MouseForcePassScrollEvents = false };
        AddChild(_backdrop); _backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _panel = new PanelContainer { Name = "RoamingChampionPanel", MouseForcePassScrollEvents = false };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new("201d24"),
            BorderColor = new("ae8a68"),
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
        var title = Text("DANGEROUS COMPANY", 23); title.AddThemeColorOverride("font_color", new("decba0")); _column.AddChild(title);
        _summary = Text("Your regional sightings", 13); _summary.Name = "ChampionJournalSummary"; _column.AddChild(_summary);
        _entries = new HBoxContainer { Name = "ChampionJournalEntries" }; _entries.AddThemeConstantOverride("separation", 8); _column.AddChild(_entries);
        _scroll = new ScrollContainer
        {
            Name = "ChampionDetailsScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _column.AddChild(_scroll); _details = Stack(10); _details.SizeFlagsHorizontal = SizeFlags.ExpandFill; _scroll.AddChild(_details);
        _notice = Text("", 13); _notice.Name = "ChampionNotice"; _notice.MaxLinesVisible = 3; _column.AddChild(_notice);
        _close = Button("Close [Esc]", "ChampionClose", () => SetOpen(false)); _column.AddChild(_close);
        _confirmation = new ConfirmationDialog { Name = "ChampionConfirmation", DialogAutowrap = true, CancelButtonText = "Keep inspecting" };
        AddChild(_confirmation); _confirmation.Canceled += CancelConfirmation;
        _confirmation.Confirmed += () =>
        {
            var pending = _pending; long revision = _pendingRevision; CancelConfirmation();
            if (pending is not null && revision == _view?.Revision && IsOpen && IsVisibleInTree() && Available(pending)) Dispatch(pending);
        };
        _panel.Visible = _backdrop.Visible = false;
        VisibilityChanged += UpdateModal; _panel.VisibilityChanged += UpdateModal;
        GetViewport().SizeChanged += RequestLayout; SetProcess(true); RequestLayout();
    }
    public void SetView(RoamingChampionDisplay view)
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
        if (open) { Rebuild(); RequestLayout(); _close.GrabFocus(); }
        UpdateModal(); VisibilityChangedByPlayer?.Invoke(open);
    }
    public void SelectEntry(string id)
    {
        if (_view is not { } view || !view.Entries.Any(e => e.Id == id) || view.LockedSelectionId.Length > 0 && view.LockedSelectionId != id) return;
        CancelConfirmation(); _scroll.ScrollVertical = 0; SelectionRequested?.Invoke(id);
    }
    public void SessionRestored() { CancelConfirmation(); _view = null; SetOpen(false); }
    public void Notice(string text) { _notice.Text = text; _notice.TooltipText = text; _notice.Visible = text.Length > 0; }
    public override void _Process(double delta) { if (IsOpen && _layoutFrames > 0) { _layoutFrames--; UpdateLayout(); } }
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
        GetViewport().SizeChanged -= RequestLayout; CancelConfirmation();
        if (!_pauseHeld) return;
        _pauseHeld = false; var sandbox = _sandbox;
        Callable.From(() =>
        {
            if (sandbox is not null && GodotObject.IsInstanceValid(sandbox) && sandbox.IsInsideTree() && !sandbox.IsQueuedForDeletion())
                sandbox.SetModalPaused("roaming-champion", false);
        }).CallDeferred();
    }
    private void Rebuild()
    {
        if (_view is null || !IsOpen) return;
        string focus = GetViewport().GuiGetFocusOwner()?.Name.ToString() ?? "";
        Clear(_entries); Clear(_details); Notice(_view.Notice);
        _summary.Text = _view.Entries.Length == 0 ? "No champions sighted. Explore secured paths for signs of wandering threats." : $"{_view.Entries.Length} recorded {(_view.Entries.Length == 1 ? "sighting" : "sightings")} · optional encounters";
        if (_view.LockedSelectionId.Length > 0 && _view.Entries.Length > 1) _summary.Text += " · Return to the region to inspect other sightings.";
        _entries.Visible = _view.Entries.Length > 0;
        foreach (var entry in _view.Entries)
        {
            string id = entry.Id;
            var button = Button(entry.Name + "\n" + entry.Status, "ChampionEntry_" + SafeName(id), () => SelectEntry(id));
            button.CustomMinimumSize = new(0, 68); button.ToggleMode = true; button.SetPressedNoSignal(id == _view.SelectedId);
            button.Disabled = _view.LockedSelectionId.Length > 0 && _view.LockedSelectionId != id;
            button.TooltipText = button.Disabled ? "Return to the region before inspecting another champion." : entry.Region; _entries.AddChild(button);
        }
        var selected = _view.Entries.FirstOrDefault(e => e.Id == _view.SelectedId);
        var heading = Text(_view.Heading, 22); heading.Name = "ChampionHeading"; heading.AddThemeColorOverride("font_color", new("e6d7b7")); _details.AddChild(heading);
        if (selected is not null) _details.AddChild(Text(selected.Region + " · " + selected.Status, 13));
        var narrative = Text(_view.Narrative, 16); narrative.Name = "ChampionNarrative"; _details.AddChild(narrative);
        if (selected is not null)
        {
            if (selected.Summary.Length > 0 && selected.Summary != _view.Narrative) _details.AddChild(Text(selected.Summary, 14));
            if (selected.Counterplay.Length > 0)
            {
                _details.AddChild(new HSeparator()); _details.AddChild(Text("READ THE THREAT", 12));
                var counterplay = Text(selected.Counterplay, 15); counterplay.Name = "ChampionCounterplay"; _details.AddChild(counterplay);
            }
            if (selected.Reward.Length > 0)
            {
                var reward = Text("CHAMPION REWARD · " + selected.Reward, 14); reward.Name = "ChampionReward";
                reward.AddThemeColorOverride("font_color", new("cbb889")); _details.AddChild(reward);
            }
        }
        if (_view.Actions.Length > 0) _details.AddChild(new HSeparator());
        foreach (var option in _view.Actions)
        {
            var choice = option;
            var button = Button(choice.Label, "ChampionAction_" + SafeName(choice.Action) + "_" + SafeName(choice.Id), () => Request(choice));
            button.Disabled = !choice.Enabled; button.TooltipText = choice.Hint; _details.AddChild(button);
            if (choice.Hint.Length > 0) _details.AddChild(Text(choice.Hint, 12));
        }
        RequestLayout();
        if (focus.Length > 0 && FindChild(focus, true, false) is Control control && control.IsVisibleInTree()) control.GrabFocus();
    }
    private bool Available(RoamingChampionActionDisplay request) => _view?.Actions.Any(a => a == request && a.Enabled) == true;
    private void Request(RoamingChampionActionDisplay request)
    {
        if (!IsOpen || !IsVisibleInTree() || !Available(request)) return;
        CancelConfirmation();
        if (request.Confirm.Length == 0) { Dispatch(request); return; }
        _pending = request; _pendingRevision = _view!.Revision;
        _confirmation.Title = request.Label; _confirmation.OkButtonText = request.Label; _confirmation.DialogText = request.Confirm;
        ModalChanged?.Invoke(true); _confirmation.PopupCentered(new(Math.Min(560, (int)GetViewport().GetVisibleRect().Size.X - 44), 280));
    }
    private void Dispatch(RoamingChampionActionDisplay request) { CancelConfirmation(); SetOpen(false); ActionRequested?.Invoke(request.Action, request.Id); }
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
        if (_pauseHeld == open) return; _pauseHeld = open; _sandbox?.SetModalPaused("roaming-champion", open);
        if (!open) CancelConfirmation();
    }
    // Wrapped controls need several container passes after content or viewport changes.
    private void RequestLayout() { _layoutFrames = 4; UpdateLayout(); }
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
            _entries.Notification((int)Container.NotificationSortChildren);
        }
    }
    private static bool SamePresentation(RoamingChampionDisplay a, RoamingChampionDisplay b)
    {
        if (a.LockedSelectionId != b.LockedSelectionId || a.SelectedId != b.SelectedId || a.Heading != b.Heading || a.Narrative != b.Narrative || a.Notice != b.Notice ||
            !a.Actions.SequenceEqual(b.Actions) || a.Entries.Length != b.Entries.Length) return false;
        for (int i = 0; i < a.Entries.Length; i++)
        {
            var x = a.Entries[i]; var y = b.Entries[i];
            if (x.Id != y.Id || x.Name != y.Name || x.Region != y.Region || x.Status != y.Status || x.Summary != y.Summary ||
                x.Counterplay != y.Counterplay || x.Reward != y.Reward) return false;
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
