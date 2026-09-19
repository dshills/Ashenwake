using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public partial class GearLoadout
{
    private OptionButton _sort = null!, _typeFilter = null!, _rarityFilter = null!;
    private LineEdit _search = null!;
    private GearComparison? _comparison;
    private PanelContainer? _feedback;
    private Label _feedbackText = null!;
    private Control? _hoverSource, _feedbackSource;
    private bool _keyboardComparison;
    private float _feedbackSeconds;
    private bool _searchPaused;
    private Vector2 _pointer;

    private void BuildFilters(VBoxContainer contents)
    {
        var searchRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        contents.AddChild(searchRow);
        _search = new LineEdit { Name = "GearSearch", PlaceholderText = "Search items", SizeFlagsHorizontal = SizeFlags.ExpandFill, MaxLength = 80 };
        _search.AddThemeFontSizeOverride("font_size", 12); searchRow.AddChild(_search);
        _search.FocusEntered += () => { _searchPaused = true; _sandbox?.SetModalPaused("inventory-search", true); };
        _search.FocusExited += ReleaseSearchPause;
        var reset = new Button { Name = "GearResetFilters", Text = "Clear", TooltipText = "Show all items and restore type sorting." };
        reset.AddThemeFontSizeOverride("font_size", 11); searchRow.AddChild(reset);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; contents.AddChild(row);
        _typeFilter = Filter("GearTypeFilter", "Filter by equipment type", row, ["All types", "Weapons", "Armor", "Accessories"]);
        _rarityFilter = Filter("GearRarityFilter", "Filter by rarity", row, ["All rarities", .. Enum.GetNames<ItemRarity>()]);
        _sort = Filter("GearSort", "Sort inventory", row, ["Type", "Rarity", "Name"]);
        _search.TextChanged += _ => ProjectionChanged();
        foreach (var selector in new[] { _typeFilter, _rarityFilter, _sort }) selector.ItemSelected += _ => ProjectionChanged();
        reset.Pressed += () =>
        {
            _typeFilter.Select(0); _rarityFilter.Select(0); _sort.Select(0);
            _search.Text = ""; ProjectionChanged();
        };
    }

    private static OptionButton Filter(string name, string tip, HBoxContainer parent, string[] choices)
    {
        var select = new OptionButton { Name = name, TooltipText = tip, SizeFlagsHorizontal = SizeFlags.ExpandFill, FitToLongestItem = false };
        select.AddThemeFontSizeOverride("font_size", 11);
        for (int i = 0; i < choices.Length; i++) select.AddItem(choices[i], i);
        parent.AddChild(select); return select;
    }

    private void ProjectionChanged()
    {
        CancelDrag();
        ApplyProjection();
        if (_inventory?.GetParent() is ScrollContainer scroll) scroll.ScrollVertical = 0;
    }

    private void ApplyProjection()
    {
        if (_state is null || _inventory is null) return;
        HideComparison();
        string query = _search.Text.Trim();
        var entries = _state.Character.Items.Where(i => _items.ContainsKey(i.Id))
            .Select(i => new { Item = i, Category = ItemCategory(i), Name = ItemName(i) }).ToArray();
        var ordered = _sort.Selected switch
        {
            1 => entries.OrderByDescending(e => e.Item.Rarity).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Item.Id),
            2 => entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Item.Id),
            _ => entries.OrderBy(e => e.Category).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Item.Id)
        };
        int index = 0, visible = 0;
        foreach (var entry in ordered)
        {
            var card = _items[entry.Item.Id];
            card.Visible = (_typeFilter.Selected == 0 || _typeFilter.Selected == entry.Category) &&
                (_rarityFilter.Selected == 0 || (int)entry.Item.Rarity == _rarityFilter.Selected - 1) &&
                (query.Length == 0 || entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
            if (card.GetIndex() != index) _inventory.MoveChild(card, index);
            index++;
            if (card.Visible) visible++;
        }
        _empty.Visible = visible == 0;
        _empty.Text = entries.Length == 0 ? "Your backpack is empty.\nDrag equipped gear here to remove it." : "No matching items.\nClear the filters to see all your gear.";
        _inventoryTitle.Text = $"INVENTORY · {visible}/{entries.Length} · drop to unequip";
    }

    private int ItemCategory(PermanentItem item) => _content.Items.Single(d => d.Id == item.DefinitionId).Slots[0] switch
    {
        EquipmentSlot.MainHand or EquipmentSlot.OffHand => 1,
        EquipmentSlot.Amulet or EquipmentSlot.Ring1 or EquipmentSlot.Ring2 => 3,
        _ => 2
    };

    private EquipmentSlot PreferredSlot(PermanentItem item)
    {
        var slots = _content.Items.Single(d => d.Id == item.DefinitionId).Slots;
        return slots.Contains(ComparisonSlot) ? ComparisonSlot : slots[0];
    }

    private string DropReason(Variant data, EquipmentSlot? target)
    {
        if (!Owns(data)) return "This is not equipment from your inventory.";
        if (!ReadDrag(data, out long id, out var from)) return "Your equipment changed. Pick up the item again.";
        if (!_canEdit) return EquipBlockedReason?.Invoke(id, target ?? from ?? PreferredSlot(_state!.Character.Items.Single(i => i.Id == id)))
            is { Length: > 0 } reason ? reason : "Visit Torren in Greyhaven to change equipment.";
        if (target is not { } slot) return from is null ? "This item is already in your inventory." : "";
        if (_state!.Character.Equipment.GetValueOrDefault(slot) == id) return "Already equipped in this slot.";
        return EquipBlockedReason?.Invoke(id, slot) ?? "This item cannot be equipped here.";
    }

    private void BuildOverlays()
    {
        // These panels sit outside the inventory's clipping and layout, but never intercept input.
        var layer = new CanvasLayer { Name = "GearOverlays", Layer = 40 }; AddChild(layer);
        _comparison = new GearComparison { Name = "GearComparison", Visible = false, MouseFilter = MouseFilterEnum.Ignore }; layer.AddChild(_comparison);
        _feedback = new PanelContainer { Name = "GearDropFeedbackPanel", Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _feedback.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new("2c1c20f5"),
            BorderColor = new("d99375"),
            BorderWidthLeft = 2,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 8,
            ContentMarginBottom = 8
        });
        layer.AddChild(_feedback);
        _feedbackText = Caption(""); _feedbackText.Name = "GearDropFeedback";
        _feedbackText.CustomMinimumSize = new(260, 0); _feedbackText.AddThemeColorOverride("font_color", new Color("ffd3bf"));
        _feedback.AddChild(_feedbackText);
        SetProcess(false);
    }

    private void WirePresentation(GearDragCard card, Func<PermanentItem?> item, Func<EquipmentSlot> slot)
    {
        card.MouseEntered += () => ShowComparison(card, item(), slot, false);
        card.FocusEntered += () => ShowComparison(card, item(), slot, true);
        card.MouseExited += () => { if (_hoverSource == card && !_keyboardComparison) HideComparison(); ClearFeedbackFrom(card); };
        card.FocusExited += () => { if (_hoverSource == card && _keyboardComparison) HideComparison(); };
    }

    private void ShowComparison(Control source, PermanentItem? item, Func<EquipmentSlot> slot, bool keyboard)
    {
        if (item is null || _state is null || _comparison is null || !source.IsVisibleInTree() || GetViewport().GuiIsDragging()) return;
        _hoverSource = source; _keyboardComparison = keyboard;
        EquipmentSlot target = slot();
        _comparison.SetItems(_state, _content, item, target, EquipBlockedReason?.Invoke(item.Id, target) ?? "");
        _comparison.ResetSize(); _comparison.Show();
        PlaceOverlay(_comparison, keyboard ? source.GetGlobalRect().End : _pointer);
        SetProcess(true);
    }

    private void HideComparison()
    {
        _comparison?.Hide(); _hoverSource = null;
    }

    private void PresentDropReason(Control source, string reason)
    {
        if (_feedback is null || !IsVisibleInTree()) return;
        HideComparison();
        if (reason.Length == 0) { _feedback.Hide(); _feedbackSeconds = 0; _feedbackSource = null; return; }
        _feedbackSource = source; _feedbackText.Text = reason; _feedbackSeconds = 2;
        _feedback.ResetSize(); _feedback.Show(); PlaceOverlay(_feedback, _pointer); SetProcess(true);
    }

    private void ClearFeedbackFrom(Control source)
    {
        // Native drag teardown may send MouseExited even though release occurred on this card.
        if (_feedbackSource != source || source.GetGlobalRect().HasPoint(_pointer) || !GetViewport().GuiIsDragging()) return;
        _feedback?.Hide(); _feedbackSource = null; _feedbackSeconds = 0;
    }

    private void HidePresentation()
    {
        HideComparison(); _feedback?.Hide(); _feedbackSource = null; _feedbackSeconds = 0;
    }

    private void ReleaseSearchPause()
    {
        if (!_searchPaused) return;
        _searchPaused = false;
        if (_sandbox is not null && GodotObject.IsInstanceValid(_sandbox)) _sandbox.SetModalPaused("inventory-search", false);
    }

    public void SynchronizeSearchPause()
    {
        // A save restore may clear pause owners while keeping this cached field focused.
        if (_search.HasFocus() && IsVisibleInTree()) { _searchPaused = true; _sandbox?.SetModalPaused("inventory-search", true); }
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) { HidePresentation(); SetProcess(false); return; }
        if (_comparison?.Visible == true)
        {
            if (_hoverSource is null || !GodotObject.IsInstanceValid(_hoverSource) || !_hoverSource.IsVisibleInTree() || GetViewport().GuiIsDragging()) HideComparison();
            else PlaceOverlay(_comparison, _keyboardComparison ? _hoverSource.GetGlobalRect().End : _pointer);
        }
        if (_feedback?.Visible == true)
        {
            if (!GetViewport().GuiIsDragging()) _feedbackSeconds -= (float)delta;
            if (_feedbackSeconds <= 0) _feedback.Hide();
            else PlaceOverlay(_feedback, _pointer);
        }
        if (_comparison?.Visible != true && _feedback?.Visible != true) SetProcess(false);
    }

    private void PlaceOverlay(Control panel, Vector2 anchor)
    {
        var viewport = GetViewportRect().Size;
        var size = panel.GetCombinedMinimumSize();
        if (panel.Size != size) panel.Size = size;
        Vector2 at = anchor + new Vector2(18, 18);
        if (at.Y + size.Y > viewport.Y - 8) at.Y = anchor.Y - size.Y - 12;
        panel.Position = new(Math.Clamp(at.X, 8, Math.Max(8, viewport.X - size.X - 8)), Math.Clamp(at.Y, 8, Math.Max(8, viewport.Y - size.Y - 8)));
    }
}
