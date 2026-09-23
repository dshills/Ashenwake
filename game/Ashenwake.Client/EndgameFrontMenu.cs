using Ashenwake.Core.Endgame;
using Ashenwake.Core.Experiments;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Godot;

namespace Ashenwake.Client;

public partial class EndgameDirector
{
    private FrontMenu _frontMenu = null!;
    private ClientCharacterCatalog _characterCatalog = null!;
    private FrontDiscipline[] _frontDisciplines = [];
    private bool _hasActiveCharacter;
    private CharacterSlot[] _catalogSlots = [];
    private Task<CharacterCatalogScan>? _catalogRefresh;
    private CancellationTokenSource? _catalogCancellation;
    private string _catalogRecent = "";
    private string _echoesOriginalSaveName = "", _legacyEchoesSlot = "", _legacyOriginalSlot = "";
    private string ActiveCharacterFilename => _experiment is null ? _saveName : _echoesSaveName;

    private void InitializeFrontMenu()
    {
        _characterCatalog = new(_combatJson, _adventure, _progression, _campaign, _endgame, _experimentContent);
        // The previous client had a single selected original/Echoes pair. Remember
        // that association before introducing any additional original characters.
        if (!File.Exists(Path.Combine(_output, "current-character.txt")))
        { _legacyEchoesSlot = ReadCharacterPointer("current-echoes.txt"); _legacyOriginalSlot = _saveName; }
        _frontDisciplines = BuildFrontDisciplines();
        _frontMenu = new FrontMenu(); _sandbox.AddOverlay(_frontMenu); _classSelection = _frontMenu.Panel;
        _frontMenu.CreateRequested += discipline => Safely(() => CreateCharacter(discipline));
        _frontMenu.LoadRequested += filename => Safely(() => PlayCharacter(filename));
        _frontMenu.ResumeRequested += ResumeFromFrontMenu;
        _frontMenu.SettingsRequested += _sandbox.OpenFrontSettings;
        _frontMenu.QuitRequested += () => Safely(MenuQuit);
        _frontMenu.ImportRequested += () => _importDialog.PopupCentered(new(860, 560));
        _frontMenu.OpenChanged += open =>
        {
            if (open) { _collection?.SetOpen(false); _campaignHud.SetOpen(false); _board.SetOpen(false); _character.Close(); CloseExperimentPanel(); }
            else CancelCatalogRefresh();
            _sandbox.SetModalPaused("front-menu", open);
        };
        _sandbox.ConfigureFrontMenu(() => Safely(ShowFrontMenu), () => Safely(MenuQuit));
        GetTree().AutoAcceptQuit = false;
        _sandbox.FrontSettingsClosed += () =>
        {
            if (_frontMenu.IsOpen)
                _frontMenu.FindChildren("*", "Button", true, false).OfType<Button>()
                    .FirstOrDefault(b => b.IsVisibleInTree() && !b.Disabled)?.GrabFocus();
        };
    }

    private FrontDiscipline[] BuildFrontDisciplines() => _productionDefinition.Disciplines.Select(definition =>
    {
        // Preview sessions are isolated, unsaved projections. Selecting a card never adopts one.
        var preview = Fresh(definition.Id);
        string playstyle = definition.Id switch
        {
            "Vanguard" => "Hold the front line with sweeping melee attacks, armor breaks and defensive control.",
            "Veilwalker" => "Strike with venom, reposition through shadows and exploit exposed enemies.",
            "Arcanist" => "Control space with fire, frost and lightning while managing spell heat.",
            "Gravecaller" => "Fight at range with bone magic and summoned ancestors, turning the fallen into resources.",
            "Warden" => "Mix thorns, roots, a feral companion and protective bark to adapt to the fight.",
            _ => "Choose your starting combat discipline. Divine Anatomy will broaden your build."
        };
        string resource = definition.Id switch
        {
            "Vanguard" => "Build Momentum with your generator attacks; spend it on heavier strikes and control.",
            "Veilwalker" => "Generate Exposure with your opening attacks and spend it on shadow techniques.",
            "Arcanist" => "Spells add Instability. Vent heat to keep casting without exceeding your limit.",
            "Gravecaller" => "Generate Remains with your attacks and consume corpses [V] to fuel your magic.",
            "Warden" => "Build Adaptation with your attacks and spend it on nature's stronger responses.",
            _ => definition.Resource
        };
        var abilities = preview.Combat.View.Skills.Where(s => s.Available).Select(s =>
        {
            var info = preview.Combat.InspectSkill(s.Id);
            string cost = s.ResourceMode == "Heat" ? $"+{s.Cost} {definition.Resource}" :
                s.Cost > 0 ? $"{s.Cost} {definition.Resource}" : s.Generate > 0 ? $"Generates {s.Generate} {definition.Resource}" : "No resource cost";
            string family = info.Family.ToString() switch { "PhysicalSlash" => "Physical slash", "PhysicalPierce" => "Physical pierce", "PhysicalCrush" => "Physical impact", var name => name };
            return new FrontAbility(s.Id, s.Name, $"{family} · {s.Shape}", cost);
        }).ToArray();
        return new FrontDiscipline(definition.Id, definition.Resource, playstyle, resource,
            CharacterAppearance.FromCombat(preview.Combat.View), abilities);
    }).ToArray();

    private void RefreshFrontMenu()
    {
        CancelCatalogRefresh();
        _catalogRecent = ReadCharacterPointer("current-character.txt");
        if (_catalogRecent.Length == 0) _catalogRecent = _saveName;
        _frontMenu.SetView(_frontDisciplines, _catalogSlots, _catalogRecent, _hasActiveCharacter, _hasActiveCharacter ? ActiveCharacterFilename : "", true);
        _catalogCancellation = new();
        _catalogRefresh = _characterCatalog.ScanAsync(_output, _catalogRecent, _catalogCancellation.Token);
    }

    private void PollCatalogRefresh()
    {
        if (_catalogRefresh is not { IsCompleted: true } task) return;
        _catalogRefresh = null;
        _catalogCancellation?.Dispose(); _catalogCancellation = null;
        try
        {
            var result = task.GetAwaiter().GetResult();
            if (!_frontMenu.IsOpen) return;
            _catalogSlots = result.Slots;
            _frontMenu.SetView(_frontDisciplines, _catalogSlots, _catalogRecent, _hasActiveCharacter, _hasActiveCharacter ? ActiveCharacterFilename : "");
            if (result.LimitReached) _frontMenu.Notice("Showing up to 128 character slots. All other archives remain in your save folder.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_frontMenu.IsOpen) return;
            _catalogSlots = [];
            _frontMenu.SetView(_frontDisciplines, [], _catalogRecent, _hasActiveCharacter, _hasActiveCharacter ? ActiveCharacterFilename : "");
            _frontMenu.Notice("The save folder could not be read. Existing archives are preserved.");
            GD.PushWarning(ex.Message);
        }
    }

    private void CancelCatalogRefresh()
    {
        _catalogCancellation?.Cancel(); _catalogCancellation?.Dispose(); _catalogCancellation = null;
        // A replaced/exiting menu never adopts a late result. Observe faults even
        // when the scene is gone; the worker holds only managed catalog data.
        if (_catalogRefresh is { } task)
            _ = task.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _catalogRefresh = null;
    }

    public override void _ExitTree() { _training?.Close(); CancelCatalogRefresh(); }

    private void ShowFrontMenu()
    {
        _huntBoard?.SetOpen(false); _secretPanel?.SetOpen(false); _stashPanel?.SetOpen(false); _championPanel?.SetOpen(false); _deathRecapHud?.Close(); EndTraining();
        if (_hasActiveCharacter) Save();
        RefreshFrontMenu(); _frontMenu.ShowPage("Main");
    }

    private void ResumeFromFrontMenu()
    {
        if (!_hasActiveCharacter) return;
        _frontMenu.SetOpen(false); _sandbox.ResumeFromFrontMenu();
        if (_session.RunView is { AwaitingRetry: true } or { Status: "Failed" }) { _shownRecovery = ""; _shownDeathRecap = null; ObserveDeathRecap(); }
    }

    private void CreateCharacter(string discipline)
    {
        if (!_productionDefinition.Disciplines.Any(d => d.Id == discipline)) throw new InvalidDataException("Choose an available discipline.");
        if (_hasActiveCharacter) Save();
        PreserveLegacyEchoesLink();
        var fresh = Fresh(discipline, _session.Production.Capture().Progression.Profile);
        var mapped = fresh.EnableExplorationMap();
        if (!mapped.Success) throw new InvalidDataException(mapped.Reason);
        string filename = "endgame.character-" + Guid.NewGuid().ToString("N") + ".save.json";
        string path = Path.Combine(_output, filename);
        if (File.Exists(path) || File.Exists(path + ".bak") || Directory.Exists(path)) throw new IOException("The new character slot is unavailable. Try again.");
        EndgameRuntimeSaveStore.Write(path, _combatJson, _adventure, _progression, _campaign, _endgame, fresh.Capture());
        var saved = EndgameRuntimeSaveStore.Load(path, _combatJson, _adventure, _progression, _campaign, _endgame).Session;
        PublishCharacterSelection(filename);
        _saveName = filename; _echoesOriginalSaveName = ""; Adopt(saved);
        Notice("New character saved in its own slot. Click Mara to begin your journey.");
    }

    private void PlayCharacter(string filename)
    {
        if (!ClientCharacterCatalog.IsValidFilename(filename)) throw new InvalidDataException("Select a character from this save folder.");
        if (_hasActiveCharacter && filename == ActiveCharacterFilename) { ResumeFromFrontMenu(); return; }
        var inspected = _characterCatalog.Inspect(_output, filename);
        if (!inspected.Available) throw new InvalidDataException(inspected.Notice);
        // Validate the destination first; a failed source save must stop the switch.
        if (_hasActiveCharacter) Save();
        PreserveLegacyEchoesLink();
        string path = Path.Combine(_output, filename);
        if (inspected.IsEchoes)
        {
            var loaded = ExperimentSaveStore.Load(path, _combatJson, _adventure, _progression, _campaign, _endgame, _experimentContent);
            string original = OriginalForEchoes(filename);
            PublishCharacterSelection(filename);
            _echoesOriginalSaveName = original; if (original.Length > 0) _saveName = original;
            _echoesSaveName = filename; _experiment = loaded.Session; Adopt(_experiment.Endgame, true);
            Notice(loaded.RecoveredBackup ? "Echoes character recovered from its previous valid save." : "Continued your separate Echoes character.");
        }
        else
        {
            var loaded = EndgameRuntimeSaveStore.Load(path, _combatJson, _adventure, _progression, _campaign, _endgame);
            PublishCharacterSelection(filename);
            _saveName = filename; _echoesOriginalSaveName = ""; Adopt(loaded.Session);
            Notice(loaded.RecoveredBackup ? "Character recovered from its previous valid save." : "Continued your saved character.");
        }
    }

    private void MenuQuit()
    {
        if (_hasActiveCharacter) Save();
        GetTree().Quit();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && _frontMenu is not null) Safely(MenuQuit);
    }

    private bool BlockFrontMenuInput(InputEvent input)
    {
        if (_frontMenu?.IsOpen != true || _sandbox.FrontSettingsVisible || _importDialog.Visible) return false;
        if (input is not (InputEventKey or InputEventJoypadButton)) return false;
        if (input.IsActionPressed("ui_cancel"))
        {
            if (_frontMenu.Page != "Main") _frontMenu.ShowPage("Main");
            else if (_hasActiveCharacter) ResumeFromFrontMenu();
            GetViewport().SetInputAsHandled(); return true;
        }
        if (!new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev" }.Any(action => input.IsAction(action)))
        { GetViewport().SetInputAsHandled(); return true; }
        return false;
    }

    private string ReadCharacterPointer(string name)
    {
        try
        {
            string path = Path.Combine(_output, name);
            if (!File.Exists(path) || new FileInfo(path).Length > 256) return "";
            string filename = File.ReadAllText(path).Trim();
            return ClientCharacterCatalog.IsValidFilename(filename) ? filename : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    private void PublishCharacterSelection(string filename)
    {
        if (!ClientCharacterCatalog.IsValidFilename(filename)) throw new InvalidDataException("Invalid character slot.");
        string legacy = Path.Combine(_output, ClientCharacterCatalog.IsEchoesFilename(filename) ? "current-echoes.txt" : "current-save.txt");
        string? previous = File.Exists(legacy) && new FileInfo(legacy).Length <= 256 ? File.ReadAllText(legacy) : null;
        AtomicFile.Write(legacy, filename);
        try { AtomicFile.Write(Path.Combine(_output, "current-character.txt"), filename); }
        catch
        {
            // Only selector metadata is rolled back. Every character archive remains intact.
            if (previous is null) File.Delete(legacy); else AtomicFile.Write(legacy, previous);
            throw;
        }
    }

    private string OriginalForEchoes(string filename)
    {
        string original = ReadCharacterPointer(filename + ".origin");
        if (original.Length == 0 && filename == _legacyEchoesSlot) original = _legacyOriginalSlot;
        return original.Length > 0 && !ClientCharacterCatalog.IsEchoesFilename(original) &&
            (File.Exists(Path.Combine(_output, original)) || File.Exists(Path.Combine(_output, original + ".bak"))) ? original : "";
    }

    private void PreserveLegacyEchoesLink()
    {
        if (_legacyEchoesSlot.Length == 0) return;
        string original = OriginalForEchoes(_legacyEchoesSlot);
        if (original.Length > 0) AtomicFile.Write(Path.Combine(_output, _legacyEchoesSlot + ".origin"), original);
        _legacyEchoesSlot = ""; _legacyOriginalSlot = "";
    }
}
