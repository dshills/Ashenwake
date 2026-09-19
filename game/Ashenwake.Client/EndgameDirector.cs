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
    private ColorRect _classBackdrop = null!;
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
                _session = Fresh(Argument("--discipline=") ?? "Vanguard", LocalProfileStore.Load(profile, _session.Production.Content).Profile);
            CacheDefinitions();
            _sandbox = new Sandbox { ContentJsonOverride = _combatJson }; AddChild(_sandbox); _sandbox.EnableCampaign(); _sandbox.SetSession(_session.Combat);
            _sandbox.AutomaticStep = _smoke; _sandbox.AdvanceOverride = Advance; _sandbox.SessionOverride = () => _session.Combat;
            _sandbox.LootCompatibility = item =>
            { var definition = _productionDefinition.Items.FirstOrDefault(i => i.Id == item.DefinitionId); return definition is null || definition.Disciplines.Length == 0 || definition.Disciplines.Contains(_session.Production.ProgressionView.Discipline); };
            _sandbox.SaveOverride = () => Safely(Save); _sandbox.LoadOverride = () => Safely(Load); _sandbox.ReplayOverride = () => Safely(VerifyReplay);
            _stage = new CampaignStage(); AddChild(_stage); _effects = new EndgamePresentation(); AddChild(_effects); _effects.AttachOverlay(_sandbox);
            _campaignHud = new CampaignHud(); _sandbox.AddOverlay(_campaignHud);
            _campaignHud.SetFragmentDescriptions(_combat.Fragments.ToDictionary(f => f.Id, f => f.Description));
            _character = new ProductionHud { Catalog = _text }; _sandbox.AddOverlay(_character); _sandbox.InventoryOverride = _character.Toggle;
            _board = new EndgameHud(); _sandbox.AddOverlay(_board);
            WireCampaign(); WireCharacter(); WireBoard(); BuildClassSelection(); BuildImportDialog(); BindBoardInput(); InitializeExperiments(); Refresh();
            if (_smoke) VerifyMigrationFixture();
            if (OS.GetCmdlineUserArgs().Contains("--show-character")) _character.Toggle();
            string? import = Argument("--import-campaign=");
            if (import is not null) Import(import);
            else if (OS.GetCmdlineUserArgs().Contains("--continue")) Load();
            else if (!_smoke && !_echoesSmoke && Argument("--discipline=") is null)
            { _classSelection.Visible = true; _classBackdrop.Visible = true; _sandbox.SetPaused(true); _classSelection.GetChild<VBoxContainer>(0).GetChildren().OfType<Button>().First().GrabFocus(); }
            Notice("Click a person to approach and interact · J: journey · B: expeditions · C or I: character");
            ConfigureExperimentStart();
        }
        catch (Exception ex) { Fail(ex); }
    }
    private string SavePath => Path.Combine(_output, _saveName);
    private EndgameRuntimeSession Fresh(string discipline, LocalProfileState? profile = null)
        => EndgameRuntimeSession.Create(_combatJson, _adventure, _progression, _campaign, _endgame, 42, discipline, profile);
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
        _campaignHud.ImplantRequested += (slot, id) => Permanent(new(ProductionAction.Expedition, new(ExpeditionAction.InstallFragment, slot, id ?? "")));
        _campaignHud.ManifestationRequested += id => Permanent(new(ProductionAction.Expedition, new(ExpeditionAction.Manifestation, id)));
        _campaignHud.SaveRequested += () => Safely(Save); _campaignHud.LoadRequested += () => Safely(Load);
    }
    private void WireCharacter()
    {
        _character.RetrainRequested += id => Permanent(new(ProductionAction.Retrain, Id: id));
        _character.PassiveRequested += id => Permanent(new(ProductionAction.Passive, Id: id));
        _character.RespecRequested += () => Permanent(new(ProductionAction.Respec));
        _character.EquipRequested += (id, slot) => Permanent(new(ProductionAction.Equip, ItemId: id, Slot: slot));
        _character.UnequipRequested += slot => Permanent(new(ProductionAction.Unequip, Slot: slot));
        _character.CraftRequested += request => Permanent(new(ProductionAction.Craft, Crafting: request));
        _character.MutationRequested += (id, value) => Permanent(new(ProductionAction.Mutation, Id: id, Value: value));
        _character.ServiceRequested += Interact;
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
        _board.SaveRequested += () => Safely(Save); _board.LoadRequested += () => Safely(Load);
        _board.ReplayRequested += () => Safely(VerifyReplay); _board.ImportRequested += () => _importDialog.PopupCentered(new(860, 560));
        _board.VisibilityChangedByPlayer += _ => UpdatePanelVisibility();
        _board.ModalChanged += open => _sandbox.SetModalPaused("endgame-confirmation", open);
    }
    private void BindBoardInput()
    {
        if (InputMap.HasAction("aw_endgame")) return;
        InputMap.AddAction("aw_endgame"); InputMap.ActionAddEvent("aw_endgame", new InputEventKey { PhysicalKeycode = Key.B });
    }
    public override void _Input(InputEvent input)
    {
        if (BlockExperimentPanelInput(input)) return;
        if (_importDialog is { Visible: true } || _classSelection is not { Visible: true } || input is not (InputEventKey or InputEventJoypadButton)) return;
        if (!new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev" }.Any(action => input.IsAction(action))) GetViewport().SetInputAsHandled();
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (HandleExperimentInput(input)) return;
        if (_smoke || _finished || _session is null || _classSelection.Visible) return;
        if (input.IsActionPressed("aw_character")) { _character.Toggle(); GetViewport().SetInputAsHandled(); }
        if (input.IsActionPressed("aw_endgame")) { _board.Toggle(); GetViewport().SetInputAsHandled(); }
        if (input.IsActionPressed("aw_journey"))
        {
            if (_session.Combat.View.Endgame is not null) _board.ShowRun();
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
    { if (_session.Combat.View.Endgame is null) Campaign(new(CampaignRuntimeAction.ReturnToHub)); else Apply(new(EndgameRuntimeAction.ReturnToHub)); }
    private void Interact(string id)
    {
        if (id == "journey.next") { _campaignHud.RequestNextStep(); return; }
        if (id == "endgame.gate") { _board.SetOpen(true); return; }
        if (_session.InHub) Permanent(new(ProductionAction.Expedition, new(ExpeditionAction.Interact, id)));
        else Campaign(new(CampaignRuntimeAction.TrackClue, Id: id));
    }
    private void Campaign(CampaignRuntimeCommand command) => Apply(new(EndgameRuntimeAction.Campaign, Campaign: command));
    private void Permanent(ProductionCommand command) => Apply(new(EndgameRuntimeAction.Production, Production: command));
    private void Apply(EndgameRuntimeCommand command)
    {
        Safely(() =>
        {
            var result = ExecuteActive(command); if (!result.Success) { Notice(result.Reason); return; }
            _revision++; Observe(result);
            if (!ReferenceEquals(_sandbox.Session, _session.Combat)) _sandbox.AdoptSession(_session.Combat);
            Refresh();
        });
    }
    private IReadOnlyList<CombatEvent> Advance(CombatCommand[] commands)
    {
        if (_finished || _capturing) return [];
        try
        {
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
        var snapshot = _session.Capture(); var campaign = snapshot.Campaign; var combat = _session.Combat.View; var player = combat.Actors.Single(a => a.Id == 1);
        var interactions = _session.Interactions.Select(i => new InteractionDisplay(i.ActionId, i.Name, (int)Math.Sqrt(CorePosition.DistanceSquared(i.Position, player.Position)), i.Range)).ToArray();
        _campaignHud.SetView(_session.Campaign.View, campaign.Campaign, _campaignDefinition, _session.Production.View, campaign.Production.Expedition.Adventure, _anatomyDefinition, combat, interactions, _revision);
        _character.SetEndgameMaterials(true, _session.View.Catalysts);
        _character.SetView(_session.Production.ProgressionView, campaign.Production.Progression, _productionDefinition, combat, interactions, _session.InHub, _revision, _session.Combat.ProgressionBuild.UnlockedMutations);
        var view = _session.View;
        var displayKey = (_revision, combat.Loot.Count, AtGate());
        if (_cachedDisplay is null || _displayKey != displayKey)
        {
            _cachedDisplay = Display(view, snapshot); _displayKey = displayKey;
        }
        _board.SetView(_cachedDisplay);
        var manifestations = _session.Production.View.ActiveManifestations;
        _character.SetAppearance(CharacterAppearance.FromProgression(campaign.Production.Progression, manifestations));
        if (combat.Endgame is { } active)
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
            _stage.Show(campaign.Campaign, _session.Campaign.View, _session.Room, _session.Interactions, manifestations, _session.Production.ProgressionView.HubStage, player.Position, combat.BossPhase, bellDefeated, combat, _session.Campaign.ActiveEncounterId);
        }
        string context = combat.Endgame?.ContextKey ?? $"campaign:{_session.Campaign.ActiveEncounterId}:{campaign.Campaign.Deaths}";
        string style = combat.Endgame is null ? EnvironmentGround.Style(_session.InHub, _session.Campaign.ActiveEncounterId, campaign.Campaign.Exploration?.Id, campaign.Campaign.CurrentAct)
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
        var mouseTargets = _session.Interactions.Select(i => new WorldInteractionTarget(i.ActionId, i.Name, i.Position, i.Range, _stage.GetInteractionVisual(i.ActionId))).ToList();
        var wayForward = _stage.PresentWayForward(_session.Room, !_session.InHub && combat.Endgame is null && campaign.Campaign.Exploration is null &&
            _session.EncounterCleared && _campaignHud.CanRequestNextStep, _campaignHud.NextStepLabel);
        if (wayForward is not null) mouseTargets.Add(wayForward);
        _sandbox.SetWorldInteractions(mouseTargets, Interact);
        _sandbox.SetMechanismVisuals(_effects.GetMechanismVisual);
        _sandbox.SetManifestationPresentation(manifestations);
        _sandbox.SetWorldSubtitle(combat.Endgame is null ? $"CAMPAIGN / {_session.Campaign.View.Region.ToUpperInvariant()}" : $"{view.Run?.Kind.ToUpperInvariant()} / {view.Run?.Name.ToUpperInvariant()}");
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
            run.EncounterCleared, run.CanAdvance, run.CanRetry, run.CanAbandon);
        var hunts = _endgameDefinition.Hunts.Select(h => new EndgameHuntDisplay(h.Id, h.Name, h.RequiredTier, h.Secret, view.UnlockedHunts.Contains(h.Id),
            $"Requires cleared Fracture tier {h.RequiredTier}" + (h.Secret ? " and victories over all four known God Hunts." : "."), h.Phases, h.Counterplay, h.EvolutionMaterial)).ToArray();
        var reward = snapshot.Endgame.Rewards.Values.LastOrDefault();
        string summary = reward is null ? "Expedition completion commits materials, mastery and any catalyst once, together with the outcome." :
            $"Last completion: +{reward.Materials} common materials · +{reward.Mastery} mastery" + (reward.EvolutionMaterial.Length == 0 ? "." : $" · +{reward.EvolutionCount} {Readable(reward.EvolutionMaterial)}.");
        return new(view.Unlocked, view.InHub, view.HighestClearedTier, view.Materials, view.Catalysts, view.AvailableSigils.Select(SigilDisplay).ToArray(), hunts,
            presentation, view.CanClaimRecoverySigil, AtGate(), _session.Combat.View.Loot.Count, summary, _revision);
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
        var display = new EndgameSigilDisplay(sigil.Id, Region(sigil.Region), sigil.Tier, sigil.Seed, sigil.BossFamily, sigil.RewardTendency, modifiers, replacements, preview.EncounterNames, preview.InheritedModifiers, preview.SkippedInheritance);
        if (_sigilDisplays.Count >= 256) _sigilDisplays.Clear(); _sigilDisplays[sigil.Id] = (key, display); return display;
    }
    private void UpdatePanelVisibility()
    { if (_campaignHud is not null && _board is not null) _campaignHud.Visible = !_board.IsOpen && _session.Combat.View.Endgame is null; }
    private string Region(string id) => _campaignDefinition.Acts.FirstOrDefault(a => a.Id == id)?.Name ?? Readable(id);
    private string RuleName(string id) => _endgameDefinition.Modifiers.FirstOrDefault(m => m.Id == id || m.Rule == id)?.Name ?? Readable(id);
    private void Notice(string message) { _board.Notice(message); _campaignHud.Notice(message); _character.Notice(message); }
    private string? PlayerNotice(string message)
    {
        string[] parts = message.Split(':'); string value = parts.Length > 1 ? parts[1] : "";
        return parts[0] switch
        {
            "MasteryGained" or "ExperienceEarned" or "WorldChanged" or "ObjectiveCompleted" => null,
            "EndgameUnlocked" => "The Fractures are open. Your campaign character can continue from the expedition board.",
            "SigilAwarded" or "RecoverySigilClaimed" => "A new Sigil is ready on your expedition board.",
            "SigilAttuned" => "Sigil attuned. Review its new rule before entering.",
            "FractureStarted" => "Sigil consumed. Four rooms, three attempts. Read the expedition rules.",
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
            "GroundLootLeftBehind" => "Left " + value + " uncollected drops behind.",
            "Crafted" => value + " completed. Your item and material balance have been updated.",
            "ItemEquipped" or "ItemUnequipped" => "Equipment updated.",
            "PassiveAllocated" => "Passive point allocated to " + value + ".",
            "PassivesReset" => "Passive points refunded. Choose your new allocation with Mara.",
            "DisciplineChanged" => "Retrained as " + value + ". Equipment and discoveries are preserved.",
            "HubInvested" => "Greyhaven's workshops have been restored.",
            "MutationSelected" or "MutationRemoved" => "Skill mutation updated.",
            "ExplorationCompleted" => "Exploration complete. Your discovery and rewards are preserved.",
            "ExplorationEnded" => "Returned to the region from exploration.",
            "HuntClueTracked" => "Clue recorded. Follow the next marked trace.",
            "EndgameAttemptRestarted" => "Attempt restarted. The current encounter is ready; cleared rooms remain complete.",
            "EndgameReturnedToGreyhaven" or "ReturnedToGreyhaven" => "Welcome back to Greyhaven.",
            "CampaignAnchorRespawn" or "CampaignCombatRestoredAtAnchor" => "Restored at the regional anchor. Earned progress is preserved.",
            _ => null
        };
    }
    private void Save()
    { if (SaveExperiment()) return; EndgameRuntimeSaveStore.Write(SavePath, _combatJson, _adventure, _progression, _campaign, _endgame, _session.Capture()); AtomicFile.Write(Path.Combine(_output, "current-save.txt"), _saveName); Notice("Campaign, expedition, character and profile saved together."); }
    private void Load()
    {
        if (_experiment is not null) { LoadExperiment(); return; }
        var result = EndgameRuntimeSaveStore.Load(SavePath, _combatJson, _adventure, _progression, _campaign, _endgame);
        Adopt(result.Session); _loaded = true;
        Notice(result.RecoveredBackup ? "Recovered the previous valid endgame save." : "Campaign and expedition loaded.");
    }
    private void Adopt(EndgameRuntimeSession session, bool retainExperiment = false)
    {
        if (!retainExperiment) _experiment = null;
        _session = session; CacheDefinitions(); _classSelection.Visible = false; _classBackdrop.Visible = false;
        _sandbox.SetSession(_session.Combat); _sandbox.SetPaused(false); _revision++; Refresh();
    }
    private void Import(string path)
    {
        if (Path.GetFullPath(path) == Path.GetFullPath(SavePath)) throw new InvalidDataException("Select a Phase 4 campaign source; the endgame save uses a separate destination.");
        var imported = EndgameRuntimeMigration.ImportPhaseFour(File.ReadAllText(path), _previousCombatJson, _combatJson, _adventure, _progression, _campaign, _endgame);
        string destinationName = File.Exists(SavePath) || File.Exists(SavePath + ".bak") ? "endgame.imported-" + Guid.NewGuid().ToString("N") + ".save.json" : _saveName;
        string destination = Path.Combine(_output, destinationName);
        EndgameRuntimeSaveStore.Write(destination, _combatJson, _adventure, _progression, _campaign, _endgame, imported.Capture());
        _saveName = destinationName; AtomicFile.Write(Path.Combine(_output, "current-save.txt"), _saveName);
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
        string savePath = Path.Combine(directory, "endgame.save.json");
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
    private void BuildClassSelection()
    {
        _classBackdrop = new ColorRect { Color = new Color(0, 0, 0, .45f), MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _classBackdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); _sandbox.AddOverlay(_classBackdrop);
        _classSelection = new PanelContainer { Position = new(334, 176), Size = new(612, 441), Visible = false };
        _classSelection.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("101b27"), BorderColor = new Color("8a9b9a"), BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 16, ContentMarginBottom = 16 });
        _sandbox.AddOverlay(_classSelection); var column = new VBoxContainer(); _classSelection.AddChild(column);
        var title = new Label { Text = "CHOOSE YOUR FIRST DISCIPLINE" }; title.AddThemeFontSizeOverride("font_size", 21); column.AddChild(title);
        column.AddChild(new Label { Text = "Begin the campaign; continue your character into Fractures and God Hunts.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        foreach (var discipline in _productionDefinition.Disciplines)
        {
            var button = new Button { Text = $"{discipline.Id} · {discipline.Resource}", CustomMinimumSize = new(0, 40) };
            button.Pressed += () => Adopt(Fresh(discipline.Id, _session.Capture().Campaign.Production.Progression.Profile)); column.AddChild(button);
        }
        var load = new Button { Text = "Continue saved character", Disabled = !File.Exists(SavePath) && !File.Exists(SavePath + ".bak") };
        load.Pressed += () => Safely(Load); column.AddChild(load);
        if (_selectionNotice.Length > 0) column.AddChild(new Label { Text = _selectionNotice, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var import = new Button { Text = "Import a Phase 4 campaign save" }; import.Pressed += () => _importDialog.PopupCentered(new(860, 560)); column.AddChild(import);
    }
    private void BuildImportDialog()
    {
        _importDialog = new FileDialog { Title = "Import a campaign save · source remains unchanged", FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.json ; Campaign save"] };
        _importDialog.FileSelected += path => Safely(() => Import(path)); AddChild(_importDialog);
    }
    private static string Readable(string value) => string.Join(' ', value.Split('.').Skip(1).DefaultIfEmpty(value)).Replace('_', ' ');
    private static string? Argument(string prefix) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    private void Safely(Action action) { try { action(); } catch (Exception ex) { Notice(ex.Message); GD.PushWarning(ex.Message); } }
    private void Fail(Exception ex)
    {
        _finished = true;
        if (_smoke && _session is not null) AtomicFile.Write(Path.Combine(_output, "endgame-failed-snapshot.json"), JsonData.Write(_session.Capture()));
        GD.PushError(ex.ToString()); GetTree().Quit(1); SetProcess(false);
    }
}
