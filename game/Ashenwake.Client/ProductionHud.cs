using Ashenwake.Core.Combat;
using Ashenwake.Core.Authoring;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Permanent progression UI; all changes are requests validated by ProductionSession.</summary>
public partial class ProductionHud : Control
{
    public TextCatalog Catalog { get; set; } = null!;
    public event Action<string>? RetrainRequested;
    public event Action<string>? PassiveRequested;
    public event Action? RespecRequested;
    public event Action<long, EquipmentSlot>? EquipRequested;
    public event Action<EquipmentSlot>? UnequipRequested;
    public event Action<CraftingRequest>? CraftRequested;
    public event Action<string, string>? MutationRequested;
    public event Action<string>? ServiceRequested;
    private ProgressionView _view = null!;
    private ProgressionSnapshot _state = null!;
    private ProgressionDefinition _content = null!;
    private CombatView _combat = null!;
    private IReadOnlyList<InteractionDisplay> _interactions = [];
    private IReadOnlyList<string>? _unlockedMutations;
    private PanelContainer _panel = null!;
    private VBoxContainer _rows = null!;
    private Label _summary = null!, _notice = null!;
    private Button _firstTab = null!;
    private string _tab = "Character";
    private long _revision, _renderedRevision = -1;
    private int _rangeMask, _renderedRangeMask = -1;
    private bool _inTown;
    private long _craftItem;
    private CraftingService _service = CraftingService.Tempering;
    private ConfirmationDialog _confirmation = null!;
    private CraftingRequest? _pendingCraft;
    private bool _endgameCrafting;
    private IReadOnlyDictionary<string, int> _catalysts = new Dictionary<string, int>();
    private string _craftCatalyst = "", _craftLineage = "Serath";
    public void SetEndgameMaterials(bool enabled, IReadOnlyDictionary<string, int> catalysts)
    { _endgameCrafting = enabled; _catalysts = catalysts; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _summary = Label("", 13); _summary.Position = new(603, 23); _summary.Size = new(305, 36); AddChild(_summary);
        var toggle = new Button { Text = Catalog.Format("production.title") + " [C]", Position = new(601, 61), Size = new(266, 32), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        toggle.Pressed += Toggle; AddChild(toggle);
        _panel = new PanelContainer { Position = new(22, 201), Size = new(455, 419), Visible = false };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(.035f, .052f, .075f, .98f),
            BorderColor = new Color("728188"),
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 9,
            ContentMarginBottom = 9
        });
        AddChild(_panel);
        var column = new VBoxContainer(); _panel.AddChild(column);
        var tabs = new HBoxContainer(); column.AddChild(tabs);
        foreach (string tab in new[] { "Character", "Gear", "Craft", "Town", "Profile" })
        {
            var button = new Button { Text = tab, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            if (tab == "Character") _firstTab = button;
            button.AddThemeFontSizeOverride("font_size", 12); button.Pressed += () => { _tab = tab; Rebuild(true); }; tabs.AddChild(button);
        }
        _notice = Label("", 12); column.AddChild(_notice);
        var scroll = new ScrollContainer { CustomMinimumSize = new(429, 319), SizeFlagsVertical = SizeFlags.ExpandFill }; column.AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(_rows);
        var close = new Button { Text = "Close character" }; close.Pressed += Toggle; column.AddChild(close);
        _confirmation = new ConfirmationDialog { Title = "Confirm permanent crafting", OkButtonText = "Commit craft", CancelButtonText = "Keep current item" };
        _confirmation.Confirmed += () => { if (_pendingCraft is not null) CraftRequested?.Invoke(_pendingCraft with { ConfirmPermanent = true }); _pendingCraft = null; };
        _confirmation.Canceled += () => _pendingCraft = null; AddChild(_confirmation);
    }

    public void Toggle() { _panel.Visible = !_panel.Visible; if (_panel.Visible) { Rebuild(true); _firstTab.GrabFocus(); } }
    public void Notice(string message) => _notice.Text = message;
    public void SetView(ProgressionView view, ProgressionSnapshot state, ProgressionDefinition content, CombatView combat,
        IReadOnlyList<InteractionDisplay> interactions, bool inTown, long revision, IReadOnlyList<string>? unlockedMutations)
    {
        _view = view; _state = state; _content = content; _combat = combat; _interactions = interactions; _inTown = inTown; _revision = revision;
        _unlockedMutations = unlockedMutations;
        _rangeMask = 0;
        for (int i = 0; i < interactions.Count; i++) if (interactions[i].Distance <= interactions[i].Range) _rangeMask |= 1 << i;
        _summary.Text = Catalog.Format("production.level", new Dictionary<string, string> { ["level"] = view.Level.ToString(), ["discipline"] = view.Discipline }) +
            $"\nXP {view.Experience:N0}/{view.NextLevelExperience:N0}  ·  " + Catalog.Format("production.materials", count: view.Materials);
        Rebuild(false);
    }
    private void Rebuild(bool force)
    {
        if (_view is null || !_panel.Visible || (!force && _renderedRevision == _revision && _renderedRangeMask == _rangeMask)) return;
        _renderedRevision = _revision; _renderedRangeMask = _rangeMask;
        foreach (var child in _rows.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
        switch (_tab)
        {
            case "Character": Character(); break;
            case "Gear": Gear(); break;
            case "Craft": Craft(); break;
            case "Town": Town(); break;
            case "Profile": Profile(); break;
        }
    }
    private void Character()
    {
        _rows.AddChild(Label(Catalog.Format("production.discipline") + $": {_view.Discipline} · {_view.Resource}", 17));
        _rows.AddChild(Label("Choose a discipline to retrain. Retraining preserves permanent progress and removes incompatible equipment.", 12));
        foreach (var discipline in _content.Disciplines)
        {
            var button = Button(Catalog.Format("production.retrain", new Dictionary<string, string> { ["discipline"] = discipline.Id }) +
                $" · {discipline.Resource} · " + Catalog.Format("production.materials", count: _content.RespecCost), () => RetrainRequested?.Invoke(discipline.Id));
            button.Disabled = !At("service.mara") || _view.Level < 5 || _view.Discipline == discipline.Id || _view.Materials < _content.RespecCost;
        }
        if (_view.Level < 5) _rows.AddChild(Label("Retraining unlocks at level 5. Defeat enemies and complete expeditions to earn experience.", 12));
        _rows.AddChild(new HSeparator());
        _rows.AddChild(Label($"PASSIVES · {_view.AvailablePassivePoints} points available", 14));
        foreach (string passive in new[] { "Offense", "Defense", "Resource" })
        {
            int rank = _state.Character.Passives.GetValueOrDefault(passive);
            Button($"{passive} · rank {rank} · allocate one point", () => PassiveRequested?.Invoke(passive)).Disabled = !At("service.mara") || _view.AvailablePassivePoints == 0;
        }
        Button(Catalog.Format("production.respec", count: _content.RespecCost), () => RespecRequested?.Invoke()).Disabled = !At("service.mara") || _state.Character.Passives.Count == 0 || _view.Materials < _content.RespecCost;
        _rows.AddChild(new HSeparator()); _rows.AddChild(Label(Catalog.Format("production.mastery"), 14));
        foreach (var skill in _combat.Skills)
        {
            _rows.AddChild(Label(skill.Name, 14));
            int mastery = _state.Character.Mastery.GetValueOrDefault(skill.Id);
            _rows.AddChild(Label($"{skill.Shape} · mastery {mastery}/100 · {(skill.ResourceMode == "Heat" ? "builds" : "cost")} {skill.Cost} {_view.Resource}" + (skill.Available ? "" : " · LOCKED"), 12));
            foreach (var mutation in _combat.Mutations.Where(m => m.SkillId == skill.Id))
            {
                var button = Button((skill.Mutation == mutation.Id ? "◆ " : "") + mutation.Name, () => MutationRequested?.Invoke(skill.Id, mutation.Id));
                button.Disabled = !At("service.mara") || (_unlockedMutations is not null && !_unlockedMutations.Contains(mutation.Id)); button.TooltipText = mutation.Description;
                _rows.AddChild(Label(mutation.Description, 12));
            }
            if (skill.Mutation.Length > 0) Button("Restore unmutated " + skill.Name, () => MutationRequested?.Invoke(skill.Id, "")).Disabled = !At("service.mara");
        }
        _rows.AddChild(Label(_view.UltimateSkills.Length == 0 ? "Discipline ultimates unlock at level 10." : "Unlocked ultimates: " + string.Join(", ", _view.UltimateSkills.Select(Readable)), 12));
    }

    private void Gear()
    {
        _rows.AddChild(Label(Catalog.Format("production.equipment") + " · 12 PERMANENT SLOTS", 16));
        _rows.AddChild(Label("Approach Torren to equip a compatible item. Rolls and identity persist through travel, death and retraining.", 12));
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            _rows.AddChild(Label(slot.ToString(), 13));
            var choice = new OptionButton { Disabled = !At("service.torren") }; choice.AddItem("Empty"); choice.SetItemMetadata(0, 0L); int selection = 0;
            foreach (var item in _state.Character.Items)
            {
                var definition = _content.Items.Single(d => d.Id == item.DefinitionId);
                if (!definition.Slots.Contains(slot) || definition.Disciplines.Length > 0 && !definition.Disciplines.Contains(_view.Discipline)) continue;
                int index = choice.ItemCount;
                choice.AddItem($"#{item.Id} {Readable(item.DefinitionId)} · {item.Rarity}"); choice.SetItemMetadata(index, item.Id);
                choice.SetItemTooltip(index, DescribeItem(item, slot));
                if (_state.Character.Equipment.GetValueOrDefault(slot) == item.Id) selection = index;
            }
            choice.Select(selection);
            choice.ItemSelected += index =>
            { long id = choice.GetItemMetadata((int)index).AsInt64(); if (id == 0) UnequipRequested?.Invoke(slot); else EquipRequested?.Invoke(id, slot); };
            _rows.AddChild(choice);
            if (_state.Character.Equipment.TryGetValue(slot, out long equipped))
                _rows.AddChild(Label(DescribeItem(_state.Character.Items.Single(i => i.Id == equipped), slot), 12));
        }
        _rows.AddChild(new HSeparator()); _rows.AddChild(Label("PERMANENT BONUSES · EQUIPMENT + PASSIVES", 14));
        foreach (var pair in _view.Stats) _rows.AddChild(Label($"{Readable(pair.Key)}  {pair.Value}", 12));
        if (_view.Stats.Count == 0) _rows.AddChild(Label("Equip rolled items and allocate passives to add permanent stats.", 12));
    }
    private string DescribeItem(PermanentItem item, EquipmentSlot slot)
    {
        var equipped = _state.Character.Equipment.TryGetValue(slot, out long id) ? _state.Character.Items.FirstOrDefault(i => i.Id == id) : null;
        int damage = item.BaseDamage + item.Affixes.GetValueOrDefault("affix.damage");
        int oldDamage = (equipped?.BaseDamage ?? 0) + (equipped?.Affixes.GetValueOrDefault("affix.damage") ?? 0);
        int armor = item.BaseArmor + item.Affixes.GetValueOrDefault("affix.armor");
        int oldArmor = (equipped?.BaseArmor ?? 0) + (equipped?.Affixes.GetValueOrDefault("affix.armor") ?? 0);
        var entries = item.Affixes.Select(p => $"{Readable(p.Key)} {p.Value} ({p.Value - (equipped?.Affixes.GetValueOrDefault(p.Key) ?? 0):+0;-0;0})");
        return $"Damage {damage} ({damage - oldDamage:+0;-0;0}) · Armor {armor} ({armor - oldArmor:+0;-0;0})\n" +
            string.Join(" · ", entries) + (item.Engraving.Length == 0 ? "" : "\nEngraving: " + Readable(item.Engraving)) +
            (item.Rarity == ItemRarity.Godwrought ? $"\nBurning kills {item.BurningKills}/1000 · {item.Evolution}" : "");
    }

    private void Craft()
    {
        _rows.AddChild(Label(Catalog.Format("production.crafting"), 16));
        var services = new OptionButton();
        foreach (var service in Enum.GetValues<CraftingService>()) services.AddItem($"{service} · {_content.CraftingCosts[service]} materials{(_view.Services.Contains(service) ? "" : " · LOCKED")}", (int)service);
        services.Select((int)_service); services.ItemSelected += index => { _service = (CraftingService)services.GetItemId((int)index); Rebuild(true); }; _rows.AddChild(services);
        if (!_view.Services.Contains(_service))
        {
            var objective = _content.Objectives.First(o => o.Service == _service.ToString());
            _rows.AddChild(Label("Speak with Greyhaven's specialists and complete their prerequisites to reopen this workshop.\n" + _content.Strings[objective.JournalKey], 12));
            return;
        }
        if (!_inTown) { _rows.AddChild(Label("Return to Greyhaven to craft.", 12)); return; }
        PermanentItem? selected = null;
        if (_service != CraftingService.Purification)
        {
            _rows.AddChild(Label("Choose an item instance", 13));
            var items = new OptionButton(); int current = 0;
            if (_state.Character.Items.Length == 0) { _rows.AddChild(Label("No items are available.", 12)); return; }
            foreach (var item in _state.Character.Items)
            {
                int index = items.ItemCount; items.AddItem($"#{item.Id} {Readable(item.DefinitionId)} · {item.Rarity}"); items.SetItemMetadata(index, item.Id);
                if (item.Id == _craftItem) current = index;
            }
            items.Select(current); _craftItem = items.GetItemMetadata(current).AsInt64(); selected = _state.Character.Items.Single(i => i.Id == _craftItem);
            items.ItemSelected += index => { _craftItem = items.GetItemMetadata((int)index).AsInt64(); Rebuild(true); }; _rows.AddChild(items);
            _rows.AddChild(Label(string.Join(" · ", selected.Affixes.Select(p => $"{Readable(p.Key)} {p.Value}")), 12));
        }
        OptionButton? existing = null, replacement = null, property = null, fragment = null, lineage = null;
        if (_service is CraftingService.Tempering or CraftingService.Rebinding)
        {
            IEnumerable<string> affixes = selected!.Affixes.Keys;
            if (_service == CraftingService.Tempering && selected.Rarity == ItemRarity.Godwrought && !selected.Affixes.ContainsKey("affix.damage"))
                affixes = affixes.Append("affix.damage");
            existing = Choice("Affix to improve", affixes);
            if (_service == CraftingService.Rebinding)
            {
                var definition = _content.Items.Single(i => i.Id == selected.DefinitionId);
                replacement = Choice("Replacement affix", _content.Affixes.Where(a => definition.Slots.All(a.Slots.Contains) && !selected.Affixes.ContainsKey(a.Id)).Select(a => a.Id));
            }
        }
        if (_service == CraftingService.Engraving) property = Choice("Learned property", _state.Character.PropertyLibrary.Where(id => !id.StartsWith("evolution.", StringComparison.Ordinal)));
        if (_service == CraftingService.Purification) fragment = Choice("Unpurified fragment", _state.Character.OwnedFragments.Where(id => !_state.Character.PurifiedFragments.Contains(id)));
        if (_service == CraftingService.DivineGrafting)
        {
            lineage = Choice("Permanent evolution", new[] { "Serath", "Orrun" });
            lineage.Select(_craftLineage == "Orrun" ? 1 : 0);
            lineage.ItemSelected += index => { _craftLineage = index == 1 ? "Orrun" : "Serath"; Rebuild(true); };
            _rows.AddChild(Label($"Burning kills: {selected!.BurningKills}/1000 · current evolution: {(selected.Evolution.Length == 0 ? "none" : selected.Evolution)}", 12));
            _rows.AddChild(Label(_craftLineage == "Serath" ? "Serath raises burning victims as temporary flaming revenants. It also retains its direct-hit healing benefit." : "Orrun replaces the awakened flame wave with an igniting molten seismic attack. It also retains its defense and on-hit barrier benefits.", 12));
        }
        if (_service == CraftingService.Tempering && selected!.Rarity == ItemRarity.Godwrought && _endgameCrafting)
        {
            _rows.AddChild(Label("Payment · +2 damage, up to the existing +10 cap", 13));
            var payment = new OptionButton(); payment.AddItem($"{_content.CraftingCosts[_service]} common materials"); payment.SetItemMetadata(0, "");
            foreach (var pair in _catalysts.Where(p => p.Value > 0))
            { int index = payment.ItemCount; payment.AddItem($"1 {Readable(pair.Key)} · owned {pair.Value}"); payment.SetItemMetadata(index, pair.Key); if (_craftCatalyst == pair.Key) payment.Select(index); }
            _craftCatalyst = payment.GetItemMetadata(payment.Selected).AsString();
            payment.ItemSelected += index => { _craftCatalyst = payment.GetItemMetadata((int)index).AsString(); Rebuild(true); }; _rows.AddChild(payment);
            _rows.AddChild(Label("Selecting a named catalyst replaces this Tempering craft's common-material fee. No automatic substitution occurs.", 12));
        }
        else _craftCatalyst = "";
        if (_service == CraftingService.Extraction) _rows.AddChild(Label("Extraction permanently destroys a Legendary item and learns its exceptional property.", 12));
        if (_service == CraftingService.DivineGrafting) _rows.AddChild(Label("An awakened Ashcleaver and the matching lineage fragment are required. This evolution is permanent.", 12));
        string catalystId = _service == CraftingService.DivineGrafting && _endgameCrafting
            ? _craftLineage == "Serath" ? "material.serath_memory" : "material.orrun_oath" : _craftCatalyst;
        int materialCost = _service == CraftingService.Tempering && catalystId.Length > 0 ? 0 : _content.CraftingCosts[_service];
        string paymentText = $"{materialCost} common materials" + (catalystId.Length == 0 ? "" : $" + 1 {Readable(catalystId)}");
        if (catalystId.Length > 0) _rows.AddChild(Label($"Catalyst: {Readable(catalystId)} · owned {_catalysts.GetValueOrDefault(catalystId)}", 12));
        var craft = Button($"{_service} · spend {paymentText}", () =>
        {
            string Value(OptionButton? control) => control is null || control.ItemCount == 0 ? "" : control.GetItemMetadata(control.Selected).AsString();
            var request = new CraftingRequest(Guid.NewGuid().ToString("N"), _service, selected?.Id ?? 0,
                Value(existing), Value(replacement), Value(property), Value(lineage), Value(fragment), CatalystId: catalystId.Length == 0 ? null : catalystId);
            if (_service is CraftingService.Extraction or CraftingService.DivineGrafting)
            {
                _pendingCraft = request;
                _confirmation.DialogText = _service == CraftingService.Extraction
                    ? $"Permanently destroy #{selected!.Id} {Readable(selected.DefinitionId)} and spend {_content.CraftingCosts[_service]} materials to learn its property?"
                    : Catalog.Format("production.confirm_graft", new Dictionary<string, string> { ["item"] = "#" + selected!.Id, ["branch"] = request.Lineage }) + "\n\nCost: " + paymentText + ". The other evolution branch will be unavailable for this item.";
                _confirmation.PopupCentered(new(510, 200));
            }
            else CraftRequested?.Invoke(request);
        });
        string specialist = _service switch { CraftingService.Tempering => "service.torren", CraftingService.Rebinding => "npc.oris", CraftingService.Engraving => "hub.workshops", CraftingService.Extraction => "npc.kesh", CraftingService.Purification => "npc.cael", _ => "service.mara" };
        craft.Disabled = !At(specialist) || _view.Materials < materialCost || catalystId.Length > 0 && _catalysts.GetValueOrDefault(catalystId) == 0 ||
            _service == CraftingService.DivineGrafting && (selected!.DefinitionId != "item.ashcleaver" || selected.BurningKills < 1000 || selected.Evolution.Length > 0 ||
                !_state.Character.OwnedFragments.Contains(_craftLineage == "Serath" ? "fragment.heart_serath" : "fragment.orrun_bone")) || new[] { existing, replacement, property, fragment, lineage }.Any(c => c is not null && c.ItemCount == 0);
        if (!At(specialist)) _rows.AddChild(Label("Approach this workshop's specialist to commit the craft.", 12));
    }
    private void Town()
    {
        _rows.AddChild(Label($"GREYHAVEN · RESTORATION STAGE {_view.HubStage}/3", 16));
        foreach (var interaction in _interactions)
        {
            Button(interaction.Name, () => ServiceRequested?.Invoke(interaction.Id)).Disabled = interaction.Distance > interaction.Range;
        }
        foreach (var objective in _content.Objectives)
        {
            bool complete = _state.Character.CompletedObjectives.Contains(objective.Id);
            _rows.AddChild(Label((complete ? "✓ " : "○ ") + _content.Strings[objective.JournalKey], 13));
            if (!complete && objective.Requires.Length > 0) _rows.AddChild(Label("Requires: " + string.Join(", ", objective.Requires.Select(Readable)), 12));
        }
    }
    private void Profile()
    {
        _rows.AddChild(Label($"CHARACTER · {_state.Character.CharacterId}", 16));
        _rows.AddChild(Label("Character equipment, levels and crafting are saved separately from local profile discoveries.", 12));
        _rows.AddChild(Label(Catalog.Format("production.profile") + " · " + _state.Profile.ProfileId, 15));
        foreach (string unlock in _view.ProfileUnlocks) _rows.AddChild(Label("✓ " + Readable(unlock), 13));
        if (_view.ProfileUnlocks.Length == 0) _rows.AddChild(Label("Complete expeditions to earn persistent profile unlocks.", 12));
        foreach (string discovery in _state.Profile.Discoveries) _rows.AddChild(Label(Readable(discovery), 12));
        _rows.AddChild(new HSeparator()); _rows.AddChild(Label("PROGRESSION JOURNAL", 14));
        foreach (string entry in _view.Journal) _rows.AddChild(Label(entry, 12));
    }
    private OptionButton Choice(string label, IEnumerable<string> ids)
    {
        _rows.AddChild(Label(label, 13)); var control = new OptionButton();
        foreach (string id in ids) { int index = control.ItemCount; control.AddItem(Readable(id)); control.SetItemMetadata(index, id); }
        _rows.AddChild(control); return control;
    }
    private bool At(string action) => _inTown && _interactions.Any(i => i.Id == action && i.Distance <= i.Range);
    private Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        button.AddThemeFontSizeOverride("font_size", 12); button.Pressed += action; _rows.AddChild(button); return button;
    }
    private static Label Label(string text, int fontSize)
    { var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart }; label.AddThemeFontSizeOverride("font_size", fontSize); return label; }
    private static string Readable(string id) => (id.Contains('.') ? string.Join(' ', id.Split('.').Skip(1)) : id).Replace('_', ' ');
}
