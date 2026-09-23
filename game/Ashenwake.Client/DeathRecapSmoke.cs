using System.Reflection;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Godot;
using Position = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Shipping recovery controls driven by ordinary deaths, with the maintained
/// campaign-complete archive used only to unlock the Fracture gate.</summary>
public partial class DeathRecapSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [], _skipped = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private string _output = "", _case = "";
    private bool _writeReport;
    private int _commands, _replays;
    private EndgameRuntimeSession Journey => Field<EndgameRuntimeSession>(_director, "_session");
    private DeathRecapHud Hud => Field<DeathRecapHud>(_director, "_deathRecapHud");
    private bool Open => Hud.IsOpen;

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--death-recap-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Death recap smoke requires --death-recap-smoke --discipline=Vanguard --output=<fresh-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director);
            _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false);
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            await Campaign(); await Fracture();
            await ReleaseDiagnosticAudio();
            Finish(true, "");
        }
        catch (Exception ex)
        {
            GD.PushError(ex.ToString());
            if (_sandbox is not null) await ReleaseDiagnosticAudio();
            Finish(false, ex.Message);
        }
    }

    private async Task Campaign()
    {
        _case = "campaign";
        Call(_director, "Adopt", Call(_director, "Fresh", "Vanguard", null), false); await Frames();
        Apply(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.EnterAct, Act: 1)));
        string owned = Owned(), retained = RetainedProgress();
        Input.ActionPress("ui_accept");
        try
        {
            await Die();
            Check("held_accept_cannot_trigger_recovery", Open && Descendants(Hud).OfType<Button>().Where(b => b.Visible).All(b => b.Disabled));
        }
        finally { Input.ActionRelease("ui_accept"); }
        await Frames();
        Check("release_arms_explicit_recovery", !Find<Button>("DeathRecapPrimary").Disabled);
        await InputIsolation();
        Check("checkpoint_already_restored", Journey.Combat.View.Actors.Single(a => a.Id == 1).Health > 0);
        Check("owned_gear_retained", Owned() == owned);
        Check("earned_progress_retained", RetainedProgress() == retained);
        await Inspect("campaign-death.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720);
        await Inspect("campaign-death-minimum.png");
        string hash = Journey.StateHash;
        _sandbox.SetModalPaused("death-diagnostic-independent", true);
        Click("DeathRecapPrimary"); await Frames();
        Check("closing_recap_preserves_other_pause_owner", _sandbox.IsPaused &&
            Field<HashSet<string>>(_sandbox, "_modalPauses").Contains("death-diagnostic-independent") &&
            !Field<HashSet<string>>(_sandbox, "_modalPauses").Contains("death-recap"));
        _sandbox.SetModalPaused("death-diagnostic-independent", false);
        Check("continue_dismisses_without_second_recovery", !Open && Journey.StateHash == hash);
        Call(_director, "DeathRecapPrimary"); Check("repeated_continue_is_inert", Journey.StateHash == hash);
        Verify("campaign-recovered");
        Call(_director, "ShowFrontMenu"); await Frames();
        Call(_director, "ResumeFromFrontMenu"); await Frames();
        Check("menu_does_not_resurrect_dismissed_death", !Open && Journey.StateHash == hash);
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
    }

    private async Task InputIsolation()
    {
        string hash = Journey.StateHash;
        foreach (Key key in new[] { Key.W, Key.F, Key.C, Key.I, Key.J, Key.B, Key.H, Key.M, Key.P, Key.Key1 })
            foreach (bool pressed in new[] { true, false })
                GetViewport().PushInput(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed }, true);
        await Frames();
        Check("world_shortcuts_do_not_escape_recap", Open && _sandbox.IsPaused && Journey.StateHash == hash &&
            !Field<EndgameHud>(_director, "_board").IsOpen && _sandbox.PendingWorldActionId is null);
    }

    private async Task ReleaseDiagnosticAudio()
    {
        // The native mixer must release diagnostic-owned streams before immediate quit.
        // This cleanup has no production or gameplay behavior.
        foreach (var player in _sandbox.AudioDirector.MusicPlayers) { player.Stop(); player.Stream = null; }
        await Frames();
    }

    private async Task Fracture()
    {
        _case = "fracture";
        string fixture = Godot.FileAccess.GetFileAsString("res://phase4-campaign-complete.json");
        var imported = EndgameRuntimeMigration.ImportPhaseFour(fixture, Field<string>(_director, "_previousCombatJson"),
            Field<string>(_director, "_combatJson"), Field<AdventureContent>(_director, "_adventure"), Field<ProgressionContent>(_director, "_progression"),
            Field<CampaignContent>(_director, "_campaign"), Field<EndgameContent>(_director, "_endgame"));
        Call(_director, "Adopt", imported, false); await Frames();
        AtGate(new(EndgameRuntimeAction.ClaimRecoverySigil));
        AtGate(new(EndgameRuntimeAction.StartFracture, Journey.View.AvailableSigils.Single().Id));
        string owned = Owned(), retained = RetainedProgress(); int rewards = Journey.Capture().Endgame.Rewards.Count;
        await Die();
        Check("one_attempt_consumed", Journey.RunView is { Deaths: 1, AttemptsRemaining: 2, CanRetry: true });
        Check("no_reward_or_owned_gear_loss", Owned() == owned && Journey.Capture().Endgame.Rewards.Count == rewards);
        Check("earned_progress_retained", RetainedProgress() == retained);
        await Inspect("fracture-death.png"); Verify("fracture-dead");
        string deadHash = Journey.StateHash;
        Call(_director, "Save"); Call(_director, "Load"); await Frames();
        Check("restored_dead_run_opens_recovery", Open && Journey.StateHash == deadHash && Journey.RunView!.CanRetry);
        await Inspect("fracture-restored.png", expectHistory: false);
        Click("DeathRecapClose"); await Frames();
        Check("close_keeps_dead_run_and_shows_board", !Open && Journey.StateHash == deadHash && Field<EndgameHud>(_director, "_board").IsOpen);
        Call(_director, "Refresh"); Check("dismissed_generic_report_does_not_reopen", !Open);
        Call(_director, "Load"); await Frames();
        Click("DeathRecapPrimary"); await Frames();
        Check("retry_restores_once", !Open && Journey.RunView is { Deaths: 1, AttemptsRemaining: 2, CanRetry: false } && Journey.Combat.View.Actors.Single(a => a.Id == 1).Health > 0);
        string retryHash = Journey.StateHash;
        Call(_director, "DeathRecapPrimary"); Check("repeated_retry_is_inert", Journey.StateHash == retryHash);
        await Die();
        Check("second_death_consumes_one_attempt", Journey.RunView is { Deaths: 2, AttemptsRemaining: 1 });
        Call(_director, "ShowFrontMenu"); await Frames();
        Check("menu_hides_recap", !Open && Field<FrontMenu>(_director, "_frontMenu").IsOpen);
        Call(_director, "ResumeFromFrontMenu"); await Frames();
        Check("resume_exposes_pending_recovery", Open || Field<EndgameHud>(_director, "_board").IsOpen);
        if (Open) Click("DeathRecapPrimary");
        else Apply(new(EndgameRuntimeAction.RetryEncounter));
        await Frames(); await Die();
        Check("attempts_exhausted", Journey.RunView is { Deaths: 3, AttemptsRemaining: 0, Status: "Failed", CanRetry: false });
        GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720);
        await Inspect("fracture-failed-minimum.png"); Verify("fracture-failed");
        Click("DeathRecapPrimary"); await Frames();
        Check("failed_run_returns_to_greyhaven", !Open && Journey.InHub && Journey.Capture().Endgame.Rewards.Count == rewards && Owned() == owned);
        string hubHash = Journey.StateHash;
        Call(_director, "DeathRecapPrimary"); Check("repeated_return_does_not_grant_rewards", Journey.StateHash == hubHash);
        Check("maintained_archive_unchanged", fixture == Godot.FileAccess.GetFileAsString("res://phase4-campaign-complete.json"));
        Verify("fracture-returned");
    }

    private async Task Die()
    {
        Field<ProductionHud>(_director, "_character").Close(); Field<CampaignHud>(_director, "_campaignHud").SetOpen(false);
        Field<EndgameHud>(_director, "_board").SetOpen(false);
        for (int i = 0; i < 5000 && !Open; i++)
        {
            var hero = Journey.Combat.View.Actors.Single(a => a.Id == 1);
            var target = Journey.Combat.View.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0)
                .OrderBy(a => Position.DistanceSquared(a.Position, hero.Position)).FirstOrDefault();
            var direction = target is null ? default : CombatProductionSmoke.MovementDirection(hero.Position, target.Position, Journey.Room);
            Advance(target is not null && Position.DistanceSquared(hero.Position, target.Position) > 900L * 900 ?
                [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)] : [new(CombatCommandKind.Stop)]);
            if (i % 120 == 0) await Frames(1);
        }
        Check("ordinary_death_opens_recap." + _commands, Open); await Frames();
    }
    private async Task Inspect(string filename, bool expectHistory = true)
    {
        await Frames(6);
        Check("modal_pause." + filename, Open && _sandbox.IsPaused);
        string hash = Journey.StateHash;
        var nodes = Descendants(Hud).Select(n => n.GetInstanceId()).ToArray();
        for (int i = 0; i < 8; i++) Call(_director, "Refresh");
        Check("stable_controls." + filename, nodes.SequenceEqual(Descendants(Hud).Select(n => n.GetInstanceId())));
        Advance([new(CombatCommandKind.Move, X: 1)]); _sandbox._Process(.15); await Frames();
        Check("paused_simulation." + filename, Journey.StateHash == hash);
        var viewport = GetViewport().GetVisibleRect();
        var visibleButtons = Descendants(Hud).OfType<Button>().Where(b => b.IsVisibleInTree()).ToArray();
        bool actionsFit = visibleButtons.All(b => viewport.Encloses(b.GetGlobalRect()));
        if (!actionsFit)
        {
            var panel = Find<Control>("DeathRecapPanel"); var scroll = Find<Control>("DeathRecapScroll");
            GD.Print(JsonData.Write(new
            {
                kind = "DeathRecapBoundsFailure",
                filename,
                viewport = viewport.ToString(),
                panel = new { rect = panel.GetGlobalRect().ToString(), minimum = panel.GetCombinedMinimumSize().ToString() },
                scroll = new { rect = scroll.GetGlobalRect().ToString(), minimum = scroll.GetCombinedMinimumSize().ToString() },
                buttons = visibleButtons.Select(b => new { name = b.Name.ToString(), rect = b.GetGlobalRect().ToString(), minimum = b.GetCombinedMinimumSize().ToString() }).ToArray()
            }));
        }
        Check("actions_fit." + filename, actionsFit);
        Check("primary_enabled." + filename, !Find<Button>("DeathRecapPrimary").Disabled);
        DeathRecapClientChecks.Validate(Hud, (key, value) => Check(filename + "." + key, value));
        string killing = Find<Label>("DeathRecapKillingBlow").Text;
        if (expectHistory)
        {
            var recap = Journey.LastDeathRecap;
            Check("actual_resolved_hit." + filename, recap is { KillingBlow.Damage: > 0 } && recap.RecentDamage.Count > 0 &&
                killing.Contains(recap.KillingBlow.SourceName, StringComparison.Ordinal) && killing.Contains(recap.KillingBlow.AttackName, StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(_output, filename + ".recap.json"), JsonData.Write(recap));
        }
        else Check("restored_history_is_not_invented." + filename, Journey.LastDeathRecap is null && killing.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
        await Capture(filename);
        if (filename.Contains("minimum", StringComparison.Ordinal))
        {
            var scroll = Find<ScrollContainer>("DeathRecapScroll");
            scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue;
            await Frames();
            Check("recovery_scroll_reachable." + filename, scroll.GetGlobalRect().Intersects(Find<Label>("DeathRecapRecovery").GetGlobalRect()));
            await Capture(filename.Replace(".png", "-recovery.png", StringComparison.Ordinal));
            scroll.ScrollVertical = 0;
        }
    }
    private void AtGate(EndgameRuntimeCommand command)
    {
        for (int i = 0; i < 500; i++)
        {
            var next = EndgameRuntimeSmoke.AtGate(Journey, command);
            if (next.Action == EndgameRuntimeAction.Tick) Advance(next.Commands ?? []); else { Apply(next); return; }
        }
        throw new InvalidDataException("Could not approach the Fracture gate.");
    }
    private void Advance(CombatCommand[] commands)
    {
        var events = (IReadOnlyList<CombatEvent>)Call(_director, "Advance", (object)commands)!; _commands++;
        _sandbox.PresentCombatEvents(events, Journey.Combat);
    }
    private void Apply(EndgameRuntimeCommand command) { Call(_director, "Apply", command); _commands++; }
    private string Owned() => JsonData.Hash(Journey.Production.Capture().Progression.Character.Items.OrderBy(i => i.Id));
    private string RetainedProgress()
    {
        var state = Journey.Production.Capture().Progression;
        return JsonData.Hash(new
        {
            state.Character.Experience,
            state.Character.Materials,
            state.Character.Mastery,
            state.Character.CompletedObjectives,
            state.Character.Passives,
            state.Profile
        });
    }
    private void Verify(string label)
    {
        Call(_director, "VerifyAndWrite", Path.Combine(_output, label), null); _replays++;
        Check("save_replay." + label, File.Exists(Path.Combine(_output, label, "endgame.awendgame")));
    }
    private async Task Capture(string filename)
    {
        if (DisplayServer.GetName() == "headless" || !OS.GetCmdlineUserArgs().Contains("--capture-death-recap")) { _skipped.Add(filename); return; }
        for (int attempt = 0; attempt < 4; attempt++)
        {
            GetWindow().GrabFocus(); Call(_sandbox, "ResumePlaying"); await Frames(5);
            if (Field<PanelContainer>(_sandbox, "_resumePanel").Visible) continue;
            RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
            using var image = GetViewport().GetTexture().GetImage();
            Check("capture." + filename, image.GetWidth() == GetWindow().Size.X && image.GetHeight() == GetWindow().Size.Y && image.SavePng(Path.Combine(_output, filename)) == Error.Ok);
            _captures.Add(filename); return;
        }
        throw new InvalidDataException("Native recap capture remained obscured by interruption pause.");
    }
    private void Click(string name) { var button = Find<Button>(name); Check("click." + name + "." + _commands, button.IsVisibleInTree() && !button.Disabled); button.EmitSignal(Button.SignalName.Pressed); }
    private async Task Frames(int count = 4)
    {
        for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); _sandbox._Process(0); }
    }
    private void Check(string key, bool okay) { _checks[_case + "." + key] = okay; if (!okay) throw new InvalidDataException("Death recap smoke failed: " + _case + "." + key); }
    private T Find<T>(string name) where T : Node => Descendants(_sandbox).OfType<T>().Single(n => n.Name == name);
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static T Field<T>(object instance, string name) => (T)(instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance))!;
    private static object? Call(object instance, string name, params object?[] args)
    {
        try { return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
    }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = "DeathRecapClientSmoke",
            passed,
            checks = _checks,
            captures = _captures,
            skipped = _skipped,
            commands = _commands,
            replays = _replays,
            error,
            scope = "Shipping director and recovery controls; fresh Vanguard campaign death and maintained campaign archive unlock ordinary Fracture deaths, retries and exhaustion. No damage, health, attempt, reward or item injection. Save/replay and minimum/default captures. God Hunt mechanics are covered by Core tests, not this native route."
        };
        if (_writeReport) File.WriteAllText(Path.Combine(_output, "death-recap-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
