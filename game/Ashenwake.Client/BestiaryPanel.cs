using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public sealed record BestiaryRewardDisplay(string ItemId, string Name, string SourceLabel, LegendaryCollectionSourceKind SourceKind);
public sealed record BestiaryEntryDisplay(string Id, string Name, string Category, string Region, string Lore,
    string VisualId, string Role, bool Seen, int Kills, int EliteKills, string[] CombatFacts, BestiaryRewardDisplay[] Rewards);
public sealed record BestiaryDisplay(BestiaryEntryDisplay[] Entries, string Notice = "");

/// <summary>A discovery-gated, view-only journal. The owner resolves navigation; browsing never awards knowledge.</summary>
public partial class BestiaryPanel : Control
{
    public event Action<string, LegendaryCollectionSourceKind>? SourceRequested;
    public event Action<string>? MenuRequested;
    public event Action<bool>? VisibilityChangedByPlayer;
    private BestiaryDisplay? _view;
    private PanelContainer _panel = null!;
    private ColorRect _backdrop = null!;
    private VBoxContainer _catalog = null!, _details = null!;
    private ScrollContainer _detailsScroll = null!;
    private CreaturePreview _preview = null!;
    private Control _previewFrame = null!;
    private Label _summary = null!, _notice = null!, _unknown = null!;
    private LineEdit _search = null!;
    private Button _close = null!;
    private readonly Dictionary<string, Button> _filters = [];
    private string _selected = "", _filter = "All";
    private Sandbox? _sandbox;
    private int _oldSibling = -1;
    private Vector2 _layoutViewport = new(-1, -1);
    public bool IsOpen => _panel is { Visible: true };
    public string SelectedEntry => _selected;
    public string Filter => _filter;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        for (Node? parent = GetParent(); parent is not null; parent = parent.GetParent())
            if (parent is Sandbox sandbox) { _sandbox = sandbox; break; }
        _backdrop = new ColorRect { Color = new(0, 0, 0, .76f), MouseFilter = MouseFilterEnum.Stop, MouseForcePassScrollEvents = false };
        AddChild(_backdrop); _backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _panel = new PanelContainer { Name = "HuntersBestiary", MouseForcePassScrollEvents = false };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new("101b24"),
            BorderColor = new("ac9465"),
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
        column.AddChild(Text("THE HUNTER’S BESTIARY", 22));
        _summary = Text("", 13); _summary.Name = "BestiarySummary"; column.AddChild(_summary);
        var toolbar = new HBoxContainer(); column.AddChild(toolbar);
        foreach (string filter in new[] { "All", "Seen", "Defeated", "Families", "Bosses", "Champions" })
        {
            var button = Button(filter, "BestiaryFilter" + filter, () => SetFilter(filter));
            button.ToggleMode = true; toolbar.AddChild(button); _filters.Add(filter, button);
        }
        _search = new LineEdit { Name = "BestiarySearch", PlaceholderText = "Search discovered creatures or regions…", MaxLength = 80, CustomMinimumSize = new(0, 32) };
        _search.AddThemeFontSizeOverride("font_size", 13); column.AddChild(_search);
        _search.TextChanged += _ => Rebuild();
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 16); column.AddChild(body);
        var left = Stack(6); left.CustomMinimumSize = new(270, 0); body.AddChild(left);
        var listScroll = new ScrollContainer { Name = "BestiaryCatalog", CustomMinimumSize = new(270, 100), SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        left.AddChild(listScroll); _catalog = Stack(5); _catalog.SizeFlagsHorizontal = SizeFlags.ExpandFill; listScroll.AddChild(_catalog);
        // A bounded frame keeps the render surface's minimum size from expanding the modal.
        _previewFrame = new Control { Name = "BestiaryPreviewFrame", CustomMinimumSize = new(270, 240), ClipContents = true }; left.AddChild(_previewFrame);
        _preview = new CreaturePreview(); _previewFrame.AddChild(_preview); _preview.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); _preview.SetCompact(true, 240);
        _unknown = Text("?\n\nUNDISCOVERED\n\nEncounter this creature to reveal its form.", 16);
        _unknown.Name = "BestiaryUnknownPreview"; _unknown.HorizontalAlignment = HorizontalAlignment.Center;
        _unknown.VerticalAlignment = VerticalAlignment.Center; _previewFrame.AddChild(_unknown); _unknown.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _detailsScroll = new ScrollContainer { Name = "BestiaryDetailsScroll", SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.AddChild(_detailsScroll); _details = Stack(9); _details.Name = "BestiaryDetails"; _details.SizeFlagsHorizontal = SizeFlags.ExpandFill; _detailsScroll.AddChild(_details);
        _notice = Text("", 12); _notice.Name = "BestiaryNotice"; _notice.MaxLinesVisible = 2; column.AddChild(_notice);
        column.AddChild(Text("Discover through encounters. Defeat creatures to record their combat knowledge and known rewards.", 12));
        _close = Button("Close [Esc]", "BestiaryClose", () => SetOpen(false)); column.AddChild(_close);
        SetOpen(false);
    }

    public void SetView(BestiaryDisplay view)
    {
        _view = view;
        if (!view.Entries.Any(e => e.Id == _selected)) _selected = "";
        if (IsOpen) Rebuild();
    }
    public void SetOpen(bool open)
    {
        _panel.Visible = _backdrop.Visible = open; _sandbox?.SetModalPaused("bestiary", open);
        if (open)
        {
            if (_oldSibling < 0) { _oldSibling = GetIndex(); GetParent().MoveChild(this, GetParent().GetChildCount() - 1); }
            _layoutViewport = new(-1, -1); LayoutPanel(); Rebuild(); _close.GrabFocus();
        }
        else if (_oldSibling >= 0) { GetParent().MoveChild(this, Math.Min(_oldSibling, GetParent().GetChildCount() - 1)); _oldSibling = -1; }
        VisibilityChangedByPlayer?.Invoke(open);
    }
    public void SelectEntry(string id)
    {
        if (_view?.Entries.Any(e => e.Id == id) != true) return;
        _selected = id; _detailsScroll.ScrollVertical = 0; Rebuild();
    }
    public void SetFilter(string filter)
    {
        if (!_filters.ContainsKey(filter)) return;
        _filter = filter; Rebuild(); _filters[filter].GrabFocus();
    }
    public void ResetSelection()
    {
        _selected = ""; _filter = "All";
        if (_search is not null) _search.Text = "";
        SetOpen(false);
    }
    public override void _Process(double delta) { if (IsOpen) LayoutPanel(); }
    public override void _Input(InputEvent input)
    {
        if (!IsOpen || input is not (InputEventKey or InputEventJoypadButton)) return;
        if (input.IsActionPressed("ui_cancel")) { SetOpen(false); GetViewport().SetInputAsHandled(); return; }
        // Searching must accept letters that are also gameplay or menu shortcuts.
        if (_search.HasFocus()) return;
        string? menu = new[] { "aw_inventory", "aw_character", "aw_journey", "aw_endgame", "aw_experiment" }.FirstOrDefault(a => input.IsActionPressed(a));
        if (menu is not null) { MenuRequested?.Invoke(menu); GetViewport().SetInputAsHandled(); return; }
        if (!new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev", "aw_save", "aw_load" }.Any(a => input.IsAction(a))) GetViewport().SetInputAsHandled();
    }
    public override void _ExitTree() => _sandbox?.SetModalPaused("bestiary", false);

    private void LayoutPanel()
    {
        Vector2 viewport = GetViewportRect().Size;
        Vector2 size = new(Math.Min(1080, viewport.X - 32), Math.Min(730, viewport.Y - 32));
        if (viewport != _layoutViewport)
        {
            _layoutViewport = viewport;
            float previewHeight = Math.Clamp(size.Y - 440, 220, 290);
            _previewFrame.CustomMinimumSize = new(270, previewHeight); _preview.SetCompact(true, previewHeight);
        }
        // Wrapped catalog controls can temporarily expand their ancestors during a deferred sort.
        // Keep the scrolling modal bounded after that sort as well as after a window resize.
        if (_panel.Size != size) _panel.Size = size;
        Vector2 position = (viewport - size) / 2;
        if (_panel.Position != position) _panel.Position = position;
    }
    private void Rebuild()
    {
        if (_view is null || !IsOpen) return;
        _summary.Text = $"{_view.Entries.Count(e => e.Seen)} / {_view.Entries.Length} discovered · {_view.Entries.Count(e => e.Seen && e.Kills > 0)} defeated · {_view.Entries.Count(e => !e.Seen)} undiscovered";
        _notice.Text = _view.Notice; _notice.Visible = _view.Notice.Length > 0;
        foreach (var filter in _filters) filter.Value.SetPressedNoSignal(filter.Key == _filter);
        string query = _search.Text.Trim();
        var entries = _view.Entries.Where(e => (_filter switch
        {
            "Seen" => e.Seen,
            "Defeated" => e.Seen && e.Kills > 0,
            "Families" => e.Seen && !e.Category.Contains("boss", StringComparison.OrdinalIgnoreCase) && !e.Category.Contains("champion", StringComparison.OrdinalIgnoreCase),
            "Bosses" => e.Seen && e.Category.Contains("boss", StringComparison.OrdinalIgnoreCase),
            "Champions" => e.Seen && e.Category.Contains("champion", StringComparison.OrdinalIgnoreCase),
            _ => true
        }) && (query.Length == 0 || e.Seen && (e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Region.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Category.Contains(query, StringComparison.OrdinalIgnoreCase)))).ToArray();
        if (!entries.Any(e => e.Id == _selected)) _selected = entries.FirstOrDefault(e => e.Seen)?.Id ?? entries.FirstOrDefault()?.Id ?? "";
        Clear(_catalog); Clear(_details);
        foreach (var entry in entries)
        {
            string id = entry.Id;
            var button = Button(entry.Seen ? entry.Name + "\n" + entry.Category + (entry.Kills > 0 ? " · Defeated" : " · Seen") : "?  Undiscovered creature", "BestiaryEntry_" + id.Replace('.', '_'), () => SelectEntry(id));
            button.SetMeta("bestiary_id", id); button.CustomMinimumSize = new(0, 52); button.ToggleMode = true; button.SetPressedNoSignal(id == _selected);
            button.TooltipText = entry.Seen ? entry.Name + " · " + entry.Region : "Encounter this creature to reveal its entry."; _catalog.AddChild(button);
        }
        var selected = entries.FirstOrDefault(e => e.Id == _selected);
        bool seen = selected?.Seen == true;
        _preview.Visible = seen; _unknown.Visible = selected is not null && !seen;
        _preview.SetCreature(seen ? selected!.VisualId : "", seen ? selected!.Role : "");
        if (selected is null) { _details.AddChild(Text("No matching entries", 22)); _details.AddChild(Text("Try another filter or search for a discovered creature or region.", 14)); return; }
        if (!seen)
        {
            _details.AddChild(Text("Undiscovered creature", 22));
            _details.AddChild(Text("This page awaits a first encounter. Explore Edrath to learn the creature’s name, story and habitat.", 15));
            _details.AddChild(Text("Combat observations and rewards remain hidden until you defeat it.", 13)); return;
        }
        _details.AddChild(Text(selected.Name, 23));
        _details.AddChild(Text(selected.Category + " · " + (selected.Kills > 0 ? "DEFEATED" : "ENCOUNTERED"), 13));
        _details.AddChild(Text(selected.Region, 14));
        _details.AddChild(Text(selected.Lore, 15));
        _details.AddChild(new HSeparator()); _details.AddChild(Text("YOUR RECORD", 16));
        _details.AddChild(Text($"Defeats: {selected.Kills} · Elite defeats: {selected.EliteKills}", 14));
        _details.AddChild(new HSeparator()); _details.AddChild(Text("COMBAT KNOWLEDGE", 16));
        if (selected.Kills <= 0)
        {
            _details.AddChild(Text("Defeat this creature to record its attack tells, defenses, counterplay and known rewards.", 14)); return;
        }
        foreach (string fact in selected.CombatFacts) _details.AddChild(Text("• " + fact, 14));
        if (selected.CombatFacts.Length == 0) _details.AddChild(Text("No additional combat observations recorded.", 14));
        _details.AddChild(new HSeparator()); _details.AddChild(Text("KNOWN REWARDS & HUNTS", 16));
        if (selected.Rewards.Length == 0) _details.AddChild(Text("No signature equipment source is recorded for this creature.", 14));
        foreach (var reward in selected.Rewards)
        {
            _details.AddChild(Text(reward.Name, 15));
            _details.AddChild(Text(reward.SourceLabel, 13));
            _details.AddChild(Button("View source", "BestiaryReward_" + reward.ItemId.Replace('.', '_') + "_" + reward.SourceKind,
                () => SourceRequested?.Invoke(reward.ItemId, reward.SourceKind)));
        }
    }
    private static void Clear(Node node) { foreach (var child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }
    private static VBoxContainer Stack(int separation = 8) { var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", separation); return stack; }
    private static Label Text(string text, int size) { var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart }; label.AddThemeFontSizeOverride("font_size", size); return label; }
    private static Button Button(string text, string name, Action action)
    { var button = new Button { Name = name, Text = text, CustomMinimumSize = new(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart }; button.AddThemeFontSizeOverride("font_size", 13); button.Pressed += action; return button; }
}
