using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Experiments;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Production;
using Ashenwake.Core.Serialization;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;
using FileAccess = Godot.FileAccess;

namespace Ashenwake.Client;

public partial class EndgameDirector
{
    private ExperimentRuntimeSession? _experiment;
    private ExperimentContent _experimentContent = null!;
    private ExperimentPresentation _memoryPresentation = null!;
    private PanelContainer _echoesPanel = null!;
    private ColorRect _echoesBackdrop = null!;
    private VBoxContainer _echoesRows = null!;
    private Label _memoryStatus = null!;
    private Button _bindMemory = null!, _releaseMemory = null!, _castMemory = null!;
    private bool _echoesSmoke, _echoesPaused, _echoesSavedBound, _echoesSavedWarning, _echoesReleasedCheck, _echoesMindPrepared, _echoesSuppressionChecked, _echoesCompletionScheduled;
    private int _echoesSteps;
    private int _echoesFrames;
    private string _echoesSaveName = "", _echoesOriginal = "";
    private readonly HashSet<string> _echoesCaptures = [];
    private string EchoesPath => Path.Combine(_output, _echoesSaveName);

    private void InitializeExperiments()
    {
        _echoesSmoke = OS.GetCmdlineUserArgs().Contains("--echoes-smoke");
        _experimentContent = ExperimentContent.Parse(FileAccess.GetFileAsString("res://experiments.json"));
        _sandbox.AutomaticStep |= _echoesSmoke;
        _memoryPresentation = new ExperimentPresentation(); AddChild(_memoryPresentation);
        var open = new Button { Text = "Echoes: Borrowed Memory [H]", Position = new(921, 100), Size = new(326, 32) };
        open.AddThemeFontSizeOverride("font_size", 13); open.Pressed += ShowExperimentPanel; _sandbox.AddOverlay(open);
        _memoryStatus = new Label { Position = new(32, 304), Size = new(295, 180), AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
        _memoryStatus.AddThemeFontSizeOverride("font_size", 14); _memoryStatus.AddThemeColorOverride("font_color", new Color("c1dcff")); _sandbox.AddOverlay(_memoryStatus);
        _bindMemory = MemoryButton("Bind nearby memory [H]", new(32, 485), () => { if (_experiment?.View.Memory is { } memory) ApplyExperiment(new(ExperimentAction.BindMemory, SourceActorId: memory.SourceActorId)); });
        _castMemory = MemoryButton("Cast Echo Storm", new(32, 524), () =>
        {
            int target = _session.Combat.View.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0).OrderBy(a => CorePosition.DistanceSquared(a.Position, _session.Combat.View.Actors.Single(a => a.Id == 1).Position)).FirstOrDefault()?.Id ?? 0;
            Apply(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.CastEcho, TargetId: target)]));
        });
        _releaseMemory = MemoryButton("Release · restore owned Mind", new(32, 563), () => ApplyExperiment(new(ExperimentAction.ReleaseMemory)));
        foreach (Control control in new Control[] { _memoryStatus, _bindMemory, _castMemory, _releaseMemory }) control.GetParent().MoveChild(control, _character.GetIndex());
        _echoesBackdrop = new ColorRect { Color = new Color(0, 0, 0, .4f), MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _echoesBackdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); _sandbox.AddOverlay(_echoesBackdrop);
        _echoesPanel = new PanelContainer { Position = new(266, 162), Size = new(748, 466), Visible = false };
        _echoesPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("122031"), BorderColor = new Color("9abadd"), BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2, ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 15, ContentMarginBottom = 15 });
        _sandbox.AddOverlay(_echoesPanel);
        var scroll = new ScrollContainer { CustomMinimumSize = new(700, 435) }; _echoesPanel.AddChild(scroll);
        _echoesRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; scroll.AddChild(_echoesRows);
        if (!InputMap.HasAction("aw_experiment")) { InputMap.AddAction("aw_experiment"); InputMap.ActionAddEvent("aw_experiment", new InputEventKey { PhysicalKeycode = Key.H }); }
    }
    private Button MemoryButton(string text, Vector2 position, Action action)
    {
        var button = new Button { Text = text, Position = position, Size = new(295, 32), Visible = false };
        button.AddThemeFontSizeOverride("font_size", 13); button.Pressed += () => Safely(action); _sandbox.AddOverlay(button); return button;
    }
    private void ConfigureExperimentStart()
    {
        if (_echoesSmoke) { BeginExperimentSmoke(); return; }
        if (OS.GetCmdlineUserArgs().Contains("--echoes")) ShowExperimentPanel();
    }
    public override void _Notification(int what)
    { if (what == NotificationApplicationFocusOut && _echoesPanel is { Visible: true }) _echoesPaused = true; }
    public override void _Process(double delta)
    {
        if (!_echoesSmoke || _finished) return;
        if (++_echoesFrames > 10000) { Fail(new InvalidDataException("Experiment smoke exceeded its scene-frame bound.")); return; }
        if (_echoesFrames % 600 == 0) GD.Print(JsonData.Write(new { kind = "ExperimentClientFrameProgress", frames = _echoesFrames, steps = _echoesSteps, tick = _session.Tick, paused = _sandbox.IsPaused, capturing = _capturing }));
    }
    private bool BlockExperimentPanelInput(InputEvent input)
    {
        if (_echoesPanel is not { Visible: true } || input is not (InputEventKey or InputEventJoypadButton)) return false;
        if (input.IsActionPressed("ui_cancel")) { CloseExperimentPanel(); GetViewport().SetInputAsHandled(); return true; }
        if (!new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev" }.Any(action => input.IsAction(action))) { GetViewport().SetInputAsHandled(); return true; }
        return false;
    }
    private bool HandleExperimentInput(InputEvent input)
    {
        if (_echoesSmoke || _finished || _classSelection is not { Visible: false } || !input.IsActionPressed("aw_experiment")) return false;
        if (_experiment?.View.Memory is { CanBind: true } memory && !_sandbox.IsPaused) ApplyExperiment(new(ExperimentAction.BindMemory, SourceActorId: memory.SourceActorId));
        else ShowExperimentPanel();
        GetViewport().SetInputAsHandled(); return true;
    }
    private void ShowExperimentPanel()
    {
        if (_classSelection.Visible) return;
        if (!_echoesPanel.Visible) { _echoesPaused = _sandbox.IsPaused; _sandbox.SetPaused(true); }
        _echoesPanel.Visible = _echoesBackdrop.Visible = true;
        foreach (var child in _echoesRows.GetChildren()) { _echoesRows.RemoveChild(child); child.QueueFree(); }
        EchoesText("ECHOES: BORROWED MEMORY", 23);
        EchoesAction("Close [Esc]", CloseExperimentPanel).GrabFocus();
        if (_experiment?.View.Cosmetics.Count > 0) EchoesText("EARNED · Borrowed Memory cosmetic record", 18);
        if (_experiment?.View.Entries.LastOrDefault() is { } latest) EchoesText($"Last contract: {(latest.Choice == ExperimentChoice.KeepMind ? "kept owned Mind" : "borrowed a memory")} · {latest.Outcome}");
        EchoesText("An optional Fracture contract. A separate Echoes character preserves your original save. Future progress in this character, including the contract outcome, stays in its Echoes archive.");
        EchoesText("KEEP YOUR MIND: enter an ordinary Fracture with your current anatomy.\nBORROW A MEMORY: suppress the owned Mind effect, defeat an elite, then approach its memory to bind Echo Storm. Your permanent anatomy and Resonance remain intact.");
        var rules = _experimentContent.Capture();
        EchoesText($"Use the borrowed Echo within {rules.EchoLifetimeTicks / 30d:F0} seconds. Casting warns of a hostile Storm circle at your feet for {rules.WarningTicks / 30d:F1} seconds, then makes it dangerous for {rules.HazardTicks / 30d:F1} seconds. Move or dodge out. Release restores your owned Mind without using the Echo.");
        EchoesText("Complete the actual Fracture after using the Echo to earn the Borrowed Memory cosmetic record. No additional combat power is awarded.");
        if (!_session.View.Unlocked) EchoesText("Complete the campaign before entering this contract.");
        else if (!_session.InHub) EchoesText("Complete or abandon this expedition, then return to Greyhaven to begin another contract.");
        else
        {
            var sigils = _session.View.AvailableSigils;
            if (sigils.Length == 0) EchoesText("Claim a recovery Sigil from the expedition board first.");
            else
            {
                var selector = new OptionButton(); foreach (var sigil in sigils) selector.AddItem($"Tier {sigil.Tier} · {Region(sigil.Region)} · Sigil #{sigil.Id}"); _echoesRows.AddChild(selector);
                EchoesText(AtGate() ? "Entering consumes the selected Sigil. The expedition's existing rules and three attempts still apply." : "Approach the Fracture gate in eastern Greyhaven first.");
                EchoesAction("Keep my Mind · consume Sigil and enter", () => BeginExperiment(sigils[selector.Selected].Id, ExperimentChoice.KeepMind)).Disabled = !AtGate();
                EchoesAction("Borrow a memory · consume Sigil and enter", () => BeginExperiment(sigils[selector.Selected].Id, ExperimentChoice.BorrowMind)).Disabled = !AtGate() || !_experimentContent.AcceptingEntries;
                if (!_experimentContent.AcceptingEntries) EchoesText("New Borrowed Memory entries are closed. Existing contracts can still finish, save and resume.");
            }
        }
        if (_experiment is not null)
        {
            EchoesAction("Save this Echoes character", () => { SaveExperiment(); CloseExperimentPanel(); });
            EchoesAction("Return to original character · save Echoes separately", ExitExperiment).Disabled = !_session.InHub;
        }
        if (TryEchoesSelection() is not null) EchoesAction("Continue saved Echoes character", LoadExperiment);
        RefreshExperiment();
    }
    private void EchoesText(string text, int size = 14)
    { var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new(675, 0) }; label.AddThemeFontSizeOverride("font_size", size); _echoesRows.AddChild(label); }
    private Button EchoesAction(string text, Action action)
    { var button = new Button { Text = text, CustomMinimumSize = new(0, 35) }; button.Pressed += () => Safely(action); _echoesRows.AddChild(button); return button; }
    private void CloseExperimentPanel() { _echoesPanel.Visible = _echoesBackdrop.Visible = false; _sandbox.SetPaused(_echoesPaused); RefreshExperiment(); }
    private void BeginExperiment(long sigil, ExperimentChoice choice)
    {
        if (_experiment is null)
        {
            Save();
            _experiment = ExperimentRuntimeSession.FromEndgame(_combatJson, _adventure, _progression, _campaign, _endgame, _experimentContent, _session.Capture());
            _echoesSaveName = "echoes." + Guid.NewGuid().ToString("N") + ".save.json";
        }
        CloseExperimentPanel(); ApplyExperiment(new(ExperimentAction.StartContract, sigil, Choice: choice)); SaveExperiment();
    }
    private EndgameRuntimeResult ExecuteActive(EndgameRuntimeCommand command)
    {
        if (_experiment is null) return _session.Execute(command);
        var result = _experiment.ExecuteEndgame(command); _session = _experiment.Endgame;
        return new(result.Success, result.Reason, result.CombatEvents, result.WorldEvents);
    }
    private void ApplyExperiment(ExperimentCommand command)
    {
        if (_experiment is null) return;
        var result = _experiment.Execute(command); _session = _experiment.Endgame;
        if (!result.Success) { Notice(result.Reason); return; }
        Observe(new(result.Success, result.Reason, result.CombatEvents, result.WorldEvents)); _revision++;
        _sandbox.AdoptSession(_session.Combat); Refresh();
        Notice(command.Action switch { ExperimentAction.BindMemory => "Echo Storm bound. Use it before the memory fades; leave the Storm warning after casting.", ExperimentAction.ReleaseMemory => "Memory released. Your owned Mind effect is restored.", _ => command.Choice == ExperimentChoice.KeepMind ? "Entered with your owned Mind intact." : "Your Mind effect is suppressed until this borrowed memory ends." });
    }
    private void RefreshExperiment()
    {
        if (_memoryStatus is null) return;
        var view = _experiment?.View; var memory = view?.Memory;
        _memoryPresentation.Show(memory);
        bool visible = memory is not null && !_echoesPanel.Visible;
        _memoryStatus.Visible = visible; _bindMemory.Visible = visible && memory!.Status == "Offered";
        _releaseMemory.Visible = visible && memory!.CanRelease; _castMemory.Visible = visible && memory!.EchoSkillId.Length > 0;
        if (memory is null) return;
        string suppression = memory.SuppressedMindId.Length > 0 ? "Suppressed: " + (_combat.Fragments.FirstOrDefault(f => f.Id == memory.SuppressedMindId)?.Name ?? "owned Mind fragment") : memory.CanRelease ? "Mind socket loan active." : "Owned Mind restored.";
        string echoKey = InputMap.ActionGetEvents("aw_echo").OfType<InputEventKey>().FirstOrDefault()?.PhysicalKeycode.ToString() ?? "Controls";
        string state = memory.Status switch { "Pending" => "Defeat an elite to reveal its memory.", "Offered" => memory.CanBind ? "Memory nearby. Bind it [H] or release the loan." : "Approach the marked elite memory.", "Bound" => $"Echo Storm · {memory.RemainingTicks / 30d:F1}s remaining · [{echoKey}] to cast", "Spent" => "Echo used. Finish the Fracture to earn its cosmetic record.", "Released" => "You released the memory.", "Expired" => "The borrowed memory faded.", "Lost" => "The borrowed memory was lost on death.", _ => "" };
        _castMemory.Text = $"Cast Echo Storm [{echoKey}]";
        _memoryStatus.Text = "BORROWED MEMORY\n" + suppression + "\n\n" + state + (memory.HazardStage == "None" ? "" : $"\n\nSTORM {memory.HazardStage.ToUpperInvariant()} · {memory.HazardRemainingTicks / 30d:F1}s · leave the circle");
        _bindMemory.Disabled = !memory.CanBind || _sandbox.IsPaused; _castMemory.Disabled = _sandbox.IsPaused;
        if (OS.GetCmdlineUserArgs().Contains("--capture-echoes") && DisplayServer.GetName() != "headless" && (memory.Status == "Offered" || memory.HazardStage != "None"))
        {
            string key = memory.HazardStage != "None" ? "storm-" + memory.HazardStage.ToLowerInvariant() : "memory-offered";
            if (_echoesCaptures.Add(key)) CaptureRenderedFrame("echoes-" + key + ".png");
        }
    }
    private string? TryEchoesSelection()
    {
        try
        {
            string pointer = Path.Combine(_output, "current-echoes.txt"); if (!File.Exists(pointer) || new FileInfo(pointer).Length > 256) return null;
            string name = File.ReadAllText(pointer).Trim();
            return name.StartsWith("echoes.", StringComparison.Ordinal) && name.EndsWith(".save.json", StringComparison.Ordinal) && name.Length <= 128 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-') &&
                (File.Exists(Path.Combine(_output, name)) || File.Exists(Path.Combine(_output, name + ".bak"))) ? name : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
    private bool SaveExperiment()
    {
        if (_experiment is null) return false;
        ExperimentSaveStore.Write(EchoesPath, _combatJson, _adventure, _progression, _campaign, _endgame, _experimentContent, _experiment.Capture());
        AtomicFile.Write(Path.Combine(_output, "current-echoes.txt"), _echoesSaveName); Notice("Echoes character and contract saved separately. Your original character is preserved."); return true;
    }
    private void LoadExperiment()
    {
        string name = _experiment is not null ? _echoesSaveName : TryEchoesSelection() ?? throw new InvalidDataException("No valid Echoes save selection is available.");
        var loaded = ExperimentSaveStore.Load(Path.Combine(_output, name), _combatJson, _adventure, _progression, _campaign, _endgame, _experimentContent);
        _experiment = loaded.Session; _echoesSaveName = name; _echoesPanel.Visible = _echoesBackdrop.Visible = false; Adopt(_experiment.Endgame, true);
        Notice(loaded.RecoveredBackup ? "Recovered the previous valid Echoes character and contract." : "Echoes character, choices and contract loaded.");
    }
    private void ExitExperiment()
    {
        if (!_session.InHub) throw new InvalidDataException("Return to Greyhaven before switching characters.");
        var original = EndgameRuntimeSaveStore.Load(SavePath, _combatJson, _adventure, _progression, _campaign, _endgame).Session;
        SaveExperiment(); _echoesPanel.Visible = _echoesBackdrop.Visible = false; Adopt(original);
        Notice("Original character restored. Continue the separate Echoes character from its panel whenever you choose.");
    }
    private void VerifyExperiment(string directory)
    {
        if (_experiment is null) throw new InvalidDataException("No Echoes session is active.");
        var replay = _experiment.CaptureReplay(); var verified = ExperimentReplayRunner.Run(_combatJson, _adventure, _progression, _campaign, _endgame, _experimentContent, replay);
        if (!verified.Success || verified.FinalHash != _experiment.StateHash) throw new InvalidDataException("Echoes replay diverged: " + verified.Detail);
        AtomicFile.Write(Path.Combine(directory, "echoes.awexperiment"), JsonData.Write(replay));
        string path = Path.Combine(directory, "echoes.checkpoint.save.json"); ExperimentSaveStore.Write(path, _combatJson, _adventure, _progression, _campaign, _endgame, _experimentContent, _experiment.Capture());
        if (ExperimentSaveStore.Load(path, _combatJson, _adventure, _progression, _campaign, _endgame, _experimentContent).Session.StateHash != _experiment.StateHash) throw new InvalidDataException("Echoes save round trip changed state.");
    }
    private async Task<bool> WaitForExperimentDraw()
    {
        var completion = new TaskCompletionSource<bool>();
        void Draw() => completion.TrySetResult(true);
        void Timeout() => completion.TrySetResult(false);
        var timer = GetTree().CreateTimer(3);
        RenderingServer.FramePostDraw += Draw; timer.Timeout += Timeout;
        try { return await completion.Task; }
        finally { RenderingServer.FramePostDraw -= Draw; if (GodotObject.IsInstanceValid(timer)) timer.Timeout -= Timeout; }
    }
}

public partial class EndgameDirector
{
    private bool _echoesKeptCheck;
    private void BeginExperimentSmoke()
    {
        string source = FileAccess.GetFileAsString("res://phase4-campaign-complete.json");
        var imported = EndgameRuntimeMigration.ImportPhaseFour(source, _previousCombatJson, _combatJson, _adventure, _progression, _campaign, _endgame);
        Adopt(imported); Save(); _echoesOriginal = File.ReadAllText(SavePath);
        _experiment = ExperimentRuntimeSession.FromEndgame(_combatJson, _adventure, _progression, _campaign, _endgame, _experimentContent, imported.Capture());
        _echoesSaveName = "echoes.smoke.save.json"; _session = _experiment.Endgame; _sandbox.AutomaticStep = true; _sandbox.SetPaused(false);
    }
    private IReadOnlyList<CombatEvent> AdvanceExperimentSmoke()
    {
        if (_echoesCompletionScheduled) return [];
        if (_experiment is null) throw new InvalidDataException("Experiment smoke lost its wrapper.");
        if (++_echoesSteps > ExperimentRuntimeSmoke.MaximumCommands) throw new InvalidDataException("Experiment client smoke exceeded its bounded public-action route.");
        if (!_echoesMindPrepared)
        {
            var mara = _session.Interactions.Single(i => i.ActionId == "npc.mara"); var player = _session.Combat.View.Actors.Single(a => a.Id == 1);
            EndgameRuntimeCommand prepare;
            if (CorePosition.DistanceSquared(player.Position, mara.Position) <= (long)mara.Range * mara.Range)
            {
                prepare = new(EndgameRuntimeAction.Production, Production: new(ProductionAction.Expedition, new(ExpeditionAction.InstallFragment, "Mind", "fragment.last_memory")));
                _echoesMindPrepared = true;
            }
            else
            {
                var direction = CombatProductionSmoke.MovementDirection(player.Position, mara.Position, _session.Room);
                prepare = new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]);
            }
            var prepared = ExecuteActive(prepare); if (!prepared.Success) throw new InvalidDataException("Mind preparation failed: " + prepared.Reason);
            _revision++; Refresh(); return prepared.CombatEvents;
        }
        var command = ExperimentRuntimeSmoke.Next(_experiment);
        if (!_echoesKeptCheck && command.Action == ExperimentAction.StartContract)
        {
            var branch = ExperimentRuntimeSession.Restore(_combatJson, _adventure, _progression, _campaign, _endgame, _experimentContent, _experiment.Capture());
            if (!branch.StartContract(command.SigilId, ExperimentChoice.KeepMind).Success || branch.View.Memory is not null || branch.Endgame.RunView?.Status != "Active") throw new InvalidDataException("Keep Mind did not enter an ordinary Fracture.");
            if (!branch.ExecuteEndgame(new(EndgameRuntimeAction.Abandon)).Success || !branch.InHub) throw new InvalidDataException("Keep Mind branch could not safely exit.");
            if (branch.View.Entries.Single().Choice != ExperimentChoice.KeepMind) throw new InvalidDataException("Keep Mind choice receipt was lost.");
            VerifyExperimentBranch(branch, "keep-mind"); _echoesKeptCheck = true;
        }
        var result = _experiment.Execute(command); _session = _experiment.Endgame;
        if (!result.Success) throw new InvalidDataException("Experiment smoke command rejected: " + result.Reason);
        if (_experiment.View.Memory is { SuppressedMindId: "fragment.last_memory" }) _echoesSuppressionChecked = true;
        Observe(new(true, "", result.CombatEvents, result.WorldEvents));
        if (_experiment.View.Memory is { Status: "Bound" } && !_echoesSavedBound)
        {
            VerifyExperiment(Path.Combine(_output, "bound-memory")); SaveExperiment(); LoadExperiment(); _echoesSavedBound = true;
            var release = ExperimentRuntimeSession.Restore(_combatJson, _adventure, _progression, _campaign, _endgame, _experimentContent, _experiment!.Capture());
            if (!release.ReleaseMemory().Success || release.View.Memory is not { Status: "Released", SuppressedMindId: "" }) throw new InvalidDataException("Releasing the memory did not restore the owned Mind.");
            VerifyExperimentBranch(release, "release-memory"); _echoesReleasedCheck = true;
        }
        if (_experiment!.View.Memory is { HazardStage: "Warning" } && !_echoesSavedWarning)
        { VerifyExperiment(Path.Combine(_output, "storm-warning")); SaveExperiment(); LoadExperiment(); _echoesSavedWarning = true; }
        _revision++; Refresh();
        if (_echoesSteps % 1000 == 0) GD.Print(JsonData.Write(new { kind = "ExperimentClientProgress", steps = _echoesSteps, run = _session.RunView, memory = _experiment!.View.Memory }));
        if (ExperimentRuntimeSmoke.Complete(_experiment!)) { _echoesCompletionScheduled = true; Callable.From(CompleteExperimentSmoke).CallDeferred(); }
        return result.CombatEvents;
    }
    private void VerifyExperimentBranch(ExperimentRuntimeSession branch, string name)
    {
        var replay = branch.CaptureReplay(); var result = ExperimentReplayRunner.Run(_combatJson, _adventure, _progression, _campaign, _endgame, _experimentContent, replay);
        if (!result.Success || result.FinalHash != branch.StateHash) throw new InvalidDataException("Experiment choice branch diverged.");
        AtomicFile.Write(Path.Combine(_output, name + ".awexperiment"), JsonData.Write(replay));
    }
    private async void CompleteExperimentSmoke()
    {
        if (_finished) return; _finished = true;
        try
        {
            if (!_echoesKeptCheck || !_echoesSavedBound || !_echoesSavedWarning || !_echoesReleasedCheck || !_echoesSuppressionChecked || _experiment?.View.Cosmetics.Count != 1) throw new InvalidDataException("Experiment smoke missed a choice, memory, warning, or reward check.");
            VerifyExperiment(_output); string hash = _experiment.StateHash; var run = _experiment.View.Run; var cosmetics = _experiment.View.Cosmetics.ToArray(); var entries = _experiment.View.Entries.ToArray();
            if (entries.Single() is not { Choice: ExperimentChoice.BorrowMind, Outcome: "Completed" }) throw new InvalidDataException("Completed contract choice receipt was lost.");
            SaveExperiment(); ExitExperiment();
            bool originalPreserved = File.ReadAllText(SavePath) == _echoesOriginal && _experiment is null;
            LoadExperiment(); if (_experiment!.StateHash != hash || !originalPreserved) throw new InvalidDataException("Safe experiment exit changed an archive or dropped experiment state.");
            var report = new
            {
                kind = "ExperimentClientSmokePassed",
                steps = _echoesSteps,
                stateHash = hash,
                actualFractureCompleted = run?.Status == "Completed",
                run,
                keptMind = _echoesKeptCheck,
                releasedMind = _echoesReleasedCheck,
                ownedMindSuppressed = _echoesSuppressionChecked,
                boundSaveLoad = _echoesSavedBound,
                stormSaveLoad = _echoesSavedWarning,
                originalPreserved,
                resumedAfterSafeExit = true,
                cosmetics,
                entries,
                replay = "echoes.awexperiment",
                events = new SortedDictionary<string, int>(_events),
                note = "Actual public input, memory proximity, cast, hostile Storm, Fracture completion and separate archive lifecycle. Procedural presentation."
            };
            AtomicFile.Write(Path.Combine(_output, "experiment-client-report.json"), JsonData.Write(report)); GD.Print(JsonData.Write(report));
            if (OS.GetCmdlineUserArgs().Contains("--capture-echoes") && DisplayServer.GetName() != "headless")
            {
                ShowExperimentPanel(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (await WaitForExperimentDraw()) GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_output, "echoes-complete.png"));
                else GD.PushWarning("Echoes completion screenshot skipped: renderer did not produce a frame within three seconds.");
            }
            GetTree().Quit();
        }
        catch (Exception ex) { Fail(ex); }
    }
}
