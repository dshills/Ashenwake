using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Inspection is detached; live build changes always return through the owning director.</summary>
public partial class SkillsPanel : VBoxContainer
{
    public event Action<ProgressionBuildRequest>? BuildRequested;
    public event Action? CloseRequested;
    public ProgressionBuildPreview? Preview { get; private set; }
    public string PreviewText { get; private set; } = "";
    public string SelectedSkillId { get; private set; } = "";
    public string SelectedMutationId { get; private set; } = "";
    private ProgressionBuildAction _action = ProgressionBuildAction.SelectMutation;
    private string _passive = "Offense", _stateKey = "", _pendingKey = "";
    private ProgressionSnapshot? _state;
    private ProgressionDefinition _definition = null!;
    private ProgressionContent _content = null!;
    private ProgressionView _view = null!;
    private CombatView _combat = null!;
    private readonly List<Button> _cards = [];
    private readonly List<SkillIcon> _icons = [];
    private readonly List<ProgressBar> _mastery = [];
    private readonly Dictionary<string, Button> _passives = [];
    private Label _heading = null!, _title = null!, _description = null!, _status = null!, _result = null!;
    private VBoxContainer _variants = null!, _comparison = null!;
    private Button _apply = null!, _respec = null!;
    private ConfirmationDialog _confirmation = null!;
    private ProgressionBuildRequest? _pending;
    private ProgressionBuildPreview? _submitted;
    private Sandbox? _sandbox;
    private object? _seenSession;
    private bool _atMara, _alive, _inTown, _busy, _pauseHeld, _selectionInitialized;

    public override void _Ready()
    {
        Name = "SkillsPanel"; CustomMinimumSize = new(680, 470);
        SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 10);
        _heading = Caption("SKILLS & MASTERY", 16); AddChild(_heading);
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 14); AddChild(body);
        var left = new VBoxContainer { CustomMinimumSize = new(280, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = .85f };
        left.AddThemeConstantOverride("separation", 7); body.AddChild(left);
        left.AddChild(Caption("YOUR SIX ABILITIES", 12));
        var grid = new GridContainer { Columns = 2 }; grid.AddThemeConstantOverride("h_separation", 6); grid.AddThemeConstantOverride("v_separation", 6); left.AddChild(grid);
        for (int i = 0; i < 6; i++)
        {
            int index = i;
            var card = new Button { Name = "SkillCard" + i, CustomMinimumSize = new(136, 83), SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart, ToggleMode = true };
            card.AddThemeFontSizeOverride("font_size", 11);
            foreach (string state in new[] { "normal", "hover", "pressed", "focus" }) card.AddThemeStyleboxOverride(state, CardStyle(state == "pressed" ? "304451" : "17262d", state == "pressed" ? "9dcbd5" : "56696d", 39, 9));
            grid.AddChild(card); _cards.Add(card);
            var icon = new SkillIcon { Position = new(7, 21), Size = new(29, 29), MouseFilter = MouseFilterEnum.Ignore }; card.AddChild(icon); _icons.Add(icon);
            var progress = new ProgressBar { Name = "SkillMastery" + i, MinValue = 0, MaxValue = 100, ShowPercentage = false, MouseFilter = MouseFilterEnum.Ignore };
            card.AddChild(progress); progress.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide); progress.OffsetLeft = 7; progress.OffsetRight = -7; progress.OffsetTop = -8; progress.OffsetBottom = -4; _mastery.Add(progress);
            card.Pressed += () => { if (_combat is not null && index < _combat.Skills.Count) SelectSkill(_combat.Skills[index].Id); };
        }
        left.AddChild(Caption("PASSIVE INVESTMENT", 12));
        foreach (string passive in new[] { "Offense", "Defense", "Resource" })
        {
            var button = new Button { Name = "SkillPassive" + passive, CustomMinimumSize = new(0, 34), ToggleMode = true, Text = passive };
            button.AddThemeFontSizeOverride("font_size", 12); button.Pressed += () => SelectPassive(passive); left.AddChild(button); _passives.Add(passive, button);
        }
        _respec = new Button { Name = "SkillRespec", CustomMinimumSize = new(0, 34), Text = "Preview passive refund" };
        _respec.AddThemeFontSizeOverride("font_size", 12); _respec.Pressed += SelectRespec; left.AddChild(_respec);
        var guidance = Caption("Inspect anywhere. Visit Mara in Greyhaven to apply build changes.", 12); left.AddChild(guidance);
        var right = new VBoxContainer { CustomMinimumSize = new(360, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.15f }; right.AddThemeConstantOverride("separation", 8); body.AddChild(right);
        _title = Caption("", 18); right.AddChild(_title);
        var scroll = new ScrollContainer { Name = "SkillDetailsScroll", CustomMinimumSize = new(0, 180), SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; right.AddChild(scroll);
        var details = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; details.AddThemeConstantOverride("separation", 9); scroll.AddChild(details);
        _description = Caption("", 13); details.AddChild(_description);
        _variants = new VBoxContainer { Name = "SkillVariants", SizeFlagsHorizontal = SizeFlags.ExpandFill }; _variants.AddThemeConstantOverride("separation", 6); details.AddChild(_variants);
        _comparison = new VBoxContainer { Name = "SkillPreview", SizeFlagsHorizontal = SizeFlags.ExpandFill }; _comparison.AddThemeConstantOverride("separation", 5); details.AddChild(_comparison);
        _status = Caption("", 12); _status.Name = "SkillStatus"; details.AddChild(_status);
        _apply = new Button { Name = "SkillApplyMutation", CustomMinimumSize = new(0, 40) }; _apply.AddThemeFontSizeOverride("font_size", 13); _apply.Pressed += Commit; right.AddChild(_apply);
        _result = Caption("", 12); _result.Name = "SkillResult"; _result.MaxLinesVisible = 3; _result.MouseFilter = MouseFilterEnum.Stop; right.AddChild(_result);
        _confirmation = new ConfirmationDialog { Name = "SkillConfirmRespec", Title = "Refund passive points", DialogAutowrap = true, OkButtonText = "Pay and refund", CancelButtonText = "Keep my passives" };
        AddChild(_confirmation); _confirmation.Canceled += CancelConfirmation;
        _confirmation.Confirmed += () =>
        {
            var request = _pending; string key = _pendingKey; CancelConfirmation();
            if (request is not null && key == _stateKey && IsVisibleInTree() && Eligibility().Length == 0) Submit(request);
        };
        for (Node? node = GetParent(); node is not null; node = node.GetParent()) if (node is Sandbox sandbox) { _sandbox = sandbox; break; }
        VisibilityChanged += () => { if (!IsVisibleInTree()) CancelConfirmation(); SynchronizePause(); };
    }

    public void SetView(ProgressionSnapshot state, ProgressionDefinition definition, ProgressionView view, CombatView combat, bool inTown, IReadOnlyList<InteractionDisplay> interactions)
    {
        string key = JsonData.Hash(state);
        bool alive = combat.Actors.Any(a => a.Id == 1 && a.Health > 0);
        bool atMara = inTown && interactions.Any(i => i.Id == "service.mara" && i.Distance <= i.Range);
        if (_state is null || _state.Character.ContentHash != state.Character.ContentHash) _content = ProgressionContent.Create(definition);
        if (_stateKey != key || _atMara != atMara || _alive != alive || _inTown != inTown) CancelConfirmation();
        bool reset = !_selectionInitialized || _state?.Character.Discipline != state.Character.Discipline || !combat.Skills.Any(s => s.Id == SelectedSkillId);
        _state = state; _definition = definition; _view = view; _combat = combat; _stateKey = key; _inTown = inTown; _atMara = atMara; _alive = alive;
        if (reset)
        {
            SelectedSkillId = combat.Skills.First().Id; SelectedMutationId = state.Character.SelectedMutations.GetValueOrDefault(SelectedSkillId, "");
            _action = ProgressionBuildAction.SelectMutation; _selectionInitialized = true;
        }
        if (SelectedMutationId.Length > 0 && !combat.Mutations.Any(m => m.Id == SelectedMutationId && m.SkillId == SelectedSkillId)) SelectedMutationId = "";
        _heading.Text = $"{view.Discipline.ToUpperInvariant()} · SKILLS & MASTERY\nLevel {view.Level}  ·  {view.AvailablePassivePoints} unspent points  ·  {view.Materials:N0} materials";
        for (int i = 0; i < _cards.Count; i++)
        {
            bool present = i < combat.Skills.Count; _cards[i].Visible = present; if (!present) continue;
            var skill = combat.Skills[i]; int mastery = state.Character.Mastery.GetValueOrDefault(skill.Id);
            _cards[i].Text = skill.Name + "\n" + (!skill.Available ? "Level 10 unlock" : mastery >= 100 ? "Mastered · " + mastery : $"Mastery {mastery}/100");
            _cards[i].TooltipText = skill.Name + " · select to inspect this ability and its mutations.";
            _cards[i].SetPressedNoSignal(_action == ProgressionBuildAction.SelectMutation && SelectedSkillId == skill.Id);
            _icons[i].SetSkill(skill.Id, view.Discipline, skill.Shape); _mastery[i].Value = Math.Min(100, mastery);
            _mastery[i].Modulate = mastery >= 100 ? new Color("a9dfb9") : Colors.White;
        }
        foreach (var (name, button) in _passives)
        { button.Text = name + " · rank " + state.Character.Passives.GetValueOrDefault(name) + "  +"; button.SetPressedNoSignal(_action == ProgressionBuildAction.AllocatePassive && _passive == name); }
        _respec.Text = $"Preview refund · {definition.RespecCost} materials";
        Render(); SynchronizePause();
    }

    public void SelectSkill(string id)
    {
        if (_combat is null || !_combat.Skills.Any(s => s.Id == id)) return;
        SelectionChanged(); SelectedSkillId = id; SelectedMutationId = _state!.Character.SelectedMutations.GetValueOrDefault(id, ""); _action = ProgressionBuildAction.SelectMutation; RefreshSelection(); Render();
    }
    public void SelectMutation(string id)
    {
        if (id.Length > 0 && !_combat.Mutations.Any(m => m.Id == id && m.SkillId == SelectedSkillId)) return;
        SelectionChanged(); SelectedMutationId = id; _action = ProgressionBuildAction.SelectMutation; RefreshSelection(); Render();
    }
    public void SelectPassive(string name)
    {
        if (!_passives.ContainsKey(name)) return;
        SelectionChanged(); _passive = name; _action = ProgressionBuildAction.AllocatePassive; RefreshSelection(); Render();
    }
    public void SelectRespec() { SelectionChanged(); _action = ProgressionBuildAction.Respec; RefreshSelection(); Render(); }
    private void SelectionChanged() { CancelConfirmation(); _result.Text = ""; _result.TooltipText = ""; }
    private void RefreshSelection()
    {
        for (int i = 0; i < _cards.Count && i < _combat.Skills.Count; i++) _cards[i].SetPressedNoSignal(_action == ProgressionBuildAction.SelectMutation && _combat.Skills[i].Id == SelectedSkillId);
        foreach (var (name, button) in _passives) button.SetPressedNoSignal(_action == ProgressionBuildAction.AllocatePassive && _passive == name);
    }
    private ProgressionBuildRequest Request() => _action switch
    {
        ProgressionBuildAction.AllocatePassive => new(_action, _passive),
        ProgressionBuildAction.SelectMutation => new(_action, SelectedSkillId, SelectedMutationId),
        _ => new(_action)
    };
    private void Render()
    {
        if (_state is null) return;
        Preview = ProgressionSession.Restore(_content, _state).PreviewBuild(Request());
        var focused = GetViewport().GuiGetFocusOwner();
        string focusName = focused is not null && _variants.IsAncestorOf(focused) ? focused.Name.ToString() : "";
        Clear(_variants); Clear(_comparison); PreviewText = "";
        if (_action == ProgressionBuildAction.SelectMutation) ShowSkill(); else ShowPassives();
        if (focusName.Length > 0 && _variants.GetNodeOrNull<Control>(focusName) is { } choice) choice.GrabFocus();
        string reason = Eligibility(); _status.Text = reason.Length > 0 ? reason : "Preview only. Apply when you are ready.";
        _status.Modulate = reason.Length > 0 ? new("eeb196") : new("9eddb4");
        _apply.Name = _action switch { ProgressionBuildAction.SelectMutation => "SkillApplyMutation", ProgressionBuildAction.AllocatePassive => "SkillApplyPassive", _ => "SkillApplyRespec" };
        _apply.Text = _action switch { ProgressionBuildAction.SelectMutation => SelectedMutationId.Length == 0 ? "Restore base ability" : "Apply selected mutation", ProgressionBuildAction.AllocatePassive => "Invest one point · " + _passive, _ => "Review passive refund" };
        _apply.Disabled = _busy || reason.Length > 0;
    }
    private string Eligibility()
    {
        if (_state is null) return "Choose a skill or passive.";
        if (!_alive) return "Cannot change builds while defeated.";
        if (!_inTown) return "Return to Greyhaven to apply build changes.";
        if (!_atMara) return "Visit Mara to apply this change. You can inspect it here.";
        if (Preview?.Success != true) return Preview?.Reason ?? "Choose a build change.";
        if (_action == ProgressionBuildAction.SelectMutation && _state.Character.SelectedMutations.GetValueOrDefault(SelectedSkillId, "") == SelectedMutationId) return "This form is already active.";
        return "";
    }
    private void Commit()
    {
        if (!IsVisibleInTree() || _busy) return;
        Render(); if (Eligibility().Length > 0) return;
        var request = Request();
        if (_action == ProgressionBuildAction.Respec)
        { _pending = request; _pendingKey = _stateKey; _confirmation.DialogText = "Refund all passive points for the displayed material cost. You can reinvest the returned points.\n\n" + PreviewText; _confirmation.PopupCentered(new(Math.Min(560, (int)GetViewportRect().Size.X - 40), 320)); }
        else Submit(request);
    }
    private void Submit(ProgressionBuildRequest request)
    {
        if (_busy || Preview?.Success != true || Eligibility().Length > 0) return;
        _submitted = Preview; _busy = true; _apply.Disabled = true; BuildRequested?.Invoke(request);
        if (_busy) ReportResult(false, "The build change did not complete.");
    }
    public void ReportResult(bool success, string reason)
    {
        if (!_busy) return;
        _busy = false;
        _result.Text = success && _submitted is not null ? ResultSummary(_submitted) : "Change not applied: " + reason;
        _result.TooltipText = _result.Text; _result.Modulate = success ? new("9eddb4") : new("eeb196"); _submitted = null; Render();
    }
    private void CancelConfirmation() { _pending = null; _pendingKey = ""; _confirmation?.Hide(); }
    public void CancelInteraction() => CancelConfirmation();
    public void SynchronizePause()
    {
        if (_sandbox is null || !GodotObject.IsInstanceValid(_sandbox)) return;
        if (!ReferenceEquals(_seenSession, _sandbox.Session))
        { _seenSession = _sandbox.Session; CancelConfirmation(); _result.Text = ""; _result.TooltipText = ""; }
        bool visible = IsVisibleInTree(); if (visible || _pauseHeld) _sandbox.SetModalPaused("skills-panel", visible); _pauseHeld = visible;
    }
    // A native confirmation takes focus from its parent window without leaving the application.
    public override void _Notification(int what) { if (what == NotificationApplicationFocusOut && IsInsideTree()) CancelConfirmation(); }
    public override void _ExitTree() { CancelConfirmation(); if (_pauseHeld && _sandbox is not null && GodotObject.IsInstanceValid(_sandbox)) _sandbox.SetModalPaused("skills-panel", false); }
    private static void Clear(Node node) { foreach (var child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }
    private static Label Caption(string text, int size = 13)
    { var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill }; label.AddThemeFontSizeOverride("font_size", size); return label; }
    private static StyleBoxFlat CardStyle(string background, string border, int left, int bottom) => new() { BgColor = new(background), BorderColor = new(border), BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1, ContentMarginLeft = left, ContentMarginRight = 5, ContentMarginTop = 6, ContentMarginBottom = bottom, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 };
}
