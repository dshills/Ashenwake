using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public sealed record AppearanceWardrobeDisplay(AppearanceWardrobeMemory Memory, ProgressionDefinition Definition,
    CharacterAppearance Equipped, string Notice = "");

/// <summary>View-driven cosmetic draft editor. Only the owner applies or persists requested changes.</summary>
public partial class AppearanceWardrobePanel : Control
{
    public event Action<EquipmentSlot, string>? SelectRequested;
    public event Action<bool>? HelmetHiddenRequested;
    public event Action? ResetRequested;
    public event Action<string>? SaveLookRequested;
    public event Action<string>? ApplyLookRequested;
    public event Action<string>? DeleteLookRequested;
    public event Action? ApplyRequested;
    public event Action<bool>? VisibilityChangedByPlayer;
    public event Action<string>? MenuRequested;

    private static readonly EquipmentSlot[] Slots = [EquipmentSlot.Head, EquipmentSlot.Chest, EquipmentSlot.Shoulders,
        EquipmentSlot.Gloves, EquipmentSlot.Belt, EquipmentSlot.Legs, EquipmentSlot.Boots];
    private AppearanceWardrobeDisplay? _view;
    private PanelContainer _panel = null!;
    private ColorRect _backdrop = null!;
    private CharacterPreview _preview = null!;
    private Control _previewFrame = null!;
    private GridContainer _cards = null!;
    private ScrollContainer _catalog = null!;
    private Label _summary = null!, _slotSummary = null!, _notice = null!, _looksSummary = null!;
    private CheckButton _hideHelmet = null!;
    private LineEdit _lookName = null!;
    private OptionButton _savedLooks = null!;
    private Button _save = null!, _use = null!, _delete = null!, _apply = null!, _cancel = null!, _draftPreview = null!;
    private readonly Dictionary<EquipmentSlot, Button> _slots = [];
    private EquipmentSlot _selectedSlot = EquipmentSlot.Head;
    private string _selectedLook = "";
    private bool _previewingLook;
    private Sandbox? _sandbox;
    private int _oldSibling = -1;
    private Vector2 _layoutViewport = new(-1, -1);
    public bool IsOpen => _panel is { Visible: true };
    public EquipmentSlot SelectedSlot => _selectedSlot;
    public string SelectedLook => _selectedLook;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        for (Node? parent = GetParent(); parent is not null; parent = parent.GetParent())
            if (parent is Sandbox sandbox) { _sandbox = sandbox; break; }
        _backdrop = new ColorRect { Color = new(0, 0, 0, .76f), MouseFilter = MouseFilterEnum.Stop, MouseForcePassScrollEvents = false };
        AddChild(_backdrop); _backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _panel = new PanelContainer { Name = "AppearanceWardrobe", MouseForcePassScrollEvents = false };
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
        var column = Stack(8); _panel.AddChild(column);
        column.AddChild(Text("THE WANDERER’S WARDROBE", 22));
        _summary = Text("", 13); _summary.Name = "WardrobeSummary"; column.AddChild(_summary);
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 16); column.AddChild(body);
        var left = Stack(6); left.CustomMinimumSize = new(270, 0); body.AddChild(left);
        // Keep the viewport's minimum size from expanding the entire modal while labels settle.
        _previewFrame = new Control { Name = "WardrobePreviewFrame", CustomMinimumSize = new(270, 240), ClipContents = true };
        left.AddChild(_previewFrame);
        _preview = new CharacterPreview(); _previewFrame.AddChild(_preview);
        _preview.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); _preview.SetCompact(true, 240);
        _draftPreview = Button("Return to draft preview", "WardrobePreviewDraft", () => { _previewingLook = false; RefreshPreview(); }); left.AddChild(_draftPreview);
        left.AddChild(new HSeparator());
        _looksSummary = Text("SAVED LOOKS · 0 / 8", 13); _looksSummary.Name = "WardrobeLooksSummary"; left.AddChild(_looksSummary);
        _savedLooks = new OptionButton { Name = "WardrobeSavedLooks", CustomMinimumSize = new(270, 32), FitToLongestItem = false, ClipText = true };
        _savedLooks.AddThemeFontSizeOverride("font_size", 13); left.AddChild(_savedLooks);
        _savedLooks.ItemSelected += index =>
        {
            if (_view is null) return;
            _selectedLook = index > 0 && index <= _view.Memory.Looks.Length ? _view.Memory.Looks[(int)index - 1].Name : "";
            _previewingLook = _selectedLook.Length > 0;
            if (_previewingLook) _lookName.Text = _selectedLook;
            RefreshPreview(); RefreshLookActions();
        };
        var lookActions = new HBoxContainer(); left.AddChild(lookActions);
        _use = Button("Use look", "WardrobeUseLook", () => { if (_selectedLook.Length > 0) { _previewingLook = false; ApplyLookRequested?.Invoke(_selectedLook); } }); lookActions.AddChild(_use);
        _delete = Button("Delete", "WardrobeDeleteLook", () => { if (_selectedLook.Length > 0) DeleteLookRequested?.Invoke(_selectedLook); }); lookActions.AddChild(_delete);
        var saveRow = new HBoxContainer(); left.AddChild(saveRow);
        _lookName = new LineEdit
        {
            Name = "WardrobeLookName",
            PlaceholderText = "Name this draft…",
            MaxLength = AppearanceWardrobe.MaximumNameLength,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new(0, 32)
        };
        _lookName.AddThemeFontSizeOverride("font_size", 13); saveRow.AddChild(_lookName);
        _lookName.TextChanged += _ => RefreshLookActions();
        _lookName.TextSubmitted += _ => { if (!_save.Disabled) SaveLookRequested?.Invoke(_lookName.Text.Trim()); };
        _save = Button("Save", "WardrobeSaveLook", () => SaveLookRequested?.Invoke(_lookName.Text.Trim()));
        _save.SizeFlagsHorizontal = SizeFlags.ShrinkEnd; _save.CustomMinimumSize = new(72, 32); saveRow.AddChild(_save);

        var right = Stack(8); right.SizeFlagsHorizontal = SizeFlags.ExpandFill; body.AddChild(right);
        var slotGrid = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill }; right.AddChild(slotGrid);
        foreach (var slot in Slots)
        {
            var chosen = slot;
            var button = Button(slot.ToString(), "WardrobeSlot" + slot, () => SelectSlot(chosen)); button.ToggleMode = true;
            _slots.Add(slot, button); slotGrid.AddChild(button);
        }
        _hideHelmet = new CheckButton { Name = "WardrobeHideHelmet", Text = "Hide helmet", CustomMinimumSize = new(0, 30) };
        _hideHelmet.AddThemeFontSizeOverride("font_size", 13); _hideHelmet.Toggled += hidden => HelmetHiddenRequested?.Invoke(hidden); right.AddChild(_hideHelmet);
        _slotSummary = Text("", 13); _slotSummary.Name = "WardrobeSlotSummary"; _slotSummary.MaxLinesVisible = 5; right.AddChild(_slotSummary);
        _catalog = new ScrollContainer
        {
            Name = "WardrobeCatalog",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new(0, 140),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        right.AddChild(_catalog);
        _cards = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _cards.AddThemeConstantOverride("h_separation", 8); _cards.AddThemeConstantOverride("v_separation", 8); _catalog.AddChild(_cards);
        _notice = Text("", 12); _notice.Name = "WardrobeNotice"; _notice.MaxLinesVisible = 2; column.AddChild(_notice);
        column.AddChild(Text("Changes apply only when you choose Apply look. Armor stats and set bonuses stay equipped.", 12));
        var footer = new HBoxContainer(); column.AddChild(footer);
        footer.AddChild(Button("Reset appearance", "WardrobeReset", () => ResetRequested?.Invoke()));
        _apply = Button("Apply look", "WardrobeApply", () => ApplyRequested?.Invoke()); footer.AddChild(_apply);
        _cancel = Button("Cancel / Close [Esc]", "WardrobeCancel", () => SetOpen(false)); footer.AddChild(_cancel);
        SetOpen(false);
    }

    public void SetView(AppearanceWardrobeDisplay view)
    {
        if (_view is not null && _view.Memory.CharacterId != view.Memory.CharacterId)
        {
            _selectedSlot = EquipmentSlot.Head; _selectedLook = "";
            if (_lookName is not null) _lookName.Text = "";
        }
        _view = view; _previewingLook = false;
        if (!view.Memory.Looks.Any(l => l.Name == _selectedLook)) _selectedLook = "";
        if (IsOpen) Rebuild();
    }
    public void SetOpen(bool open)
    {
        _panel.Visible = _backdrop.Visible = open;
        _sandbox?.SetModalPaused("appearance-wardrobe", open);
        if (open)
        {
            _previewingLook = false;
            _selectedLook = ""; _lookName.Text = "";
            if (_oldSibling < 0) { _oldSibling = GetIndex(); GetParent().MoveChild(this, GetParent().GetChildCount() - 1); }
            _layoutViewport = new(-1, -1); LayoutPanel(); Rebuild(); _cancel.GrabFocus();
        }
        else
        {
            _previewingLook = false;
            if (_oldSibling >= 0) { GetParent().MoveChild(this, Math.Min(_oldSibling, GetParent().GetChildCount() - 1)); _oldSibling = -1; }
        }
        VisibilityChangedByPlayer?.Invoke(open);
    }
    public void SelectSlot(EquipmentSlot slot)
    {
        if (!Slots.Contains(slot)) return;
        _selectedSlot = slot; _catalog.ScrollVertical = 0; _previewingLook = false; Rebuild(); _slots[slot].GrabFocus();
    }
    public override void _Process(double delta) { if (IsOpen) LayoutPanel(); }
    public override void _Input(InputEvent input)
    {
        if (!IsOpen || input is not (InputEventKey or InputEventJoypadButton)) return;
        if (_savedLooks.GetPopup().Visible) return;
        if (input.IsActionPressed("ui_cancel")) { SetOpen(false); GetViewport().SetInputAsHandled(); return; }
        // A named look can contain the same letters as gameplay/menu shortcuts.
        if (_lookName.HasFocus()) return;
        string? menu = new[] { "aw_inventory", "aw_character", "aw_journey", "aw_endgame", "aw_experiment" }.FirstOrDefault(a => input.IsActionPressed(a));
        if (menu is not null) { MenuRequested?.Invoke(menu); GetViewport().SetInputAsHandled(); return; }
        if (!new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev", "aw_save", "aw_load" }.Any(a => input.IsAction(a))) GetViewport().SetInputAsHandled();
    }
    public override void _ExitTree() => _sandbox?.SetModalPaused("appearance-wardrobe", false);

    private void LayoutPanel()
    {
        Vector2 viewport = GetViewportRect().Size;
        Vector2 size = new(Math.Min(1080, viewport.X - 32), Math.Min(730, viewport.Y - 32));
        if (viewport != _layoutViewport)
        {
            _layoutViewport = viewport;
            _cards.Columns = size.X >= 900 ? 3 : 2;
            float previewHeight = Math.Clamp(size.Y - 410, 240, 320);
            _previewFrame.CustomMinimumSize = new(270, previewHeight);
            _preview.SetCompact(true, previewHeight);
        }
        // Container minimums can temporarily grow when rebuilding wrapped labels at zero width.
        // Reassert the intended size after they settle, even when the window has not resized.
        Vector2 position = (viewport - size) / 2;
        if (_panel.Position != position) _panel.Position = position;
        if (_panel.Size != size) _panel.Size = size;
    }
    private void Rebuild()
    {
        if (_view is null || !IsOpen) return;
        var memory = _view.Memory;
        _summary.Text = $"{memory.Unlocks.Length} unlocked appearances · Your armor, your silhouette";
        _notice.Text = _view.Notice; _notice.Visible = _view.Notice.Length > 0;
        _hideHelmet.SetPressedNoSignal(memory.HideHelmet);
        foreach (var pair in _slots) pair.Value.SetPressedNoSignal(pair.Key == _selectedSlot);
        ItemAppearance equipped = At(_view.Equipped, _selectedSlot);
        string selected = memory.Overrides.GetValueOrDefault(_selectedSlot, "");
        _slotSummary.Text = $"{_selectedSlot} · Equipped: {ItemName(equipped.DefinitionId)}\nSelected: {(selected.Length == 0 ? "Equipped appearance" : ItemName(selected))}" +
            (_selectedSlot == EquipmentSlot.Head && memory.HideHelmet ? " · Helmet hidden" : "") +
            (equipped.DefinitionId.Length == 0 ? "\nEquip armor here to display this appearance." : "");
        Clear(_cards);
        AddCard("", "Equipped appearance", Enum.TryParse<ItemRarity>(equipped.Rarity, out var rarity) ? rarity : ItemRarity.Common,
            selected.Length == 0, true, equipped.DefinitionId);
        var unlocked = memory.Unlocks.Where(u => _view.Definition.Items.Any(d => d.Id == u.ItemId && d.Slots.Contains(_selectedSlot)))
            .OrderByDescending(u => u.Rarity).ThenBy(u => EquipmentNames.For(u.ItemId), StringComparer.Ordinal).ToArray();
        foreach (var unlock in unlocked) AddCard(unlock.ItemId, EquipmentNames.For(unlock.ItemId), unlock.Rarity,
            selected == unlock.ItemId, equipped.DefinitionId == unlock.ItemId, unlock.ItemId);
        _savedLooks.Clear(); _savedLooks.AddItem("Choose a saved look…");
        foreach (var look in memory.Looks) _savedLooks.AddItem(look.Name);
        int selectedLook = Array.FindIndex(memory.Looks, l => l.Name == _selectedLook);
        _savedLooks.Select(selectedLook + 1);
        _looksSummary.Text = $"SAVED LOOKS · {memory.Looks.Length} / {AppearanceWardrobe.MaximumLooks}";
        RefreshLookActions(); RefreshPreview();
    }
    private void AddCard(string id, string name, ItemRarity rarity, bool selected, bool equipped, string iconId)
    {
        var button = Button("", id.Length == 0 ? "WardrobeEquipped" : "WardrobeItem_" + id.Replace("item.", ""),
            () => { _previewingLook = false; SelectRequested?.Invoke(_selectedSlot, id); });
        button.CustomMinimumSize = new(120, 112); button.ToggleMode = true; button.SetPressedNoSignal(selected);
        button.TooltipText = name + "\n" + (id.Length == 0 ? "Follow the armor currently equipped in this slot." : rarity + " · Unlocked appearance") +
            (selected ? "\nSelected in draft" : "") + (equipped && id.Length > 0 ? "\nCurrently equipped item" : "") +
            (At(_view!.Equipped, _selectedSlot).DefinitionId.Length == 0 ? "\nEquip armor here to display this appearance." : "");
        _cards.AddChild(button);
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore }; button.AddChild(margin); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 6); margin.AddThemeConstantOverride("margin_right", 6);
        margin.AddThemeConstantOverride("margin_top", 4); margin.AddThemeConstantOverride("margin_bottom", 4);
        var content = Stack(2); content.MouseFilter = MouseFilterEnum.Ignore; margin.AddChild(content);
        var icon = new GearItemIcon { CustomMinimumSize = new(38, 34) };
        icon.Configure(iconId, _selectedSlot, _view!.Equipped.Discipline, iconId.Length > 0 ? rarity : null); content.AddChild(icon);
        var title = Text(name, 12); title.MaxLinesVisible = 2; title.HorizontalAlignment = HorizontalAlignment.Center; title.MouseFilter = MouseFilterEnum.Ignore; content.AddChild(title);
        var subtitle = Text(id.Length == 0 ? "Use current armor" : rarity.ToString(), 11); subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        subtitle.MouseFilter = MouseFilterEnum.Ignore; subtitle.AddThemeColorOverride("font_color", RarityColor(rarity)); content.AddChild(subtitle);
        var mark = Text(selected ? "SELECTED" : equipped && id.Length > 0 ? "EQUIPPED" : "UNLOCKED", 10);
        mark.HorizontalAlignment = HorizontalAlignment.Center; mark.MouseFilter = MouseFilterEnum.Ignore;
        mark.AddThemeColorOverride("font_color", new Color(selected ? "d9c292" : "8fa7ad")); content.AddChild(mark);
    }
    private void RefreshPreview()
    {
        if (_view is null) return;
        var memory = _view.Memory;
        var look = _previewingLook ? memory.Looks.FirstOrDefault(l => l.Name == _selectedLook) : null;
        var previewMemory = look is null ? memory : memory with { Overrides = look.Overrides, HideHelmet = look.HideHelmet };
        _preview.SetAppearance(WardrobeAppearance.Project(_view.Equipped, previewMemory));
        _preview.SetCaption(look is null ? "Draft appearance · Drag to rotate" : $"Preview: {look.Name}\nChoose Use look to edit this draft");
        _draftPreview.Visible = look is not null;
        _apply.Disabled = look is not null;
        RefreshLookActions();
    }
    private void RefreshLookActions()
    {
        if (_view is null || _save is null) return;
        string name = _lookName.Text.Trim();
        bool replacing = _view.Memory.Looks.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        _save.Disabled = name.Length == 0 || name.Any(char.IsControl) || _view.Memory.Looks.Length >= AppearanceWardrobe.MaximumLooks && !replacing || _previewingLook;
        _save.Text = replacing ? "Replace" : "Save";
        _save.TooltipText = replacing ? "Replace this saved look with the current draft. Apply look commits the change." : "Save the current draft as a named look. Apply look commits the change.";
        _use.Disabled = _delete.Disabled = !_view.Memory.Looks.Any(l => l.Name == _selectedLook);
        _delete.TooltipText = "Remove this saved look from the draft. Apply look commits the removal.";
    }
    private static ItemAppearance At(CharacterAppearance appearance, EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Head => appearance.Head,
        EquipmentSlot.Chest => appearance.Chest,
        EquipmentSlot.Shoulders => appearance.Shoulders,
        EquipmentSlot.Gloves => appearance.Gloves,
        EquipmentSlot.Belt => appearance.Belt,
        EquipmentSlot.Legs => appearance.Legs,
        EquipmentSlot.Boots => appearance.Boots,
        _ => ItemAppearance.Empty
    };
    private static string ItemName(string id) => id.Length == 0 ? "Nothing equipped" : EquipmentNames.For(id);
    private static Color RarityColor(ItemRarity rarity) => new(rarity switch
    {
        ItemRarity.Tempered => "7dcbae",
        ItemRarity.Rare => "6cafff",
        ItemRarity.Relic => "b98bf3",
        ItemRarity.Legendary => "ffd172",
        ItemRarity.Godwrought => "ff8559",
        _ => "bdc6c8"
    });
    private static void Clear(Node node) { foreach (var child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }
    private static VBoxContainer Stack(int separation) { var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", separation); return stack; }
    private static Label Text(string text, int size)
    { var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart }; label.AddThemeFontSizeOverride("font_size", size); return label; }
    private static Button Button(string text, string name, Action action)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            CustomMinimumSize = new(0, 32),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        button.AddThemeFontSizeOverride("font_size", 13); button.Pressed += action; return button;
    }
}
