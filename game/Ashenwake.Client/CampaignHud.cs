using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Campaign navigation and narrative presentation. Every button requests an authoritative runtime action.</summary>
public partial class CampaignHud : Control
{
    public event Action? DiscoveriesRequested;
    public event Action? ChampionsRequested;
    public event Action? OpeningGuideRequested;
    public bool IsOpen => _panel is { Visible: true };
    public event Action<int>? ActRequested;
    public event Action? HubRequested, ContinueRequested, LeaveExplorationRequested, SaveRequested, LoadRequested;
    public event Action<string, string>? ChoiceRequested;
    public event Action<string>? ExplorationRequested, InteractionRequested, ManifestationRequested;
    public event Action<string, string?>? ImplantRequested;
    private CampaignView _view = null!;
    private CampaignState _state = null!;
    private CampaignDefinition _content = null!;
    private AdventureState _anatomy = null!;
    private AdventureView _anatomyView = null!;
    private AdventureDefinition _anatomyContent = null!;
    private CombatView _combat = null!;
    private IReadOnlyList<InteractionDisplay> _interactions = [];
    private Label _headline = null!, _objective = null!, _notice = null!;
    private PanelContainer _panel = null!, _objectivePanel = null!;
    private ScrollContainer _journeyScroll = null!;
    private AnatomyWorkbench _anatomyWorkbench = null!;
    private AdventureContent? _previewContent;
    private AdventureDefinition? _previewDefinition;
    private ColorRect _anatomyBackdrop = null!;
    private Sandbox? _anatomySandbox;
    private bool _anatomyPaused;
    private VBoxContainer _rows = null!, _objectiveRows = null!;
    private Button _firstTab = null!;
    private readonly Dictionary<string, Button> _tabButtons = [];
    private ConfirmationDialog _choiceDialog = null!;
    private string _pendingChoice = "", _pendingOutcome = "", _tab = "Map", _revealedChoice = "";
    private string _maraDialogue = "";
    private long _revision;
    private bool _engaged;
    private (long Revision, string Tab, int Act, bool Hub, bool Engaged, int RangeMask, int Seconds, int LootCount)? _rendered;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _objectivePanel = Panel(new(22, 88), new(565, 104)); _objectivePanel.Name = "HudObjective"; _objectiveRows = new VBoxContainer(); _objectivePanel.AddChild(_objectiveRows);
        _objectiveRows.AddThemeConstantOverride("separation", 6);
        _headline = Label("", 14); _objectiveRows.AddChild(_headline);
        _objective = Label("", 14); _objectiveRows.AddChild(_objective);
        _notice = Label("", 11); _notice.MaxLinesVisible = 2; _notice.Visible = false; _objectiveRows.AddChild(_notice);
        var toggle = new Button { Text = "Journey map & anatomy [J]", Position = new(921, 22), Size = new(326, 35) };
        toggle.Pressed += Toggle; AddChild(toggle);
        CombatHudLayout.Navigation(toggle, 0);
        _anatomyBackdrop = new ColorRect { Color = new(0, 0, 0, .62f), MouseFilter = MouseFilterEnum.Stop, MouseForcePassScrollEvents = false, Visible = false };
        _anatomyBackdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(_anatomyBackdrop);
        _panel = Panel(new(876, 140), new(371, 482)); _panel.Name = "CampaignPanel"; var column = new VBoxContainer(); _panel.AddChild(column);
        var tabs = new HBoxContainer(); column.AddChild(tabs);
        foreach (string tab in new[] { "Map", "Story", "Anatomy", "Journal" })
        {
            var button = new Button { Text = tab, SizeFlagsHorizontal = SizeFlags.ExpandFill, ToggleMode = true };
            button.AddThemeFontSizeOverride("font_size", 12); button.Pressed += () => { CancelJourneyConfirmations(); _tab = tab; _journeyScroll.ScrollVertical = 0; Rebuild(true); }; tabs.AddChild(button);
            _tabButtons.Add(tab, button);
            if (tab == "Map") _firstTab = button;
        }
        var relics = new Button { Name = "JourneyCollection", Text = "Relics", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        relics.AddThemeFontSizeOverride("font_size", 12); relics.Pressed += () => CollectionRequested?.Invoke(); tabs.AddChild(relics);
        var discoveries = new Button { Name = "JourneyDiscoveries", Text = "Discoveries", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        discoveries.AddThemeFontSizeOverride("font_size", 12); discoveries.Pressed += () => DiscoveriesRequested?.Invoke(); tabs.AddChild(discoveries);
        var champions = new Button { Name = "JourneyChampions", Text = "Champions", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        champions.AddThemeFontSizeOverride("font_size", 12); champions.Pressed += () => ChampionsRequested?.Invoke(); tabs.AddChild(champions);
        BuildJourneyBody(column);
        _anatomyWorkbench = new AnatomyWorkbench { Visible = false }; column.AddChild(_anatomyWorkbench);
        _anatomyWorkbench.ImplantRequested += (slot, id) => ImplantRequested?.Invoke(slot, id);
        _anatomyWorkbench.ManifestationRequested += id => ManifestationRequested?.Invoke(id);
        _anatomyWorkbench.VisitMaraRequested += () => RequestInteraction("service.mara");
        _anatomyWorkbench.ReturnRequested += () => { if (_combat.Loot.Count > 0) OpenTab("Map"); else { SetOpen(false); HubRequested?.Invoke(); } };
        var footer = new HBoxContainer(); column.AddChild(footer);
        var guide = new Button { Name = "JourneyOpeningGuide", Text = "First steps", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        guide.Pressed += () => OpeningGuideRequested?.Invoke(); footer.AddChild(guide);
        foreach (var (text, action) in new[] { ("Save", (Action)(() => SaveRequested?.Invoke())), ("Load", (Action)(() => LoadRequested?.Invoke())), ("Close", (Action)Toggle) })
        { var button = new Button { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill }; button.Pressed += action; footer.AddChild(button); }
        _choiceDialog = new ConfirmationDialog { Name = "JourneyChoiceConfirmation", DialogAutowrap = true, Title = "Commit this decision", OkButtonText = "Choose this future", CancelButtonText = "Consider the options" };
        _choiceDialog.Confirmed += ConfirmJourneyChoice; _choiceDialog.Canceled += CancelJourneyConfirmations; AddChild(_choiceDialog);
        BuildTravelConfirmation();
        BuildNextStep();
        _panel.Visible = false;
        for (Node? ancestor = GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
            if (ancestor is Sandbox sandbox) { _anatomySandbox = sandbox; break; }
        _panel.VisibilityChanged += UpdateAnatomyModal;
        VisibilityChanged += UpdateAnatomyModal;
    }
    public void Toggle() => SetOpen(!_panel.Visible);
    public void SetOpen(bool open)
    {
        if (!open) CancelJourneyConfirmations();
        _panel.Visible = open;
        if (_panel.Visible)
        { if (!_state.InHub && !_engaged) _tab = _nextTab; Rebuild(true); FocusCurrentTab(); }
    }
    public bool PresentInteraction(string message)
    {
        if (message is "Dialogue:mara.false_history" or "Dialogue:mara.reward_reaction")
        {
            _maraDialogue = message == "Dialogue:mara.false_history"
                ? "Mara: The wound will hold. Find the Bell Saint beneath Last Mercy. Bring back what it guards."
                : "Mara: A heart that remembers the dead. Implant it, and see what answers your call.";
            _tab = "Map";
            Notice("Mara's lead is recorded. Choose your next destination below.");
        }
        else if (message == "ServiceOpened:service.mara") _tab = "Anatomy";
        else return false;
        RefreshNextStep(); Visible = true; _panel.Visible = true; Rebuild(true); FocusCurrentTab();
        if (message == "ServiceOpened:service.mara" && FirstHeartAvailable) _anatomyWorkbench.InspectReward();
        return true;
    }
    public void Notice(string message) { _notice.Text = message; _notice.TooltipText = message; _notice.Visible = message.Length > 0; }
    public void SetView(CampaignView view, CampaignState state, CampaignDefinition content, AdventureView anatomyView,
        AdventureState anatomy, AdventureDefinition anatomyContent, CombatView combat, IReadOnlyList<InteractionDisplay> interactions, long revision, ProgressionSnapshot? equipment = null)
    {
        if (_view is not null && (_revision != revision || _state.CurrentAct != state.CurrentAct || _state.InHub != state.InHub || _combat.Loot.Count != combat.Loot.Count)) CancelJourneyConfirmations();
        bool locationChanged = _view is null || _state.CurrentAct != state.CurrentAct || _state.InHub != state.InHub;
        bool enteredCombat = combat.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0) && !_engaged;
        bool changed = _view is null || _view.Region != view.Region || _view.Ending is null && view.Ending is not null;
        bool endingArrived = _view?.Ending is null && view.Ending is not null;
        _view = view; _state = state; _content = content; _anatomyView = anatomyView; _anatomy = anatomy; _anatomyContent = anatomyContent;
        _combat = combat; _interactions = interactions; _revision = revision; _openingEquipment = equipment;
        if (locationChanged) _selectedJourneyRegion = state.InHub ? 0 : state.CurrentAct;
        SynchronizeJourneySession();
        _engaged = combat.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        bool activeBoss = combat.Actors.Any(a => a.Health > 0 && a.DefinitionId.StartsWith("boss.", StringComparison.Ordinal));
        _headline.Text = $"{view.Region.ToUpperInvariant()} · ACT {view.Act}" + (activeBoss && combat.BossPhase > 0 ? $" · PHASE {combat.BossPhase}" : "");
        if (!state.InHub) _maraDialogue = "";
        RefreshNextStep();
        if (changed) _panel.Visible = state.InHub || endingArrived;
        else if (enteredCombat) _panel.Visible = false;
        if (endingArrived) _tab = "Story";
        string ready = !_state.InHub && !_engaged && _state.Exploration is null
            ? _content.Choices.FirstOrDefault(c => c.Act == state.CurrentAct && state.CompletedEncounters.Contains(c.RequiredEncounter) && !state.Choices.ContainsKey(c.Id))?.Id ?? "" : "";
        if (ready.Length > 0 && ready != _revealedChoice) { _tab = "Story"; _panel.Visible = true; changed = true; }
        _revealedChoice = ready;
        Rebuild(changed);
    }
    private void Rebuild(bool force)
    {
        UpdateAnatomyLayout();
        foreach (var tab in _tabButtons) tab.Value.SetPressedNoSignal(tab.Key == _tab);
        if (_view is null || !_panel.Visible) return;
        int mask = 0; for (int i = 0; i < _interactions.Count; i++) if (_interactions[i].Distance <= _interactions[i].Range) mask |= 1 << i;
        var key = (_revision, _tab, _view.Act, _state.InHub, _engaged, mask, (_state.Exploration?.RemainingTicks ?? 0) / 30, _combat.Loot.Count);
        if (!force && _rendered == key) return; _rendered = key;
        foreach (var child in _rows.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
        switch (_tab) { case "Map": Map(); break; case "Story": Story(); break; case "Anatomy": RefreshAnatomy(); break; default: Journal(); break; }
    }
    private void Story()
    {
        if (_view.Ending is { } ending)
        {
            _rows.AddChild(Label("THE BREACH IS STABLE", 18)); _rows.AddChild(Label(ending.Summary, 13));
            _rows.AddChild(Label(Readable(ending.Id).ToUpperInvariant(), 15)); _rows.AddChild(Label(ending.GreyhavenCondition, 13));
            _rows.AddChild(Label(ending.ResonanceRelationship, 13));
            _rows.AddChild(Label("Allies: " + string.Join(", ", ending.Alliances), 12));
            _rows.AddChild(Label("Surviving leaders: " + string.Join(", ", ending.SurvivingLeaders), 12));
            _rows.AddChild(Label(ending.FracturesUnlocked ? "RESONANCE FRACTURES UNLOCKED" : "Campaign complete", 15));
            Button("Return to the people of Greyhaven", () => RequestJourneyTravel(new(JourneyTravelKind.Hub))).Disabled = _state.InHub || _engaged;
            return;
        }
        _rows.AddChild(Label(_view.Region.ToUpperInvariant(), 17)); _rows.AddChild(Label(_view.Revelation, 13));
        var encounter = _content.Acts.SelectMany(a => a.Encounters).FirstOrDefault(e => e.Id == _view.EncounterId);
        if (encounter is not null)
        { _rows.AddChild(new HSeparator()); _rows.AddChild(Label(Readable(encounter.Id).ToUpperInvariant(), 15)); _rows.AddChild(Label(encounter.Counterplay, 13)); }
        foreach (var choice in _content.Choices.Where(c => c.Act == _view.Act))
        {
            _rows.AddChild(new HSeparator()); _rows.AddChild(Label(choice.Prompt, 15));
            if (_state.Choices.TryGetValue(choice.Id, out string? chosen)) _rows.AddChild(Label("Your decision: " + choice.Outcomes.Single(o => o.Id == chosen).Text, 13));
            else foreach (var outcome in choice.Outcomes)
                {
                    var button = Button(outcome.Text, () =>
                    {
                        CancelJourneyConfirmations(); _confirmationRevision = _revision;
                        _pendingChoice = choice.Id; _pendingOutcome = outcome.Id;
                        _choiceDialog.DialogText = choice.Prompt + "\n\n" + outcome.Text + "\n\nThis decision is permanent for this campaign. Its consequences can arrive in later acts.";
                        _choiceDialog.PopupCentered(new(560, 260));
                    });
                    button.Disabled = _state.InHub || _engaged || !_state.CompletedEncounters.Contains(choice.RequiredEncounter) || _state.Exploration is not null;
                }
        }
    }
    private Button Button(string text, Action action)
    { var button = new Button { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart }; button.AddThemeFontSizeOverride("font_size", 12); button.Pressed += action; _rows.AddChild(button); return button; }
    private static string Readable(string id) => string.Join(' ', id.Split('.').Skip(1).DefaultIfEmpty(id)).Replace('_', ' ').Replace(':', ' ');
    private static Label Label(string text, int size) { var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart }; label.AddThemeFontSizeOverride("font_size", size); return label; }
    private PanelContainer Panel(Vector2 position, Vector2 size)
    {
        var panel = new PanelContainer { Position = position, Size = size };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(.04f, .061f, .084f, .98f), BorderColor = new Color("657b82"), BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10 });
        AddChild(panel); return panel;
    }
}
