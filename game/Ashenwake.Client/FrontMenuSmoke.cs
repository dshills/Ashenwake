using System.Reflection;
using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Experiments;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Simulation;
using Godot;

namespace Ashenwake.Client;

/// <summary>Native shipping startup, character creation and archive selection in an isolated directory.</summary>
public partial class FrontMenuSmoke : Node3D
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private readonly List<object> _replays = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private FrontMenu _menu = null!;
    private string _output = "", _firstName = "", _firstBytes = "", _firstHash = "";
    private bool _writeReport;
    private int _restarts;
    private EndgameRuntimeSession Session => Field<EndgameRuntimeSession>(_director, "_session");
    private ExperimentRuntimeSession? Experiment => OptionalField<ExperimentRuntimeSession>(_director, "_experiment");
    private bool HasCharacter => Value<bool>(_director, "_hasActiveCharacter");
    private string CombatJson => Field<string>(_director, "_combatJson");
    private AdventureContent Adventure => Field<AdventureContent>(_director, "_adventure");
    private ProgressionContent Progression => Field<ProgressionContent>(_director, "_progression");
    private CampaignContent Campaign => Field<CampaignContent>(_director, "_campaign");
    private EndgameContent Endgame => Field<EndgameContent>(_director, "_endgame");
    private ExperimentContent Content => Field<ExperimentContent>(_director, "_experimentContent");
    private string CurrentFilename => Experiment is null ? Field<string>(_director, "_saveName") : Field<string>(_director, "_echoesSaveName");

    public override async void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--front-menu-smoke") || args.Any(a => a.StartsWith("--discipline=", StringComparison.Ordinal)) || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Front menu smoke requires --front-menu-smoke --output=<fresh-artifact-directory>, without --discipline.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            Resize(1280, 800);
            _director = new EndgameDirector(); AddChild(_director); _director.SetProcess(false);
            _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false); _sandbox.AutomaticStep = true;
            _menu = Field<FrontMenu>(_director, "_frontMenu");
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            await Frames(8);
            await InitialMenuAndPreviews();
            await CreateSeparateCharacters();
            await BlockedWritesPreserveCharacter();
            await ArchiveRecoveryAndCompatibility();
            await EchoesCharacterSelection();
            Check("first_character_archive_remains_byte_identical_after_all_other_characters", Read(_firstName) == _firstBytes);
            Finish(true, "");
        }
        catch (Exception ex)
        {
            try { await Capture("front-menu-failure.png"); } catch (Exception captureError) { GD.PushError(captureError.Message); }
            GD.PushError(ex.ToString()); Finish(false, ex.Message);
        }
    }

    private async Task InitialMenuAndPreviews()
    {
        string hash = Session.StateHash;
        Check("ordinary_startup_opens_paused_main_without_an_active_character", _menu.IsOpen && _menu.Page == "Main" && _sandbox.IsPaused && !HasCharacter);
        Check("fresh_directory_disables_continue", Button("FrontContinue").Disabled && Archives().Count == 0);
        foreach (var size in new[] { (1280, 800), (1000, 720), (780, 720) })
        {
            Resize(size.Item1, size.Item2); await Frames(5);
            AssertLayout("main_" + size.Item1, "FrontContinue", "FrontNew", "FrontCharacters", "FrontSettings", "FrontQuit");
            await Capture("front-main-" + size.Item1 + ".png");
            await Click("FrontNew");
            AssertLayout("new_" + size.Item1, "FrontBegin", "FrontBack");
            await Capture("front-new-" + size.Item1 + ".png");
            await Click("FrontBack"); await Click("FrontCharacters");
            AssertLayout("empty_characters_" + size.Item1, "FrontBack");
            await Capture("front-empty-characters-" + size.Item1 + ".png");
            await Click("FrontBack");
        }
        Resize(1280, 800); await Frames(); await Click("FrontNew");
        var appearanceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (string discipline in new[] { "Vanguard", "Veilwalker", "Arcanist", "Gravecaller", "Warden" })
        {
            await Click("FrontDiscipline" + discipline);
            string text = VisibleText(_menu);
            Check("preview_" + discipline + "_describes_its_discipline_and_resource", text.Contains(discipline, StringComparison.OrdinalIgnoreCase) &&
                text.Contains(discipline switch { "Vanguard" => "Momentum", "Veilwalker" => "Exposure", "Arcanist" => "Instability", "Gravecaller" => "Remains", _ => "Adaptation" }, StringComparison.OrdinalIgnoreCase));
            Check("preview_" + discipline + "_renders_character", Descendants(_menu).OfType<SubViewportContainer>().Any(n => n.IsVisibleInTree()));
            appearanceKeys.Add(SelectedPreviewKey());
            await Capture("front-discipline-" + discipline.ToLowerInvariant() + ".png");
        }
        Check("all_five_discipline_previews_have_distinct_appearance", appearanceKeys.Count == 5);
        await Click("FrontBack"); await Click("FrontSettings");
        Check("settings_open_over_front_menu_while_world_stays_paused", _sandbox.FrontSettingsVisible && _menu.IsOpen && _sandbox.IsPaused);
        foreach (var action in new[] { "Save character", "Load character", "Verify & save replay", "Inspect ground loot" })
        {
            var button = Descendants(_sandbox).OfType<Button>().First(b => b.Text.StartsWith(action, StringComparison.Ordinal));
            Check("unstarted_settings_block_" + action.Replace(' ', '_'), !button.IsVisibleInTree() || button.Disabled);
        }
        await Click("FrontNew");
        Check("settings_backdrop_blocks_underlying_menu_actions", _sandbox.FrontSettingsVisible && _menu.Page == "Main" && !HasCharacter);
        await KeyPress(Key.F5); await KeyPress(Key.F9); await KeyPress(Key.F6);
        for (int i = 0; i < 15; i++) _sandbox._Process(FixedStepClock.SecondsPerTick);
        await Capture("front-settings.png"); await ClickText("Close settings");
        Check("closing_settings_returns_to_still_paused_front_menu", !_sandbox.FrontSettingsVisible && _menu.IsOpen && _sandbox.IsPaused);
        foreach (Key key in new[] { Key.W, Key.F, Key.C, Key.J, Key.B, Key.H, Key.P, Key.F5, Key.F9 }) await KeyPress(key);
        for (int i = 0; i < 15; i++) _sandbox._Process(FixedStepClock.SecondsPerTick);
        Check("all_startup_previews_and_shortcuts_are_read_only", Session.StateHash == hash && Session.CaptureReplay().Frames.Length == 0 && Archives().Count == 0 && !HasCharacter);
        Check("startup_does_not_publish_a_ghost_character_selector", !System.IO.File.Exists(Path.Combine(_output, "current-save.txt")) && !System.IO.File.Exists(Path.Combine(_output, "current-character.txt")));
    }

    private async Task CreateSeparateCharacters()
    {
        await Click("FrontNew"); await Click("FrontDisciplineVanguard");
        string initialHash = Session.StateHash;
        string blockedSelector = Path.Combine(_output, "current-character.txt");
        Directory.CreateDirectory(blockedSelector);
        try
        {
            await Click("FrontBegin");
            Check("failed_first_creation_preserves_unstarted_state_and_visible_menu", !HasCharacter && _menu.IsOpen && Session.StateHash == initialHash);
            Check("failed_first_creation_rolls_back_original_selection", !System.IO.File.Exists(Path.Combine(_output, "current-save.txt")));
            Check("failed_first_creation_is_explained_without_erasing_new_archive", Find<Label>("FrontNotice").Text.Length > 0 && Archives().Count == 1);
            string unselected = Archives().Keys.Single();
            Check("unselected_new_archive_remains_valid_after_selection_failure", Load(unselected).Session.Production.ProgressionView.Discipline == "Vanguard");
            // Keep the failure artifact outside the selectable-slot namespace for the remaining fresh-start checks.
            System.IO.File.Move(Path.Combine(_output, unselected), Path.Combine(_output, "failed-first-creation.archive.json"));
        }
        finally { Directory.Delete(blockedSelector); }
        await Click("FrontBegin");
        Check("native_begin_creates_one_deliberate_vanguard_and_resumes_play", HasCharacter && !_menu.IsOpen && !_sandbox.IsPaused && Session.Production.ProgressionView.Discipline == "Vanguard" && Archives().Count == 1);
        _firstName = CurrentFilename;
        Check("new_character_has_its_own_durable_slot_before_play", _firstName.StartsWith("endgame.character-", StringComparison.Ordinal) && _firstName.EndsWith(".save.json", StringComparison.Ordinal) &&
            Read("current-save.txt").Trim() == _firstName && Read("current-character.txt").Trim() == _firstName && Load(_firstName).Session.StateHash == Session.StateHash);
        // One actual, deterministic world step makes this character distinct from all later previews.
        Call(_director, "Apply", new EndgameRuntimeCommand(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: 1, Z: 0)]));
        RecordReplay("first-character");
        // F6 must write a diagnostic checkpoint, never another playable character's legacy slot.
        string preservedDefault = Read(_firstName);
        System.IO.File.WriteAllText(Path.Combine(_output, "endgame.save.json"), preservedDefault);
        await KeyPress(Key.F6);
        Check("native_f6_from_unique_slot_preserves_existing_default_character", Read("endgame.save.json") == preservedDefault && CurrentFilename == _firstName);
        Check("native_f6_uses_non_selectable_valid_checkpoint", System.IO.File.Exists(Path.Combine(_output, "endgame.checkpoint.save.json")) &&
            !ClientCharacterCatalog.IsValidFilename("endgame.checkpoint.save.json") && Load("endgame.checkpoint.save.json").Session.StateHash == Session.StateHash);
        System.IO.File.Move(Path.Combine(_output, "endgame.save.json"), Path.Combine(_output, "f6-preserved-default.archive.json"));
        _firstHash = Session.StateHash;
        await SaveToMenu(); _firstBytes = Read(_firstName);
        Check("save_and_main_menu_commits_active_character_and_pauses", _menu.IsOpen && _sandbox.IsPaused && Load(_firstName).Session.StateHash == _firstHash);
        Check("continue_summary_identifies_saved_discipline_level_and_location", VisibleText(_menu).Contains("Vanguard", StringComparison.OrdinalIgnoreCase) &&
            VisibleText(_menu).Contains("Level", StringComparison.OrdinalIgnoreCase) && VisibleText(_menu).Contains("Greyhaven", StringComparison.OrdinalIgnoreCase));
        await Capture("front-main-saved-character.png");
        string originalHash = Session.StateHash; var before = Archives();
        await Click("FrontNew"); await Click("FrontDisciplineArcanist");
        Check("choosing_second_discipline_preserves_active_character_and_archive", Session.StateHash == originalHash && SameFiles(before, Archives()) && Read(_firstName) == _firstBytes);
        await Click("FrontBegin"); string secondName = CurrentFilename;
        Check("second_begin_creates_separate_arcanist_without_overwriting_first", secondName != _firstName && Session.Production.ProgressionView.Discipline == "Arcanist" && Read(_firstName) == _firstBytes && Archives().Count == 2);
        await SaveToMenu(); string secondHash = Session.StateHash; var secondFiles = Archives();
        await Click("FrontCharacters");
        await SelectSlot(_firstName);
        Check("character_card_preview_does_not_switch_or_write", Session.StateHash == secondHash && CurrentFilename == secondName && SameFiles(secondFiles, Archives()));
        foreach (var size in new[] { (1280, 800), (1000, 720), (780, 720) })
        {
            Resize(size.Item1, size.Item2); await Frames(5);
            AssertLayout("saved_characters_" + size.Item1, "FrontPlay", "FrontBack");
            await Capture("front-characters-" + size.Item1 + ".png");
        }
        await Click("FrontPlay");
        Check("native_play_restores_exact_selected_character_hash_and_location", !_menu.IsOpen && Experiment is null && CurrentFilename == _firstName && Session.StateHash == _firstHash && Session.InHub && Session.Production.ProgressionView.Discipline == "Vanguard");
        await SaveToMenu();
        await RestartDirector("original", _firstName);
        await Click("FrontContinue");
        Check("native_continue_after_restart_loads_the_saved_original", HasCharacter && !_menu.IsOpen && Experiment is null && CurrentFilename == _firstName && Session.StateHash == _firstHash);
        _firstBytes = Read(_firstName);
        Resize(1280, 800); await Frames();
    }

    private async Task BlockedWritesPreserveCharacter()
    {
        // A new directory at the selected save path makes a real save publication fail without
        // deleting or altering the archive used by this test; the original bytes live in a sibling.
        string name = CurrentFilename, hash = Session.StateHash, pointer = Read("current-character.txt");
        string originalPath = Path.Combine(_output, name), heldPath = originalPath + ".held";
        System.IO.File.Move(originalPath, heldPath); Directory.CreateDirectory(originalPath);
        try
        {
            await KeyPress(Key.P); await Click("FrontReturnToMenu");
            Check("failed_save_to_menu_preserves_live_character_and_stays_in_play", !_menu.IsOpen && HasCharacter && CurrentFilename == name && Session.StateHash == hash && Read("current-character.txt") == pointer);
            Check("failed_save_to_menu_preserves_previous_durable_bytes", System.IO.File.ReadAllText(heldPath) == _firstBytes);
            await Click("FrontSaveQuit");
            Check("failed_save_and_quit_keeps_process_and_current_character_alive", HasCharacter && !_menu.IsOpen && _sandbox.IsPaused &&
                CurrentFilename == name && Session.StateHash == hash && Read("current-character.txt") == pointer && System.IO.File.ReadAllText(heldPath) == _firstBytes);
            await Capture("front-blocked-save.png");
        }
        finally { Directory.Delete(originalPath); System.IO.File.Move(heldPath, originalPath); }
        if (_sandbox.IsPaused) await ClickText("Resume playing");
        await SaveToMenu(); await Click("FrontNew"); await Click("FrontDisciplineWarden");
        string selector = Path.Combine(_output, "current-character.txt"), heldSelector = selector + ".held";
        System.IO.File.Move(selector, heldSelector); Directory.CreateDirectory(selector);
        try
        {
            await Click("FrontBegin");
            Check("failed_new_character_selection_publication_preserves_active_character", _menu.IsOpen && CurrentFilename == name && HasCharacter && Session.StateHash == hash);
            Check("failed_new_character_selection_does_not_modify_previous_save", Read(name) == _firstBytes && System.IO.File.ReadAllText(heldSelector) == pointer);
            Check("failed_new_character_selection_explains_failure_in_menu", Find<Label>("FrontNotice").Text.Length > 0);
            await Capture("front-blocked-character-creation.png");
        }
        finally { Directory.Delete(selector); System.IO.File.Move(heldSelector, selector); }
        await Click("FrontBack");
    }

    private async Task ArchiveRecoveryAndCompatibility()
    {
        const string corrupt = "endgame.corrupt.save.json", recovered = "endgame.recovered.save.json", future = "endgame.future.save.json";
        System.IO.File.WriteAllText(Path.Combine(_output, corrupt), "{broken");
        System.IO.File.WriteAllText(Path.Combine(_output, recovered), "{broken");
        System.IO.File.WriteAllText(Path.Combine(_output, recovered + ".bak"), _firstBytes);
        var futureDocument = JsonNode.Parse(_firstBytes)!.AsObject(); futureDocument["schemaVersion"] = 99;
        string futureBytes = futureDocument.ToJsonString();
        System.IO.File.WriteAllText(Path.Combine(_output, future), futureBytes);
        System.IO.File.WriteAllText(Path.Combine(_output, future + ".bak"), _firstBytes);
        Call(_director, "RefreshFrontMenu"); await Click("FrontCharacters");
        string activeHash = Session.StateHash; var before = Archives();
        await SelectSlot(corrupt);
        Check("corrupt_character_without_backup_is_visible_but_cannot_play", Button("FrontPlay").Disabled && VisibleText(_menu).Length > 0);
        await Click("FrontPlay");
        Check("disabled_corrupt_play_preserves_active_character", _menu.IsOpen && Session.StateHash == activeHash && SameFiles(before, Archives()));
        await SelectSlot(future);
        Check("future_version_primary_cannot_be_replaced_by_older_backup", Button("FrontPlay").Disabled && Read(future) == futureBytes && Read(future + ".bak") == _firstBytes);
        await Capture("front-incompatible-character.png");
        await SelectSlot(recovered);
        Check("valid_backup_is_previewed_as_recovered_and_can_play", !Button("FrontPlay").Disabled && VisibleText(_menu).Contains("backup", StringComparison.OrdinalIgnoreCase));
        Check("browsing_recovery_never_repairs_or_rewrites_archives", SameFiles(before, Archives()));
        await Capture("front-recovered-character.png"); await Click("FrontPlay");
        Check("native_recovered_play_loads_exact_legitimate_backup", !_menu.IsOpen && CurrentFilename == recovered && Session.StateHash == _firstHash && Read(recovered) == "{broken" && Read(recovered + ".bak") == _firstBytes);
        await SaveToMenu();
        Check("deliberate_save_repairs_recovered_slot_while_original_is_preserved", Load(recovered).Session.StateHash == _firstHash && Read(_firstName) == _firstBytes);
    }

    private async Task EchoesCharacterSelection()
    {
        string fixture = Godot.FileAccess.GetFileAsString("res://phase4-campaign-complete.json");
        string fixturePath = Path.Combine(_output, "phase4-source.json"); System.IO.File.WriteAllText(fixturePath, fixture);
        Call(_director, "Import", fixturePath);
        Check("maintained_campaign_import_creates_playable_original_without_modifying_source", HasCharacter && Experiment is null && Session.InHub && Session.View.Unlocked && System.IO.File.ReadAllText(fixturePath) == fixture);
        string originalName = CurrentFilename, originalHash = Session.StateHash, originalBytes = Read(originalName);
        var echoes = ExperimentRuntimeSession.FromEndgame(CombatJson, Adventure, Progression, Campaign, Endgame, Content, Session.Capture());
        const string echoesName = "echoes.front-menu.save.json";
        ExperimentSaveStore.Write(Path.Combine(_output, echoesName), CombatJson, Adventure, Progression, Campaign, Endgame, Content, echoes.Capture());
        System.IO.File.WriteAllText(Path.Combine(_output, echoesName + ".origin"), originalName);
        await SaveToMenu(); await Click("FrontCharacters"); await SelectSlot(echoesName);
        Check("echoes_card_explicitly_identifies_separate_character", VisibleText(_menu).Contains("Echoes", StringComparison.OrdinalIgnoreCase) && !Button("FrontPlay").Disabled);
        await Capture("front-echoes-character.png"); await Click("FrontPlay");
        Check("native_echoes_play_restores_wrapper_and_correct_origin", Experiment is not null && Experiment.StateHash == echoes.StateHash && CurrentFilename == echoesName && Field<string>(_director, "_echoesOriginalSaveName") == originalName);
        Check("echoes_play_preserves_original_character_bytes", Read(originalName) == originalBytes && Read(_firstName) == _firstBytes);
        await SaveToMenu();
        Check("echoes_continue_selector_stays_separate_from_original_selector", Read("current-character.txt").Trim() == echoesName && Read("current-save.txt").Trim() == originalName);
        await RestartDirector("echoes", echoesName);
        await Click("FrontContinue");
        Check("continue_after_restart_restores_echoes_wrapper_and_origin", HasCharacter && Experiment is not null && CurrentFilename == echoesName &&
            Experiment.StateHash == echoes.StateHash && Field<string>(_director, "_echoesOriginalSaveName") == originalName);
        await KeyPress(Key.H); await Click("EchoesTabCharacter");
        Check("character_selected_from_menu_can_return_to_its_original", !Button("EchoesReturn").Disabled);
        await Click("EchoesReturn");
        Check("native_echoes_return_restores_its_associated_original", Experiment is null && CurrentFilename == originalName && Session.StateHash == originalHash && Read(originalName) == originalBytes);
        var other = ExperimentRuntimeSession.FromEndgame(CombatJson, Adventure, Progression, Campaign, Endgame, Content, Load(_firstName).Session.Capture());
        const string otherName = "echoes.other-character.save.json";
        ExperimentSaveStore.Write(Path.Combine(_output, otherName), CombatJson, Adventure, Progression, Campaign, Endgame, Content, other.Capture());
        System.IO.File.WriteAllText(Path.Combine(_output, otherName + ".origin"), _firstName);
        await SaveToMenu(); await Click("FrontCharacters"); await SelectSlot(otherName); await Click("FrontPlay");
        Check("another_echoes_character_keeps_its_distinct_original_link", Experiment is not null && CurrentFilename == otherName && Field<string>(_director, "_echoesOriginalSaveName") == _firstName);
        await SaveToMenu(); await Click("FrontCharacters"); await SelectSlot(originalName); await Click("FrontPlay");
        var expeditionClose = Descendants(_director).OfType<Button>().FirstOrDefault(b => b.Name == "ExpeditionClose" && b.IsVisibleInTree());
        if (expeditionClose is not null) await ClickControl(expeditionClose);
        Check("other_echoes_remains_the_global_recent_selection", Read("current-echoes.txt").Trim() == otherName && Experiment is null && CurrentFilename == originalName);
        await KeyPress(Key.H); await Click("EchoesTabCharacter");
        Check("original_finds_its_own_echoes_after_another_character_was_played", !Button("EchoesContinue").Disabled);
        await Click("EchoesContinue");
        Check("native_original_continue_does_not_cross_to_another_heroes_echoes", Experiment is not null && CurrentFilename == echoesName && Field<string>(_director, "_echoesOriginalSaveName") == originalName);
        await KeyPress(Key.H); await Click("EchoesTabCharacter"); await Click("EchoesReturn");
        await SaveToMenu();
        Check("fixture_source_is_unchanged_at_end", System.IO.File.ReadAllText(fixturePath) == fixture && Godot.FileAccess.GetFileAsString("res://phase4-campaign-complete.json") == fixture);
        await Capture("front-final-main.png");
    }

    private async Task RestartDirector(string kind, string selectedFilename)
    {
        var archives = Archives(); string selection = Read("current-character.txt");
        _director.QueueFree(); await Frames(5);
        _director = new EndgameDirector(); AddChild(_director); _director.SetProcess(false);
        _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false); _sandbox.AutomaticStep = true;
        _menu = Field<FrontMenu>(_director, "_frontMenu");
        await Frames(8); _restarts++;
        Check(kind + "_restart_builds_a_fresh_paused_director_without_active_session", _menu.IsOpen && _menu.Page == "Main" && _sandbox.IsPaused && !HasCharacter && Experiment is null);
        Check(kind + "_restart_preserves_archives_and_selected_character", SameFiles(archives, Archives()) && Read("current-character.txt") == selection && selection.Trim() == selectedFilename && !Button("FrontContinue").Disabled);
        await Capture("front-restart-" + kind + ".png");
    }

    private string SelectedPreviewKey() => Find<CharacterPreview>("FrontPreview").AppearanceKey;
    private async Task SelectSlot(string filename)
    {
        var button = Descendants(_menu).OfType<Button>().FirstOrDefault(b => b.Name.ToString().StartsWith("FrontSlot", StringComparison.Ordinal) &&
            (b.TooltipText.Contains(filename, StringComparison.Ordinal) || b.Text.Contains(filename, StringComparison.Ordinal)));
        if (button is null)
        {
            var slotsField = _menu.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic).FirstOrDefault(f => f.FieldType == typeof(CharacterSlot[]));
            var slots = slotsField?.GetValue(_menu) as CharacterSlot[] ?? [];
            int index = Array.FindIndex(slots, s => s.Filename == filename);
            if (index >= 0) button = Button("FrontSlot" + index);
        }
        if (button is null) throw new InvalidDataException("Character slot is missing from its native browser: " + filename);
        await ClickControl(button);
    }
    private async Task SaveToMenu()
    {
        if (_menu.IsOpen) return;
        // Unlocked imports and restores intentionally present the native expedition board.
        // Dismiss that actual pause owner before opening the manual pause menu.
        if (Field<EndgameHud>(_director, "_board").IsOpen) await Click("ExpeditionClose");
        if (!_sandbox.IsPaused) await KeyPress(Key.P);
        await Click("FrontReturnToMenu"); await Frames(3);
        Check("native_save_to_menu_" + _checks.Keys.Count(k => k.StartsWith("native_save_to_menu_", StringComparison.Ordinal)), _menu.IsOpen && _menu.Page == "Main" && _sandbox.IsPaused);
    }
    private void RecordReplay(string name)
    {
        var replay = Session.CaptureReplay(); var result = EndgameRuntimeReplayRunner.Run(CombatJson, Adventure, Progression, Campaign, Endgame, replay);
        Check(name + "_actual_gameplay_replay_is_exact", result.Success && result.FinalHash == Session.StateHash);
        string filename = "front-menu-" + name + "-replay.json"; System.IO.File.WriteAllText(Path.Combine(_output, filename), JsonData.Write(replay));
        _replays.Add(new { file = filename, commands = replay.Frames.Length, finalHash = result.FinalHash });
    }
    private EndgameRuntimeLoadResult Load(string filename) => EndgameRuntimeSaveStore.Load(Path.Combine(_output, filename), CombatJson, Adventure, Progression, Campaign, Endgame);
    private string Read(string filename) => System.IO.File.ReadAllText(Path.Combine(_output, filename));
    private Dictionary<string, string> Archives() => Directory.EnumerateFiles(_output, "*.save.json").Where(p => ClientCharacterCatalog.IsValidFilename(Path.GetFileName(p))).ToDictionary(p => Path.GetFileName(p)!, System.IO.File.ReadAllText, StringComparer.Ordinal);
    private static bool SameFiles(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(pair => b.GetValueOrDefault(pair.Key) == pair.Value);
    private void AssertLayout(string key, params string[] actions)
    {
        var panel = Find<Control>("FrontMenuPanel").GetGlobalRect(); var screen = GetViewport().GetVisibleRect();
        Check(key + "_panel_fits_viewport", screen.Grow(2).Encloses(panel));
        var controls = actions.Select(Find<Control>).ToArray();
        Check(key + "_actions_are_visible_inside_panel", controls.All(c => c.IsVisibleInTree() && panel.Grow(2).Encloses(c.GetGlobalRect())));
        Check(key + "_actions_do_not_overlap", controls.All(a => controls.All(b => a == b || !a.GetGlobalRect().Intersects(b.GetGlobalRect()))));
    }
    private static T Field<T>(object owner, string name) where T : class => OptionalField<T>(owner, name) ?? throw new MissingFieldException(name);
    private static T? OptionalField<T>(object owner, string name) where T : class => (T?)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner);
    private static T Value<T>(object owner, string name) => (T)(owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) ?? throw new MissingFieldException(name));
    private static object? Call(object owner, string name, params object[] args) =>
        (owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(name)).Invoke(owner, args);
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
    private T Find<T>(string name) where T : Node => Descendants(_director).OfType<T>().Single(n => n.Name == name);
    private Button Button(string name) => Find<Button>(name);
    private static string VisibleText(Node root) => string.Join("\n", Descendants(root).OfType<Label>().Where(l => l.IsVisibleInTree()).Select(l => l.Text));
    private async Task Click(string name) => await ClickControl(Find<Control>(name));
    private async Task ClickText(string text) => await ClickControl(Descendants(_director).OfType<Button>().Single(b => b.Text == text && b.IsVisibleInTree()));
    private async Task ClickControl(Control control)
    {
        for (Node? ancestor = control.GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
            if (ancestor is ScrollContainer scroll) { scroll.EnsureControlVisible(control); await Frames(3); }
        if (!control.IsVisibleInTree()) throw new InvalidDataException("Native front menu control is hidden: " + control.Name);
        var point = control.GetGlobalRect().GetCenter(); GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = point, Pressed = pressed }, true);
        await Frames(3);
    }
    private async Task KeyPress(Key key)
    {
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
        await Frames(3);
    }
    private void Resize(int width, int height) { GetWindow().Size = new(width, height); GetWindow().ContentScaleSize = new(width, height); }
    private async Task Frames(int count = 2) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Capture(string filename)
    {
        if (!_writeReport || !OS.GetCmdlineUserArgs().Contains("--capture-front-menu") || DisplayServer.GetName() == "headless") return;
        await Frames(3); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + filename, image.GetWidth() > 0 && image.SavePng(Path.Combine(_output, filename)) == Error.Ok); _captures.Add(filename);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Front menu check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "FrontMenuClientSmokePassed" : "FrontMenuClientSmokeFailed",
            passed,
            checks = _checks,
            captures = _captures,
            replays = _replays,
            error,
            scope = "Shipping EndgameDirector ordinary startup, native viewport clicks and keyboard input at 1280x800/1000x720/780x720, five discipline previews, guarded settings, deliberate unique character creation, save and main menu, character and Continue routing after fresh director restarts, byte preservation, blocked writes and failed save-and-quit, corrupt archive backup recovery, future archive rejection, and separate Echoes wrapper/origin return. One actual gameplay command is exactly replayed. The unchanged maintained Phase 4 archive supplies the Echoes origin. No player saves or fabricated gameplay rewards are used; successful quitting is not invoked inside the diagnostic."
        };
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "front-menu-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
