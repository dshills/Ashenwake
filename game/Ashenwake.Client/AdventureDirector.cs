using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using CorePosition = Ashenwake.Core.Simulation.Position;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Ashenwake.Client;

/// <summary>Presentation adapter for the authoritative, saveable Greyhaven expedition.</summary>
public partial class AdventureDirector : Node3D
{
    private Sandbox _sandbox = null!;
    private AdventureStage _stage = null!;
    private AdventureHud _hud = null!;
    private ExpeditionSession _expedition = null!;
    private AdventureContent _content = null!;
    private AdventureDefinition _definition = null!;
    private CombatContent _combatContent = null!;
    private string _combatJson = "", _output = "";
    private bool _smoke, _finished;
    private int _smokeStage;
    private readonly Dictionary<string, int> _eventCounts = new(StringComparer.Ordinal);
    private readonly List<string> _worldLog = [];
    private readonly List<string> _visited = [];
    private long _smokeSteps;
    private long _uiRevision;
    private int _omittedVisits;

    public override void _Ready()
    {
        try
        {
            _smoke = OS.GetCmdlineUserArgs().Contains("--adventure-smoke");
            _output = Argument("--output=") ?? ProjectSettings.GlobalizePath("user://adventure");
            _combatJson = FileAccess.GetFileAsString("res://combat.json");
            _combatContent = CombatContent.Parse(_combatJson);
            _content = AdventureContent.Parse(FileAccess.GetFileAsString("res://adventure.json"));
            _definition = _content.Capture();
            _expedition = ExpeditionSession.Create(_combatJson, _content, 42);
            _sandbox = new Sandbox(); AddChild(_sandbox);
            _sandbox.SetSession(_expedition.Combat); _sandbox.EnableCampaign();
            _sandbox.AutomaticStep = _smoke;
            _sandbox.AdvanceOverride = Advance;
            _sandbox.SessionOverride = () => _expedition.Combat;
            _sandbox.SaveOverride = Save;
            _sandbox.LoadOverride = Load;
            _sandbox.ReplayOverride = VerifyReplay;
            _stage = new AdventureStage(); AddChild(_stage);
            _hud = new AdventureHud(); _sandbox.AddOverlay(_hud);
            _hud.SetFragmentDescriptions(_combatContent.Fragments.ToDictionary(f => f.Id, f => f.Description));
            _hud.TravelRequested += id => Apply(() => _expedition.Travel(id));
            _hud.InteractionRequested += id => Apply(() => _expedition.Interact(id));
            _hud.ImplantRequested += (slot, id) => Apply(() => _expedition.InstallFragment(slot, id));
            _hud.ManifestationRequested += id => Apply(() => _expedition.SelectManifestation(id));
            _hud.TemperRequested += id => Apply(() => _expedition.Temper(id));
            _hud.EquipGodwroughtRequested += id => Apply(() => _expedition.EquipGodwrought(id));
            _hud.GraftRequested += (id, lineage) => Apply(() => _expedition.Graft(id, lineage, true));
            _hud.SaveRequested += Save; _hud.LoadRequested += Load;
            RefreshWorld();
            _hud.Notice("F: interact near a marker · J: journey map and services · I: equipment and mutations");
        }
        catch (Exception ex) { Fail(ex); }
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (_smoke || _finished || _expedition is null) return;
        if (input.IsActionPressed("aw_journey")) { _hud.Toggle(); GetViewport().SetInputAsHandled(); }
        if (input.IsActionPressed("aw_interact"))
        {
            var player = _expedition.Combat.View.Actors.Single(a => a.Id == 1);
            var closest = _expedition.Interactions.OrderBy(i => CorePosition.DistanceSquared(i.Position, player.Position)).FirstOrDefault();
            if (closest is not null) Apply(() => _expedition.Interact(closest.ActionId));
            else _hud.Notice("No interaction is available here. Clear the encounter and open the journey map with J.");
            GetViewport().SetInputAsHandled();
        }
    }

    private IReadOnlyList<CombatEvent> Advance(CombatCommand[] commands)
    {
        try { return AdvanceCore(commands); }
        catch (Exception ex) { Fail(ex); return []; }
    }
    private IReadOnlyList<CombatEvent> AdvanceCore(CombatCommand[] commands)
    {
        if (_finished) return [];
        if (_smoke)
        {
            if (++_smokeSteps > 12000) throw new InvalidDataException("Adventure smoke exceeded its bounded traversal budget.");
            commands = SmokeCommands();
        }
        var events = _expedition.Step(commands);
        foreach (var e in events) _eventCounts[e.Kind] = _eventCounts.GetValueOrDefault(e.Kind) + 1;
        ConsumeWorldEvents(_expedition.WorldEvents);
        RefreshWorld();
        if (_smoke && _expedition.View.Expedition >= 2 && _expedition.View.RoomId == "room.ossuary")
            Callable.From(CompleteSmoke).CallDeferred();
        return events;
    }

    private void Apply(Func<AdventureResult> action)
    {
        try
        {
            var result = action();
            if (!result.Success) _hud.Notice(result.Reason);
            else
            {
                _uiRevision++;
                ConsumeWorldEvents(result.Events);
                if (!ReferenceEquals(_sandbox.Session, _expedition.Combat)) _sandbox.SetSession(_expedition.Combat);
                RefreshWorld();
            }
        }
        catch (Exception ex) { _hud.Notice(ex.Message); GD.PushWarning(ex.Message); }
    }
    private void ConsumeWorldEvents(IReadOnlyList<string> events)
    {
        if (events.Count > 0) _uiRevision++;
        foreach (string worldEvent in events)
        {
            _worldLog.Add(worldEvent); if (_worldLog.Count > 1000) _worldLog.RemoveAt(0);
            _hud.Notice(worldEvent switch
            {
                "Dialogue:mara.false_history" => "Mara: The wound will hold. Find the Bell Saint beneath Last Mercy. Bring back what it guards.",
                "Dialogue:mara.reward_reaction" => "Mara: A heart that remembers the dead. Implant it, and see what answers your call.",
                "BossDefeated:bell_saint" => "THE BELL IS SILENT · Heart of Serath acquired. Return to Mara in Greyhaven.",
                "BellSaintPhase:2" => "The ritual begins. Attack both glowing anchors before the Saint can be harmed.",
                "BellSaintPhase:3" => "The creature breaks free. Independent bell fragments are ringing—watch their tells.",
                _ when worldEvent.StartsWith("PlayerReturnedToAnchor:", StringComparison.Ordinal) => "You awaken at the last anchor. Your equipment, discoveries and earned rewards remain.",
                _ when worldEvent.StartsWith("FragmentInstalled:", StringComparison.Ordinal) => "Implant installed. Its lineage and Resonance now shape your build.",
                _ when worldEvent.StartsWith("ManifestationSelected:", StringComparison.Ordinal) => _expedition.View.NpcReaction,
                _ => worldEvent.Replace(':', ' ').Replace('_', ' ')
            });
        }
    }
    private void RefreshWorld()
    {
        var view = _expedition.View; var snapshot = _expedition.Capture();
        if (_visited.LastOrDefault() != view.RoomId)
        {
            if (_visited.Count == 256) { _visited.RemoveAt(0); _omittedVisits++; }
            _visited.Add(view.RoomId);
        }
        var player = _expedition.Combat.View.Actors.Single(a => a.Id == 1);
        var interactions = _expedition.Interactions.Select(i => new InteractionDisplay(i.ActionId, i.Name,
            (int)Math.Sqrt(CorePosition.DistanceSquared(i.Position, player.Position)), i.Range)).ToArray();
        _hud.SetView(view, snapshot.Adventure, _definition, interactions, _uiRevision);
        _stage.ShowRoom(view.RoomId, view.BellPhase, _combatContent.Room, view.ActiveManifestations,
            _expedition.Interactions.ToDictionary(i => i.ActionId, i => i.Position), snapshot.Adventure.DestroyedAnchors);
        _sandbox.SetManifestationPresentation(view.ActiveManifestations);
    }

    private void Save()
    {
        ExpeditionSaveStore.Write(Path.Combine(_output, "expedition.save.json"), _combatJson, _content, _expedition.Capture());
        _hud.Notice("Expedition saved: checkpoint, boss phases, discoveries, rolls, implants and Godwrought progress.");
    }
    private void Load()
    {
        var result = ExpeditionSaveStore.Load(Path.Combine(_output, "expedition.save.json"), _combatJson, _content);
        _expedition = result.Session; _uiRevision++; _sandbox.SetSession(_expedition.Combat); RefreshWorld();
        _hud.Notice(result.RecoveredBackup ? "Recovered the previous valid expedition save." : "Expedition restored.");
    }
    private void VerifyReplay()
    {
        var replay = _expedition.CaptureReplay();
        var result = ExpeditionReplayRunner.Run(_combatJson, _content, replay);
        if (!result.Success) throw new InvalidDataException($"Expedition replay diverged at {result.DivergentTick}: {result.Detail}");
        AtomicFile.Write(Path.Combine(_output, "expedition.awx"), JsonData.Write(replay));
        _hud.Notice($"Expedition replay verified · {replay.Frames.Length} world and combat operations.");
    }

    private CombatCommand[] SmokeCommands()
    {
        var world = _expedition.View; var state = _expedition.Capture().Adventure;
        if (world.RoomId == "room.greyhaven")
        {
            if (!state.QuestAccepted)
            {
                Require(_expedition.Interact("npc.mara"));
                Require(_expedition.EquipGodwrought(state.Godwrought[0].InstanceId));
                Require(_expedition.InstallFragment("Spine", "fragment.nerve_ilyra"));
                Require(_expedition.InstallFragment("Arms", "fragment.orrun_bone"));
                Require(_expedition.SelectManifestation("manifestation.stone_memory"));
                Require(_expedition.Travel("room.ossuary")); return [];
            }
            if (state.BellVictories > 0 && !state.ReturnedToMara)
            {
                Require(_expedition.Interact("npc.mara"));
                Require(_expedition.InstallFragment("Heart", "fragment.heart_serath"));
                Require(_expedition.SelectManifestation("manifestation.burning_blood"));
                _smokeStage = 1;
            }
            if (_smokeStage == 1)
            {
                var interaction = _expedition.Interactions.Single(i => i.ActionId == "service.torren");
                if (!Near(interaction)) return WalkTo(interaction.Position);
                Require(_expedition.Temper(state.Godwrought[0].InstanceId)); _smokeStage = 2;
            }
            if (_smokeStage == 2)
            {
                var interaction = _expedition.Interactions.Single(i => i.ActionId == "dungeon.replay");
                if (!Near(interaction)) return WalkTo(interaction.Position);
                Require(_expedition.Interact("dungeon.replay"));
                Require(_expedition.Travel("room.ossuary")); _smokeStage = 3; return [];
            }
            return [];
        }
        if (world.EncounterId is null)
        {
            string next = world.RoomId switch { "room.ossuary" => "room.cloister", "room.cloister" => "room.bell_sanctum", _ => "room.greyhaven" };
            Require(_expedition.Travel(next)); return [];
        }
        var view = _expedition.Combat.View; var player = view.Actors.Single(a => a.Id == 1);
        var target = view.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0)
            .OrderBy(a => a.Role == "Anchor" ? 0 : 1).ThenBy(a => CorePosition.DistanceSquared(a.Position, player.Position)).ThenBy(a => a.Id).FirstOrDefault();
        if (target is null) return [];
        var commands = new List<CombatCommand>();
        if (player.Health < player.MaxHealth / 2) commands.Add(new(CombatCommandKind.Potion));
        int dx = target.Position.X - player.Position.X, dz = target.Position.Z - player.Position.Z;
        commands.Add(new(CombatCommandKind.Move, X: Math.Sign(dx), Z: Math.Sign(dz)));
        if (view.Tick % 16 == 0) commands.Add(new(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: target.Id));
        if (view.Tick % 61 == 0) commands.Add(new(CombatCommandKind.Cast, SkillId: "skill.shield_breaker", TargetId: target.Id));
        if (view.Tick % 91 == 0) commands.Add(new(CombatCommandKind.Cast, SkillId: "skill.seismic_wave", TargetId: target.Id));
        if (view.Tick % 131 == 0) commands.Add(new(CombatCommandKind.Cast, SkillId: "skill.charge", TargetId: target.Id));
        if (view.Tick % 157 == 0) commands.Add(new(CombatCommandKind.Cast, SkillId: "skill.iron_guard", TargetId: target.Id));
        if (view.Tick % 223 == 0) commands.Add(new(CombatCommandKind.Cast, SkillId: "skill.cataclysm", TargetId: target.Id));
        if (view.Tick % 73 == 0 && target.TelegraphTicks > 0) commands.Add(new(CombatCommandKind.Dodge, X: -Math.Sign(dz), Z: Math.Sign(dx)));
        var loot = view.Loot.OrderBy(l => CorePosition.DistanceSquared(l.Position, player.Position)).FirstOrDefault();
        if (loot is not null) commands.Add(new(CombatCommandKind.Pickup, ItemId: loot.Id));
        return commands.ToArray();
    }
    private bool Near(ExpeditionInteraction interaction) => CorePosition.DistanceSquared(_expedition.Combat.View.Actors.Single(a => a.Id == 1).Position, interaction.Position) <= (long)interaction.Range * interaction.Range;
    private CombatCommand[] WalkTo(CorePosition target)
    {
        var position = _expedition.Combat.View.Actors.Single(a => a.Id == 1).Position;
        return [new(CombatCommandKind.Move, X: Math.Sign(target.X - position.X), Z: Math.Sign(target.Z - position.Z))];
    }
    private void Require(AdventureResult result)
    {
        if (!result.Success) throw new InvalidDataException("Adventure scripted action failed: " + result.Reason);
        ConsumeWorldEvents(result.Events);
    }
    private void CompleteSmoke()
    {
        if (_finished) return; _finished = true;
        try
        {
            VerifyReplay(); Save();
            var restored = ExpeditionSaveStore.Load(Path.Combine(_output, "expedition.save.json"), _combatJson, _content).Session;
            var state = _expedition.Capture().Adventure;
            if (restored.StateHash != _expedition.StateHash || state.BellVictories != 1 || !state.ReturnedToMara || state.Expedition != 2 || !state.OwnedFragments.Contains("fragment.heart_serath") || state.Godwrought[0].TemperLevel != 1)
                throw new InvalidDataException("Adventure smoke did not complete the reward, return, rebuild, save and replay loop.");
            var report = new
            {
                kind = "AdventureClientSmokePassed",
                tick = _expedition.Tick,
                stateHash = _expedition.StateHash,
                visited = _visited,
                earlierVisitedRoomsOmitted = _omittedVisits,
                state.BellVictories,
                state.Expedition,
                state.Deaths,
                state.ReturnedToMara,
                ownedFragments = state.OwnedFragments,
                temperLevel = state.Godwrought[0].TemperLevel,
                events = new SortedDictionary<string, int>(_eventCounts, StringComparer.Ordinal),
                display = DisplayServer.GetName(),
                note = "Automated traversal of authoritative combat. Procedural prototype art and synthetic cues; external playtest, production rigging and release certification remain separate gates."
            };
            AtomicFile.Write(Path.Combine(_output, "adventure-client-report.json"), JsonData.Write(report));
            AtomicFile.Write(Path.Combine(_output, "adventure-world-events.jsonl"), string.Join('\n', _worldLog));
            GD.Print(JsonData.Write(report)); GetTree().Quit();
        }
        catch (Exception ex) { Fail(ex); }
    }
    private static string? Argument(string prefix) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    private void Fail(Exception ex)
    {
        if (_smoke && _expedition is not null)
            AtomicFile.Write(Path.Combine(_output, "adventure-failed-snapshot.json"), JsonData.Write(_expedition.Capture()));
        GD.PushError(ex.ToString()); GetTree().Quit(1); SetProcess(false);
    }
}
