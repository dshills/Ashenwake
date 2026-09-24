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

/// <summary>Earned campaign rooms, explicit puzzle answers, ordinary combat, and real viewport menu clicks.</summary>
public partial class SecretChambersSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private string _output = "";
    private int _commands, _clicks;
    private bool _writeReport;
    private string _lastClick = "";
    private EndgameRuntimeSession Session => Field<EndgameRuntimeSession>(_director, "_session");
    private SecretChamberPanel Panel => Field<SecretChamberPanel>(_director, "_secretPanel");
    private SecretChamberDisplay Display => Field<SecretChamberDisplay>(Panel, "_view");
    private LegendaryCollectionPanel Collection => Field<LegendaryCollectionPanel>(_director, "_collection");
    private LegendaryCollectionDisplay CollectionDisplay => Field<LegendaryCollectionDisplay>(Collection, "_view");
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--secret-chambers-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Secret chamber smoke requires --secret-chambers-smoke --discipline=Vanguard --output=<fresh-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director);
            _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false);
            await Frames(); CloseMenus(); Call(_sandbox, "ResumePlaying");
            Call(_director, "OpenCollection"); await Frames();
            Check("fresh_collection_hides_three_secrets", CollectionDisplay.Cards.Length == 9 && CollectionDisplay.Cards.All(c => c.Entry.SecretChamberId.Length == 0));
            Collection.SetOpen(false);
            OpenPanel(""); await Frames();
            Check("fresh_journal_empty", Session.SecretChambers.Entries.Length == 0 && Display.Entries.Length == 0);
            Check("journal_modal_pause", Panel.IsOpen && _sandbox.IsPaused);
            CheckLayout("empty"); await Capture("empty-mystery-journal.png");
            Panel.SetOpen(false);
            foreach (var definition in SecretChamberCatalog.Definitions) await Explore(definition);
            Check("three_unique_treasures", Session.SecretChambers.Entries.Count(e => e.Claimed) == 3);
            Check("exact_final_replay", Replay());
            OpenPanel(SecretChamberCatalog.Definitions[0].Id); await Frames(); await Capture("completed-mystery-journal.png");
            foreach (var player in _sandbox.AudioDirector.MusicPlayers) { player.Stop(); player.Stream = null; }
            await Frames(); Finish(true, "");
        }
        catch (Exception ex) { try { await Capture("failure.png"); } catch { } GD.PushError(ex.ToString()); Finish(false, ex.Message); }
    }
    private async Task Explore(SecretChamberDefinition definition)
    {
        int treasureSoundsBefore = _director.SecretTreasureAudioCount;
        CloseMenus();
        for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands &&
            !(Session.Campaign.ActiveEncounterId == definition.SourceEncounterId && Session.Campaign.EncounterCleared && Session.Campaign.Capture().Campaign.Exploration is null); i++)
        {
            Step(new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(Session.Campaign)), false);
            if (i % 200 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        Refresh(); Call(_director, "ClearDeathRecap"); CloseMenus();
        Check(definition.Id + "_earned_source", Session.Campaign.ActiveEncounterId == definition.SourceEncounterId && Session.Campaign.EncounterCleared);
        Check(definition.Id + "_not_revealed", !Session.SecretChambers.Entries.Any(e => e.Id == definition.Id && (e.Revealed || e.Claimed)));
        Walk(new(0, 0), 600);
        if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus(); await Frames(); Call(_sandbox, "ResumePlaying");
        // Initiate the same cancellable world-approach driver used by clicking the clue;
        // a distant, uninspected clue is intentionally absent from the discoveries journal.
        Check(definition.Id + "_clue_world_target", _sandbox.RequestWorldInteraction(definition.Clues[0].Id));
        for (int i = 0; i < 500 && !Panel.IsOpen; i++) { _sandbox._Process(1d / 30); if (i % 10 == 0) await Frames(1); }
        Check(definition.Id + "_mouse_inspection", Panel.IsOpen && Session.SecretChambers.Entries.Single(e => e.Id == definition.Id).Clue?.InReach == true);
        Check(definition.Id + "_name_hidden", !Display.Entries.Any(e => e.Name == definition.Name) && !Display.Narrative.Contains(definition.Name, StringComparison.Ordinal));
        Check(definition.Id + "_reward_hidden", Display.Entries.Single(e => e.Id == definition.Id).Reward.Length == 0);
        CheckLayout(definition.Id + "_wide"); await Capture(definition.Id + "-clue.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(960, 720); await Frames(8); CheckLayout(definition.Id + "_compact"); await Capture(definition.Id + "-clue-compact.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames(8);
        var first = definition.Clues[0]; string beforeWrong = Session.StateHash;
        await ClickAction(a => a.Label == first.Choices.Single(c => c != first.Solution));
        Check(definition.Id + "_wrong_answer_nonmutating", Session.StateHash == beforeWrong && Session.SecretChambers.Entries.Single(e => e.Id == definition.Id).PuzzleStep == 0);
        for (int i = 0; i < definition.Clues.Length; i++)
        {
            var clue = definition.Clues[i];
            Walk(clue.Position, SecretChamberCatalog.InteractionRange);
            Call(_director, "Interact", clue.Id); await Frames();
            Check(definition.Id + "_clue_" + i + "_hint", Display.Narrative.Contains(clue.Hint, StringComparison.Ordinal));
            await ClickAction(a => a.Label == clue.Solution);
            Check(definition.Id + "_clue_" + i + "_progress", Session.SecretChambers.Entries.Single(e => e.Id == definition.Id).PuzzleStep == i + 1);
        }
        Check(definition.Id + "_revealed", Session.SecretChambers.Entries.Single(e => e.Id == definition.Id).Revealed);
        Check(definition.Id + "_clues_do_not_spoil_treasure_audio", _director.SecretTreasureAudioCount == treasureSoundsBefore);
        RoundTrip(definition.Id + "_puzzle_save");
        Walk(definition.EntrancePosition, SecretChamberCatalog.InteractionRange);
        Call(_director, "Interact", definition.Id + ".enter"); await Frames();
        string sourceLoot = JsonData.Hash(Session.Campaign.Combat.Capture().Loot);
        string sourceCampaign = JsonData.Hash(Session.Campaign.Capture().Campaign);
        var sourcePosition = Session.Campaign.Combat.View.Actors.Single(a => a.Id == 1).Position;
        int beforeReward = Session.Capture().Campaign.Production.Progression.Character.Items.Count(i => i.DefinitionId == definition.RewardItemId);
        await ClickAction(a => a.Action == "enter"); ConfirmIfVisible(true); await Frames();
        Check(definition.Id + "_safe_foyer", Session.InSecretChamber && Session.SecretChambers.Run?.Stage == "Foyer" && !Session.Combat.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0));
        Check(definition.Id + "_no_source_map", Session.LocalMap is null);
        Panel.SetOpen(false); await Capture(definition.Id + "-safe-foyer.png");
        for (int i = 0; i < 30; i++) Step(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)]), false);
        Check(definition.Id + "_foyer_stays_safe", Session.SecretChambers.Run?.Stage == "Foyer");
        RoundTrip(definition.Id + "_foyer_save");
        Walk(SecretChamberCatalog.ChallengePosition, SecretChamberCatalog.InteractionRange);
        Call(_director, "Interact", definition.Id + ".challenge"); await Frames();
        await ClickAction(a => a.Action == "challenge");
        Check(definition.Id + "_challenge_confirmation", Find<ConfirmationDialog>("SecretConfirmation").Visible);
        ConfirmIfVisible(false); Check(definition.Id + "_cancel_challenge_safe", Session.SecretChambers.Run?.Stage == "Foyer");
        await ClickAction(a => a.Action == "challenge"); ConfirmIfVisible(true); await Frames();
        Check(definition.Id + "_guardian_started", Session.SecretChambers.Run?.Stage == "Combat");
        Panel.SetOpen(false); await Capture(definition.Id + "-guardian.png"); RoundTrip(definition.Id + "_combat_save");
        if (definition.Act == 1)
        {
            CloseMenus();
            for (int i = 0; i < 7000 && Session.SecretChambers.Run?.Stage != "Failed"; i++)
                Step(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)]), false);
            Refresh(); await Frames();
            Check("actual_secret_guardian_death", Session.SecretChambers.Run?.Stage == "Failed" && Field<DeathRecapHud>(_director, "_deathRecapHud").IsOpen);
            await Capture("secret-guardian-death.png"); RoundTrip("secret_failed_save");
            Check("restored_secret_death_recovery", Field<DeathRecapHud>(_director, "_deathRecapHud").IsOpen);
            Call(_director, "DeathRecapPrimary"); await Frames();
            Check("secret_death_returns_to_source", !Session.InSecretChamber && Session.Campaign.ActiveEncounterId == definition.SourceEncounterId &&
                JsonData.Hash(Session.Campaign.Combat.Capture().Loot) == sourceLoot);
            Walk(definition.EntrancePosition, SecretChamberCatalog.InteractionRange);
            Call(_director, "Interact", definition.Id + ".enter"); await Frames(); await ClickAction(a => a.Action == "enter"); ConfirmIfVisible(true); await Frames();
            Check("secret_retry_safe_foyer", Session.SecretChambers.Run?.Stage == "Foyer");
            Walk(SecretChamberCatalog.ChallengePosition, SecretChamberCatalog.InteractionRange);
            Call(_director, "Interact", definition.Id + ".challenge"); await Frames();
            await ClickAction(a => a.Action == "challenge"); ConfirmIfVisible(true); await Frames();
            Check("secret_retry_explicit_challenge", Session.SecretChambers.Run?.Stage == "Combat");
        }
        CloseMenus();
        for (int i = 0; i < 10000 && Session.SecretChambers.Run?.Stage == "Combat"; i++)
        {
            Step(new(EndgameRuntimeAction.Tick, Commands: EndgameCombatSmoke.Commands(Session.Combat.View, Session.Room)), false);
            if (i % 100 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        Refresh(); await Frames();
        Check(definition.Id + "_actual_guardian_victory", Session.SecretChambers.Run?.Stage == "Victory");
        Check(definition.Id + "_not_paid_early", Session.Capture().Campaign.Production.Progression.Character.Items.Count(i => i.DefinitionId == definition.RewardItemId) == beforeReward);
        RoundTrip(definition.Id + "_victory_save");
        Walk(SecretChamberCatalog.TreasurePosition, SecretChamberCatalog.InteractionRange);
        Call(_director, "Interact", definition.Id + ".treasure"); await Frames(); await Capture(definition.Id + "-treasure.png");
        Check(definition.Id + "_unclaimed_treasure_stays_quiet", _director.SecretTreasureAudioCount == treasureSoundsBefore);
        await ClickAction(a => a.Action == "claim"); ConfirmIfVisible(true); await Frames();
        Check(definition.Id + "_earned_treasure_audio_once", _director.SecretTreasureAudioCount == treasureSoundsBefore + 1);
        var earnedClaim = new EndgameRuntimeResult(true, "", [], Session.WorldEvents.ToArray());
        Check(definition.Id + "_claim_event_is_authoritative", earnedClaim.WorldEvents.Contains("SecretTreasureClaimed:" + definition.Id));
        Call(_director, "Observe", earnedClaim);
        Check(definition.Id + "_duplicate_claim_presentation_quiet", _director.SecretTreasureAudioCount == treasureSoundsBefore + 1);
        Check(definition.Id + "_treasure_once", Session.SecretChambers.Run?.Stage == "Claimed" && Session.Capture().Campaign.Production.Progression.Character.Items.Count(i => i.DefinitionId == definition.RewardItemId) == beforeReward + 1);
        string hash = Session.StateHash; Check(definition.Id + "_duplicate_claim_rejected", !Session.ClaimSecretTreasure().Success && Session.StateHash == hash);
        RoundTrip(definition.Id + "_claimed_save");
        Call(_director, "Observe", earnedClaim);
        Check(definition.Id + "_claimed_load_and_refresh_quiet", _director.SecretTreasureAudioCount == treasureSoundsBefore + 1);
        string previewHash = Session.StateHash;
        Call(_director, "OpenCollection"); Collection.SelectItem(definition.RewardItemId); await Frames();
        Check(definition.Id + "_collection_tracks_discoveries", CollectionDisplay.Cards.Count(c => c.Entry.SecretChamberId.Length > 0) == definition.Act);
        Check(definition.Id + "_collection_owned_reward", CollectionDisplay.Cards.Single(c => c.Entry.ItemId == definition.RewardItemId) is { Collected: true, Owned: > 0 });
        Check(definition.Id + "_reward_preview", Descendants(Collection).OfType<CharacterPreview>().Single().AppearanceKey.Contains(definition.RewardItemId, StringComparison.Ordinal));
        Check(definition.Id + "_preview_is_cosmetic", Session.StateHash == previewHash);
        await Capture(definition.Id + "-claimed-equipment.png"); Collection.SetOpen(false);
        OpenPanel(definition.Id); await Frames(); await ClickAction(a => a.Action == "exit"); ConfirmIfVisible(true); await Frames();
        Check(definition.Id + "_exact_source_room", !Session.InSecretChamber && Session.Campaign.ActiveEncounterId == definition.SourceEncounterId);
        Check(definition.Id + "_source_progress_retained", JsonData.Hash(Session.Campaign.Capture().Campaign) == sourceCampaign);
        Check(definition.Id + "_source_loot_retained", JsonData.Hash(Session.Campaign.Combat.Capture().Loot) == sourceLoot);
        Check(definition.Id + "_source_position_retained", Session.Campaign.Combat.View.Actors.Single(a => a.Id == 1).Position == sourcePosition);
        RoundTrip(definition.Id + "_exit_save");
        CloseMenus();
    }
    private void OpenPanel(string id) => Call(_director, "OpenSecretChambers", id);
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
        throw new InvalidDataException("Could not approach secret chamber interaction.");
    }
    private bool Replay() => EndgameRuntimeReplayRunner.Run(Field<string>(_director, "_combatJson"), Field<AdventureContent>(_director, "_adventure"), Field<ProgressionContent>(_director, "_progression"), Field<CampaignContent>(_director, "_campaign"), Field<EndgameContent>(_director, "_endgame"), Session.CaptureReplay()).Success;
    private void RoundTrip(string name) { string hash = Session.StateHash; Check(name + "_replay", Replay()); Call(_director, "Save"); Call(_director, "Load"); Check(name, Session.StateHash == hash); }
    private async Task ClickAction(Func<SecretChamberActionDisplay, bool> predicate)
    {
        await Frames();
        var action = Display.Actions.Single(predicate);
        await Click("SecretAction_" + SafeName(action.Action) + "_" + SafeName(action.Id));
    }
    private async Task Click(string name)
    {
        await Frames(); var button = Find<Button>(name);
        if (button.GetParent() is Control) Find<ScrollContainer>("SecretDetailsScroll").EnsureControlVisible(button);
        await Frames(); Check("clickable_" + name + "_" + _clicks, button.IsVisibleInTree() && !button.Disabled && GetViewport().GetVisibleRect().HasPoint(button.GetGlobalRect().GetCenter()));
        var point = button.GetGlobalRect().GetCenter();
        _lastClick = name + " button=" + button.GetGlobalRect() + " scroll=" + Find<ScrollContainer>("SecretDetailsScroll").GetGlobalRect() + " center=" + point;
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
        var dialog = Find<ConfirmationDialog>("SecretConfirmation"); if (!dialog.Visible) return;
        dialog.Hide(); dialog.EmitSignal(accept ? ConfirmationDialog.SignalName.Confirmed : ConfirmationDialog.SignalName.Canceled);
    }
    private void CheckLayout(string suffix)
    {
        Check("panel_visible_" + suffix, Panel.IsOpen && Panel.IsVisibleInTree());
        var viewport = GetViewport().GetVisibleRect();
        foreach (string name in new[] { "SecretChamberPanel", "SecretJournalEntries", "SecretDetailsScroll", "SecretClose" })
        {
            var control = Find<Control>(name);
            if (control.IsVisibleInTree()) Check(name + "_fits_" + suffix, viewport.Encloses(control.GetGlobalRect()));
        }
    }
    private async Task Frames(int count = 4) { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); _sandbox?._Process(0); } }
    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless" || !OS.GetCmdlineUserArgs().Contains("--capture-secret-chambers")) return;
        GetWindow().GrabFocus(); await Frames(); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage(); Check("capture_" + name, image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Secret chamber check failed: " + name); }
    private T Find<T>(string name) where T : Node => Descendants(_sandbox).OfType<T>().Single(n => n.Name == name);
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static object? Call(object instance, string name, params object?[] args)
    { try { return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
    private static string SafeName(string value) => new(value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = "SecretChambersSmoke",
            passed,
            checks = _checks,
            captures = _captures,
            clicks = _clicks,
            commands = _commands,
            error,
            currentChamber = _director is null ? null : Session.SecretChambers.Run,
            notice = _director is null ? "" : Field<string>(_director, "_secretNotice"),
            lastClick = _lastClick,
            currentActions = _director is null ? null : Field<SecretChamberDisplay?>(Panel, "_view")?.Actions,
            currentRewardCopies = _director is null || Session.CurrentSecretChamber is not { } current ? -1 : Session.Capture().Campaign.Production.Progression.Character.Items.Count(i => i.DefinitionId == current.RewardItemId),
            scope = "Earns all three campaign source rooms through public commands; inspects clues using mouse approach; tests wrong and correct choices, explicit safe-foyer challenges, ordinary guardian combat, one-time treasure, actual guardian death and recovery, exact campaign-room return, save/load/replay and 1280/960 layouts. Native confirmation decisions use explicit public signals."
        };
        if (_writeReport) File.WriteAllText(Path.Combine(_output, "secret-chambers-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
