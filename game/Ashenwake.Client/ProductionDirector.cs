using Ashenwake.Core.Adventure;
using Ashenwake.Core.Authoring;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;
using FileAccess = Godot.FileAccess;

namespace Ashenwake.Client;

/// <summary>Five-discipline client over the authoritative permanent progression and expedition bridge.</summary>
public partial class ProductionDirector : Node3D
{
    private Sandbox _sandbox = null!;
    private AdventureStage _stage = null!;
    private AdventureHud _journey = null!;
    private ProductionHud _character = null!;
    private ProductionSession _session = null!;
    private AdventureContent _adventure = null!;
    private AdventureDefinition _adventureDefinition = null!;
    private ProgressionContent _progression = null!;
    private ProgressionDefinition _productionDefinition = null!;
    private CombatContent _combat = null!;
    private TextCatalog _text = null!;
    private PanelContainer _classSelection = null!;
    private string _combatJson = "", _output = "";
    private long _revision, _steps;
    private bool _smoke, _finished, _mainSmoke;
    private int _classIndex, _serviceIndex;
    private bool _crafted, _passivesChecked, _retrained;
    private readonly List<object> _classReports = [];
    private readonly Dictionary<string, int> _eventCounts = new(StringComparer.Ordinal);
    private readonly List<string> _worldEvents = [];
    private static readonly string[] Classes = ["Vanguard", "Veilwalker", "Arcanist", "Gravecaller", "Warden"];
    private static readonly string[] SpecialistTour = ["npc.mara", "npc.torren", "npc.cael", "npc.oris", "npc.kesh", "hub.workshops"];

    public override void _Ready()
    {
        try
        {
            _smoke = OS.GetCmdlineUserArgs().Contains("--production-smoke");
            _output = Argument("--output=") ?? ProjectSettings.GlobalizePath("user://production");
            _combatJson = FileAccess.GetFileAsString("res://combat.json"); _combat = CombatContent.Parse(_combatJson);
            _adventure = AdventureContent.Parse(FileAccess.GetFileAsString("res://adventure.json")); _adventureDefinition = _adventure.Capture();
            _progression = ProgressionContent.Parse(FileAccess.GetFileAsString("res://progression.json"));
            _text = TextCatalog.Parse(FileAccess.GetFileAsString("res://text.en.json"));
            if (OS.GetCmdlineUserArgs().Contains("--pseudo-locale")) _text = _text.PseudoLocalize();
            _session = ProductionSession.Create(_combatJson, _adventure, _progression, 42, Argument("--discipline=") ?? "Vanguard");
            string profilePath = ProductionSaveStore.ProfilePath(Path.Combine(_output, "production.save.json"));
            if (!_smoke && (File.Exists(profilePath) || File.Exists(profilePath + ".bak")))
            {
                var profile = LocalProfileStore.Load(profilePath, _session.Content).Profile;
                _session = ProductionSession.Create(_combatJson, _adventure, _progression, 42, Argument("--discipline=") ?? "Vanguard", profile);
            }
            _adventureDefinition = _session.AdventureContent.Capture(); _productionDefinition = _session.Content.Capture();
            _sandbox = new Sandbox(); AddChild(_sandbox); _sandbox.EnableCampaign(); _sandbox.SetSession(_session.Combat);
            _sandbox.AutomaticStep = _smoke; _sandbox.AdvanceOverride = Advance; _sandbox.SessionOverride = () => _session.Combat;
            _sandbox.SaveOverride = () => Safely(Save); _sandbox.LoadOverride = () => Safely(Load); _sandbox.ReplayOverride = () => Safely(VerifyReplay);
            _stage = new AdventureStage(); AddChild(_stage);
            _journey = new AdventureHud { ProductionMode = true }; _sandbox.AddOverlay(_journey);
            _journey.SetFragmentDescriptions(_combat.Fragments.ToDictionary(f => f.Id, f => f.Description));
            _character = new ProductionHud { Catalog = _text }; _sandbox.AddOverlay(_character);
            _sandbox.InventoryOverride = _character.ToggleInventory;
            _journey.TravelRequested += id => Apply(() => _session.Travel(id));
            _journey.InteractionRequested += id => Apply(() => _session.Interact(id));
            _journey.ImplantRequested += (slot, id) => Apply(() => _session.InstallFragment(slot, id));
            _journey.ManifestationRequested += id => Apply(() => _session.SelectManifestation(id));
            _journey.SaveRequested += () => Safely(Save); _journey.LoadRequested += () => Safely(Load);
            _character.RetrainRequested += id => Apply(() => _session.Retrain(id));
            _character.PassiveRequested += id => Apply(() => _session.AllocatePassive(id));
            _character.RespecRequested += () => Apply(_session.Respec);
            _character.EquipRequested += (id, slot) => Apply(() => _session.Equip(id, slot));
            _character.UnequipRequested += slot => Apply(() => _session.Unequip(slot));
            _character.CraftRequested += request => Apply(() => _session.Craft(request));
            _character.MutationRequested += (skill, mutation) => Apply(() => _session.SetMutation(skill, mutation));
            _character.ServiceRequested += id => Apply(() => _session.Interact(id));
            BuildClassSelection(); Refresh();
            if (OS.GetCmdlineUserArgs().Contains("--show-character")) _character.Toggle();
            if (!_smoke && Argument("--discipline=") is null)
            {
                _classSelection.Visible = true; _sandbox.SetPaused(true);
                _classSelection.GetChild<VBoxContainer>(0).GetChildren().OfType<Button>().First().GrabFocus();
            }
            _journey.Notice("F: interact · J: journey/anatomy · C or I: character/workshops · V: consume corpse · G: captured echo");
        }
        catch (Exception ex) { Fail(ex); }
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (_smoke || _finished || _session is null || _classSelection.Visible) return;
        if (input.IsActionPressed("aw_character")) { _character.Toggle(); GetViewport().SetInputAsHandled(); }
        if (input.IsActionPressed("aw_journey")) { _journey.Toggle(); GetViewport().SetInputAsHandled(); }
        if (input.IsActionPressed("aw_interact"))
        {
            var player = _session.Combat.View.Actors.Single(a => a.Id == 1);
            var closest = _session.Interactions.OrderBy(i => CorePosition.DistanceSquared(i.Position, player.Position)).FirstOrDefault();
            if (closest is not null) Apply(() => _session.Interact(closest.ActionId));
            GetViewport().SetInputAsHandled();
        }
    }
    private void BuildClassSelection()
    {
        _classSelection = new PanelContainer { Position = new(334, 204), Size = new(612, 399), Visible = false };
        _classSelection.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("101b27"),
            BorderColor = new Color("8a9b9a"),
            BorderWidthBottom = 2,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            ContentMarginLeft = 20,
            ContentMarginRight = 20,
            ContentMarginTop = 16,
            ContentMarginBottom = 16
        });
        _sandbox.AddOverlay(_classSelection); var column = new VBoxContainer(); _classSelection.AddChild(column);
        var title = new Label { Text = "CHOOSE YOUR FIRST DISCIPLINE" }; title.AddThemeFontSizeOverride("font_size", 21); column.AddChild(title);
        column.AddChild(new Label { Text = "Retrain at level 5. Your equipment and discoveries carry forward.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        foreach (var discipline in _session.Content.Capture().Disciplines)
        {
            var button = new Button { Text = $"{discipline.Id} · {discipline.Resource}", CustomMinimumSize = new(0, 40) };
            button.Pressed += () =>
            {
                _session = ProductionSession.Create(_combatJson, _adventure, _progression, 42, discipline.Id, _session.Capture().Progression.Profile);
                _classSelection.Visible = false; _sandbox.SetSession(_session.Combat); _sandbox.SetPaused(false); _revision++; Refresh();
            };
            column.AddChild(button);
        }
        var load = new Button { Text = "Continue saved character", Disabled = !File.Exists(Path.Combine(_output, "production.save.json")) };
        load.Pressed += () => Safely(Load); column.AddChild(load);
    }

    private IReadOnlyList<CombatEvent> Advance(CombatCommand[] commands)
    {
        if (_finished) return [];
        try
        {
            if (_smoke)
            {
                if (++_steps > 26000) throw new InvalidDataException("Production client smoke exceeded its bounded run.");
                commands = SmokeCommands();
            }
            var events = _session.Step(commands);
            foreach (var e in events) _eventCounts[e.Kind] = _eventCounts.GetValueOrDefault(e.Kind) + 1;
            Consume(_session.WorldEvents); Refresh();
            if (_smoke && _mainSmoke && _session.View.Expedition >= 2 && _session.View.RoomId == "room.ossuary")
                Callable.From(CompleteSmoke).CallDeferred();
            return events;
        }
        catch (Exception ex) { Fail(ex); return []; }
    }
    private void Apply(Func<ProductionResult> action)
    {
        Safely(() =>
        {
            var result = action();
            if (!result.Success) { _journey.Notice(result.Reason); _character.Notice(result.Reason); return; }
            _revision++; Consume(result.WorldEvents);
            if (!ReferenceEquals(_sandbox.Session, _session.Combat)) _sandbox.SetSession(_session.Combat);
            Refresh();
        });
    }
    private void Consume(IReadOnlyList<string> events)
    {
        if (events.Count > 0) _revision++;
        foreach (string item in events)
        {
            _worldEvents.Add(item); if (_worldEvents.Count > 2048) _worldEvents.RemoveAt(0);
            string message = item.Replace(':', ' ').Replace('_', ' ');
            _journey.Notice(message); _character.Notice(message);
        }
    }
    private void Refresh()
    {
        var snapshot = _session.Capture(); var world = _session.View;
        var player = _session.Combat.View.Actors.Single(a => a.Id == 1);
        var interactions = _session.Interactions.Select(i => new InteractionDisplay(i.ActionId, i.Name,
            (int)Math.Sqrt(CorePosition.DistanceSquared(i.Position, player.Position)), i.Range)).ToArray();
        _journey.SetView(world, snapshot.Expedition.Adventure, _adventureDefinition, interactions, _revision);
        _character.SetView(_session.ProgressionView, snapshot.Progression, _productionDefinition, _session.Combat.View,
            interactions, world.RoomId == _adventureDefinition.Hub, _revision, _session.Combat.ProgressionBuild.UnlockedMutations);
        _character.SetAppearance(CharacterAppearance.FromProgression(snapshot.Progression, world.ActiveManifestations,
            snapshot.Expedition.Adventure.Anatomy.Values));
        _stage.ShowRoom(world.RoomId, world.BellPhase, _combat.Room, world.ActiveManifestations,
            _session.Interactions.ToDictionary(i => i.ActionId, i => i.Position), snapshot.Expedition.Adventure.DestroyedAnchors, _session.ProgressionView.HubStage,
            bossDefeated: world.RoomId == _adventureDefinition.BossRoom && world.BellPhase >= 4);
        _stage.FocusNearestInteraction(player.Position);
        _sandbox.PresentAuthoredRoom(_combat.Room, world.RoomId, EnvironmentGround.Style(world.RoomId == "room.greyhaven", world.RoomId));
        _sandbox.SetManifestationPresentation(world.ActiveManifestations);
    }
    private void Save()
    {
        ProductionSaveStore.Write(Path.Combine(_output, "production.save.json"), _combatJson, _adventure, _progression, _session.Capture());
        _journey.Notice(_text.Format("production.saved"));
    }
    private void Load()
    {
        var result = ProductionSaveStore.Load(Path.Combine(_output, "production.save.json"), _combatJson, _adventure, _progression);
        _session = result.Session; _classSelection.Visible = false; _sandbox.SetSession(_session.Combat); _sandbox.SetPaused(false); _revision++; Refresh();
        _journey.Notice(result.RecoveredBackup ? "Recovered the previous valid production character." : _text.Format("production.saved"));
    }
    private void VerifyReplay()
    {
        VerifyAndWrite(_output);
        _journey.Notice("Permanent progression and combat replay verified.");
    }
    private void VerifyAndWrite(string directory)
    {
        var replay = _session.CaptureReplay(); var result = ProductionReplayRunner.Run(_combatJson, _adventure, _progression, replay);
        if (!result.Success) throw new InvalidDataException($"Production replay diverged at {result.DivergentTick}: {result.Detail}");
        AtomicFile.Write(Path.Combine(directory, "production.awp"), JsonData.Write(replay));
        ProductionSaveStore.Write(Path.Combine(directory, "production.save.json"), _combatJson, _adventure, _progression, _session.Capture());
        var restored = ProductionSaveStore.Load(Path.Combine(directory, "production.save.json"), _combatJson, _adventure, _progression).Session;
        if (restored.StateHash != _session.StateHash) throw new InvalidDataException("Production save round trip changed state.");
    }

    private CombatCommand[] SmokeCommands()
    {
        var world = _session.View; var snapshot = _session.Capture();
        if (world.RoomId == "room.greyhaven")
        {
            if (!snapshot.Expedition.Adventure.QuestAccepted)
            {
                var mara = _session.Interactions.Single(i => i.ActionId == "npc.mara");
                if (!Near(mara.Position, mara.Range)) return Walk(mara.Position);
                Require(_session.Interact("npc.mara"));
                Require(_session.InstallFragment("Spine", "fragment.nerve_ilyra"));
                Require(_session.InstallFragment("Arms", "fragment.orrun_bone"));
                Require(_session.SelectManifestation("manifestation.stone_memory"));
                Require(_session.Travel("room.ossuary")); return [];
            }
            if (_mainSmoke && world.Victories > 0)
            {
                if (_serviceIndex < SpecialistTour.Length)
                {
                    string action = SpecialistTour[_serviceIndex]; var interaction = _session.Interactions.Single(i => i.ActionId == action);
                    if (!Near(interaction.Position, interaction.Range)) return Walk(interaction.Position);
                    Require(_session.Interact(action)); _serviceIndex++; return [];
                }
                if (!_crafted)
                {
                    var forge = _session.Interactions.Single(i => i.ActionId == "service.torren");
                    if (!Near(forge.Position, forge.Range)) return Walk(forge.Position);
                    var state = _session.Capture().Progression.Character;
                    var definitions = _session.Content.Capture();
                    var item = state.Items.FirstOrDefault(i => i.Affixes.Any(a => a.Value < definitions.Affixes.Single(d => d.Id == a.Key).Maximum && !definitions.Affixes.Single(d => d.Id == a.Key).Advanced));
                    if (item is null) throw new InvalidDataException("Production traversal earned no temperable loot.");
                    string affix = item.Affixes.First(a => a.Value < definitions.Affixes.Single(d => d.Id == a.Key).Maximum && !definitions.Affixes.Single(d => d.Id == a.Key).Advanced).Key;
                    Require(_session.Craft(new("client-smoke-temper", CraftingService.Tempering, item.Id, affix)));
                    _crafted = true; return [];
                }
                if (!_passivesChecked)
                {
                    var mara = _session.Interactions.Single(i => i.ActionId == "service.mara");
                    if (!Near(mara.Position, mara.Range)) return Walk(mara.Position);
                    if (_session.ProgressionView.AvailablePassivePoints > 0) Require(_session.AllocatePassive("Defense"));
                    if (_session.ProgressionView.Level >= 5 && _session.ProgressionView.Materials >= _productionDefinition.RespecCost)
                    { Require(_session.Retrain("Arcanist")); _retrained = true; }
                    _passivesChecked = true; return [];
                }
                var exit = _session.Interactions.Single(i => i.ActionId == "dungeon.replay");
                if (!Near(exit.Position, exit.Range)) return Walk(exit.Position);
                Require(_session.Interact("dungeon.replay")); Require(_session.Travel("room.ossuary")); return [];
            }
        }
        if (world.EncounterId is null && world.RoomId != "room.greyhaven")
        {
            var player = _session.Combat.View.Actors.Single(a => a.Id == 1);
            var loot = _session.Combat.View.Loot.OrderBy(l => CorePosition.DistanceSquared(l.Position, player.Position)).FirstOrDefault();
            if (loot is not null) return [.. Walk(loot.Position), new(CombatCommandKind.Pickup, ItemId: loot.Id)];
            if (!_mainSmoke)
            {
                string directory = Path.Combine(_output, "classes", Classes[_classIndex].ToLowerInvariant()); VerifyAndWrite(directory);
                if (_eventCounts.GetValueOrDefault("DamageApplied") == 0) throw new InvalidDataException("Discipline smoke produced no real combat damage.");
                _classReports.Add(new
                {
                    discipline = Classes[_classIndex],
                    stateHash = _session.StateHash,
                    level = _session.ProgressionView.Level,
                    events = new SortedDictionary<string, int>(_eventCounts, StringComparer.Ordinal),
                    replay = Path.Combine(directory, "production.awp")
                });
                _classIndex++; _eventCounts.Clear();
                if (_classIndex == Classes.Length) { _mainSmoke = true; _classIndex = 0; }
                _session = ProductionSession.Create(_combatJson, _adventure, _progression, 42, Classes[_classIndex], _session.Capture().Progression.Profile);
                _revision++; return [];
            }
            string next = world.RoomId switch { "room.ossuary" => "room.cloister", "room.cloister" => "room.bell_sanctum", _ => "room.greyhaven" };
            Require(_session.Travel(next)); return [];
        }
        return CombatInputs();
    }
    private CombatCommand[] CombatInputs()
    {
        var view = _session.Combat.View; var player = view.Actors.Single(a => a.Id == 1);
        var commands = new List<CombatCommand>(CombatProductionSmoke.Commands(view));
        var loot = view.Loot.OrderBy(l => CorePosition.DistanceSquared(l.Position, player.Position)).FirstOrDefault();
        if (loot is not null) commands.Add(new(CombatCommandKind.Pickup, ItemId: loot.Id));
        return commands.ToArray();
    }
    private bool Near(CorePosition position, int range) => CorePosition.DistanceSquared(position, _session.Combat.View.Actors.Single(a => a.Id == 1).Position) <= (long)range * range;
    private CombatCommand[] Walk(CorePosition target)
    {
        var position = _session.Combat.View.Actors.Single(a => a.Id == 1).Position;
        return [new(CombatCommandKind.Move, X: Math.Sign(target.X - position.X), Z: Math.Sign(target.Z - position.Z))];
    }
    private void Require(ProductionResult result)
    { if (!result.Success) throw new InvalidDataException("Production public action failed: " + result.Reason); Consume(result.WorldEvents); }
    private void CompleteSmoke()
    {
        if (_finished) return; _finished = true;
        try
        {
            VerifyAndWrite(_output); var snapshot = _session.Capture();
            if (_classReports.Count != 5 || snapshot.Expedition.Adventure.BellVictories != 1 || !snapshot.Expedition.Adventure.ReturnedToMara || !_crafted || snapshot.Progression.Character.Services.Count != 6)
                throw new InvalidDataException("Production smoke missed a discipline, boss return, specialist unlock or crafting transaction.");
            var report = new
            {
                kind = "ProductionClientSmokePassed",
                steps = _steps,
                stateHash = _session.StateHash,
                classes = _classReports,
                level = _session.ProgressionView.Level,
                services = _session.ProgressionView.Services,
                materials = _session.ProgressionView.Materials,
                equipmentSlots = snapshot.Progression.Character.Equipment.Count,
                retrainingVerified = _retrained,
                bossVictories = snapshot.Expedition.Adventure.BellVictories,
                deaths = snapshot.Expedition.Adventure.Deaths,
                events = new SortedDictionary<string, int>(_eventCounts, StringComparer.Ordinal),
                publicTransactions = new[] { "Five fresh discipline encounters", "Six specialist unlocks", "Permanent item tempering", "Boss return and second expedition", "Per-discipline and integrated save/replay" },
                replay = Path.Combine(_output, "production.awp"),
                display = DisplayServer.GetName(),
                note = "Procedural presentation with real public progression actions. Independent playtest and production art remain separate gates."
            };
            AtomicFile.Write(Path.Combine(_output, "production-client-report.json"), JsonData.Write(report));
            AtomicFile.Write(Path.Combine(_output, "production-world-events.jsonl"), string.Join('\n', _worldEvents));
            GD.Print(JsonData.Write(report)); GetTree().Quit();
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void Safely(Action action)
    { try { action(); } catch (Exception ex) { _journey.Notice(ex.Message); _character.Notice(ex.Message); GD.PushWarning(ex.Message); } }
    private static string? Argument(string prefix) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    private void Fail(Exception ex)
    {
        _finished = true;
        if (_smoke && _session is not null) AtomicFile.Write(Path.Combine(_output, "production-failed-snapshot.json"), JsonData.Write(_session.Capture()));
        GD.PushError(ex.ToString()); GetTree().Quit(1); SetProcess(false);
    }
}
