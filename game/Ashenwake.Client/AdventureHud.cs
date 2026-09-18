using Ashenwake.Core.Adventure;
using Godot;

namespace Ashenwake.Client;

public sealed record InteractionDisplay(string Id, string Name, int Distance, int Range);

/// <summary>World and service UI. Choices are requests; the ExpeditionSession validates and applies them.</summary>
public partial class AdventureHud : Control
{
    public bool ProductionMode { get; set; }
    public event Action<string>? TravelRequested;
    public event Action<string>? InteractionRequested;
    public event Action<string, string?>? ImplantRequested;
    public event Action<string>? ManifestationRequested;
    public event Action<string>? TemperRequested;
    public event Action<string>? EquipGodwroughtRequested;
    public event Action<string, string>? GraftRequested;
    public event Action? SaveRequested;
    public event Action? LoadRequested;
    private Label _objective = null!, _subtitle = null!, _notice = null!;
    private PanelContainer _panel = null!;
    private VBoxContainer _rows = null!;
    private Button _journey = null!;
    private Button _firstTab = null!;
    private string _tab = "Journey";
    private (long Revision, string Tab, string Room, int RangeMask, int RangeCount)? _renderedKey;
    private long _revision;
    private int _rangeMask;
    private AdventureView _view = null!;
    private AdventureState _state = null!;
    private AdventureDefinition _content = null!;
    private IReadOnlyList<InteractionDisplay> _interactions = [];
    private IReadOnlyDictionary<string, string> _fragmentDescriptions = new Dictionary<string, string>();
    private string _graftInstance = "", _graftLineage = "";
    private ConfirmationDialog _graftDialog = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var objectivePanel = Panel(new(22, 88), new(565, 102));
        var objectiveRows = new VBoxContainer(); objectivePanel.AddChild(objectiveRows);
        _objective = Label("", 16); objectiveRows.AddChild(_objective);
        _subtitle = Label("", 12); objectiveRows.AddChild(_subtitle);
        _notice = Label("", 12); objectiveRows.AddChild(_notice);
        _journey = new Button { Text = "Journey & services [J]", Position = new(921, 22), Size = new(326, 35) };
        _journey.Pressed += Toggle; AddChild(_journey);
        _panel = Panel(new(876, 68), new(371, 554));
        var column = new VBoxContainer(); _panel.AddChild(column);
        var tabs = new HBoxContainer(); column.AddChild(tabs);
        foreach (var name in new[] { "Journey", "Anatomy", "Forge", "Journal" })
        {
            if (ProductionMode && name == "Forge") continue;
            var button = new Button { Text = name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            if (name == "Journey") _firstTab = button;
            button.AddThemeFontSizeOverride("font_size", 12); button.Pressed += () => { _tab = name; Rebuild(true); }; tabs.AddChild(button);
        }
        var scroll = new ScrollContainer { CustomMinimumSize = new(339, 425), SizeFlagsVertical = SizeFlags.ExpandFill }; column.AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(_rows);
        var actions = new HBoxContainer(); column.AddChild(actions);
        foreach (var pair in new[] { ("Save", (Action)(() => SaveRequested?.Invoke())), ("Load", (Action)(() => LoadRequested?.Invoke())), ("Close", (Action)Toggle) })
        { var button = new Button { Text = pair.Item1, SizeFlagsHorizontal = SizeFlags.ExpandFill }; button.Pressed += pair.Item2; actions.AddChild(button); }
        _graftDialog = new ConfirmationDialog
        {
            Title = "Confirm permanent evolution",
            DialogText = "",
            OkButtonText = "Commit evolution",
            CancelButtonText = "Keep current item"
        };
        _graftDialog.Confirmed += () => GraftRequested?.Invoke(_graftInstance, _graftLineage);
        AddChild(_graftDialog); _panel.Visible = false;
    }

    public void Toggle()
    {
        _panel.Visible = !_panel.Visible;
        if (_panel.Visible) { Rebuild(true); _firstTab.GrabFocus(); }
    }
    public void Notice(string text) => _notice.Text = text;
    public void SetFragmentDescriptions(IReadOnlyDictionary<string, string> descriptions) => _fragmentDescriptions = descriptions;
    public void SetView(AdventureView view, AdventureState state, AdventureDefinition content, IReadOnlyList<InteractionDisplay> interactions, long revision)
    {
        bool roomChanged = _view?.RoomId != view.RoomId;
        _view = view; _state = state; _content = content; _interactions = interactions;
        _revision = revision; _rangeMask = 0;
        for (int i = 0; i < interactions.Count; i++)
            if (interactions[i].Distance <= interactions[i].Range) _rangeMask |= 1 << i;
        _objective.Text = $"{view.RoomName.ToUpperInvariant()}  ·  EXPEDITION {view.Expedition}";
        _subtitle.Text = view.Objective;
        _journey.Text = $"Journey & services [J]  ·  {view.Materials} materials";
        if (roomChanged) _panel.Visible = view.RoomId == content.Hub;
        Rebuild(roomChanged);
    }

    private void Rebuild(bool force)
    {
        if (_view is null || !_panel.Visible) return;
        // World actions/events invalidate the presentation revision; movement only changes this key
        // when it crosses a service range boundary. No world serialization occurs on UI refresh.
        var key = (_revision, _tab, _view.RoomId, _rangeMask, _interactions.Count);
        if (!force && _renderedKey == key) return; _renderedKey = key;
        foreach (var child in _rows.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
        switch (_tab)
        {
            case "Journey": Journey(); break;
            case "Anatomy": Anatomy(); break;
            case "Forge": Forge(); break;
            case "Journal": Journal(); break;
        }
    }
    private void Journey()
    {
        _rows.AddChild(Label("THE ROAD TO LAST MERCY", 17));
        _rows.AddChild(Label("Greyhaven → Ossuary → Cloister → Bell Saint", 12));
        foreach (var room in _content.Rooms)
        {
            string marker = room.Id == _view.RoomId ? "● " : _state.Discoveries.Contains(room.Discovery) ? "✓ " : "○ ";
            _rows.AddChild(Label(marker + room.Name + (room.Anchor ? "  ⚑" : ""), 13));
        }
        _rows.AddChild(new HSeparator());
        foreach (var interaction in _interactions)
        {
            bool nearby = interaction.Distance <= interaction.Range;
            var button = Button(interaction.Name + (nearby ? " [F]" : " · approach marker"), () => InteractionRequested?.Invoke(interaction.Id));
            button.Disabled = !nearby || (interaction.Id == "dungeon.replay" && _state.RewardedExpedition != _state.Expedition);
        }
        if (_view.RoomId == _content.Hub)
        {
            _rows.AddChild(Label(_view.NpcReaction, 13));
        }
        _rows.AddChild(new HSeparator()); _rows.AddChild(Label("CONNECTED EXITS", 14));
        foreach (string exit in _view.AvailableExits)
        {
            string destination = _content.Rooms.Single(r => r.Id == exit).Name;
            Button("Travel to " + destination, () => TravelRequested?.Invoke(exit)).Disabled = !_state.QuestAccepted && exit != _content.Hub;
        }
        if (_view.AvailableExits.Length == 0) _rows.AddChild(Label("Clear the current encounter to open the onward route.", 12));
        _rows.AddChild(Label("F interacts with the nearest marker. The cloister anchor preserves your discoveries, equipment and earned rewards after death.", 12));
    }
    private void Anatomy()
    {
        _rows.AddChild(Label($"DIVINE ANATOMY · {_view.Resonance} RESONANCE", 16));
        bool atMara = _interactions.Any(i => i.Id == "service.mara" && i.Distance <= i.Range);
        _rows.AddChild(Label(atMara ? "Choose a compatible owned fragment for each slot." : "Approach Mara in Greyhaven to change implants and Manifestations.", 12));
        foreach (string slot in new[] { "Mind", "Eyes", "Heart", "Spine", "Arms", "Legs" })
        {
            _rows.AddChild(Label(slot, 13));
            var choice = new OptionButton { Disabled = !atMara };
            choice.AddItem("Empty"); choice.SetItemMetadata(0, ""); int selected = 0;
            foreach (var fragment in _content.Fragments.Where(f => f.Slot == slot && _state.OwnedFragments.Contains(f.Id)))
            {
                int index = choice.ItemCount;
                choice.AddItem($"{ReadableName(fragment.Id)} · {fragment.Resonance} R"); choice.SetItemMetadata(index, fragment.Id);
                choice.SetItemTooltip(index, string.Join(" · ", fragment.Tags) + "\n" + _fragmentDescriptions.GetValueOrDefault(fragment.Id, ""));
                if (_state.Anatomy.GetValueOrDefault(slot) == fragment.Id) selected = index;
            }
            choice.Select(selected); choice.ItemSelected += index =>
            { string id = choice.GetItemMetadata((int)index).AsString(); ImplantRequested?.Invoke(slot, id.Length == 0 ? null : id); };
            _rows.AddChild(choice);
            if (_state.Anatomy.TryGetValue(slot, out var equipped) && _fragmentDescriptions.TryGetValue(equipped, out var description))
                _rows.AddChild(Label(description, 12));
        }
        _rows.AddChild(new HSeparator()); _rows.AddChild(Label("MANIFESTATIONS · reversible choices", 14));
        foreach (var manifestation in _content.Manifestations.Where(m => ProductionMode || m.Id is "manifestation.burning_blood" or "manifestation.stone_memory"))
        {
            bool selected = _state.Manifestations.GetValueOrDefault(manifestation.Threshold) == manifestation.Id;
            var button = Button($"{(selected ? "◆ " : "")}{ReadableName(manifestation.Id)} · {manifestation.Threshold} R", () => ManifestationRequested?.Invoke(manifestation.Id));
            button.Disabled = !atMara || _view.Resonance < manifestation.Threshold;
            _rows.AddChild(Label("Benefit: " + manifestation.Benefit + "\nCost: " + manifestation.Complication, 12));
        }
        _rows.AddChild(Label("Keeping low Resonance is a valid choice. Removing implants suppresses a Manifestation below its threshold.", 12));
    }
    private void Forge()
    {
        _rows.AddChild(Label($"THE FORGE · {_view.Materials} MATERIALS", 16));
        bool atTorren = _interactions.Any(i => i.Id == "service.torren" && i.Distance <= i.Range);
        bool atMara = _interactions.Any(i => i.Id == "service.mara" && i.Distance <= i.Range);
        _rows.AddChild(Label("Approach Torren for tempering, or Mara for divine grafting.", 12));
        foreach (var item in _state.Godwrought)
        {
            _rows.AddChild(Label($"ASHCLEAVER · {item.InstanceId}", 14));
            _rows.AddChild(Label($"Burning kills {item.BurningKills}/1000 · temper {item.TemperLevel}/5\n" +
                $"{(item.Awakened ? "Awakened: flame waves unlocked" : "Dormant: burning kills advance awakening")}\n" +
                $"Evolution: {(item.Evolution.Length == 0 ? "uncommitted" : item.Evolution)}", 12));
            int cost = 5 * (item.TemperLevel + 1);
            Button("Equip this Ashcleaver", () => EquipGodwroughtRequested?.Invoke(item.InstanceId));
            Button($"Temper · {cost} materials", () => TemperRequested?.Invoke(item.InstanceId)).Disabled = !atTorren || item.TemperLevel >= 5 || _view.Materials < cost;
            foreach (string lineage in new[] { "Serath", "Orrun" })
            {
                var button = Button($"Evolve toward {lineage} · 20 materials", () =>
                {
                    _graftInstance = item.InstanceId; _graftLineage = lineage;
                    _graftDialog.DialogText = $"Permanently evolve {item.InstanceId} toward {lineage}?\n\nThis costs 20 materials and fixes this item's evolution. The lineage fragment must be owned.\n\nThe current item remains unchanged until you confirm.";
                    _graftDialog.PopupCentered(new(510, 220));
                });
                button.Disabled = !atMara || !item.Awakened || item.Evolution.Length > 0 || _view.Materials < 20;
            }
            _rows.AddChild(new HSeparator());
        }
        _rows.AddChild(Label("Canonical awakening requires 1,000 burning-enemy kills. Development demonstrations are labeled separately.", 12));
    }
    private void Journal()
    {
        _rows.AddChild(Label("JOURNAL & DISCOVERIES", 16));
        foreach (var entry in _state.Journal)
        {
            _rows.AddChild(Label(ReadableName(entry), 14));
            _rows.AddChild(Label(entry switch
            {
                "quest.bell_saint" => "Mara believes the Bell Saint guards a relic of Serath. Beneath Last Mercy, the dead are being called by the wrong names.",
                "discovery.false_history" => "The ossuary tablets contradict the public memorial. Someone replaced the names long before the city fell.",
                "discovery.ritual" => "Two ritual anchors bind the revenants. Break their connection before striking at the Saint again.",
                "quest.bell_saint.defeated" => "The bell has fallen silent. Its Heart can turn the last breath of a burning enemy into an allied spirit.",
                "quest.bell_saint.returned" => "Mara recognizes the new implant. The route to Last Mercy remains open; the next expedition begins with what you learned.",
                _ => "A place remembered. Its discovery persists after death."
            }, 12));
        }
        _rows.AddChild(new HSeparator()); _rows.AddChild(Label("DISCOVERED CONCORDANCES", 14));
        foreach (var id in _state.Concordances) _rows.AddChild(Label(ReadableName(id), 13));
        if (_state.Concordances.Count == 0) _rows.AddChild(Label("Explore combinations of lineage tags to discover a Concordance.", 12));
    }
    private Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        button.AddThemeFontSizeOverride("font_size", 13); button.Pressed += action; _rows.AddChild(button); return button;
    }
    private static string ReadableName(string id) => string.Join(' ', id.Split('.').Skip(1)).Replace('_', ' ');
    private static Label Label(string text, int size)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size); return label;
    }
    private PanelContainer Panel(Vector2 position, Vector2 size)
    {
        var panel = new PanelContainer { Position = position, Size = size };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(.04f, .061f, .084f, .98f),
            BorderColor = new Color("657b82"),
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 10,
            ContentMarginBottom = 10
        });
        AddChild(panel); return panel;
    }
}
