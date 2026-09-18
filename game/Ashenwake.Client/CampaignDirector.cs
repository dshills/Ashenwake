using Ashenwake.Core.Adventure;
using Ashenwake.Core.Authoring;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;
using FileAccess = Godot.FileAccess;

namespace Ashenwake.Client;

/// <summary>Campaign client adapter over the authoritative story, permanent progression and combat runtime.</summary>
public partial class CampaignDirector : Node3D
{
    private Sandbox _sandbox = null!;
    private CampaignStage _stage = null!;
    private CampaignHud _campaign = null!;
    private ProductionHud _character = null!;
    private CampaignRuntimeSession _session = null!;
    private AdventureContent _adventure = null!;
    private ProgressionContent _progression = null!;
    private CampaignContent _content = null!;
    private CampaignDefinition _definition = null!;
    private AdventureDefinition _anatomyDefinition = null!;
    private ProgressionDefinition _productionDefinition = null!;
    private CombatContent _combat = null!;
    private TextCatalog _text = null!;
    private PanelContainer _classSelection = null!;
    private ColorRect _classBackdrop = null!;
    private string _combatJson = "", _output = "";
    private long _revision, _steps;
    private bool _smoke, _finished, _mainSmoke, _captureCampaign;
    private int _classIndex;
    private readonly List<object> _classes = [];
    private readonly Dictionary<string, int> _events = new(StringComparer.Ordinal);
    private readonly List<string> _worldEvents = [];
    private readonly HashSet<int> _capturedActs = [];
    private static readonly string[] Disciplines = ["Vanguard", "Veilwalker", "Arcanist", "Gravecaller", "Warden"];

    public override void _Ready()
    {
        try
        {
            _smoke = OS.GetCmdlineUserArgs().Contains("--campaign-smoke");
            _captureCampaign = OS.GetCmdlineUserArgs().Contains("--capture-campaign");
            _output = Argument("--output=") ?? ProjectSettings.GlobalizePath("user://campaign");
            _combatJson = CampaignCombatContent.Parse(FileAccess.GetFileAsString("res://combat.json"), FileAccess.GetFileAsString("res://campaign-combat.json")).CombatJson;
            _combat = CombatContent.Parse(_combatJson);
            _adventure = AdventureContent.Parse(FileAccess.GetFileAsString("res://adventure.json"));
            _progression = ProgressionContent.Parse(FileAccess.GetFileAsString("res://progression.json"));
            _content = CampaignContent.Parse(FileAccess.GetFileAsString("res://campaign.json")); _definition = _content.Capture();
            _text = TextCatalog.Parse(FileAccess.GetFileAsString("res://text.en.json"));
            if (OS.GetCmdlineUserArgs().Contains("--pseudo-locale")) _text = _text.PseudoLocalize();
            _session = Fresh(Argument("--discipline=") ?? "Vanguard");
            string profilePath = CampaignRuntimeSaveStore.ProfilePath(Path.Combine(_output, "campaign.save.json"));
            if (!_smoke && (File.Exists(profilePath) || File.Exists(profilePath + ".bak")))
                _session = Fresh(Argument("--discipline=") ?? "Vanguard", LocalProfileStore.Load(profilePath, _session.Production.Content).Profile);
            CacheDefinitions();
            _sandbox = new Sandbox { ContentJsonOverride = _combatJson }; AddChild(_sandbox); _sandbox.EnableCampaign(); _sandbox.SetSession(_session.Combat);
            _sandbox.AutomaticStep = _smoke; _sandbox.AdvanceOverride = Advance; _sandbox.SessionOverride = () => _session.Combat;
            _sandbox.SaveOverride = () => Safely(Save); _sandbox.LoadOverride = () => Safely(Load); _sandbox.ReplayOverride = () => Safely(VerifyReplay);
            _stage = new CampaignStage(); AddChild(_stage);
            _campaign = new CampaignHud(); _sandbox.AddOverlay(_campaign);
            _campaign.SetFragmentDescriptions(_combat.Fragments.ToDictionary(f => f.Id, f => f.Description));
            _character = new ProductionHud { Catalog = _text }; _sandbox.AddOverlay(_character); _sandbox.InventoryOverride = _character.Toggle;
            _campaign.ActRequested += act => Apply(() => _session.EnterAct(act));
            _campaign.HubRequested += () => Apply(_session.ReturnToHub);
            _campaign.ContinueRequested += () => Apply(_session.AdvanceEncounter);
            _campaign.ChoiceRequested += (choice, outcome) => Apply(() => _session.Choose(choice, outcome));
            _campaign.ExplorationRequested += id => Apply(() => _session.BeginExploration(id));
            _campaign.LeaveExplorationRequested += () => Apply(_session.LeaveExploration);
            _campaign.InteractionRequested += Interact;
            _campaign.ImplantRequested += (slot, id) => Permanent(new(ProductionAction.Expedition, new(ExpeditionAction.InstallFragment, slot, id ?? "")));
            _campaign.ManifestationRequested += id => Permanent(new(ProductionAction.Expedition, new(ExpeditionAction.Manifestation, id)));
            _campaign.SaveRequested += () => Safely(Save); _campaign.LoadRequested += () => Safely(Load);
            _character.RetrainRequested += id => Permanent(new(ProductionAction.Retrain, Id: id));
            _character.PassiveRequested += id => Permanent(new(ProductionAction.Passive, Id: id));
            _character.RespecRequested += () => Permanent(new(ProductionAction.Respec));
            _character.EquipRequested += (id, slot) => Permanent(new(ProductionAction.Equip, ItemId: id, Slot: slot));
            _character.UnequipRequested += slot => Permanent(new(ProductionAction.Unequip, Slot: slot));
            _character.CraftRequested += request => Permanent(new(ProductionAction.Craft, Crafting: request));
            _character.MutationRequested += (skill, mutation) => Permanent(new(ProductionAction.Mutation, Id: skill, Value: mutation));
            _character.ServiceRequested += Interact;
            BuildClassSelection(); Refresh();
            if (OS.GetCmdlineUserArgs().Contains("--show-character")) _character.Toggle();
            if (!_smoke && Argument("--discipline=") is null)
            { _classSelection.Visible = true; _classBackdrop.Visible = true; _sandbox.SetPaused(true); _classSelection.GetChild<VBoxContainer>(0).GetChildren().OfType<Button>().First().GrabFocus(); }
            Notice("F: interact · J: campaign/map · C or I: character · V: corpse · G: captured echo");
        }
        catch (Exception ex) { Fail(ex); }
    }
    private CampaignRuntimeSession Fresh(string discipline, LocalProfileState? profile = null)
        => CampaignRuntimeSession.Create(_combatJson, _adventure, _progression, _content, 42, discipline, profile);
    private void CacheDefinitions()
    { _anatomyDefinition = _session.Production.AdventureContent.Capture(); _productionDefinition = _session.Production.Content.Capture(); }
    public override void _Input(InputEvent input)
    {
        if (_classSelection is not { Visible: true } || input is not (InputEventKey or InputEventJoypadButton)) return;
        if (!new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev" }.Any(action => input.IsAction(action)))
            GetViewport().SetInputAsHandled();
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (_smoke || _finished || _session is null || _classSelection.Visible) return;
        if (input.IsActionPressed("aw_character")) { _character.Toggle(); GetViewport().SetInputAsHandled(); }
        if (input.IsActionPressed("aw_journey")) { _campaign.Toggle(); GetViewport().SetInputAsHandled(); }
        if (input.IsActionPressed("aw_interact"))
        {
            var player = _session.Combat.View.Actors.Single(a => a.Id == 1);
            var nearest = _session.Interactions.OrderBy(i => CorePosition.DistanceSquared(i.Position, player.Position)).FirstOrDefault();
            if (nearest is not null) Interact(nearest.ActionId); GetViewport().SetInputAsHandled();
        }
    }
    private void Interact(string id)
    {
        if (_session.InHub) Permanent(new(ProductionAction.Expedition, new(ExpeditionAction.Interact, id)));
        else Apply(() => _session.TrackClue(id));
    }
    private void Permanent(ProductionCommand command) => Apply(() => _session.ExecuteProduction(command));
    private void Apply(Func<CampaignRuntimeResult> action)
    {
        Safely(() =>
        {
            var result = action(); if (!result.Success) { Notice(result.Reason); return; }
            _revision++; Consume(result.WorldEvents);
            if (!ReferenceEquals(_sandbox.Session, _session.Combat)) _sandbox.AdoptSession(_session.Combat);
            Refresh();
        });
    }
    private IReadOnlyList<CombatEvent> Advance(CombatCommand[] commands)
    {
        if (_finished) return [];
        try
        {
            if (_smoke && ++_steps > 60000) throw new InvalidDataException("Campaign client smoke exceeded its bounded public-action route.");
            if (_smoke) CheckClassRoute();
            var command = _smoke ? CampaignRuntimeSmoke.Next(_session) : new CampaignRuntimeCommand(CampaignRuntimeAction.Tick, Commands: commands);
            var result = _session.Execute(command);
            if (_smoke && !result.Success) throw new InvalidDataException("Campaign smoke action rejected: " + result.Reason);
            if (result.CombatEvents.Any(e => e.Kind == "LootPickedUp")) _revision++;
            foreach (var e in result.CombatEvents) _events[e.Kind] = _events.GetValueOrDefault(e.Kind) + 1;
            Consume(result.WorldEvents); Refresh();
            if (_smoke && _mainSmoke && CampaignRuntimeSmoke.Complete(_session)) Callable.From(CompleteSmoke).CallDeferred();
            return result.CombatEvents;
        }
        catch (Exception ex) { Fail(ex); return []; }
    }
    private void CheckClassRoute()
    {
        if (_mainSmoke) return;
        var snapshot = _session.Capture();
        if (!snapshot.Campaign.CompletedEncounters.Contains("campaign.road") || _session.Combat.View.Loot.Count > 0) return;
        string directory = Path.Combine(_output, "classes", Disciplines[_classIndex].ToLowerInvariant()); VerifyAndWrite(directory);
        if (_events.GetValueOrDefault("EntityKilled") == 0) throw new InvalidDataException("Campaign discipline route completed without actual enemy deaths.");
        _classes.Add(new { discipline = Disciplines[_classIndex], stateHash = _session.StateHash, events = new SortedDictionary<string, int>(_events, StringComparer.Ordinal), replay = Path.Combine(directory, "campaign.awcampaign") });
        _classIndex++; _events.Clear();
        if (_classIndex == Disciplines.Length) { _mainSmoke = true; _classIndex = 0; }
        _session = Fresh(Disciplines[_classIndex], snapshot.Production.Progression.Profile); CacheDefinitions(); _revision++;
    }
    private void Consume(IReadOnlyList<string> events)
    {
        if (events.Count > 0) _revision++;
        foreach (string message in events)
        {
            _worldEvents.Add(message); if (_worldEvents.Count > 4096) _worldEvents.RemoveAt(0);
            _campaign.PresentInteraction(message);
            _character.PresentInteraction(message);
            string? notice = PlayerNotice(message); if (notice is not null) Notice(notice);
        }
    }
    private string? PlayerNotice(string message)
    {
        string[] parts = message.Split(':'); string value = parts.Length > 1 ? parts[1] : "";
        return parts[0] switch
        {
            "MasteryGained" or "ExperienceEarned" or "WorldChanged" or "ObjectiveCompleted" => null,
            "ActEntered" => "Entered " + (_definition.Acts.FirstOrDefault(a => a.Id == value)?.Name ?? "a new region") + ".",
            "ActCompleted" => "Region complete. A new route is open on your map.",
            "CampaignEncounterEntered" => "Watch the marked danger zones and follow the encounter guidance.",
            "CampaignEncounterCompleted" => "Area secured. Collect the spoils before continuing.",
            "ResidentRescued" => value + " can now return to Greyhaven.",
            "LevelReached" => "Level " + value + " reached. Visit Mara to spend your passive points.",
            "ServiceUnlocked" => value + " is now available in Greyhaven.",
            "ChoiceCommitted" => "Your decision is recorded. Its consequences will unfold through the campaign.",
            "ConsequenceArrived" => "A past decision has changed life in Edrath. Read the journal for the latest reactions.",
            "FragmentInstalled" => "Your divine anatomy has been updated.",
            "ManifestationSelected" => "Manifestation selected. Its benefits and costs follow your current Resonance.",
            "FragmentGranted" => "New fragment: " + (_combat.Fragments.FirstOrDefault(f => f.Id == value)?.Name ?? "a divine relic") + ".",
            "ItemGranted" => "A new item has been added to your permanent inventory.",
            "GroundLootLeftBehind" => "Left " + value + " uncollected drops behind.",
            "Crafted" => value + " completed. Your permanent item has been updated.",
            "ItemEquipped" or "ItemUnequipped" => "Equipment updated.",
            "PassiveAllocated" => "Passive point allocated to " + value + ".",
            "PassivesReset" => "Passive points refunded. Choose your new allocation with Mara.",
            "DisciplineChanged" => "Retrained as " + value + ". Equipment and discoveries are preserved.",
            "HubInvested" => "Greyhaven's workshops have been restored.",
            "MutationSelected" or "MutationRemoved" => "Skill mutation updated.",
            "ExplorationStarted" => "Exploring " + (_definition.Exploration.FirstOrDefault(e => e.Id == value)?.Name ?? "a new location") + ".",
            "ExplorationCompleted" => "Exploration complete. Your discovery and rewards are preserved.",
            "ExplorationEnded" => "Returned to the region from exploration.",
            "HuntClueTracked" => "Clue recorded. Follow the next marked trace.",
            "ProfileUnlocked" => "A new activity or discovery is available in your local profile.",
            "EndgameUnlocked" => "Resonance Fractures and God-Fragment Hunts are now unlocked.",
            "ReturnedToGreyhaven" => "Welcome back to Greyhaven.",
            "CampaignAnchorRespawn" or "CampaignCombatRestoredAtAnchor" => "Restored at the region's anchor. Your earned progress is preserved.",
            _ => null
        };
    }
    private void Notice(string message) { _campaign.Notice(message); _character.Notice(message); }
    private void Refresh()
    {
        var snapshot = _session.Capture(); var combat = _session.Combat.View; var player = combat.Actors.Single(a => a.Id == 1);
        var interactions = _session.Interactions.Select(i => new InteractionDisplay(i.ActionId, i.Name, (int)Math.Sqrt(CorePosition.DistanceSquared(i.Position, player.Position)), i.Range)).ToArray();
        _campaign.SetView(_session.View, snapshot.Campaign, _definition, _session.Production.View, snapshot.Production.Expedition.Adventure, _anatomyDefinition, combat, interactions, _revision);
        _character.SetView(_session.Production.ProgressionView, snapshot.Production.Progression, _productionDefinition, combat, interactions, _session.InHub, _revision, _session.Combat.ProgressionBuild.UnlockedMutations);
        var manifestations = _session.Production.View.ActiveManifestations;
        bool bellDefeated = !_session.InHub && _session.ActiveEncounterId == "campaign.bell_saint" &&
            (_session.EncounterCleared || combat.Actors.Any(actor => actor.DefinitionId == "boss.bell_saint" && actor.Health <= 0));
        _stage.Show(snapshot.Campaign, _session.View, _combat.Room, _session.Interactions, manifestations, _session.Production.ProgressionView.HubStage, player.Position, combat.BossPhase, bellDefeated);
        _sandbox.PresentAuthoredRoom(_session.Room, $"campaign:{_session.ActiveEncounterId}:{snapshot.Campaign.Deaths}", EnvironmentGround.Style(_session.InHub, _session.ActiveEncounterId));
        _sandbox.SetManifestationPresentation(manifestations); _sandbox.SetWorldSubtitle($"CAMPAIGN / {_session.View.Region.ToUpperInvariant()}");
        if (_captureCampaign && DisplayServer.GetName() != "headless" && !_session.InHub &&
            combat.CampaignHazards is { Count: > 0 } && _capturedActs.Add(snapshot.Campaign.CurrentAct))
        {
            string imagePath = Path.Combine(_output, $"act-{snapshot.Campaign.CurrentAct}.png"); Directory.CreateDirectory(_output);
            CaptureRenderedFrame(imagePath);
        }
    }
    private async void CaptureRenderedFrame(string path)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(path);
    }
    private void Save()
    { CampaignRuntimeSaveStore.Write(Path.Combine(_output, "campaign.save.json"), _combatJson, _adventure, _progression, _content, _session.Capture()); Notice("Campaign, character and local profile saved."); }
    private void Load()
    {
        var result = CampaignRuntimeSaveStore.Load(Path.Combine(_output, "campaign.save.json"), _combatJson, _adventure, _progression, _content);
        _session = result.Session; CacheDefinitions(); _classSelection.Visible = false; _classBackdrop.Visible = false; _sandbox.SetSession(_session.Combat); _sandbox.SetPaused(false); _revision++; Refresh();
        Notice(result.RecoveredBackup ? "Recovered the previous valid campaign save." : "Campaign loaded.");
    }
    private void VerifyReplay() { VerifyAndWrite(_output); Notice("Campaign combat, choices, exploration and permanent actions replay verified."); }
    private void VerifyAndWrite(string directory)
    {
        var replay = _session.CaptureReplay(); var result = CampaignRuntimeReplayRunner.Run(_combatJson, _adventure, _progression, _content, replay);
        if (!result.Success) throw new InvalidDataException("Campaign replay diverged: " + result.Detail);
        AtomicFile.Write(Path.Combine(directory, "campaign.awcampaign"), JsonData.Write(replay));
        CampaignRuntimeSaveStore.Write(Path.Combine(directory, "campaign.save.json"), _combatJson, _adventure, _progression, _content, _session.Capture());
        var restored = CampaignRuntimeSaveStore.Load(Path.Combine(directory, "campaign.save.json"), _combatJson, _adventure, _progression, _content).Session;
        if (restored.StateHash != _session.StateHash) throw new InvalidDataException("Campaign save round trip changed state.");
    }
    private async void CompleteSmoke()
    {
        if (_finished) return; _finished = true;
        try
        {
            VerifyAndWrite(_output); var snapshot = _session.Capture();
            if (_classes.Count != 5 || snapshot.Campaign.CompletedActs.Count != 5 || snapshot.Campaign.CompletedEncounters.Count != 15 || snapshot.Campaign.CompletedExploration.Count != 3 || snapshot.Campaign.Choices.Count != 5 || _session.View.Ending?.FracturesUnlocked != true)
                throw new InvalidDataException("Campaign smoke missed a discipline, encounter, exploration, choice or ending unlock.");
            var report = new
            {
                kind = "CampaignClientSmokePassed",
                steps = _steps,
                stateHash = _session.StateHash,
                classes = _classes,
                completedActs = snapshot.Campaign.CompletedActs,
                encounters = snapshot.Campaign.CompletedEncounters,
                exploration = snapshot.Campaign.CompletedExploration,
                choices = snapshot.Campaign.Choices,
                deaths = snapshot.Campaign.Deaths,
                ending = _session.View.Ending,
                level = _session.Production.ProgressionView.Level,
                events = new SortedDictionary<string, int>(_events, StringComparer.Ordinal),
                replay = Path.Combine(_output, "campaign.awcampaign"),
                display = DisplayServer.GetName(),
                note = "Public campaign and combat actions, persistent character state, and exact save/replay verification. Procedural art; independent playtest remains a separate gate."
            };
            AtomicFile.Write(Path.Combine(_output, "campaign-client-report.json"), JsonData.Write(report));
            AtomicFile.Write(Path.Combine(_output, "campaign-world-events.jsonl"), string.Join('\n', _worldEvents)); GD.Print(JsonData.Write(report));
            if (_captureCampaign && DisplayServer.GetName() != "headless")
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_output, "ending.png"));
            }
            GetTree().Quit();
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void BuildClassSelection()
    {
        _classBackdrop = new ColorRect { Color = new Color(0, 0, 0, .45f), MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _classBackdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); _sandbox.AddOverlay(_classBackdrop);
        _classSelection = new PanelContainer { Position = new(334, 204), Size = new(612, 399), Visible = false };
        _classSelection.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("101b27"), BorderColor = new Color("8a9b9a"), BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 16, ContentMarginBottom = 16 });
        _sandbox.AddOverlay(_classSelection); var column = new VBoxContainer(); _classSelection.AddChild(column);
        var title = new Label { Text = "CHOOSE YOUR FIRST DISCIPLINE" }; title.AddThemeFontSizeOverride("font_size", 21); column.AddChild(title);
        column.AddChild(new Label { Text = "A five-act journey through Edrath. Retrain at level 5 in Greyhaven.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        foreach (var discipline in _productionDefinition.Disciplines)
        {
            var button = new Button { Text = $"{discipline.Id} · {discipline.Resource}", CustomMinimumSize = new(0, 40) };
            button.Pressed += () => { _session = Fresh(discipline.Id, _session.Capture().Production.Progression.Profile); CacheDefinitions(); _classSelection.Visible = false; _classBackdrop.Visible = false; _sandbox.SetSession(_session.Combat); _sandbox.SetPaused(false); _revision++; Refresh(); }; column.AddChild(button);
        }
        var load = new Button { Text = "Continue saved campaign", Disabled = !File.Exists(Path.Combine(_output, "campaign.save.json")) && !File.Exists(Path.Combine(_output, "campaign.save.json.bak")) };
        load.Pressed += () => Safely(Load); column.AddChild(load);
    }
    private void Safely(Action action) { try { action(); } catch (Exception ex) { Notice(ex.Message); GD.PushWarning(ex.Message); } }
    private static string? Argument(string prefix) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    private void Fail(Exception ex)
    {
        _finished = true;
        if (_smoke && _session is not null) AtomicFile.Write(Path.Combine(_output, "campaign-failed-snapshot.json"), JsonData.Write(_session.Capture()));
        GD.PushError(ex.ToString()); GetTree().Quit(1); SetProcess(false);
    }
}
