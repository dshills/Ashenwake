using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Item inspection and isolated Core projections; only an explicit craft requests a live transaction.</summary>
public partial class CraftingWorkbench : VBoxContainer
{
    public event Action<CraftingRequest>? CraftRequested;
    public CraftingPreview? Preview { get; private set; }
    public string PreviewText { get; private set; } = "";
    public long SelectedItemId => _inventory.SelectedItemId;
    private CraftingInventory _inventory = null!;
    private GearDragCard _target = null!;
    private Label _balance = null!, _description = null!, _status = null!, _result = null!;
    private VBoxContainer _options = null!, _comparison = null!;
    private Button _commit = null!;
    private ConfirmationDialog _confirmation = null!;
    private readonly Dictionary<CraftingService, Button> _services = [];
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private ProgressionSnapshot? _state;
    private ProgressionDefinition _definition = null!;
    private ProgressionContent _content = null!;
    private IReadOnlyList<InteractionDisplay> _interactions = [];
    private CraftingService _service;
    private string _stateKey = "", _pendingKey = "";
    private bool _inTown, _alive, _submitting, _pauseHeld;
    private CraftingRequest? _pending;
    private CraftingPreview? _submitted;
    private string _submittedName = "";
    private Sandbox? _sandbox;
    private object? _seenSession;

    public override void _Ready()
    {
        Name = "CraftingWorkbench";
        CustomMinimumSize = new(680, 480);
        SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 8);
        _balance = Caption("GREYHAVEN WORKBENCH", 15); AddChild(_balance);
        var services = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        services.AddThemeConstantOverride("h_separation", 6); services.AddThemeConstantOverride("v_separation", 6); AddChild(services);
        foreach (var service in Enum.GetValues<CraftingService>())
        {
            var button = new Button { Name = "CraftService" + service, CustomMinimumSize = new(0, 49), SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            button.AddThemeFontSizeOverride("font_size", 12); button.Pressed += () => SelectService(service);
            _services.Add(service, button); services.AddChild(button);
        }
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 12); AddChild(body);
        _inventory = new CraftingInventory { Name = "CraftInventory", CustomMinimumSize = new(270, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = .8f };
        body.AddChild(_inventory); _inventory.ItemSelected += _ => { CancelConfirmation(); _result.Text = ""; BuildRecipe(); };
        var recipe = new VBoxContainer { CustomMinimumSize = new(370, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.2f };
        recipe.AddThemeConstantOverride("separation", 6); body.AddChild(recipe);
        _target = new GearDragCard { Name = "CraftTarget", CustomMinimumSize = new(0, 64), AutowrapMode = TextServer.AutowrapMode.WordSmart, Text = "Drop an item here\nor choose one in your inventory" };
        _target.AddThemeFontSizeOverride("font_size", 14);
        _target.CanReceive = data => _service != CraftingService.Purification && _inventory.TryReadDrag(data, out _);
        _target.Receive = data => { if (_service != CraftingService.Purification && _inventory.TryReadDrag(data, out long id)) SelectItem(id); };
        recipe.AddChild(_target);
        _description = Caption("", 12); recipe.AddChild(_description);
        var scroll = new ScrollContainer { Name = "CraftRecipeScroll", CustomMinimumSize = new(0, 160), SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        recipe.AddChild(scroll);
        var details = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; details.AddThemeConstantOverride("separation", 8); scroll.AddChild(details);
        _options = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; details.AddChild(_options);
        _comparison = new VBoxContainer { Name = "CraftComparison", SizeFlagsHorizontal = SizeFlags.ExpandFill }; details.AddChild(_comparison);
        _status = Caption("", 12); _status.Name = "CraftStatus"; details.AddChild(_status);
        _commit = new Button { Name = "CraftCommit", Text = "Choose an item", Disabled = true, CustomMinimumSize = new(0, 39) };
        _commit.AddThemeFontSizeOverride("font_size", 13); _commit.Pressed += Commit; recipe.AddChild(_commit);
        _result = Caption("", 12); _result.Name = "CraftResult"; _result.MaxLinesVisible = 3; _result.MouseFilter = MouseFilterEnum.Stop; recipe.AddChild(_result);
        _confirmation = new ConfirmationDialog { Name = "CraftConfirmation", Title = "Confirm permanent crafting", DialogAutowrap = true, OkButtonText = "Commit craft", CancelButtonText = "Keep current item" };
        AddChild(_confirmation); _confirmation.Canceled += CancelConfirmation;
        _confirmation.Confirmed += () =>
        {
            var request = _pending; string key = _pendingKey; CancelConfirmation();
            if (request is not null && key == _stateKey && IsVisibleInTree() && Eligibility().Length == 0) Submit(request with { ConfirmPermanent = true });
        };
        for (Node? parent = GetParent(); parent is not null; parent = parent.GetParent()) if (parent is Sandbox sandbox) { _sandbox = sandbox; break; }
        VisibilityChanged += () => { if (!IsVisibleInTree()) CancelInteraction(); SynchronizePause(); };
    }

    public void SetView(ProgressionSnapshot state, ProgressionDefinition definition, bool inTown, bool alive, IReadOnlyList<InteractionDisplay> interactions)
    {
        string key = JsonData.Hash(state);
        if (_state is null || _state.Character.ContentHash != state.Character.ContentHash) _content = ProgressionContent.Create(definition);
        if (_stateKey != key || _inTown != inTown || _alive != alive || !SameReach(interactions)) CancelConfirmation();
        _state = state; _definition = definition; _inTown = inTown; _alive = alive; _interactions = interactions; _stateKey = key;
        _inventory.SetView(state, definition);
        _balance.Text = $"GREYHAVEN WORKBENCH   ·   {state.Character.Materials:N0} materials";
        foreach (var (service, button) in _services)
        {
            bool unlocked = state.Character.Services.Contains(service);
            button.Text = (service == _service ? "◆ " : "") + ServiceName(service) + "\n" + (unlocked ? SpecialistName(service) : "LOCKED · " + SpecialistName(service));
            button.TooltipText = unlocked ? Description(service) : UnlockReason(service);
            button.Modulate = unlocked ? Colors.White : new Color("9aa7b2");
        }
        BuildRecipe(); SynchronizePause();
    }

    private bool SameReach(IReadOnlyList<InteractionDisplay> next) => Enum.GetValues<CraftingService>().All(service =>
        At(_interactions, Specialist(service)) == At(next, Specialist(service)));
    private static bool At(IReadOnlyList<InteractionDisplay> interactions, string id) => interactions.Any(i => i.Id == id && i.Distance <= i.Range);
    public void SelectService(CraftingService service)
    {
        if (!Enum.IsDefined(service)) return;
        _inventory.CancelDrag(); CancelConfirmation(); _service = service; _result.Text = "";
        if (_state is not null) SetView(_state, _definition, _inTown, _alive, _interactions);
    }
    public void SelectItem(long id) => _inventory.SelectItem(id);

    private void BuildRecipe()
    {
        if (_state is null) return;
        Clear(_options);
        var item = _state.Character.Items.FirstOrDefault(i => i.Id == SelectedItemId);
        _description.Text = Description(_service);
        _target.Text = _service == CraftingService.Purification ? "DIVINE FRAGMENT\nChoose a fragment below" : item is null ? "Drop an item here\nor choose one in your inventory" : $"{EquipmentNames.For(item.DefinitionId)} · {item.Rarity}\n#{item.Id}" + (_state.Character.Equipment.Values.Contains(item.Id) ? " · EQUIPPED" : "");
        var slot = item is null ? EquipmentSlot.MainHand : _definition.Items.Single(i => i.Id == item.DefinitionId).Slots[0];
        _target.SetItemVisual(_service == CraftingService.Purification ? "" : item?.DefinitionId ?? "", slot, _state.Character.Discipline, _service == CraftingService.Purification ? null : item?.Rarity);
        _target.GetNode<GearItemIcon>("GearItemIcon").Visible = _service != CraftingService.Purification;
        if (_service is CraftingService.Tempering or CraftingService.Rebinding && item is not null)
        {
            IEnumerable<string> affixes = item.Affixes.Keys;
            if (_service == CraftingService.Tempering && item.Rarity == ItemRarity.Godwrought && !item.Affixes.ContainsKey("affix.damage")) affixes = affixes.Append("affix.damage");
            Choice("CraftExistingAffix", "Affix", affixes);
            if (_service == CraftingService.Rebinding)
            {
                var definition = _definition.Items.Single(i => i.Id == item.DefinitionId);
                Choice("CraftReplacementAffix", "Replace with", _definition.Affixes.Where(a => definition.Slots.All(a.Slots.Contains) && !item.Affixes.ContainsKey(a.Id)).Select(a => a.Id));
            }
        }
        if (_service == CraftingService.Engraving) Choice("CraftProperty", "Learned property", _state.Character.PropertyLibrary.Where(p => !p.StartsWith("evolution.", StringComparison.Ordinal)), EquipmentDetails.PowerName);
        if (_service == CraftingService.Purification) Choice("CraftFragment", "Owned fragment", _state.Character.OwnedFragments.Where(p => !_state.Character.PurifiedFragments.Contains(p)));
        if (_service == CraftingService.DivineGrafting) Choice("CraftLineage", "Permanent evolution", ["Serath", "Orrun"]);
        if (_service == CraftingService.Tempering && item?.Rarity == ItemRarity.Godwrought && _state.Character.Endgame is { } endgame)
            Choice("CraftPayment", "Payment", new[] { "" }.Concat(endgame.Catalysts.Where(p => p.Value > 0).Select(p => p.Key)), id => id.Length == 0 ? "Common materials" : "1 " + Readable(id));
        else _values["CraftPayment"] = "";
        RefreshPreview();
    }

    private void Choice(string name, string title, IEnumerable<string> options, Func<string, string>? format = null)
    {
        var row = new HBoxContainer(); _options.AddChild(row);
        var label = Caption(title, 12); label.CustomMinimumSize = new(107, 0); row.AddChild(label);
        var choice = new OptionButton { Name = name, SizeFlagsHorizontal = SizeFlags.ExpandFill, FitToLongestItem = false };
        choice.AddThemeFontSizeOverride("font_size", 12); row.AddChild(choice);
        string previous = _values.GetValueOrDefault(name, ""); int current = 0;
        foreach (string id in options)
        {
            int index = choice.ItemCount; choice.AddItem(format?.Invoke(id) ?? Readable(id)); choice.SetItemMetadata(index, id);
            if (id == previous) current = index;
        }
        choice.Disabled = choice.ItemCount == 0;
        if (choice.ItemCount > 0) choice.Select(current);
        _values[name] = choice.ItemCount == 0 ? "" : choice.GetItemMetadata(current).AsString();
        choice.ItemSelected += index => { CancelConfirmation(); _values[name] = choice.GetItemMetadata((int)index).AsString(); _result.Text = ""; RefreshPreview(); };
    }

    private CraftingRequest Request()
    {
        string Value(string name) => _values.GetValueOrDefault(name, "");
        string catalyst = _service == CraftingService.DivineGrafting && _state?.Character.Endgame is not null
            ? Value("CraftLineage") == "Orrun" ? "material.orrun_oath" : "material.serath_memory" : _service == CraftingService.Tempering ? Value("CraftPayment") : "";
        return new("", _service, _service == CraftingService.Purification ? 0 : SelectedItemId,
            Value("CraftExistingAffix"), Value("CraftReplacementAffix"), Value("CraftProperty"), Value("CraftLineage"), Value("CraftFragment"), CatalystId: catalyst.Length == 0 ? null : catalyst);
    }

    private void RefreshPreview()
    {
        if (_state is null) return;
        if (_service == CraftingService.DivineGrafting)
            _description.Text = EquipmentDetails.Evolution(_values.GetValueOrDefault("CraftLineage", "Serath"));
        else if (_service == CraftingService.Engraving)
            _description.Text = "Engrave an empty slot. " + EquipmentDetails.Power(_values.GetValueOrDefault("CraftProperty"));
        Preview = ProgressionSession.Restore(_content, _state).PreviewCraft(Request());
        ShowPreview(Preview, Request());
        string reason = Eligibility();
        _status.Text = reason.Length > 0 ? reason : Preview.RequiresConfirmation ? "Review the permanent change before confirming." : "Ready. Inspection has not changed your item.";
        _status.Modulate = reason.Length > 0 ? new("eeb196") : new("9eddb4");
        _commit.Text = Preview.RequiresConfirmation ? "Review " + ServiceName(_service).ToLowerInvariant() : ServiceName(_service) + " · apply improvement";
        _commit.Disabled = reason.Length > 0 || _submitting;
    }

    private string Eligibility()
    {
        if (_state is null) return "Choose an item to begin.";
        if (!_state.Character.Services.Contains(_service)) return UnlockReason(_service);
        if (!_inTown) return "Return to Greyhaven to craft.";
        if (!_alive) return "Cannot craft while defeated.";
        if (!At(_interactions, Specialist(_service))) return "Visit " + SpecialistName(_service) + " to apply this craft. You can inspect the result here.";
        if (_service != CraftingService.Purification && SelectedItemId == 0) return "Choose an item or drag it into the workbench.";
        return Preview?.Success == true ? "" : Preview?.Reason ?? "Choose a recipe.";
    }

    private void Commit()
    {
        if (!IsVisibleInTree() || _submitting) return;
        RefreshPreview(); if (Eligibility().Length > 0 || Preview is null) return;
        var request = Request() with { OperationId = Guid.NewGuid().ToString("N") };
        if (Preview.RequiresConfirmation)
        {
            _pending = request; _pendingKey = _stateKey;
            _confirmation.DialogText = ConfirmationText(Preview, request, _definition);
            _confirmation.PopupCentered(new(Math.Min(560, (int)GetViewportRect().Size.X - 40), 320));
        }
        else Submit(request);
    }

    private void Submit(CraftingRequest request)
    {
        if (_submitting || Preview?.Success != true || Eligibility().Length > 0) return;
        _submitted = Preview; _submittedName = ServiceName(_service); _submitting = true; _commit.Disabled = true;
        CraftRequested?.Invoke(request);
        // Shipping directors answer synchronously after their authoritative transaction and refresh.
        if (_submitting) ReportResult(false, "The craft did not complete. Your current item is shown above.");
    }
    public void ReportResult(bool success, string reason)
    {
        if (!_submitting) return;
        _submitting = false;
        _result.Text = success && _submitted is not null ? _submittedName + " complete · " + (_submitted.Before.Character.Materials - _submitted.After.Character.Materials) + " materials spent.\n" + ResultSummary(_submitted) : "Craft not applied: " + reason;
        _result.Modulate = success ? new("9eddb4") : new("eeb196");
        _result.TooltipText = _result.Text;
        _submitted = null; RefreshPreview();
    }

    private void CancelConfirmation() { _pending = null; _pendingKey = ""; _confirmation?.Hide(); }
    public void CancelInteraction() { _inventory?.CancelDrag(); CancelConfirmation(); }
    public void SynchronizePause()
    {
        if (_sandbox is null || !GodotObject.IsInstanceValid(_sandbox)) return;
        if (!ReferenceEquals(_seenSession, _sandbox.Session))
        { _seenSession = _sandbox.Session; CancelInteraction(); _result.Text = ""; _result.TooltipText = ""; }
        bool visible = IsVisibleInTree();
        if (visible || _pauseHeld) _sandbox.SetModalPaused("crafting-workbench", visible);
        _pauseHeld = visible;
    }
    public override void _Notification(int what) { if (what == NotificationApplicationFocusOut && IsInsideTree()) CancelInteraction(); }
    public override void _ExitTree() { CancelInteraction(); if (_pauseHeld && _sandbox is not null && GodotObject.IsInstanceValid(_sandbox)) _sandbox.SetModalPaused("crafting-workbench", false); }
    private string UnlockReason(CraftingService service)
    {
        var objective = _definition.Objectives.FirstOrDefault(o => o.Service == service.ToString());
        return "Workshop locked. " + (objective is null ? "Visit " + SpecialistName(service) + "." : _definition.Strings[objective.JournalKey]);
    }
    private static void Clear(Node node) { foreach (var child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }
    private static Label Caption(string text, int size = 13)
    { var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill }; label.AddThemeFontSizeOverride("font_size", size); return label; }
    private static string Readable(string id) => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Split('.').Last().Replace('_', ' '));
    private static string ServiceName(CraftingService service) => service == CraftingService.DivineGrafting ? "Divine Grafting" : service.ToString();
    private static string Specialist(CraftingService service) => service switch { CraftingService.Tempering => "service.torren", CraftingService.Rebinding => "npc.oris", CraftingService.Engraving => "hub.workshops", CraftingService.Extraction => "npc.kesh", CraftingService.Purification => "npc.cael", _ => "service.mara" };
    private static string SpecialistName(CraftingService service) => service switch { CraftingService.Tempering => "Torren", CraftingService.Rebinding => "Oris", CraftingService.Engraving => "the workshops", CraftingService.Extraction => "Kesh", CraftingService.Purification => "Sister Cael", _ => "Mara" };
    private static string Description(CraftingService service) => service switch
    {
        CraftingService.Tempering => "Strengthen an existing affix. Godwrought damage can be tempered five times.",
        CraftingService.Rebinding => "Replace an unwanted affix with a new one at its starting strength.",
        CraftingService.Engraving => "Place a learned property into an item's empty engraving slot.",
        CraftingService.Extraction => "Destroy a Legendary item to learn its exceptional property.",
        CraftingService.DivineGrafting => "Choose a permanent Serath or Orrun evolution for an awakened Ashcleaver.",
        _ => "Purify an owned divine fragment while preserving ownership."
    };
}
