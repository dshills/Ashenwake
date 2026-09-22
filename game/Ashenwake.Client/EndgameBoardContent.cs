using Godot;

namespace Ashenwake.Client;

public partial class EndgameHud
{
    private void Sigils()
    {
        var view = _view!; _catalogTitle.Text = "YOUR FRACTURE SIGILS";
        if (view.Sigils.Length == 0)
        {
            _detailTitle.Text = "A new route awaits"; _detailStatus.Text = "RECOVERY SIGIL · TIER 1";
            _catalog.AddChild(Text("No unconsumed Sigils remain.", 15));
            Row("Claim a free tier-one recovery Sigil at the Fracture gate in eastern Greyhaven. Your equipment, discoveries and character progress are preserved.");
            GateGuidance();
            RequestButton("Claim recovery Sigil · free", new(BoardAction.Recovery), "ExpeditionRecovery");
            ReturnAction(); return;
        }
        if (!view.Sigils.Any(s => s.Id == _selectedSigil)) { _selectedSigil = view.Sigils[0].Id; _oldModifier = _newModifier = ""; }
        foreach (var sigil in view.Sigils)
        {
            var card = Card("ExpeditionSigil" + sigil.Id, sigil.Region, $"TIER {sigil.Tier} · {sigil.RewardTendency}\n{sigil.Modifiers.Length} rules · {sigil.BossFamily}",
                "sigil", sigil.RegionId.Length > 0 ? sigil.RegionId : sigil.Region, sigil.Id == _selectedSigil, () => SelectSigil(sigil.Id));
            card.TooltipText = $"Sigil #{sigil.Id}\n" + string.Join('\n', sigil.Modifiers.Select(m => m.Name));
        }
        var selected = view.Sigils.Single(s => s.Id == _selectedSigil);
        _detailTitle.Text = selected.Region; _detailStatus.Text = $"TIER {selected.Tier} · {selected.Rooms.Length} ROOMS · 3 ATTEMPTS";
        ShowRoute(selected.Rooms.Select((room, i) => new ExpeditionRouteNode(room, i == selected.Rooms.Length - 1 ? "Final confrontation" : "Encounter " + (i + 1), "upcoming")).ToArray());
        Row($"Boss family · {selected.BossFamily}     Reward tendency · {selected.RewardTendency}", 14);
        Row("Each death reduces the base material reward by 20%, to a minimum of 40%. Entry consumes this Sigil.");
        GateGuidance();
        foreach (var modifier in selected.Modifiers) { Section(modifier.Name); Row(modifier.Description); }
        if (selected.Inherited.Length > 0) { Section("FINAL BOSS INHERITANCE"); Row(string.Join(" · ", selected.Inherited)); }
        foreach (string skipped in selected.Skipped) Row("Not inherited · " + skipped, 12);
        Attunement(selected);
        RequestButton("Consume Sigil & enter", new(BoardAction.Fracture, Sigil: selected.Id), "ExpeditionEnter");
        ReturnAction();
    }
    private void Attunement(EndgameSigilDisplay sigil)
    {
        Section("ATTUNEMENT · PREVIEW A RULE CHANGE");
        Row($"Cost · 5 common materials     Available · {_view!.Materials}");
        if (sigil.Modifiers.Length == 0) { Row("This Sigil has no replaceable rule."); return; }
        if (!sigil.Modifiers.Any(m => m.Id == _oldModifier)) _oldModifier = sigil.Modifiers[0].Id;
        var old = sigil.Modifiers.Single(m => m.Id == _oldModifier);
        Row("CURRENT RULE", 12);
        Choice(sigil.Modifiers, _oldModifier, "ExpeditionOldModifier", id => { CancelConfirmation(); _oldModifier = id; _newModifier = ""; Rebuild(true); });
        var current = Text(old.Description, 12); current.Name = "ExpeditionCurrentRule"; _rows.AddChild(current);
        var replacements = sigil.Replacements.GetValueOrDefault(_oldModifier, []);
        if (replacements.Length == 0) { Row("No other compatible rule is available at this tier."); return; }
        if (!replacements.Any(m => m.Id == _newModifier)) _newModifier = replacements[0].Id;
        Row("REPLACE WITH", 12);
        Choice(replacements, _newModifier, "ExpeditionNewModifier", id => { CancelConfirmation(); _newModifier = id; Rebuild(true); });
        var next = replacements.Single(m => m.Id == _newModifier);
        var preview = Text(next.Description, 12); preview.Name = "ExpeditionReplacementRule"; _rows.AddChild(preview);
        Row("Other rules remain on the Sigil. Inspecting a replacement spends nothing; the route above reflects the current Sigil until you apply it.", 12);
        var request = new BoardRequest(BoardAction.Attune, Sigil: sigil.Id, Id: _oldModifier, Value: _newModifier);
        var button = Button("Apply attunement · 5 materials", () => Request(request), "ExpeditionAttune", _rows);
        string reason = Availability(request); button.Disabled = reason.Length > 0; button.TooltipText = reason;
        if (reason.Length > 0) Row(reason, 12);
    }
    private void Hunts()
    {
        var view = _view!; _catalogTitle.Text = "RECONSTRUCTIONS OF DEAD GODS";
        var known = view.Hunts.Where(h => !h.Secret || h.Unlocked).ToArray();
        if (!known.Any(h => h.Id == _selectedHunt)) _selectedHunt = known.FirstOrDefault()?.Id ?? "";
        foreach (var hunt in known)
            Card("ExpeditionHunt" + hunt.Id.Replace('.', '_'), hunt.Name, hunt.Unlocked ? "AVAILABLE · 2 ATTEMPTS\n" + Readable(hunt.Material) : "LOCKED · CLEAR TIER " + hunt.RequiredTier,
                "hunt", hunt.Id, hunt.Id == _selectedHunt, () => SelectHunt(hunt.Id));
        if (view.Hunts.Any(h => h.Secret && !h.Unlocked)) _catalog.AddChild(Text("An unremembered shape remains hidden. Explore higher tiers and complete the four known God Hunts.", 12));
        var selected = known.FirstOrDefault(h => h.Id == _selectedHunt);
        if (selected is null) { _detailTitle.Text = "No known hunts"; return; }
        _detailTitle.Text = selected.Name; _detailStatus.Text = selected.Unlocked ? "AVAILABLE · 3 PHASES · 2 ATTEMPTS" : selected.Gate;
        ShowRoute(selected.Phases.Select((phase, i) => new ExpeditionRouteNode(Readable(phase), "Phase " + (i + 1), "upcoming")).ToArray(), true);
        Row("Victory grants an evolution catalyst for permanent crafting. Each phase must be defeated before the next opens.");
        Section("CATALYST REWARD"); Row(Readable(selected.Material), 16);
        GateGuidance();
        for (int i = 0; i < selected.Phases.Length; i++)
        { Section($"{i + 1} · {Readable(selected.Phases[i])}"); if (i < selected.Counterplay.Length) Row(selected.Counterplay[i]); }
        RequestButton("Begin God Hunt", new(BoardAction.Hunt, Id: selected.Id), "ExpeditionEnter");
        ReturnAction();
    }
    private void Run()
    {
        var view = _view!; var run = view.Run; _catalogTitle.Text = "EXPEDITION STATUS";
        if (run is null)
        {
            _detailTitle.Text = "Choose your next expedition";
            _catalog.AddChild(Text("No expedition has begun.", 16));
            Row("Inspect your Sigils or the available God Hunts. Preview the route and its rules before committing.");
            ActionButton("Browse Sigils", () => ShowTab("Sigils"), "ExpeditionBrowseSigils"); return;
        }
        _detailTitle.Text = run.Name; _detailStatus.Text = $"{run.Kind.ToUpperInvariant()} · {run.Status.ToUpperInvariant()} · {run.Region}";
        Metric("ATTEMPTS REMAINING", run.Attempts.ToString(), "ExpeditionAttempts");
        Metric("BASE MATERIAL REWARD", run.RewardPercent + "%", "ExpeditionRewardPercent");
        Metric("DEATHS THIS RUN", run.Deaths.ToString(), "ExpeditionDeaths");
        _catalog.AddChild(Text("Cleared rooms remain complete after retry. Save to resume this exact encounter later.", 12));
        var nodes = Enumerable.Range(0, run.RoomCount).Select(i => new ExpeditionRouteNode(
            run.Rooms is { } rooms && i < rooms.Length ? rooms[i] : (run.Kind == "Fracture" ? "Room " : "Phase ") + (i + 1),
            run.Kind == "Fracture" && i == run.RoomCount - 1 ? "Final confrontation" : "Stage " + (i + 1),
            run.Status == "Completed" || i + 1 < run.Room || i + 1 == run.Room && run.Cleared ? "completed" :
            i + 1 == run.Room ? run.Status is "Failed" or "Abandoned" || run.CanRetry ? "failed" : "current" : "upcoming")).ToArray();
        ShowRoute(nodes, run.Kind != "Fracture");
        if (run.Status == "Active")
        {
            Row(run.CanRetry ? "An attempt was spent. Retry the current encounter when ready." : run.Cleared ? "Area secured. Collect your spoils, then continue." : "The encounter is active. Close this screen to resume.", 15);
            if (run.Cleared && run.Room >= run.RoomCount) Row("Finishing records the expedition reward. You can still collect this room's loot before returning to Greyhaven.", 12);
            if (view.GroundDrops > 0) Row($"{view.GroundDrops} uncollected ground drops remain. Travel will ask before leaving them behind.");
            foreach (string rule in run.Rules) { Section(rule); }
            foreach (string counterplay in run.Counterplay) Row(counterplay);
            if (run.Inherited.Length > 0) { Section("FINAL BOSS INHERITANCE"); Row(string.Join(" · ", run.Inherited)); }
            foreach (string skipped in run.Skipped) Row("Not inherited · " + skipped, 12);
            RequestButton(run.Room >= run.RoomCount ? "Finish expedition" : "Continue to next room", new(BoardAction.Advance), "ExpeditionContinue");
            if (run.CanRetry) RequestButton("Retry this encounter", new(BoardAction.Retry), "ExpeditionRetry");
            RequestButton("Abandon expedition", new(BoardAction.Abandon), "ExpeditionAbandon");
        }
        else
        {
            if (run.Status == "Completed" && view.Reward is { } reward && reward.RunId == run.Id) RewardReceipt(reward);
            else Row(run.Status == "Completed" ? view.RewardSummary : "No final expedition reward was granted. Earned character progress is preserved. Return to Greyhaven to prepare another expedition.", 15);
            RequestButton("Return to Greyhaven", new(BoardAction.Hub), "ExpeditionReturn");
            if (view.InHub) ActionButton("Choose the next Sigil", () => ShowTab("Sigils"), "ExpeditionBrowseSigils");
        }
        Rule(); Button("Verify & export replay", () => ReplayRequested?.Invoke(), "ExpeditionReplay", _rows);
    }
    private void Rewards()
    {
        var view = _view!; _catalogTitle.Text = "YOUR PERMANENT REWARDS"; _detailTitle.Text = "Power carried home"; _detailStatus.Text = "MATERIALS · MASTERY · EVOLUTION CATALYSTS";
        Metric("COMMON MATERIALS", view.Materials.ToString(), "ExpeditionMaterials"); Metric("HIGHEST CLEARED FRACTURE", "Tier " + view.HighestTier, "ExpeditionHighestTier");
        _catalog.AddChild(Text("EVOLUTION CATALYSTS", 13));
        foreach (var pair in view.Catalysts.Where(p => p.Value > 0)) _catalog.AddChild(Text($"{Readable(pair.Key)} · {pair.Value}", 14));
        if (!view.Catalysts.Any(p => p.Value > 0)) _catalog.AddChild(Text("Win a God Hunt to earn its matching catalyst.", 12));
        if (view.Reward is { } reward) { Section("LAST COMPLETED EXPEDITION"); RewardReceipt(reward); }
        else Row("Complete an expedition to record its permanent rewards here.");
        Section("ASHCLEAVER · A PERMANENT CHOICE");
        Row("Awaken Ashcleaver through 1,000 credited burning kills, learn the matching lineage fragment, then approach Mara in Greyhaven. Character → Craft → Divine Grafting shows exact requirements and costs.");
        Row("Serath · Burning victims can rise as temporary flaming revenants.\nOrrun · The awakened flame wave becomes a molten seismic attack that ignites enemies.");
        Row("Your Sigils, catalysts, attempts and earned rewards are preserved in the same character save.");
        ActionButton("Browse God Hunts", () => ShowTab("Hunts"), "ExpeditionBrowseHunts");
    }
    private void RewardReceipt(EndgameRewardDisplay reward)
    {
        var receipt = Text($"EARNED · EXPEDITION #{reward.RunId}\n+{reward.Materials} common materials\n+{reward.Mastery} mastery" +
            (reward.CatalystCount > 0 ? $"\n+{reward.CatalystCount} {Readable(reward.Catalyst)}" : ""), 16);
        receipt.Name = "ExpeditionRewardReceipt"; _rows.AddChild(receipt);
        Row("These rewards are already part of your character. No claim action is needed.", 12);
    }
    private void GateGuidance()
    {
        if (_view!.Run is { Status: "Active" }) Row("Finish or abandon your current expedition before starting another.");
        else if (!_view.InHub) Row("Return to Greyhaven to prepare an expedition.");
        else if (!_view.AtGate)
        {
            Row("Approach the Fracture gate in eastern Greyhaven to enter, recover or attune a Sigil.");
            ActionButton("Walk to the Fracture gate", RequestGateApproach, "ExpeditionApproachGate").Disabled = !_view.Alive;
        }
        else Row("At the Fracture gate · ready to prepare your next expedition.", 12);
    }
    private void ReturnAction()
    { if (!_view!.InHub && _view.Run is not { Status: "Active" }) RequestButton("Return to Greyhaven", new(BoardAction.Hub), "ExpeditionReturn"); }
    private void ShowRoute(ExpeditionRouteNode[] nodes, bool hunt = false) { _route.Visible = true; _route.SetRoute(nodes, hunt); }
    private void Section(string title) { Rule(); Row(title, 14); }
    private void Metric(string name, string value, string nodeName)
    {
        var panel = Panel(); var box = Stack(); panel.AddChild(box); box.AddChild(Text(name, 11));
        var label = Text(value, 23); label.Name = nodeName; box.AddChild(label); _catalog.AddChild(panel);
    }
    private Button Card(string name, string title, string subtitle, string kind, string identity, bool selected, Action action)
    {
        var button = new Button { Name = name, ToggleMode = true, ButtonPressed = selected, CustomMinimumSize = new(0, 105), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (string state in new[] { "normal", "hover", "pressed", "focus" }) button.AddThemeStyleboxOverride(state, CardStyle(state == "pressed" ? "263c45" : "14232c", state is "pressed" or "focus" ? "ddca92" : "526e77"));
        button.Pressed += action; _catalog.AddChild(button);
        var content = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; button.AddChild(content); content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); content.OffsetLeft = content.OffsetTop = 10; content.OffsetRight = content.OffsetBottom = -10;
        var emblem = new ExpeditionEmblem { CustomMinimumSize = new(48, 48), SizeFlagsVertical = SizeFlags.ShrinkCenter }; content.AddChild(emblem); emblem.SetIdentity(kind, identity);
        var copy = Stack(); copy.MouseFilter = MouseFilterEnum.Ignore; content.AddChild(copy); copy.AddThemeConstantOverride("separation", 5);
        var heading = Text(title, 15); heading.MaxLinesVisible = 2; heading.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; copy.AddChild(heading);
        copy.AddChild(Text(subtitle, 11)); button.TooltipText = title + "\n" + subtitle;
        return button;
    }
    private OptionButton Choice(EndgameChoice[] choices, string selected, string name, Action<string> action)
    {
        var button = new OptionButton { Name = name, FitToLongestItem = false, ClipText = true };
        foreach (var choice in choices) { int index = button.ItemCount; button.AddItem(choice.Name); button.SetItemMetadata(index, choice.Id); button.SetItemTooltip(index, choice.Description); if (choice.Id == selected) button.Select(index); }
        button.ItemSelected += index => action(button.GetItemMetadata((int)index).AsString()); _rows.AddChild(button); return button;
    }
    private Button ActionButton(string text, Action action, string name) => Button(text, action, name, _actions);
    private static Button Button(string text, Action action, string name, Node parent)
    { var button = new Button { Name = name, Text = text, CustomMinimumSize = new(0, 36), AutowrapMode = TextServer.AutowrapMode.WordSmart }; button.AddThemeFontSizeOverride("font_size", 13); button.Pressed += action; parent.AddChild(button); return button; }
}
