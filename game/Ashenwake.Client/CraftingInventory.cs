using System.Globalization;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Cached, read-only crafting inventory. Selecting or dragging never equips, removes or crafts an item.</summary>
public partial class CraftingInventory : VBoxContainer
{
    public event Action<long>? ItemSelected;
    public long SelectedItemId { get; private set; }
    public string DragOwner => GetInstanceId().ToString(CultureInfo.InvariantCulture);
    public long Epoch { get; private set; }

    private readonly Dictionary<long, GearDragCard> _cards = [];
    private Dictionary<long, PermanentItem> _owned = [];
    private Dictionary<string, ProductionItemDefinition> _definitions = new(StringComparer.Ordinal);
    private ProgressionSnapshot? _state;
    private ProgressionDefinition? _content;
    private GridContainer _grid = null!;
    private ScrollContainer _scroll = null!;
    private LineEdit _search = null!;
    private OptionButton _typeFilter = null!, _rarityFilter = null!, _sort = null!;
    private Label _heading = null!, _empty = null!;
    private string _signature = "";
    private bool _dirty, _projectionDirty, _refreshQueued;
    private static readonly string[] MenuActions = ["aw_inventory", "aw_character", "aw_journey", "aw_endgame", "aw_experiment", "aw_settings", "aw_save", "aw_load"];

    public override void _Ready()
    {
        Name = "CraftingInventory";
        CustomMinimumSize = CustomMinimumSize.Max(new Vector2(260, 300));
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;
        MouseForcePassScrollEvents = false;
        AddThemeConstantOverride("separation", 6);
        _heading = Caption("YOUR EQUIPMENT"); _heading.Name = "CraftInventoryHeading"; AddChild(_heading);
        AddChild(Caption("Click an item or drag it to the workbench."));
        BuildFilters();
        _scroll = new ScrollContainer
        {
            Name = "CraftInventoryScroll",
            CustomMinimumSize = new(0, 160),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            MouseFilter = MouseFilterEnum.Pass,
            MouseForcePassScrollEvents = false
        };
        AddChild(_scroll);
        _grid = new GridContainer { Name = "CraftInventoryGrid", Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
        _grid.AddThemeConstantOverride("h_separation", 5); _grid.AddThemeConstantOverride("v_separation", 5);
        _scroll.AddChild(_grid);
        _empty = Caption("No items available."); _empty.Name = "CraftEmptyState"; AddChild(_empty);
        VisibilityChanged += () =>
        {
            if (IsVisibleInTree()) QueueRefresh();
            else { CancelDrag(); _search.ReleaseFocus(); }
        };
        QueueRefresh();
    }

    public void SetView(ProgressionSnapshot state, ProgressionDefinition content)
    {
        // Item values, not just instance IDs, invalidate a drag after tempering, engraving or evolution.
        string signature = JsonData.Hash(new
        {
            state.Character.CharacterId,
            state.Character.ContentHash,
            state.Character.Discipline,
            state.Character.Items,
            state.Character.Equipment
        });
        if (!ReferenceEquals(_content, content))
        {
            _content = content;
            _definitions = content.Items.ToDictionary(item => item.Id, StringComparer.Ordinal);
        }
        _state = state;
        if (_signature == signature) return;
        _signature = signature;
        _owned = state.Character.Items.ToDictionary(item => item.Id);
        if (!_owned.ContainsKey(SelectedItemId)) SelectedItemId = 0;
        Epoch++;
        _dirty = true;
        QueueRefresh();
    }

    public void SelectItem(long id)
    {
        if (!_owned.ContainsKey(id)) return;
        SelectedItemId = id;
        RefreshSelection();
        ItemSelected?.Invoke(id);
    }

    public bool TryReadDrag(Variant data, out long id)
    {
        id = 0;
        if (!IsInsideTree() || IsQueuedForDeletion() || !IsVisibleInTree() || !Owns(data)) return false;
        var values = data.AsGodotDictionary();
        if (!values.TryGetValue("epoch", out var epoch) || epoch.VariantType != Variant.Type.Int || epoch.AsInt64() != Epoch ||
            !values.TryGetValue("item", out var item) || item.VariantType != Variant.Type.Int) return false;
        long candidate = item.AsInt64();
        if (!_owned.ContainsKey(candidate)) return false;
        id = candidate;
        return true;
    }

    public void CancelDrag()
    {
        Epoch++;
        if (IsInsideTree() && Owns(GetViewport().GuiGetDragData())) GetViewport().GuiCancelDrag();
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) => TryReadDrag(data, out _);

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (TryReadDrag(data, out long id)) SelectItem(id);
    }

    public override void _Input(InputEvent input)
    {
        if (!IsInsideTree() || !Owns(GetViewport().GuiGetDragData())) return;
        if (input.IsActionPressed("ui_cancel")) { CancelDrag(); GetViewport().SetInputAsHandled(); }
        else if (input is InputEventKey or InputEventJoypadButton)
        {
            if (MenuActions.Any(action => InputMap.HasAction(action) && input.IsActionPressed(action)))
            {
                // Native drag cancellation consumes its triggering event. Replay this menu press
                // once after dispatch so the existing menu or restore owner receives it normally.
                var viewport = GetViewport();
                var menuPress = (InputEvent)input.Duplicate();
                viewport.SetInputAsHandled(); CancelDrag();
                long canceledEpoch = Epoch;
                Callable.From(() =>
                {
                    if (GodotObject.IsInstanceValid(this) && !IsQueuedForDeletion() && IsInsideTree() && IsVisibleInTree() &&
                        Epoch == canceledEpoch && GodotObject.IsInstanceValid(viewport)) viewport.PushInput(menuPress, true);
                }).CallDeferred();
            }
            else GetViewport().SetInputAsHandled();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut || what == NotificationWMWindowFocusOut) CancelDrag();
        else if (what == NotificationDragEnd) QueueRefresh();
    }

    public override void _ExitTree() => CancelDrag();

    private bool Owns(Variant data) => data.VariantType == Variant.Type.Dictionary &&
        data.AsGodotDictionary().TryGetValue("craftOwner", out var owner) &&
        owner.VariantType == Variant.Type.String && owner.AsString() == DragOwner;

    private Variant DragData(long id)
    {
        if (!IsVisibleInTree() || !_owned.ContainsKey(id)) return default;
        return new Godot.Collections.Dictionary { ["craftOwner"] = DragOwner, ["epoch"] = Epoch, ["item"] = id };
    }

    private void BuildFilters()
    {
        var searchRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; AddChild(searchRow);
        _search = new LineEdit { Name = "CraftSearch", PlaceholderText = "Search items", MaxLength = 80, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _search.AddThemeFontSizeOverride("font_size", 12); searchRow.AddChild(_search);
        var reset = new Button { Name = "CraftResetFilters", Text = "Clear", TooltipText = "Show all items and restore type sorting." };
        reset.AddThemeFontSizeOverride("font_size", 11); searchRow.AddChild(reset);
        var filterRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; AddChild(filterRow);
        _typeFilter = Filter("CraftTypeFilter", "Filter by equipment type", filterRow, ["All types", "Weapons", "Armor", "Accessories"]);
        _rarityFilter = Filter("CraftRarityFilter", "Filter by rarity", filterRow, ["All rarities", .. Enum.GetNames<ItemRarity>()]);
        // Keep sorting on its own row so all filter values remain readable at the 260px minimum.
        var sortRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; AddChild(sortRow);
        var sortLabel = Caption("SORT BY"); sortLabel.CustomMinimumSize = new(65, 0); sortRow.AddChild(sortLabel);
        _sort = Filter("CraftSort", "Sort owned equipment", sortRow, ["Type", "Rarity", "Name"]);
        _search.TextChanged += _ => ProjectionChanged();
        foreach (var selector in new[] { _typeFilter, _rarityFilter, _sort }) selector.ItemSelected += _ => ProjectionChanged();
        reset.Pressed += () =>
        {
            _typeFilter.Select(0); _rarityFilter.Select(0); _sort.Select(0);
            _search.Text = ""; ProjectionChanged();
        };
    }

    private static OptionButton Filter(string name, string tooltip, HBoxContainer parent, string[] choices)
    {
        var filter = new OptionButton { Name = name, TooltipText = tooltip, SizeFlagsHorizontal = SizeFlags.ExpandFill, FitToLongestItem = false };
        filter.AddThemeFontSizeOverride("font_size", 11);
        for (int i = 0; i < choices.Length; i++) filter.AddItem(choices[i], i);
        parent.AddChild(filter);
        return filter;
    }

    private void ProjectionChanged()
    {
        CancelDrag();
        _projectionDirty = true;
        _scroll.ScrollVertical = 0;
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (_refreshQueued || !IsInsideTree() || _grid is null || IsQueuedForDeletion()) return;
        _refreshQueued = true;
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(this)) return;
            _refreshQueued = false;
            if (!IsInsideTree() || IsQueuedForDeletion() || GetViewport().GuiIsDragging()) return;
            RefreshCards();
        }).CallDeferred();
    }

    private void RefreshCards()
    {
        if (_state is null || (!_dirty && !_projectionDirty)) return;
        if (_dirty)
        {
            _dirty = false;
            foreach (long id in _cards.Keys.Where(id => !_owned.ContainsKey(id)).ToArray())
            {
                var removed = _cards[id];
                _grid.RemoveChild(removed); removed.QueueFree(); _cards.Remove(id);
            }
            foreach (var item in _owned.Values)
            {
                if (!_cards.TryGetValue(item.Id, out var card))
                {
                    long id = item.Id;
                    card = new GearDragCard
                    {
                        Name = "CraftInventoryItem" + id,
                        CustomMinimumSize = new(118, 72),
                        SizeFlagsHorizontal = SizeFlags.ExpandFill,
                        AutowrapMode = TextServer.AutowrapMode.WordSmart,
                        MouseForcePassScrollEvents = true,
                        ToggleMode = true,
                        FocusMode = FocusModeEnum.All,
                        DragDataRequested = () => DragData(id)
                    };
                    card.AddThemeFontSizeOverride("font_size", 11);
                    card.Pressed += () => SelectItem(id);
                    _cards.Add(id, card); _grid.AddChild(card);
                }
                bool equipped = _state.Character.Equipment.Values.Contains(item.Id);
                string name = ItemName(item);
                card.Text = name + "\n" + item.Rarity + (equipped ? "\nEquipped" : "");
                card.DragLabel = name + " · " + item.Rarity + (equipped ? " · Equipped" : "");
                card.TooltipText = name + " · #" + item.Id + (equipped ? " · Equipped" : "") + "\nSelect to inspect crafting options.";
                card.SetItemVisual(item.DefinitionId, _definitions[item.DefinitionId].Slots[0], _state.Character.Discipline, item.Rarity);
                card.AddThemeStyleboxOverride("hover_pressed", card.GetThemeStylebox("pressed"));
            }
        }
        _projectionDirty = false;
        RefreshSelection();
        ApplyProjection();
    }

    private void RefreshSelection()
    {
        foreach (var (id, card) in _cards) card.SetPressedNoSignal(id == SelectedItemId);
    }

    private void ApplyProjection()
    {
        string query = _search.Text.Trim();
        var entries = _owned.Values.Select(item => new { Item = item, Category = ItemCategory(item), Name = ItemName(item) }).ToArray();
        var ordered = _sort.Selected switch
        {
            1 => entries.OrderByDescending(entry => entry.Item.Rarity).ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Item.Id),
            2 => entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Item.Id),
            _ => entries.OrderBy(entry => entry.Category).ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Item.Id)
        };
        int index = 0, visible = 0;
        foreach (var entry in ordered)
        {
            var card = _cards[entry.Item.Id];
            card.Visible = (_typeFilter.Selected == 0 || _typeFilter.Selected == entry.Category) &&
                (_rarityFilter.Selected == 0 || (int)entry.Item.Rarity == _rarityFilter.Selected - 1) &&
                (query.Length == 0 || entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
            if (card.GetIndex() != index) _grid.MoveChild(card, index);
            index++;
            if (card.Visible) visible++;
        }
        _heading.Text = $"YOUR EQUIPMENT · {visible}/{entries.Length}";
        _empty.Visible = visible == 0;
        _empty.Text = entries.Length == 0 ? "No items available." : "No matching items.\nClear the filters to see all your gear.";
    }

    private int ItemCategory(PermanentItem item) => _definitions[item.DefinitionId].Slots[0] switch
    {
        EquipmentSlot.MainHand or EquipmentSlot.OffHand => 1,
        EquipmentSlot.Amulet or EquipmentSlot.Ring1 or EquipmentSlot.Ring2 => 3,
        _ => 2
    };

    private static string ItemName(PermanentItem item) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(item.DefinitionId.Split('.').Last().Replace('_', ' '));

    private static Label Caption(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", new Color("b8cbd2"));
        return label;
    }
}
