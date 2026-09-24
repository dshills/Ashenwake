using Godot;

namespace Ashenwake.Client;

public sealed record PetEntryDisplay(string Id, string Species, string Name, string Description, string SourceHint,
    bool Known, bool Rescued, bool Selected, string AppearanceId, string[] AppearanceIds, string[] AppearanceNames);
public sealed record PetDisplay(PetEntryDisplay[] Entries, bool AutoGather, string Notice = "");

/// <summary>Companion presentation. Rescue, ownership and every requested change are validated by the owner.</summary>
public partial class PetPanel : Control
{
    public event Action<string>? SelectRequested;
    public event Action? DismissRequested;
    public event Action<string, string>? RenameRequested;
    public event Action<string, string>? AppearanceRequested;
    public event Action<bool>? AutoGatherRequested;
    public event Action<string>? MenuRequested;
    private PetDisplay? _view;
    private PanelContainer _panel = null!;
    private ColorRect _backdrop = null!;
    private VBoxContainer _catalog = null!, _details = null!;
    private ScrollContainer _scroll = null!;
    private PetPreview _preview = null!;
    private Control _previewFrame = null!;
    private Label _summary = null!, _notice = null!, _unknown = null!;
    private LineEdit? _name;
    private CheckBox _gather = null!;
    private Button _close = null!;
    private string _selected = "";
    private Sandbox? _sandbox;
    private int _oldSibling = -1;
    private Vector2 _layoutViewport = new(-1, -1);
    public bool IsOpen => _panel is { Visible: true };
    public string SelectedEntry => _selected;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        for (Node? parent = GetParent(); parent is not null; parent = parent.GetParent())
            if (parent is Sandbox sandbox) { _sandbox = sandbox; break; }
        _backdrop = new ColorRect { Color = new(0, 0, 0, .76f), MouseFilter = MouseFilterEnum.Stop, MouseForcePassScrollEvents = false };
        AddChild(_backdrop); _backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _panel = new PanelContainer { Name = "TravelCompanions", MouseForcePassScrollEvents = false };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new("101d26"),
            BorderColor = new("ba9b63"),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 16,
            ContentMarginRight = 16,
            ContentMarginTop = 12,
            ContentMarginBottom = 12
        });
        AddChild(_panel);
        var column = Stack(); _panel.AddChild(column);
        column.AddChild(Text("COMPANIONS OF THE ASHEN ROAD", 22));
        _summary = Text("", 13); _summary.Name = "PetSummary"; column.AddChild(_summary);
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 18); column.AddChild(body);
        // A plain, bounded frame isolates the preview's minimum size from container sorting.
        _previewFrame = new Control { Name = "PetPreviewFrame", CustomMinimumSize = new(270, 100), SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true }; body.AddChild(_previewFrame);
        _preview = new PetPreview(); _previewFrame.AddChild(_preview); _preview.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _unknown = Text("?\n\nA FRIEND STILL WAITING\n\nExplore Edrath to discover a companion.", 16);
        _unknown.Name = "PetUnknownPreview"; _unknown.HorizontalAlignment = HorizontalAlignment.Center; _unknown.VerticalAlignment = VerticalAlignment.Center;
        _previewFrame.AddChild(_unknown); _unknown.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _scroll = new ScrollContainer { Name = "PetDetailsScroll", SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.AddChild(_scroll);
        var right = Stack(9); right.SizeFlagsHorizontal = SizeFlags.ExpandFill; _scroll.AddChild(right);
        _catalog = Stack(5); right.AddChild(_catalog); right.AddChild(new HSeparator());
        _details = Stack(9); _details.Name = "PetDetails"; right.AddChild(_details);
        _gather = new CheckBox { Name = "PetGather", Text = "Gather nearby materials", CustomMinimumSize = new(0, 32) };
        _gather.AddThemeFontSizeOverride("font_size", 13); column.AddChild(_gather);
        _gather.Toggled += enabled => AutoGatherRequested?.Invoke(enabled);
        column.AddChild(Text("Companions travel beside you without fighting. Material gathering is optional.", 12));
        _notice = Text("", 12); _notice.Name = "PetNotice"; _notice.MaxLinesVisible = 2; column.AddChild(_notice);
        _close = Button("Close [Esc]", "PetClose", () => SetOpen(false)); column.AddChild(_close);
        SetOpen(false);
    }

    public void SetView(PetDisplay view)
    {
        bool changed = _view is null || _view.AutoGather != view.AutoGather || _view.Notice != view.Notice ||
            _view.Entries.Length != view.Entries.Length || !_view.Entries.Zip(view.Entries).All(pair => SameEntry(pair.First, pair.Second));
        string? draft = _name is not null && IsInstanceValid(_name) && _view?.Entries.FirstOrDefault(e => e.Id == _selected)?.Name == view.Entries.FirstOrDefault(e => e.Id == _selected)?.Name ? _name.Text : null;
        _view = view;
        if (!view.Entries.Any(e => e.Id == _selected)) _selected = "";
        if (IsOpen && changed) { Rebuild(); if (draft is not null && _name is not null) _name.Text = draft; }
    }
    public void SetOpen(bool open)
    {
        if (_panel is null) return;
        _panel.Visible = _backdrop.Visible = open; _sandbox?.SetModalPaused("pets", open);
        if (open)
        {
            if (_oldSibling < 0) { _oldSibling = GetIndex(); GetParent().MoveChild(this, GetParent().GetChildCount() - 1); }
            _layoutViewport = new(-1, -1); LayoutPanel(); Rebuild(); _close.GrabFocus();
        }
        else if (_oldSibling >= 0) { GetParent().MoveChild(this, Math.Min(_oldSibling, GetParent().GetChildCount() - 1)); _oldSibling = -1; }
    }
    public void SelectEntry(string id)
    {
        if (_view?.Entries.Any(e => e.Id == id) != true) return;
        _selected = id; _scroll.ScrollVertical = 0; Rebuild();
    }
    public void ResetSelection() { _selected = ""; SetOpen(false); }
    public override void _Process(double delta) { if (IsOpen) LayoutPanel(); }
    public override void _Input(InputEvent input)
    {
        if (!IsOpen || input is not (InputEventKey or InputEventJoypadButton)) return;
        if (input.IsActionPressed("ui_cancel")) { SetOpen(false); GetViewport().SetInputAsHandled(); return; }
        // Names use plain LineEdit/Label text, with no markup interpretation or shortcut interception.
        if (_name?.HasFocus() == true) return;
        string? menu = new[] { "aw_inventory", "aw_character", "aw_journey", "aw_endgame", "aw_experiment" }.FirstOrDefault(a => input.IsActionPressed(a));
        if (menu is not null) { MenuRequested?.Invoke(menu); GetViewport().SetInputAsHandled(); return; }
        if (!new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev", "aw_save", "aw_load" }.Any(a => input.IsAction(a))) GetViewport().SetInputAsHandled();
    }
    public override void _ExitTree() => _sandbox?.SetModalPaused("pets", false);

    private void LayoutPanel()
    {
        Vector2 viewport = GetViewportRect().Size;
        Vector2 size = new(Math.Min(980, viewport.X - 32), Math.Min(730, viewport.Y - 32));
        if (viewport != _layoutViewport)
        {
            _layoutViewport = viewport;
            _previewFrame.CustomMinimumSize = new(Math.Clamp(size.X * .4f, 270, 380), 100);
            _preview.SetCompact(size.Y < 650, Math.Max(180, size.Y - 200));
        }
        // Reassert the bounds after deferred container sorting as well as window resizing.
        if (_panel.Size != size) _panel.Size = size;
        Vector2 position = (viewport - size) / 2;
        if (_panel.Position != position) _panel.Position = position;
    }
    private void Rebuild()
    {
        if (_view is null || !IsOpen) return;
        if (!_view.Entries.Any(e => e.Id == _selected)) _selected = _view.Entries.FirstOrDefault(e => e.Selected)?.Id ?? _view.Entries.FirstOrDefault(e => e.Rescued)?.Id ?? _view.Entries.FirstOrDefault()?.Id ?? "";
        _summary.Text = $"{_view.Entries.Count(e => e.Rescued)} / {_view.Entries.Length} rescued · " + (_view.Entries.FirstOrDefault(e => e.Selected && e.Known && e.Rescued) is { } active ? active.Name + " is traveling with you" : "No traveling companion");
        _notice.Text = _view.Notice; _notice.Visible = _view.Notice.Length > 0;
        _gather.SetPressedNoSignal(_view.AutoGather); _gather.Disabled = !_view.Entries.Any(e => e.Rescued);
        Clear(_catalog); Clear(_details); _name = null;
        foreach (var entry in _view.Entries)
        {
            string id = entry.Id;
            var button = Button(entry.Known ? entry.Species + "\n" + (entry.Rescued ? entry.Name + (entry.Selected ? " · Traveling" : " · Rescued") : "Awaiting rescue") : "?  Undiscovered companion", "PetCatalog_" + SafeId(id), () => SelectEntry(id));
            button.SetMeta("pet_id", id); button.CustomMinimumSize = new(0, 52); button.ToggleMode = true; button.SetPressedNoSignal(id == _selected); _catalog.AddChild(button);
        }
        var selected = _view.Entries.FirstOrDefault(e => e.Id == _selected);
        bool known = selected?.Known == true;
        _preview.Visible = known; _unknown.Visible = !known;
        _preview.SetPet(known ? selected!.Id : "", known ? selected!.AppearanceId : "");
        if (!known)
        {
            _details.AddChild(Text("An undiscovered companion", 22));
            _details.AddChild(Text("Some creatures need a friend. Explore the world to learn where one may be waiting.", 15)); return;
        }
        _details.AddChild(Text(selected!.Rescued ? selected.Name : selected.Species, 23));
        _details.AddChild(Text(selected.Species + (selected.Rescued ? " · RESCUED" : " · AWAITING RESCUE"), 13));
        _details.AddChild(Text(selected.Description, 15));
        if (selected.SourceHint.Length > 0) _details.AddChild(Text(selected.SourceHint, 13));
        if (!selected.Rescued) return;
        _details.AddChild(new HSeparator()); _details.AddChild(Text("A NAME OF THEIR OWN", 14));
        _name = new LineEdit { Name = "PetName", Text = selected.Name, MaxLength = 24, PlaceholderText = "Companion name", CustomMinimumSize = new(0, 34) };
        _name.AddThemeFontSizeOverride("font_size", 14); _details.AddChild(_name);
        string petId = selected.Id;
        var edit = _name;
        _details.AddChild(Button("Save name", "PetRename", () => RenameRequested?.Invoke(petId, edit.Text)));
        edit.TextSubmitted += name => RenameRequested?.Invoke(petId, name);
        _details.AddChild(Text("APPEARANCE", 14));
        for (int i = 0; i < Math.Min(selected.AppearanceIds.Length, selected.AppearanceNames.Length); i++)
        {
            string appearance = selected.AppearanceIds[i];
            var button = Button(selected.AppearanceNames[i], "PetAppearance_" + SafeId(appearance), () => AppearanceRequested?.Invoke(petId, appearance));
            button.ToggleMode = true; button.SetPressedNoSignal(selected.AppearanceId == appearance); _details.AddChild(button);
        }
        var select = Button(selected.Selected ? "Traveling with you" : "Select companion", "PetSelect", () => SelectRequested?.Invoke(petId)); select.Disabled = selected.Selected; _details.AddChild(select);
        if (_view.Entries.Any(e => e.Selected && e.Rescued)) _details.AddChild(Button("Send companion home", "PetDismiss", () => DismissRequested?.Invoke()));
    }
    private static bool SameEntry(PetEntryDisplay a, PetEntryDisplay b) => a.Id == b.Id && a.Species == b.Species && a.Name == b.Name && a.Description == b.Description && a.SourceHint == b.SourceHint && a.Known == b.Known && a.Rescued == b.Rescued && a.Selected == b.Selected && a.AppearanceId == b.AppearanceId && a.AppearanceIds.SequenceEqual(b.AppearanceIds) && a.AppearanceNames.SequenceEqual(b.AppearanceNames);
    private static string SafeId(string id) => id.Replace('.', '_');
    private static void Clear(Node node) { foreach (var child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }
    private static VBoxContainer Stack(int separation = 8) { var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", separation); return stack; }
    private static Label Text(string text, int size) { var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart }; label.AddThemeFontSizeOverride("font_size", size); return label; }
    private static Button Button(string text, string name, Action action)
    { var button = new Button { Name = name, Text = text, CustomMinimumSize = new(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart }; button.AddThemeFontSizeOverride("font_size", 13); button.Pressed += action; return button; }
}
