using Ashenwake.Core.Combat;
using Ashenwake.Core.Authoring;
using Ashenwake.Core.Adventure;
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
    private BoxContainer _body = null!;
    private ScrollContainer _scroll = null!;
    private CharacterPreview _preview = null!;
    private GearLoadout _gearLoadout = null!;
    private CharacterAppearance? _appearance;
    private EquipmentSlot _gearSlot = EquipmentSlot.MainHand;
    private long _gearItemId;
    private bool _gearInspecting;
    private VBoxContainer _rows = null!;
    private Label _summary = null!, _notice = null!;
    private Button _firstTab = null!;
    private readonly Dictionary<string, Button> _tabs = new(StringComparer.Ordinal);
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
            _tabs.Add(tab, button);
            if (tab == "Character") _firstTab = button;
            button.AddThemeFontSizeOverride("font_size", 12); button.Pressed += () => { _tab = tab; Rebuild(true); }; tabs.AddChild(button);
        }
        _notice = Label("", 12); column.AddChild(_notice);
        _body = new BoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; _body.AddThemeConstantOverride("separation", 16); column.AddChild(_body);
        _preview = new CharacterPreview { Visible = false }; _body.AddChild(_preview);
        var gearColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        gearColumn.AddThemeConstantOverride("separation", 8); _body.AddChild(gearColumn);
        _gearLoadout = new GearLoadout { Visible = false }; gearColumn.AddChild(_gearLoadout);
        _gearLoadout.CanEquip = (id, slot) => CanChangeGear && _state.Character.Items.FirstOrDefault(i => i.Id == id) is { } item &&
            Compatible(item, slot) && EquipmentConflict(item, slot).Length == 0;
        _gearLoadout.InspectRequested += (slot, id) => { _gearSlot = slot; _gearItemId = id; _gearInspecting = true; Rebuild(true); };
        _gearLoadout.EquipRequested += (id, slot) => { _gearSlot = slot; _gearInspecting = false; EquipRequested?.Invoke(id, slot); };
        _gearLoadout.UnequipRequested += slot => { _gearSlot = slot; _gearInspecting = false; UnequipRequested?.Invoke(slot); };
        _scroll = new ScrollContainer
        {
            CustomMinimumSize = new(429, 319),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        }; gearColumn.AddChild(_scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _scroll.AddChild(_rows);
        var close = new Button { Text = "Close character" }; close.Pressed += Toggle; column.AddChild(close);
        _confirmation = new ConfirmationDialog { Title = "Confirm permanent crafting", OkButtonText = "Commit craft", CancelButtonText = "Keep current item" };
        _confirmation.Confirmed += () => { if (_pendingCraft is not null) CraftRequested?.Invoke(_pendingCraft with { ConfirmPermanent = true }); _pendingCraft = null; };
        _confirmation.Canceled += () => _pendingCraft = null; AddChild(_confirmation);
        GetViewport().SizeChanged += LayoutPanel;
        VisibilityChanged += () => { if (!IsVisibleInTree()) { _gearInspecting = false; _gearLoadout.CancelDrag(); } };
    }

    public override void _ExitTree() { GetViewport().SizeChanged -= LayoutPanel; }
    public void Toggle() { _panel.Visible = !_panel.Visible; if (_panel.Visible) { Rebuild(true); _firstTab.GrabFocus(); } else _gearInspecting = false; }
    public void ToggleInventory()
    {
        _gearLoadout.CancelDrag();
        if (_panel.Visible && _tab == "Gear") { Toggle(); return; }
        _tab = "Gear"; _panel.Visible = true; Rebuild(true); _tabs["Gear"].GrabFocus();
    }
    public bool PresentInteraction(string message)
    {
        string specialist;
        switch (message)
        {
            case "ServiceOpened:service.torren":
                _tab = "Gear"; specialist = "Torren · compare and equip your items."; break;
            case "ServiceOpened:npc.torren":
                _tab = "Craft"; _service = CraftingService.Tempering; specialist = "Torren · improve equipment through tempering."; break;
            case "ServiceOpened:npc.cael":
                _tab = "Craft"; _service = CraftingService.Purification; specialist = "Sister Cael · purify a divine fragment."; break;
            case "ServiceOpened:npc.oris":
                _tab = "Craft"; _service = CraftingService.Rebinding; specialist = "Oris · rebind an unwanted affix."; break;
            case "ServiceOpened:npc.kesh":
                _tab = "Craft"; _service = CraftingService.Extraction; specialist = "Kesh · extract a Legendary item's property."; break;
            case "ServiceOpened:hub.workshops":
                _tab = "Craft"; _service = CraftingService.Engraving; specialist = "Greyhaven workshops · engrave a learned property."; break;
            default: return false;
        }
        Visible = true; _panel.Visible = true; Notice(specialist); Rebuild(true); _tabs[_tab].GrabFocus(); return true;
    }
    public void Notice(string message) => _notice.Text = message;
    public void SetAppearance(CharacterAppearance appearance)
    {
        if (_appearance?.Key == appearance.Key) return;
        _appearance = appearance;
        if (_preview is not null) UpdatePreview();
    }
    public void SetView(ProgressionView view, ProgressionSnapshot state, ProgressionDefinition content, CombatView combat,
        IReadOnlyList<InteractionDisplay> interactions, bool inTown, long revision, IReadOnlyList<string>? unlockedMutations)
    {
        _view = view; _state = state; _content = content; _combat = combat; _interactions = interactions; _inTown = inTown; _revision = revision;
        if (_gearInspecting && _gearItemId != 0 && !state.Character.Items.Any(i => i.Id == _gearItemId && Compatible(i, _gearSlot))) _gearInspecting = false;
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
        LayoutPanel();
        foreach (var child in _rows.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
        switch (_tab)
        {
            case "Character": Character(); break;
            case "Gear": Gear(); break;
            case "Craft": Craft(); break;
            case "Town": Town(); break;
            case "Profile": Profile(); break;
        }
        UpdatePreview();
    }

    private void LayoutPanel()
    {
        if (_preview is null) return;
        bool gear = _tab == "Gear";
        bool showPreview = _tab == "Character" || gear && GetViewportRect().Size.X >= 1020;
        bool compact = GetViewportRect().Size.X < 820;
        _preview.Visible = showPreview;
        _gearLoadout.Visible = gear;
        _body.Vertical = compact && showPreview;
        var viewport = GetViewportRect().Size;
        float height = Math.Min(gear ? 690 : showPreview && compact ? 660 : showPreview ? 560 : 419, viewport.Y - 44);
        float bodyHeight = Math.Max(100, height - 140);
        float scrollHeight = gear ? Math.Clamp(bodyHeight - 310, 85, 150) : compact && showPreview ? Math.Min(190, bodyHeight * .4f) : Math.Min(319, bodyHeight);
        _scroll.CustomMinimumSize = new(gear ? 540 : compact ? 300 : 429, scrollHeight);
        _preview.SetCompact(compact, compact && showPreview ? bodyHeight - scrollHeight - 16 : bodyHeight);
        // Lift the panel at shorter viewport heights instead of letting the preview's minimum size push Close below the screen.
        _panel.Position = new(22, Math.Max(22, Math.Min(compact && showPreview ? 98 : 201, viewport.Y - height - 22)));
        _panel.Size = new(Math.Min(gear ? 980 : showPreview && !compact ? 755 : 455, viewport.X - 44),
            height);
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
        _gearLoadout.SetView(_state, _content, _revision, CanChangeGear);
        _rows.AddChild(Label(CanChangeGear ? "Drag inventory gear onto a compatible slot. Drag equipped gear back to inventory to unequip. Click to compare below." :
            "Inspect your gear anywhere. Visit Torren in Greyhaven to equip or unequip, including by dragging.", 12));
        var slots = new OptionButton { Name = "GearSlot" };
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            long equippedId = _state.Character.Equipment.GetValueOrDefault(slot);
            var equippedItem = _state.Character.Items.FirstOrDefault(i => i.Id == equippedId);
            slots.AddItem($"{slot} · {(equippedItem is null ? "Empty" : Readable(equippedItem.DefinitionId))}", (int)slot);
        }
        slots.Select((int)_gearSlot);
        slots.ItemSelected += index => { _gearSlot = (EquipmentSlot)slots.GetItemId((int)index); _gearInspecting = false; Rebuild(true); };
        _rows.AddChild(slots);
        long currentId = _state.Character.Equipment.GetValueOrDefault(_gearSlot);
        if (!_gearInspecting) _gearItemId = currentId;
        var current = _state.Character.Items.FirstOrDefault(i => i.Id == currentId);
        var candidate = _state.Character.Items.FirstOrDefault(i => i.Id == _gearItemId);
        var choice = new OptionButton { Name = "GearItem" };
        choice.AddItem("Empty · preview unequipped slot"); choice.SetItemMetadata(0, 0L);
        foreach (var item in _state.Character.Items.Where(i => Compatible(i, _gearSlot)))
        {
            int index = choice.ItemCount;
            choice.AddItem($"#{item.Id} {Readable(item.DefinitionId)} · {item.Rarity}"); choice.SetItemMetadata(index, item.Id);
            choice.SetItemTooltip(index, DescribeItem(item, _gearSlot));
            if (_gearItemId == item.Id) choice.Select(index);
        }
        choice.ItemSelected += index => { _gearItemId = choice.GetItemMetadata((int)index).AsInt64(); _gearInspecting = true; Rebuild(true); };
        _rows.AddChild(choice);
        _rows.AddChild(new HSeparator());
        _rows.AddChild(Label("SLOT COMPARISON · EQUIPPED → SELECTED", 13));
        _rows.AddChild(Label($"{ItemTitle(current)}\n→ {ItemTitle(candidate)}", 12));
        AddComparison("Damage", ItemDamage(current), ItemDamage(candidate));
        AddComparison("Armor", ItemArmor(current), ItemArmor(candidate));
        AddComparison("Critical chance (basis points)", ItemCritical(current), ItemCritical(candidate));
        var affixIds = (current?.Affixes.Keys.AsEnumerable() ?? []).Union(candidate?.Affixes.Keys.AsEnumerable() ?? [])
            .Where(id => id is not ("affix.damage" or "affix.armor" or "affix.critical")).Order(StringComparer.Ordinal);
        foreach (string id in affixIds) AddComparison(Readable(id), current?.Affixes.GetValueOrDefault(id) ?? 0, candidate?.Affixes.GetValueOrDefault(id) ?? 0);
        if (current?.Engraving.Length > 0 || candidate?.Engraving.Length > 0)
            _rows.AddChild(Label($"Engraving: {OptionalName(current?.Engraving)} → {OptionalName(candidate?.Engraving)}", 12));
        string oldProperty = current is null ? "" : _content.Items.Single(i => i.Id == current.DefinitionId).Property;
        string newProperty = candidate is null ? "" : _content.Items.Single(i => i.Id == candidate.DefinitionId).Property;
        if (oldProperty.Length > 0 || newProperty.Length > 0)
            _rows.AddChild(Label($"Property: {OptionalName(oldProperty)} → {OptionalName(newProperty)}", 12));
        if (candidate is not null)
        {
            var elsewhere = _state.Character.Equipment.Where(p => p.Value == candidate.Id && p.Key != _gearSlot).Select(p => p.Key.ToString()).ToArray();
            if (elsewhere.Length > 0) _rows.AddChild(Label($"Equipping this item moves it from {string.Join(", ", elsewhere)}. Comparisons above show this slot only.", 12));
            if (candidate.Rarity == ItemRarity.Godwrought)
                _rows.AddChild(Label($"Burning kills {candidate.BurningKills}/{GodwroughtProgress.AwakeningKills} · {(candidate.Evolution.Length > 0 ? candidate.Evolution : candidate.Awakened ? "Awakened" : "Dormant")}", 12));
        }
        string conflict = EquipmentConflict(candidate);
        var equip = Button(candidate is null ? $"Unequip {_gearSlot}" : $"Equip selected {_gearSlot}", () =>
        {
            if (_gearItemId == 0) UnequipRequested?.Invoke(_gearSlot);
            else EquipRequested?.Invoke(_gearItemId, _gearSlot);
        });
        equip.Name = candidate is null ? "UnequipItem" : "EquipItem";
        equip.Disabled = !CanChangeGear || _gearItemId == currentId || conflict.Length > 0;
        if (conflict.Length > 0) _rows.AddChild(Label(conflict, 12));
        if (!At("service.torren")) _rows.AddChild(Label("Inspect anywhere. Stand near Torren in Greyhaven to equip or unequip.", 12));
        if (_gearItemId != currentId)
        {
            var reset = Button("Show equipped item", () => { _gearInspecting = false; Rebuild(true); });
            reset.Name = "ResetGearPreview";
        }
        if (_gearSlot is not (EquipmentSlot.MainHand or EquipmentSlot.OffHand or EquipmentSlot.Head or EquipmentSlot.Chest))
            _rows.AddChild(Label("This slot changes stats. The model displays your weapon, off hand, helmet and chest armor.", 12));
        _rows.AddChild(new HSeparator()); _rows.AddChild(Label("PERMANENT BONUSES · EQUIPMENT + PASSIVES", 14));
        foreach (var pair in _view.Stats) _rows.AddChild(Label($"{Readable(pair.Key)}  {pair.Value}", 12));
        if (_view.Stats.Count == 0) _rows.AddChild(Label("Equip rolled items and allocate passives to add permanent stats.", 12));
    }

    private bool Compatible(PermanentItem item, EquipmentSlot slot)
    {
        var definition = _content.Items.Single(d => d.Id == item.DefinitionId);
        return definition.Slots.Contains(slot) && (definition.Disciplines.Length == 0 || definition.Disciplines.Contains(_view.Discipline));
    }

    private bool CanChangeGear => At("service.torren") && _combat.Actors.Any(a => a.Id == 1 && a.Health > 0);

    private string EquipmentConflict(PermanentItem? candidate, EquipmentSlot? target = null)
    {
        if (candidate is null) return "";
        var definition = _content.Items.Single(i => i.Id == candidate.DefinitionId);
        if (definition.Hands == 2 && _state.Character.Equipment.ContainsKey(EquipmentSlot.OffHand))
            return "This weapon needs both hands. Unequip the off hand first; the preview shows the required empty off hand.";
        if ((target ?? _gearSlot) == EquipmentSlot.OffHand && _state.Character.Equipment.TryGetValue(EquipmentSlot.MainHand, out long mainId))
        {
            var main = _state.Character.Items.Single(i => i.Id == mainId);
            if (_content.Items.Single(i => i.Id == main.DefinitionId).Hands == 2)
                return "Your equipped weapon uses both hands. Unequip it before equipping an off-hand item.";
        }
        return "";
    }

    private void AddComparison(string label, int current, int candidate)
    {
        int delta = candidate - current;
        var line = Label($"{label}  {current} → {candidate}   ({delta:+0;-0;0})", 12);
        line.Modulate = delta > 0 ? new("9eddb4") : delta < 0 ? new("eeb196") : new("c3ced5");
        _rows.AddChild(line);
    }

    private void UpdatePreview()
    {
        if (_appearance is null || _preview is null) return;
        CharacterAppearance shown = _appearance;
        bool inspecting = _tab == "Gear" && _gearInspecting && _state is not null &&
            _gearItemId != _state.Character.Equipment.GetValueOrDefault(_gearSlot);
        if (inspecting && _state is not null)
        {
            var equipment = new SortedDictionary<EquipmentSlot, long>(_state.Character.Equipment);
            foreach (var occupied in equipment.Where(p => p.Value == _gearItemId).Select(p => p.Key).ToArray()) equipment.Remove(occupied);
            if (_gearItemId == 0) equipment.Remove(_gearSlot); else equipment[_gearSlot] = _gearItemId;
            var candidate = _state.Character.Items.FirstOrDefault(i => i.Id == _gearItemId);
            if (candidate is not null && _content.Items.Single(i => i.Id == candidate.DefinitionId).Hands == 2) equipment.Remove(EquipmentSlot.OffHand);
            var previewState = _state with { Character = _state.Character with { Equipment = equipment } };
            shown = CharacterAppearance.FromProgression(previewState) with
            {
                ManifestationMask = _appearance.ManifestationMask,
                AnatomyMask = _appearance.AnatomyMask
            };
        }
        _preview.SetAppearance(shown);
        _preview.SetCaption(inspecting ? $"Previewing {_gearSlot} · not equipped" : "Equipped appearance · drag to rotate");
    }

    private static string ItemTitle(PermanentItem? item) => item is null ? "Empty" : $"#{item.Id} {Readable(item.DefinitionId)} · {item.Rarity}";
    private static string OptionalName(string? id) => string.IsNullOrEmpty(id) ? "none" : Readable(id);
    private static int ItemDamage(PermanentItem? item) => (item?.BaseDamage ?? 0) + (item?.Affixes.GetValueOrDefault("affix.damage") ?? 0);
    private static int ItemArmor(PermanentItem? item) => (item?.BaseArmor ?? 0) + (item?.Affixes.GetValueOrDefault("affix.armor") ?? 0);
    private static int ItemCritical(PermanentItem? item) => (item?.BaseCriticalBasisPoints ?? 0) + (item?.Affixes.GetValueOrDefault("affix.critical") ?? 0);
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
            (item.Rarity == ItemRarity.Godwrought ? $"\nBurning kills {item.BurningKills}/{GodwroughtProgress.AwakeningKills} · {item.Evolution}" : "");
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
            _rows.AddChild(Label($"Burning kills: {selected!.BurningKills}/{GodwroughtProgress.AwakeningKills} · current evolution: {(selected.Evolution.Length == 0 ? "none" : selected.Evolution)}", 12));
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
            _service == CraftingService.DivineGrafting && (selected!.DefinitionId != "item.ashcleaver" || !selected.Awakened || selected.Evolution.Length > 0 ||
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
