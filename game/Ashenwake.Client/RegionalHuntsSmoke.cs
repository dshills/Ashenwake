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

/// <summary>Earned progression and public commands; viewport board clicks, with native dialog decisions signaled explicitly.</summary>
public partial class RegionalHuntsSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private string _output = "";
    private int _commands, _clicks;
    private bool _writeReport;
    private EndgameRuntimeSession Session => Field<EndgameRuntimeSession>(_director, "_session");
    private RegionalHuntBoard Board => Field<RegionalHuntBoard>(_director, "_huntBoard");
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--regional-hunts-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Regional hunt smoke requires --regional-hunts-smoke --discipline=Vanguard --output=<fresh-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director); _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false);
            await Frames(); Field<CampaignHud>(_director, "_campaignHud").SetOpen(false); Call(_sandbox, "ResumePlaying");
            await Click("RegionalHuntNavigation");
            Check("fresh_contracts_locked", Session.RegionalHunts.Contracts.All(c => !c.Unlocked));
            Check("board_pauses", Board.IsOpen && _sandbox.IsPaused); CheckLayout("locked"); await Capture("locked-board.png");
            GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(8); CheckLayout("locked_minimum");
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames(8);
            Board.SetOpen(false);
            for (int i = 0; i < 45000 && !Session.Campaign.Capture().Campaign.CompletedActs.Contains(3); i++)
            {
                Step(new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(Session.Campaign)), false);
                if (i % 200 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Check("three_acts_earned", Session.Campaign.Capture().Campaign.CompletedActs.Contains(3));
            Step(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.ReturnToHub)));
            Call(_director, "ClearDeathRecap"); Call(_director, "Refresh");
            Check("all_contracts_unlocked", Session.RegionalHunts.Contracts.All(c => c.Unlocked));
            Walk(RegionalHuntCatalog.BoardPosition, RegionalHuntCatalog.BoardRange);
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus(); await Frames(); Call(_sandbox, "ResumePlaying");
            await Click("RegionalHuntNavigation"); Check("unlocked_board_open", Board.IsOpen); CheckLayout("wide"); await Capture("three-hunt-board.png");
            GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(8); CheckLayout("minimum"); await Capture("three-hunt-board-minimum.png");
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames(8);
            foreach (var contract in RegionalHuntCatalog.Contracts) await CompleteHunt(contract);
            await AbandonAndRecovery();
            Check("three_rewards", Session.Capture().RegionalHunts!.Rewards.Length == 3);
            Check("exact_replay", EndgameRuntimeReplayRunner.Run(Field<string>(_director, "_combatJson"), Field<AdventureContent>(_director, "_adventure"), Field<ProgressionContent>(_director, "_progression"), Field<CampaignContent>(_director, "_campaign"), Field<EndgameContent>(_director, "_endgame"), Session.CaptureReplay()).Success);
            foreach (var player in _sandbox.AudioDirector.MusicPlayers) { player.Stop(); player.Stream = null; }
            await Frames();
            Check("all_layout_checks", _checks.Values.All(v => v));
            Finish(true, "");
        }
        catch (Exception ex) { try { await Capture("failure.png"); } catch { } GD.PushError(ex.ToString()); Finish(false, ex.Message); }
    }
    private async Task CompleteHunt(RegionalHuntContract contract)
    {
        Walk(RegionalHuntCatalog.BoardPosition, RegionalHuntCatalog.BoardRange);
        Call(_director, "OpenRegionalHunts"); Board.SelectContract(contract.Id); await Frames();
        var before = Session.Capture().Campaign.Production.Progression.Character;
        await Click("HuntStart"); Check(contract.Id + "_confirmation", Find<ConfirmationDialog>("HuntConfirmation").Visible);
        Confirm(false); Check(contract.Id + "_cancel_start", !Session.InRegionalHunt);
        await Click("HuntStart"); Confirm(true); await Frames();
        Check(contract.Id + "_tracking", Session.InRegionalHunt && Session.RegionalHunts.Run?.Stage == "Tracking" && !Board.IsOpen);
        Check(contract.Id + "_no_hub_map", Session.LocalMap is null);
        Check(contract.Id + "_stage", Field<CampaignStage>(_director, "_stage").PresentedEncounter == contract.EncounterId);
        await Capture(contract.Id + "-trail.png");
        Board.SetOpen(false); if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus(); await Frames(); Call(_sandbox, "ResumePlaying");
        Call(_director, "OpenRegionalHunts"); await Frames();
        await Click("HuntFollowClue"); Check(contract.Id + "_approach_unpauses", !Board.IsOpen);
        // Exercise the actual mouse approach driver until it reaches and consumes the first clue.
        for (int i = 0; i < 400 && Session.RegionalHunts.Run?.TrackedClues == 0; i++) { _sandbox._Process(1d / 30); if (i % 10 == 0) await Frames(1); }
        Check(contract.Id + "_mouse_clue", Session.RegionalHunts.Run?.TrackedClues == 1);
        RoundTrip(contract.Id + "_tracking_save");
        Until(contract.Id, () => Session.RegionalHunts.Run?.Stage == "Combat");
        await Capture(contract.Id + "-combat.png"); RoundTrip(contract.Id + "_combat_save");
        for (int i = 0; i < 7000 && Session.RegionalHunts.Run?.Stage == "Combat"; i++)
        {
            Step(RegionalHuntSmoke.Next(Session, contract.Id), false);
            if (i % 100 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        _sandbox.AdoptSession(Session.Combat); Call(_director, "Refresh");
        Check(contract.Id + "_won", Session.RegionalHunts.Run?.Stage == "Victory");
        Check(contract.Id + "_not_paid_early", Session.Production.ProgressionView.Materials == before.Materials);
        Check(contract.Id + "_no_ground_loot", Session.Combat.View.Loot.Count == 0);
        await Frames(); Check(contract.Id + "_victory_board", Board.IsOpen); await Capture(contract.Id + "-victory.png");
        await Click("HuntReturn"); Check(contract.Id + "_returned", Session.InHub);
        RoundTrip(contract.Id + "_pending_claim_save");
        Board.SetOpen(false); if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus(); await Frames(); Call(_sandbox, "ResumePlaying");
        Call(_director, "OpenRegionalHunts"); await Frames();
        if (!Session.RegionalHunts.NearBoard)
        {
            await Click("HuntApproach");
            for (int i = 0; i < 400 && !Board.IsOpen; i++) { _sandbox._Process(1d / 30); if (i % 10 == 0) await Frames(1); }
            Check(contract.Id + "_board_approach", Board.IsOpen && Session.RegionalHunts.NearBoard);
        }
        await Click("HuntClaim"); Check(contract.Id + "_claimed", Session.RegionalHunts.Run?.Stage == "Claimed");
        var after = Session.Capture().Campaign.Production.Progression.Character;
        Check(contract.Id + "_reward_once", after.Materials == before.Materials + contract.Materials && after.Items.Count(i => i.DefinitionId == contract.RewardItemId) == before.Items.Count(i => i.DefinitionId == contract.RewardItemId) + 1);
        string hash = Session.StateHash; Check(contract.Id + "_duplicate_rejected", !Session.ClaimRegionalHuntReward().Success && Session.StateHash == hash);
        RoundTrip(contract.Id + "_claimed_save");
    }
    private async Task AbandonAndRecovery()
    {
        string id = RegionalHuntCatalog.Contracts[0].Id;
        Until(id, () => Session.RegionalHunts.Run?.Stage == "Tracking");
        Call(_director, "OpenRegionalHunts"); await Frames(); await Click("HuntAbandon"); Confirm(false);
        Check("cancel_abandon_keeps_hunt", Session.InRegionalHunt);
        await Click("HuntAbandon"); Confirm(true); await Frames();
        Check("abandoned_without_reward", Session.InHub && Session.RegionalHunts.Run?.Stage == "Abandoned" && Session.Capture().RegionalHunts!.Rewards.Length == 3);
        Until(id, () => Session.RegionalHunts.Run?.Stage == "Combat");
        for (int i = 0; i < 7000 && Session.RegionalHunts.Run?.Stage != "Failed"; i++) Step(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)]), false);
        _sandbox.AdoptSession(Session.Combat); Call(_director, "Refresh"); await Frames();
        Check("actual_hunt_death", Session.RegionalHunts.Run?.Stage == "Failed" && Field<DeathRecapHud>(_director, "_deathRecapHud").IsOpen);
        await Capture("hunt-death.png"); RoundTrip("failed_save");
        Check("restored_recovery", Field<DeathRecapHud>(_director, "_deathRecapHud").IsOpen);
        Call(_director, "DeathRecapPrimary"); await Frames();
        Check("death_return_resolves", Session.InHub && Session.RegionalHunts.Run?.Stage == "Abandoned" && Session.Capture().RegionalHunts!.Rewards.Length == 3);
    }
    private void RoundTrip(string name)
    {
        string hash = Session.StateHash; Check(name + "_replay", EndgameRuntimeReplayRunner.Run(Field<string>(_director, "_combatJson"), Field<AdventureContent>(_director, "_adventure"), Field<ProgressionContent>(_director, "_progression"), Field<CampaignContent>(_director, "_campaign"), Field<EndgameContent>(_director, "_endgame"), Session.CaptureReplay()).Success); Call(_director, "Save"); Call(_director, "Load"); Check(name, Session.StateHash == hash);
    }
    private void Until(string id, Func<bool> done)
    {
        Board.SetOpen(false);
        for (int i = 0; i < 2000 && !done(); i++) Step(RegionalHuntSmoke.Next(Session, id));
        Check("reached_" + id + "_" + _commands, done());
    }
    private void Step(EndgameRuntimeCommand command, bool refresh = true)
    {
        var result = Session.Execute(command); _commands++; if (!result.Success) throw new InvalidDataException(result.Reason + " / " + command.Action);
        Call(_director, "Observe", result); if (refresh) { _sandbox.AdoptSession(Session.Combat); Call(_director, "Refresh"); }
    }
    private void Walk(Position target, int range)
    {
        Board.SetOpen(false); Field<ProductionHud>(_director, "_character").Close(); Field<CampaignHud>(_director, "_campaignHud").SetOpen(false);
        for (int i = 0; i < 400; i++)
        {
            var player = Session.Combat.View.Actors.Single(a => a.Id == 1);
            if (Position.DistanceSquared(player.Position, target) <= (long)(range - 200) * (range - 200)) { Step(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)])); return; }
            var direction = CombatProductionSmoke.MovementDirection(player.Position, target, Session.Room);
            Step(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]));
        }
        throw new InvalidDataException("Could not approach hunt board.");
    }
    private void Confirm(bool accept)
    {
        var dialog = Find<ConfirmationDialog>("HuntConfirmation"); dialog.Hide(); dialog.EmitSignal(accept ? ConfirmationDialog.SignalName.Confirmed : ConfirmationDialog.SignalName.Canceled);
    }
    private void CheckLayout(string suffix)
    {
        Check("board_visible_" + suffix, Board.IsOpen && Board.IsVisibleInTree());
        var viewport = GetViewport().GetVisibleRect();
        foreach (string name in new[] { "RegionalHuntBoard", "HuntContracts", "HuntDetailsScroll", "HuntActions", "HuntClose" }) { _checks[name + "_fits_" + suffix] = viewport.Encloses(Find<Control>(name).GetGlobalRect()); }
    }
    private async Task Click(string name)
    {
        await Frames();
        var button = Find<Button>(name); Check("clickable_" + name + "_" + _clicks, button.IsVisibleInTree() && !button.Disabled);
        var point = button.GetGlobalRect().GetCenter(); GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { Position = point, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
        _clicks++; await Frames();
    }
    private async Task Frames(int count = 4) { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); _sandbox?._Process(0); } }
    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless" || !OS.GetCmdlineUserArgs().Contains("--capture-regional-hunts")) return;
        GetWindow().GrabFocus(); await Frames(); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage(); Check("capture_" + name, image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private void Check(string name, bool value) { _checks[name] = value; if (!value) throw new InvalidDataException("Regional hunt check failed: " + name); }
    private T Find<T>(string name) where T : Node => Descendants(_sandbox).OfType<T>().Single(n => n.Name == name);
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static object? Call(object instance, string name, params object?[] args)
    { try { return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = "RegionalHuntsSmoke",
            passed,
            checks = _checks,
            captures = _captures,
            clicks = _clicks,
            commands = _commands,
            run = _director is null ? null : Session.RegionalHunts.Run,
            notice = _director is null ? "" : Field<string>(_director, "_huntNotice"),
            error,
            scope = "Earns Acts 1–3 through public commands, tracks clues with native approach input, wins all three hunts with ordinary combat policy, and clicks board controls for departure, return, claim and abandon. Native confirmation decisions use public signals. Tests stage saves, reward duplication, actual death recovery, replay and 1280/780 layouts."
        };
        if (_writeReport) File.WriteAllText(Path.Combine(_output, "regional-hunts-review.json"), JsonData.Write(report)); GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
