using Godot;

namespace Ashenwake.Client;

public sealed record EndgameChoice(string Id, string Name, string Description);
public sealed record EndgameSigilDisplay(long Id, string Region, int Tier, ulong Seed, string BossFamily,
    string RewardTendency, EndgameChoice[] Modifiers, IReadOnlyDictionary<string, EndgameChoice[]> Replacements, string[] Rooms, string[] Inherited, string[] Skipped, string RegionId = "");
public sealed record EndgameHuntDisplay(string Id, string Name, int RequiredTier, bool Secret, bool Unlocked,
    string Gate, string[] Phases, string[] Counterplay, string Material);
public sealed record EndgameRunDisplay(long Id, string Name, string Kind, string Status, string Region, int Tier,
    int Room, int RoomCount, int Attempts, int Deaths, int RewardPercent, string[] Rules, string[] Counterplay,
    string[] Inherited, string[] Skipped, bool Cleared, bool CanAdvance, bool CanRetry, bool CanAbandon, string[]? Rooms = null);
public sealed record EndgameRewardDisplay(long RunId, int Materials, int Mastery, string Catalyst, int CatalystCount);
public sealed record EndgameDisplay(bool Unlocked, bool InHub, int HighestTier, int Materials,
    IReadOnlyDictionary<string, int> Catalysts, EndgameSigilDisplay[] Sigils, EndgameHuntDisplay[] Hunts,
    EndgameRunDisplay? Run, bool CanRecover, bool AtGate, int GroundDrops, string RewardSummary, long Revision,
    EndgameRewardDisplay? Reward = null, bool Alive = true, bool InExpedition = false);

/// <summary>Read-only expedition presentation. Every spend, attempt and reward remains a Core transaction.</summary>
public partial class EndgameHud : Control
{
    public event Action? BestiaryRequested;
    public event Action<long>? FractureRequested;
    public event Action<long, string, string>? AttuneRequested;
    public event Action<string>? HuntRequested;
    public event Action? RecoveryRequested, AdvanceRequested, RetryRequested, AbandonRequested, HubRequested, GateApproachRequested;
    public event Action? SaveRequested, LoadRequested, ReplayRequested, ImportRequested;
    public event Action<bool>? VisibilityChangedByPlayer, ModalChanged;
    private EndgameDisplay? _view;
    private PanelContainer _panel = null!, _headlinePanel = null!;
    private ColorRect _backdrop = null!;
    private VBoxContainer _catalog = null!, _rows = null!, _actions = null!;
    private ScrollContainer _catalogScroll = null!, _detailsScroll = null!;
    private Label _title = null!, _status = null!, _notice = null!, _wallet = null!, _detailTitle = null!, _catalogTitle = null!, _detailStatus = null!, _boardNotice = null!;
    private ExpeditionRouteDiagram _route = null!;
    private readonly Dictionary<string, Button> _tabs = [];
    private ConfirmationDialog _confirmation = null!;
    private string _tab = "Sigils", _oldModifier = "", _newModifier = "", _selectedHunt = "";
    private long _selectedSigil;
    private bool _unlockPresented, _pauseHeld;
    private Sandbox? _sandbox;
    private object? _seenSession;
    private int _oldSibling = -1;
    private (long Revision, string Tab, long Sigil, string Hunt, int Drops, bool AtGate)? _rendered;
    public bool IsOpen => _panel is { Visible: true };

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        for (Node? parent = GetParent(); parent is not null; parent = parent.GetParent())
            if (parent is Sandbox sandbox) { _sandbox = sandbox; break; }
        _headlinePanel = Panel(); _headlinePanel.Name = "HudExpeditionObjective"; _headlinePanel.Position = new(22, 88); _headlinePanel.Size = new(565, 104); AddChild(_headlinePanel);
        var heading = new VBoxContainer(); _headlinePanel.AddChild(heading);
        _title = Text("THE FRACTURES", 16); heading.AddChild(_title);
        _status = Text("", 12); heading.AddChild(_status);
        _notice = Text("", 12); _notice.MaxLinesVisible = 1; heading.AddChild(_notice);
        var toggle = new Button { Text = "Fractures & God Hunts [B]", Position = new(921, 61), Size = new(326, 32) };
        toggle.AddThemeFontSizeOverride("font_size", 13); toggle.Pressed += Toggle; AddChild(toggle);
        CombatHudLayout.Navigation(toggle, 2);
        _backdrop = new ColorRect { Color = new(0, 0, 0, .64f), MouseFilter = MouseFilterEnum.Stop, MouseForcePassScrollEvents = false }; AddChild(_backdrop);
        _backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _panel = Panel(); _panel.Name = "ExpeditionPanel"; _panel.MouseForcePassScrollEvents = false; AddChild(_panel);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 10); _panel.AddChild(column);
        column.AddChild(Text("BEYOND THE BREACH · FRACTURES & GOD HUNTS", 19));
        _wallet = Text("", 12); column.AddChild(_wallet);
        BuildCollectionTracking(column);
        var tabs = new HBoxContainer(); column.AddChild(tabs);
        var group = new ButtonGroup();
        foreach (string tab in new[] { "Sigils", "Hunts", "Run", "Rewards" })
        {
            var button = new Button { Name = "ExpeditionTab" + tab, Text = tab, CustomMinimumSize = new(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill, ToggleMode = true, ButtonGroup = group };
            button.Pressed += () => ShowTab(tab); tabs.AddChild(button); _tabs.Add(tab, button);
        }
        var relics = new Button { Name = "ExpeditionCollection", Text = "Relics", CustomMinimumSize = new(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        relics.Pressed += () => CollectionRequested?.Invoke(); tabs.AddChild(relics);
        var bestiary = new Button { Name = "ExpeditionBestiary", Text = "Bestiary", CustomMinimumSize = new(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bestiary.Pressed += () => BestiaryRequested?.Invoke(); tabs.AddChild(bestiary);
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 16); column.AddChild(body);
        var left = new VBoxContainer { CustomMinimumSize = new(270, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = .85f }; body.AddChild(left);
        _catalogTitle = Text("", 13); left.AddChild(_catalogTitle);
        _catalogScroll = Scroll("ExpeditionCatalogScroll"); left.AddChild(_catalogScroll);
        _catalog = Stack(); _catalogScroll.AddChild(_catalog);
        var right = new VBoxContainer { CustomMinimumSize = new(334, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.5f }; right.AddThemeConstantOverride("separation", 8); body.AddChild(right);
        _detailTitle = Text("", 19); _detailTitle.Name = "ExpeditionDetailTitle"; right.AddChild(_detailTitle);
        _detailStatus = Text("", 12); _detailStatus.Name = "ExpeditionDetailStatus"; _detailStatus.AddThemeColorOverride("font_color", new Color("a6d7ce")); right.AddChild(_detailStatus);
        _route = new ExpeditionRouteDiagram { Name = "ExpeditionRoute", SizeFlagsHorizontal = SizeFlags.ExpandFill }; right.AddChild(_route);
        _detailsScroll = Scroll("ExpeditionDetailsScroll"); right.AddChild(_detailsScroll); _rows = Stack(); _detailsScroll.AddChild(_rows);
        _actions = Stack(); right.AddChild(_actions);
        _boardNotice = Text("", 12); _boardNotice.Name = "ExpeditionNotice"; _boardNotice.MaxLinesVisible = 2; _boardNotice.Visible = false; column.AddChild(_boardNotice);
        var footer = new HBoxContainer(); column.AddChild(footer);
        foreach (var (name, action) in new[] { ("Save", (Action)(() => SaveRequested?.Invoke())), ("Load", (Action)(() => { CancelConfirmation(); LoadRequested?.Invoke(); })), ("Close", (Action)(() => SetOpen(false))) })
        { var button = new Button { Name = "Expedition" + name, Text = name, CustomMinimumSize = new(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill }; button.Pressed += action; footer.AddChild(button); }
        BuildConfirmation();
        _panel.Visible = _backdrop.Visible = _headlinePanel.Visible = false;
        VisibilityChanged += UpdateModal; _panel.VisibilityChanged += UpdateModal;
        UpdateLayout();
    }

    public void Toggle() => SetOpen(!IsOpen);
    public void SetOpen(bool open)
    {
        if (!open) CancelConfirmation();
        _panel.Visible = open; _headlinePanel.Visible = !open && _view?.Run is { Status: "Active" };
        if (open) { Rebuild(true); _tabs[_tab].GrabFocus(); }
        UpdateModal(); VisibilityChangedByPlayer?.Invoke(open);
    }
    public void ShowRun() => ShowTab("Run");
    public void ShowTab(string tab)
    {
        if (!_tabs.ContainsKey(tab)) return;
        CancelConfirmation(); _tab = tab; _detailsScroll.ScrollVertical = _catalogScroll.ScrollVertical = 0;
        SetOpen(true);
    }
    public void SelectSigil(long id)
    {
        if (_view?.Sigils.Any(s => s.Id == id) != true) return;
        CancelConfirmation(); _selectedSigil = id; _oldModifier = _newModifier = ""; _detailsScroll.ScrollVertical = 0; Rebuild(true);
    }
    public void SelectHunt(string id)
    {
        if (_view?.Hunts.Any(h => h.Id == id && (!h.Secret || h.Unlocked)) != true) return;
        CancelConfirmation(); _selectedHunt = id; _detailsScroll.ScrollVertical = 0; Rebuild(true);
    }
    public void SessionRestored() { CancelConfirmation(); _seenSession = null; _rendered = null; UpdateModal(); Rebuild(true); }
    public void Notice(string message)
    { _notice.Text = message; _notice.TooltipText = message; _boardNotice.Text = message; _boardNotice.TooltipText = message; _boardNotice.Visible = message.Length > 0; }
    public void SetView(EndgameDisplay view)
    {
        if (_view is not null && (_view.Revision != view.Revision || _view.GroundDrops != view.GroundDrops || _view.AtGate != view.AtGate || _view.InHub != view.InHub || _view.Run != view.Run || _view.Alive != view.Alive)) CancelConfirmation();
        if (!view.Unlocked) _unlockPresented = false;
        bool unlocked = view.Unlocked && view.InHub && !_unlockPresented;
        if (unlocked) _unlockPresented = true;
        bool entered = view.Run is { Status: "Active" } && (_view?.Run?.Id != view.Run.Id || _view.Run.Status != "Active");
        bool died = view.Run is { } run && _view?.Run?.Id == run.Id && _view.Run.Deaths < run.Deaths;
        bool ended = _view?.Run is { Status: "Active" } && view.Run is { Status: not "Active" };
        if (view.Run is { } next && (_view?.Run?.Id != next.Id || _view.Run.Room != next.Room)) Notice("");
        _view = view;
        if (entered || died || ended) { _tab = "Run"; SetOpen(true); }
        else if (unlocked) { _tab = "Sigils"; SetOpen(true); }
        _wallet.Text = view.Unlocked ? $"Highest cleared tier {view.HighestTier}   ·   {view.Sigils.Length} Sigils   ·   {view.Materials} common materials" : "Complete the campaign to open expeditions. Your character and discoveries continue here.";
        var current = view.Run; bool active = current?.Status == "Active";
        _headlinePanel.Visible = !IsOpen && active;
        _title.Text = active ? $"{current!.Name.ToUpperInvariant()} · {(current.Kind == "Fracture" ? "ROOM" : "PHASE")} {Math.Min(current.Room, current.RoomCount)}/{current.RoomCount}" : "THE FRACTURES · GOD HUNTS";
        _status.Text = active ? $"{current!.Attempts} attempts left · {current.RewardPercent}% base material reward · {(current.Cleared ? "Area cleared; collect spoils or continue [B]." : "Read the marked dangers and current counterplay.")}" : _wallet.Text;
        UpdateModal(); Rebuild(false);
    }
    private void Rebuild(bool force)
    {
        if (_view is null || !IsOpen) return;
        var key = (_view.Revision, _tab, _selectedSigil, _selectedHunt, _view.GroundDrops, _view.AtGate);
        if (!force && _rendered == key) return; _rendered = key;
        string focus = GetViewport().GuiGetFocusOwner()?.Name.ToString() ?? "";
        foreach (var tab in _tabs) tab.Value.SetPressedNoSignal(tab.Key == _tab);
        Clear(_rows); Clear(_catalog); Clear(_actions); _route.Visible = false;
        _detailStatus.Text = "";
        if (!_view.Unlocked)
        {
            _catalogTitle.Text = "A FUTURE BEYOND THE BREACH"; _detailTitle.Text = "The seals have not yet broken";
            _catalog.AddChild(Text("Complete the five-act campaign to unlock Fractures and God Hunts.", 15));
            Row("Your existing character, equipment and discoveries carry into every expedition. Four-room Fractures lead to encounters with incomplete reconstructions of dead gods.");
            ActionButton("Import an existing campaign save", () => ImportRequested?.Invoke(), "ExpeditionImport");
        }
        else switch (_tab) { case "Sigils": Sigils(); break; case "Hunts": Hunts(); break; case "Run": Run(); break; default: Rewards(); break; }
        UpdateLayout();
        if (focus.Length > 0 && FindChild(focus, true, false) is Control control && control.IsVisibleInTree()) control.GrabFocus();
    }
    private void UpdateLayout()
    {
        var viewport = GetViewportRect().Size;
        CombatHudLayout.Objective(_headlinePanel, viewport);
        Vector2 size = new(Math.Min(1080, viewport.X - 44), Math.Min(714, viewport.Y - 44));
        _panel.Position = (viewport - size) / 2; _panel.Size = size;
    }
    public override void _Process(double delta) { if (_panel is not null) { UpdateLayout(); UpdateModal(); } }
    private void UpdateModal()
    {
        if (_panel is null || _backdrop is null) return;
        bool open = IsVisibleInTree() && IsOpen;
        _backdrop.Visible = open;
        if (open && _oldSibling < 0) { _oldSibling = GetIndex(); GetParent().MoveChild(this, GetParent().GetChildCount() - 1); }
        else if (!open && _oldSibling >= 0) { GetParent().MoveChild(this, Math.Min(_oldSibling, GetParent().GetChildCount() - 1)); _oldSibling = -1; }
        bool changedSession = _sandbox is not null && !ReferenceEquals(_seenSession, _sandbox.Session);
        if (changedSession) { _seenSession = _sandbox!.Session; CancelConfirmation(); }
        if (_pauseHeld != open || changedSession && open) { _sandbox?.SetModalPaused("expedition-panel", open); _pauseHeld = open; }
        if (!open) CancelConfirmation();
    }
    private static readonly string[] MenuActions = ["aw_inventory", "aw_character", "aw_journey", "aw_endgame", "aw_experiment", "aw_save", "aw_load"];
    private static readonly string[] UiActions = ["ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev"];
    private static readonly string[] CloseActions = ["ui_cancel", "aw_endgame"];
    private static readonly string[] TransferActions = ["aw_inventory", "aw_character", "aw_experiment"];
    public override void _Input(InputEvent input)
    {
        if (!IsOpen || !IsVisibleInTree() || _confirmation.Visible || input is not (InputEventKey or InputEventJoypadButton)) return;
        if (input.IsActionPressed("aw_journey"))
        { SetOpen(false); if (_view?.InExpedition == true) GetViewport().SetInputAsHandled(); }
        else if (MatchesAction(input, CloseActions, pressed: true))
        { SetOpen(false); GetViewport().SetInputAsHandled(); }
        else if (MatchesAction(input, TransferActions, pressed: true)) SetOpen(false);
        else if (!MatchesAction(input, MenuActions) && !MatchesAction(input, UiActions)) GetViewport().SetInputAsHandled();
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (IsOpen && IsVisibleInTree() && !_confirmation.Visible && input is InputEventKey or InputEventJoypadButton &&
            !MatchesAction(input, MenuActions)) GetViewport().SetInputAsHandled();
    }
    private static bool MatchesAction(InputEvent input, string[] actions, bool pressed = false)
    {
        foreach (string action in actions)
            if (InputMap.HasAction(action) && (pressed ? input.IsActionPressed(action) : input.IsAction(action))) return true;
        return false;
    }
    public override void _Notification(int what) { if (what == NotificationApplicationFocusOut && IsInsideTree()) CancelConfirmation(); }
    public override void _ExitTree()
    {
        CancelConfirmation();
        if (!_pauseHeld) return;
        _pauseHeld = false;
        var sandbox = _sandbox;
        // Releasing the last modal may raise the interruption-pause overlay. Wait until
        // child removal finishes before reordering that overlay on a surviving Sandbox.
        Callable.From(() =>
        {
            if (sandbox is not null && GodotObject.IsInstanceValid(sandbox) && sandbox.IsInsideTree() && !sandbox.IsQueuedForDeletion())
                sandbox.SetModalPaused("expedition-panel", false);
        }).CallDeferred();
    }
    private static void Clear(Node node) { foreach (var child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }
    private static VBoxContainer Stack() { var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 9); return box; }
    private static ScrollContainer Scroll(string name) => new() { Name = name, CustomMinimumSize = new(0, 80), SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private static string Readable(string id) => string.Join(' ', id.Split('.').Skip(1).DefaultIfEmpty(id)).Replace('_', ' ');
    private static Label Text(string text, int size = 13) => new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore, LabelSettings = new LabelSettings { FontSize = size } };
    private void Row(string text, int size = 13) => _rows.AddChild(Text(text, size));
    private void Rule() => _rows.AddChild(new HSeparator());
    private static PanelContainer Panel() { var panel = new PanelContainer(); panel.AddThemeStyleboxOverride("panel", CardStyle("0b141c", "71878c", 12)); return panel; }
    private static StyleBoxFlat CardStyle(string fill, string border, int margin = 10) => new()
    {
        BgColor = new(fill),
        BorderColor = new(border),
        BorderWidthBottom = 1,
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        BorderWidthTop = 1,
        CornerRadiusTopLeft = 4,
        CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4,
        CornerRadiusBottomRight = 4,
        ContentMarginLeft = margin,
        ContentMarginRight = margin,
        ContentMarginTop = margin,
        ContentMarginBottom = margin
    };
}
