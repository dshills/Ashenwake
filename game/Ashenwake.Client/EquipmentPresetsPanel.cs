using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Named equipment previews. Only ProductionSession may validate or mutate a saved set.</summary>
public partial class EquipmentPresetsPanel : VBoxContainer
{
    public event Action<ProductionAction, string, string>? Requested;
    public event Action? CloseRequested;
    public string SelectedId { get; private set; } = "preset.1";
    private IReadOnlyList<EquipmentPresetView> _presets = [];
    private Func<string, EquipmentPresetPreview>? _preview;
    private ProgressionSnapshot? _state;
    private ProgressionDefinition? _definition;
    private string _context = "", _pendingContext = "", _restriction = "", _displayedSavedName = "";
    private ProductionAction? _pending;
    private ProductionAction _submittedAction;
    private string _submittedName = "";
    private string _pendingName = "";
    private bool _busy, _pauseHeld;
    private Sandbox? _sandbox;
    private object? _seenSession;
    private OptionButton _selector = null!;
    private LineEdit _name = null!;
    private Label _status = null!, _result = null!, _description = null!;
    private VBoxContainer _equipment = null!;
    private Button _save = null!, _rename = null!, _delete = null!, _apply = null!;
    private ConfirmationDialog _confirmation = null!;

    public override void _Ready()
    {
        Name = "EquipmentPresetsPanel";
        CustomMinimumSize = new(320, 410);
        SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 8);
        AddChild(Caption("EQUIPMENT PRESETS", 19, "dec99a"));
        AddChild(Caption("Keep up to eight named sets of your own equipment. Visit Torren in Greyhaven to save or switch a set.", 12));
        _selector = new OptionButton { Name = "EquipmentPresetSelector", SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 34), FitToLongestItem = false };
        _selector.ItemSelected += index => SelectPreset("preset." + (index + 1)); AddChild(_selector);
        _name = new LineEdit { Name = "EquipmentPresetName", PlaceholderText = "Name this equipment set", MaxLength = 32, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 34) };
        _name.TextChanged += _ => { CancelConfirmation(); UpdateButtons(); };
        _name.TextSubmitted += _ => _name.ReleaseFocus(); AddChild(_name);
        _description = Caption("", 12); AddChild(_description);
        var scroll = new ScrollContainer { Name = "EquipmentPresetPreviewScroll", CustomMinimumSize = new(0, 130), SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        _equipment = new VBoxContainer { Name = "EquipmentPresetPreview", SizeFlagsHorizontal = SizeFlags.ExpandFill }; _equipment.AddThemeConstantOverride("separation", 5); scroll.AddChild(_equipment);
        _status = Caption("", 12, "eeb196"); _status.Name = "EquipmentPresetStatus"; _status.MaxLinesVisible = 3; _status.MouseFilter = MouseFilterEnum.Stop; AddChild(_status);
        var actions = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill }; actions.AddThemeConstantOverride("h_separation", 8); actions.AddThemeConstantOverride("v_separation", 6); AddChild(actions);
        _save = ActionButton("EquipmentPresetSave", "Save current gear", () => Begin(ProductionAction.SaveEquipmentPreset)); actions.AddChild(_save);
        _rename = ActionButton("EquipmentPresetRename", "Rename saved set", () => Begin(ProductionAction.RenameEquipmentPreset)); actions.AddChild(_rename);
        _apply = ActionButton("EquipmentPresetApply", "Equip saved set", () => Begin(ProductionAction.ApplyEquipmentPreset)); actions.AddChild(_apply);
        _delete = ActionButton("EquipmentPresetDelete", "Delete saved set…", () => Begin(ProductionAction.DeleteEquipmentPreset)); actions.AddChild(_delete);
        _result = Caption("", 12); _result.Name = "EquipmentPresetResult"; _result.MaxLinesVisible = 2; AddChild(_result);
        var back = ActionButton("EquipmentPresetBack", "Back to gear", () => CloseRequested?.Invoke()); AddChild(back);
        _confirmation = new ConfirmationDialog { Name = "EquipmentPresetConfirmation", Title = "Review equipment preset", DialogAutowrap = true, Exclusive = true, CancelButtonText = "Cancel" };
        AddChild(_confirmation); _confirmation.Canceled += CancelConfirmation;
        _confirmation.Confirmed += Confirm;
        for (Node? parent = GetParent(); parent is not null; parent = parent.GetParent())
            if (parent is Sandbox sandbox) { _sandbox = sandbox; break; }
        VisibilityChanged += () => { if (!IsVisibleInTree()) CancelInteraction(); SynchronizePause(); };
    }

    public void SetView(ProgressionSnapshot state, ProgressionDefinition definition, IReadOnlyList<EquipmentPresetView> presets,
        Func<string, EquipmentPresetPreview>? preview, bool canChangeGear, long revision)
    {
        string context = state.Character.CharacterId + ":" + revision + ":" + canChangeGear + ":" + JsonData.Hash(presets);
        bool changed = _context != context;
        if (changed) CancelConfirmation();
        _context = context; _state = state; _definition = definition; _presets = presets; _preview = preview;
        _restriction = canChangeGear ? "" : "Stand near Torren in Greyhaven while alive to change equipment presets.";
        // Rendering a fresh authoritative snapshot must not replace a name the player is typing.
        string savedName = Current()?.Name ?? "";
        if (_displayedSavedName != savedName) { _displayedSavedName = savedName; _name.Text = savedName; }
        if (changed) { RefreshSelector(); Render(); }
        SynchronizePause();
    }

    public void SelectPreset(string id)
    {
        if (!Enumerable.Range(1, 8).Any(i => id == "preset." + i)) return;
        CancelConfirmation(); SelectedId = id;
        _displayedSavedName = Current()?.Name ?? ""; _name.Text = _displayedSavedName; _result.Text = "";
        RefreshSelector(); Render();
    }

    private EquipmentPresetView? Current() => _presets.FirstOrDefault(p => p.Id == SelectedId);

    private void RefreshSelector()
    {
        _selector.Clear();
        for (int i = 1; i <= 8; i++)
        {
            var preset = _presets.FirstOrDefault(p => p.Id == "preset." + i);
            _selector.AddItem($"{i} · {preset?.Name ?? "Empty preset"}");
        }
        _selector.Select(int.Parse(SelectedId.AsSpan(7)) - 1);
    }

    private void Render()
    {
        if (_state is null) return;
        foreach (var child in _equipment.GetChildren()) { _equipment.RemoveChild(child); child.QueueFree(); }
        var saved = Current();
        var preview = saved is null ? null : _preview?.Invoke(SelectedId);
        _description.Text = saved is null ? "CURRENT GEAR · saving records every slot, including empty slots." :
            "SAVED SET · applying replaces every equipment slot. Empty slots will be unequipped; items stay in your inventory.";
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            long target = (saved?.Equipment ?? _state.Character.Equipment).GetValueOrDefault(slot);
            var item = _state.Character.Items.FirstOrDefault(i => i.Id == target);
            string name = target == 0 ? "Empty" : item is null ? $"Missing item #{target}" : EquipmentNames.For(item.DefinitionId) + $" · {item.Rarity} · #{item.Id}";
            long equipped = _state.Character.Equipment.GetValueOrDefault(slot);
            var row = Caption(SlotName(slot) + "  ·  " + name + (saved is not null && equipped == target ? "  ✓" : ""), 12, target != 0 && item is null ? "eeb196" : "ced8dc");
            if (item is not null && _definition is not null) { row.TooltipText = EquipmentDetails.Inspect(item, _definition); row.MouseFilter = MouseFilterEnum.Stop; }
            _equipment.AddChild(row);
        }
        if (preview is not null)
            foreach (var issue in preview.Issues) _equipment.AddChild(Caption(issue.Reason, 12, "eeb196"));
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        if (_save is null) return;
        var saved = Current();
        var preview = saved is null ? null : _preview?.Invoke(SelectedId);
        bool named = !string.IsNullOrWhiteSpace(_name.Text);
        string block = _restriction;
        _save.Text = saved is null ? "Save current gear" : "Replace with current gear…";
        _save.Disabled = _busy || block.Length > 0 || !named || _preview is null;
        _rename.Disabled = _busy || block.Length > 0 || saved is null || !named || saved.Name == _name.Text.Trim();
        _delete.Disabled = _busy || block.Length > 0 || saved is null;
        _apply.Disabled = _busy || block.Length > 0 || preview?.Success != true;
        _status.Text = block.Length > 0 ? block : preview is { Success: false } ? preview.Reason :
            !named ? "Enter a name to save your current gear. You can still inspect a saved set." :
            saved is null ? "Ready to save the current equipment shown above." : "Ready to equip the saved set shown above. No items are consumed.";
        _status.TooltipText = _status.Text;
        _apply.TooltipText = preview is { Success: false } ? preview.Reason : "Equip the complete saved set after reviewing the slots above.";
    }

    private void Begin(ProductionAction action)
    {
        if (_busy || !IsVisibleInTree() || _state is null || _restriction.Length > 0) return;
        UpdateButtons();
        var button = action switch { ProductionAction.SaveEquipmentPreset => _save, ProductionAction.RenameEquipmentPreset => _rename, ProductionAction.DeleteEquipmentPreset => _delete, _ => _apply };
        if (button.Disabled) return;
        if (action is ProductionAction.DeleteEquipmentPreset || action is ProductionAction.SaveEquipmentPreset && Current() is not null)
        {
            _pending = action; _pendingContext = _context; _pendingName = _name.Text.Trim();
            bool deleting = action == ProductionAction.DeleteEquipmentPreset;
            _confirmation.Title = deleting ? "Delete saved equipment set?" : "Replace saved equipment set?";
            _confirmation.OkButtonText = deleting ? "Delete saved set" : "Replace saved set";
            _confirmation.DialogText = deleting ? $"Delete “{Current()!.Name}”?\n\nOnly this preset is removed. Your equipment and items stay unchanged." :
                $"Replace “{Current()!.Name}” with your current equipment, named “{_pendingName}”?\n\nThis replaces the old saved slot choices. Your currently equipped items do not change.\n\nCURRENT GEAR\n" + CurrentEquipmentSummary();
            _confirmation.PopupCentered(new(Math.Min(560, (int)GetViewportRect().Size.X - 40), deleting ? 250 : Math.Min(500, (int)GetViewportRect().Size.Y - 60)));
            _confirmation.GetCancelButton().GrabFocus();
        }
        else Submit(action, _name.Text.Trim());
    }

    private void Confirm()
    {
        var action = _pending; string context = _pendingContext, name = _pendingName;
        CancelConfirmation();
        if (action is not null && context == _context && ReferenceEquals(_seenSession, _sandbox?.Session) && IsVisibleInTree() && _restriction.Length == 0)
            Submit(action.Value, name);
    }

    private void Submit(ProductionAction action, string name)
    {
        if (_busy || !IsVisibleInTree() || _restriction.Length > 0) return;
        if (action == ProductionAction.ApplyEquipmentPreset && _preview?.Invoke(SelectedId).Success != true) { Render(); return; }
        _submittedAction = action; _submittedName = name;
        _busy = true; UpdateButtons(); Requested?.Invoke(action, SelectedId, name);
        if (_busy) ReportResult(false, "The equipment preset change did not complete.");
    }

    public void ReportResult(bool success, string reason)
    {
        if (!_busy) return;
        _busy = false; _result.Text = success ? _submittedAction switch
        {
            ProductionAction.SaveEquipmentPreset => $"Saved “{_submittedName}” with your current equipment.",
            ProductionAction.RenameEquipmentPreset => $"Renamed the set to “{_submittedName}”.",
            ProductionAction.DeleteEquipmentPreset => "Deleted the saved set. All owned equipment is preserved.",
            _ => $"Equipped “{Current()?.Name ?? "saved set"}”."
        } : "Preset unchanged: " + reason;
        _result.TooltipText = _result.Text; _result.Modulate = new(success ? "9eddb4" : "eeb196");
        if (success) _name.Text = Current()?.Name ?? "";
        Render();
    }

    private string CurrentEquipmentSummary() => string.Join("\n", Enum.GetValues<EquipmentSlot>().Select(slot =>
    {
        long id = _state!.Character.Equipment.GetValueOrDefault(slot);
        var item = _state.Character.Items.FirstOrDefault(i => i.Id == id);
        return SlotName(slot) + ": " + (item is null ? "Empty" : EquipmentNames.For(item.DefinitionId));
    }));

    private void CancelConfirmation() { _pending = null; _pendingContext = ""; _pendingName = ""; _confirmation?.Hide(); }
    public void CancelInteraction() { CancelConfirmation(); if (_name?.HasFocus() == true) _name.ReleaseFocus(); }
    public void SynchronizePause()
    {
        if (_sandbox is null || !GodotObject.IsInstanceValid(_sandbox)) return;
        if (!ReferenceEquals(_seenSession, _sandbox.Session)) { _seenSession = _sandbox.Session; CancelInteraction(); if (_result is not null) _result.Text = ""; }
        bool visible = IsVisibleInTree();
        if (visible || _pauseHeld) _sandbox.SetModalPaused("equipment-presets", visible);
        _pauseHeld = visible;
    }
    public override void _Notification(int what) { if (what == NotificationApplicationFocusOut && IsInsideTree()) CancelInteraction(); }
    public override void _ExitTree()
    {
        CancelInteraction();
        if (_pauseHeld && _sandbox is not null && GodotObject.IsInstanceValid(_sandbox)) _sandbox.SetModalPaused("equipment-presets", false);
    }

    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree() || _confirmation.Visible) return;
        if (input.IsActionPressed("ui_cancel")) { CloseRequested?.Invoke(); GetViewport().SetInputAsHandled(); }
        // Keyboard events reach GUI controls first. Any unconsumed shortcut is stopped below.
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (IsVisibleInTree() && input is InputEventKey or InputEventJoypadButton) GetViewport().SetInputAsHandled();
    }

    private static string SlotName(EquipmentSlot slot) => slot switch { EquipmentSlot.MainHand => "Weapon", EquipmentSlot.OffHand => "Off hand", EquipmentSlot.Ring1 => "Ring 1", EquipmentSlot.Ring2 => "Ring 2", _ => slot.ToString() };
    private static Button ActionButton(string name, string text, Action action)
    {
        var button = new Button { Name = name, Text = text, CustomMinimumSize = new(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        button.AddThemeFontSizeOverride("font_size", 12); button.Pressed += action; return button;
    }
    private static Label Caption(string text, int size = 13, string color = "c3ced5")
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill, Modulate = new(color) };
        label.AddThemeFontSizeOverride("font_size", size); return label;
    }
}
