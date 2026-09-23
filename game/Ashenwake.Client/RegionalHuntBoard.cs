using Godot;

namespace Ashenwake.Client;

public sealed record RegionalHuntContractDisplay(string Id, string Name, string Region, string Description,
    bool Unlocked, string Requirement, string[] Counterplay, string Reward, string[] Clues, int CompletedClues,
    bool CanStart, string StartReason = "", string Accent = "ac9465");
public sealed record RegionalHuntRunDisplay(string Id, string Name, string Status, string Objective,
    bool CanAdvance, bool CanClaim, bool CanRetry, bool CanAbandon, bool CanReturn);
public sealed record RegionalHuntBoardDisplay(RegionalHuntContractDisplay[] Contracts, RegionalHuntRunDisplay? Run,
    bool InHub, bool AtBoard, bool Alive, int GroundDrops, long Revision, string Notice = "");

/// <summary>Read-only contract presentation. Requests are validated and executed by the runtime.</summary>
public partial class RegionalHuntBoard : Control
{
    public event Action<string>? HuntRequested, MenuRequested;
    public event Action? AdvanceRequested, ClaimRequested, RetryRequested, AbandonRequested, ReturnRequested, BoardApproachRequested;
    public event Action<bool>? VisibilityChangedByPlayer, ModalChanged;
    private enum RequestKind { Start, Follow, Claim, Retry, Abandon, Return }
    private sealed record Request(RequestKind Kind, string Id = "");
    private RegionalHuntBoardDisplay? _view;
    private PanelContainer _panel = null!;
    private ColorRect _backdrop = null!;
    private HBoxContainer _cards = null!, _actions = null!;
    private VBoxContainer _column = null!, _details = null!;
    private ScrollContainer _scroll = null!;
    private Label _summary = null!, _notice = null!;
    private Button _close = null!;
    private ConfirmationDialog _confirmation = null!;
    private Request? _pending;
    private long _pendingRevision = -1;
    private string _selected = "";
    private Sandbox? _sandbox;
    private int _oldSibling = -1;
    private bool _pauseHeld;
    public bool IsOpen => _panel is { Visible: true };
    public string SelectedContract => _selected;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        for (Node? parent = GetParent(); parent is not null; parent = parent.GetParent())
            if (parent is Sandbox sandbox) { _sandbox = sandbox; break; }
        _backdrop = new ColorRect { Color = new(0, 0, 0, .72f), MouseFilter = MouseFilterEnum.Stop, MouseForcePassScrollEvents = false };
        AddChild(_backdrop); _backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _panel = new PanelContainer { Name = "RegionalHuntBoard", MouseForcePassScrollEvents = false };
        _panel.AddThemeStyleboxOverride("panel", Surface(new("bca16c"), new("111d25"), 16)); AddChild(_panel);
        var column = _column = Stack(10); _panel.AddChild(column);
        var heading = Text("THE GREYHAVEN HUNT BOARD", 23); heading.AddThemeColorOverride("font_color", new("e4c992")); column.AddChild(heading);
        _summary = Text("Regional contracts · Track the signs. Learn the quarry. Claim your bounty.", 13); _summary.Name = "HuntSummary"; column.AddChild(_summary);
        _cards = new HBoxContainer { Name = "HuntContracts" }; _cards.AddThemeConstantOverride("separation", 10); column.AddChild(_cards);
        _scroll = new ScrollContainer { Name = "HuntDetailsScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(_scroll); _details = Stack(9); _details.SizeFlagsHorizontal = SizeFlags.ExpandFill; _scroll.AddChild(_details);
        _notice = Text("", 12); _notice.Name = "HuntNotice"; _notice.MaxLinesVisible = 3; column.AddChild(_notice);
        _actions = new HBoxContainer { Name = "HuntActions" }; _actions.AddThemeConstantOverride("separation", 8); column.AddChild(_actions);
        _close = Button("Close [Esc]", "HuntClose", () => SetOpen(false)); column.AddChild(_close);
        _confirmation = new ConfirmationDialog { Name = "HuntConfirmation", DialogAutowrap = true, CancelButtonText = "Keep inspecting" };
        AddChild(_confirmation); _confirmation.Canceled += CancelConfirmation;
        _confirmation.Confirmed += () =>
        {
            var request = _pending; long revision = _pendingRevision; CancelConfirmation();
            if (request is not null && revision == _view?.Revision && IsOpen && IsVisibleInTree() && Available(request)) Dispatch(request);
        };
        _panel.Visible = _backdrop.Visible = false;
        VisibilityChanged += UpdateModal; _panel.VisibilityChanged += UpdateModal;
        GetViewport().SizeChanged += UpdateLayout;
        SetProcess(true); UpdateLayout();
    }

    public void SetView(RegionalHuntBoardDisplay view)
    {
        bool changed = _view is null || !SamePresentation(_view, view);
        if (_view is not null && (_view.Revision != view.Revision || changed)) CancelConfirmation();
        bool newRun = view.Run is { } run && run.Id != _view?.Run?.Id;
        _view = view;
        if (!changed) return;
        if (newRun && view.Contracts.Any(c => c.Id == view.Run!.Id)) _selected = view.Run!.Id;
        if (!view.Contracts.Any(c => c.Id == _selected)) _selected = view.Contracts.FirstOrDefault(c => c.Unlocked)?.Id ?? view.Contracts.FirstOrDefault()?.Id ?? "";
        if (IsOpen) Rebuild();
    }
    // Runtime projections allocate fresh arrays. Compare their values before touching the controls,
    // preserving keyboard focus, scroll position and confirmation state on unchanged frames.
    private static bool SamePresentation(RegionalHuntBoardDisplay left, RegionalHuntBoardDisplay right)
    {
        if (left.Run != right.Run || left.InHub != right.InHub || left.AtBoard != right.AtBoard ||
            left.Alive != right.Alive || left.GroundDrops != right.GroundDrops || left.Notice != right.Notice ||
            left.Contracts.Length != right.Contracts.Length) return false;
        for (int i = 0; i < left.Contracts.Length; i++)
        {
            var a = left.Contracts[i]; var b = right.Contracts[i];
            if (a.Id != b.Id || a.Name != b.Name || a.Region != b.Region || a.Description != b.Description ||
                a.Unlocked != b.Unlocked || a.Requirement != b.Requirement || a.Reward != b.Reward ||
                a.CompletedClues != b.CompletedClues || a.CanStart != b.CanStart || a.StartReason != b.StartReason ||
                a.Accent != b.Accent || !a.Counterplay.SequenceEqual(b.Counterplay) || !a.Clues.SequenceEqual(b.Clues)) return false;
        }
        return true;
    }
    public void SetOpen(bool open)
    {
        if (!open) CancelConfirmation();
        _panel.Visible = open;
        if (open) { Rebuild(); UpdateLayout(); _close.GrabFocus(); }
        UpdateModal(); VisibilityChangedByPlayer?.Invoke(open);
    }
    public void SelectContract(string id)
    {
        if (_view?.Contracts.Any(c => c.Id == id) != true) return;
        CancelConfirmation(); _selected = id; _scroll.ScrollVertical = 0; Rebuild();
        if (FindChild("HuntContract_" + SafeName(id), true, false) is Button button) button.GrabFocus();
    }
    public void SessionRestored() { CancelConfirmation(); _view = null; _selected = ""; SetOpen(false); }
    public void Notice(string message)
    {
        _notice.Text = message; _notice.TooltipText = message; _notice.Visible = message.Length > 0;
    }
    public override void _Process(double delta) { if (IsOpen) UpdateLayout(); }
    public override void _Input(InputEvent input)
    {
        if (!IsOpen || input is not (InputEventKey or InputEventJoypadButton)) return;
        if (_confirmation.Visible) return;
        if (input.IsActionPressed("ui_cancel")) { SetOpen(false); GetViewport().SetInputAsHandled(); return; }
        string? menu = new[] { "aw_inventory", "aw_character", "aw_journey", "aw_endgame", "aw_experiment" }.FirstOrDefault(a => input.IsActionPressed(a));
        if (menu is not null) { CancelConfirmation(); MenuRequested?.Invoke(menu); GetViewport().SetInputAsHandled(); return; }
        if (!new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev", "aw_save", "aw_load" }.Any(a => input.IsAction(a))) GetViewport().SetInputAsHandled();
    }
    public override void _Notification(int what)
    { if (what == NotificationApplicationFocusOut && IsInsideTree()) CancelConfirmation(); }
    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= UpdateLayout;
        CancelConfirmation();
        if (!_pauseHeld) return;
        _pauseHeld = false;
        var sandbox = _sandbox;
        Callable.From(() =>
        {
            if (sandbox is not null && GodotObject.IsInstanceValid(sandbox) && sandbox.IsInsideTree() && !sandbox.IsQueuedForDeletion())
                sandbox.SetModalPaused("regional-hunt-board", false);
        }).CallDeferred();
    }
    private void UpdateModal()
    {
        if (_panel is null || _backdrop is null) return;
        bool open = IsVisibleInTree() && IsOpen; _backdrop.Visible = open;
        if (open && _oldSibling < 0) { _oldSibling = GetIndex(); GetParent().MoveChild(this, GetParent().GetChildCount() - 1); }
        else if (!open && _oldSibling >= 0) { GetParent().MoveChild(this, Math.Min(_oldSibling, GetParent().GetChildCount() - 1)); _oldSibling = -1; }
        if (_pauseHeld == open) return;
        _pauseHeld = open; _sandbox?.SetModalPaused("regional-hunt-board", open);
        if (!open) CancelConfirmation();
    }
    private void UpdateLayout()
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        var size = new Vector2(Math.Min(1080, viewport.X - 32), Math.Min(730, viewport.Y - 32));
        // Wrapped labels can transiently enlarge a Container before its first width pass.
        // Reconcile after layout settles even when the viewport itself has not changed.
        if (_panel.Size != size)
        {
            _panel.Size = size;
            _panel.QueueSort(); _column.QueueSort(); _cards.QueueSort(); _details.QueueSort(); _actions.QueueSort();
        }
        var position = (viewport - size) / 2;
        if (_panel.Position != position) _panel.Position = position;
        // A reopened modal can retain a queued container layout from its previous width.
        // Keep its content rectangle bounded immediately, then flush only stale layouts.
        var contentSize = size - new Vector2(32, 32);
        if (_column.Size != contentSize) { _column.Size = contentSize; _column.QueueSort(); }
        if (_cards.Size.X != contentSize.X || _scroll.Size.X != contentSize.X || _close.Size.X != contentSize.X)
        {
            _column.Notification((int)Container.NotificationSortChildren);
            _cards.Notification((int)Container.NotificationSortChildren);
            _actions.Notification((int)Container.NotificationSortChildren);
        }
    }
    private void Rebuild()
    {
        if (_view is null || !IsOpen) return;
        string focus = GetViewport().GuiGetFocusOwner()?.Name.ToString() ?? "";
        Clear(_cards); Clear(_details); Clear(_actions); Notice(_view.Notice);
        _summary.Text = _view.Run is { } run ? $"{run.Name} · {run.Status}" : "Regional contracts · Track the signs. Learn the quarry. Claim your bounty.";
        foreach (var contract in _view.Contracts)
        {
            string id = contract.Id;
            var card = Button("", "HuntContract_" + SafeName(id), () => SelectContract(id));
            card.CustomMinimumSize = new(0, 164); card.ToggleMode = true; card.SetPressedNoSignal(id == _selected);
            var accent = Color.FromString(contract.Accent, new("ac9465"));
            card.AddThemeStyleboxOverride("normal", Surface(id == _selected ? accent : new("3d4d53"), id == _selected ? new("223037") : new("16222a"), 10));
            card.AddThemeStyleboxOverride("pressed", Surface(accent, new("27363c"), 10));
            card.TooltipText = contract.Unlocked ? contract.Name : contract.Requirement; _cards.AddChild(card);
            var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore }; card.AddChild(margin); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 12);
            var content = Stack(6); content.MouseFilter = MouseFilterEnum.Ignore; margin.AddChild(content);
            var banner = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; banner.AddThemeConstantOverride("separation", 8); content.AddChild(banner);
            banner.AddChild(new RegionalHuntSeal { Ink = contract.Unlocked ? accent : new("687780") });
            var region = Text(contract.Region.ToUpperInvariant(), 12); region.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            region.AddThemeColorOverride("font_color", accent); banner.AddChild(region);
            var title = Text(contract.Unlocked ? contract.Name : "Uncharted quarry", 18); title.MaxLinesVisible = 3; content.AddChild(title);
            var spacer = new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore }; content.AddChild(spacer);
            string state = _view.Run?.Id == id ? _view.Run.Status.ToUpperInvariant() : contract.Unlocked ? "REPEATABLE CONTRACT" : "NOT YET DISCOVERED";
            var status = Text(state, 11); status.AddThemeColorOverride("font_color", contract.Unlocked ? new("b7d6c7") : new("9ca7ab")); content.AddChild(status);
        }
        var selected = _view.Contracts.FirstOrDefault(c => c.Id == _selected);
        if (selected is not null) RenderDetails(selected);
        if (_view.Run is { } active)
        {
            _details.AddChild(new HSeparator()); _details.AddChild(Text("CURRENT HUNT · " + active.Name, 15));
            var objective = Text(active.Objective, 14); objective.Name = "HuntObjective"; _details.AddChild(objective);
            if (active.CanAdvance) ActionButton("Follow next clue", "HuntFollowClue", new(RequestKind.Follow));
            if (active.CanClaim) ActionButton("Claim bounty", "HuntClaim", new(RequestKind.Claim));
            if (active.CanRetry) ActionButton("Retry hunt", "HuntRetry", new(RequestKind.Retry));
            if (active.CanAbandon) ActionButton("Abandon hunt", "HuntAbandon", new(RequestKind.Abandon));
            if (active.CanReturn) ActionButton("Return to Greyhaven", "HuntReturn", new(RequestKind.Return));
        }
        else if (selected is { Unlocked: true })
        {
            var start = ActionButton("Accept & depart", "HuntStart", new(RequestKind.Start, selected.Id));
            start.TooltipText = selected.StartReason;
            if (!selected.CanStart && selected.StartReason.Length > 0) _details.AddChild(Text(selected.StartReason, 13));
        }
        if (CanApproachBoard())
        {
            var approach = Button("Approach hunt board", "HuntApproach", () =>
            {
                if (!CanApproachBoard()) return;
                SetOpen(false); BoardApproachRequested?.Invoke();
            }); _actions.AddChild(approach);
        }
        if (focus.Length > 0 && FindChild(focus, true, false) is Control control && control.IsVisibleInTree()) control.GrabFocus();
    }
    private void RenderDetails(RegionalHuntContractDisplay contract)
    {
        var title = Text(contract.Unlocked ? contract.Name : "An undiscovered regional contract", 21); title.Name = "HuntDetailTitle"; _details.AddChild(title);
        if (!contract.Unlocked) { _details.AddChild(Text(contract.Requirement, 14)); return; }
        _details.AddChild(Text(contract.Description, 14));
        var reward = Text("BOUNTY · " + contract.Reward, 14); reward.Name = "HuntReward"; reward.AddThemeColorOverride("font_color", new("dfc38d")); _details.AddChild(reward);
        _details.AddChild(Text("READ THE QUARRY", 12));
        foreach (string hint in contract.Counterplay) _details.AddChild(Text("• " + hint, 13));
        if (contract.Clues.Length > 0)
        {
            _details.AddChild(Text($"TRACKING SIGNS · {Math.Clamp(contract.CompletedClues, 0, contract.Clues.Length)}/{contract.Clues.Length}", 12));
            for (int i = 0; i < contract.Clues.Length; i++)
            {
                string prefix = i < contract.CompletedClues ? "✓ " : i == contract.CompletedClues ? "→ " : "· ";
                _details.AddChild(Text(prefix + contract.Clues[i], 13));
            }
        }
    }
    private bool CanApproachBoard() => _view is { InHub: true, AtBoard: false, Alive: true } &&
        (_view.Run is null || _view.Run.Status == "Victory");
    private bool Available(Request request)
    {
        if (_view is null) return false;
        return request.Kind switch
        {
            RequestKind.Start => _view.Alive && _view.InHub && _view.AtBoard && _view.Run is null && _view.Contracts.Any(c => c.Id == request.Id && c.Unlocked && c.CanStart),
            RequestKind.Follow => _view.Alive && _view.Run?.CanAdvance == true,
            RequestKind.Claim => _view.Alive && _view.Run?.CanClaim == true,
            RequestKind.Retry => _view.Run?.CanRetry == true,
            RequestKind.Abandon => _view.Run?.CanAbandon == true,
            RequestKind.Return => _view.Run?.CanReturn == true,
            _ => false
        };
    }
    private Button ActionButton(string text, string name, Request request)
    {
        var button = Button(text, name, () => RequestAction(request)); button.Disabled = !Available(request); _actions.AddChild(button); return button;
    }
    private void RequestAction(Request request)
    {
        if (!IsOpen || !IsVisibleInTree() || !Available(request)) return;
        CancelConfirmation();
        bool confirm = request.Kind is RequestKind.Start or RequestKind.Abandon || request.Kind == RequestKind.Return && _view!.GroundDrops > 0;
        if (!confirm) { Dispatch(request); return; }
        string text;
        if (request.Kind == RequestKind.Start)
        {
            _confirmation.Title = "Accept this hunt?"; _confirmation.OkButtonText = "Accept & depart";
            text = $"Leave Greyhaven to track {_view!.Contracts.Single(c => c.Id == request.Id).Name}?\n\nFollow the signs in the hunting ground, defeat the quarry, then return to this board to claim its bounty.";
        }
        else if (request.Kind == RequestKind.Abandon)
        {
            _confirmation.Title = "Abandon this hunt?"; _confirmation.OkButtonText = "Abandon hunt";
            text = "End the current contract without claiming its bounty? Collected items and earned character progress are preserved.";
        }
        else
        {
            _confirmation.Title = "Leave uncollected loot?"; _confirmation.OkButtonText = "Leave loot & return";
            text = "Return to Greyhaven?";
        }
        if (_view!.GroundDrops > 0) text += $"\n\n{_view.GroundDrops} uncollected ground drops will be left behind.";
        _pending = request; _pendingRevision = _view.Revision; _confirmation.DialogText = text; ModalChanged?.Invoke(true);
        _confirmation.PopupCentered(new(Math.Min(560, (int)GetViewportRect().Size.X - 44), 280));
    }
    private void Dispatch(Request request)
    {
        CancelConfirmation(); SetOpen(false);
        switch (request.Kind)
        {
            case RequestKind.Start: HuntRequested?.Invoke(request.Id); break;
            case RequestKind.Follow: AdvanceRequested?.Invoke(); break;
            case RequestKind.Claim: ClaimRequested?.Invoke(); break;
            case RequestKind.Retry: RetryRequested?.Invoke(); break;
            case RequestKind.Abandon: AbandonRequested?.Invoke(); break;
            case RequestKind.Return: ReturnRequested?.Invoke(); break;
        }
    }
    private void CancelConfirmation()
    {
        bool pending = _pending is not null || _confirmation is { Visible: true };
        _pending = null; _pendingRevision = -1; _confirmation?.Hide();
        if (pending) ModalChanged?.Invoke(false);
    }
    private static string SafeName(string id) => new(id.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    private static void Clear(Node node) { foreach (var child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }
    private static VBoxContainer Stack(int gap) { var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", gap); return stack; }
    private static Label Text(string text, int size)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size); return label;
    }
    private static Button Button(string text, string name, Action action)
    {
        var button = new Button { Name = name, Text = text, CustomMinimumSize = new(0, 36), SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        button.AddThemeFontSizeOverride("font_size", 13); button.Pressed += action; return button;
    }
    private static StyleBoxFlat Surface(Color border, Color background, int padding) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthTop = 1,
        BorderWidthBottom = 1,
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        CornerRadiusTopLeft = 3,
        CornerRadiusTopRight = 3,
        CornerRadiusBottomLeft = 3,
        CornerRadiusBottomRight = 3,
        ContentMarginLeft = padding,
        ContentMarginRight = padding,
        ContentMarginTop = padding,
        ContentMarginBottom = padding
    };
}

/// <summary>A small geometric contract seal; no unreached monster portrait is revealed.</summary>
public partial class RegionalHuntSeal : Control
{
    public Color Ink { get; init; } = new("ac9465");
    public RegionalHuntSeal() { CustomMinimumSize = new(28, 28); MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw()
    {
        var center = new Vector2(14, 14);
        DrawLine(center + new Vector2(0, -12), center + new Vector2(11, 0), Ink, 1, true);
        DrawLine(center + new Vector2(11, 0), center + new Vector2(0, 12), Ink, 1, true);
        DrawLine(center + new Vector2(0, 12), center + new Vector2(-11, 0), Ink, 1, true);
        DrawLine(center + new Vector2(-11, 0), center + new Vector2(0, -12), Ink, 1, true);
        DrawLine(new(9, 7), new(19, 21), Ink, 2, true);
        DrawLine(new(19, 7), new(9, 21), Ink, 2, true);
        DrawLine(new(9, 7), new(9, 12), Ink, 1, true);
        DrawLine(new(19, 7), new(19, 12), Ink, 1, true);
    }
}
