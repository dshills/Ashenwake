using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public sealed record LegendaryCollectionCard(LegendaryCollectionEntry Entry, bool Collected, int Owned, bool Extracted,
    LegendaryCollectionSourceView[] Sources);
public sealed record LegendaryCollectionDisplay(LegendaryCollectionCard[] Cards, string TrackedItem,
    CharacterAppearance Appearance, string Notice = "");

/// <summary>Cosmetic inspection and navigation requests only; no item grants, travel or equipment changes.</summary>
public partial class LegendaryCollectionPanel : Control
{
    public event Action<string>? TrackRequested;
    public event Action<string>? MenuRequested;
    public event Action<string, LegendaryCollectionSourceKind>? SourceRequested;
    public event Action<bool>? VisibilityChangedByPlayer;
    private LegendaryCollectionDisplay? _view;
    private PanelContainer _panel = null!;
    private ColorRect _backdrop = null!;
    private GridContainer _grid = null!;
    private VBoxContainer _details = null!;
    private ScrollContainer _scroll = null!;
    private CharacterPreview _preview = null!;
    private Label _summary = null!, _notice = null!;
    private Button _track = null!, _close = null!;
    private readonly Dictionary<string, Button> _filters = [];
    private string _selected = "", _filter = "All";
    private Sandbox? _sandbox;
    private int _oldSibling = -1;
    private Vector2 _layoutViewport = new(-1, -1);
    public bool IsOpen => _panel is { Visible: true };
    public string SelectedItem => _selected;
    public string Filter => _filter;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        for (Node? parent = GetParent(); parent is not null; parent = parent.GetParent()) if (parent is Sandbox sandbox) { _sandbox = sandbox; break; }
        _backdrop = new ColorRect { Color = new(0, 0, 0, .7f), MouseFilter = MouseFilterEnum.Stop, MouseForcePassScrollEvents = false };
        AddChild(_backdrop); _backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _panel = new PanelContainer { Name = "LegendaryCollection", MouseForcePassScrollEvents = false }; AddChild(_panel);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new("101b24"),
            BorderColor = new("ac9465"),
            BorderWidthBottom = 1,
            BorderWidthTop = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 12,
            ContentMarginBottom = 12
        });
        var column = Stack(); _panel.AddChild(column);
        column.AddChild(Text("RELICS OF EDRATH", 22));
        _summary = Text("", 13); _summary.Name = "CollectionSummary"; column.AddChild(_summary);
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 18); column.AddChild(body);
        var left = Stack(); left.CustomMinimumSize = new(282, 0); body.AddChild(left);
        var filterRow = new HBoxContainer(); left.AddChild(filterRow);
        foreach (string filter in new[] { "All", "Collected", "Missing" })
        {
            var button = Button(filter, "CollectionFilter" + filter, () => SetFilter(filter)); button.ToggleMode = true;
            filterRow.AddChild(button); _filters.Add(filter, button);
        }
        var catalogScroll = new ScrollContainer { Name = "CollectionCatalog", CustomMinimumSize = new(282, 220), SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        left.AddChild(catalogScroll); _grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill }; catalogScroll.AddChild(_grid);
        _preview = new CharacterPreview(); left.AddChild(_preview); _preview.SetCompact(true, 235);
        _scroll = new ScrollContainer { Name = "CollectionDetailsScroll", SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.AddChild(_scroll); _details = Stack(); _details.SizeFlagsHorizontal = SizeFlags.ExpandFill; _scroll.AddChild(_details);
        _notice = Text("", 12); _notice.Name = "CollectionNotice"; _notice.MaxLinesVisible = 2; column.AddChild(_notice);
        var footer = new HBoxContainer(); column.AddChild(footer);
        _track = Button("Track this item", "CollectionTrack", () => { if (_view is not null && _selected.Length > 0) TrackRequested?.Invoke(_view.TrackedItem == _selected ? "" : _selected); }); footer.AddChild(_track);
        _close = Button("Close [Esc]", "CollectionClose", () => SetOpen(false)); footer.AddChild(_close);
        SetOpen(false);
    }

    public void SetView(LegendaryCollectionDisplay view)
    {
        _view = view;
        if (!view.Cards.Any(c => c.Entry.ItemId == _selected)) _selected = view.TrackedItem.Length > 0 ? view.TrackedItem : view.Cards.FirstOrDefault()?.Entry.ItemId ?? "";
        if (IsOpen) Rebuild();
    }
    public void SetOpen(bool open)
    {
        _panel.Visible = _backdrop.Visible = open;
        _sandbox?.SetModalPaused("legendary-collection", open);
        if (open)
        {
            if (_oldSibling < 0) { _oldSibling = GetIndex(); GetParent().MoveChild(this, GetParent().GetChildCount() - 1); }
            Rebuild(); _close.GrabFocus();
        }
        else if (_oldSibling >= 0) { GetParent().MoveChild(this, Math.Min(_oldSibling, GetParent().GetChildCount() - 1)); _oldSibling = -1; }
        VisibilityChangedByPlayer?.Invoke(open);
    }
    public void SelectItem(string id)
    {
        if (_view?.Cards.Any(c => c.Entry.ItemId == id) != true) return;
        _selected = id; _scroll.ScrollVertical = 0; Rebuild(); _track.GrabFocus();
    }
    public void SetFilter(string filter)
    {
        if (!_filters.ContainsKey(filter)) return;
        _filter = filter; Rebuild(); _filters[filter].GrabFocus();
    }
    public void ResetSelection() { _selected = ""; _filter = "All"; SetOpen(false); }
    public override void _Process(double delta)
    {
        if (!IsOpen) return;
        var viewport = GetViewportRect().Size;
        if (viewport == _layoutViewport) return;
        _layoutViewport = viewport;
        var size = new Vector2(Math.Min(1080, viewport.X - 32), Math.Min(730, viewport.Y - 32));
        _panel.Position = (viewport - size) / 2; _panel.Size = size;
        _preview.SetCompact(true, Math.Clamp(size.Y - 430, 190, 250));
    }
    public override void _Input(InputEvent input)
    {
        if (!IsOpen || input is not (InputEventKey or InputEventJoypadButton)) return;
        if (input.IsActionPressed("ui_cancel")) { SetOpen(false); GetViewport().SetInputAsHandled(); return; }
        string? menu = new[] { "aw_inventory", "aw_character", "aw_journey", "aw_endgame", "aw_experiment" }.FirstOrDefault(a => input.IsActionPressed(a));
        if (menu is not null) { MenuRequested?.Invoke(menu); GetViewport().SetInputAsHandled(); return; }
        if (!new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev", "aw_save", "aw_load" }.Any(a => input.IsAction(a))) GetViewport().SetInputAsHandled();
    }
    public override void _ExitTree() => _sandbox?.SetModalPaused("legendary-collection", false);

    private void Rebuild()
    {
        if (_view is null || !IsOpen) return;
        _summary.Text = $"{_view.Cards.Count(c => c.Collected)} / {_view.Cards.Length} discovered · {_view.Cards.Count(c => c.Extracted)} powers learned";
        _notice.Text = _view.Notice; _notice.Visible = _view.Notice.Length > 0;
        foreach (var filter in _filters) filter.Value.SetPressedNoSignal(filter.Key == _filter);
        Clear(_grid); Clear(_details);
        var cards = _view.Cards.Where(c => _filter == "All" || (_filter == "Collected" ? c.Collected : !c.Collected)).ToArray();
        if (!cards.Any(c => c.Entry.ItemId == _selected)) _selected = cards.FirstOrDefault()?.Entry.ItemId ?? "";
        foreach (var card in cards)
        {
            string id = card.Entry.ItemId, name = EquipmentNames.For(id);
            var button = Button("", "CollectionItem_" + id[5..], () => SelectItem(id));
            button.CustomMinimumSize = new(90, 92); button.ToggleMode = true; button.SetPressedNoSignal(id == _selected);
            button.TooltipText = name + (card.Collected ? " · Discovered" : " · Not yet collected"); _grid.AddChild(button);
            var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore }; button.AddChild(margin); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            margin.AddThemeConstantOverride("margin_left", 4); margin.AddThemeConstantOverride("margin_right", 4);
            var content = Stack(); content.MouseFilter = MouseFilterEnum.Ignore; margin.AddChild(content);
            var icon = new GearItemIcon { CustomMinimumSize = new(42, 36) }; icon.Configure(id, card.Entry.Slot, _view.Appearance.Discipline, ItemRarity.Legendary); content.AddChild(icon);
            var title = Text((card.Collected ? "✓ " : "") + name, 11); title.MaxLinesVisible = 3; title.HorizontalAlignment = HorizontalAlignment.Center; title.MouseFilter = MouseFilterEnum.Ignore; content.AddChild(title);
        }
        var selected = _view.Cards.FirstOrDefault(c => c.Entry.ItemId == _selected);
        _track.Disabled = selected is null;
        if (selected is null) { _details.AddChild(Text("No items in this filter yet.", 18)); _preview.Visible = false; return; }
        _preview.Visible = true;
        _details.AddChild(Text(EquipmentNames.For(_selected), 22));
        _details.AddChild(Text($"{selected.Entry.Slot} · {(selected.Collected ? "DISCOVERED" : "NOT YET COLLECTED")}\nOwned copies: {selected.Owned} · Power {(selected.Extracted ? "learned" : "not extracted")}", 13));
        _details.AddChild(Text(EquipmentDetails.Power(selected.Entry.PowerId), 14));
        _details.AddChild(Text(selected.Collected ? EquipmentDetails.Lore(_selected) : "Its history has not yet been discovered. Collect this relic to read its lore.", 13));
        _details.AddChild(new HSeparator()); _details.AddChild(Text("WHERE TO FIND IT", 16));
        foreach (var source in selected.Sources)
        {
            _details.AddChild(Text(source.Label + (source.Repeatable ? " · Repeatable" : ""), 14));
            _details.AddChild(Text(source.Requirement, 12));
            var kind = source.Kind;
            string label = kind == LegendaryCollectionSourceKind.RoamingChampion ? "View champion sighting" : kind == LegendaryCollectionSourceKind.SecretChamber ? "View discovery" : kind == LegendaryCollectionSourceKind.Campaign ? "View Journey route" : kind == LegendaryCollectionSourceKind.Fracture ? "View Fractures" : "View God Hunt";
            var route = Button(label, "CollectionSource" + kind, () => SourceRequested?.Invoke(_selected, kind));
            route.Disabled = kind == LegendaryCollectionSourceKind.RoamingChampion && source.ChampionId.Length == 0 || kind == LegendaryCollectionSourceKind.GodHunt && source.HuntId.Length == 0 || kind == LegendaryCollectionSourceKind.SecretChamber && source.ChamberId.Length == 0;
            _details.AddChild(route); _details.AddChild(new HSeparator());
        }
        _details.AddChild(Text(selected.Entry.RoamingChampionId.Length > 0
            ? "Claim the champion’s signature treasure to own it. This character receives one copy; extraction consumes it and teaches its existing power. Previewing and tracking never change your equipment."
            : selected.Entry.SecretChamberId.Length > 0
            ? "Claim the chamber treasure to own it. This character receives one copy; extracting its power consumes the item. Previewing and tracking never change your equipment."
            : "Collect the ground drop to own it. Extract a spare copy at its specialist to learn its power. Previewing and tracking never change your equipment.", 12));
        _track.Text = _view.TrackedItem == _selected ? "Stop tracking this item" : "Track this item";
        _preview.SetAppearance(Preview(_view.Appearance, selected.Entry)); _preview.SetCaption("Preview only · " + EquipmentNames.For(_selected));
    }
    private static CharacterAppearance Preview(CharacterAppearance current, LegendaryCollectionEntry entry)
    {
        var item = new ItemAppearance(entry.ItemId, "Legendary");
        return entry.Slot switch
        {
            EquipmentSlot.MainHand => current with { MainHand = item },
            EquipmentSlot.OffHand => current with { OffHand = item },
            EquipmentSlot.Head => current with { Head = item },
            EquipmentSlot.Chest => current with { Chest = item },
            EquipmentSlot.Shoulders => current with { Shoulders = item },
            EquipmentSlot.Gloves => current with { Gloves = item },
            EquipmentSlot.Belt => current with { Belt = item },
            EquipmentSlot.Legs => current with { Legs = item },
            EquipmentSlot.Boots => current with { Boots = item },
            EquipmentSlot.Amulet => current with { Amulet = item },
            EquipmentSlot.Ring1 => current with { Ring1 = item },
            _ => current
        };
    }
    private static void Clear(Node node) { foreach (var child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }
    private static VBoxContainer Stack() { var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 8); return stack; }
    private static Label Text(string text, int size) { var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart }; label.AddThemeFontSizeOverride("font_size", size); return label; }
    private static Button Button(string text, string name, Action action)
    { var button = new Button { Name = name, Text = text, CustomMinimumSize = new(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart }; button.AddThemeFontSizeOverride("font_size", 13); button.Pressed += action; return button; }
}
