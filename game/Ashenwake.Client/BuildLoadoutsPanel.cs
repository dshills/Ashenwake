using System.Globalization;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Read-only build comparisons; complete loadout transactions belong to ProductionSession.</summary>
public partial class BuildLoadoutsPanel : VBoxContainer
{
    // The offline director completes this command and reports its result synchronously.
    public event Action<ProductionAction, string, string>? Requested;
    public event Action? CloseRequested;
    public event Action? TrainingRequested;
    public event Action? StashRequested;
    public string SelectedId { get; private set; } = "loadout.1";
    private IReadOnlyList<BuildLoadoutView> _loadouts = [];
    private IReadOnlyDictionary<string, string> _fragments = new Dictionary<string, string>();
    private IReadOnlyDictionary<int, string> _manifestations = new Dictionary<int, string>();
    private Func<string, BuildLoadoutPreview>? _preview;
    private BuildLoadoutPreview? _shownPreview;
    private ProgressionSnapshot? _state;
    private ProgressionDefinition? _definition;
    private IReadOnlyDictionary<string, string> _contentNames = new Dictionary<string, string>();
    private string _context = "", _anatomyContext = "", _pendingContext = "", _pendingPreview = "";
    private string _restriction = "", _displayedSavedName = "", _pendingName = "", _submittedName = "";
    private ProductionAction? _pending;
    private ProductionAction _submittedAction;
    private bool _busy, _pauseHeld;
    private Sandbox? _sandbox;
    private object? _seenSession;
    private OptionButton _selector = null!;
    private LineEdit _name = null!;
    private Label _description = null!, _cost = null!, _status = null!, _result = null!;
    private VBoxContainer _comparison = null!;
    private ScrollContainer _scroll = null!;
    private Button _save = null!, _rename = null!, _delete = null!, _apply = null!, _practice = null!, _stash = null!;
    private ConfirmationDialog _confirmation = null!;

    public override void _Ready()
    {
        Name = "BuildLoadoutsPanel";
        CustomMinimumSize = new(320, 450);
        SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 6);
        AddChild(Caption("COMPLETE BUILD LOADOUTS", 18, "dec99a"));
        AddChild(Caption("Eight named builds. Inspect anywhere; visit Mara in Greyhaven to save or switch.", 12));
        _selector = new OptionButton { Name = "BuildLoadoutSelector", FitToLongestItem = false, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 32) };
        _selector.ItemSelected += index => SelectLoadout("loadout." + (index + 1)); AddChild(_selector);
        _name = new LineEdit { Name = "BuildLoadoutName", PlaceholderText = "Name this build", MaxLength = 32, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 32) };
        _name.TextChanged += _ => { CancelConfirmation(); UpdateButtons(); };
        _name.TextSubmitted += _ => _name.ReleaseFocus(); AddChild(_name);
        _description = Caption("", 12); AddChild(_description);
        _scroll = new ScrollContainer { Name = "BuildLoadoutPreviewScroll", CustomMinimumSize = new(0, 130), SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(_scroll);
        _comparison = new VBoxContainer { Name = "BuildLoadoutPreview", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _comparison.AddThemeConstantOverride("separation", 5); _scroll.AddChild(_comparison);
        _cost = Caption("", 12, "dec99a"); _cost.Name = "BuildLoadoutCost"; AddChild(_cost);
        _status = Caption("", 12, "eeb196"); _status.Name = "BuildLoadoutStatus"; _status.MaxLinesVisible = 3; _status.MouseFilter = MouseFilterEnum.Stop; AddChild(_status);
        var actions = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        actions.AddThemeConstantOverride("h_separation", 8); actions.AddThemeConstantOverride("v_separation", 6); AddChild(actions);
        _save = ActionButton("BuildLoadoutSave", "Save current build", () => Begin(ProductionAction.SaveBuildLoadout)); actions.AddChild(_save);
        _rename = ActionButton("BuildLoadoutRename", "Rename saved build", () => Begin(ProductionAction.RenameBuildLoadout)); actions.AddChild(_rename);
        _apply = ActionButton("BuildLoadoutApply", "Review and apply…", () => Begin(ProductionAction.ApplyBuildLoadout)); actions.AddChild(_apply);
        _delete = ActionButton("BuildLoadoutDelete", "Delete saved build…", () => Begin(ProductionAction.DeleteBuildLoadout)); actions.AddChild(_delete);
        _result = Caption("", 12); _result.Name = "BuildLoadoutResult"; _result.MaxLinesVisible = 2; _result.MouseFilter = MouseFilterEnum.Stop; AddChild(_result);
        _stash = ActionButton("BuildLoadoutStash", "Open stash · retrieve saved equipment", () => { CancelInteraction(); StashRequested?.Invoke(); });
        _stash.Visible = false; AddChild(_stash);
        var navigation = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; navigation.AddThemeConstantOverride("separation", 8); AddChild(navigation);
        _practice = ActionButton("BuildLoadoutPractice", "Practice current build", () => { CancelInteraction(); TrainingRequested?.Invoke(); });
        _practice.TooltipText = "Test your currently equipped build in the training grounds. A selected saved loadout must be applied first."; navigation.AddChild(_practice);
        navigation.AddChild(ActionButton("BuildLoadoutBack", "Back to gear", () => CloseRequested?.Invoke()));
        _confirmation = new ConfirmationDialog { Name = "BuildLoadoutConfirmation", Title = "Review build loadout", DialogAutowrap = true, Exclusive = true, CancelButtonText = "Cancel" };
        AddChild(_confirmation); _confirmation.Canceled += CancelConfirmation; _confirmation.Confirmed += Confirm;
        for (Node? parent = GetParent(); parent is not null; parent = parent.GetParent())
            if (parent is Sandbox sandbox) { _sandbox = sandbox; break; }
        VisibilityChanged += () => { if (!IsVisibleInTree()) CancelInteraction(); SynchronizePause(); };
    }

    public void SetCurrentAnatomy(IReadOnlyDictionary<string, string> fragments, IReadOnlyDictionary<int, string> manifestations)
    {
        string context = JsonData.Hash(new { fragments, manifestations });
        if (context != _anatomyContext) { CancelConfirmation(); _context = ""; }
        _anatomyContext = context;
        _fragments = new Dictionary<string, string>(fragments);
        _manifestations = new Dictionary<int, string>(manifestations);
    }

    public void SetContentNames(IReadOnlyDictionary<string, string> names) => _contentNames = new Dictionary<string, string>(names);

    public void SetView(ProgressionSnapshot state, ProgressionDefinition definition, IReadOnlyList<BuildLoadoutView> loadouts,
        Func<string, BuildLoadoutPreview>? preview, bool canManage, long revision)
    {
        string context = state.Character.CharacterId + ":" + revision + ":" + canManage + ":" + JsonData.Hash(loadouts) + ":" + _anatomyContext + ":" + JsonData.Hash(state.Character.Stash);
        bool changed = _context != context || !ReferenceEquals(_seenSession, _sandbox?.Session);
        if (changed) CancelConfirmation();
        _context = context; _state = state; _definition = definition; _loadouts = loadouts; _preview = preview;
        _restriction = canManage ? "" : "Stand near Mara in Greyhaven while alive to manage complete build loadouts.";
        string savedName = Current()?.Name ?? "";
        if (_displayedSavedName != savedName) { _displayedSavedName = savedName; _name.Text = savedName; }
        if (changed) { RefreshSelector(); Render(); }
        SynchronizePause();
    }

    public void SelectLoadout(string id)
    {
        if (!Enumerable.Range(1, 8).Any(i => id == "loadout." + i)) return;
        CancelConfirmation(); SelectedId = id;
        _displayedSavedName = Current()?.Name ?? ""; _name.Text = _displayedSavedName; _result.Text = "";
        _scroll.ScrollVertical = 0; RefreshSelector(); Render();
    }

    private BuildLoadoutView? Current() => _loadouts.FirstOrDefault(p => p.Id == SelectedId);

    private void RefreshSelector()
    {
        _selector.Clear();
        for (int i = 1; i <= 8; i++)
        {
            var saved = _loadouts.FirstOrDefault(p => p.Id == "loadout." + i);
            _selector.AddItem($"{i} · {saved?.Name ?? "Empty loadout"}");
        }
        _selector.Select(int.Parse(SelectedId.AsSpan(8), CultureInfo.InvariantCulture) - 1);
    }

    private void Render()
    {
        if (_state is null) return;
        foreach (var child in _comparison.GetChildren()) { _comparison.RemoveChild(child); child.QueueFree(); }
        var saved = Current();
        _shownPreview = saved is null ? null : _preview?.Invoke(SelectedId);
        _description.Text = saved is null ? $"CURRENT BUILD · {_state.Character.Discipline}\nSaving records every slot, including empty slots." :
            $"CURRENT → {saved.Name.ToUpperInvariant()} · {saved.Discipline}\nApplying restores the complete saved build.";
        if (_shownPreview is { Requirements.Count: > 0 })
        {
            Heading("REQUIREMENTS");
            foreach (string requirement in _shownPreview.Requirements) _comparison.AddChild(Caption(requirement, 12, "eeb196"));
        }
        Heading("EQUIPMENT");
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            long before = _state.Character.Equipment.GetValueOrDefault(slot);
            long after = saved?.Equipment.GetValueOrDefault(slot) ?? before;
            Row(SlotName(slot), ItemName(before), ItemName(after), saved is not null, before == after, after != 0 && (!_state.Character.Items.Any(i => i.Id == after) || CharacterStash.IsStored(_state.Character, after)));
        }
        Heading("DIVINE ANATOMY");
        foreach (var slot in Enum.GetValues<AnatomySlot>())
        {
            string before = _fragments.GetValueOrDefault(slot.ToString(), "");
            string after = saved?.Fragments.GetValueOrDefault(slot.ToString(), "") ?? before;
            Row(slot.ToString(), ContentName(before, "Empty"), ContentName(after, "Empty"), saved is not null, before == after, after.Length > 0 && !_state.Character.OwnedFragments.Contains(after));
        }
        Heading("MANIFESTATIONS");
        var manifestationKeys = _manifestations.Keys.Concat(saved?.Manifestations.Keys ?? []).Distinct().Order().ToArray();
        if (manifestationKeys.Length == 0) _comparison.AddChild(Caption("None selected", 12));
        foreach (int threshold in manifestationKeys)
        {
            string before = _manifestations.GetValueOrDefault(threshold, "");
            string after = saved?.Manifestations.GetValueOrDefault(threshold, "") ?? before;
            Row("Resonance " + threshold, ContentName(before, "None"), ContentName(after, "None"), saved is not null, before == after);
        }
        Heading("SKILL MUTATIONS");
        var skillIds = (_definition?.Skills.Where(s => s.Discipline == (saved?.Discipline ?? _state.Character.Discipline)).Select(s => s.Id) ?? [])
            .Concat(_state.Character.SelectedMutations.Keys).Concat(saved?.Mutations.Keys ?? []).Distinct().Order(StringComparer.Ordinal);
        foreach (string skill in skillIds)
        {
            string before = _state.Character.SelectedMutations.GetValueOrDefault(skill, "");
            string after = saved?.Mutations.GetValueOrDefault(skill, "") ?? before;
            Row(ContentName(skill), ContentName(before, "Base skill"), ContentName(after, "Base skill"), saved is not null, before == after);
        }
        Heading("PASSIVE ALLOCATIONS");
        foreach (string passive in new[] { "Offense", "Defense", "Resource" })
        {
            int before = _state.Character.Passives.GetValueOrDefault(passive);
            int after = saved?.Passives.GetValueOrDefault(passive) ?? before;
            Row(passive, "Rank " + before, "Rank " + after, saved is not null, before == after);
        }
        UpdateButtons();
    }

    private string ItemName(long id)
    {
        if (id == 0) return "Empty";
        var item = _state!.Character.Items.FirstOrDefault(i => i.Id == id);
        return item is null ? $"Missing item #{id}" : EquipmentNames.For(item.DefinitionId) + $" · #{id}" +
            (CharacterStash.IsStored(_state.Character, id) ? " · Stored in tab “" + CharacterStash.TabName(_state.Character, CharacterStash.TabForItem(_state.Character, id)) + "”" : "");
    }

    private void Heading(string text) => _comparison.AddChild(Caption(text, 12, "dec99a"));
    private void Row(string category, string before, string after, bool comparison, bool same, bool missing = false)
    {
        string text = category + " · " + (comparison && !same ? before + " → " + after : after + (comparison ? "  ✓" : ""));
        var label = Caption(text, 12, missing ? "eeb196" : comparison && !same ? "b9dce4" : "c3ced5");
        label.TooltipText = text; label.MouseFilter = MouseFilterEnum.Stop; _comparison.AddChild(label);
    }

    private void UpdateButtons()
    {
        if (_save is null || _state is null) return;
        var saved = Current();
        bool named = !string.IsNullOrWhiteSpace(_name.Text), blocked = _restriction.Length > 0;
        _save.Text = saved is null ? "Save current build" : "Replace with current build…";
        _save.Disabled = _busy || blocked || !named || _preview is null;
        _rename.Disabled = _busy || blocked || saved is null || !named || saved.Name == _name.Text.Trim();
        _delete.Disabled = _busy || blocked || saved is null;
        _apply.Disabled = _busy || blocked || _shownPreview?.Success != true;
        _practice.Disabled = _busy || blocked;
        _stash.Visible = saved?.Equipment.Values.Any(id => CharacterStash.IsStored(_state.Character, id)) == true;
        _stash.Disabled = _busy;
        _cost.Text = _shownPreview is null ? $"Saving is free · {_state.Character.Materials:N0} materials owned" :
            $"Apply cost: {_shownPreview.MaterialCost:N0} materials · Owned: {_state.Character.Materials:N0}\nPassive refund {_shownPreview.RespecCost:N0} · Fragment removal {_shownPreview.FragmentRemovalCost:N0}";
        _status.Text = blocked ? _restriction : _shownPreview is { Success: false } ? _shownPreview.Reason :
            saved is null ? named ? "Ready to save the current build shown above." : "Enter a name to save your current build." :
            "Review every slot above. Applying changes the whole build together.";
        _status.TooltipText = _status.Text;
        _apply.TooltipText = _shownPreview is { Success: false } ? _shownPreview.Reason : "Review the exact material cost and confirm before applying this complete build.";
    }

    private void Begin(ProductionAction action)
    {
        if (_busy || !IsVisibleInTree() || _state is null || _restriction.Length > 0) return;
        UpdateButtons();
        var button = action switch { ProductionAction.SaveBuildLoadout => _save, ProductionAction.RenameBuildLoadout => _rename, ProductionAction.DeleteBuildLoadout => _delete, _ => _apply };
        if (button.Disabled) return;
        if (action == ProductionAction.RenameBuildLoadout || action == ProductionAction.SaveBuildLoadout && Current() is null) { Submit(action, _name.Text.Trim()); return; }
        _pending = action; _pendingContext = _context; _pendingName = _name.Text.Trim();
        bool deleting = action == ProductionAction.DeleteBuildLoadout, applying = action == ProductionAction.ApplyBuildLoadout;
        _confirmation.Title = deleting ? "Delete saved build?" : applying ? "Apply complete build?" : "Replace saved build?";
        _confirmation.OkButtonText = deleting ? "Delete saved build" : applying ? "Apply complete build" : "Replace saved build";
        if (applying)
        {
            var preview = _preview?.Invoke(SelectedId);
            if (preview?.Success != true) { CancelConfirmation(); Render(); return; }
            _pendingPreview = JsonData.Hash(preview);
            _confirmation.DialogText = $"Apply “{Current()!.Name}” ({Current()!.Discipline})?\n\nEquipment, fragments, manifestations, mutations and passive ranks will match the saved build shown above. Empty saved slots are cleared.\n\nTotal: {preview.MaterialCost:N0} materials\nPassive refund: {preview.RespecCost:N0}\nFragment removal: {preview.FragmentRemovalCost:N0}\nMaterials remaining: {_state.Character.Materials - preview.MaterialCost:N0}\n\nAll requirements are checked again when you confirm. The build is applied together or stays unchanged.";
        }
        else _confirmation.DialogText = deleting ? $"Delete “{Current()!.Name}”?\n\nThis removes the saved loadout. Your current build, items and materials stay unchanged." :
            $"Replace “{Current()!.Name}” with your current {_state.Character.Discipline} build, named “{_pendingName}”?\n\nThis overwrites its saved equipment, fragments, manifestations, mutations and passive allocations. Your current build stays unchanged.\n\nSaving costs no materials.";
        _confirmation.PopupCentered(new(Math.Min(560, (int)GetViewportRect().Size.X - 40), Math.Min(applying ? 420 : 290, (int)GetViewportRect().Size.Y - 60)));
        _confirmation.GetCancelButton().GrabFocus();
    }

    private void Confirm()
    {
        var action = _pending; string context = _pendingContext, name = _pendingName, previewKey = _pendingPreview;
        CancelConfirmation();
        if (action is null || context != _context || !ReferenceEquals(_seenSession, _sandbox?.Session) || !IsVisibleInTree() || _restriction.Length > 0) return;
        if (action == ProductionAction.ApplyBuildLoadout && (_preview is null || previewKey != JsonData.Hash(_preview(SelectedId)))) { Render(); return; }
        Submit(action.Value, name);
    }

    private void Submit(ProductionAction action, string name)
    {
        if (_busy || !IsVisibleInTree() || _restriction.Length > 0) return;
        if (action == ProductionAction.ApplyBuildLoadout && _preview?.Invoke(SelectedId).Success != true) { Render(); return; }
        _submittedAction = action; _submittedName = name;
        _busy = true; UpdateButtons();
        try { Requested?.Invoke(action, SelectedId, name); }
        finally { if (_busy) ReportResult(false, "The build loadout change did not complete."); }
    }

    public void ReportResult(bool success, string reason)
    {
        if (!_busy) return;
        _busy = false; _result.Text = success ? _submittedAction switch
        {
            ProductionAction.SaveBuildLoadout => $"Saved “{_submittedName}” with your current build.",
            ProductionAction.RenameBuildLoadout => $"Renamed the build to “{_submittedName}”.",
            ProductionAction.DeleteBuildLoadout => "Deleted the saved loadout. Your current build is preserved.",
            _ => $"Applied “{Current()?.Name ?? "saved build"}”. Practice it in the training grounds."
        } : "Loadout unchanged: " + reason;
        _result.TooltipText = _result.Text; _result.Modulate = new(success ? "9eddb4" : "eeb196");
        if (success) _name.Text = Current()?.Name ?? "";
        Render();
    }

    private void CancelConfirmation() { _pending = null; _pendingContext = ""; _pendingName = ""; _pendingPreview = ""; _confirmation?.Hide(); }
    public void CancelInteraction() { CancelConfirmation(); if (_name?.HasFocus() == true) _name.ReleaseFocus(); }
    public void SynchronizePause()
    {
        if (_sandbox is null || !GodotObject.IsInstanceValid(_sandbox)) return;
        if (!ReferenceEquals(_seenSession, _sandbox.Session))
        {
            _seenSession = _sandbox.Session; CancelInteraction();
            if (_result is not null) _result.Text = "";
            if (_name is not null) { _displayedSavedName = Current()?.Name ?? ""; _name.Text = _displayedSavedName; }
        }
        bool visible = IsVisibleInTree();
        if (visible || _pauseHeld) _sandbox.SetModalPaused("build-loadouts", visible);
        _pauseHeld = visible;
    }
    public override void _Notification(int what) { if (what == NotificationApplicationFocusOut && IsInsideTree()) CancelInteraction(); }
    public override void _ExitTree()
    {
        CancelInteraction();
        if (_pauseHeld && _sandbox is not null && GodotObject.IsInstanceValid(_sandbox)) _sandbox.SetModalPaused("build-loadouts", false);
    }
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree() || _confirmation.Visible) return;
        if (input.IsActionPressed("ui_cancel")) { CloseRequested?.Invoke(); GetViewport().SetInputAsHandled(); }
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (IsVisibleInTree() && input is InputEventKey or InputEventJoypadButton) GetViewport().SetInputAsHandled();
    }

    private string ContentName(string id, string empty = "None")
    {
        if (id.Length == 0) return empty;
        if (_contentNames.TryGetValue(id, out string? authoredName)) return authoredName;
        if (_definition?.Strings.TryGetValue(id + ".name", out string? name) == true) return name;
        string text = id[(id.LastIndexOf('.') + 1)..].Replace('_', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text);
    }
    private static string SlotName(EquipmentSlot slot) => slot switch { EquipmentSlot.MainHand => "Weapon", EquipmentSlot.OffHand => "Off hand", EquipmentSlot.Ring1 => "Ring 1", EquipmentSlot.Ring2 => "Ring 2", _ => slot.ToString() };
    private static Button ActionButton(string name, string text, Action action)
    {
        var button = new Button { Name = name, Text = text, CustomMinimumSize = new(0, 32), SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        button.AddThemeFontSizeOverride("font_size", 12); button.Pressed += action; return button;
    }
    private static Label Caption(string text, int size = 13, string color = "c3ced5")
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill, Modulate = new(color) };
        label.AddThemeFontSizeOverride("font_size", size); return label;
    }
}
