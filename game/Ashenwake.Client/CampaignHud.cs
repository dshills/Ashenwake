using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

/// <summary>Campaign navigation and narrative presentation. Every button requests an authoritative runtime action.</summary>
public partial class CampaignHud : Control
{
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
    private IReadOnlyDictionary<string, string> _fragmentDescriptions = new Dictionary<string, string>();
    private Label _headline = null!, _objective = null!, _notice = null!;
    private PanelContainer _panel = null!;
    private VBoxContainer _rows = null!;
    private Button _firstTab = null!;
    private ConfirmationDialog _choiceDialog = null!;
    private string _pendingChoice = "", _pendingOutcome = "", _tab = "Map", _revealedChoice = "";
    private string _maraDialogue = "";
    private long _revision;
    private bool _engaged;
    private (long Revision, string Tab, int Act, bool Hub, bool Engaged, int RangeMask, int Seconds, int LootCount)? _rendered;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var objective = Panel(new(22, 88), new(565, 104)); var headline = new VBoxContainer(); objective.AddChild(headline);
        _headline = Label("", 16); headline.AddChild(_headline);
        _objective = Label("", 12); headline.AddChild(_objective);
        _notice = Label("", 12); _notice.MaxLinesVisible = 2; headline.AddChild(_notice);
        var toggle = new Button { Text = "Journey map & anatomy [J]", Position = new(921, 22), Size = new(326, 35) };
        toggle.Pressed += Toggle; AddChild(toggle);
        _panel = Panel(new(876, 140), new(371, 482)); var column = new VBoxContainer(); _panel.AddChild(column);
        var tabs = new HBoxContainer(); column.AddChild(tabs);
        foreach (string tab in new[] { "Map", "Story", "Anatomy", "Journal" })
        {
            var button = new Button { Text = tab, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            button.AddThemeFontSizeOverride("font_size", 12); button.Pressed += () => { _tab = tab; Rebuild(true); }; tabs.AddChild(button);
            if (tab == "Map") _firstTab = button;
        }
        var scroll = new ScrollContainer { CustomMinimumSize = new(339, 353), SizeFlagsVertical = SizeFlags.ExpandFill }; column.AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(_rows);
        var footer = new HBoxContainer(); column.AddChild(footer);
        foreach (var (text, action) in new[] { ("Save", (Action)(() => SaveRequested?.Invoke())), ("Load", (Action)(() => LoadRequested?.Invoke())), ("Close", (Action)Toggle) })
        { var button = new Button { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill }; button.Pressed += action; footer.AddChild(button); }
        _choiceDialog = new ConfirmationDialog { Title = "Commit this decision", OkButtonText = "Choose this future", CancelButtonText = "Consider the options" };
        _choiceDialog.Confirmed += () => ChoiceRequested?.Invoke(_pendingChoice, _pendingOutcome); AddChild(_choiceDialog);
        BuildNextStep();
        _panel.Visible = false;
    }
    public void Toggle()
    {
        _panel.Visible = !_panel.Visible;
        if (_panel.Visible)
        { if (!_state.InHub && !_engaged) _tab = _nextTab; Rebuild(true); _firstTab.GrabFocus(); }
    }
    public bool PresentInteraction(string message)
    {
        if (message is "Dialogue:mara.false_history" or "Dialogue:mara.reward_reaction")
        {
            _maraDialogue = message == "Dialogue:mara.false_history"
                ? "Mara: The wound will hold. Find the Bell Saint beneath Last Mercy. Bring back what it guards."
                : "Mara: A heart that remembers the dead. Implant it, and see what answers your call.";
            _tab = "Map";
            Notice("Mara has spoken. Choose an act on the Journey map to leave Greyhaven.");
        }
        else if (message == "ServiceOpened:service.mara") _tab = "Anatomy";
        else return false;
        Visible = true; _panel.Visible = true; Rebuild(true); _firstTab.GrabFocus(); return true;
    }
    public void Notice(string message) { _notice.Text = message; _notice.TooltipText = message; }
    public void SetFragmentDescriptions(IReadOnlyDictionary<string, string> descriptions) => _fragmentDescriptions = descriptions;
    public void SetView(CampaignView view, CampaignState state, CampaignDefinition content, AdventureView anatomyView,
        AdventureState anatomy, AdventureDefinition anatomyContent, CombatView combat, IReadOnlyList<InteractionDisplay> interactions, long revision)
    {
        bool enteredCombat = combat.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0) && !_engaged;
        bool changed = _view is null || _view.Region != view.Region || _view.Ending is null && view.Ending is not null;
        bool endingArrived = _view?.Ending is null && view.Ending is not null;
        _view = view; _state = state; _content = content; _anatomyView = anatomyView; _anatomy = anatomy; _anatomyContent = anatomyContent;
        _combat = combat; _interactions = interactions; _revision = revision;
        _engaged = combat.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        bool activeBoss = combat.Actors.Any(a => a.Health > 0 && a.DefinitionId.StartsWith("boss.", StringComparison.Ordinal));
        _headline.Text = $"{view.Region.ToUpperInvariant()} · ACT {view.Act}" + (activeBoss && combat.BossPhase > 0 ? $" · PHASE {combat.BossPhase}" : "");
        RefreshNextStep();
        if (!state.InHub) _maraDialogue = "";
        if (changed) _panel.Visible = state.InHub || endingArrived;
        else if (enteredCombat) _panel.Visible = false;
        if (endingArrived) _tab = "Story";
        string ready = !_state.InHub && !_engaged && _state.Exploration is null
            ? _content.Choices.FirstOrDefault(c => c.Act == state.CurrentAct && state.CompletedEncounters.Contains(c.RequiredEncounter) && !state.Choices.ContainsKey(c.Id))?.Id ?? "" : "";
        if (ready.Length > 0 && ready != _revealedChoice) { _revealedChoice = ready; _tab = "Story"; _panel.Visible = true; changed = true; }
        Rebuild(changed);
    }
    private void Rebuild(bool force)
    {
        if (_view is null || !_panel.Visible) return;
        int mask = 0; for (int i = 0; i < _interactions.Count; i++) if (_interactions[i].Distance <= _interactions[i].Range) mask |= 1 << i;
        var key = (_revision, _tab, _view.Act, _state.InHub, _engaged, mask, (_state.Exploration?.RemainingTicks ?? 0) / 30, _combat.Loot.Count);
        if (!force && _rendered == key) return; _rendered = key;
        foreach (var child in _rows.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
        switch (_tab) { case "Map": Map(); break; case "Story": Story(); break; case "Anatomy": Anatomy(); break; default: Journal(); break; }
    }
    private void Map()
    {
        _rows.AddChild(Label("EDRATH · REGIONS & ANCHORS", 17));
        if (_state.InHub)
        {
            if (_maraDialogue.Length > 0)
            {
                _rows.AddChild(Label(_maraDialogue, 13));
                Button("Mara · Divine Anatomy", () => InteractionRequested?.Invoke("service.mara")).Disabled =
                    !_interactions.Any(i => i.Id == "service.mara" && i.Distance <= i.Range);
            }
            _rows.AddChild(Label("Leave Greyhaven: choose an unlocked act below.", 13));
        }
        _rows.AddChild(Label($"Anchor: {Readable(_view.Anchor)} · deaths {_state.Deaths}", 12));
        if (!_state.InHub) MapNextStep();
        foreach (var act in _content.Acts)
        {
            bool unlocked = _view.AvailableActs.Contains(act.Number);
            Button($"{(_state.CompletedActs.Contains(act.Number) ? "✓" : !unlocked ? "○" : "●")} Act {act.Number} · {act.Name}", () => ActRequested?.Invoke(act.Number)).Disabled = !unlocked || _engaged || _state.Exploration is not null;
            if (act.Number == _view.Act && !_state.InHub)
                foreach (var encounter in act.Encounters)
                    _rows.AddChild(Label(($"{(_state.CompletedEncounters.Contains(encounter.Id) ? "✓" : encounter.Id == _view.EncounterId ? "→" : "○")} ") + Readable(encounter.Id), 12));
        }
        if (!_state.InHub)
        {
            string droppedLoot = _combat.Loot.Count > 0 ? $" · leave {_combat.Loot.Count} uncollected drops" : "";
            _rows.AddChild(new HSeparator()); _rows.AddChild(Label("EXPLORATION", 14));
            foreach (var exploration in _content.Exploration.Where(e => e.Act == _view.Act))
                Button((_state.CompletedExploration.Contains(exploration.Id) ? "✓ " : "") + exploration.Name + " · " + exploration.Kind + droppedLoot,
                    () => ExplorationRequested?.Invoke(exploration.Id)).Disabled = _engaged || _state.Exploration is not null || _state.CompletedExploration.Contains(exploration.Id);
            if (_state.Exploration is { } active)
            {
                var definition = _content.Exploration.Single(e => e.Id == active.Id);
                _rows.AddChild(Label(definition.Name + (active.RemainingTicks > 0 ? $" · {active.RemainingTicks / 30d:F0}s remaining" : ""), 14));
                foreach (var clue in definition.Clues.Select((id, index) => (id, index)))
                    _rows.AddChild(Label((clue.index < active.TrackedClues ? "✓ " : "○ ") + Readable(clue.id), 12));
                foreach (string rule in _view.ExplorationRules) _rows.AddChild(Label(Readable(rule), 12));
                bool won = !_engaged && (definition.Kind != "Hunt" || active.TrackedClues == definition.Clues.Length);
                Button((won ? "Finish exploration" : "Leave exploration") + droppedLoot, () => LeaveExplorationRequested?.Invoke());
            }
        }
        if (_interactions.Count > 0) { _rows.AddChild(new HSeparator()); _rows.AddChild(Label("NEARBY PEOPLE & LANDMARKS", 14)); }
        foreach (var interaction in _interactions)
            Button(interaction.Name + (interaction.Distance <= interaction.Range ? " [F]" : " · approach marker"), () => InteractionRequested?.Invoke(interaction.Id)).Disabled = interaction.Distance > interaction.Range;
        if (_state.InHub) foreach (string reaction in _view.HubReactions) _rows.AddChild(Label(reaction, 12));
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
            Button("Return to the people of Greyhaven", () => HubRequested?.Invoke()).Disabled = _state.InHub || _engaged;
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
                        _pendingChoice = choice.Id; _pendingOutcome = outcome.Id;
                        _choiceDialog.DialogText = choice.Prompt + "\n\n" + outcome.Text + "\n\nThis decision is permanent for this campaign. Its consequences can arrive in later acts.";
                        _choiceDialog.PopupCentered(new(560, 260));
                    });
                    button.Disabled = _state.InHub || _engaged || !_state.CompletedEncounters.Contains(choice.RequiredEncounter) || _state.Exploration is not null;
                }
        }
    }
    private void Anatomy()
    {
        bool atMara = _state.InHub && _interactions.Any(i => i.Id == "service.mara" && i.Distance <= i.Range);
        _rows.AddChild(Label($"DIVINE ANATOMY · {_anatomyView.Resonance} RESONANCE", 16));
        _rows.AddChild(Label(atMara ? "Choose a compatible fragment that you own." : "Approach Mara in Greyhaven to change implants and Manifestations.", 12));
        foreach (string slot in new[] { "Mind", "Eyes", "Heart", "Spine", "Arms", "Legs" })
        {
            _rows.AddChild(Label(slot, 13)); var choice = new OptionButton { Disabled = !atMara }; choice.AddItem("Empty"); choice.SetItemMetadata(0, ""); int selected = 0;
            foreach (var fragment in _anatomyContent.Fragments.Where(f => f.Slot == slot && _anatomy.OwnedFragments.Contains(f.Id)))
            {
                int index = choice.ItemCount; choice.AddItem($"{Readable(fragment.Id)} · {fragment.Resonance} R"); choice.SetItemMetadata(index, fragment.Id);
                choice.SetItemTooltip(index, _fragmentDescriptions.GetValueOrDefault(fragment.Id, "")); if (_anatomy.Anatomy.GetValueOrDefault(slot) == fragment.Id) selected = index;
            }
            choice.Select(selected); choice.ItemSelected += index => { string id = choice.GetItemMetadata((int)index).AsString(); ImplantRequested?.Invoke(slot, id.Length == 0 ? null : id); }; _rows.AddChild(choice);
            if (_anatomy.Anatomy.TryGetValue(slot, out string? id)) _rows.AddChild(Label(_fragmentDescriptions.GetValueOrDefault(id, ""), 12));
        }
        _rows.AddChild(new HSeparator()); _rows.AddChild(Label("MANIFESTATIONS · reversible choices", 14));
        foreach (var manifestation in _anatomyContent.Manifestations)
        {
            Button((_anatomy.Manifestations.GetValueOrDefault(manifestation.Threshold) == manifestation.Id ? "◆ " : "") + $"{Readable(manifestation.Id)} · {manifestation.Threshold} R",
                () => ManifestationRequested?.Invoke(manifestation.Id)).Disabled = !atMara || _anatomyView.Resonance < manifestation.Threshold;
            _rows.AddChild(Label("Benefit: " + manifestation.Benefit + "\nCost: " + manifestation.Complication, 12));
        }
    }
    private void Journal()
    {
        _rows.AddChild(Label("THE JOURNEY REMEMBERED", 17));
        foreach (var act in _content.Acts.Where(a => a.Number <= _state.HighestActVisited))
        { _rows.AddChild(Label(act.Name, 15)); _rows.AddChild(Label(act.Revelation, 13)); }
        _rows.AddChild(new HSeparator()); _rows.AddChild(Label("GREYHAVEN'S PEOPLE", 15));
        foreach (string resident in _view.Residents) _rows.AddChild(Label("✓ " + resident, 13));
        foreach (string reaction in _view.HubReactions) _rows.AddChild(Label(reaction, 12));
        if (_view.PendingConsequences.Length > 0) _rows.AddChild(Label("Some consequences of your decisions are still unfolding beyond this region.", 12));
        _rows.AddChild(new HSeparator()); _rows.AddChild(Label("DISCOVERIES", 15));
        foreach (string discovery in _state.Discoveries) _rows.AddChild(Label(Readable(discovery), 12));
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
