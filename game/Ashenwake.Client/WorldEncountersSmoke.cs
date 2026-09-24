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

/// <summary>Earned campaign sources, viewport actions, optional outcomes and persistence.</summary>
public partial class WorldEncountersSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private string _output = "", _lastClick = "";
    private int _commands, _clicks;
    private bool _writeReport;
    private EndgameRuntimeSession Session => Field<EndgameRuntimeSession>(_director, "_session");
    private WorldEncounterPanel Panel => Field<WorldEncounterPanel>(_director, "_worldEncounterPanel");
    private WorldEncounterDisplay Display => Field<WorldEncounterDisplay>(Panel, "_view");
    private WorldEventPresentation Presentation => Field<WorldEventPresentation>(_director, "_worldEncounterPresentation");
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--world-encounters-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("World smoke requires --world-encounters-smoke --discipline=Vanguard --output=<fresh-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director); _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false);
            await Frames(); CloseMenus(); Call(_sandbox, "ResumePlaying");
            OpenPanel(""); await Frames(); Check("fresh_journal_empty", Display.Entries.Length == 0);
            Check("journal_modal_pause", Panel.IsOpen && _sandbox.IsPaused); CheckLayout("empty");
            foreach (string id in new[] { "event.lantern", "storm.grey_march", "event.caravan", "event.shrine", "storm.verdant", "storm.cinder", "storm.spine", "storm.hollow" })
                await Explore(WorldEncounterCatalog.Find(id)!);
            Check("eight_encounters_claimed", Session.WorldEncounters.Entries.Count(e => e.Claimed) == 8);
            Check("final_replay", Replay());
            OpenPanel("storm.hollow"); await Frames(); await Capture("completed-encounters.png");
            GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(8); CheckLayout("all_compact");
            Check("eight_journal_entries", Display.Entries.Length == 8); await Capture("completed-encounters-compact.png");
            foreach (var player in _sandbox.AudioDirector.MusicPlayers) { player.Stop(); player.Stream = null; }
            await Finish(true, "");
        }
        catch (Exception ex) { try { await Capture("failure.png"); } catch { } GD.PushError(ex.ToString()); await Finish(false, ex.Message); }
    }
    private async Task Explore(WorldEncounterDefinition d)
    {
        CloseMenus();
        for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && !Session.WorldEncounters.Entries.Any(e => e.Id == d.Id && e.Here); i++)
        {
            Step(new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(Session.Campaign)), false);
            if (i % 150 == 0) await Frames(1);
        }
        Refresh(); Call(_director, "ClearDeathRecap"); CloseMenus();
        Check(d.Id + "_earned_source", Session.Campaign.ActiveEncounterId == d.SourceEncounterId && Session.Campaign.EncounterCleared);
        if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus(); await Frames(); Call(_sandbox, "ResumePlaying");
        Check(d.Id + "_world_target", _sandbox.RequestWorldInteraction(d.Id + ".enter"));
        for (int i = 0; i < 700 && !Panel.IsOpen; i++) { _sandbox._Process(1d / 30); if (i % 10 == 0) await Frames(1); }
        Check(d.Id + "_mouse_approach_inspection", Panel.IsOpen && Session.WorldEncounters.Entries.Single(e => e.Id == d.Id).CanEnter);
        CheckLayout(d.Id + "_wide"); await Capture(d.Id + "-inspection.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(8); CheckLayout(d.Id + "_compact"); await Capture(d.Id + "-inspection-compact.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames(8);
        string sourceLoot = JsonData.Hash(Session.Campaign.Combat.Capture().Loot), sourceCampaign = JsonData.Hash(Session.Campaign.Capture().Campaign);
        var sourcePosition = Session.Campaign.Combat.View.Actors.Single(a => a.Id == 1).Position;
        int copies = RewardCopies(d.RewardItemId);
        await ClickAction(a => a.Action == "enter"); Confirm(true); await Frames();
        Check(d.Id + "_safe_foyer", Session.InWorldEncounter && Session.WorldEncounters.Run?.Stage == "Foyer" && !Session.Combat.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0));
        Check(d.Id + "_no_source_map", Session.LocalMap is null); Panel.SetOpen(false); await Capture(d.Id + "-foyer.png");
        RoundTrip(d.Id + "_foyer_save"); Walk(WorldEncounterCatalog.ChoicePosition); OpenPanel(d.Id); await Frames();
        if (d.Id == "event.caravan")
        {
            CheckCaravanCombatBranch(Session.Capture());
            foreach (string choice in new[] { "extinguish", "return", "speak" })
            { await ClickAction(a => a.Action == "choose" && a.Id == choice); await Frames(); }
            Check("caravan_puzzle_outcome", Session.WorldEncounters.Run is { Stage: "Victory", Outcome: "Puzzle", PuzzleStep: 3 });
        }
        else
        {
            await ClickAction(a => a.Action == "choose");
            Check(d.Id + "_fight_confirmation", Find<ConfirmationDialog>("WorldEncounterConfirmation").Visible);
            string before = Session.StateHash; Confirm(false); await Frames(); Check(d.Id + "_cancel_no_mutation", Session.StateHash == before);
            await ClickAction(a => a.Action == "choose"); Confirm(true); await Frames(); Check(d.Id + "_combat_started", Session.WorldEncounters.Run?.Stage == "Combat");
            Panel.SetOpen(false); await Frames(); Check(d.Id + "_scenery_present", Presentation.PresentedEvent == d.Id);
            Check(d.Id + "_tutorial_does_not_obscure_combat", !Field<OpeningGuidancePanel>(_director, "_openingGuide").IsHintVisible);
            if (d.Id == "storm.grey_march") await CheckCosmeticSettings();
            await Capture(d.Id + "-combat.png"); RoundTrip(d.Id + "_combat_save");
            if (d.Id == "event.lantern")
            {
                for (int i = 0; i < 7000 && Session.WorldEncounters.Run?.Stage == "Combat"; i++) Step(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)]), false);
                Refresh(); await Frames(); Check("actual_encounter_death", Session.WorldEncounters.Run?.Stage == "Failed" && Field<DeathRecapHud>(_director, "_deathRecapHud").IsOpen);
                await Capture("encounter-death.png"); RoundTrip("failed_save"); Call(_director, "DeathRecapPrimary"); await Frames();
                Check("death_returns_source", !Session.InWorldEncounter && Session.Campaign.ActiveEncounterId == d.SourceEncounterId);
                CloseMenus(); OpenPanel(d.Id); await Frames(); await ClickAction(a => a.Action == "enter"); Confirm(true); await Frames();
                Walk(WorldEncounterCatalog.ChoicePosition); OpenPanel(d.Id); await Frames(); await ClickAction(a => a.Action == "choose"); Confirm(true); await Frames();
            }
            CloseMenus();
            for (int i = 0; i < 14000 && Session.WorldEncounters.Run?.Stage == "Combat"; i++)
            { Step(WorldEncounterSmoke.Next(Session, d.Id), false); if (i % 150 == 0) await Frames(1); }
            Refresh(); await Frames(); Check(d.Id + "_ordinary_combat_victory", Session.WorldEncounters.Run?.Stage == "Victory");
        }
        Check(d.Id + "_no_early_reward", RewardCopies(d.RewardItemId) == copies);
        RoundTrip(d.Id + "_victory_save"); OpenPanel(d.Id); await Frames(); await ClickAction(a => a.Action == "exit"); await Frames();
        Check(d.Id + "_unclaimed_persists", !Session.InWorldEncounter && Session.WorldEncounters.Entries.Single(e => e.Id == d.Id) is { Completed: true, Claimed: false });
        OpenPanel(d.Id); await Frames(); await ClickAction(a => a.Action == "enter"); Confirm(true); await Frames();
        Check(d.Id + "_reentry_no_refight", Session.WorldEncounters.Run?.Stage == "Victory");
        Walk(WorldEncounterCatalog.TreasurePosition); OpenPanel(d.Id); await Frames(); await Capture(d.Id + "-reward.png");
        await ClickAction(a => a.Action == "claim"); await Frames();
        Check(d.Id + "_reward_once", Session.WorldEncounters.Run?.Stage == "Claimed" && RewardCopies(d.RewardItemId) == copies + 1);
        string hash = Session.StateHash; Check(d.Id + "_duplicate_rejected", !Session.ClaimWorldEncounterReward().Success && Session.StateHash == hash);
        RoundTrip(d.Id + "_claimed_save"); OpenPanel(d.Id); await Frames(); await ClickAction(a => a.Action == "exit"); await Frames();
        Check(d.Id + "_exact_source", !Session.InWorldEncounter && Session.Campaign.ActiveEncounterId == d.SourceEncounterId && Session.Campaign.Combat.View.Actors.Single(a => a.Id == 1).Position == sourcePosition);
        Check(d.Id + "_source_loot_preserved", JsonData.Hash(Session.Campaign.Combat.Capture().Loot) == sourceLoot);
        Check(d.Id + "_source_progress_preserved", JsonData.Hash(Session.Campaign.Capture().Campaign) == sourceCampaign);
        Check(d.Id + "_visuals_reset", Presentation.PresentedEvent == "" && Presentation.DecorationCount == 0);
        RoundTrip(d.Id + "_exit_save"); CloseMenus();
    }
    private void CheckCaravanCombatBranch(EndgameRuntimeSnapshot snapshot)
    {
        var branch = EndgameRuntimeSession.Restore(Field<string>(_director, "_combatJson"), Field<AdventureContent>(_director, "_adventure"), Field<ProgressionContent>(_director, "_progression"), Field<CampaignContent>(_director, "_campaign"), Field<EndgameContent>(_director, "_endgame"), snapshot);
        for (int i = 0; i < 14000 && branch.WorldEncounters.Run?.Stage is "Foyer" or "Combat"; i++)
            Check("caravan_branch_command_accepted", branch.Execute(WorldEncounterSmoke.Next(branch, "event.caravan", caravanCombat: true)).Success);
        Check("caravan_detached_combat_alternative", branch.WorldEncounters.Run is { Stage: "Victory", Outcome: "Combat" });
        Check("caravan_branch_did_not_mutate_played_route", Session.StateHash == JsonData.Hash(snapshot));
    }
    private async Task CheckCosmeticSettings()
    {
        string hash = Session.StateHash;
        Check("storm_decoration_bounded", Presentation.DecorationCount == 6);
        _sandbox.SetModalPaused("world-smoke", true); double time = Presentation.MotionTime; await Frames();
        Check("storm_cosmetics_pause", Presentation.MotionTime == time);
        var reduced = Find<CheckButton>("SettingsReducedEffects"); bool previous = reduced.ButtonPressed;
        reduced.ButtonPressed = true; await Frames();
        Check("storm_reduced_effects_settle", Presentation.MotionTime == 0 && Presentation.DecorationCount == 6);
        await Capture("storm-reduced-effects.png"); reduced.ButtonPressed = previous;
        _sandbox.SetModalPaused("world-smoke", false); await Frames();
        Check("cosmetic_settings_no_gameplay_mutation", Session.StateHash == hash);
    }
    private int RewardCopies(string id) => Session.Capture().Campaign.Production.Progression.Character.Items.Count(i => i.DefinitionId == id);
    private void OpenPanel(string id) => Call(_director, "OpenWorldEncounters", id);
    private void Refresh() { _sandbox.AdoptSession(Session.Combat); Call(_director, "Refresh"); }
    private void CloseMenus()
    {
        Panel.SetOpen(false); Field<CampaignHud>(_director, "_campaignHud").SetOpen(false);
        Field<ProductionHud>(_director, "_character").Close(); Field<EndgameHud>(_director, "_board").SetOpen(false);
    }
    private void Step(EndgameRuntimeCommand command, bool refresh = true)
    {
        var result = Session.Execute(command); _commands++;
        if (!result.Success) throw new InvalidDataException(result.Reason + " / " + command.Action);
        Call(_director, "Observe", result); if (refresh) Refresh();
    }
    private void Walk(Position target)
    {
        CloseMenus();
        for (int i = 0; i < 700; i++)
        {
            var player = Session.Combat.View.Actors.Single(a => a.Id == 1);
            if (Position.DistanceSquared(player.Position, target) <= 1600L * 1600)
            { Step(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)])); return; }
            var direction = CombatProductionSmoke.MovementDirection(player.Position, target, Session.Room);
            Step(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]));
        }
        throw new InvalidDataException("Could not approach world encounter interaction.");
    }
    private bool Replay() => EndgameRuntimeReplayRunner.Run(Field<string>(_director, "_combatJson"), Field<AdventureContent>(_director, "_adventure"), Field<ProgressionContent>(_director, "_progression"), Field<CampaignContent>(_director, "_campaign"), Field<EndgameContent>(_director, "_endgame"), Session.CaptureReplay()).Success;
    private void RoundTrip(string name) { string hash = Session.StateHash; Check(name + "_replay", Replay()); Call(_director, "Save"); Call(_director, "Load"); Check(name, Session.StateHash == hash); }
    private async Task ClickAction(Func<WorldEncounterActionDisplay, bool> predicate)
    {
        await Frames(); var action = Display.Actions.Single(predicate);
        await Click("WorldEncounterAction_" + SafeName(action.Action) + "_" + SafeName(action.Id));
    }
    private async Task Click(string name)
    {
        await Frames(); var button = Find<Button>(name);
        for (Node? parent = button.GetParent(); parent is not null; parent = parent.GetParent()) if (parent is ScrollContainer scroll) scroll.EnsureControlVisible(button);
        await Frames(); Check("clickable_" + _clicks, button.IsVisibleInTree() && !button.Disabled && GetViewport().GetVisibleRect().HasPoint(button.GetGlobalRect().GetCenter()));
        var point = button.GetGlobalRect().GetCenter(); _lastClick = name;
        bool received = false; void Receipt() => received = true; button.Pressed += Receipt;
        GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { Position = point, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
        _clicks++; await Frames(); if (GodotObject.IsInstanceValid(button)) button.Pressed -= Receipt;
        Check("click_received_" + _clicks, received);
    }
    private void Confirm(bool accept)
    {
        var dialog = Find<ConfirmationDialog>("WorldEncounterConfirmation"); if (!dialog.Visible) return;
        dialog.Hide(); dialog.EmitSignal(accept ? ConfirmationDialog.SignalName.Confirmed : ConfirmationDialog.SignalName.Canceled);
    }
    private void CheckLayout(string suffix)
    {
        Check("panel_visible_" + suffix, Panel.IsOpen && Panel.IsVisibleInTree());
        var viewport = GetViewport().GetVisibleRect();
        foreach (string name in new[] { "WorldEncounterPanel", "WorldEncounterJournalEntries", "WorldEncounterDetailsScroll", "WorldEncounterClose" })
        { var control = Find<Control>(name); if (control.IsVisibleInTree()) Check(name + "_fits_" + suffix, viewport.Encloses(control.GetGlobalRect())); }
    }
    private async Task Frames(int count = 4) { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); _sandbox?._Process(0); } }
    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless" || !OS.GetCmdlineUserArgs().Contains("--capture-world-encounters")) return;
        GetWindow().GrabFocus(); await Frames(); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage(); Check("capture_" + name, image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("World encounter check failed: " + name); }
    private T Find<T>(string name) where T : Node => Descendants(_sandbox).OfType<T>().Single(n => n.Name == name);
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static object? Call(object instance, string name, params object?[] args)
    { try { return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
    private static string SafeName(string value) => new(value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    private async Task Finish(bool passed, string error)
    {
        var report = new
        {
            kind = "WorldEncountersSmoke",
            passed,
            checks = _checks,
            captures = _captures,
            clicks = _clicks,
            commands = _commands,
            error,
            run = _director is null ? null : Session.WorldEncounters.Run,
            lastClick = _lastClick,
            scope = "Earned campaign route; eight real mouse approaches; viewport choice/cancel/reward actions; ordinary Vanguard combat and caravan puzzle; detached caravan combat branch; death recovery, pending victory, once-only rewards, exact source, saves/replays, 1280/780 layouts. Scripted play is not human balance or hardware certification."
        };
        if (_writeReport) File.WriteAllText(Path.Combine(_output, "world-encounters-review.json"), JsonData.Write(report)); GD.Print(JsonData.Write(report));
        if (_director is not null && GodotObject.IsInstanceValid(_director)) _director.QueueFree(); _director = null!; _sandbox = null!;
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); GC.Collect();
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); GetTree().Quit(passed ? 0 : 1);
    }
}
