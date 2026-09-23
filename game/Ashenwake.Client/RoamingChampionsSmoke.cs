using System.Reflection;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Godot;
using Position = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Earned sightings, actual viewport actions, public combat inputs and exact persistence.</summary>
public partial class RoamingChampionsSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private string _output = "", _lastClick = "";
    private int _commands, _clicks;
    private bool _writeReport;
    private EndgameRuntimeSession Session => Field<EndgameRuntimeSession>(_director, "_session");
    private RoamingChampionPanel Panel => Field<RoamingChampionPanel>(_director, "_championPanel");
    private RoamingChampionDisplay Display => Field<RoamingChampionDisplay>(Panel, "_view");
    private LegendaryCollectionPanel Collection => Field<LegendaryCollectionPanel>(_director, "_collection");
    private LegendaryCollectionDisplay CollectionDisplay => Field<LegendaryCollectionDisplay>(Collection, "_view");
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--roaming-champions-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Champion smoke requires --roaming-champions-smoke --discipline=Vanguard --output=<fresh-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director); _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false);
            await Frames(); CloseMenus(); Call(_sandbox, "ResumePlaying");
            Call(_director, "OpenCollection"); await Frames();
            Check("fresh_collection_hides_champions", CollectionDisplay.Cards.All(c => c.Entry.RoamingChampionId.Length == 0)); Collection.SetOpen(false);
            OpenPanel(""); await Frames(); Check("fresh_journal_empty", Display.Entries.Length == 0);
            Check("journal_modal_pause", Panel.IsOpen && _sandbox.IsPaused); CheckLayout("empty");
            foreach (var definition in RoamingChampionCatalog.Definitions) await Explore(definition);
            Check("three_unique_champion_rewards", Session.RoamingChampions.Entries.Count(e => e.Claimed) == 3);
            Check("exact_final_replay", Replay());
            OpenPanel(RoamingChampionCatalog.Definitions[0].Id); await Frames(); await Capture("completed-champion-journal.png");
            GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(8);
            Check("completed_journal_has_three_entries", Display.Entries.Length == 3); CheckLayout("three_completed_compact"); await Capture("completed-champion-journal-compact.png");
            Panel.SetOpen(false); var journey = Field<CampaignHud>(_director, "_campaignHud"); journey.Visible = true; journey.SetOpen(true); await Frames(8);
            var tabs = Find<Button>("JourneyChampions").GetParent().GetChildren().OfType<Button>().ToArray();
            Check("journey_seven_tabs_fit_compact", tabs.Length == 7 && tabs.All(b => b.IsVisibleInTree() && GetViewport().GetVisibleRect().Encloses(b.GetGlobalRect())));
            await Capture("journey-seven-tabs-compact.png"); journey.SetOpen(false);
            foreach (var player in _sandbox.AudioDirector.MusicPlayers) { player.Stop(); player.Stream = null; }
            await Frames(); await Finish(true, "");
        }
        catch (Exception ex) { try { await Capture("failure.png"); } catch { } GD.PushError(ex.ToString()); await Finish(false, ex.Message); }
    }
    private async Task Explore(RoamingChampionDefinition definition)
    {
        CloseMenus();
        for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && !Session.RoamingChampions.Entries.Any(e => e.Id == definition.Id && e.Here); i++)
        {
            Step(new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(Session.Campaign)), false);
            if (i % 150 == 0) await Frames(1);
        }
        Refresh(); Call(_director, "ClearDeathRecap"); CloseMenus();
        var entry = Session.RoamingChampions.Entries.Single(e => e.Id == definition.Id && e.Here);
        string source = entry.SourceEncounterId;
        Check(definition.Id + "_earned_seeded_source", definition.SourceEncounterIds.Contains(source) && Session.Campaign.EncounterCleared && Session.Campaign.ActiveEncounterId == source);
        RoundTrip(definition.Id + "_source_save");
        Check(definition.Id + "_seeded_source_stable", Session.RoamingChampions.Entries.Single(e => e.Id == definition.Id).SourceEncounterId == source);
        CloseMenus(); Walk(new(0, 0), 600);
        if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus(); await Frames(); Call(_sandbox, "ResumePlaying");
        Check(definition.Id + "_world_target", _sandbox.RequestWorldInteraction(definition.Id + ".sighting"));
        for (int i = 0; i < 700 && !Panel.IsOpen; i++) { _sandbox._Process(1d / 30); if (i % 10 == 0) await Frames(1); }
        Check(definition.Id + "_mouse_sighting", Panel.IsOpen && Session.RoamingChampions.Entries.Single(e => e.Id == definition.Id) is { Discovered: true, InReach: true });
        Check(definition.Id + "_counterplay_visible", Find<Label>("ChampionCounterplay").Text == definition.Counterplay);
        CheckLayout(definition.Id + "_wide"); await Capture(definition.Id + "-inspection.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(8); CheckLayout(definition.Id + "_compact"); await Capture(definition.Id + "-inspection-compact.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames(8);
        Panel.SetOpen(false); await Capture(definition.Id + "-sighting.png"); OpenPanel(definition.Id); await Frames();
        string sourceLoot = JsonData.Hash(Session.Campaign.Combat.Capture().Loot), sourceCampaign = JsonData.Hash(Session.Campaign.Capture().Campaign);
        var sourcePosition = Session.Campaign.Combat.View.Actors.Single(a => a.Id == 1).Position;
        int copies = RewardCopies(definition.RewardItemId);
        await ClickAction(a => a.Action == "enter"); ConfirmIfVisible(true); await Frames();
        Check(definition.Id + "_safe_foyer", Session.InRoamingChampion && Session.RoamingChampions.Run?.Stage == "Foyer" && !Session.Combat.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0));
        Check(definition.Id + "_no_source_map", Session.LocalMap is null);
        Panel.SetOpen(false); await Capture(definition.Id + "-safe-foyer.png"); RoundTrip(definition.Id + "_foyer_save");
        await StartChallenge(definition, true);
        await CheckActiveSelection(definition);
        Check(definition.Id + "_custom_combat_model", ActorVisualKeys().Any(k => k.StartsWith(definition.Id + "/", StringComparison.Ordinal)));
        if (definition.Act == 2) Check("custom_poison_nests", ActorVisualKeys().Count(k => k.StartsWith("champion.nest/", StringComparison.Ordinal)) == 3);
        await Capture(definition.Id + "-combat.png"); RoundTrip(definition.Id + "_combat_save");
        if (definition.Act == 1)
        {
            CloseMenus();
            for (int i = 0; i < 7000 && Session.RoamingChampions.Run?.Stage == "Combat"; i++) Step(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)]), false);
            Refresh(); await Frames(); Check("champion_actual_death", Session.RoamingChampions.Run?.Stage == "Failed" && Field<DeathRecapHud>(_director, "_deathRecapHud").IsOpen);
            await Capture("champion-death-recovery.png"); RoundTrip("champion_failed_save");
            Check("champion_loaded_death_recovery", Field<DeathRecapHud>(_director, "_deathRecapHud").IsOpen);
            Call(_director, "DeathRecapPrimary"); await Frames();
            Check("champion_death_returns_source", !Session.InRoamingChampion && Session.Campaign.ActiveEncounterId == source && JsonData.Hash(Session.Campaign.Combat.Capture().Loot) == sourceLoot && Session.Campaign.Combat.View.Actors.Single(a => a.Id == 1).Position == sourcePosition);
            Walk(RoamingChampionCatalog.SightingPosition, RoamingChampionCatalog.InteractionRange); Call(_director, "Interact", definition.Id + ".sighting"); await Frames();
            sourcePosition = Session.Campaign.Combat.View.Actors.Single(a => a.Id == 1).Position;
            await ClickAction(a => a.Action == "enter"); ConfirmIfVisible(true); await Frames(); Check("champion_retry_safe", Session.RoamingChampions.Run?.Stage == "Foyer");
            await StartChallenge(definition, false);
        }
        CloseMenus(); bool ventCaptured = false;
        for (int i = 0; i < 12000 && Session.RoamingChampions.Run?.Stage == "Combat"; i++)
        {
            Step(RoamingChampionSmoke.Next(Session, definition.Id), false);
            if (definition.Act == 3)
            {
                var primary = Session.Combat.View.Actors.FirstOrDefault(a => a.DefinitionId == definition.PrimaryEnemyId && a.Health > 0);
                if (primary is { BossRecoveryTicks: > 0 } && !ventCaptured)
                { Refresh(); await Frames(); await Capture("champion.tithekeeper-vent-recovery.png"); ventCaptured = true; }
            }
            if (i % 100 == 0) await Frames(1);
        }
        Refresh(); await Frames(); Check(definition.Id + "_ordinary_combat_victory", Session.RoamingChampions.Run?.Stage == "Victory");
        Check(definition.Id + "_no_early_reward", RewardCopies(definition.RewardItemId) == copies);
        if (definition.Act == 3) Check("observed_tithekeeper_vent_recovery", ventCaptured);
        RoundTrip(definition.Id + "_victory_save");
        // Leaving after victory must preserve the pending reward without another fight.
        OpenPanel(definition.Id); await Frames(); await ClickAction(a => a.Action == "exit"); await Frames();
        Check(definition.Id + "_unclaimed_source_preserved", !Session.InRoamingChampion && JsonData.Hash(Session.Campaign.Combat.Capture().Loot) == sourceLoot && JsonData.Hash(Session.Campaign.Capture().Campaign) == sourceCampaign);
        Check(definition.Id + "_unclaimed_victory_persists", Session.RoamingChampions.Entries.Single(e => e.Id == definition.Id) is { Defeated: true, Claimed: false });
        RoundTrip(definition.Id + "_unclaimed_exit_save");
        Call(_director, "Interact", definition.Id + ".sighting"); await Frames(); await ClickAction(a => a.Action == "enter"); await Frames();
        Check(definition.Id + "_reentry_preserves_victory", Session.RoamingChampions.Run?.Stage == "Victory");
        Walk(RoamingChampionCatalog.TreasurePosition, RoamingChampionCatalog.InteractionRange); Call(_director, "Interact", definition.Id + ".treasure"); await Frames();
        await Capture(definition.Id + "-reward.png"); await ClickAction(a => a.Action == "claim"); await Frames();
        Check(definition.Id + "_signature_once", Session.RoamingChampions.Run?.Stage == "Claimed" && RewardCopies(definition.RewardItemId) == copies + 1);
        string hash = Session.StateHash; Check(definition.Id + "_duplicate_reward_rejected", !Session.ClaimRoamingChampionReward().Success && Session.StateHash == hash);
        string item = JsonData.Write(Session.Capture().Campaign.Production.Progression.Character.Items.Single(i => i.DefinitionId == definition.RewardItemId));
        RoundTrip(definition.Id + "_claimed_save");
        Check(definition.Id + "_reward_metadata_preserved", JsonData.Write(Session.Capture().Campaign.Production.Progression.Character.Items.Single(i => i.DefinitionId == definition.RewardItemId)) == item);
        Call(_director, "OpenCollection"); Collection.SelectItem(definition.RewardItemId); await Frames();
        Check(definition.Id + "_collection_owned", CollectionDisplay.Cards.Single(c => c.Entry.ItemId == definition.RewardItemId) is { Collected: true, Owned: 1 });
        Check(definition.Id + "_collection_only_discovered", CollectionDisplay.Cards.Count(c => c.Entry.RoamingChampionId.Length > 0) == definition.Act);
        Check(definition.Id + "_equipment_preview", Descendants(Collection).OfType<CharacterPreview>().Single().AppearanceKey.Contains(definition.RewardItemId, StringComparison.Ordinal));
        await Capture(definition.Id + "-signature-equipment.png"); await Click("CollectionSourceRoamingChampion");
        Check(definition.Id + "_collection_route", Panel.IsOpen && !Collection.IsOpen && Display.SelectedId == definition.Id);
        await ClickAction(a => a.Action == "exit"); await Frames();
        Check(definition.Id + "_exact_source", !Session.InRoamingChampion && Session.Campaign.ActiveEncounterId == source);
        Check(definition.Id + "_source_loot", JsonData.Hash(Session.Campaign.Combat.Capture().Loot) == sourceLoot);
        Check(definition.Id + "_source_progress", JsonData.Hash(Session.Campaign.Capture().Campaign) == sourceCampaign);
        Check(definition.Id + "_source_position", Session.Campaign.Combat.View.Actors.Single(a => a.Id == 1).Position == sourcePosition);
        RoundTrip(definition.Id + "_exit_save"); CloseMenus();
    }
    private async Task CheckActiveSelection(RoamingChampionDefinition definition)
    {
        string other = RoamingChampionCatalog.Definitions[0].Id;
        string hash = Session.StateHash;
        OpenPanel(other); await Frames();
        Check(definition.Id + "_active_journal_consistent", Display.SelectedId == definition.Id && Display.LockedSelectionId == definition.Id &&
            Display.Heading == definition.Name && Find<Label>("ChampionCounterplay").Text == definition.Counterplay &&
            Display.Entries.Single(e => e.Id == Display.SelectedId).Reward == EquipmentNames.For(definition.RewardItemId));
        var labels = Descendants(Panel).OfType<Label>().Select(l => l.Text).ToArray();
        Check(definition.Id + "_journal_copy_once", labels.Count(text => text.Contains(definition.Counterplay, StringComparison.Ordinal)) == 1 &&
            labels.Count(text => text.Contains(definition.Description, StringComparison.Ordinal)) == 1);
        if (definition.Act > 1)
        {
            Check(definition.Id + "_other_entries_disabled", Display.Entries.Where(e => e.Id != definition.Id).All(e => Find<Button>("ChampionEntry_" + SafeName(e.Id)).Disabled));
            Panel.SelectEntry(other); await Frames();
            Check(definition.Id + "_stale_selection_ignored", Display.SelectedId == definition.Id && Session.StateHash == hash);
        }
        Panel.SetOpen(false);
    }
    private int RewardCopies(string id) => Session.Capture().Campaign.Production.Progression.Character.Items.Count(i => i.DefinitionId == id);
    private async Task StartChallenge(RoamingChampionDefinition definition, bool testCancel)
    {
        Walk(RoamingChampionCatalog.ChallengePosition, RoamingChampionCatalog.InteractionRange);
        Call(_director, "Interact", definition.Id + ".challenge"); await Frames(); await ClickAction(a => a.Action == "challenge");
        Check(definition.Id + "_challenge_confirmation", Find<ConfirmationDialog>("ChampionConfirmation").Visible);
        if (testCancel)
        {
            string hash = Session.StateHash; ConfirmIfVisible(false); await Frames();
            Check(definition.Id + "_challenge_cancel_safe", Session.RoamingChampions.Run?.Stage == "Foyer" && Session.StateHash == hash);
            await ClickAction(a => a.Action == "challenge");
        }
        ConfirmIfVisible(true); await Frames(); Check(definition.Id + "_explicit_challenge", Session.RoamingChampions.Run?.Stage == "Combat"); Panel.SetOpen(false);
    }
    private void OpenPanel(string id) => Call(_director, "OpenRoamingChampions", id);
    private void Refresh() { _sandbox.AdoptSession(Session.Combat); Call(_director, "Refresh"); }
    private void CloseMenus()
    {
        Panel.SetOpen(false); Collection.SetOpen(false); Field<CampaignHud>(_director, "_campaignHud").SetOpen(false);
        Field<ProductionHud>(_director, "_character").Close(); Field<EndgameHud>(_director, "_board").SetOpen(false);
    }
    private void Step(EndgameRuntimeCommand command, bool refresh = true)
    {
        var result = Session.Execute(command); _commands++;
        if (!result.Success) throw new InvalidDataException(result.Reason + " / " + command.Action);
        Call(_director, "Observe", result); if (refresh) Refresh();
    }
    private void Walk(Position target, int range)
    {
        CloseMenus();
        for (int i = 0; i < 600; i++)
        {
            var player = Session.Combat.View.Actors.Single(a => a.Id == 1);
            if (Position.DistanceSquared(player.Position, target) <= (long)(range - 100) * (range - 100))
            { Step(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)])); return; }
            var direction = CombatProductionSmoke.MovementDirection(player.Position, target, Session.Room);
            Step(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]));
        }
        throw new InvalidDataException("Could not approach champion interaction.");
    }
    private bool Replay() => EndgameRuntimeReplayRunner.Run(Field<string>(_director, "_combatJson"), Field<AdventureContent>(_director, "_adventure"), Field<ProgressionContent>(_director, "_progression"), Field<CampaignContent>(_director, "_campaign"), Field<EndgameContent>(_director, "_endgame"), Session.CaptureReplay()).Success;
    private void RoundTrip(string name) { string hash = Session.StateHash; Check(name + "_replay", Replay()); Call(_director, "Save"); Call(_director, "Load"); Check(name, Session.StateHash == hash); }
    private async Task ClickAction(Func<RoamingChampionActionDisplay, bool> predicate)
    {
        await Frames();
        var action = Display.Actions.Single(predicate);
        await Click("ChampionAction_" + SafeName(action.Action) + "_" + SafeName(action.Id));
    }
    private async Task Click(string name)
    {
        await Frames(); var button = Find<Button>(name);
        for (Node? parent = button.GetParent(); parent is not null; parent = parent.GetParent())
            if (parent is ScrollContainer scroll) scroll.EnsureControlVisible(button);
        await Frames(); Check("clickable_" + name + "_" + _clicks, button.IsVisibleInTree() && !button.Disabled && GetViewport().GetVisibleRect().HasPoint(button.GetGlobalRect().GetCenter()));
        var point = button.GetGlobalRect().GetCenter();
        _lastClick = name + " button=" + button.GetGlobalRect() + " center=" + point;
        bool received = false; void Receipt() => received = true;
        button.Pressed += Receipt;
        GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { Position = point, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
        _clicks++; await Frames();
        if (GodotObject.IsInstanceValid(button)) button.Pressed -= Receipt;
        Check("click_received_" + name + "_" + _clicks, received);
    }
    private void ConfirmIfVisible(bool accept)
    {
        var dialog = Find<ConfirmationDialog>("ChampionConfirmation"); if (!dialog.Visible) return;
        dialog.Hide(); dialog.EmitSignal(accept ? ConfirmationDialog.SignalName.Confirmed : ConfirmationDialog.SignalName.Canceled);
    }
    private void CheckLayout(string suffix)
    {
        Check("panel_visible_" + suffix, Panel.IsOpen && Panel.IsVisibleInTree());
        var viewport = GetViewport().GetVisibleRect();
        foreach (string name in new[] { "RoamingChampionPanel", "ChampionJournalEntries", "ChampionDetailsScroll", "ChampionClose" })
        {
            var control = Find<Control>(name);
            if (control.IsVisibleInTree()) Check(name + "_fits_" + suffix, viewport.Encloses(control.GetGlobalRect()));
        }
    }
    private async Task Frames(int count = 4) { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); _sandbox?._Process(0); } }
    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless" || !OS.GetCmdlineUserArgs().Contains("--capture-roaming-champions")) return;
        GetWindow().GrabFocus(); await Frames(); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage(); Check("capture_" + name, image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Roaming champion check failed: " + name); }
    private T Find<T>(string name) where T : Node => Descendants(_sandbox).OfType<T>().Single(n => n.Name == name);
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static object? Call(object instance, string name, params object?[] args)
    { try { return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
    private static string SafeName(string value) => new(value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    private string[] ActorVisualKeys() => ((System.Collections.IDictionary)Field<object>(_sandbox, "_actors")).Values.Cast<object>()
        .Select(a => (string)a.GetType().GetProperty("VisualKey")!.GetValue(a)!).ToArray();
    private async Task Finish(bool passed, string error)
    {
        var report = new
        {
            kind = "RoamingChampionsSmoke",
            passed,
            checks = _checks,
            captures = _captures,
            clicks = _clicks,
            commands = _commands,
            error,
            run = _director is null ? null : Session.RoamingChampions.Run,
            lastClick = _lastClick,
            notice = _director is null ? "" : Field<string>(_director, "_championNotice"),
            actions = _director is null ? null : Field<RoamingChampionDisplay?>(Panel, "_view")?.Actions,
            scope = "Earned seeded campaign sources, real mouse approach, explicit safe-foyer challenge/cancel, ordinary combat, death recovery, persistent unclaimed victory, exactly-once signature rewards, exact source return, save/load/replay, collection routes and1280/780 layouts."
        };
        if (_writeReport) File.WriteAllText(Path.Combine(_output, "roaming-champions-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report));
        // Release the diagnostic's many temporary scene generations while the native renderer is alive.
        if (_director is not null && GodotObject.IsInstanceValid(_director)) _director.QueueFree();
        _director = null!; _sandbox = null!;
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GC.Collect();
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Quit(passed ? 0 : 1);
    }
}
