using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public sealed record PersonalStashTabDisplay(string Id, string Name, int Capacity);
public sealed record PersonalStashItemDisplay(long Id, string DefinitionId, EquipmentSlot Slot, ItemRarity Rarity,
    string Name, string Summary, string Details, bool Favorite, bool Locked, bool Equipped, string TabId,
    string[] References, string DepositReason = "", string RetrieveReason = "");
public sealed record PersonalStashDisplay(string SessionKey, long Revision, string Discipline, bool AtChest,
    bool CanTransfer, bool CanApproach, string AccessReason, int BackpackCapacity,
    PersonalStashTabDisplay[] Tabs, PersonalStashItemDisplay[] Items, string Notice = "");

/// <summary>Personal storage inspection and explicit transfers. Every payload is bound to this panel and adopted session.</summary>
public partial class PersonalStashPanel : Control
{
    public event Action<long, string>? DepositRequested, MoveRequested;
    public event Action<long>? RetrieveRequested;
    public event Action<string, string>? RenameTabRequested;
    public event Action<string>? MenuRequested;
    public event Action? ApproachRequested;
    public event Action<bool>? VisibilityChangedByPlayer;
    private PersonalStashDisplay? _view;
    private PanelContainer _panel = null!;
    private ColorRect _backdrop = null!;
    private VBoxContainer _column = null!, _backpack = null!, _stored = null!, _details = null!;
    private HBoxContainer _tabs = null!, _body = null!;
    private ScrollContainer _detailScroll = null!, _backpackScroll = null!, _storedScroll = null!;
    private Label _summary = null!, _bagTitle = null!, _stashTitle = null!, _notice = null!;
    private LineEdit _search = null!, _rename = null!;
    private OptionButton _type = null!, _rarity = null!, _usage = null!;
    private Button _transfer = null!, _approach = null!, _renameButton = null!, _close = null!;
    private Sandbox? _sandbox;
    private string _tab = "";
    private long _selected, _epoch;
    private int _oldSibling = -1, _layoutFrames;
    private bool _pauseHeld, _dirty;
    public bool IsOpen => _panel is { Visible: true };
    public string SelectedTab => _tab;
    public long SelectedItem => _selected;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        for (Node? parent = GetParent(); parent is not null; parent = parent.GetParent())
            if (parent is Sandbox sandbox) { _sandbox = sandbox; break; }
        _backdrop = new ColorRect { Color = new(0, 0, 0, .73f), MouseFilter = MouseFilterEnum.Stop, MouseForcePassScrollEvents = false };
        AddChild(_backdrop); _backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _panel = new PanelContainer { Name = "PersonalStashPanel", MouseForcePassScrollEvents = false };
        _panel.AddThemeStyleboxOverride("panel", Surface()); AddChild(_panel);
        _column = Stack(7); _panel.AddChild(_column);
        var title = Text("YOUR GREYHAVEN STASH", 23); title.AddThemeColorOverride("font_color", new("dac497")); _column.AddChild(title);
        _summary = Text("", 12); _summary.Name = "StashSummary"; _column.AddChild(_summary);
        var filters = new HBoxContainer(); filters.AddThemeConstantOverride("separation", 6); _column.AddChild(filters);
        _search = new LineEdit { Name = "StashSearch", PlaceholderText = "Search gear or saved builds", MaxLength = 80, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.5f };
        _search.AddThemeFontSizeOverride("font_size", 12); filters.AddChild(_search);
        _type = Filter(filters, "StashTypeFilter", ["All types", "Weapons", "Armor", "Accessories"]);
        _rarity = Filter(filters, "StashRarityFilter", ["All rarities", .. Enum.GetNames<ItemRarity>()]);
        _usage = Filter(filters, "StashUsageFilter", ["All gear", "Favorites", "Locked", "Saved builds"]);
        var clear = Button("Clear", "StashClearFilters", ResetFilters); clear.SizeFlagsHorizontal = SizeFlags.Fill; clear.CustomMinimumSize = new(50, 32); filters.AddChild(clear);
        _search.TextChanged += _ => ProjectionChanged();
        foreach (var selector in new[] { _type, _rarity, _usage }) selector.ItemSelected += _ => ProjectionChanged();
        _tabs = new HBoxContainer { Name = "StashTabs" }; _tabs.AddThemeConstantOverride("separation", 6); _column.AddChild(_tabs);
        var renameRow = new HBoxContainer(); renameRow.AddThemeConstantOverride("separation", 6); _column.AddChild(renameRow);
        _rename = new LineEdit { Name = "StashTabName", MaxLength = 32, PlaceholderText = "Tab name", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rename.AddThemeFontSizeOverride("font_size", 12); renameRow.AddChild(_rename);
        _rename.TextSubmitted += _ => Rename();
        _renameButton = Button("Rename tab", "StashRename", Rename); _renameButton.SizeFlagsHorizontal = SizeFlags.Fill; _renameButton.CustomMinimumSize = new(110, 32); renameRow.AddChild(_renameButton);
        _body = new HBoxContainer { Name = "StashContents", SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 180) };
        _body.AddThemeConstantOverride("separation", 12); _column.AddChild(_body);
        (_bagTitle, _backpack, _backpackScroll) = BuildPane("StashBackpack", "Backpack", () => "");
        (_stashTitle, _stored, _storedScroll) = BuildPane("StashStorage", "Stored gear", () => _tab);
        _detailScroll = new ScrollContainer { Name = "StashDetailsScroll", CustomMinimumSize = new(0, 112), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _column.AddChild(_detailScroll); _details = Stack(5); _details.SizeFlagsHorizontal = SizeFlags.ExpandFill; _detailScroll.AddChild(_details);
        _notice = Text("", 12); _notice.Name = "StashNotice"; _notice.MaxLinesVisible = 2; _column.AddChild(_notice);
        var footer = new HBoxContainer(); footer.AddThemeConstantOverride("separation", 8); _column.AddChild(footer);
        _transfer = Button("Select an item", "StashTransfer", TransferSelection); footer.AddChild(_transfer);
        _approach = Button("Approach stash chest", "StashApproach", () => { if (_view?.CanApproach != true) return; SetOpen(false); ApproachRequested?.Invoke(); }); footer.AddChild(_approach);
        _close = Button("Close [Esc]", "StashClose", () => SetOpen(false)); footer.AddChild(_close);
        _panel.Visible = _backdrop.Visible = false;
        VisibilityChanged += () => { if (!IsVisibleInTree()) CancelDrag(); UpdateModal(); };
        _panel.VisibilityChanged += UpdateModal; GetViewport().SizeChanged += RequestLayout; SetProcess(true); RequestLayout();
    }
    public void SetView(PersonalStashDisplay view)
    {
        bool newSession = _view?.SessionKey != view.SessionKey;
        bool changed = _view is null || _view.Revision != view.Revision || !SameView(_view, view);
        _view = view;
        if (!changed) return;
        _epoch++; _dirty = true;
        if (newSession) { _selected = 0; _tab = ""; CancelDrag(); }
        if (!view.Tabs.Any(t => t.Id == _tab)) _tab = view.Tabs.FirstOrDefault()?.Id ?? "";
        if (!view.Items.Any(i => i.Id == _selected)) _selected = 0;
        if (!GetViewport().GuiIsDragging()) Rebuild();
    }
    public void SetOpen(bool open)
    {
        if (!open) { CancelDrag(); _search.ReleaseFocus(); _rename.ReleaseFocus(); }
        _panel.Visible = open;
        if (open) { Rebuild(); RequestLayout(); _close.GrabFocus(); }
        UpdateModal(); VisibilityChangedByPlayer?.Invoke(open);
    }
    public void SelectTab(string id)
    {
        if (_view?.Tabs.Any(t => t.Id == id) != true) return;
        CancelDrag(); _tab = id; _storedScroll.ScrollVertical = 0; _rename.ReleaseFocus(); Rebuild();
    }
    public void SelectItem(long id)
    {
        if (_view?.Items.FirstOrDefault(i => i.Id == id) is not { } item) return;
        _selected = id;
        _detailScroll.ScrollVertical = 0;
        if (item.TabId.Length > 0 && item.TabId != _tab) { _tab = item.TabId; Rebuild(); }
        else RefreshSelection();
    }
    public void RevealItem(long id)
    {
        ResetFilters(); SelectItem(id);
        Callable.From(() =>
        {
            if (!IsInsideTree() || !IsOpen || FindChild("StashItem_" + id, true, false) is not Control card) return;
            if (_view?.Items.FirstOrDefault(i => i.Id == id) is { } item)
                (item.TabId.Length == 0 ? _backpackScroll : _storedScroll).EnsureControlVisible(card);
        }).CallDeferred();
    }
    public void SessionRestored() { CancelDrag(); _view = null; _selected = 0; _tab = ""; SetOpen(false); }
    public void Notice(string message) { _notice.Text = message; _notice.TooltipText = message; _notice.Visible = message.Length > 0; }
    public void CancelDrag()
    {
        _epoch++;
        if (!IsInsideTree()) return;
        var viewport = GetViewport(); var data = viewport.GuiGetDragData();
        if (!Owns(data)) return;
        var values = data.AsGodotDictionary();
        if (!values.TryGetValue("epoch", out var epoch) || epoch.VariantType != Variant.Type.Int) return;
        long oldEpoch = epoch.AsInt64(); string owner = GetInstanceId().ToString();
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(viewport) || !viewport.GuiIsDragging()) return;
            var current = viewport.GuiGetDragData();
            if (current.VariantType != Variant.Type.Dictionary) return;
            var currentValues = current.AsGodotDictionary();
            if (currentValues.TryGetValue("owner", out var value) && value.VariantType == Variant.Type.String && value.AsString() == owner &&
                currentValues.TryGetValue("epoch", out var currentEpoch) && currentEpoch.VariantType == Variant.Type.Int && currentEpoch.AsInt64() == oldEpoch) viewport.GuiCancelDrag();
        }).CallDeferred();
    }
    public override void _Process(double delta)
    {
        if (!IsOpen) return;
        if (_layoutFrames > 0) { _layoutFrames--; UpdateLayout(); }
        if (_dirty && !GetViewport().GuiIsDragging()) Rebuild();
    }
    public override void _Input(InputEvent input)
    {
        if (!IsOpen || !IsVisibleInTree() || input is not (InputEventKey or InputEventJoypadButton)) return;
        if (GetViewport().GuiGetFocusOwner() is LineEdit edit)
        {
            if (input.IsActionPressed("ui_cancel")) { edit.ReleaseFocus(); _close.GrabFocus(); GetViewport().SetInputAsHandled(); }
            return;
        }
        if (input.IsActionPressed("ui_cancel"))
        { if (Owns(GetViewport().GuiGetDragData())) CancelDrag(); else SetOpen(false); GetViewport().SetInputAsHandled(); return; }
        string? menu = new[] { "aw_inventory", "aw_character", "aw_journey", "aw_endgame", "aw_experiment" }.FirstOrDefault(a => input.IsActionPressed(a));
        if (menu is not null) { CancelDrag(); MenuRequested?.Invoke(menu); GetViewport().SetInputAsHandled(); return; }
        if (!new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev", "aw_save", "aw_load" }.Any(a => input.IsAction(a))) GetViewport().SetInputAsHandled();
    }
    public override void _UnhandledInput(InputEvent input)
    { if (IsOpen && IsVisibleInTree() && GetViewport().GuiGetFocusOwner() is LineEdit && input is InputEventKey or InputEventJoypadButton) GetViewport().SetInputAsHandled(); }
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut && IsInsideTree()) CancelDrag();
        if (what == NotificationDragEnd && IsInsideTree()) Callable.From(() => { if (IsInsideTree() && !IsQueuedForDeletion() && IsOpen && _dirty) Rebuild(); }).CallDeferred();
    }
    public override void _ExitTree()
    {
        CancelDrag(); GetViewport().SizeChanged -= RequestLayout;
        if (!_pauseHeld) return; _pauseHeld = false; var sandbox = _sandbox;
        Callable.From(() => { if (sandbox is not null && GodotObject.IsInstanceValid(sandbox) && sandbox.IsInsideTree() && !sandbox.IsQueuedForDeletion()) sandbox.SetModalPaused("personal-stash", false); }).CallDeferred();
    }
    private (Label Title, VBoxContainer Items, ScrollContainer Scroll) BuildPane(string name, string title, Func<string> destination)
    {
        var pane = Stack(5); pane.SizeFlagsHorizontal = SizeFlags.ExpandFill; _body.AddChild(pane);
        var heading = Text(title, 14); heading.Name = name + "Title"; pane.AddChild(heading);
        var zone = Card(name + "Drop"); zone.CustomMinimumSize = new(0, 140); zone.SizeFlagsVertical = SizeFlags.ExpandFill;
        zone.CanReceive = data => ReadDrag(data, out var item) && TransferReason(item!, destination()).Length == 0;
        zone.Receive = data => Drop(data, destination()); zone.TooltipText = "Drop gear here to transfer it. Items retain favorites, locks and saved build references."; pane.AddChild(zone);
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Pass }; zone.AddChild(margin); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 7);
        var scroll = new ScrollContainer { Name = name + "Scroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass };
        margin.AddChild(scroll); var items = Stack(5); items.SizeFlagsHorizontal = SizeFlags.ExpandFill; items.MouseFilter = MouseFilterEnum.Pass; scroll.AddChild(items);
        return (heading, items, scroll);
    }
    private void Rebuild()
    {
        if (!IsOpen || _view is null) return;
        if (GetViewport().GuiIsDragging()) { _dirty = true; return; }
        _dirty = false; string focus = GetViewport().GuiGetFocusOwner()?.Name.ToString() ?? "";
        Clear(_tabs); Clear(_backpack); Clear(_stored);
        int stored = _view.Items.Count(i => i.TabId.Length > 0), bag = _view.Items.Length - stored;
        _summary.Text = $"Personal storage · {stored}/{_view.Tabs.Sum(t => t.Capacity)} stored · ★ favorite · L locked · ◆ saved build";
        foreach (var tab in _view.Tabs)
        {
            string id = tab.Id; int count = _view.Items.Count(i => i.TabId == id);
            var button = Card("StashTab_" + SafeName(id)); button.Text = tab.Name + $"  {count}/{tab.Capacity}"; button.CustomMinimumSize = new(0, 34);
            button.ToggleMode = true; button.SetPressedNoSignal(id == _tab); button.Pressed += () => SelectTab(id);
            button.CanReceive = data => ReadDrag(data, out var item) && TransferReason(item!, id).Length == 0;
            button.Receive = data => Drop(data, id); _tabs.AddChild(button);
        }
        var activeTab = _view.Tabs.FirstOrDefault(t => t.Id == _tab);
        if (!_rename.HasFocus()) _rename.Text = activeTab?.Name ?? "";
        _rename.Editable = _view.CanTransfer; _renameButton.Disabled = !_view.CanTransfer || activeTab is null;
        _renameButton.TooltipText = _view.CanTransfer ? "Name this tab (up to 32 characters)." : _view.AccessReason;
        var visible = _view.Items.Where(Matches).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Id).ToArray();
        var bagItems = visible.Where(i => i.TabId.Length == 0).ToArray(); var storedItems = visible.Where(i => i.TabId == _tab).ToArray();
        _bagTitle.Text = $"BACKPACK · {bag}/{_view.BackpackCapacity} · {bagItems.Length} shown";
        _stashTitle.Text = (activeTab?.Name ?? "STASH").ToUpperInvariant() + $" · {storedItems.Length} shown";
        foreach (var item in bagItems) AddItem(_backpack, item, "");
        foreach (var item in storedItems) AddItem(_stored, item, _tab);
        if (bagItems.Length == 0) _backpack.AddChild(Text("No matching carried gear.\nDrop stored gear here to retrieve it.", 13));
        if (storedItems.Length == 0) _stored.AddChild(Text("No matching stored gear.\nDrop backpack gear here to store it.", 13));
        RefreshSelection();
        _approach.Visible = _view.CanApproach; _approach.Disabled = !_view.CanApproach;
        Notice(_view.Notice.Length > 0 ? _view.Notice : _view.AccessReason);
        RequestLayout();
        if (focus.Length > 0 && FindChild(focus, true, false) is Control control && control.IsVisibleInTree()) control.GrabFocus();
    }
    private void RefreshSelection()
    {
        if (!IsOpen || _view is null) return;
        string selectedName = "StashItem_" + _selected;
        foreach (var list in new[] { _backpack, _stored })
            foreach (var card in list.GetChildren().OfType<GearDragCard>()) card.SetPressedNoSignal(card.Name == selectedName);
        Clear(_details);
        var selected = _view.Items.FirstOrDefault(i => i.Id == _selected);
        if (selected is null) _details.AddChild(Text("Select gear to inspect its power, stats and saved build references. Drag between the two panels, or use the transfer button below.", 14));
        else
        {
            _details.AddChild(Text(selected.Name + " · " + selected.Slot + (selected.Equipped ? " · EQUIPPED" : ""), 18));
            _details.AddChild(Text(selected.Details, 13));
            _details.AddChild(Text(selected.References.Length == 0 ? "No saved builds reference this item." : "USED BY · " + string.Join(" · ", selected.References), 13));
            string reason = TransferReason(selected, selected.TabId.Length == 0 ? _tab : "");
            if (reason.Length > 0) _details.AddChild(Text(reason, 13));
        }
        _transfer.Text = selected is null ? "Select an item" : selected.TabId.Length == 0 ? "Deposit selected item" : "Retrieve selected item";
        _transfer.Disabled = selected is null || TransferReason(selected, selected.TabId.Length == 0 ? _tab : "").Length > 0;
        _transfer.TooltipText = selected is null ? "" : TransferReason(selected, selected.TabId.Length == 0 ? _tab : "");
        RequestLayout();
    }
    private void AddItem(VBoxContainer list, PersonalStashItemDisplay item, string destination)
    {
        long id = item.Id; var card = Card("StashItem_" + id); card.CustomMinimumSize = new(0, 57);
        card.Text = item.Name + "\n" + (item.Equipped ? "Equipped · " : "") + item.Summary;
        card.DragLabel = item.Name; card.ToggleMode = true; card.SetPressedNoSignal(id == _selected);
        card.SetItemVisual(item.DefinitionId, item.Slot, _view!.Discipline, item.Rarity);
        card.SetManagementBadges(item.Favorite, item.Locked, item.References.Length > 0);
        card.TooltipText = item.Name + "\n" + item.Details + (item.References.Length == 0 ? "" : "\nSaved builds: " + string.Join(", ", item.References));
        card.Pressed += () => SelectItem(id); card.DragDataRequested = () => DragData(id);
        card.CanReceive = data => ReadDrag(data, out var source) && TransferReason(source!, destination).Length == 0;
        card.Receive = data => Drop(data, destination); list.AddChild(card);
    }
    private Variant DragData(long id)
    {
        var item = _view?.Items.FirstOrDefault(i => i.Id == id);
        if (!IsOpen || !IsVisibleInTree() || _view?.CanTransfer != true || item is null || item.Equipped) return default;
        return new Godot.Collections.Dictionary
        {
            ["owner"] = GetInstanceId().ToString(),
            ["session"] = _view.SessionKey,
            ["epoch"] = _epoch,
            ["item"] = id,
            ["from"] = item.TabId
        };
    }
    private bool Owns(Variant data) => data.VariantType == Variant.Type.Dictionary && data.AsGodotDictionary().TryGetValue("owner", out var owner) &&
        owner.VariantType == Variant.Type.String && owner.AsString() == GetInstanceId().ToString();
    private bool ReadDrag(Variant data, out PersonalStashItemDisplay? item)
    {
        item = null;
        if (!IsOpen || !IsVisibleInTree() || _view is null || !Owns(data)) return false;
        var values = data.AsGodotDictionary();
        if (!values.TryGetValue("session", out var session) || session.VariantType != Variant.Type.String || session.AsString() != _view.SessionKey ||
            !values.TryGetValue("epoch", out var epoch) || epoch.VariantType != Variant.Type.Int || epoch.AsInt64() != _epoch ||
            !values.TryGetValue("item", out var id) || id.VariantType != Variant.Type.Int ||
            !values.TryGetValue("from", out var from) || from.VariantType != Variant.Type.String) return false;
        item = _view.Items.FirstOrDefault(i => i.Id == id.AsInt64());
        return item is not null && item.TabId == from.AsString();
    }
    private string TransferReason(PersonalStashItemDisplay item, string destination)
    {
        if (_view?.CanTransfer != true) return _view?.AccessReason is { Length: > 0 } reason ? reason : "Approach your Greyhaven stash chest to transfer gear.";
        if (item.TabId == destination) return "This item is already here.";
        if (destination.Length == 0)
        {
            if (item.RetrieveReason.Length > 0) return item.RetrieveReason;
            return _view.Items.Count(i => i.TabId.Length == 0) >= _view.BackpackCapacity ? "Your backpack is full." : "";
        }
        var tab = _view.Tabs.FirstOrDefault(t => t.Id == destination);
        if (tab is null) return "Select an available stash tab.";
        if (item.Equipped) return "Unequip this item before storing it.";
        if (item.TabId.Length == 0 && item.DepositReason.Length > 0) return item.DepositReason;
        return _view.Items.Count(i => i.TabId == destination) >= tab.Capacity ? "This stash tab is full." : "";
    }
    private void Drop(Variant data, string destination)
    {
        if (!ReadDrag(data, out var item) || TransferReason(item!, destination).Length > 0) return;
        Dispatch(item!, destination);
    }
    private void TransferSelection()
    {
        var item = _view?.Items.FirstOrDefault(i => i.Id == _selected);
        if (item is null) return;
        string destination = item.TabId.Length == 0 ? _tab : "";
        if (TransferReason(item, destination).Length == 0) Dispatch(item, destination);
    }
    private void Dispatch(PersonalStashItemDisplay item, string destination)
    {
        _epoch++;
        if (destination.Length == 0) RetrieveRequested?.Invoke(item.Id);
        else if (item.TabId.Length == 0) DepositRequested?.Invoke(item.Id, destination);
        else MoveRequested?.Invoke(item.Id, destination);
    }
    private void Rename()
    {
        string name = _rename.Text.Trim();
        if (_view?.CanTransfer != true || !_view.Tabs.Any(t => t.Id == _tab) || name.Length is < 1 or > 32 || name.Any(char.IsControl))
        { Notice("Choose a tab name of 1–32 characters at the stash chest."); return; }
        CancelDrag(); _rename.ReleaseFocus(); RenameTabRequested?.Invoke(_tab, name);
    }
    private void ProjectionChanged() { CancelDrag(); _backpackScroll.ScrollVertical = _storedScroll.ScrollVertical = 0; Rebuild(); }
    private void ResetFilters() { _type.Select(0); _rarity.Select(0); _usage.Select(0); _search.Text = ""; ProjectionChanged(); }
    private bool Matches(PersonalStashItemDisplay item)
    {
        string query = _search.Text.Trim();
        int type = item.Slot is EquipmentSlot.MainHand or EquipmentSlot.OffHand ? 1 : item.Slot is EquipmentSlot.Ring1 or EquipmentSlot.Ring2 or EquipmentSlot.Amulet ? 3 : 2;
        return (_type.Selected == 0 || _type.Selected == type) && (_rarity.Selected == 0 || (int)item.Rarity == _rarity.Selected - 1) &&
            (_usage.Selected == 0 || _usage.Selected == 1 && item.Favorite || _usage.Selected == 2 && item.Locked || _usage.Selected == 3 && item.References.Length > 0) &&
            (query.Length == 0 || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || item.References.Any(r => r.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }
    private void UpdateModal()
    {
        if (_panel is null || _backdrop is null) return;
        bool open = IsVisibleInTree() && IsOpen; _backdrop.Visible = open;
        if (open && _oldSibling < 0) { _oldSibling = GetIndex(); GetParent().MoveChild(this, GetParent().GetChildCount() - 1); }
        else if (!open && _oldSibling >= 0) { GetParent().MoveChild(this, Math.Min(_oldSibling, GetParent().GetChildCount() - 1)); _oldSibling = -1; }
        if (_pauseHeld == open) return; _pauseHeld = open; _sandbox?.SetModalPaused("personal-stash", open);
    }
    // Wrapped Godot controls settle over several container passes after opening or resizing.
    // Bound reconciliation to those changes instead of traversing layout every idle frame.
    private void RequestLayout() { _layoutFrames = 4; UpdateLayout(); }
    private void UpdateLayout()
    {
        var viewport = GetViewport().GetVisibleRect().Size; var size = new Vector2(Math.Min(1180, viewport.X - 32), Math.Min(760, viewport.Y - 32));
        if (_panel.Size != size) { _panel.Size = size; _panel.QueueSort(); _column.QueueSort(); }
        _panel.Position = (viewport - size) / 2; var content = size - new Vector2(28, 28);
        if (_column.Size != content) { _column.Size = content; _column.QueueSort(); }
        if (_body.Size.X != content.X || _tabs.Size.X != content.X)
        { _column.Notification((int)Container.NotificationSortChildren); _body.Notification((int)Container.NotificationSortChildren); _tabs.Notification((int)Container.NotificationSortChildren); }
    }
    private static bool SameView(PersonalStashDisplay a, PersonalStashDisplay b)
    {
        if (a.SessionKey != b.SessionKey || a.Discipline != b.Discipline || a.AtChest != b.AtChest || a.CanTransfer != b.CanTransfer || a.CanApproach != b.CanApproach ||
            a.AccessReason != b.AccessReason || a.BackpackCapacity != b.BackpackCapacity || a.Notice != b.Notice || !a.Tabs.SequenceEqual(b.Tabs) || a.Items.Length != b.Items.Length) return false;
        for (int i = 0; i < a.Items.Length; i++)
        {
            var x = a.Items[i]; var y = b.Items[i];
            if (x.Id != y.Id || x.DefinitionId != y.DefinitionId || x.Slot != y.Slot || x.Rarity != y.Rarity || x.Name != y.Name || x.Summary != y.Summary || x.Details != y.Details ||
                x.Favorite != y.Favorite || x.Locked != y.Locked || x.Equipped != y.Equipped || x.TabId != y.TabId || x.DepositReason != y.DepositReason || x.RetrieveReason != y.RetrieveReason || !x.References.SequenceEqual(y.References)) return false;
        }
        return true;
    }
    private static string SafeName(string id) => new(id.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    private static void Clear(Node node) { foreach (var child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }
    private static VBoxContainer Stack(int gap) { var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", gap); return box; }
    private static Label Text(string text, int size) { var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore }; label.AddThemeFontSizeOverride("font_size", size); return label; }
    private static Button Button(string text, string name, Action action) { var button = new Button { Name = name, Text = text, CustomMinimumSize = new(0, 32), SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart }; button.AddThemeFontSizeOverride("font_size", 12); button.Pressed += action; return button; }
    private static GearDragCard Card(string name) { var card = new GearDragCard { Name = name, SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart }; card.AddThemeFontSizeOverride("font_size", 12); return card; }
    private static OptionButton Filter(HBoxContainer parent, string name, string[] choices)
    { var option = new OptionButton { Name = name, SizeFlagsHorizontal = SizeFlags.ExpandFill, FitToLongestItem = false }; option.AddThemeFontSizeOverride("font_size", 11); foreach (string value in choices) option.AddItem(value); parent.AddChild(option); return option; }
    private static StyleBoxFlat Surface() => new() { BgColor = new("131f27"), BorderColor = new("a9956b"), BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 14, ContentMarginBottom = 14 };
}
