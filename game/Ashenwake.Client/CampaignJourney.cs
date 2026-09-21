using Ashenwake.Core.Campaign;
using Godot;

namespace Ashenwake.Client;

/// <summary>Journey presentation reads the campaign ledger; only explicit actions request travel.</summary>
public partial class CampaignHud
{
    private JourneyRegionMap _regionMap = null!;
    private VBoxContainer _regionColumn = null!;
    private HBoxContainer _journeyBody = null!;
    private Label _mapObjective = null!;
    private string _journalCategory = "Objectives";
    private int _selectedJourneyRegion = -1;
    public int SelectedJourneyRegion => _selectedJourneyRegion;

    private void BuildJourneyBody(VBoxContainer column)
    {
        var body = _journeyBody = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 16); column.AddChild(body);
        _regionColumn = new VBoxContainer { CustomMinimumSize = new(380, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.25f };
        _regionColumn.AddThemeConstantOverride("separation", 10); body.AddChild(_regionColumn);
        _regionColumn.AddChild(Label("EDRATH · REGIONS & ANCHORS", 17));
        _regionMap = new JourneyRegionMap { Name = "JourneyMap", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        _regionMap.RegionSelected += SelectJourneyRegion; _regionColumn.AddChild(_regionMap);
        _regionColumn.AddChild(Label("YOUR NEXT OBJECTIVE", 13));
        _mapObjective = Label("", 14); _mapObjective.Name = "JourneyObjective"; _mapObjective.MaxLinesVisible = 4; _regionColumn.AddChild(_mapObjective);
        _regionColumn.AddChild(Label("Select a location to inspect its route. Travel only begins when you choose a destination action.", 12));
        _journeyScroll = new ScrollContainer { CustomMinimumSize = new(280, 180), SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.AddChild(_journeyScroll);
        _rows = new VBoxContainer { Name = "JourneyJournalContent", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 9); _journeyScroll.AddChild(_rows);
    }

    public void SelectJourneyRegion(int number)
    {
        if (_view is null || number < 0 || number > 5) return;
        CancelJourneyConfirmations(); _selectedJourneyRegion = number; _journeyScroll.ScrollVertical = 0;
        _tab = "Map"; Rebuild(true);
    }

    public void OpenJourneyJournal(string category)
    {
        if (category is not ("Objectives" or "Discoveries" or "Choices" or "People")) return;
        CancelJourneyConfirmations(); _journalCategory = category; _journeyScroll.ScrollVertical = 0; OpenTab("Journal");
    }

    private void Map()
    {
        if (_selectedJourneyRegion < 0) _selectedJourneyRegion = _state.InHub ? 0 : _state.CurrentAct;
        _regionMap.SetRegions(_content.Acts.Select(a => new JourneyRegionDisplay(a.Number, a.Name,
            _view.AvailableActs.Contains(a.Number), _state.CompletedActs.Contains(a.Number), !_state.InHub && a.Number == _state.CurrentAct)).ToArray(), _state.InHub, _selectedJourneyRegion);
        _mapObjective.Text = _objective.Text; _mapObjective.TooltipText = _objective.Text;
        if (_selectedJourneyRegion == 0)
        {
            JourneyHeading("GREYHAVEN", _state.InHub ? "YOU ARE HERE · SANCTUARY" : "HOME · WORKSHOPS & RESIDENTS");
            _rows.AddChild(Label("Rest at your anchor, visit rescued specialists, and prepare your next build.", 13));
            var travel = Button("Return to Greyhaven" + LootSuffix, () => RequestJourneyTravel(new(JourneyTravelKind.Hub)));
            travel.Name = "JourneyTravel"; travel.Disabled = _state.InHub || !JourneyAlive;
            if (_engaged) _rows.AddChild(Label("Returning leaves the current fight. Earned character progress is preserved.", 12));
            JourneyLootNotice();
            if (_state.InHub)
            {
                AnatomyRewardMapAction();
                if (_maraDialogue.Length > 0)
                {
                    _rows.AddChild(Label(_maraDialogue, 13));
                    var mara = _interactions.FirstOrDefault(i => i.Id == "service.mara");
                    if (mara is not null) Button(mara.Distance <= mara.Range ? "Mara · Divine Anatomy" : "Walk to Mara · Divine Anatomy", () => RequestInteraction(mara.Id));
                }
                if (NextAvailableAct() is { } next)
                    Button($"Preview Act {next.Number} · {next.Name}", () => SelectJourneyRegion(next.Number));
                JourneyNearby();
                foreach (string reaction in _view.HubReactions) _rows.AddChild(Label(reaction, 12));
            }
            return;
        }
        var act = _content.Acts.Single(a => a.Number == _selectedJourneyRegion);
        bool unlocked = _view.AvailableActs.Contains(act.Number), here = !_state.InHub && act.Number == _state.CurrentAct;
        bool completed = _state.CompletedActs.Contains(act.Number);
        JourneyHeading($"ACT {act.Number} · {act.Name.ToUpperInvariant()}", here ? "YOU ARE HERE" + (completed ? " · COMPLETED" : "") : completed ? "COMPLETED" : unlocked ? "ROUTE OPEN" : "LOCKED ROUTE");
        var request = new JourneyTravelRequest(JourneyTravelKind.Act, act.Number);
        var enter = Button($"Travel to Act {act.Number} · {act.Name}" + LootSuffix, () => RequestJourneyTravel(request));
        enter.Name = "JourneyTravel"; enter.Disabled = here || JourneyTravelReason(request).Length > 0;
        string reason = here ? "You are here. Follow the next step below." : JourneyTravelReason(request);
        if (reason.Length > 0) _rows.AddChild(Label(reason, 12));
        if (!unlocked)
        {
            _rows.AddChild(Label($"Complete Act {act.Number - 1} to open this route. Its encounters and testimony remain undiscovered.", 13));
            return;
        }
        if (here) MapNextStep(); else JourneyLootNotice();
        _rows.AddChild(Label("Anchor · " + Readable(act.Anchor), 12));
        _rows.AddChild(Label($"{act.Encounters.Count(e => _state.CompletedEncounters.Contains(e.Id))}/{act.Encounters.Length} encounters completed", 13));
        _rows.AddChild(new HSeparator()); _rows.AddChild(Label("REGIONAL ROUTE", 14));
        var nextEncounter = act.Encounters.FirstOrDefault(e => !_state.CompletedEncounters.Contains(e.Id));
        foreach (var encounter in act.Encounters)
        {
            bool done = _state.CompletedEncounters.Contains(encounter.Id), next = encounter == nextEncounter;
            string name = done || next ? Readable(encounter.Id) : "Undiscovered encounter";
            _rows.AddChild(Label((done ? "✓ " : next ? "→ " : "○ ") + name, 13));
            if (next)
            {
                _rows.AddChild(Label(encounter.Counterplay, 12));
                _rows.AddChild(Label($"Completion reward · {encounter.Experience} XP · {encounter.Materials} materials", 12));
            }
        }
        if (here)
        {
            AnatomyRewardMapAction(); JourneyExploration(); JourneyNearby();
        }
    }

    private void JourneyHeading(string title, string status)
    { _rows.AddChild(Label(title, 18)); var label = Label(status, 12); label.Modulate = new("9bd4c7"); _rows.AddChild(label); }
    private bool RoomLootRetained => !_state.InHub && !_engaged && _interactions.Any(i => i.Id.StartsWith("opening.", StringComparison.Ordinal) || i.Id.StartsWith("verdant.", StringComparison.Ordinal) || i.Id.StartsWith("cinder.", StringComparison.Ordinal));
    private string LootSuffix => _combat.Loot.Count > 0 ? $" · {_combat.Loot.Count} uncollected drops" : "";
    private void JourneyLootNotice()
    {
        if (_combat.Loot.Count == 0) return;
        var notice = Label($"{_combat.Loot.Count} ground drops remain here. " + (RoomLootRetained ? "This cleared room keeps remaining drops when you travel. " : "Travel leaves them behind. ") + "Close the map and click a drop or press E nearby to collect it; hold Alt to reveal filtered drops.", 12);
        notice.Name = "JourneyLootWarning"; notice.Modulate = new("eab28d"); _rows.AddChild(notice);
    }
    private void JourneyNearby()
    {
        if (_interactions.Count == 0) return;
        _rows.AddChild(new HSeparator()); _rows.AddChild(Label("NEARBY PEOPLE & LANDMARKS", 14));
        foreach (var interaction in _interactions)
            Button(interaction.Distance <= interaction.Range ? interaction.Name + " [F]" : "Walk to " + interaction.Name, () => RequestInteraction(interaction.Id));
    }
    private void JourneyExploration()
    {
        var entries = _content.Exploration.Where(e => e.Act == _view.Act).ToArray();
        if (entries.Length == 0) return;
        _rows.AddChild(new HSeparator()); _rows.AddChild(Label("OPTIONAL EXPLORATION", 14));
        foreach (var entry in entries)
        {
            if (entry.Id == "event.widow_crypt")
            {
                bool available = _interactions.Any(i => i.Id == "opening.crypt.enter");
                Button((_state.CompletedExploration.Contains(entry.Id) ? "✓ Revisit " : "Explore ") + entry.Name + " · side crypt",
                    () => RequestInteraction("opening.crypt.enter")).Disabled = !available;
                if (!available && !InOpeningCrypt) _rows.AddChild(Label("Its marked entrance branches north from the secured Grey March road.", 12));
                continue;
            }
            if (HasVerdantExploration && entry.Id is ("event.briar_shrine" or "event.wake_hunt"))
            {
                bool shrine = entry.Id == "event.briar_shrine";
                string entrance = shrine ? "verdant.shrine.enter" : "verdant.hunt.enter";
                bool available = _interactions.Any(i => i.Id == entrance);
                Button((_state.CompletedExploration.Contains(entry.Id) ? "✓ Revisit " : "Explore ") + entry.Name + (shrine ? " · hidden shrine" : " · tracking hunt"),
                    () => RequestInteraction(entrance)).Disabled = !available;
                if (!available && !(shrine ? InBriarShrine : InAntlerGrove)) _rows.AddChild(Label(shrine
                    ? "Its marked entrance branches north from the secured Living Ruins."
                    : "Follow the southern passage from secured Plague Village to the Antler's grove.", 12));
                continue;
            }
            if (HasCinderExploration && entry.Id is ("event.sealed_foundry" or "event.resonance_storm"))
            {
                bool foundry = entry.Id == "event.sealed_foundry";
                string entrance = foundry ? "cinder.foundry.enter" : "cinder.storm.enter";
                bool available = _interactions.Any(i => i.Id == entrance);
                Button((_state.CompletedExploration.Contains(entry.Id) ? "✓ Revisit " : "Explore ") + entry.Name + (foundry ? " · workers' refuge" : _state.CompletedExploration.Contains(entry.Id) ? " · cleared collectors" : " · timed storm"),
                    () => RequestInteraction(entrance)).Disabled = !available;
                if (!available && !(foundry ? InSealedFoundry : InBurningRain)) _rows.AddChild(Label(foundry
                    ? "Its marked entrance branches north from the secured Cinder Fields."
                    : "Enter Burning Rain through the southern passage from the secured Extraction Floor.", 12));
                if (!foundry && !_state.CompletedExploration.Contains(entry.Id))
                    _rows.AddChild(Label($"Defeat its creatures within {entry.DurationTicks / 30d:F0} seconds. The timer starts on entry; expiry returns you to the regional route without the storm reward.", 12));
                continue;
            }
            var request = new JourneyTravelRequest(JourneyTravelKind.Exploration, Id: entry.Id);
            Button((_state.CompletedExploration.Contains(entry.Id) ? "✓ " : "") + entry.Name + " · " + entry.Kind + LootSuffix,
                () => RequestJourneyTravel(request)).Disabled = JourneyTravelReason(request).Length > 0;
        }
        if (_state.Exploration is not { } active) return;
        var definition = entries.Single(e => e.Id == active.Id);
        if (definition.Id == "event.widow_crypt")
        {
            _rows.AddChild(Label("Open the testament after defeating its guardians. The western passage returns to the road; remaining drops stay in cleared rooms.", 12));
            Button("Return to the Grey March road", () => RequestInteraction("opening.crypt.return"));
            return;
        }
        if (definition.Id == "event.briar_shrine")
        {
            _rows.AddChild(Label("Uncover the oathstone after defeating its guardians. The western passage returns to the Living Ruins; remaining drops stay in cleared rooms.", 12));
            Button("Return to the Living Ruins", () => RequestInteraction("verdant.shrine.return"));
            return;
        }
        if (HasCinderExploration && definition.Id == "event.sealed_foundry")
        {
            _rows.AddChild(Label("Open the Foundry Testament after defeating the furnace guardians. The western passage returns to the Cinder Fields; remaining drops stay in cleared rooms.", 12));
            Button("Return to the Cinder Fields", () => RequestInteraction("cinder.foundry.return"));
            return;
        }
        if (HasCinderExploration && definition.Id == "event.resonance_storm")
        {
            _rows.AddChild(Label(_engaged ? $"BURNING RAIN · {Math.Ceiling(active.RemainingTicks / 30d):F0}s remaining" : "BURNING RAIN · COMPLETED", 14));
            _rows.AddChild(Label(_engaged
                ? "Defeat the storm creatures before time expires. You can retreat through the western passage at any time; an unfinished attempt restarts on entry. The Journey map pauses the timer."
                : "The storm reward is secured and its timer has stopped. Remaining drops stay here when you return to the regional route.", 12));
            foreach (string rule in _view.ExplorationRules) _rows.AddChild(Label(Readable(rule), 12));
            Button(StormReturnName, () => RequestInteraction("cinder.storm.return"));
            return;
        }
        _rows.AddChild(Label(definition.Name + (active.RemainingTicks > 0 ? $" · {active.RemainingTicks / 30d:F0}s remaining" : ""), 14));
        foreach (var (clue, index) in definition.Clues.Select((id, index) => (id, index)))
            _rows.AddChild(Label((index < active.TrackedClues ? "✓ " : "○ ") + Readable(clue), 12));
        foreach (string rule in _view.ExplorationRules) _rows.AddChild(Label(Readable(rule), 12));
        if (HasVerdantExploration && definition.Id == "event.wake_hunt")
        {
            _rows.AddChild(Label("Follow each marked trace through the grove, then defeat the Antler. The western passage returns to Plague Village; drops stay in the secured grove.", 12));
            Button("Return to Plague Village", () => RequestInteraction("verdant.hunt.return"));
            return;
        }
        bool won = !_engaged && (definition.Kind != "Hunt" || active.TrackedClues == definition.Clues.Length);
        Button((won ? "Finish exploration" : "Leave exploration") + LootSuffix, () => RequestJourneyTravel(new(JourneyTravelKind.LeaveExploration)));
    }

    private void Journal()
    {
        _rows.AddChild(Label("THE JOURNEY REMEMBERED", 18));
        var filters = new HBoxContainer { Name = "JourneyJournalFilters" }; filters.AddThemeConstantOverride("separation", 6); _rows.AddChild(filters);
        foreach (string category in new[] { "Objectives", "Discoveries", "Choices", "People" })
        {
            var button = new Button { Name = "JourneyJournal" + category, Text = category, ToggleMode = true, ButtonPressed = _journalCategory == category, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 36) };
            button.AddThemeFontSizeOverride("font_size", 13); button.Pressed += () => { OpenJourneyJournal(category); _rows.GetNode<Control>("JourneyJournalFilters/JourneyJournal" + category).GrabFocus(); }; filters.AddChild(button);
        }
        _rows.AddChild(new HSeparator());
        switch (_journalCategory)
        {
            case "Objectives":
                JourneyHeading("CURRENT OBJECTIVE", _view.Region + " · " + (_state.InHub ? "Greyhaven" : "Act " + _state.CurrentAct));
                _rows.AddChild(Label(_objective.Text, 15));
                Button(ChoicePending ? "Review the region's decision" : "View current route", () => { if (ChoicePending) OpenTab("Story"); else { _selectedJourneyRegion = _state.InHub ? 0 : _state.CurrentAct; OpenTab("Map"); } });
                foreach (var act in _content.Acts.Where(a => a.Number <= _state.HighestActVisited))
                {
                    _rows.AddChild(new HSeparator()); _rows.AddChild(Label(act.Name, 15));
                    foreach (var encounter in act.Encounters.Where(e => _state.CompletedEncounters.Contains(e.Id)))
                        _rows.AddChild(Label("✓ " + Readable(encounter.Id), 13));
                    if (!act.Encounters.Any(e => _state.CompletedEncounters.Contains(e.Id))) _rows.AddChild(Label("No encounters completed yet.", 12));
                }
                break;
            case "Discoveries":
                bool any = false;
                foreach (var act in _content.Acts.Where(a => _state.Discoveries.Contains("discovery." + a.Id)))
                {
                    any = true; _rows.AddChild(Label(act.Name, 16));
                    _rows.AddChild(Label(_state.CompletedEncounters.Contains(act.Encounters[^2].Id) ? act.Revelation : "This region's testimony has not yet been recovered.", 13));
                }
                foreach (var exploration in _content.Exploration.Where(e => _state.CompletedExploration.Contains(e.Id)))
                {
                    any = true; _rows.AddChild(Label("✓ " + exploration.Name, 15)); _rows.AddChild(Label(exploration.Id switch
                    {
                        "event.widow_crypt" => "The widow hid her family beneath the monastery when the bells began calling the dead. Her testament names the first resurrection as an experiment, not a miracle. She left a burial mantle for whoever would carry that truth beyond the crypt.",
                        "event.sealed_foundry" => "The foundry workers sealed their furnace to shelter their families from an extraction surge. Their foreman left the Cinderwake Saber beside a roster of those saved. The city remembered its lost output; this testament remembers its people.",
                        "event.briar_shrine" => "Before the roots covered this refuge, an oathstone promised shelter to every plague exile. The names beneath it belong to people the village had turned away. Orrun's Oathseal survived among their offerings: a vow kept even when its keepers were forgotten.",
                        _ => Readable(exploration.Discovery)
                    }, 13));
                }
                if (!any) _rows.AddChild(Label("Discoveries will appear as you explore Edrath. Unvisited regions keep their secrets.", 14));
                break;
            case "Choices":
                bool chosenAny = false;
                foreach (var choice in _content.Choices.Where(c => _state.Choices.ContainsKey(c.Id) || _state.CompletedEncounters.Contains(c.RequiredEncounter)))
                {
                    chosenAny = true; _rows.AddChild(Label(_content.Acts.Single(a => a.Number == choice.Act).Name, 16)); _rows.AddChild(Label(choice.Prompt, 14));
                    if (_state.Choices.TryGetValue(choice.Id, out string? outcome)) _rows.AddChild(Label("Your decision · " + choice.Outcomes.Single(o => o.Id == outcome).Text, 13));
                    else { _rows.AddChild(Label("Decision awaits you in this region.", 13)); if (ChoicePending) Button("Review this decision", () => OpenTab("Story")); }
                    _rows.AddChild(new HSeparator());
                }
                if (!chosenAny) _rows.AddChild(Label("No regional decisions have been reached. Your choices will be recorded here.", 14));
                if (_view.PendingConsequences.Length > 0) _rows.AddChild(Label("Some consequences are still unfolding beyond this region.", 13));
                if (_view.Ending is { } ending) { _rows.AddChild(Label("THE BREACH IS STABLE", 16)); _rows.AddChild(Label(ending.Summary, 13)); }
                break;
            default:
                _rows.AddChild(Label("GREYHAVEN'S PEOPLE", 16));
                foreach (string resident in _view.Residents) _rows.AddChild(Label("✓ " + resident, 14));
                _rows.AddChild(new HSeparator()); foreach (string reaction in _view.HubReactions) _rows.AddChild(Label(reaction, 13));
                break;
        }
    }
}
