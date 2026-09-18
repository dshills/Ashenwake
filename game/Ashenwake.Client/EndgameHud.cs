using Ashenwake.Core.Endgame;
using Godot;

namespace Ashenwake.Client;

public sealed record EndgameChoice(string Id, string Name, string Description);
public sealed record EndgameSigilDisplay(long Id, string Region, int Tier, ulong Seed, string BossFamily,
    string RewardTendency, EndgameChoice[] Modifiers, IReadOnlyDictionary<string, EndgameChoice[]> Replacements, string[] Rooms, string[] Inherited, string[] Skipped);
public sealed record EndgameHuntDisplay(string Id, string Name, int RequiredTier, bool Secret, bool Unlocked,
    string Gate, string[] Phases, string[] Counterplay, string Material);
public sealed record EndgameRunDisplay(long Id, string Name, string Kind, string Status, string Region, int Tier,
    int Room, int RoomCount, int Attempts, int Deaths, int RewardPercent, string[] Rules, string[] Counterplay,
    string[] Inherited, string[] Skipped, bool Cleared, bool CanAdvance, bool CanRetry, bool CanAbandon);
public sealed record EndgameDisplay(bool Unlocked, bool InHub, int HighestTier, int Materials,
    IReadOnlyDictionary<string, int> Catalysts, EndgameSigilDisplay[] Sigils, EndgameHuntDisplay[] Hunts,
    EndgameRunDisplay? Run, bool CanRecover, bool AtGate, int GroundDrops, string RewardSummary, long Revision);

/// <summary>Read-only expedition presentation. Buttons request transactions from the endgame runtime.</summary>
public partial class EndgameHud : Control
{
    public event Action<long>? FractureRequested;
    public event Action<long, string, string>? AttuneRequested;
    public event Action<string>? HuntRequested;
    public event Action? RecoveryRequested, AdvanceRequested, RetryRequested, AbandonRequested, HubRequested;
    public event Action? SaveRequested, LoadRequested, ReplayRequested, ImportRequested;
    public event Action<bool>? VisibilityChangedByPlayer, ModalChanged;
    private EndgameDisplay? _view;
    private PanelContainer _panel = null!, _headlinePanel = null!;
    private VBoxContainer _rows = null!;
    private Label _title = null!, _status = null!, _notice = null!;
    private readonly Dictionary<string, Button> _tabs = [];
    private ConfirmationDialog _confirmation = null!;
    private Action? _pendingAction;
    private string _tab = "Sigils", _oldModifier = "", _newModifier = "";
    private long _selectedSigil;
    private bool _unlockPresented;
    private (long Revision, string Tab, long Sigil, int Drops, bool AtGate)? _rendered;
    public bool IsOpen => _panel.Visible;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _headlinePanel = Panel(new(22, 88), new(565, 104));
        var heading = new VBoxContainer(); _headlinePanel.AddChild(heading);
        _title = Text("THE FRACTURES", 16); heading.AddChild(_title);
        _status = Text("", 12); heading.AddChild(_status);
        _notice = Text("", 12); _notice.MaxLinesVisible = 1; heading.AddChild(_notice);
        var toggle = new Button { Text = "Fractures & God Hunts [B]", Position = new(921, 61), Size = new(326, 32) };
        toggle.AddThemeFontSizeOverride("font_size", 13); toggle.Pressed += Toggle; AddChild(toggle);
        _panel = Panel(new(810, 140), new(437, 482));
        var column = new VBoxContainer(); _panel.AddChild(column);
        var tabs = new HBoxContainer(); column.AddChild(tabs);
        var tabGroup = new ButtonGroup();
        foreach (string tab in new[] { "Sigils", "Hunts", "Run", "Rewards" })
        {
            var button = new Button { Text = tab, SizeFlagsHorizontal = SizeFlags.ExpandFill, ToggleMode = true, ButtonGroup = tabGroup };
            button.AddThemeFontSizeOverride("font_size", 12); button.Pressed += () => { _tab = tab; Rebuild(true); }; tabs.AddChild(button);
            _tabs.Add(tab, button);
        }
        var scroll = new ScrollContainer { CustomMinimumSize = new(405, 382), SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(scroll); _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(_rows);
        var footer = new HBoxContainer(); column.AddChild(footer);
        foreach (var (name, action) in new[] { ("Save", (Action)(() => SaveRequested?.Invoke())), ("Load", (Action)(() => LoadRequested?.Invoke())), ("Close", (Action)(() => SetOpen(false))) })
        { var button = new Button { Text = name, SizeFlagsHorizontal = SizeFlags.ExpandFill }; button.Pressed += action; footer.AddChild(button); }
        _confirmation = new ConfirmationDialog { Title = "Leave this expedition", OkButtonText = "Abandon expedition", CancelButtonText = "Keep playing" };
        _confirmation.Confirmed += () => { var action = _pendingAction; _pendingAction = null; action?.Invoke(); ModalChanged?.Invoke(false); };
        _confirmation.Canceled += () => { _pendingAction = null; ModalChanged?.Invoke(false); }; AddChild(_confirmation);
        _panel.Visible = false; _headlinePanel.Visible = false;
    }
    public void Toggle() => SetOpen(!IsOpen);
    public void SetOpen(bool open)
    {
        _panel.Visible = open; _headlinePanel.Visible = open || _view?.Run is { Status: "Active" };
        if (open) { Rebuild(true); _tabs[_tab].GrabFocus(); }
        VisibilityChangedByPlayer?.Invoke(open);
    }
    public void ShowRun() { _tab = "Run"; SetOpen(true); }
    public void Notice(string message) { _notice.Text = message; _notice.TooltipText = message; }
    public void SetView(EndgameDisplay view)
    {
        if (!view.Unlocked) _unlockPresented = false;
        bool unlocked = view.Unlocked && view.InHub && !_unlockPresented;
        if (unlocked) _unlockPresented = true;
        bool entered = view.Run is { Status: "Active" } && (_view?.Run?.Id != view.Run.Id || _view.Run.Status != "Active");
        bool died = view.Run is { } run && _view?.Run?.Id == run.Id && _view.Run.Deaths < run.Deaths;
        bool ended = _view?.Run is { Status: "Active" } && view.Run is { Status: not "Active" };
        if (view.Run is { } next && (_view?.Run?.Id != next.Id || _view.Run.Room != next.Room)) _notice.Text = "";
        _view = view;
        if (entered || died || ended) { _tab = "Run"; SetOpen(true); }
        else if (unlocked) { _tab = "Sigils"; SetOpen(true); }
        var current = view.Run;
        bool active = current?.Status == "Active";
        _headlinePanel.Visible = IsOpen || active;
        _title.Text = active ? $"{current!.Name.ToUpperInvariant()} · {(current.Kind == "Fracture" ? "ROOM" : "PHASE")} {current.Room}/{current.RoomCount}" : "THE FRACTURES · GOD HUNTS";
        _status.Text = active ? $"{current!.Attempts} attempts left · {current.RewardPercent}% base material reward · {(current.Cleared ? "Area cleared; collect spoils or continue." : "Read the marked dangers and current counterplay.")}" :
            view.Unlocked ? $"Highest cleared tier {view.HighestTier} · {view.Sigils.Length} Sigils · {view.Materials} common materials" : "Complete the campaign to unlock expeditions. Your character and discoveries continue here.";
        Rebuild(false);
    }
    private void Rebuild(bool force)
    {
        if (_view is null || !IsOpen) return;
        var key = (_view.Revision, _tab, _selectedSigil, _view.GroundDrops, _view.AtGate);
        if (!force && _rendered == key) return; _rendered = key;
        foreach (var tab in _tabs) tab.Value.SetPressedNoSignal(tab.Key == _tab);
        foreach (var child in _rows.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
        if (!_view.Unlocked)
        {
            Row("A FUTURE BEYOND THE BREACH", 17);
            Row("Continue the five-act campaign. Its ending unlocks Fractures and the path to God Hunts for this character.");
            Button("Import an existing campaign save", () => ImportRequested?.Invoke());
            return;
        }
        switch (_tab) { case "Sigils": Sigils(); break; case "Hunts": Hunts(); break; case "Run": Run(); break; default: Rewards(); break; }
    }
    private void Sigils()
    {
        var view = _view!;
        Row("CHOOSE A FRACTURE SIGIL", 17);
        Row("A Sigil is consumed on entry. Clear four rooms with three attempts; each death reduces the base material reward by 20%, to a minimum of 40%.");
        if (view.InHub && !view.AtGate) Row("Approach the Fracture gate in eastern Greyhaven to claim, attune or consume a Sigil.");
        if (!view.InHub) { Row("Return to Greyhaven to select or attune your next expedition."); Button("Return to Greyhaven", () => HubRequested?.Invoke()).Disabled = view.Run is { Status: "Active" }; }
        if (view.Sigils.Length == 0)
        {
            Row("No unconsumed Sigils remain. A recovery Sigil begins a new tier-1 route without a material cost.");
            Button("Claim a tier-1 recovery Sigil", () => RecoveryRequested?.Invoke()).Disabled = !view.CanRecover || !view.AtGate;
            return;
        }
        if (!view.Sigils.Any(s => s.Id == _selectedSigil)) { _selectedSigil = view.Sigils[0].Id; _oldModifier = _newModifier = ""; }
        var choice = new OptionButton();
        foreach (var sigil in view.Sigils)
        {
            int index = choice.ItemCount; choice.AddItem($"Tier {sigil.Tier} · {sigil.Region} · {sigil.BossFamily} · #{sigil.Id}"); choice.SetItemMetadata(index, sigil.Id);
            if (sigil.Id == _selectedSigil) choice.Select(index);
        }
        choice.ItemSelected += index => { _selectedSigil = choice.GetItemMetadata((int)index).AsInt64(); _oldModifier = _newModifier = ""; Rebuild(true); };
        _rows.AddChild(choice);
        var selected = view.Sigils.Single(s => s.Id == _selectedSigil);
        Row($"{selected.Region} · TIER {selected.Tier}", 15);
        Row($"Boss family: {selected.BossFamily}\nReward tendency: {selected.RewardTendency}\nGeneration seed: {selected.Seed}");
        foreach (var modifier in selected.Modifiers) { Row(modifier.Name, 14); Row(modifier.Description); }
        Row("Route: " + string.Join(" → ", selected.Rooms));
        if (selected.Inherited.Length > 0) Row("Boss inheritance: " + string.Join(", ", selected.Inherited));
        foreach (string skipped in selected.Skipped) Row("Skipped: " + skipped);
        Button("Consume this Sigil & enter", () => FractureRequested?.Invoke(selected.Id)).Disabled = !view.InHub || !view.AtGate || view.Run is { Status: "Active" };
        Rule(); Row("ATTUNEMENT · 5 COMMON MATERIALS", 14);
        Row($"Available: {view.Materials}. Replace one rule before consuming the Sigil. Only compatible rules for this tier are listed.");
        if (selected.Modifiers.Length == 0) { Row("This Sigil has no replaceable modifier."); return; }
        if (!selected.Modifiers.Any(m => m.Id == _oldModifier)) _oldModifier = selected.Modifiers[0].Id;
        var old = Choice(selected.Modifiers, _oldModifier, id => { _oldModifier = id; _newModifier = ""; Rebuild(true); });
        old.Disabled = !view.InHub || !view.AtGate || view.Run is { Status: "Active" };
        var replacements = selected.Replacements.GetValueOrDefault(_oldModifier, []);
        if (replacements.Length == 0) { Row("No other compatible rule is available at this tier."); return; }
        if (!replacements.Any(m => m.Id == _newModifier)) _newModifier = replacements[0].Id;
        Choice(replacements, _newModifier, id => { _newModifier = id; Rebuild(true); });
        Row(replacements.Single(m => m.Id == _newModifier).Description);
        Button("Attune · spend 5 common materials", () => AttuneRequested?.Invoke(selected.Id, _oldModifier, _newModifier)).Disabled = !view.InHub || !view.AtGate || view.Materials < 5 || view.Run is { Status: "Active" };
    }
    private void Hunts()
    {
        var view = _view!; Row("GOD HUNTS", 18);
        if (view.InHub && !view.AtGate) Row("Approach the Fracture gate in eastern Greyhaven to enter a hunt.");
        Row("These are incomplete reconstructions of dead gods. Each hunt has three encounters and two attempts. Success grants a permanent evolution catalyst.");
        foreach (var hunt in view.Hunts.Where(h => !h.Secret || h.Unlocked))
        {
            Rule(); Row(hunt.Name, 15); Row(hunt.Unlocked ? "Available · 2 attempts" : hunt.Gate);
            for (int i = 0; i < hunt.Phases.Length; i++) Row($"{i + 1}. {Readable(hunt.Phases[i])}\n{hunt.Counterplay[i]}");
            Row("Evolution catalyst: " + Readable(hunt.Material));
            Button("Begin " + hunt.Name, () => HuntRequested?.Invoke(hunt.Id)).Disabled = !hunt.Unlocked || !view.InHub || !view.AtGate || view.Run is { Status: "Active" };
        }
        if (view.Hunts.Any(h => h.Secret && !h.Unlocked)) { Rule(); Row("An unremembered shape remains hidden. Explore higher tiers and complete the four known God Hunts."); }
    }
    private void Run()
    {
        var view = _view!; var run = view.Run;
        if (run is null) { Row("No expedition has begun. Choose a Sigil or an unlocked God Hunt."); return; }
        Row(run.Name, 18); Row($"{run.Kind} · {run.Status} · tier {run.Tier}\n{run.Region}");
        Row($"{(run.Kind == "Fracture" ? "Room" : "Phase")} {Math.Min(run.Room, run.RoomCount)}/{run.RoomCount} · {run.Attempts} attempts left · {run.Deaths} deaths\nBase material reward: {run.RewardPercent}%");
        foreach (string rule in run.Rules) Row(rule, 14);
        foreach (string counterplay in run.Counterplay) Row(counterplay);
        if (run.Inherited.Length > 0) { Rule(); Row("FINAL BOSS INHERITANCE · AT MOST TWO", 14); foreach (string modifier in run.Inherited) Row(modifier); }
        foreach (string skipped in run.Skipped) Row("Skipped inheritance: " + skipped);
        string drops = view.GroundDrops > 0 ? $" · leave {view.GroundDrops} uncollected {(view.GroundDrops == 1 ? "drop" : "drops")}" : "";
        if (run.Status == "Active")
        {
            Button((run.Room >= run.RoomCount ? "Finish expedition" : "Continue to next room") + drops, () => AdvanceRequested?.Invoke()).Disabled = !run.CanAdvance;
            Button("Retry this encounter", () => RetryRequested?.Invoke()).Disabled = !run.CanRetry;
            Button("Abandon expedition", () =>
            {
                _pendingAction = () => AbandonRequested?.Invoke();
                _confirmation.DialogText = "End this expedition without its final reward? Your earned character progress is preserved. A consumed Sigil is not returned." +
                    (view.GroundDrops > 0 ? $"\n\n{view.GroundDrops} ground drops will be left behind." : "");
                ModalChanged?.Invoke(true); _confirmation.PopupCentered(new(520, 240));
            }).Disabled = !run.CanAbandon;
            Row("Save to resume this exact encounter later. Returning to the application does not restore spent attempts or consumed Sigils.");
        }
        else
        {
            Row(run.Status == "Completed" ? view.RewardSummary : "This expedition has ended. You can return to Greyhaven and choose another Sigil or claim a recovery Sigil when none remain.");
            Button("Return to Greyhaven" + drops, () => HubRequested?.Invoke()).Disabled = view.InHub;
        }
        Rule(); Button("Verify & export replay", () => ReplayRequested?.Invoke());
    }
    private void Rewards()
    {
        var view = _view!; Row("PERMANENT REWARDS", 18);
        Row($"Common materials: {view.Materials}\nHighest cleared Fracture: tier {view.HighestTier}");
        Row(view.RewardSummary);
        Rule(); Row("EVOLUTION CATALYSTS", 15);
        foreach (var pair in view.Catalysts.Where(p => p.Value > 0)) Row($"{Readable(pair.Key)} · {pair.Value}");
        if (!view.Catalysts.Any(p => p.Value > 0)) Row("Complete a God Hunt to earn its matching catalyst.");
        Rule(); Row("ASHCLEAVER · A PERMANENT CHOICE", 15);
        Row("Awaken Ashcleaver through 1,000 credited burning kills, learn the matching lineage fragment, then approach Mara in Greyhaven. Character → Craft → Divine Grafting shows the exact requirements and spends the catalyst only when the permanent craft succeeds.");
        Row("Serath: burning victims can rise as temporary flaming revenants.\nOrrun: the awakened flame wave becomes a molten seismic attack that ignites enemies.");
        Row("Sigils, catalysts, attempts and rewards are included in the same character save. Repeated loading never grants the same expedition reward again.");
    }
    private OptionButton Choice(EndgameChoice[] choices, string selected, Action<string> action)
    {
        var button = new OptionButton();
        foreach (var choice in choices) { int index = button.ItemCount; button.AddItem(choice.Name); button.SetItemMetadata(index, choice.Id); button.SetItemTooltip(index, choice.Description); if (choice.Id == selected) button.Select(index); }
        button.ItemSelected += index => action(button.GetItemMetadata((int)index).AsString()); _rows.AddChild(button); return button;
    }
    private Button Button(string text, Action action)
    { var button = new Button { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart }; button.AddThemeFontSizeOverride("font_size", 12); button.Pressed += action; _rows.AddChild(button); return button; }
    private void Row(string text, int size = 12) => _rows.AddChild(Text(text, size));
    private void Rule() => _rows.AddChild(new HSeparator());
    private static string Readable(string id) => string.Join(' ', id.Split('.').Skip(1).DefaultIfEmpty(id)).Replace('_', ' ');
    private static Label Text(string text, int size) => new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, LabelSettings = new LabelSettings { FontSize = size } };
    private PanelContainer Panel(Vector2 position, Vector2 size)
    {
        var panel = new PanelContainer { Position = position, Size = size };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(.035f, .052f, .075f, .98f), BorderColor = new Color("8a9b9a"), BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10 });
        AddChild(panel); return panel;
    }
}
