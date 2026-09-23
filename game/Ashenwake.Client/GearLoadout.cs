using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Cached equipment targets and backpack cards. Drops request the existing authoritative transactions.</summary>
public partial class GearLoadout : HBoxContainer
{
    public event Action<EquipmentSlot, long>? InspectRequested;
    public event Action<long, EquipmentSlot>? EquipRequested;
    public event Action<EquipmentSlot>? UnequipRequested;
    public Func<long, EquipmentSlot, string>? EquipBlockedReason { get; set; }
    public EquipmentSlot ComparisonSlot { get; set; } = EquipmentSlot.MainHand;
    private readonly Dictionary<EquipmentSlot, GearDragCard> _slots = [];
    private readonly Dictionary<long, GearDragCard> _items = [];
    private ProgressionSnapshot? _state;
    private HashSet<long> _presetItemIds = [];
    private ProgressionDefinition _content = null!;
    private GridContainer _inventory = null!;
    private Label _inventoryTitle = null!, _empty = null!;
    private long _epoch, _revision = -1;
    private bool _canEdit, _dirty;
    private string _signature = "";
    private Sandbox? _sandbox;
    private bool _dragPaused;
    private static readonly string[] MenuActions = ["aw_inventory", "aw_character", "aw_journey", "aw_endgame", "aw_experiment", "aw_save", "aw_load"];

    public override void _Ready()
    {
        Name = "GearLoadout"; CustomMinimumSize = new(540, 340);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 12);
        var equipment = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(equipment); equipment.AddChild(Caption("EQUIPPED · drop onto a slot"));
        var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 5); grid.AddThemeConstantOverride("v_separation", 5);
        equipment.AddChild(grid);
        EquipmentSlot?[] bodySlots = [EquipmentSlot.Shoulders, EquipmentSlot.Head, EquipmentSlot.Amulet,
            EquipmentSlot.MainHand, EquipmentSlot.Chest, EquipmentSlot.OffHand,
            EquipmentSlot.Gloves, EquipmentSlot.Belt, EquipmentSlot.Ring1,
            EquipmentSlot.Ring2, EquipmentSlot.Legs, null, null, EquipmentSlot.Boots, null];
        foreach (var bodySlot in bodySlots)
        {
            if (bodySlot is not { } slot) { grid.AddChild(new Control { MouseFilter = MouseFilterEnum.Ignore }); continue; }
            var card = Card("GearEquipment" + slot); card.CustomMinimumSize = new(76, 56);
            card.DragDataRequested = () => DragData(_state?.Character.Equipment.GetValueOrDefault(slot) ?? 0, slot.ToString());
            card.CanReceive = data => CanEquipDrop(data, slot);
            card.Receive = data => EquipDrop(data, slot);
            WirePresentation(card, () => _state?.Character.Items.FirstOrDefault(i => i.Id == _state.Character.Equipment.GetValueOrDefault(slot)), () => slot);
            card.DragHover = data => PresentDropReason(card, DropReason(data, slot));
            card.Pressed += () => InspectRequested?.Invoke(slot, _state?.Character.Equipment.GetValueOrDefault(slot) ?? 0);
            _slots.Add(slot, card); grid.AddChild(card);
        }
        var bag = Card("GearBackpack"); bag.CustomMinimumSize = new(268, 340);
        bag.CanReceive = CanUnequipDrop; bag.Receive = UnequipDrop;
        bag.DragHover = data => PresentDropReason(bag, DropReason(data, null));
        bag.MouseExited += () => ClearFeedbackFrom(bag);
        bag.TooltipText = "Drop an equipped item anywhere in this inventory panel to unequip it. Nothing is discarded.";
        AddChild(bag);
        var contents = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        bag.AddChild(contents); contents.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        contents.OffsetLeft = 7; contents.OffsetRight = -7; contents.OffsetTop = 5; contents.OffsetBottom = -5;
        _inventoryTitle = Caption("INVENTORY · drop here to unequip");
        _inventoryTitle.CustomMinimumSize = new(0, 28); contents.AddChild(_inventoryTitle);
        BuildFilters(contents);
        var scroll = new ScrollContainer
        {
            Name = "GearBackpackScroll",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            MouseFilter = MouseFilterEnum.Pass,
            MouseForcePassScrollEvents = false
        };
        contents.AddChild(scroll);
        _inventory = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
        _inventory.AddThemeConstantOverride("h_separation", 5); _inventory.AddThemeConstantOverride("v_separation", 5); scroll.AddChild(_inventory);
        _empty = Caption("Your backpack is empty.\nDrag equipped gear here to remove it."); _empty.Name = "GearEmptyState";
        contents.AddChild(_empty);
        BuildOverlays();
        for (Node? ancestor = GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
            if (ancestor is Sandbox sandbox) { _sandbox = sandbox; break; }
        VisibilityChanged += () => { if (!IsVisibleInTree()) { CancelDrag(); _search.ReleaseFocus(); ReleaseSearchPause(); } };
    }

    public void SetView(ProgressionSnapshot state, ProgressionDefinition content, long revision, bool canEdit)
    {
        string signature = state.Character.CharacterId + "/" + state.Character.Discipline + "/" +
            string.Join(',', state.Character.Equipment.Select(p => p.Key + ":" + p.Value)) + "/" + string.Join(',', state.Character.Items.Select(i => i.Id + ":" + i.IsFavorite + ":" + i.IsLocked)) + "/" +
            string.Join(';', (state.Character.EquipmentPresets ?? []).Select(preset => preset.Id + ":" + string.Join(',', preset.Equipment.Values))) + ":" +
            string.Join(';', (state.Character.BuildLoadouts ?? []).Select(loadout => loadout.Id + ":" + string.Join(',', loadout.Equipment.Values))) + "/" +
            Ashenwake.Core.Content.JsonData.Hash(state.Character.Stash);
        bool changed = _state is null || _revision != revision || _signature != signature || _canEdit != canEdit;
        _state = state; _content = content; _revision = revision; _signature = signature; _canEdit = canEdit;
        if (!changed) return;
        _presetItemIds = (state.Character.EquipmentPresets ?? []).SelectMany(preset => preset.Equipment.Values)
            .Concat((state.Character.BuildLoadouts ?? []).SelectMany(loadout => loadout.Equipment.Values)).ToHashSet();
        _epoch++; _dirty = true;
        // Keep the source/target controls alive until Godot finishes dispatching the native drop.
        if (!GetViewport().GuiIsDragging()) RefreshCards();
    }

    public void CancelDrag()
    {
        _epoch++;
        HidePresentation();
        if (!IsInsideTree()) return;
        var viewport = GetViewport();
        var data = viewport.GuiGetDragData();
        if (!Owns(data)) return;
        var values = data.AsGodotDictionary();
        if (!values.TryGetValue("epoch", out var epoch) || epoch.VariantType != Variant.Type.Int) return;
        string owner = values["owner"].AsString();
        long dragEpoch = epoch.AsInt64();
        // Focus/visibility notifications can arrive while Godot removes the drag preview.
        // Reject drops immediately above, but let native tree mutation finish before cancelling.
        // Capture the old payload identity: a queued cancellation must never end a newer drag.
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(viewport) || viewport.IsQueuedForDeletion() || !viewport.GuiIsDragging()) return;
            var current = viewport.GuiGetDragData();
            if (current.VariantType != Variant.Type.Dictionary) return;
            var currentValues = current.AsGodotDictionary();
            if (currentValues.TryGetValue("owner", out var currentOwner) && currentOwner.VariantType == Variant.Type.String && currentOwner.AsString() == owner &&
                currentValues.TryGetValue("epoch", out var currentEpoch) && currentEpoch.VariantType == Variant.Type.Int && currentEpoch.AsInt64() == dragEpoch)
                viewport.GuiCancelDrag();
        }).CallDeferred();
    }

    public override void _Input(InputEvent input)
    {
        if (input is InputEventMouseMotion motion) _pointer = motion.Position;
        else if (input is InputEventMouseButton button) _pointer = button.Position;
        if (!Owns(GetViewport().GuiGetDragData())) return;
        if (input.IsActionPressed("ui_cancel")) { CancelDrag(); GetViewport().SetInputAsHandled(); }
        else if (input is InputEventKey or InputEventJoypadButton)
        {
            // Menus and restoration may proceed after cancellation; gameplay shortcuts cannot run mid-drag.
            if (MenuActions.Any(action => InputMap.HasAction(action) && input.IsActionPressed(action)))
            {
                // Godot consumes the event that cancels its native drag. Deliver this menu
                // press once after drag dispatch ends so the ordinary owner can handle it.
                var viewport = GetViewport();
                var menuPress = (InputEvent)input.Duplicate();
                viewport.SetInputAsHandled(); CancelDrag();
                long canceledEpoch = _epoch;
                Callable.From(() =>
                {
                    if (GodotObject.IsInstanceValid(this) && IsInsideTree() && IsVisibleInTree() &&
                        _epoch == canceledEpoch && GodotObject.IsInstanceValid(viewport) && !viewport.GuiIsDragging()) viewport.PushInput(menuPress, true);
                }).CallDeferred();
            }
            else GetViewport().SetInputAsHandled();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut && IsInsideTree()) CancelDrag();
        if (what == NotificationDragBegin && IsInsideTree() && Owns(GetViewport().GuiGetDragData()))
        { HidePresentation(); _dragPaused = true; _sandbox?.SetModalPaused("equipment-drag", true); }
        if (what == NotificationDragEnd)
        {
            ReleaseDragPause();
            if (IsInsideTree()) Callable.From(RefreshCards).CallDeferred();
        }
    }

    public override void _ExitTree() { CancelDrag(); ReleaseDragPause(); ReleaseSearchPause(); }
    private void ReleaseDragPause()
    {
        if (!_dragPaused) return;
        _dragPaused = false;
        if (_sandbox is not null && GodotObject.IsInstanceValid(_sandbox)) _sandbox.SetModalPaused("equipment-drag", false);
    }

    private Variant DragData(long id, string from)
    {
        if (id == 0 || _state is null || CharacterStash.IsStored(_state.Character, id) || !_state.Character.Items.Any(i => i.Id == id)) return default;
        return new Godot.Collections.Dictionary
        {
            ["owner"] = GetInstanceId().ToString(),
            ["epoch"] = _epoch,
            ["item"] = id,
            ["from"] = from
        };
    }

    private bool Owns(Variant data) => data.VariantType == Variant.Type.Dictionary &&
        data.AsGodotDictionary().TryGetValue("owner", out var owner) && owner.VariantType == Variant.Type.String && owner.AsString() == GetInstanceId().ToString();

    private bool ReadDrag(Variant data, out long id, out EquipmentSlot? from)
    {
        id = 0; from = null;
        if (!IsVisibleInTree() || _state is null || !Owns(data)) return false;
        var values = data.AsGodotDictionary();
        if (!values.TryGetValue("epoch", out var epoch) || epoch.VariantType != Variant.Type.Int || epoch.AsInt64() != _epoch ||
            !values.TryGetValue("item", out var item) || item.VariantType != Variant.Type.Int ||
            !values.TryGetValue("from", out var origin) || origin.VariantType != Variant.Type.String) return false;
        long itemId = item.AsInt64(); id = itemId;
        if (CharacterStash.IsStored(_state.Character, itemId) || !_state.Character.Items.Any(i => i.Id == itemId)) return false;
        if (origin.AsString().Length == 0) return !_state.Character.Equipment.Values.Contains(id);
        if (!Enum.TryParse<EquipmentSlot>(origin.AsString(), out var slot) || !Enum.IsDefined(slot) || _state.Character.Equipment.GetValueOrDefault(slot) != id) return false;
        from = slot; return true;
    }

    private bool CanEquipDrop(Variant data, EquipmentSlot slot) => Owns(data) && DropReason(data, slot).Length == 0;
    private bool CanUnequipDrop(Variant data) => Owns(data) && DropReason(data, null).Length == 0;
    private void EquipDrop(Variant data, EquipmentSlot slot)
    {
        if (!CanEquipDrop(data, slot) || !ReadDrag(data, out long id, out _)) return;
        _epoch++; EquipRequested?.Invoke(id, slot);
    }
    private void UnequipDrop(Variant data)
    {
        if (!CanUnequipDrop(data) || !ReadDrag(data, out _, out var from) || from is null) return;
        _epoch++; UnequipRequested?.Invoke(from.Value);
    }

    private void RefreshCards()
    {
        if (!_dirty || _state is null || !IsInsideTree() || IsQueuedForDeletion()) return;
        _dirty = false;
        HideComparison();
        foreach (var (slot, card) in _slots)
        {
            var item = _state.Character.Items.FirstOrDefault(i => i.Id == _state.Character.Equipment.GetValueOrDefault(slot));
            card.Text = SlotName(slot) + "\n" + (item is null ? "Empty" : ItemName(item));
            card.DragLabel = SlotName(slot) + " · " + (item is null ? "Empty" : ItemName(item));
            card.TooltipText = item is null ? "Empty " + SlotName(slot) + " slot · drop compatible gear here." : "";
            card.SetManagementBadges(item?.IsFavorite == true, item?.IsLocked == true, item is not null && _presetItemIds.Contains(item.Id));
            card.SetItemVisual(item?.DefinitionId ?? "", slot, _state.Character.Discipline, item?.Rarity);
        }
        var backpack = _state.Character.Items.Where(i => !CharacterStash.IsStored(_state.Character, i.Id) && !_state.Character.Equipment.Values.Contains(i.Id)).OrderBy(i => i.Id).ToArray();
        var ids = backpack.Select(i => i.Id).ToHashSet();
        foreach (long id in _items.Keys.Where(id => !ids.Contains(id)).ToArray())
        { var removed = _items[id]; _inventory.RemoveChild(removed); removed.QueueFree(); _items.Remove(id); }
        for (int index = 0; index < backpack.Length; index++)
        {
            var item = backpack[index];
            if (!_items.TryGetValue(item.Id, out var card))
            {
                long id = item.Id;
                card = Card("GearInventoryItem" + id); card.CustomMinimumSize = new(114, 64); card.MouseForcePassScrollEvents = true;
                card.DragDataRequested = () => DragData(id, "");
                card.CanReceive = CanUnequipDrop; card.Receive = UnequipDrop;
                card.DragHover = data => PresentDropReason(card, DropReason(data, null));
                WirePresentation(card, () => _state.Character.Items.FirstOrDefault(i => i.Id == id), () => PreferredSlot(_state.Character.Items.Single(i => i.Id == id)));
                card.Pressed += () =>
                {
                    var owned = _state.Character.Items.FirstOrDefault(i => i.Id == id);
                    if (owned is not null && !CharacterStash.IsStored(_state.Character, id)) InspectRequested?.Invoke(PreferredSlot(owned), id);
                };
                _items.Add(id, card); _inventory.AddChild(card);
            }
            card.Text = ItemName(item) + "\n" + item.Rarity; card.DragLabel = card.Text;
            card.TooltipText = "";
            card.SetManagementBadges(item.IsFavorite, item.IsLocked, _presetItemIds.Contains(item.Id));
            card.SetItemVisual(item.DefinitionId, _content.Items.Single(d => d.Id == item.DefinitionId).Slots[0], _state.Character.Discipline, item.Rarity);
        }
        ApplyProjection();
    }

    private static string ItemName(PermanentItem item) => EquipmentNames.For(item.DefinitionId);
    private static string SlotName(EquipmentSlot slot) => slot switch { EquipmentSlot.MainHand => "Weapon", EquipmentSlot.OffHand => "Off hand", EquipmentSlot.Ring1 => "Ring 1", EquipmentSlot.Ring2 => "Ring 2", _ => slot.ToString() };
    private static Label Caption(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", 12); return label;
    }
    private static GearDragCard Card(string name)
    {
        var card = new GearDragCard { Name = name, SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseForcePassScrollEvents = false };
        card.AddThemeFontSizeOverride("font_size", 11);
        var style = new StyleBoxFlat
        {
            BgColor = new("162833"),
            BorderColor = new("496571"),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = 5,
            ContentMarginRight = 5,
            ContentMarginTop = 3,
            ContentMarginBottom = 3
        };
        card.AddThemeStyleboxOverride("normal", style);
        var hover = (StyleBoxFlat)style.Duplicate(); hover.BorderColor = new("92d9c1"); card.AddThemeStyleboxOverride("hover", hover);
        return card;
    }
}
