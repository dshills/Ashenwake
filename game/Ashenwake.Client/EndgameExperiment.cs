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
    private ExperimentRules _experimentRules = null!;
    private ExperimentPresentation _memoryPresentation = null!;
    private PanelContainer _echoesPanel = null!;
    private EchoesBoard _echoesBoard = null!;
    private EchoesMemoryHud _memoryHud = null!;
    private bool _hasEchoesSelection;
    private (long Run, int Actor) _memorySourceKey;
    private string _memorySourceName = "";
    private string _memoryNoticeStatus = "";
    private bool _echoesSmoke, _echoesSavedBound, _echoesSavedWarning, _echoesReleasedCheck, _echoesMindPrepared, _echoesSuppressionChecked, _echoesCompletionScheduled;
    private int _echoesSteps;
    private int _echoesFrames;
    private string _echoesSaveName = "", _echoesOriginal = "";
    private readonly HashSet<string> _echoesCaptures = [];
    private string EchoesPath => Path.Combine(_output, _echoesSaveName);

    private void InitializeExperiments()
    {
        _echoesSmoke = OS.GetCmdlineUserArgs().Contains("--echoes-smoke");
        _experimentContent = ExperimentContent.Parse(FileAccess.GetFileAsString("res://experiments.json"));
        _experimentRules = _experimentContent.Capture();
        _sandbox.AutomaticStep |= _echoesSmoke;
        _memoryPresentation = new ExperimentPresentation(); AddChild(_memoryPresentation);
        var open = new Button { Name = "EchoesNavigation", Text = "Echoes: Borrowed Memory [H]", Position = new(921, 100), Size = new(326, 32) };
        open.AddThemeFontSizeOverride("font_size", 13); open.Pressed += ShowExperimentPanel; _sandbox.AddOverlay(open);
        CombatHudLayout.Navigation(open, 3);
        _memoryHud = new EchoesMemoryHud(); _sandbox.AddOverlay(_memoryHud);
        _memoryHud.GetParent().MoveChild(_memoryHud, _character.GetIndex());
        _memoryHud.BindRequested += () => Safely(() =>
        { if (!_sandbox.IsPaused && _experiment?.View.Memory is { CanBind: true } memory) ApplyExperiment(new(ExperimentAction.BindMemory, SourceActorId: memory.SourceActorId)); });
        _memoryHud.ReleaseRequested += () => Safely(() =>
        { if (!_sandbox.IsPaused && _experiment?.View.Memory is { CanRelease: true }) ApplyExperiment(new(ExperimentAction.ReleaseMemory)); });
        _memoryHud.CastRequested += () => Safely(() =>
        {
            if (_sandbox.IsPaused || _experiment?.View.Memory is not { Status: "Bound", RemainingTicks: > 0 }) return;
            int target = _session.Combat.View.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0)
                .OrderBy(a => CorePosition.DistanceSquared(a.Position, _session.Combat.View.Actors.Single(a => a.Id == 1).Position)).FirstOrDefault()?.Id ?? 0;
            Apply(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.CastEcho, TargetId: target)]));
        });
        _echoesBoard = new EchoesBoard(); _sandbox.AddOverlay(_echoesBoard); _echoesPanel = _echoesBoard.Panel;
        _echoesBoard.EntryRequested += (sigil, choice) => Safely(() => BeginExperiment(sigil, choice));
        _echoesBoard.SaveRequested += () => Safely(() => { SaveExperiment(); CloseExperimentPanel(); });
        _echoesBoard.ContinueRequested += () => Safely(() =>
        {
            if (_experiment is not null || !_session.InHub || TryEchoesSelection() is null) return;
            PlayCharacter(TryEchoesSelection()!);
        });
        _echoesBoard.ReturnRequested += () => Safely(ExitExperiment);
        _echoesBoard.OpenChanged += isOpen =>
        {
            _sandbox.SetModalPaused("echoes", isOpen);
            if (isOpen) { _collection?.SetOpen(false); _campaignHud.SetOpen(false); _board.SetOpen(false); _character.Close(); }
            RefreshExperiment();
        };
        _hasEchoesSelection = TryEchoesSelection() is not null;
        if (!InputMap.HasAction("aw_experiment")) { InputMap.AddAction("aw_experiment"); InputMap.ActionAddEvent("aw_experiment", new InputEventKey { PhysicalKeycode = Key.H }); }
    }
    private void ConfigureExperimentStart()
    {
        if (_echoesSmoke) { BeginExperimentSmoke(); return; }
        if (OS.GetCmdlineUserArgs().Contains("--echoes")) ShowExperimentPanel();
    }
    public override void _Process(double delta)
    {
        PollCatalogRefresh();
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
        _huntBoard?.SetOpen(false);
        if (_classSelection.Visible) return;
        _hasEchoesSelection = TryEchoesSelection() is not null;
        _echoesBoard.Notice(""); RefreshExperiment(); _echoesBoard.SetOpen(true);
    }
    private void CloseExperimentPanel() => _echoesBoard.SetOpen(false);
    private void BeginExperiment(long sigil, ExperimentChoice choice)
    {
        if (_session.HasUnresolvedRegionalHunt) { Notice("Claim or abandon your regional hunt before entering an Echoes expedition."); return; }
        if (!_session.InHub || !_session.View.Unlocked || !AtGate() || !_session.View.AvailableSigils.Any(s => s.Id == sigil) ||
            choice == ExperimentChoice.BorrowMind && !_experimentContent.AcceptingEntries)
        { Notice("This contract is no longer available. Review the selected Sigil and approach the Fracture gate."); RefreshExperiment(); return; }
        if (_experiment is null)
        {
            Save();
            var experiment = ExperimentRuntimeSession.FromEndgame(_combatJson, _adventure, _progression, _campaign, _endgame, _experimentContent, _session.Capture());
            string filename = "echoes." + Guid.NewGuid().ToString("N") + ".save.json";
            AtomicFile.Write(Path.Combine(_output, filename + ".origin"), _saveName);
            _echoesSaveName = filename; _echoesOriginalSaveName = _saveName; _experiment = experiment;
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
    }
    private void RefreshExperiment()
    {
        if (_memoryHud is null || _echoesBoard is null || _cachedDisplay is null) return;
        var viewport = GetViewport().GetVisibleRect().Size;
        _memoryHud.Position = new(viewport.X - 302, 378);
        _memoryHud.Size = new(280, Math.Clamp(viewport.Y - 584, 136, 180));
        var view = _experiment?.View; var memory = view?.Memory;
        var mind = _session.Combat.View.Fragments.FirstOrDefault(f => f.Equipped && f.Slot == AnatomySlot.Mind);
        var progression = _session.Production.ProgressionView;
        _echoesBoard.SetView(new(_cachedDisplay, view, _experimentRules, _experimentContent.AcceptingEntries && !_session.HasUnresolvedRegionalHunt,
            _hasEchoesSelection, mind?.Name ?? "Mind socket empty", mind?.Description ?? "No owned Mind effect is installed.",
            $"{progression.Discipline} · Level {progression.Level}", _revision, _echoesOriginalSaveName.Length > 0));
        _memoryPresentation.Show(memory);
        string source = "";
        if (memory is { SourceActorId: > 0 } && view?.Run is { } run)
        {
            var key = (run.RunId, memory.SourceActorId);
            if (_memorySourceKey != key) { _memorySourceKey = key; _memorySourceName = ""; }
            if (_session.Combat.View.Endgame?.EncounterIndex == run.SourceRoom &&
                _session.Combat.View.Actors.FirstOrDefault(a => a.Id == memory.SourceActorId) is { } actor)
                _memorySourceName = Readable(actor.DefinitionId).ToUpperInvariant();
            source = (_memorySourceName.Length > 0 ? _memorySourceName : "Elite memory") + $" · room {run.SourceRoom + 1}";
        }
        string echoKey = InputMap.ActionGetEvents("aw_echo").OfType<InputEventKey>().FirstOrDefault()?.PhysicalKeycode.ToString() ?? "Controls";
        _memoryHud.SetView(memory, _experimentRules, source, mind?.Name ?? "", echoKey, _sandbox.IsPaused);
        _memoryHud.Visible = memory is not null && !_echoesBoard.IsOpen;
        string status = memory?.Status ?? "";
        if (_memoryNoticeStatus != status)
        {
            _memoryNoticeStatus = status;
            string? notice = status switch
            {
                "Pending" => "Mind socket on loan. Defeat an elite to reveal a memory.",
                "Offered" => "An elite memory is revealed. Approach it to bind the Echo, or release the loan.",
                "Bound" => "Echo Storm bound. Cast before the memory fades, then leave the hostile Storm circle.",
                "Spent" => "Echo used; owned Mind restored. Avoid the Storm and finish the Fracture for its cosmetic record.",
                "Released" => "Memory released. Your owned anatomy is active again.",
                "Expired" => "The borrowed Echo faded. Your owned anatomy is active again.",
                "Lost" => "Death ended the borrowed memory. Your owned anatomy is active again.",
                _ => null
            };
            if (notice is not null) Notice(notice);
        }
        if (memory is not null && OS.GetCmdlineUserArgs().Contains("--capture-echoes") && DisplayServer.GetName() != "headless" && (memory.Status == "Offered" || memory.HazardStage != "None"))
        {
            string key = memory.HazardStage != "None" ? "storm-" + memory.HazardStage.ToLowerInvariant() : "memory-offered";
            if (_echoesCaptures.Add(key)) CaptureRenderedFrame("echoes-" + key + ".png");
        }
    }
    private string? TryEchoesSelection()
    {
        try
        {
            bool Matches(string name) => ClientCharacterCatalog.IsEchoesFilename(name) && OriginalForEchoes(name) == _saveName &&
                (File.Exists(Path.Combine(_output, name)) || File.Exists(Path.Combine(_output, name + ".bak")));
            string selected = ReadCharacterPointer("current-echoes.txt");
            if (Matches(selected)) return selected;
            if (!Directory.Exists(_output)) return null;
            // A different original may have been played most recently. Find this
            // character's own linked journey without adopting another hero's Echoes.
            return Directory.EnumerateFiles(_output, "echoes.*.save.json*")
                .Select(Path.GetFileName).OfType<string>().Select(name => name.EndsWith(".bak", StringComparison.Ordinal) ? name[..^4] : name)
                .Where(ClientCharacterCatalog.IsEchoesFilename).Distinct(StringComparer.Ordinal).Take(ClientCharacterCatalog.MaximumSlots)
                .Where(Matches).OrderByDescending(name => File.GetLastWriteTimeUtc(Path.Combine(_output, name))).ThenBy(name => name, StringComparer.Ordinal).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
    private bool SaveExperiment()
    {
        if (_experiment is null) return false;
        ExperimentSaveStore.Write(EchoesPath, _combatJson, _adventure, _progression, _campaign, _endgame, _experimentContent, _experiment.Capture());
        PublishCharacterSelection(_echoesSaveName); _hasEchoesSelection = true; Notice("Echoes character and contract saved separately. Your original character is preserved."); return true;
    }
    private void LoadExperiment()
    {
        string name = _experiment is not null ? _echoesSaveName : TryEchoesSelection() ?? throw new InvalidDataException("No valid Echoes save selection is available.");
        var loaded = ExperimentSaveStore.Load(Path.Combine(_output, name), _combatJson, _adventure, _progression, _campaign, _endgame, _experimentContent);
        _echoesOriginalSaveName = OriginalForEchoes(name);
        if (_echoesOriginalSaveName.Length > 0) _saveName = _echoesOriginalSaveName;
        CloseExperimentPanel(); _experiment = loaded.Session; _echoesSaveName = name; Adopt(_experiment.Endgame, true);
        Notice(loaded.RecoveredBackup ? "Recovered the previous valid Echoes character and contract." : "Echoes character, choices and contract loaded.");
    }
    private void ExitExperiment()
    {
        if (!_session.InHub) throw new InvalidDataException("Return to Greyhaven before switching characters.");
        if (_echoesOriginalSaveName.Length == 0) throw new InvalidDataException("This Echoes archive has no linked original. Use Save & main menu to choose another character.");
        var original = EndgameRuntimeSaveStore.Load(Path.Combine(_output, _echoesOriginalSaveName), _combatJson, _adventure, _progression, _campaign, _endgame).Session;
        SaveExperiment(); PublishCharacterSelection(_echoesOriginalSaveName); _saveName = _echoesOriginalSaveName;
        CloseExperimentPanel(); Adopt(original);
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
        _echoesSaveName = "echoes.validation.save.json"; _echoesOriginalSaveName = _saveName;
        AtomicFile.Write(Path.Combine(_output, _echoesSaveName + ".origin"), _saveName);
        _session = _experiment.Endgame; _sandbox.AutomaticStep = true; _sandbox.SetPaused(false);
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
