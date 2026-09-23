using Ashenwake.Core.Adventure;
using Ashenwake.Core.Authoring;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;
using FileAccess = Godot.FileAccess;

namespace Ashenwake.Client;

/// <summary>Client for the campaign and its continuing expeditions. Core owns every rule, spend, attempt and reward.</summary>
public partial class EndgameDirector : Node3D
{
    private Sandbox _sandbox = null!;
    private CampaignStage _stage = null!;
    private EndgamePresentation _effects = null!;
    private CampaignHud _campaignHud = null!;
    private ProductionHud _character = null!;
    private EndgameHud _board = null!;
    private EndgameRuntimeSession _session = null!;
    private AdventureContent _adventure = null!;
    private ProgressionContent _progression = null!;
    private CampaignContent _campaign = null!;
    private EndgameContent _endgame = null!;
    private CampaignDefinition _campaignDefinition = null!;
    private EndgameDefinition _endgameDefinition = null!;
    private AdventureDefinition _anatomyDefinition = null!;
    private ProgressionDefinition _productionDefinition = null!;
    private CombatContent _combat = null!;
    private TextCatalog _text = null!;
    private PanelContainer _classSelection = null!;
    private FileDialog _importDialog = null!;
    private string _combatJson = "", _previousCombatJson = "", _output = "", _saveName = "endgame.save.json", _selectionNotice = "";
    private long _revision, _steps, _drillRun, _drillTick;
    private bool _smoke, _finished, _capture, _capturing, _retried, _abandoned, _recovered, _attuned, _deathSaved, _loaded, _migrationVerified;
    private readonly Dictionary<string, int> _events = new(StringComparer.Ordinal);
    private readonly List<string> _worldEvents = [];
    private readonly HashSet<string> _captures = [];
    private readonly HashSet<string> _checkpoints = [];
    private readonly Dictionary<long, (string Key, EndgameSigilDisplay Display)> _sigilDisplays = [];
    private EndgameDisplay? _cachedDisplay;
    private (long Revision, int Drops, bool AtGate)? _displayKey;

    public override void _Ready()
    {
        try
        {
            _smoke = OS.GetCmdlineUserArgs().Contains("--endgame-smoke");
            _capture = OS.GetCmdlineUserArgs().Contains("--capture-endgame");
            _output = Argument("--output=") ?? ProjectSettings.GlobalizePath("user://endgame");
            if (!_smoke) (_saveName, _selectionNotice) = ClientSaveSelection.Read(_output);
            string campaignCombatJson = CampaignCombatContent.Parse(FileAccess.GetFileAsString("res://combat.json"), FileAccess.GetFileAsString("res://campaign-combat.json")).CombatJson;
            _previousCombatJson = CampaignCombatContent.Parse(FileAccess.GetFileAsString("res://combat-phase4.json"), FileAccess.GetFileAsString("res://campaign-combat-phase4.json")).CombatJson;
            _endgame = EndgameContent.Parse(FileAccess.GetFileAsString("res://endgame.json")); _endgameDefinition = _endgame.Capture();
            _combatJson = EndgameCombatContent.Parse(campaignCombatJson, FileAccess.GetFileAsString("res://endgame-combat.json"), _endgame).CombatJson;
            _combat = CombatContent.Parse(_combatJson);
            _adventure = AdventureContent.Parse(FileAccess.GetFileAsString("res://adventure.json"));
            _progression = ProgressionContent.Parse(FileAccess.GetFileAsString("res://progression.json"));
            _campaign = CampaignContent.Parse(FileAccess.GetFileAsString("res://campaign.json")); _campaignDefinition = _campaign.Capture();
            _text = TextCatalog.Parse(FileAccess.GetFileAsString("res://text.en.json"));
            if (OS.GetCmdlineUserArgs().Contains("--pseudo-locale")) _text = _text.PseudoLocalize();
            _session = Fresh(Argument("--discipline=") ?? "Vanguard");
            string profile = EndgameRuntimeSaveStore.ProfilePath(SavePath);
            if (!_smoke && (File.Exists(profile) || File.Exists(profile + ".bak")))
            {
                try
                {
                    ClientCharacterCatalog.Preflight(profile); ClientCharacterCatalog.Preflight(profile + ".bak");
                    _session = Fresh(Argument("--discipline=") ?? "Vanguard", LocalProfileStore.Load(profile, _session.Production.Content).Profile);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
                { _selectionNotice = "The local profile could not be loaded. Existing files are preserved; character cards show which saves can be continued."; }
            }
            _hasActiveCharacter = !OS.GetCmdlineUserArgs().Contains("--continue") && (_smoke || Argument("--discipline=") is not null || OS.GetCmdlineUserArgs().Contains("--echoes-smoke"));
            CacheDefinitions();
            _sandbox = new Sandbox { ContentJsonOverride = _combatJson }; AddChild(_sandbox); _sandbox.EnableCampaign(); _sandbox.SetSession(_session.Combat);
            _sandbox.AutomaticStep = _smoke; _sandbox.AdvanceOverride = Advance; _sandbox.SessionOverride = () => _training?.Combat ?? _session.Combat;
            _sandbox.LootCompatibility = item =>
            { var definition = _productionDefinition.Items.FirstOrDefault(i => i.Id == item.DefinitionId); return definition is null || definition.Disciplines.Length == 0 || definition.Disciplines.Contains(_session.Production.ProgressionView.Discipline); };
            _sandbox.SaveOverride = () => Safely(Save); _sandbox.LoadOverride = () => Safely(Load); _sandbox.ReplayOverride = () => Safely(VerifyReplay);
            _stage = new CampaignStage(); AddChild(_stage); _effects = new EndgamePresentation(); AddChild(_effects); _effects.AttachOverlay(_sandbox);
            _campaignHud = new CampaignHud(); _sandbox.AddOverlay(_campaignHud);
            _character = new ProductionHud { Catalog = _text }; _sandbox.AddOverlay(_character); _sandbox.InventoryOverride = () => { _collection?.SetOpen(false); _huntBoard?.SetOpen(false); _secretPanel?.SetOpen(false); if (_deathRecapHud?.IsOpen == true) return; if (_training is not null) _trainingHud.SetReportOpen(true); else _character.ToggleInventory(); };
            _board = new EndgameHud(); _sandbox.AddOverlay(_board);
            WireCampaign(); WireCharacter(); WireBoard(); BuildImportDialog(); BindBoardInput(); InitializeExperiments(); InitializeTraining(); InitializeFrontMenu(); InitializeDeathRecap(); InitializeCollection(); InitializeRegionalHunts(); InitializeSecretChambers();
            _sandbox.ConfigureLocalMap(() => _training is null && _hasActiveCharacter && !_frontMenu.IsOpen && !_classSelection.Visible, () => _session.LocalMap);
            if (_hasActiveCharacter) EnableLocalMap();
            Refresh();
            if (_smoke) VerifyMigrationFixture();
            if (OS.GetCmdlineUserArgs().Contains("--show-character")) { _campaignHud.SetOpen(false); _board.SetOpen(false); _character.Toggle(); }
            string? import = Argument("--import-campaign=");
            if (import is not null) Import(import);
            else if (OS.GetCmdlineUserArgs().Contains("--continue"))
            { ShowFrontMenu(); Safely(() => PlayCharacter(ReadCharacterPointer("current-character.txt") is { Length: > 0 } recent ? recent : _saveName)); }
            else if (!_smoke && !_echoesSmoke && Argument("--discipline=") is null)
            { ShowFrontMenu(); if (_selectionNotice.Length > 0) _frontMenu.Notice(_selectionNotice); }
            if (_hasActiveCharacter && !_frontMenu.IsOpen) Notice("Click a person to approach and interact · J: journey · B: expeditions · C or I: character");
            ConfigureExperimentStart();
        }
        catch (Exception ex) { Fail(ex); }
    }
    private string SavePath => Path.Combine(_output, _saveName);
    private EndgameRuntimeSession Fresh(string discipline, LocalProfileState? profile = null)
        => EndgameRuntimeSession.Create(_combatJson, _adventure, _progression, _campaign, _endgame, 42, discipline, profile);
    private void EnableLocalMap()
    {
        if (_smoke || _echoesSmoke || _session.Capture().ExplorationMap is not null) return;
        var result = ExecuteActive(new(EndgameRuntimeAction.EnableExplorationMap));
        if (!result.Success) throw new InvalidDataException(result.Reason);
    }
    private string LocalMapTitle()
        => _session.InSecretChamber ? _session.CurrentSecretChamber!.Name : _session.InHub ? "Greyhaven" : _session.InRegionalHunt ? _session.CurrentRegionalHunt!.Name : _session.Combat.View.Endgame is not null ? _session.View.Run?.Name ?? "Expedition" :
            _combat.Campaign?.Encounters.FirstOrDefault(e => e.Id == _stage.PresentedEncounter)?.Name ?? _session.Campaign.View.Region;
    private void CacheDefinitions()
    {
        _anatomyDefinition = _session.Production.AdventureContent.Capture(); _productionDefinition = _session.Production.Content.Capture();
        _sigilDisplays.Clear(); _displayKey = null;
    }
    private void WireCampaign()
    {
        _campaignHud.ActRequested += act => Campaign(new(CampaignRuntimeAction.EnterAct, Act: act));
        _campaignHud.HubRequested += ReturnHub;
        _campaignHud.ContinueRequested += () => Campaign(new(CampaignRuntimeAction.AdvanceEncounter));
        _campaignHud.ChoiceRequested += (id, value) => Campaign(new(CampaignRuntimeAction.Choose, Id: id, Value: value));
        _campaignHud.ExplorationRequested += id => Campaign(new(CampaignRuntimeAction.BeginExploration, Id: id));
        _campaignHud.LeaveExplorationRequested += () => Campaign(new(CampaignRuntimeAction.LeaveExploration));
        _campaignHud.InteractionRequested += id => { UpdatePanelVisibility(); _sandbox.RequestWorldInteraction(id); };
        _campaignHud.OpeningEquipmentRequested += _character.InspectOpeningReward;
        _character.OpeningEquipmentReturnRequested += _campaignHud.ReturnForOpeningEquipment;
        _character.OpeningEquipmentVisitRequested += () => _sandbox.RequestWorldInteraction("service.torren");
        _campaignHud.ImplantRequested += (slot, id) => Permanent(new(ProductionAction.Expedition, new(ExpeditionAction.InstallFragment, slot, id ?? "")));
        _campaignHud.ManifestationRequested += id => Permanent(new(ProductionAction.Expedition, new(ExpeditionAction.Manifestation, id)));
        _campaignHud.SaveRequested += () => Safely(Save); _campaignHud.LoadRequested += () => Safely(Load);
    }
    private void WireCharacter()
    {
        _character.ConfigureBuildLoadoutNames(_combat);
        _character.RetrainRequested += id => Permanent(new(ProductionAction.Retrain, Id: id));
        _character.PassiveRequested += id => Permanent(new(ProductionAction.Passive, Id: id));
        _character.RespecRequested += () => Permanent(new(ProductionAction.Respec));
        _character.EquipRequested += (id, slot) => Permanent(new(ProductionAction.Equip, ItemId: id, Slot: slot));
        _character.UnequipRequested += slot => Permanent(new(ProductionAction.Unequip, Slot: slot));
        _character.DiscardRequested += id => Permanent(new(ProductionAction.Discard, ItemId: id, ConfirmPermanent: true));
        _character.FavoriteRequested += (id, value) => Permanent(new(ProductionAction.SetItemFavorite, ItemId: id, Value: value ? "true" : "false"));
        _character.LockRequested += (id, value) => Permanent(new(ProductionAction.SetItemLocked, ItemId: id, Value: value ? "true" : "false"));
        _character.SalvageRequested += id => Permanent(new(ProductionAction.Salvage, ItemId: id, ConfirmPermanent: true));
        _character.CraftRequested += request => Permanent(new(ProductionAction.Craft, Crafting: request));
        _character.MutationRequested += (id, value) => Permanent(new(ProductionAction.Mutation, Id: id, Value: value));
        _character.ServiceRequested += Interact;
        _character.EquipmentPresetRequested += (action, id, name) => Permanent(new(action, Id: id, Value: name));
        _character.BuildLoadoutRequested += (action, id, name) => Permanent(new(action, Id: id, Value: name));
        _character.TrainingRequested += () => { _character.Close(); _sandbox.RequestWorldInteraction(Ashenwake.Core.Training.TrainingSession.InteractionId); };
    }
    private void WireBoard()
    {
        _board.FractureRequested += id => Apply(new(EndgameRuntimeAction.StartFracture, SigilId: id));
        _board.AttuneRequested += (id, old, next) => Apply(new(EndgameRuntimeAction.AttuneSigil, SigilId: id, Id: old, Value: next));
        _board.HuntRequested += id => Apply(new(EndgameRuntimeAction.StartGodHunt, Id: id));
        _board.RecoveryRequested += () => Apply(new(EndgameRuntimeAction.ClaimRecoverySigil));
        _board.AdvanceRequested += () => Apply(new(EndgameRuntimeAction.AdvanceEncounter));
        _board.RetryRequested += () => Apply(new(EndgameRuntimeAction.RetryEncounter));
        _board.AbandonRequested += () => Apply(new(EndgameRuntimeAction.Abandon));
        _board.HubRequested += ReturnHub;
        _board.GateApproachRequested += () => { UpdatePanelVisibility(); _sandbox.RequestWorldInteraction("endgame.gate"); };
        _board.SaveRequested += () => Safely(Save); _board.LoadRequested += () => Safely(Load);
        _board.ReplayRequested += () => Safely(VerifyReplay); _board.ImportRequested += () => _importDialog.PopupCentered(new(860, 560));
        _board.VisibilityChangedByPlayer += open => { if (open) { _huntBoard?.SetOpen(false); _secretPanel?.SetOpen(false); _collection?.SetOpen(false); _campaignHud.SetOpen(false); _character.Close(); } UpdatePanelVisibility(); };
        _board.ModalChanged += open => _sandbox.SetModalPaused("endgame-confirmation", open);
    }
    private void BindBoardInput()
    {
        if (InputMap.HasAction("aw_endgame")) return;
        InputMap.AddAction("aw_endgame"); InputMap.ActionAddEvent("aw_endgame", new InputEventKey { PhysicalKeycode = Key.B });
    }
    public override void _Input(InputEvent input)
    {
        if (BlockFrontMenuInput(input)) return;
        if (_frontMenu?.IsOpen == true) return;
        if (HandleTrainingInput(input)) return;
        if (BlockExperimentPanelInput(input)) return;
        if (_importDialog is { Visible: true } || _classSelection is not { Visible: true } || input is not (InputEventKey or InputEventJoypadButton)) return;
        if (!new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev" }.Any(action => input.IsAction(action))) GetViewport().SetInputAsHandled();
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (HandleExperimentInput(input)) return;
        if (_smoke || _finished || _session is null || _classSelection.Visible) return;
        if (input.IsActionPressed("aw_character")) { _huntBoard?.SetOpen(false); _secretPanel?.SetOpen(false); _collection?.SetOpen(false); _character.Toggle(); GetViewport().SetInputAsHandled(); }
        if (input.IsActionPressed("aw_endgame")) { if (_session.InSecretChamber) OpenSecretChambers(); else if (_session.HasUnresolvedRegionalHunt) OpenRegionalHunts(); else _board.Toggle(); GetViewport().SetInputAsHandled(); }
        if (input.IsActionPressed("aw_journey"))
        {
            if (_session.InSecretChamber) OpenSecretChambers();
            else if (_session.HasUnresolvedRegionalHunt) OpenRegionalHunts();
            else if (_session.Combat.View.Endgame is not null) _board.ShowRun();
            else { _board.SetOpen(false); _campaignHud.Visible = true; _campaignHud.Toggle(); }
            GetViewport().SetInputAsHandled();
        }
        if (input.IsActionPressed("aw_interact"))
        {
            var view = _session.Combat.View; var player = view.Actors.Single(a => a.Id == 1);
            var mechanism = view.Endgame?.Mechanisms.Where(m => m.Available).OrderBy(m => CorePosition.DistanceSquared(m.Position, player.Position)).FirstOrDefault();
            if (mechanism is not null)
                Apply(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.InteractMechanism, TargetId: mechanism.Id)]));
            else
            {
                var nearest = _session.Interactions.OrderBy(i => CorePosition.DistanceSquared(i.Position, player.Position)).FirstOrDefault();
                if (nearest is not null) Interact(nearest.ActionId);
                else Notice("Open the Journey map [J] to continue once this encounter is clear.");
            }
            GetViewport().SetInputAsHandled();
        }
    }
    private void ReturnHub()
    { if (_session.InSecretChamber) OpenSecretChambers(); else if (_session.InRegionalHunt) { if (_session.RegionalHunts.Run?.CanReturn == true) Apply(new(EndgameRuntimeAction.ReturnRegionalHunt)); else OpenRegionalHunts(); } else if (_session.Combat.View.Endgame is null) Campaign(new(CampaignRuntimeAction.ReturnToHub)); else Apply(new(EndgameRuntimeAction.ReturnToHub)); }
    private void Interact(string id)
    {
        if (_training is not null) return;
        if (InteractSecretChamber(id) || InteractRegionalHunt(id)) return;
        if (id == Ashenwake.Core.Training.TrainingSession.InteractionId) { Safely(StartTraining); return; }
        if (id == "journey.next") { _campaignHud.RequestNextStep(); return; }
        if (id.StartsWith("opening.", StringComparison.Ordinal)) { Campaign(new(CampaignRuntimeAction.InteractOpening, Id: id)); return; }
        if (id.StartsWith("verdant.", StringComparison.Ordinal)) { Campaign(new(CampaignRuntimeAction.InteractVerdant, Id: id)); return; }
        if (id.StartsWith("cinder.", StringComparison.Ordinal)) { Campaign(new(CampaignRuntimeAction.InteractCinder, Id: id)); return; }
        if (id.StartsWith("spine.", StringComparison.Ordinal)) { Campaign(new(CampaignRuntimeAction.InteractSpine, Id: id)); return; }
        if (id.StartsWith("hollow.", StringComparison.Ordinal)) { Campaign(new(CampaignRuntimeAction.InteractHollow, Id: id)); return; }
        if (id == "endgame.gate") { _board.SetOpen(true); return; }
        if (_session.InHub) Permanent(new(ProductionAction.Expedition, new(ExpeditionAction.Interact, id)));
        else Campaign(new(CampaignRuntimeAction.TrackClue, Id: id));
    }
    private void Campaign(CampaignRuntimeCommand command) => Apply(new(EndgameRuntimeAction.Campaign, Campaign: command));
    private void Permanent(ProductionCommand command) => Apply(new(EndgameRuntimeAction.Production, Production: command));
    private void Apply(EndgameRuntimeCommand command)
    {
        if (_training is not null) { _sandbox.Notify("Leave training to change your build or continue your journey."); return; }
        Safely(() =>
        {
            bool reportCraft = command.Production?.Action == ProductionAction.Craft;
            bool reportBuild = command.Production?.Action is ProductionAction.Passive or ProductionAction.Respec or ProductionAction.Mutation;
            bool reportPreset = command.Production?.Action is ProductionAction.SaveEquipmentPreset or ProductionAction.RenameEquipmentPreset or ProductionAction.DeleteEquipmentPreset or ProductionAction.ApplyEquipmentPreset;
            bool reportLoadout = command.Production?.Action is ProductionAction.SaveBuildLoadout or ProductionAction.RenameBuildLoadout or ProductionAction.DeleteBuildLoadout or ProductionAction.ApplyBuildLoadout;
            bool reportLoot = command.Production?.Action is ProductionAction.SetItemFavorite or ProductionAction.SetItemLocked or ProductionAction.Salvage;
            var result = ExecuteActive(command); if (!result.Success) { Notice(result.Reason); if (reportCraft) _character.ReportCraftResult(false, result.Reason); if (reportBuild) _character.ReportBuildResult(false, result.Reason); if (reportLoot) _character.ReportLootManagementResult(false, result.Reason); if (reportPreset) _character.ReportEquipmentPresetResult(false, result.Reason); if (reportLoadout) _character.ReportBuildLoadoutResult(false, result.Reason); return; }
            _revision++; Observe(result);
            if (!ReferenceEquals(_sandbox.Session, _session.Combat)) _sandbox.AdoptSession(_session.Combat);
            Refresh();
            if (reportCraft) _character.ReportCraftResult(true, "");
            if (reportBuild) _character.ReportBuildResult(true, "");
            if (reportLoot) _character.ReportLootManagementResult(true, "");
            if (reportPreset) _character.ReportEquipmentPresetResult(true, "");
            if (reportLoadout) _character.ReportBuildLoadoutResult(true, "");
        });
    }
    private IReadOnlyList<CombatEvent> Advance(CombatCommand[] commands)
    {
        if (_finished || _capturing || _deathRecapHud?.IsOpen == true) return [];
        try
        {
            if (_training is not null) return AdvanceTraining(commands);
            if (_echoesSmoke) return AdvanceExperimentSmoke();
            if (_smoke && ++_steps > EndgameRuntimeSmoke.MaximumCommands + 10000L) throw new InvalidDataException("Endgame client smoke exceeded its bounded public-action route.");
            var command = _smoke ? SmokeNext() : new EndgameRuntimeCommand(EndgameRuntimeAction.Tick, Commands: commands);
            var result = ExecuteActive(command);
            if (_smoke && !result.Success) throw new InvalidDataException("Endgame smoke action rejected: " + result.Reason);
            Observe(result); Refresh();
            if (_smoke && _steps % 3000 == 0)
                GD.Print(JsonData.Write(new { kind = "EndgameClientProgress", steps = _steps, tick = _session.Tick, run = _session.RunView, player = _session.Combat.View.Actors.Single(a => a.Id == 1), enemies = _session.Combat.View.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0).ToArray() }));
            if (_smoke && _session.RunView is { Status: "Completed" } run && _checkpoints.Add($"run-{run.Id}")) VerifyAndWrite(Path.Combine(_output, "checkpoints", $"run-{run.Id}"));
            if (_smoke && EndgameRuntimeSmoke.Complete(_session, 10, true)) Callable.From(CompleteSmoke).CallDeferred();
            return result.CombatEvents;
        }
        catch (Exception ex) { Fail(ex); return []; }
    }
    private EndgameRuntimeCommand SmokeNext()
    {
        var view = _session.View; var run = view.Run;
        if (view.Unlocked && !_abandoned)
        {
            if (run is { Status: "Active" })
            {
                if (_drillRun == 0) { _drillRun = run.Id; _drillTick = _session.Tick; }
                if (run.AwaitingRetry)
                {
                    if (!_deathSaved)
                    {
                        string directory = Path.Combine(_output, "after-death"); VerifyAndWrite(directory);
                        _session = EndgameRuntimeSaveStore.Load(Path.Combine(directory, "endgame.save.json"), _combatJson, _adventure, _progression, _campaign, _endgame).Session;
                        CacheDefinitions(); _revision++; _deathSaved = true; _loaded = true;
                    }
                    _retried = true; return new(EndgameRuntimeAction.RetryEncounter);
                }
                if (_retried) { _abandoned = true; return new(EndgameRuntimeAction.Abandon); }
                if (_session.Tick - _drillTick > 5000) throw new InvalidDataException("Real endgame death/retry check exceeded its bound.");
                return new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)]);
            }
            if (!view.InHub) return _session.Combat.View.Endgame is null
                ? new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.ReturnToHub)) : new(EndgameRuntimeAction.ReturnToHub);
            if (view.CanClaimRecoverySigil) return AtGate(new(EndgameRuntimeAction.ClaimRecoverySigil));
            if (view.AvailableSigils.Length > 0) return AtGate(new(EndgameRuntimeAction.StartFracture, SigilId: view.AvailableSigils[0].Id));
        }
        if (_abandoned && !_recovered)
        {
            if (!view.InHub) return _session.Combat.View.Endgame is null
                ? new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.ReturnToHub)) : new(EndgameRuntimeAction.ReturnToHub);
            if (view.CanClaimRecoverySigil) { if (AtGate()) _recovered = true; return AtGate(new(EndgameRuntimeAction.ClaimRecoverySigil)); }
            throw new InvalidDataException("Abandoning the only Sigil did not expose the free recovery route.");
        }
        if (!_attuned && view.InHub && view.Materials >= 5)
        {
            foreach (var sigil in view.AvailableSigils)
            {
                var display = SigilDisplay(sigil);
                foreach (var pair in display.Replacements)
                    if (pair.Value.Length > 0) { if (AtGate()) _attuned = true; return AtGate(new(EndgameRuntimeAction.AttuneSigil, SigilId: sigil.Id, Id: pair.Key, Value: pair.Value[0].Id)); }
            }
        }
        return EndgameRuntimeSmoke.Next(_session, 10, true);
    }
    private bool AtGate()
    {
        var gate = _session.Interactions.FirstOrDefault(i => i.ActionId == "endgame.gate");
        return gate is not null && CorePosition.DistanceSquared(gate.Position, _session.Combat.View.Actors.Single(a => a.Id == 1).Position) <= (long)gate.Range * gate.Range;
    }
    private EndgameRuntimeCommand AtGate(EndgameRuntimeCommand action)
    {
        if (AtGate()) return action;
        var gate = _session.Interactions.Single(i => i.ActionId == "endgame.gate"); var player = _session.Combat.View.Actors.Single(a => a.Id == 1);
        var direction = CombatProductionSmoke.MovementDirection(player.Position, gate.Position, _session.Room);
        return new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]);
    }
    private static string RuleExplanation(string id) => id switch
    {
        "fracture.burning_haste" => "Burning enemies move 25% faster.",
        "fracture.healing_echoes" => "Positive healing creates a delayed hostile echo; at most eight per room.",
        "fracture.elite_hazards" => "Dead elites leave persistent damaging scars; at most eight per room.",
        "fracture.resistance_inversion" => "A quarter of your highest resistance becomes bonus damage; your lowest resistance loses 15 percentage points.",
        "fracture.fragment_overcharge" => "Fragment power gains 50% for one second in each four-second cycle.",
        "fracture.inherited_boss" => "The boss inherits up to two compatible elite traits from the preceding rooms. Excluded traits are disclosed in the route.",
        _ => ""
    };
    private void Observe(EndgameRuntimeResult result)
    {
        if (result.WorldEvents.Length > 0 || result.CombatEvents.Any(e => e.Kind is "LootPickedUp" or "LootDropped")) _revision++;
        if (result.CombatEvents.Any(e => e.Kind == "LootPickedUp") || result.WorldEvents.Any(e => e.StartsWith("RegionalHuntRewardClaimed:", StringComparison.Ordinal) || e.StartsWith("SecretTreasureClaimed:", StringComparison.Ordinal))) RefreshCollection(_session.Capture(), persist: true);
        foreach (var e in result.CombatEvents) _events[e.Kind] = _events.GetValueOrDefault(e.Kind) + 1;
        foreach (string message in result.WorldEvents)
        {
            _worldEvents.Add(message); if (_worldEvents.Count > 8192) _worldEvents.RemoveAt(0);
            if (_campaignHud.PresentInteraction(message)) _board.SetOpen(false);
            if (_character.PresentInteraction(message)) _board.SetOpen(false);
            string? notice = PlayerNotice(message); if (notice is not null) Notice(notice);
        }
    }
    private void Refresh()
    {
        if (_training is not null) { RefreshTraining(); return; }
        var snapshot = _session.Capture(); var campaign = snapshot.Campaign; var combat = _session.Combat.View; var player = combat.Actors.Single(a => a.Id == 1);
        _sandbox.PresentProgression(_session.Production.ProgressionView, _session.Production.CurrentLevelExperience, combat.Skills, campaign.Production.Progression.Character.UnlockedDisciplines);
        var interactions = _session.Interactions.Select(i => new InteractionDisplay(i.ActionId, i.Name, (int)Math.Sqrt(CorePosition.DistanceSquared(i.Position, player.Position)), i.Range)).ToArray();
        _campaignHud.SetView(_session.Campaign.View, campaign.Campaign, _campaignDefinition, _session.Production.View, campaign.Production.Expedition.Adventure, _anatomyDefinition, combat, interactions, _revision, campaign.Production.Progression);
        if (_smoke || _echoesSmoke) _campaignHud.SetOpen(false);
        _character.SetView(_session.Production.ProgressionView, campaign.Production.Progression, _productionDefinition, combat, interactions, _session.InHub, _revision, _session.Combat.ProgressionBuild.UnlockedMutations,
            combat.Endgame is null && campaign.Campaign.HighestActVisited <= 1);
        _character.ConfigureEquipmentPresets(_session.Production.EquipmentPresets, _session.Production.PreviewEquipmentPreset);
        _character.ConfigureBuildLoadouts(_session.Production.BuildLoadouts, _session.PreviewBuildLoadout, campaign.Production.Expedition.Adventure.Anatomy, campaign.Production.Expedition.Adventure.Manifestations);
        var view = _session.View;
        var displayKey = (_revision, combat.Loot.Count, AtGate());
        if (_cachedDisplay is null || _displayKey != displayKey)
        {
            _cachedDisplay = Display(view, snapshot); _displayKey = displayKey;
        }
        _board.SetView(_cachedDisplay);
        RefreshCollection(snapshot);
        // Public-command replay diagnostics observe combat without interactive menus.
        if (_smoke || _echoesSmoke) _board.SetOpen(false);
        var manifestations = _session.Production.View.ActiveManifestations;
        _character.SetAppearance(CharacterAppearance.FromProgression(campaign.Production.Progression, manifestations,
            campaign.Production.Expedition.Adventure.Anatomy.Values));
        if (_session.InSecretChamber && _session.CurrentSecretChamber is { } chamber)
        {
            var stageState = campaign.Campaign with { InHub = false, CurrentAct = chamber.Act, Exploration = null };
            var stageView = _session.Campaign.View with { Act = chamber.Act, Region = chamber.Name, EncounterId = chamber.EncounterId };
            _stage.Show(stageState, stageView, _session.Room, [], manifestations, _session.Production.ProgressionView.HubStage, player.Position, combat.BossPhase, combat: combat, activeEncounterId: chamber.EncounterId);
        }
        else if (_session.InRegionalHunt && _session.CurrentRegionalHunt is { } hunt)
        {
            var stageState = campaign.Campaign with { InHub = false, CurrentAct = hunt.Act, Exploration = null };
            var stageView = _session.Campaign.View with { Act = hunt.Act, Region = hunt.Region, EncounterId = hunt.EncounterId };
            _stage.Show(stageState, stageView, _session.Room, [], manifestations, _session.Production.ProgressionView.HubStage, player.Position, combat.BossPhase, combat: combat, activeEncounterId: hunt.EncounterId);
        }
        else if (combat.Endgame is { } active)
        {
            int act = Array.FindIndex(_campaignDefinition.Acts, a => a.Id == snapshot.Manifest?.Region) + 1; if (act == 0) act = 1;
            var stageState = campaign.Campaign with { InHub = false, CurrentAct = act, Exploration = null };
            var stageView = _session.Campaign.View with { Act = act, Region = _campaignDefinition.Acts[act - 1].Name, EncounterId = active.ContextKey };
            _stage.Show(stageState, stageView, _session.Room, [], manifestations, _session.Production.ProgressionView.HubStage, player.Position, combat.BossPhase, combat: combat, activeEncounterId: active.ContextKey);
        }
        else
        {
            bool bellDefeated = !_session.InHub && _session.Campaign.ActiveEncounterId == "campaign.bell_saint" &&
                (_session.Campaign.EncounterCleared || combat.Actors.Any(actor => actor.DefinitionId == "boss.bell_saint" && actor.Health <= 0));
            _stage.Show(campaign.Campaign, _session.Campaign.View, _session.Room, _session.Interactions.Where(i => !i.ActionId.StartsWith("secret.", StringComparison.Ordinal)).ToArray(), manifestations, _session.Production.ProgressionView.HubStage, player.Position, combat.BossPhase, bellDefeated, combat, _session.Campaign.ActiveEncounterId);
        }
        string context = _session.InSecretChamber ? $"secret:{_session.CurrentSecretChamber!.Id}:{snapshot.SecretChambers!.AttemptSequence}" : _session.InRegionalHunt ? $"regional:{_session.RegionalHunts.Run!.Id}" : combat.Endgame?.ContextKey ?? $"campaign:{_session.Campaign.ActiveEncounterId}:{campaign.Campaign.Deaths}";
        string style = _session.InSecretChamber ? EnvironmentGround.Style(false, _session.CurrentSecretChamber!.EncounterId, null, _session.CurrentSecretChamber.Act) : _session.InRegionalHunt ? EnvironmentGround.Style(false, _session.CurrentRegionalHunt!.EncounterId, null, _session.CurrentRegionalHunt.Act) : combat.Endgame is null ? EnvironmentGround.Style(_session.InHub, _session.Campaign.ActiveEncounterId, campaign.Campaign.Exploration?.Id, campaign.Campaign.CurrentAct)
            : snapshot.Manifest?.Region switch
            {
                "act.verdant_maw" => "verdant_ruins",
                "act.cinder_reach" => "cinder_fields",
                "act.shattered_spine" => "spine_causeway",
                "act.hollow_night" => "hollow_rooms",
                _ => "default"
            };
        _sandbox.PresentAuthoredRoom(_session.Room, context, style);
        _effects.Show(combat.Endgame, player.Position, _board.IsOpen);
        RefreshRegionalHunts(); RefreshSecretChambers();
        var mouseTargets = _session.Interactions.Select(i => new WorldInteractionTarget(i.ActionId, i.Name, i.Position, i.Range, _secretPresentation?.GetInteractionVisual(i.ActionId) ?? _huntPresentation?.GetInteractionVisual(i.ActionId) ?? _stage.GetInteractionVisual(i.ActionId))).ToList();
        var wayForward = _stage.PresentWayForward(_session.Room, !_session.InSecretChamber && !_session.InRegionalHunt && !_session.InHub && combat.Endgame is null && campaign.Campaign.Exploration is null &&
            _session.EncounterCleared && _campaignHud.CanRequestNextStep && _campaignHud.CanShowWayForward, _campaignHud.NextStepLabel);
        if (wayForward is not null) mouseTargets.Add(wayForward);
        _sandbox.SetWorldInteractions(mouseTargets, Interact);
        _sandbox.PresentLocalMap(_session.LocalMap, LocalMapTitle(), combat);
        _sandbox.SetMechanismVisuals(_effects.GetMechanismVisual);
        _sandbox.SetManifestationPresentation(manifestations, campaign.Production.Expedition.Adventure.Anatomy.Values);
        ObserveDeathRecap();
        _sandbox.SetWorldSubtitle(_session.InSecretChamber ? "HIDDEN CHAMBER / " + _session.CurrentSecretChamber!.Name.ToUpperInvariant() : _session.InRegionalHunt ? "REGIONAL HUNT / " + _session.CurrentRegionalHunt!.Name.ToUpperInvariant() : combat.Endgame is null ? $"CAMPAIGN / {_session.Campaign.View.Region.ToUpperInvariant()}" : $"{view.Run?.Kind.ToUpperInvariant()} / {view.Run?.Name.ToUpperInvariant()}");
        UpdatePanelVisibility();
        RefreshExperiment();
        if (_capture && DisplayServer.GetName() != "headless")
        {
            string? key = combat.Endgame?.Hazards.Length > 0 ? combat.Endgame.HuntId.Length > 0 ? $"hunt-{combat.Endgame.HuntId}-{combat.Endgame.PhaseIndex}" : $"fracture-tier-{combat.Endgame.Tier}" :
                view.Unlocked && view.InHub && view.Run is null ? "expedition-board" : null;
            if (key is not null && _captures.Add(key)) CaptureRenderedFrame(key + ".png");
        }
    }
    private EndgameDisplay Display(EndgameRuntimeView view, EndgameRuntimeSnapshot snapshot)
    {
        var run = view.Run;
        var presentation = run is null ? null : new EndgameRunDisplay(run.Id, run.Name, run.Kind, run.Status, Region(run.Region), run.Tier,
            run.EncounterIndex + 1, run.EncounterCount, run.AttemptsRemaining, run.Deaths, run.RewardPercent, run.Rules.Select(RuleName).ToArray(), run.Counterplay,
            run.InheritedModifiers, snapshot.Manifest?.Inheritance.Where(i => !i.Selected).Select(i => $"room {i.RoomIndex + 1}: {i.Candidate} · {i.Reason}").ToArray() ?? [],
            run.EncounterCleared, run.CanAdvance, run.CanRetry, run.CanAbandon, snapshot.Manifest?.Rooms.Select(r => r.Name).ToArray());
        var hunts = _endgameDefinition.Hunts.Select(h => new EndgameHuntDisplay(h.Id, h.Name, h.RequiredTier, h.Secret, view.UnlockedHunts.Contains(h.Id),
            $"Requires cleared Fracture tier {h.RequiredTier}" + (h.Secret ? " and victories over all four known God Hunts." : "."), h.Phases, h.Counterplay, h.EvolutionMaterial)).ToArray();
        var reward = snapshot.Endgame.Rewards.Values.LastOrDefault();
        string summary = reward is null ? "Expedition completion commits materials, mastery and any catalyst once, together with the outcome." :
            $"Last completion: +{reward.Materials} common materials · +{reward.Mastery} mastery" + (reward.EvolutionMaterial.Length == 0 ? "." : $" · +{reward.EvolutionCount} {Readable(reward.EvolutionMaterial)}.");
        return new(view.Unlocked, view.InHub, view.HighestClearedTier, view.Materials, view.Catalysts, view.AvailableSigils.Select(SigilDisplay).ToArray(), hunts,
            presentation, view.CanClaimRecoverySigil, AtGate(), _session.Combat.View.Loot.Count, summary, _revision,
            reward is null ? null : new(reward.RunId, reward.Materials, reward.Mastery, reward.EvolutionMaterial, reward.EvolutionCount),
            _session.Combat.View.Actors.Any(a => a.Id == 1 && a.Health > 0), _session.Combat.View.Endgame is not null);
    }
    private EndgameSigilDisplay SigilDisplay(FractureSigil sigil)
    {
        string key = string.Join('|', sigil.Modifiers);
        if (_sigilDisplays.TryGetValue(sigil.Id, out var cached) && cached.Key == key) return cached.Display;
        EndgameChoice Choice(FractureModifier modifier) => new(modifier.Id, modifier.Name, RuleExplanation(modifier.Id) + " " + modifier.Counterplay);
        var modifiers = sigil.Modifiers.Select(id => Choice(_endgameDefinition.Modifiers.Single(m => m.Id == id))).ToArray();
        var replacements = new Dictionary<string, EndgameChoice[]>();
        foreach (string old in sigil.Modifiers)
        {
            var valid = new List<EndgameChoice>();
            foreach (var modifier in _endgameDefinition.Modifiers.Where(m => m.Id != old))
            {
                try { EndgameContent.ValidateSigil(_endgame, sigil with { Modifiers = sigil.Modifiers.Select(id => id == old ? modifier.Id : id).ToArray() }); valid.Add(Choice(modifier)); }
                catch (InvalidDataException) { }
            }
            replacements[old] = valid.ToArray();
        }
        var preview = _session.PreviewSigil(sigil.Id);
        var display = new EndgameSigilDisplay(sigil.Id, Region(sigil.Region), sigil.Tier, sigil.Seed, sigil.BossFamily, sigil.RewardTendency, modifiers, replacements, preview.EncounterNames, preview.InheritedModifiers, preview.SkippedInheritance, sigil.Region);
        if (_sigilDisplays.Count >= 256) _sigilDisplays.Clear(); _sigilDisplays[sigil.Id] = (key, display); return display;
    }
    private void UpdatePanelVisibility()
    {
        if (_campaignHud is not null && _board is not null) _campaignHud.Visible = !_session.InSecretChamber && _secretPanel?.IsOpen != true && !_board.IsOpen && _huntBoard?.IsOpen != true && !_session.HasUnresolvedRegionalHunt && _session.Combat.View.Endgame is null;
        if (_secretObjective is not null) _secretObjective.Visible = SecretObjectiveVisible();
        if (_huntObjective is not null) _huntObjective.Visible = _session.HasUnresolvedRegionalHunt && _huntBoard?.IsOpen != true && _deathRecapHud?.IsOpen != true && _frontMenu?.IsOpen != true;
    }
    private string Region(string id) => _campaignDefinition.Acts.FirstOrDefault(a => a.Id == id)?.Name ?? Readable(id);
    private string RuleName(string id) => _endgameDefinition.Modifiers.FirstOrDefault(m => m.Id == id || m.Rule == id)?.Name ?? Readable(id);
    private void Notice(string message)
    { if (_session.HasUnresolvedRegionalHunt || _session.InSecretChamber || message.StartsWith("Move closer", StringComparison.Ordinal)) _sandbox.Notify(message); _secretNotice = message; _secretPanel?.Notice(message); _huntNotice = message; _huntBoard?.Notice(message); _board.Notice(message); _campaignHud.Notice(message); _character.Notice(message); if (_echoesBoard?.IsOpen == true) _echoesBoard.Notice(message); if (_frontMenu?.IsOpen == true) _frontMenu.Notice(message); }
    private string? PlayerNotice(string message)
    {
        string[] parts = message.Split(':'); string value = parts.Length > 1 ? parts[1] : "";
        return parts[0] switch
        {
            "RegionalHunt" => value switch { "Tracking" => "Follow the three marked clues to find your quarry.", "Combat" => "The quarry has emerged. Read its attacks and defeat the pack.", "Victory" => "Quarry defeated. Return to Greyhaven and claim your bounty.", "Failed" => "Hunt failed. Return to Greyhaven to prepare another attempt.", "Abandoned" => "Hunt abandoned. No bounty was claimed.", _ => null },
            "SecretClueResolved" => "The markings respond. Another detail awaits inspection nearby.",
            "SecretEntranceRevealed" => "A hidden passage opens. Its discovery is recorded in your journal.",
            "SecretTreasureClaimed" => "Hidden treasure claimed. Your unique equipment is in your permanent inventory.",
            "SecretChamber" => value switch { "Foyer" => "A quiet threshold. Approach the guardian's seal when ready.", "Combat" => "The guardian awakens. You can retreat through the entrance at any time.", "Victory" => "Guardian defeated. Claim the chamber's treasure.", "Exited" => "Returned through the hidden passage. Campaign progress and uncollected loot are preserved.", _ => null },
            "RegionalHuntRewardClaimed" => "Bounty claimed: a legendary item and materials are now in your permanent inventory.",
            "MasteryGained" or "ExperienceEarned" or "WorldChanged" or "ObjectiveCompleted" => null,
            "EndgameUnlocked" => "The Fractures are open. Your campaign character can continue from the expedition board.",
            "SigilAwarded" or "RecoverySigilClaimed" => "A new Sigil is ready on your expedition board.",
            "SigilAttuned" => "Sigil attuned. Review its new rule before entering.",
            "FractureStarted" => "Sigil consumed. Four rooms, three attempts. Read the expedition rules.",
            "ExperimentMindKept" => "Entered with your owned Mind intact. Ordinary Fracture rewards apply.",
            "GodHuntStarted" => "God Hunt begun. Read the phase guidance and marked mechanisms.",
            "EndgameRoomCleared" or "EndgameEncounterCompleted" => "Area cleared. Collect the spoils and inspect the next room before continuing.",
            "EndgameRewardCommitted" => "Expedition complete. Permanent rewards have been committed to this character.",
            "ExpeditionAttemptConsumed" => "An attempt was spent. Your cleared rooms remain complete; choose Retry to continue.",
            "ExpeditionFailed" => "Attempts exhausted. Return to Greyhaven to prepare another expedition.",
            "ExpeditionAbandoned" => "Expedition abandoned. Character progress is preserved; the Sigil remains consumed.",
            "ActEntered" => "Entered " + Region(value) + ".",
            "ActCompleted" => "Region complete. A new route is open on your map.",
            "CampaignEncounterCompleted" => "Area secured. Collect the spoils before continuing.",
            "ResidentRescued" => value + " can now return to Greyhaven.",
            "LevelReached" => "Level " + value + " reached. Visit Mara to spend passive points.",
            "ServiceUnlocked" => value + " is now available in Greyhaven.",
            "ChoiceCommitted" => "Your decision is recorded. Its consequences will unfold through the campaign.",
            "ConsequenceArrived" => "A past decision has changed Edrath. Read your journal for the latest reactions.",
            "FragmentInstalled" => "Divine anatomy updated.",
            "ManifestationSelected" => "Manifestation selected. Its benefits and costs follow your Resonance.",
            "FragmentGranted" => "New fragment: " + (_combat.Fragments.FirstOrDefault(f => f.Id == value)?.Name ?? "a divine relic") + ".",
            "ItemGranted" => "A new item has joined your permanent inventory.",
            "GroundLootRetained" => "Saved " + value + " uncollected drops in this cleared room. Return through its passages to collect them.",
            "CryptTestamentClaimed" => "The Widow's Testament is yours: rare armor, 25 materials, and a hidden testimony in your journal.",
            "FoundryTestamentClaimed" => "The Foundry Testament is yours: Cinderwake Saber, 35 materials, and the workers' testimony in your journal.",
            "ArchiveTestamentClaimed" => "The Archive Testament is yours: Vowkeeper's Carapace, 40 materials, and the oathkeepers' testimony in your journal.",
            "VaultTestamentClaimed" => "The Vault Testament is yours: Choir of the Unburied, 45 materials, and lost testimony in your journal.",
            "CampaignPassageEntered" => "Follow the marked passages. Cleared rooms keep their remaining treasure.",
            "GroundLootLeftBehind" => "Left " + value + " uncollected drops behind.",
            "Crafted" => value + " completed. Your item and material balance have been updated.",
            "ItemEquipped" or "ItemUnequipped" => "Equipment updated.",
            "PassiveAllocated" => "Passive point allocated to " + value + ".",
            "PassivesReset" => "Passive points refunded. Choose your new allocation with Mara.",
            "DisciplineChanged" => "Retrained as " + value + ". Equipment and discoveries are preserved.",
            "HubInvested" => "Greyhaven's workshops have been restored.",
            "MutationSelected" or "MutationRemoved" => "Skill mutation updated.",
            "ExplorationCompleted" => "Exploration complete. Your discovery and rewards are preserved.",
            "ExplorationEnded" when value == "event.resonance_storm" && parts.Length > 2 && parts[2] == "expired" => "Burning Rain expired. You returned to the regional route without the storm reward. Enter again to retry.",
            "ExplorationEnded" => "Returned to the region from exploration.",
            "HuntClueTracked" => "Clue recorded. Follow the next marked trace.",
            "EndgameAttemptRestarted" => "Attempt restarted. The current encounter is ready; cleared rooms remain complete.",
            "EndgameReturnedToGreyhaven" or "ReturnedToGreyhaven" => "Welcome back to Greyhaven.",
            "CampaignAnchorRespawn" or "CampaignCombatRestoredAtAnchor" => "Restored at the regional anchor. Earned progress is preserved.",
            _ => null
        };
    }
    private void Save()
    {
        if (!_hasActiveCharacter) return;
        RefreshCollection(_session.Capture(), persist: true);
        PreserveLegacyEchoesLink();
        if (SaveExperiment()) return;
        EndgameRuntimeSaveStore.Write(SavePath, _combatJson, _adventure, _progression, _campaign, _endgame, _session.Capture());
        PublishCharacterSelection(_saveName); Notice("Campaign, expedition, character and profile saved together.");
    }
    private void Load()
    {
        if (_experiment is not null) { LoadExperiment(); return; }
        var result = EndgameRuntimeSaveStore.Load(SavePath, _combatJson, _adventure, _progression, _campaign, _endgame);
        Adopt(result.Session); _loaded = true;
        Notice(result.RecoveredBackup ? "Recovered the previous valid endgame save." : "Campaign and expedition loaded.");
    }
    private void Adopt(EndgameRuntimeSession session, bool retainExperiment = false)
    {
        ResetCollection(); ClearDeathRecap(); EndTraining(false); ClearTrainingComparison();
        _secretPanel?.SessionRestored(); _secretPresentation?.Reset(); _secretSelection = _secretClue = _secretNotice = "";
        _huntBoard?.SessionRestored(); _shownHuntOutcome = _huntNotice = "";
        _echoesBoard?.SessionRestored();
        _memorySourceKey = default; _memorySourceName = ""; _memoryNoticeStatus = "";
        if (!retainExperiment) _experiment = null;
        _frontMenu?.SetOpen(false); _hasActiveCharacter = true;
        _session = session; EnableLocalMap(); CacheDefinitions(); _classSelection.Visible = false;
        _sandbox.SetSession(_session.Combat); _sandbox.SetPaused(false); _revision++; Refresh(); _campaignHud.AnatomySessionRestored(); _board.SessionRestored();
    }
    private void Import(string path)
    {
        if (Path.GetFullPath(path) == Path.GetFullPath(SavePath)) throw new InvalidDataException("Select a Phase 4 campaign source; the endgame save uses a separate destination.");
        var imported = EndgameRuntimeMigration.ImportPhaseFour(File.ReadAllText(path), _previousCombatJson, _combatJson, _adventure, _progression, _campaign, _endgame);
        if (_hasActiveCharacter) Save();
        PreserveLegacyEchoesLink();
        string destinationName = File.Exists(SavePath) || File.Exists(SavePath + ".bak") ? "endgame.imported-" + Guid.NewGuid().ToString("N") + ".save.json" : _saveName;
        string destination = Path.Combine(_output, destinationName);
        if (!_smoke && !_echoesSmoke)
        {
            var mapped = imported.EnableExplorationMap();
            if (!mapped.Success) throw new InvalidDataException(mapped.Reason);
        }
        EndgameRuntimeSaveStore.Write(destination, _combatJson, _adventure, _progression, _campaign, _endgame, imported.Capture());
        PublishCharacterSelection(destinationName); _saveName = destinationName;
        Adopt(EndgameRuntimeSaveStore.Load(destination, _combatJson, _adventure, _progression, _campaign, _endgame).Session);
        Notice("Campaign imported into a separate endgame save. Existing characters and the source archive are preserved.");
    }
    private void VerifyReplay() { if (_experiment is not null) { VerifyExperiment(_output); Notice("Echoes choices, memories, combat and rewards verified."); return; } VerifyAndWrite(_output); Notice("Endgame replay, attempts, permanent rewards and save round trip verified."); }
    private void VerifyAndWrite(string directory, EndgameRuntimeSession? session = null)
    {
        session ??= _session;
        var replay = session.CaptureReplay(); var result = EndgameRuntimeReplayRunner.Run(_combatJson, _adventure, _progression, _campaign, _endgame, replay);
        if (!result.Success) throw new InvalidDataException("Endgame replay diverged: " + result.Detail);
        AtomicFile.Write(Path.Combine(directory, "endgame.awendgame"), JsonData.Write(replay));
        string savePath = Path.Combine(directory, !_smoke && Path.GetFullPath(directory) == Path.GetFullPath(_output) ? "endgame.checkpoint.save.json" : "endgame.save.json");
        EndgameRuntimeSaveStore.Write(savePath, _combatJson, _adventure, _progression, _campaign, _endgame, session.Capture());
        var restored = EndgameRuntimeSaveStore.Load(savePath, _combatJson, _adventure, _progression, _campaign, _endgame).Session;
        if (restored.StateHash != session.StateHash) throw new InvalidDataException("Endgame save round trip changed authoritative state.");
    }
    private void VerifyMigrationFixture()
    {
        string source = FileAccess.GetFileAsString("res://phase4-campaign-complete.json");
        var imported = EndgameRuntimeMigration.ImportPhaseFour(source, _previousCombatJson, _combatJson, _adventure, _progression, _campaign, _endgame);
        if (!imported.View.Unlocked || imported.Campaign.View.Ending is null || !imported.InHub) throw new InvalidDataException("Maintained campaign fixture did not unlock the endgame board.");
        VerifyAndWrite(Path.Combine(_output, "phase4-import"), imported);
        if (FileAccess.GetFileAsString("res://phase4-campaign-complete.json") != source) throw new InvalidDataException("Campaign import changed its source archive.");
        _migrationVerified = true;
    }
    private async void CompleteSmoke()
    {
        if (_finished) return; _finished = true;
        try
        {
            if (!_retried || !_abandoned || !_recovered || !_attuned || !_deathSaved || !_loaded || !_migrationVerified) throw new InvalidDataException("Endgame smoke missed retry, abandonment, recovery, attunement or death save coverage.");
            VerifyAndWrite(_output); var snapshot = _session.Capture(); var view = _session.View;
            var report = new
            {
                kind = "EndgameClientSmokePassed",
                steps = _steps,
                stateHash = _session.StateHash,
                campaignCompleted = _session.Campaign.View.Ending is not null,
                highestTier = view.HighestClearedTier,
                completedFractures = view.CompletedFractures,
                completedGodHunts = view.CompletedGodHunts,
                retried = _retried,
                abandoned = _abandoned,
                recovered = _recovered,
                attuned = _attuned,
                deathSave = _deathSaved,
                continuedAfterLoad = _loaded,
                phaseFourImport = _migrationVerified,
                materials = view.Materials,
                catalysts = view.Catalysts,
                level = _session.Production.ProgressionView.Level,
                events = new SortedDictionary<string, int>(_events, StringComparer.Ordinal),
                checkpoints = _checkpoints.Order().ToArray(),
                replay = Path.Combine(_output, "endgame.awendgame"),
                display = DisplayServer.GetName(),
                note = "Actual public combat and expedition actions; exact checkpoint save/replay verification. Procedural presentation; independent playtest remains a separate gate."
            };
            AtomicFile.Write(Path.Combine(_output, "endgame-client-report.json"), JsonData.Write(report));
            AtomicFile.Write(Path.Combine(_output, "endgame-world-events.jsonl"), string.Join('\n', _worldEvents)); GD.Print(JsonData.Write(report));
            if (_capture && DisplayServer.GetName() != "headless")
            { _board.SetOpen(true); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_output, "endgame-complete.png")); }
            GetTree().Quit();
        }
        catch (Exception ex) { Fail(ex); }
    }
    private async void CaptureRenderedFrame(string filename)
    {
        if (_capturing) return;
        _capturing = true;
        bool wasOpen = _board.IsOpen;
        try
        {
            if (_session.Combat.View.Endgame is { } view)
            {
                _board.SetOpen(false);
                _effects.Show(view, _session.Combat.View.Actors.Single(a => a.Id == 1).Position, false);
                RefreshExperiment();
            }
            Directory.CreateDirectory(_output);
            AtomicFile.Write(Path.Combine(_output, Path.GetFileNameWithoutExtension(filename) + ".frame.json"), JsonData.Write(_session.Combat.View));
            if (_experiment is not null) AtomicFile.Write(Path.Combine(_output, Path.GetFileNameWithoutExtension(filename) + ".experiment-frame.json"), JsonData.Write(new { _experiment.Tick, _experiment.StateHash, _experiment.View }));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_experiment is not null)
            { if (!await WaitForExperimentDraw()) { GD.PushWarning("Echoes screenshot skipped: renderer did not produce a frame within three seconds."); return; } }
            else await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_output, filename));
        }
        finally { _board.SetOpen(wasOpen); _capturing = false; }
    }
    private void BuildImportDialog()
    {
        _importDialog = new FileDialog { Title = "Import a campaign save · source remains unchanged", FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.json ; Campaign save"] };
        _importDialog.FileSelected += path => Safely(() => Import(path)); AddChild(_importDialog);
    }
    private static string Readable(string value) => string.Join(' ', value.Split('.').Skip(1).DefaultIfEmpty(value)).Replace('_', ' ');
    private static string? Argument(string prefix) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    private void Safely(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            string message = ex is SaveCompatibilityException ? ex.Message : ex is IOException or UnauthorizedAccessException
                ? "Character files could not be accessed. Check your save folder and try again." : ex.Message;
            Notice(message); _sandbox?.FrontMenuFailure(message); GD.PushWarning(ex.Message);
        }
    }
    private void Fail(Exception ex)
    {
        _finished = true;
        if (_smoke && _session is not null) AtomicFile.Write(Path.Combine(_output, "endgame-failed-snapshot.json"), JsonData.Write(_session.Capture()));
        GD.PushError(ex.ToString()); GetTree().Quit(1); SetProcess(false);
    }
}
