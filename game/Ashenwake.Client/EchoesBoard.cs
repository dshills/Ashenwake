using Ashenwake.Core.Experiments;
using Ashenwake.Core.Simulation;
using Godot;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Ashenwake.Client;

public sealed record EchoesBoardView(EndgameDisplay Expedition, ExperimentView? Experiment, ExperimentRules Rules,
    bool AcceptingEntries, bool CanContinue, string MindName, string MindDescription, string Character, long Revision, bool CanReturn = true);

/// <summary>Read-only contract selection and history; the director executes all archive and gameplay actions.</summary>
public partial class EchoesBoard : Control
{
    public event Action<long, ExperimentChoice>? EntryRequested;
    public event Action? SaveRequested, ContinueRequested, ReturnRequested;
    public event Action<bool>? OpenChanged;
    public PanelContainer Panel { get; private set; } = null!;
    public bool IsOpen => Panel is { Visible: true };
    public long SelectedSigil => _selectedSigil;
    private ColorRect _backdrop = null!;
    private Label _identity = null!, _notice = null!;
    private VBoxContainer _body = null!;
    private ScrollContainer _scroll = null!;
    private readonly Dictionary<string, Button> _tabs = [];
    private EchoesBoardView? _view;
    private string _tab = "Contract";
    private long _selectedSigil;
    private (long Revision, bool Gate, string Tab, long Sigil, bool Continue, bool Admission, bool Hub, bool Unlocked, bool Alive, bool Echoes, bool Return)? _rendered;
    private Vector2 _viewport = new(-1, -1);
    private int _oldSibling = -1;
    private static readonly Color Mint = new("add7ca"), Blue = new("accdf0"), Bone = new("e9ddc4"), Gold = new("efd08b");

    public override void _Ready()
    {
        Name = "EchoesBoard"; MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _backdrop = new ColorRect { Color = new(0, 0, 0, .72f), MouseFilter = MouseFilterEnum.Stop, MouseForcePassScrollEvents = false };
        AddChild(_backdrop); _backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Panel = Card("EchoesPanel", new("718ca9")); Panel.MouseForcePassScrollEvents = false; AddChild(Panel);
        var column = Stack(12); Panel.AddChild(column);
        var header = new HBoxContainer(); header.AddThemeConstantOverride("separation", 14); column.AddChild(header);
        header.AddChild(new EchoesSeal { Kind = "borrow", CustomMinimumSize = new(54, 54) });
        var headings = Stack(3); headings.SizeFlagsHorizontal = SizeFlags.ExpandFill; header.AddChild(headings);
        headings.AddChild(Text("ECHOES: BORROWED MEMORY", 22, Bone));
        _identity = Text("", 12, Blue); _identity.Name = "EchoesCharacterIdentity"; headings.AddChild(_identity);
        var tabs = new HBoxContainer(); tabs.AddThemeConstantOverride("separation", 6); column.AddChild(tabs);
        var group = new ButtonGroup();
        foreach (string tab in new[] { "Contract", "Record", "Character" })
        {
            var button = ActionButton("EchoesTab" + tab, tab, () => ShowTab(tab));
            button.ToggleMode = true; button.ButtonGroup = group; tabs.AddChild(button); _tabs.Add(tab, button);
        }
        _scroll = new ScrollContainer { Name = "EchoesScroll", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(_scroll); _body = Stack(12); _body.SizeFlagsHorizontal = SizeFlags.ExpandFill; _scroll.AddChild(_body);
        _notice = Text("", 12, Gold); _notice.Name = "EchoesNotice"; _notice.MaxLinesVisible = 2; _notice.Visible = false; column.AddChild(_notice);
        var close = ActionButton("EchoesClose", "Close [Esc]", () => SetOpen(false)); column.AddChild(close);
        Panel.Visible = _backdrop.Visible = false;
        UpdateLayout();
    }

    public override void _Process(double delta) => UpdateLayout();
    private void UpdateLayout()
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        if (Panel is null) return;
        var size = new Vector2(Math.Min(1060, viewport.X - 36), Math.Min(690, viewport.Y - 36));
        var origin = (viewport - size) / 2;
        if (_viewport == viewport && Panel.Size == size && Panel.Position == origin) return;
        _viewport = viewport;
        // Wrapped labels can report a tall minimum before their first container layout.
        // Reapply the target bounds after that layout settles instead of caching the transient size.
        Panel.Size = size; Panel.Position = origin;
    }
    public void SetView(EchoesBoardView view)
    {
        _view = view;
        if (!view.Expedition.Sigils.Any(s => s.Id == _selectedSigil)) _selectedSigil = view.Expedition.Sigils.FirstOrDefault()?.Id ?? 0;
        if (IsOpen) Rebuild();
    }
    public void SetOpen(bool open)
    {
        Panel.Visible = _backdrop.Visible = open;
        if (open)
        {
            if (_oldSibling < 0) { _oldSibling = GetIndex(); GetParent().MoveChild(this, GetParent().GetChildCount() - 1); }
            Rebuild(true); _tabs[_tab].GrabFocus();
        }
        else if (_oldSibling >= 0)
        { GetParent().MoveChild(this, Math.Min(_oldSibling, GetParent().GetChildCount() - 1)); _oldSibling = -1; }
        OpenChanged?.Invoke(open);
    }
    public void ShowTab(string tab)
    { if (!_tabs.ContainsKey(tab)) return; _tab = tab; _scroll.ScrollVertical = 0; SetOpen(true); }
    public void SelectSigil(long id)
    { if (_view?.Expedition.Sigils.Any(s => s.Id == id) != true) return; _selectedSigil = id; Rebuild(true); }
    public void SessionRestored()
    { _rendered = null; _selectedSigil = 0; if (IsOpen) SetOpen(false); }
    public void Notice(string message)
    { _notice.Text = _notice.TooltipText = message; _notice.Visible = message.Length > 0; }

    private void Rebuild(bool force = false)
    {
        if (_view is null) return;
        var key = (_view.Revision, _view.Expedition.AtGate, _tab, _selectedSigil, _view.CanContinue, _view.AcceptingEntries,
            _view.Expedition.InHub, _view.Expedition.Unlocked, _view.Expedition.Alive, _view.Experiment is not null, _view.CanReturn);
        if (!force && _rendered == key) return;
        _rendered = key;
        foreach (var child in _body.GetChildren()) { _body.RemoveChild(child); child.QueueFree(); }
        foreach (var (tab, button) in _tabs) button.SetPressedNoSignal(tab == _tab);
        _identity.Text = _view.Character + (_view.Experiment is null ? " · Original character" : " · Echoes character");
        switch (_tab) { case "Record": BuildRecord(); break; case "Character": BuildCharacter(); break; default: BuildContract(); break; }
    }

    private void BuildContract()
    {
        var view = _view!; var rules = view.Rules;
        _body.AddChild(Text("One Fracture. Two ways to enter.", 19, Bone));
        _body.AddChild(Text("Both choices use an owned Sigil and the usual Fracture attempts and rewards. Your original character is preserved; this journey continues as a separate Echoes character.", 13));
        var choices = new HBoxContainer(); choices.AddThemeConstantOverride("separation", 12); _body.AddChild(choices);
        ChoiceCard(choices, false, "Keep my Mind", "YOUR ANATOMY, INTACT", Mint,
            view.MindName, ReadableDuration(view.MindDescription) + "\n\nKeep its effect throughout an ordinary Fracture. This choice does not earn the Borrowed Memory badge.");
        ChoiceCard(choices, true, "Borrow a memory", "TRADE AN EFFECT FOR ONE ECHO", Blue,
            "Echo Storm · one use",
            "Suppress your owned Mind effect while the loan is active. Defeat an elite, approach its memory, then bind it. Your fragment stays installed and your Resonance stays intact.\n\n" +
            $"Use the Echo within {rules.EchoLifetimeTicks * FixedStepClock.SecondsPerTick:0.#} seconds of combat. Casting creates a hostile Storm at your feet: {rules.WarningTicks * FixedStepClock.SecondsPerTick:0.#}s warning, then {rules.HazardTicks * FixedStepClock.SecondsPerTick:0.#}s danger. Move or dodge out.");
        var note = Card("EchoesRestoration", new("4e6875")); _body.AddChild(note); var words = Stack(4); note.AddChild(words);
        words.AddChild(Text("Your Mind effect returns when the loan ends", 14, Mint));
        words.AddChild(Text("Cast, release, expiry, death, or leaving the Fracture ends suppression. Bind and cast the Echo, then complete the Fracture to earn a cosmetic badge. The badge adds no combat power.", 12));
        _body.AddChild(Text("SELECT A SIGIL", 13, Gold));
        if (view.Expedition.Sigils.Length == 0) _body.AddChild(Text(view.Expedition.Unlocked ? "No Sigil available. Claim a recovery Sigil at the expedition board." : "Complete the campaign to unlock Fractures.", 13));
        var sigils = new HFlowContainer(); sigils.AddThemeConstantOverride("h_separation", 8); sigils.AddThemeConstantOverride("v_separation", 8); _body.AddChild(sigils);
        foreach (var sigil in view.Expedition.Sigils)
        {
            long id = sigil.Id;
            var card = ActionButton("EchoesSigil" + id, $"TIER {sigil.Tier} · {sigil.Region}\nSigil #{id}", () => SelectSigil(id));
            card.CustomMinimumSize = new(216, 56); card.SizeFlagsHorizontal = SizeFlags.Fill;
            card.ToggleMode = true; card.SetPressedNoSignal(id == _selectedSigil);
            card.TooltipText = $"{sigil.BossFamily}\n{sigil.RewardTendency}\n" + string.Join("\n", sigil.Modifiers.Select(m => m.Name + ": " + m.Description));
            sigils.AddChild(card);
        }
        var selected = view.Expedition.Sigils.FirstOrDefault(s => s.Id == _selectedSigil);
        if (selected is not null)
        {
            _body.AddChild(Text($"Selected: Tier {selected.Tier} · {selected.Region} · Sigil #{selected.Id}", 14, Gold));
            _body.AddChild(Text(string.Join(" · ", selected.Modifiers.Select(m => m.Name)), 12));
        }
        string gate = EntryGate(view);
        var status = Text(gate.Length > 0 ? gate : "Entering consumes the selected Sigil. Choose the contract you want to play.", 13, gate.Length > 0 ? Gold : Mint);
        status.Name = "EchoesEntryStatus"; _body.AddChild(status);
        var actions = new HBoxContainer(); actions.AddThemeConstantOverride("separation", 12); _body.AddChild(actions);
        var keep = ActionButton("EchoesKeep", "Keep my Mind · consume Sigil & enter", () => RequestEntry(ExperimentChoice.KeepMind));
        var borrow = ActionButton("EchoesBorrow", "Borrow a memory · consume Sigil & enter", () => RequestEntry(ExperimentChoice.BorrowMind));
        keep.Disabled = gate.Length > 0; borrow.Disabled = keep.Disabled || !view.AcceptingEntries;
        actions.AddChild(keep); actions.AddChild(borrow);
        if (!view.AcceptingEntries) _body.AddChild(Text("Borrowed Memory is closed to new entries. Keep my Mind remains available, and existing contracts can still finish or resume.", 13, Gold));
        if (view.Experiment?.Memory is { } memory)
            _body.AddChild(Text("Current memory: " + memory.Status + " · " + (memory.SuppressedMindId.Length > 0 ? "owned Mind suppressed" : memory.CanRelease ? "empty Mind socket on loan" : "loan ended; owned anatomy active"), 13, Blue));
    }
    private static string EntryGate(EchoesBoardView view)
    {
        if (!view.Expedition.Unlocked) return "Complete the campaign to unlock this contract.";
        if (!view.Expedition.InHub) return "Finish or abandon the expedition, then return to Greyhaven to start another contract.";
        if (!view.Expedition.Alive) return "Recover before starting another contract.";
        if (view.Expedition.Sigils.Length == 0) return "Claim a recovery Sigil at the expedition board first.";
        return view.Expedition.AtGate ? "" : "Approach the Fracture gate in eastern Greyhaven to enter.";
    }
    private void RequestEntry(ExperimentChoice choice)
    {
        if (_view is null || EntryGate(_view).Length > 0 || !_view.Expedition.Sigils.Any(s => s.Id == _selectedSigil) ||
            choice == ExperimentChoice.BorrowMind && !_view.AcceptingEntries) return;
        EntryRequested?.Invoke(_selectedSigil, choice);
    }

    private void ChoiceCard(HBoxContainer parent, bool borrow, string title, string eyebrow, Color accent, string effect, string detail)
    {
        var card = Card(borrow ? "EchoesBorrowCard" : "EchoesKeepCard", accent.Darkened(.25f));
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill; parent.AddChild(card);
        var content = Stack(8); card.AddChild(content);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); content.AddChild(row);
        row.AddChild(new EchoesSeal { Kind = borrow ? "borrow" : "mind", CustomMinimumSize = new(52, 52) });
        var labels = Stack(4); labels.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(labels);
        labels.AddChild(Text(eyebrow, 10, accent)); labels.AddChild(Text(title, 20, Bone));
        content.AddChild(Text(effect, 14, accent)); content.AddChild(Text(detail, 13));
    }
    private void BuildRecord()
    {
        var view = _view!;
        bool earned = view.Experiment?.Cosmetics.Contains(view.Rules.CosmeticId) == true;
        var card = Card("EchoesCosmetic", earned ? Gold : new("46596b")); _body.AddChild(card);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 16); card.AddChild(row);
        row.AddChild(new EchoesSeal { Kind = earned ? "earned" : "locked", CustomMinimumSize = new(88, 88) });
        var column = Stack(7); column.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(column);
        column.AddChild(Text(earned ? "BORROWED MEMORY · EARNED" : "BORROWED MEMORY · NOT YET EARNED", 18, earned ? Gold : Blue));
        column.AddChild(Text("A record of the mind you borrowed and the Storm you survived.", 14));
        column.AddChild(Text("Bind a memory, cast its Echo, and complete that Fracture. Cosmetic recognition only; no additional power or currency.", 13));
        _body.AddChild(Text("CONTRACT HISTORY", 15, Bone));
        var entries = view.Experiment?.Entries;
        if (entries is null || entries.Count == 0)
            _body.AddChild(Text(view.Experiment is null ? "Continue your saved Echoes character to view its records, or begin a contract to start a new history." : "No contracts recorded yet.", 13));
        else foreach (var entry in entries.Reverse())
            {
                var history = Card("EchoesHistory" + entry.RunId, new("415768")); _body.AddChild(history);
                var words = Stack(5); history.AddChild(words);
                words.AddChild(Text($"{(entry.Choice == ExperimentChoice.KeepMind ? "Kept my Mind" : "Borrowed a memory")}  ·  {entry.Outcome}", 15, entry.Outcome == "Completed" ? Mint : Blue));
                words.AddChild(Text($"Fracture #{entry.RunId} · Sigil #{entry.SigilId}", 12));
            }
    }
    private void BuildCharacter()
    {
        var view = _view!; bool active = view.Experiment is not null;
        _body.AddChild(Text(active ? "Your Echoes character" : "Your original character", 22, Bone));
        _body.AddChild(Text(view.Character, 16, Blue));
        var card = Card("EchoesArchiveNotice", new("6c889c")); _body.AddChild(card); var column = Stack(10); card.AddChild(column);
        column.AddChild(Text("Two separate journeys", 18, Mint));
        column.AddChild(Text("Beginning either contract creates an Echoes character from your current character. Its equipment, progress, choices and cosmetic record are saved together. Your original character remains where you left it. Progress earned in Echoes stays with that character.", 14));
        if (active)
        {
            _body.AddChild(ActionButton("EchoesSave", "Save Echoes character", () => SaveRequested?.Invoke()));
            var back = ActionButton("EchoesReturn", "Save Echoes & return to original character", () => ReturnRequested?.Invoke());
            back.Disabled = !view.Expedition.InHub || !view.CanReturn; _body.AddChild(back);
            _body.AddChild(Text(!view.CanReturn ? "This archive has no linked original. Save & main menu lets you choose another character." : view.Expedition.InHub ? "You can continue this saved Echoes character from the Echoes screen later." : "Return becomes available in Greyhaven. Finish or abandon the active expedition first.", 13, Gold));
        }
        else
        {
            var resume = ActionButton("EchoesContinue", "Continue saved Echoes character", () => ContinueRequested?.Invoke());
            resume.Disabled = !view.CanContinue || !view.Expedition.InHub; _body.AddChild(resume);
            _body.AddChild(Text(!view.CanContinue ? "No saved Echoes character is available yet." : !view.Expedition.InHub ? "Return to Greyhaven before switching characters." : "Your original character will be saved before you switch journeys.", 13, Gold));
        }
    }

    private static VBoxContainer Stack(int gap = 8)
    { var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", gap); return box; }
    private static string ReadableDuration(string description) => Regex.Replace(description, @"\b(\d+) ticks\b", match =>
        double.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out double ticks)
            ? (ticks * FixedStepClock.SecondsPerTick).ToString("0.#", CultureInfo.InvariantCulture) + " seconds" : match.Value);
    private static Label Text(string text, int size, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", color ?? new Color("c7d1d8")); return label;
    }
    private static Button ActionButton(string name, string text, Action action)
    {
        var button = new Button { Name = name, Text = text, CustomMinimumSize = new(0, 36), SizeFlagsHorizontal = SizeFlags.ExpandFill, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = text };
        button.AddThemeFontSizeOverride("font_size", 13); button.Pressed += action; return button;
    }
    private static PanelContainer Card(string name, Color border)
    {
        var panel = new PanelContainer { Name = name };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new("11232feb"),
            BorderColor = border,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            CornerRadiusTopLeft = 7,
            CornerRadiusTopRight = 7,
            CornerRadiusBottomLeft = 7,
            CornerRadiusBottomRight = 7,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 12,
            ContentMarginBottom = 12
        }); return panel;
    }
}
