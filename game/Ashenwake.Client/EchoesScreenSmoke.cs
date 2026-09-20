using System.Reflection;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Experiments;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Shipping Echoes controls, actual earned memories, isolated archives and exact command replays.</summary>
public partial class EchoesScreenSmoke : Node3D
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private readonly List<ReplayEvidence> _replays = [];
    private readonly List<object> _nativeEvidence = [];
    private readonly Dictionary<string, int> _recordedFrames = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private EchoesBoard _board = null!;
    private EchoesMemoryHud _memoryHud = null!;
    private string _output = "", _fixture = "", _original = "", _originalPath = "", _originalPointer = "";
    private bool _writeReport;
    private int _commands, _nativeActions, _epoch, _segmentCommands, _nativeModalClosures;
    private EndgameRuntimeSession Session => Field<EndgameRuntimeSession>(_director, "_session");
    private ExperimentRuntimeSession? Experiment => OptionalField<ExperimentRuntimeSession>(_director, "_experiment");
    private ExperimentRuntimeSession Active => Experiment ?? throw new InvalidDataException("The shipping Echoes character is not active.");
    private CorePosition Player => Session.Combat.View.Actors.Single(a => a.Id == 1).Position;
    private string CombatJson => Field<string>(_director, "_combatJson");
    private AdventureContent Adventure => Field<AdventureContent>(_director, "_adventure");
    private ProgressionContent Progression => Field<ProgressionContent>(_director, "_progression");
    private CampaignContent Campaign => Field<CampaignContent>(_director, "_campaign");
    private EndgameContent Endgame => Field<EndgameContent>(_director, "_endgame");
    private ExperimentContent Content => Field<ExperimentContent>(_director, "_experimentContent");
    private string EchoesPath => Path.Combine(_output, Field<string>(_director, "_echoesSaveName"));

    public override async void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--echoes-screen-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Echoes screen smoke requires --echoes-screen-smoke --discipline=Vanguard --output=<fresh-artifact-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            Resize(1280, 800);
            _director = new EndgameDirector(); AddChild(_director); _director.SetProcess(false);
            _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false);
            // AutomaticStep suppresses desktop focus interruptions only in this bounded diagnostic.
            // Processing remains disabled; explicit fixed-step calls below happen only under a modal.
            _sandbox.AutomaticStep = true;
            _board = Field<EchoesBoard>(_director, "_echoesBoard");
            _memoryHud = Field<EchoesMemoryHud>(_director, "_memoryHud");
            CloseOtherPanels();
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            await Frames(8);
            await AdmissionAndReadOnlyViews();
            await PrepareEarnedCharacter();
            await KeepMindBranch();
            await BorrowMemoryBranches();
            await CompletedCharacter();
            Check("every_observed_gameplay_command_has_verified_replay_coverage", _replays.Sum(r => r.NewCommands) == _commands);
            Check("fixture_import_source_is_unchanged", System.IO.File.ReadAllText(Path.Combine(_output, "phase4-source.json")) == _fixture && Godot.FileAccess.GetFileAsString("res://phase4-campaign-complete.json") == _fixture);
            AssertOriginal("final_original_archive_and_selection_are_preserved");
            Finish(true, "");
        }
        catch (Exception ex)
        {
            try { await Capture("echoes-screen-failure.png"); } catch (Exception captureError) { GD.PushError(captureError.Message); }
            GD.PushError(ex.ToString()); Finish(false, ex.Message);
        }
    }

    private async Task AdmissionAndReadOnlyViews()
    {
        string hash = Session.StateHash; int frames = Session.CaptureReplay().Frames.Length;
        await KeyPress(Key.H);
        Check("shipping_h_opens_locked_echoes_and_pauses_world", _board.IsOpen && _sandbox.IsPaused);
        Check("fresh_campaign_cannot_enter_either_contract", !Session.View.Unlocked && Button("EchoesKeep").Disabled && Button("EchoesBorrow").Disabled);
        await Click("EchoesKeep"); await Click("EchoesBorrow");
        Check("disabled_entry_never_creates_a_character_or_spends", Experiment is null && Session.StateHash == hash && Session.CaptureReplay().Frames.Length == frames);
        foreach (var layout in new[] { (1280, 800), (1000, 720), (780, 720) })
        {
            Resize(layout.Item1, layout.Item2); await Frames(4); Refresh();
            foreach (string tab in new[] { "Contract", "Record", "Character" })
            {
                await Click("EchoesTab" + tab);
                AssertBoardBounds(layout.Item1 + "x" + layout.Item2 + "_" + tab);
                await Capture("echoes-locked-" + tab.ToLowerInvariant() + "-" + layout.Item1 + ".png");
            }
        }
        for (int i = 0; i < 12; i++) _sandbox._Process(FixedStepClock.SecondsPerTick);
        Check("locked_board_navigation_and_fixed_ticks_are_read_only", Session.StateHash == hash && Session.CaptureReplay().Frames.Length == frames);
        _sandbox.SetPaused(true);
        await Click("EchoesClose");
        Check("closing_echoes_preserves_independent_manual_pause", !_board.IsOpen && _sandbox.IsPaused);
        _sandbox.SetPaused(false); Refresh();
        Check("closing_last_pause_owner_resumes_play", !_sandbox.IsPaused);
        Resize(1280, 800); await Frames();
    }

    private async Task PrepareEarnedCharacter()
    {
        _fixture = Godot.FileAccess.GetFileAsString("res://phase4-campaign-complete.json");
        string source = Path.Combine(_output, "phase4-source.json"); System.IO.File.WriteAllText(source, _fixture);
        Call(_director, "Import", source); StartReplayEpoch(); CloseOtherPanels();
        Check("maintained_campaign_fixture_imports_without_any_sigil_grant", Session.InHub && Session.View.Unlocked && Session.View.AvailableSigils.Length == 0);
        await Open("Contract");
        Check("unlocked_character_without_a_sigil_cannot_enter", Button("EchoesKeep").Disabled && Button("EchoesBorrow").Disabled);
        await Click("EchoesClose");

        var mara = Session.Interactions.Single(i => i.ActionId == "npc.mara");
        await WalkTo(mara.Position, mara.Range);
        Ordinary(new(EndgameRuntimeAction.Production, Production: new(ProductionAction.Expedition,
            new(ExpeditionAction.InstallFragment, "Mind", "fragment.last_memory"))));
        Check("mind_fixture_fragment_is_installed_through_actual_mara_service", Session.Production.Capture().Expedition.Adventure.Anatomy["Mind"] == "fragment.last_memory");

        // The reference policy spends the imported character's actual points, chooses earned mutations,
        // approaches the published gate and claims its real recovery Sigil. Stop before entering.
        bool ready = false;
        for (int i = 0; i < 1600; i++)
        {
            var command = EndgameRuntimeSmoke.Next(Session);
            if (command.Action == EndgameRuntimeAction.StartFracture) { ready = true; break; }
            Ordinary(command); if (i % 120 == 0) await Frames();
        }
        Check("real_recovery_sigil_and_gate_prepare_contract_entry", ready && Session.View.AvailableSigils.Length > 0 && AtGate());
        long sigil = Session.View.AvailableSigils.First().Id;
        await Open("Contract"); string hash = Session.StateHash; int frameCount = Session.CaptureReplay().Frames.Length;
        await Click("EchoesSigil" + sigil);
        _board.SelectSigil(sigil); _board.ShowTab("Record"); _board.ShowTab("Contract"); await Frames();
        Check("sigil_card_and_tabs_only_preview_existing_authoritative_state", Session.StateHash == hash && Session.CaptureReplay().Frames.Length == frameCount && Experiment is null);
        Check("at_gate_existing_sigil_enables_both_explicit_choices", !Button("EchoesKeep").Disabled && !Button("EchoesBorrow").Disabled);
        await RetiredAdmission();
        await Capture("echoes-contract-ready.png");
        await Click("EchoesClose");

        await WalkTo(mara.Position, mara.Range); await Open("Contract");
        Check("owned_sigil_away_from_gate_still_cannot_enter", !AtGate() && Button("EchoesKeep").Disabled && Button("EchoesBorrow").Disabled);
        await Capture("echoes-gate-required.png"); await Click("EchoesClose");
        var gate = Session.Interactions.Single(i => i.ActionId == "endgame.gate"); await WalkTo(gate.Position, gate.Range);
        Ordinary(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)]));
        RecordReplay("prepared-original");
        await KeyPress(Key.F5);
        _originalPath = Path.Combine(_output, Field<string>(_director, "_saveName"));
        _original = System.IO.File.ReadAllText(_originalPath);
        _originalPointer = System.IO.File.ReadAllText(Path.Combine(_output, "current-save.txt"));
        Check("native_save_records_the_prepared_original_character", EndgameRuntimeSaveStore.Load(_originalPath, CombatJson, Adventure, Progression, Campaign, Endgame).Session.StateHash == Session.StateHash);
    }

    private async Task KeepMindBranch()
    {
        long sigil = Session.View.AvailableSigils.First().Id; string originalHash = Session.StateHash;
        await Open("Contract"); await Click("EchoesSigil" + sigil); StartReplayEpoch();
        await GameplayClick("EchoesKeep");
        Check("native_keep_enters_once_with_owned_mind_and_no_borrowed_memory", !_board.IsOpen && !Active.InHub && Active.View.Memory is null &&
            Active.View.Entries.Single() is { Choice: ExperimentChoice.KeepMind, Outcome: "Active" } &&
            !Session.View.AvailableSigils.Any(s => s.Id == sigil) && Session.Combat.Capture().Fragments["Mind"] == "fragment.last_memory");
        await ResumeExpedition();
        AssertOriginal("keep_branch_preserves_original_archive");
        string keepPath = EchoesPath;
        Check("keep_branch_is_saved_in_its_own_echoes_archive", System.IO.File.Exists(keepPath) && Path.GetFullPath(keepPath) != Path.GetFullPath(_originalPath));
        await Open("Character");
        Check("active_fracture_cannot_return_to_original_early", Button("EchoesReturn").Disabled);
        await Capture("echoes-keep-character.png"); await Click("EchoesClose");
        Ordinary(new(EndgameRuntimeAction.Abandon));
        Check("actual_keep_abandon_records_outcome_without_cosmetic", Active.InHub && Active.View.Cosmetics.Count == 0 && Active.View.Entries.Single().Outcome == "Abandoned");
        RecordReplay("keep-abandoned");
        await Open("Record"); await Capture("echoes-keep-record.png");
        await Click("EchoesTabCharacter"); await Click("EchoesReturn"); StartReplayEpoch();
        Check("native_return_restores_original_and_keeps_separate_history", Experiment is null && Session.StateHash == originalHash &&
            ExperimentSaveStore.Load(keepPath, CombatJson, Adventure, Progression, Campaign, Endgame, Content).Session.View.Entries.Single().Choice == ExperimentChoice.KeepMind);
        AssertOriginal("return_from_keep_preserves_original_archive");
    }

    private async Task BorrowMemoryBranches()
    {
        long sigil = Session.View.AvailableSigils.First().Id;
        string anatomy = JsonData.Write(Session.Production.Capture().Expedition.Adventure.Anatomy);
        int resonance = Session.Combat.View.Resonance;
        await Open("Contract"); await Click("EchoesSigil" + sigil); StartReplayEpoch();
        await GameplayClick("EchoesBorrow");
        Check("native_borrow_consumes_one_sigil_and_suppresses_only_its_owned_mind_effect", !Active.InHub && Active.View.Entries.Single().Choice == ExperimentChoice.BorrowMind &&
            Active.View.Memory is { SuppressedMindId: "fragment.last_memory" } && Session.Combat.View.Resonance == resonance &&
            JsonData.Write(Session.Production.Capture().Expedition.Adventure.Anatomy) == anatomy && !Session.View.AvailableSigils.Any(s => s.Id == sigil));
        await ResumeExpedition();
        await Until(s => s.View.Memory?.Status == "Offered", "an elite reveals its actual memory");
        Check("memory_hud_projects_actual_offered_source_and_bind_availability", _memoryHud.MemoryStatus == "Offered" &&
            _memoryHud.BindEnabled == Active.View.Memory!.CanBind && Find<SkillIcon>("EchoesMemoryIcon").IsVisibleInTree());
        await Capture("echoes-memory-offered.png");

        // Clear this room using ordinary combat before binding. This makes the expiry branch prove
        // the actual timer, rather than letting enemies kill the character during a waiting test.
        for (int i = 0; !Session.EncounterCleared && i < 3000; i++)
        {
            Step(new(ExperimentAction.Tick, Commands: EndgameCombatSmoke.Commands(Session.Combat.View, Session.Room)));
            if (i % 120 == 0) await Frames();
        }
        Check("earned_elite_memory_survives_real_room_clear", Session.EncounterCleared && Active.View.Memory?.Status == "Offered");
        await Until(s => s.View.Memory?.CanBind == true, "reach the offered memory", 1000);
        await GameplayClick("EchoesBind");
        Check("native_bind_creates_one_real_echo_with_exact_timer", Active.View.Memory is { Status: "Bound", EchoSkillId: "skill.echo_storm" } memory &&
            memory.RemainingTicks == Content.Capture().EchoLifetimeTicks && _memoryHud.RemainingTicks == memory.RemainingTicks);
        var bound = Active.Capture(); string boundHash = Active.StateHash;
        await AssertMemoryLayouts("bound");
        await Open("Contract"); int remaining = Active.View.Memory!.RemainingTicks; string pauseHash = Active.StateHash;
        for (int i = 0; i < 30; i++) _sandbox._Process(FixedStepClock.SecondsPerTick);
        Check("echoes_board_pauses_actual_bound_memory_timer", Active.StateHash == pauseHash && Active.View.Memory!.RemainingTicks == remaining && _sandbox.IsPaused);
        await Click("EchoesTabCharacter"); await Click("EchoesSave");
        Check("native_echoes_save_closes_board_and_persists_exact_bound_memory", !_board.IsOpen &&
            ExperimentSaveStore.Load(EchoesPath, CombatJson, Adventure, Progression, Campaign, Endgame, Content).Session.StateHash == boundHash);
        RecordReplay("bound-checkpoint");
        await GameplayClick("EchoesRelease");
        Check("native_release_restores_owned_mind_without_cast_or_reward", Active.View.Memory is { Status: "Released", SuppressedMindId: "" } &&
            Session.Combat.View.CapturedSkillId == "" && Active.View.Cosmetics.Count == 0 && JsonData.Write(Session.Production.Capture().Expedition.Adventure.Anatomy) == anatomy);
        string released = Active.StateHash; Find<Button>("EchoesRelease").EmitSignal(BaseButton.SignalName.Pressed);
        Check("released_memory_cannot_submit_release_twice", Active.StateHash == released);
        await Capture("echoes-memory-released.png"); RecordReplay("release-branch");
        await RestoreBound(boundHash, "released_branch_native_load_restores_bound_save");

        CorePosition refuge = SafeRefuge();
        for (int i = 0; Active.View.Memory?.Status == "Bound" && i < Content.Capture().EchoLifetimeTicks + 1; i++)
        {
            var commands = new List<CombatCommand>(); var actor = Session.Combat.View.Actors.Single(a => a.Id == 1);
            if (actor.Health < actor.MaxHealth / 2 && Session.Combat.View.PotionCharges > 0 && Session.Combat.View.PotionCooldownTicks == 0) commands.Add(new(CombatCommandKind.Potion));
            if (CorePosition.DistanceSquared(Player, refuge) <= 400L * 400) commands.Add(new(CombatCommandKind.Stop));
            else { var move = CombatProductionSmoke.MovementDirection(Player, refuge, Session.Room); commands.Add(new(CombatCommandKind.Move, X: move.X, Z: move.Z)); }
            Step(new(ExperimentAction.Tick, Commands: commands.ToArray())); if (i % 120 == 0) await Frames();
        }
        Check("ordinary_combat_ticks_expire_the_echo_and_restore_mind", Active.View.Memory is { Status: "Expired", SuppressedMindId: "", RemainingTicks: 0 } &&
            Session.Combat.View.Actors.Single(a => a.Id == 1).Health > 0 && Session.Combat.View.CapturedSkillId == "" && _memoryHud.StateText.Contains("FADED", StringComparison.Ordinal));
        Check("expiry_never_grants_a_cosmetic_or_changes_permanent_anatomy", Active.View.Cosmetics.Count == 0 && JsonData.Write(Session.Production.Capture().Expedition.Adventure.Anatomy) == anatomy);
        await Capture("echoes-memory-expired.png"); RecordReplay("expiry-branch");
        await RestoreBound(boundHash, "expiry_branch_native_load_restores_bound_save");
        Check("bound_checkpoint_payload_remains_unedited", JsonData.Write(Active.Capture()) == JsonData.Write(bound));

        bool castReady = false;
        for (int i = 0; i < 300; i++)
        {
            var next = ExperimentRuntimeSmoke.Next(Active);
            if (next.Commands?.Any(c => c.Kind == CombatCommandKind.CastEcho) == true) { castReady = true; break; }
            Step(next); if (i % 60 == 0) await Frames();
        }
        Check("actual_next_room_supplies_a_target_before_echo_expiry", castReady && Active.View.Memory?.Status == "Bound");
        await GameplayClick("EchoesCast");
        await Until(s => s.View.Memory?.HazardStage == "Warning", "native Echo cast creates the Storm warning", 30);
        Check("native_echo_cast_uses_real_action_and_hostile_storm", Active.View.Run!.EchoActionId > 0 && Active.View.Memory is { Status: "Spent", SuppressedMindId: "", HazardStage: "Warning" } &&
            _memoryHud.HazardText.Contains("HOSTILE STORM", StringComparison.Ordinal) && _memoryHud.HazardRemainingTicks == Active.View.Memory.HazardRemainingTicks);
        string spent = Active.StateHash; Find<Button>("EchoesCast").EmitSignal(BaseButton.SignalName.Pressed);
        Check("spent_echo_cannot_submit_cast_twice", Active.StateHash == spent);
        await AssertMemoryLayouts("storm-warning");
        string warningHash = Active.StateHash; var warning = Active.View.Memory;
        await Open("Character"); await Click("EchoesSave"); RecordReplay("storm-warning");
        await KeyPress(Key.F9); StartReplayEpoch(); CloseOtherPanels(); Refresh();
        Check("native_load_restores_exact_storm_and_scoped_action", Active.StateHash == warningHash && Active.View.Memory == warning && !_board.IsOpen);
        await Until(s => s.View.Memory?.HazardStage == "Active", "real warning becomes hostile Storm", Content.Capture().WarningTicks + 5);
        Check("active_storm_hud_projects_actual_danger_stage", _memoryHud.HazardStage == "Active" && _memoryHud.HazardText.Contains("ACTIVE", StringComparison.Ordinal));
        await Capture("echoes-storm-active.png");
        await Until(ExperimentRuntimeSmoke.Complete, "finish the earned Fracture and return to Greyhaven", 6500);
        RecordReplay("borrow-completed");
        Check("actual_completion_records_one_borrow_choice_and_one_cosmetic_receipt", Active.View.Run?.Status == "Completed" &&
            Active.View.Entries.Single() is { Choice: ExperimentChoice.BorrowMind, Outcome: "Completed" } && Active.Capture().Cosmetics.Count == 1 &&
            Active.Capture().Cosmetics.Values.Single().EchoActionId > 0 && JsonData.Write(Session.Production.Capture().Expedition.Adventure.Anatomy) == anatomy);
        AssertOriginal("completed_borrow_branch_preserves_original_archive");
    }

    private async Task CompletedCharacter()
    {
        string completedHash = Active.StateHash; string path = EchoesPath;
        var entry = Active.View.Entries.Single(); var receipt = Active.Capture().Cosmetics.Values.Single();
        await Open("Record");
        string text = VisibleText(_board);
        Check("record_board_displays_the_earned_completion_and_cosmetic", text.Contains("Completed", StringComparison.OrdinalIgnoreCase) && text.Contains("Borrow", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("cosmetic", StringComparison.OrdinalIgnoreCase));
        foreach (var layout in new[] { (1280, 800), (780, 720) })
        {
            Resize(layout.Item1, layout.Item2); await Frames(4); Refresh(); AssertBoardBounds("earned_record_" + layout.Item1);
            await Capture("echoes-earned-record-" + layout.Item1 + ".png");
        }
        await Click("EchoesTabCharacter"); await Capture("echoes-separate-character.png");
        Check("completed_character_offers_save_and_safe_return", !Button("EchoesSave").Disabled && !Button("EchoesReturn").Disabled);
        await Click("EchoesReturn"); StartReplayEpoch();
        Check("native_safe_return_preserves_both_original_and_completed_echoes", Experiment is null && Session.InHub &&
            ExperimentSaveStore.Load(path, CombatJson, Adventure, Progression, Campaign, Endgame, Content).Session.StateHash == completedHash);
        AssertOriginal("completed_character_return_keeps_original_bytes");
        await Open("Character");
        Check("ordinary_character_offers_its_saved_echoes_continuation", Button("EchoesContinue").IsVisibleInTree() && !Button("EchoesContinue").Disabled);
        await Capture("echoes-continue-character.png");
        await FailedCharacterSwitch();
        await Click("EchoesContinue"); StartReplayEpoch();
        Check("native_continue_loads_exact_completed_character_without_duplicate_rewards", !_board.IsOpen && Active.StateHash == completedHash &&
            Active.View.Entries.Single() == entry && Active.Capture().Cosmetics.Values.Single() == receipt);
        await Open("Record"); await Click("EchoesTabContract"); await Click("EchoesTabRecord");
        Check("reopening_completed_records_is_read_only", Active.StateHash == completedHash && Active.CaptureReplay().Frames.Length == 0);
        await Click("EchoesClose");
    }

    private async Task FailedCharacterSwitch()
    {
        // Keep the actual character identity and its Echoes link. Preserve the original
        // bytes in a sibling while a directory blocks publication to this same slot.
        string blocked = Path.Combine(_output, Field<string>(_director, "_saveName"));
        string held = blocked + ".held";
        if (!System.IO.File.Exists(blocked + ".bak")) System.IO.File.Copy(blocked, blocked + ".bak");
        System.IO.File.Move(blocked, held); Directory.CreateDirectory(blocked);
        string hash = Session.StateHash; string pointer = System.IO.File.ReadAllText(Path.Combine(_output, "current-save.txt"));
        string notice = Find<Label>("EchoesNotice").Text;
        try
        {
            await Click("EchoesContinue");
            Check("failed_original_save_cannot_switch_or_lose_current_character", Experiment is null && Session.StateHash == hash && _board.IsOpen &&
                System.IO.File.ReadAllText(Path.Combine(_output, "current-save.txt")) == pointer);
            var error = Find<Label>("EchoesNotice");
            Check("failed_character_switch_displays_error_inside_current_modal", error.IsVisibleInTree() && error.Text.Length > 0 && error.Text != notice);
            await Capture("echoes-switch-save-failure.png");
        }
        finally { Directory.Delete(blocked); System.IO.File.Move(held, blocked); }
        AssertOriginal("failed_character_switch_leaves_original_archive_intact");
    }

    private async Task RestoreBound(string hash, string check)
    {
        await KeyPress(Key.F9); StartReplayEpoch(); CloseOtherPanels(); Refresh();
        Check(check, Active.StateHash == hash && Active.View.Memory?.Status == "Bound" && !_board.IsOpen && !_sandbox.IsPaused);
    }

    private CorePosition SafeRefuge()
    {
        var hazards = Session.Combat.View.Endgame?.Hazards ?? [];
        var space = new SpatialWorld(Session.Room);
        var points = new List<CorePosition> { Session.Room.PlayerSpawn };
        for (int x = -Session.Room.HalfWidth + 1800; x < Session.Room.HalfWidth - 1800; x += 2200)
            for (int z = -Session.Room.HalfDepth + 1800; z < Session.Room.HalfDepth - 1800; z += 2200) points.Add(new(x, z));
        return points.Where(p => space.CanOccupy(p, CombatSession.ActorRadius) && hazards.All(h =>
                !CombatSession.HazardContains(new(h.Id, h.Kind, h.Position, h.End, h.Radius + 600, h.RemainingTicks, h.ContentId, h.SourceId), p)))
            .OrderBy(p => CorePosition.DistanceSquared(p, Player)).First();
    }

    private async Task AssertMemoryLayouts(string state)
    {
        foreach (var layout in new[] { (1280, 800), (1000, 720), (780, 720) })
        {
            Resize(layout.Item1, layout.Item2); await Frames(4); Refresh();
            string key = state + "_" + layout.Item1;
            var viewport = GetViewport().GetVisibleRect(); var box = _memoryHud.GetGlobalRect();
            Check(key + "_memory_hud_is_on_screen", _memoryHud.IsVisibleInTree() && Encloses(viewport, box));
            Check(key + "_memory_hud_does_not_cover_combat_dock", !box.Intersects(Find<Control>("HudDock").GetGlobalRect()));
            foreach (string name in new[] { "EchoesMemoryState", "EchoesMemoryMind", "EchoesMemoryNotice", "EchoesMemoryTimer", "EchoesBind", "EchoesCast", "EchoesRelease" })
            {
                var control = Find<Control>(name);
                if (control.IsVisibleInTree()) Check(key + "_contains_" + name, Encloses(box, control.GetGlobalRect()));
            }
            Check(key + "_timer_matches_core_projection", _memoryHud.RemainingTicks == Active.View.Memory!.RemainingTicks &&
                _memoryHud.HazardRemainingTicks == Active.View.Memory.HazardRemainingTicks);
            await Capture("echoes-" + state + "-" + layout.Item1 + ".png");
        }
        Resize(1280, 800); await Frames(); Refresh();
    }

    private void AssertBoardBounds(string key)
    {
        var panel = Find<Control>("EchoesPanel").GetGlobalRect();
        if (!Encloses(GetViewport().GetVisibleRect(), panel)) GD.Print("Echoes diagnostic bounds: panel=", panel, " viewport=", GetViewport().GetVisibleRect());
        Check(key + "_board_fits_viewport", Encloses(GetViewport().GetVisibleRect(), panel));
        var tabs = new[] { "EchoesTabContract", "EchoesTabRecord", "EchoesTabCharacter", "EchoesClose" }.Select(Find<Button>).ToArray();
        Check(key + "_navigation_controls_fit", tabs.All(t => t.IsVisibleInTree() && Encloses(panel, t.GetGlobalRect())));
        Check(key + "_tabs_do_not_overlap", !tabs[0].GetGlobalRect().Intersects(tabs[1].GetGlobalRect()) && !tabs[1].GetGlobalRect().Intersects(tabs[2].GetGlobalRect()));
        foreach (string action in new[] { "EchoesKeep", "EchoesBorrow", "EchoesSave", "EchoesReturn", "EchoesContinue" })
        {
            var control = Descendants(_board).OfType<Button>().FirstOrDefault(b => b.Name == action);
            if (control?.IsVisibleInTree() == true)
            {
                // Contract actions legitimately scroll below the visible window; their horizontal
                // bounds must still fit, and Click/Reveal verifies native reachability separately.
                var rect = control.GetGlobalRect();
                Check(key + "_contains_width_" + action, rect.Position.X >= panel.Position.X - 2 && rect.End.X <= panel.End.X + 2);
            }
        }
    }

    private async Task WalkTo(CorePosition target, int range)
    {
        for (int i = 0; CorePosition.DistanceSquared(Player, target) > (long)(range - 100) * (range - 100) && i < 1000; i++)
        {
            var move = CombatProductionSmoke.MovementDirection(Player, target, Session.Room);
            Ordinary(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: move.X, Z: move.Z)]));
            if (i % 120 == 0) await Frames();
        }
        if (CorePosition.DistanceSquared(Player, target) > (long)range * range) throw new InvalidDataException("The real interaction approach did not converge.");
    }

    private async Task Until(Func<ExperimentRuntimeSession, bool> done, string purpose, int maximum = 4500)
    {
        for (int i = 0; !done(Active) && i < maximum; i++)
        {
            if (Field<EndgameHud>(_director, "_board").IsOpen) await ResumeExpedition();
            Step(ExperimentRuntimeSmoke.Next(Active));
            if (i % 120 == 0) await Frames();
        }
        if (!done(Active)) throw new InvalidDataException("Ordinary Echoes policy could not " + purpose + ": " + JsonData.Write(Active.View));
    }

    private void Ordinary(EndgameRuntimeCommand command)
    {
        RecordAtCapacity(); long? sequence = Experiment?.Capture().OperationSequence; string before = Session.StateHash;
        Call(_director, "Apply", command); _commands++; _segmentCommands++;
        if (sequence.HasValue ? Active.Capture().OperationSequence != sequence + 1 : Session.StateHash == before)
            throw new InvalidDataException("Shipping ordinary command did not succeed: " + command);
    }

    private void Step(ExperimentCommand command)
    {
        RecordAtCapacity(); long sequence = Active.Capture().OperationSequence;
        Call(_director, "ApplyExperiment", command); _commands++; _segmentCommands++;
        if (Active.Capture().OperationSequence != sequence + 1) throw new InvalidDataException("Shipping Echoes command did not succeed: " + command);
    }

    private void RecordAtCapacity()
    {
        if (_segmentCommands < 1800) return;
        RecordReplay("rolling-boundary"); _segmentCommands = 0;
    }
    private void StartReplayEpoch() { _epoch++; _segmentCommands = 0; }

    private void RecordReplay(string scope)
    {
        string filename = "echoes-screen-" + (_replays.Count + 1) + "-" + scope + ".json";
        if (Experiment is { } experiment)
        {
            var replay = experiment.CaptureReplay(); string key = _epoch + ":echoes:" + JsonData.Hash(replay.Initial);
            int added = replay.Frames.Length - _recordedFrames.GetValueOrDefault(key);
            if (added <= 0) return;
            var result = ExperimentReplayRunner.Run(CombatJson, Adventure, Progression, Campaign, Endgame, Content, replay);
            Check("replay_" + (_replays.Count + 1) + "_" + scope, result.Success && result.FinalHash == experiment.StateHash);
            System.IO.File.WriteAllText(Path.Combine(_output, filename), JsonData.Write(replay));
            _replays.Add(new(filename, "Echoes", replay.Frames.Length, added, result.FinalHash)); _recordedFrames[key] = replay.Frames.Length;
        }
        else
        {
            var replay = Session.CaptureReplay(); string key = _epoch + ":ordinary:" + JsonData.Hash(replay.Initial);
            int added = replay.Frames.Length - _recordedFrames.GetValueOrDefault(key);
            if (added <= 0) return;
            var result = EndgameRuntimeReplayRunner.Run(CombatJson, Adventure, Progression, Campaign, Endgame, replay);
            Check("replay_" + (_replays.Count + 1) + "_" + scope, result.Success && result.FinalHash == Session.StateHash);
            System.IO.File.WriteAllText(Path.Combine(_output, filename), JsonData.Write(replay));
            _replays.Add(new(filename, "Endgame", replay.Frames.Length, added, result.FinalHash)); _recordedFrames[key] = replay.Frames.Length;
        }
    }

    private async Task GameplayClick(string name)
    {
        RecordAtCapacity(); long sequence = Experiment?.Capture().OperationSequence ?? 0;
        var button = Button(name); bool disabled = button.Disabled, visible = button.IsVisibleInTree(), paused = _sandbox.IsPaused;
        string[] pauseOwners = Field<HashSet<string>>(_sandbox, "_modalPauses").Order().ToArray();
        var memory = Experiment?.View.Memory;
        await Click(name); _commands++; _nativeActions++; _segmentCommands++;
        _nativeEvidence.Add(new
        {
            control = name,
            disabled,
            visible,
            paused,
            pauseOwners,
            memory,
            hovered = GetViewport().GuiGetHoveredControl()?.GetPath().ToString(),
            expeditionOpen = Field<EndgameHud>(_director, "_board").IsOpen,
            echoesOpen = _board.IsOpen,
            sequenceBefore = sequence,
            sequenceAfter = Experiment?.Capture().OperationSequence,
            memoryHud = new { _memoryHud.MemoryStatus, _memoryHud.BindEnabled, _memoryHud.CastEnabled, _memoryHud.ReleaseEnabled }
        });
        Check("native_" + name + "_submits_exactly_one_gameplay_command", Active.Capture().OperationSequence == sequence + 1);
    }

    private async Task ResumeExpedition()
    {
        // Entering, dying or completing an expedition intentionally presents its real Run board.
        // Dismiss that actual pause owner through its own viewport control before combat input.
        var expedition = Field<EndgameHud>(_director, "_board");
        if (expedition.IsOpen) { await Click("ExpeditionClose"); _nativeModalClosures++; }
        Refresh();
        Check("native_expedition_close_" + _nativeModalClosures + "_releases_only_its_pause", !expedition.IsOpen && !_sandbox.IsPaused);
    }

    private async Task RetiredAdmission()
    {
        var content = Content; string hash = Session.StateHash; int frames = Session.CaptureReplay().Frames.Length;
        try
        {
            // Use the catalog's supported admission change; its immutable rules identity stays intact.
            SetField(_director, "_experimentContent", content.WithAdmission("Retired")); Refresh(); await Frames();
            Check("retired_contract_keeps_ordinary_entry_but_disables_new_borrowing", !Button("EchoesKeep").Disabled && Button("EchoesBorrow").Disabled);
            await Click("EchoesBorrow");
            Check("disabled_retired_borrow_cannot_fork_or_consume_sigil", Experiment is null && Session.StateHash == hash && Session.CaptureReplay().Frames.Length == frames);
        }
        finally { SetField(_director, "_experimentContent", content); Refresh(); }
        Check("restored_admission_reenables_borrow_without_gameplay_change", !Button("EchoesBorrow").Disabled && Session.StateHash == hash);
    }

    private async Task Open(string tab)
    {
        if (!_board.IsOpen) await KeyPress(Key.H);
        if (!_board.IsOpen) throw new InvalidDataException("Shipping H did not open the Echoes board.");
        await Click("EchoesTab" + tab);
    }
    private bool AtGate()
    {
        var gate = Session.Interactions.Single(i => i.ActionId == "endgame.gate");
        return CorePosition.DistanceSquared(Player, gate.Position) <= (long)gate.Range * gate.Range;
    }
    private void AssertOriginal(string name) => Check(name, System.IO.File.ReadAllText(_originalPath) == _original &&
        System.IO.File.ReadAllText(Path.Combine(_output, "current-save.txt")) == _originalPointer);
    private void CloseOtherPanels()
    {
        Field<CampaignHud>(_director, "_campaignHud").SetOpen(false);
        Field<EndgameHud>(_director, "_board").SetOpen(false);
        Field<ProductionHud>(_director, "_character").Close();
    }
    private void Refresh() { _sandbox.AdoptSession(Session.Combat); Call(_director, "Refresh"); }
    private void Resize(int width, int height) { GetWindow().Size = new(width, height); GetWindow().ContentScaleSize = new(width, height); }
    private static bool Encloses(Rect2 outer, Rect2 inner) => outer.Grow(2).Encloses(inner);
    private static T Field<T>(object owner, string name) where T : class => OptionalField<T>(owner, name) ?? throw new MissingFieldException(name);
    private static T? OptionalField<T>(object owner, string name) where T : class => (T?)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner);
    private static void SetField(object owner, string name, object value) =>
        (owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException(name)).SetValue(owner, value);
    private static object? Call(object owner, string name, params object[] args) =>
        (owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(name)).Invoke(owner, args);
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
    private T Find<T>(string name) where T : Node => Descendants(_director).OfType<T>().Single(n => n.Name == name);
    private Button Button(string name) => Find<Button>(name);
    private static string VisibleText(Node root) => string.Join("\n", Descendants(root).OfType<Label>().Where(l => l.IsVisibleInTree()).Select(l => l.Text));

    private async Task Click(string name)
    {
        var control = Find<Control>(name); await Reveal(control);
        if (!control.IsVisibleInTree()) throw new InvalidDataException("Requested native control is hidden: " + name);
        var point = control.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = point, Pressed = pressed }, true);
        await Frames();
    }
    private async Task Reveal(Control control)
    {
        for (Node? ancestor = control.GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
        {
            if (ancestor is not ScrollContainer scroll) continue;
            scroll.EnsureControlVisible(control); await Frames();
            for (int i = 0; i < 60 && !scroll.GetGlobalRect().Encloses(control.GetGlobalRect()); i++)
            {
                bool down = control.GetGlobalRect().End.Y > scroll.GetGlobalRect().End.Y;
                var point = scroll.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = down ? MouseButton.WheelDown : MouseButton.WheelUp, Position = point, Pressed = true }, true);
                await Frames();
            }
        }
    }
    private async Task KeyPress(Key key)
    {
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
        await Frames();
    }
    private async Task Frames(int count = 2)
    { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Capture(string filename)
    {
        if (!_writeReport || !OS.GetCmdlineUserArgs().Contains("--capture-echoes-screen") || DisplayServer.GetName() == "headless") return;
        await Frames(3); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + filename, image.GetWidth() > 0 && image.SavePng(Path.Combine(_output, filename)) == Error.Ok); _captures.Add(filename);
    }
    private void Check(string name, bool passed)
    { _checks[name] = passed; if (!passed) throw new InvalidDataException("Echoes screen check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "EchoesScreenClientSmokePassed" : "EchoesScreenClientSmokeFailed",
            passed,
            checks = _checks,
            captures = _captures,
            commands = _commands,
            nativeGameplayActions = _nativeActions,
            nativeModalClosures = _nativeModalClosures,
            nativeEvidence = _nativeEvidence,
            replays = _replays,
            error,
            scope = "Shipping EndgameDirector, native viewport clicks and keyboard input, independent modal pause, live Echoes memory HUD at 1280x800/1000x720/780x720, actual save/load and separate-character routing. The unchanged maintained Phase 4 archive supplies earned campaign progress; all subsequent Sigils, entries, elite memories, casts, timer expiry, hostile Storm and cosmetic receipts use real runtime commands. Bound-save branches exercise release and expiry before restoring the same unedited archive and completing a cast contract. Exact command replays cover preparation and every gameplay branch. No player saves, fabricated rewards, timers or combat state are used."
        };
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "echoes-screen-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
    private sealed record ReplayEvidence(string File, string Runtime, int Commands, int NewCommands, string FinalHash);
}
