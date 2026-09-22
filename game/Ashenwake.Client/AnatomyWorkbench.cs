using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Godot;

namespace Ashenwake.Client;

/// <summary>Local inspection state. Applying a choice always requests the existing service transaction.</summary>
public partial class AnatomyWorkbench : VBoxContainer
{
    public event Action<string, string?>? ImplantRequested;
    public event Action<string>? ManifestationRequested;
    public event Action? VisitMaraRequested, ReturnRequested;
    private AdventureContent _content = null!;
    private AdventureDefinition _definition = null!;
    private AdventureState _state = null!;
    private AdventureSession _source = null!;
    private CombatView _combat = null!;
    private CharacterAppearance _appearance = null!;
    private AnatomyPreviewResult _forecast = null!;
    private AnatomyDiagram _diagram = null!;
    private CharacterPreview _preview = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _cards = null!;
    private Label _heading = null!, _access = null!, _forecastText = null!;
    private Button _apply = null!, _reset = null!, _visit = null!, _return = null!;
    private string _slot = "Eyes", _fragment = "", _manifestation = "", _stateKey = "", _renderKey = "";
    private bool _canApply, _inHub, _canReturn, _inspecting, _sending;
    private int _groundLoot;
    public string SelectedSlot => _slot;
    public bool Inspecting => _inspecting;
    public string PreviewAppearanceKey => _preview.AppearanceKey;
    public void DiscardInspection() => ResetInspection();

    public override void _Ready()
    {
        Name = "AnatomyWorkbench"; SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 10);
        _heading = Text("DIVINE ANATOMY", 22, "e8dda7"); AddChild(_heading);
        _access = Text("", 13, "b9cccf"); AddChild(_access);
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 18); AddChild(body);
        var visual = new VBoxContainer { CustomMinimumSize = new(270, 0) }; body.AddChild(visual);
        var views = new HBoxContainer(); visual.AddChild(views);
        views.AddChild(ActionButton("AnatomyBodyView", "Body map", () => ShowCharacter(false)));
        views.AddChild(ActionButton("AnatomyCharacterView", "Character", () => ShowCharacter(true)));
        _diagram = new AnatomyDiagram { SizeFlagsVertical = SizeFlags.ExpandFill };
        _diagram.SlotSelected += SelectSlot; visual.AddChild(_diagram);
        _preview = new CharacterPreview { Visible = false }; visual.AddChild(_preview);
        _scroll = new ScrollContainer { Name = "AnatomyCardsScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddChild(_scroll);
        _cards = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _cards.AddThemeConstantOverride("separation", 10); _scroll.AddChild(_cards);
        _forecastText = Text("", 13, "dce9e4"); _forecastText.Name = "AnatomyForecast";
        _forecastText.CustomMinimumSize = new(0, 60); AddChild(_forecastText);
        var actions = new HBoxContainer(); AddChild(actions);
        _apply = ActionButton("AnatomyApply", "Apply implant", Apply); actions.AddChild(_apply);
        _reset = ActionButton("AnatomyReset", "Keep current anatomy", ResetInspection); actions.AddChild(_reset);
        _visit = ActionButton("AnatomyVisitMara", "Walk to Mara", () => VisitMaraRequested?.Invoke()); actions.AddChild(_visit);
        _return = ActionButton("AnatomyReturn", "Return to Greyhaven", () => ReturnRequested?.Invoke()); actions.AddChild(_return);
        VisibilityChanged += () => { if (!IsVisibleInTree()) ResetInspection(); };
    }

    public void SetView(AdventureContent content, AdventureState state, CombatView combat, IReadOnlyList<string> manifestations,
        bool inHub, bool canApply, bool canReturn, int groundLoot, string ashcleaverEvolution = "")
    {
        _combat = combat; _state = state; _inHub = inHub; _canApply = canApply; _canReturn = canReturn; _groundLoot = groundLoot;
        _appearance = CharacterAppearance.FromCombat(combat, manifestations, ashcleaverEvolution);
        string stateKey = JsonData.Hash(state);
        bool changed = _stateKey != stateKey || !ReferenceEquals(_content, content);
        _content = content;
        if (changed)
        {
            _stateKey = stateKey; _definition = content.Capture(); _source = AdventureSession.Restore(content, state);
            _inspecting = false; _manifestation = "";
            _fragment = state.Anatomy.GetValueOrDefault(_slot, "");
        }
        string key = $"{stateKey}/{_appearance.Key}/{inHub}/{canApply}/{canReturn}/{groundLoot}";
        if (_renderKey == key) return;
        _renderKey = key; Refresh();
    }

    public void InspectReward()
    {
        if (_source is null) return;
        var reward = _definition.Fragments.Single(f => f.Id == _definition.RewardFragment);
        _slot = reward.Slot; _fragment = reward.Id; _manifestation = ""; _inspecting = true;
        ShowCharacter(true); Refresh();
    }

    public void SetAvailableHeight(float height)
    {
        _scroll.CustomMinimumSize = new(0, Math.Clamp(height - 230, 220, 370));
        _diagram.CustomMinimumSize = new(270, 300);
        _preview.SetCompact(height < 600, Math.Clamp(height - 230, 300, 420));
    }

    private void SelectSlot(string slot)
    {
        _slot = slot; _fragment = _state.Anatomy.GetValueOrDefault(slot, ""); _manifestation = ""; _inspecting = false;
        _scroll.ScrollVertical = 0; Refresh();
    }

    private void SelectFragment(string id)
    { _fragment = id; _manifestation = ""; _inspecting = true; ShowCharacter(true); Refresh(); }

    private void SelectManifestation(string id)
    { _manifestation = id; _inspecting = true; ShowCharacter(true); Refresh(); }

    private void ResetInspection()
    {
        if (_source is null) return;
        _fragment = _state.Anatomy.GetValueOrDefault(_slot, ""); _manifestation = ""; _inspecting = false;
        Refresh();
    }

    private void ShowCharacter(bool show)
    { _diagram.Visible = !show; _preview.Visible = show; }

    private void Refresh()
    {
        if (_source is null) return;
        _forecast = _manifestation.Length > 0 ? _source.PreviewManifestation(_manifestation) : _source.PreviewFragment(_slot, _fragment.Length == 0 ? null : _fragment);
        var before = _forecast.Before; var after = _forecast.After;
        _heading.Text = "DIVINE ANATOMY · " + (_inspecting ? "PREVIEW" : "INSTALLED");
        _access.Text = _canApply ? "Mara's surgery · inspect a choice, then apply it when ready."
            : _inHub ? "Inspection is available here. Approach Mara to apply changes." : "Plan your next implant here. Return to Mara in Greyhaven to apply it.";
        _diagram.SetAnatomy(before.Anatomy, _slot, _inspecting && _manifestation.Length == 0 ? _slot : null, _fragment.Length > 0);
        var active = after.Manifestations.Where(m => m.Active).Select(m => m.SelectedId!).ToArray();
        _preview.SetAppearance(_appearance with
        {
            ManifestationMask = CharacterAppearance.Manifestations(active),
            AnatomyMask = CharacterAppearance.AnatomyFragments(after.Anatomy.Values)
        });
        _preview.SetCaption(_inspecting ? "Preview only · apply below to change your character" : "Installed anatomy · drag to rotate");
        foreach (var child in _cards.GetChildren()) { _cards.RemoveChild(child); child.QueueFree(); }
        _cards.AddChild(Text(_slot.ToUpperInvariant() + " · IMPLANT SLOT", 16, "e8dda7"));
        string current = _state.Anatomy.GetValueOrDefault(_slot, "");
        _cards.AddChild(Text("Installed: " + FragmentName(current), 14, "79e0cb"));
        if (current.Length > 0) _cards.AddChild(Text(FragmentDescription(current), 12, "b9cccf"));
        foreach (var fragment in _definition.Fragments.Where(f => f.Slot == _slot)) FragmentCard(fragment, current);
        if (!_definition.Fragments.Any(f => f.Slot == _slot))
            _cards.AddChild(Text("No fragments discovered for this slot yet. Other body slots hold your available implants.", 13, "b9cccf"));
        var empty = ActionButton("AnatomyEmptySlot", "Preview empty " + _slot.ToLowerInvariant() + " slot", () => SelectFragment(""));
        empty.Disabled = current.Length == 0; _cards.AddChild(empty);
        _cards.AddChild(new HSeparator());
        _cards.AddChild(Text("MANIFESTATIONS · VISIBLE POWER", 16, "e8dda7"));
        _cards.AddChild(Text("Choose one form per threshold. Falling below its threshold suppresses the form and remembers your choice.", 12, "b9cccf"));
        foreach (var manifestation in _definition.Manifestations)
        {
            var threshold = before.Manifestations.Single(m => m.Threshold == manifestation.Threshold);
            bool selected = threshold.SelectedId == manifestation.Id;
            string state = selected ? threshold.Active ? "ACTIVE" : "REMEMBERED · SUPPRESSED" : threshold.Available ? "AVAILABLE" : "LOCKED";
            var box = Card("AnatomyManifestationCard_" + manifestation.Id);
            box.AddChild(Text($"{Readable(manifestation.Id)} · {manifestation.Threshold} Resonance · {state}", 14, selected ? "79e0cb" : "e8dda7"));
            box.AddChild(Text("Benefit: " + manifestation.Benefit + "\nCost: " + manifestation.Complication, 12, "d2dfe0"));
            var button = ActionButton("AnatomyManifestation_" + manifestation.Id, "Preview " + Readable(manifestation.Id), () => SelectManifestation(manifestation.Id));
            button.Disabled = !threshold.Available;
            if (!threshold.Available) button.TooltipText = "Requires " + manifestation.Threshold + " installed Resonance. Apply your fragment change first.";
            box.AddChild(button);
        }
        int delta = after.Resonance - before.Resonance;
        var lines = new List<string> { $"RESONANCE  {before.Resonance} → {after.Resonance}  ({delta:+0;-0;0})" };
        if (!_forecast.Success) lines.Add(_forecast.Reason);
        else
        {
            var combinations = before.QualifyingConcordances.Union(after.QualifyingConcordances).Union(before.DiscoveredConcordances).Distinct().ToArray();
            foreach (string id in combinations)
                lines.Add(Readable(id) + ": " + (after.QualifyingConcordances.Contains(id) ? "tags aligned" : "discovered · tags no longer aligned") +
                    (!before.DiscoveredConcordances.Contains(id) && after.QualifyingConcordances.Contains(id) ? " · new discovery on apply" : ""));
            foreach (var threshold in after.Manifestations)
            {
                var previous = before.Manifestations.Single(m => m.Threshold == threshold.Threshold);
                string status = threshold.Available ? threshold.Active ? Readable(threshold.SelectedId!) + " active" : "choose a form" : $"{threshold.Threshold - after.Resonance} more Resonance needed";
                if (previous.Active && !threshold.Active) status += " · selected form will be suppressed";
                if (!previous.Available && threshold.Available) status += " · threshold reached on apply";
                lines.Add($"{threshold.Threshold}: {status}");
            }
        }
        _forecastText.Text = string.Join("\n", lines);
        bool changed = _manifestation.Length > 0 ? before.Manifestations.All(m => m.SelectedId != _manifestation) : current != _fragment;
        _apply.Text = _manifestation.Length > 0 ? "Apply " + Readable(_manifestation) : _fragment.Length == 0 ? "Remove " + _slot.ToLowerInvariant() + " implant" : "Implant " + FragmentName(_fragment);
        _apply.Disabled = !_canApply || !_forecast.Success || !_forecast.CanApply || !changed || _sending;
        _reset.Disabled = !_inspecting;
        _visit.Visible = _inHub && !_canApply; _visit.Disabled = !_combat.Actors.Any(a => a.Id == 1 && a.Health > 0);
        _return.Visible = !_inHub; _return.Disabled = !_canReturn;
        _return.Text = _groundLoot > 0 ? $"Review {_groundLoot} ground drops & return" : "Return to Mara in Greyhaven";
    }

    private void FragmentCard(AnatomyFragment fragment, string current)
    {
        bool owned = _state.OwnedFragments.Contains(fragment.Id);
        var box = Card("AnatomyFragmentCard_" + fragment.Id);
        box.AddChild(Text(FragmentName(fragment.Id) + (fragment.Id == current ? " · INSTALLED" : owned ? " · OWNED" : " · UNDISCOVERED"), 15, "e8dda7"));
        box.AddChild(Text(fragment.Slot + " · " + fragment.Resonance + " Resonance · " + string.Join(" / ", fragment.Tags), 12, "79e0cb"));
        box.AddChild(Text(FragmentDescription(fragment.Id), 13, "d2dfe0"));
        if (fragment.Id == "fragment.heart_serath" && owned)
        {
            var lesson = Text("HOW TO USE IT · Let Burning or Poison finish a weakened enemy. A damage-over-time kill raises a temporary allied spirit; a direct-hit kill alone does not. Pyrebound Treads can supply Burning when equipped.\n" +
                "This fragment contributes " + fragment.Resonance + " Resonance while implanted. Preview the total below: a newly reached threshold lets you choose a manifestation after applying the implant. The implant and the manifestation are separate choices.", 12, "b9cccf");
            lesson.Name = "AnatomyHeartLesson"; box.AddChild(lesson);
        }
        if (!owned) box.AddChild(Text(fragment.Id == _definition.RewardFragment ? "Defeat the Bell Saint to claim this fragment." : "Find this fragment during your journey.", 12, "b9cccf"));
        var button = ActionButton("AnatomyFragment_" + fragment.Id, "Inspect " + FragmentName(fragment.Id), () => SelectFragment(fragment.Id));
        button.Disabled = !owned; box.AddChild(button);
    }

    private VBoxContainer Card(string name)
    {
        var panel = new PanelContainer { Name = name };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new("172831"),
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 10,
            ContentMarginBottom = 10
        });
        var rows = new VBoxContainer(); rows.AddThemeConstantOverride("separation", 7); panel.AddChild(rows); _cards.AddChild(panel); return rows;
    }

    private void Apply()
    {
        if (_sending || _apply.Disabled) return;
        _sending = true;
        try
        {
            if (_manifestation.Length > 0) ManifestationRequested?.Invoke(_manifestation);
            else ImplantRequested?.Invoke(_slot, _fragment.Length == 0 ? null : _fragment);
        }
        finally { _sending = false; Refresh(); }
    }

    private string FragmentName(string id) => id.Length == 0 ? "Empty" : _combat.Fragments.FirstOrDefault(f => f.Id == id)?.Name ?? Readable(id);
    private string FragmentDescription(string id)
    {
        string description = _combat.Fragments.FirstOrDefault(f => f.Id == id)?.Description ?? "";
        return System.Text.RegularExpressions.Regex.Replace(description, @"(\d+) ticks?", match =>
            (int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * Ashenwake.Core.Simulation.FixedStepClock.SecondsPerTick)
                .ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " seconds");
    }
    private static string Readable(string id) => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(string.Join(' ', id.Split('.').Skip(1)).Replace('_', ' '));
    private static Label Text(string text, int size, string color)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", new Color(color)); return label;
    }
    private static Button ActionButton(string name, string text, Action action)
    {
        var button = new Button { Name = name, Text = text, CustomMinimumSize = new(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        button.AddThemeFontSizeOverride("font_size", 13); button.Pressed += action; return button;
    }
}
