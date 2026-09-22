using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Exploration;
using Ashenwake.Core.Serialization;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Detached endgame encounters exercise presentation with ordinary AI, combat,
/// and mechanism commands. Controlled health/invulnerability do not establish earned balance.</summary>
public partial class EndgameCombatPolishSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [], _skipped = [];
    private readonly List<object> _evidence = [];
    private readonly List<CombatEvent> _events = [];
    private readonly HashSet<string> _phaseFixtures = [], _modifierFixtures = [];
    private string _output = "", _content = "", _context = "", _variant = "";
    private string _only = "";
    private bool _writeReport, _profiled;
    private int _commands, _checkpoints;
    private Sandbox _sandbox = null!;
    private EndgamePresentation _effects = null!;
    private EndgameHud _board = null!;
    private EndgameCombatContent _catalog = null!;
    private CombatSession _session = null!;
    private CombatRecorder _recorder = null!;
    private CheckButton _reduced = null!;
    private LocalMapView? _map;

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _only = args.FirstOrDefault(a => a.StartsWith("--only=", StringComparison.Ordinal))?[7..] ?? "";
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--endgame-polish-smoke") || _output.Length == 0)
                throw new InvalidDataException("Endgame polish smoke requires --endgame-polish-smoke --output=<fresh-artifact-directory>.");
            if (Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Endgame polish smoke requires a fresh artifact directory.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _catalog = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat"), Read("campaign-combat")).CombatJson,
                Read("endgame-combat"), EndgameContent.Parse(Read("endgame")));
            _content = _catalog.CombatJson;
            if (_only.Length > 0 && _only != "modifiers" && !CombatContent.Parse(_content).Endgame!.HuntPhases.Any(p => p.Pattern.ToLowerInvariant() == _only))
                throw new InvalidDataException("Unknown diagnostic fixture filter.");
            _sandbox = new Sandbox { ContentJsonOverride = _content, AutomaticStep = false, AdvanceOverride = _ => [] };
            AddChild(_sandbox); _sandbox.SetProcess(false); _sandbox.EnableCampaign();
            _effects = new EndgamePresentation(); AddChild(_effects); _effects.AttachOverlay(_sandbox);
            _board = new EndgameHud(); _sandbox.AddOverlay(_board);
            _sandbox.ConfigureLocalMap(() => true, () => _map);
            _reduced = Descendants(_sandbox).OfType<CheckButton>().Single(b => b.Text == "Reduced visual effects");
            _reduced.ButtonPressed = false; _sandbox.SetGraphicsQuality("High");
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            await CampaignWarningLifecycle();
            foreach (var hunt in EndgameContent.Parse(Read("endgame")).Capture().Hunts)
                for (int phase = 0; phase < 3; phase++) await Hunt(hunt.Id, phase);
            if (_only.Length == 0 || _only == "modifiers") await Modifiers();
            Check("requested_fixture_coverage", _only.Length == 0 ? _phaseFixtures.Count == 15 && _modifierFixtures.Count == 6 :
                _only == "modifiers" ? _modifierFixtures.Count == 6 : _phaseFixtures.SetEquals([_only]));
            _effects.QueueFree(); _sandbox.QueueFree(); await Frames(3); Finish(true, "");
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); Finish(false, ex.Message); }
    }
    private async Task CampaignWarningLifecycle()
    {
        var campaign = CombatSession.CreateEncounter(_content, 42, "campaign.covenant_warden");
        for (int tick = 0; tick < 8 && campaign.View.CampaignHazards!.Count == 0; tick++) { campaign.Step(); _commands++; }
        Check("campaign_warning_lifecycle_fixture", campaign.View.CampaignHazards!.Count > 0);
        _sandbox.SetSession(campaign); _sandbox.PresentAuthoredRoom(campaign.Room, "warning-lifecycle"); _sandbox._Process(0);
        var anchors = SandboxField<Dictionary<long, (Label3D Label, Vector3 Anchor)>>("_warningLabelAnchors");
        Check("campaign_warning_anchors_present_before_replacement", anchors.Count > 0);
        var old = anchors.Values.Select(v => v.Label).ToArray();
        var replacement = _catalog.CreateEncounter(_catalog.CreateHuntManifest("hunt.false_vael", 42, 1), 0, 0);
        _sandbox.SetSession(replacement); _sandbox.PresentAuthoredRoom(replacement.Room, "warning-lifecycle-replaced"); _sandbox._Process(0);
        Check("campaign_warning_anchors_clear_on_replacement", anchors.Count == 0);
        await Frames(3);
        Check("campaign_warning_nodes_freed_on_replacement", old.All(label => !GodotObject.IsInstanceValid(label)));
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://" + name + ".json");
    private CombatSnapshot Fixture(EndgameCombatManifest manifest, int room = 0, string discipline = "Vanguard")
    {
        var build = CombatSession.CreateEncounter(_content, 42, "hub");
        build.ApplyProgressionBuild(new(discipline, Level: 20, Offense: 8, Defense: 8)
        { Resistances = new() { [DamageFamily.Fire] = 2200, [DamageFamily.Frost] = 900, [DamageFamily.PhysicalCrush] = 1000 } });
        var carry = build.Capture(); carry.Fragments.Clear(); carry.Equipment.Clear();
        var state = _catalog.CreateEncounter(manifest, room, 0, carry).Capture();
        state.Actors[0].InvulnerableUntil = 2500; state.Actors[0].Position = new(-3500, 0);
        foreach (var pair in state.Campaign!.Actors) pair.Value.NextEliteTick = 180;
        // One-hit weak points isolate the real shield transition without claiming a normal loadout.
        foreach (var actor in state.Actors.Where(a => state.Endgame!.ActorMechanics.ContainsKey(a.Id))) actor.Health = 1;
        return state;
    }
    private void Load(CombatSnapshot state, string context)
    {
        _context = context; _session = CombatSession.Restore(_content, state); _recorder = new(_session); _events.Clear();
        _sandbox.SetSession(_session); _sandbox.SetPaused(false);
        var room = _session.Room; const int cell = 500;
        var discovered = new LocalMapRoomState
        {
            LayoutHash = JsonData.Hash(room),
            HalfWidth = room.HalfWidth,
            HalfDepth = room.HalfDepth,
            CellSize = cell,
            SeenCells = Enumerable.Range(0,
                ((room.HalfWidth * 2 + cell - 1) / cell) * ((room.HalfDepth * 2 + cell - 1) / cell)).ToArray()
        };
        // Rendering fixture: use the shipping map control with the actual room fully revealed.
        // This does not mutate discovery or claim the character explored these cells.
        _map = (LocalMapView)Activator.CreateInstance(typeof(LocalMapView), System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic, null, [_session.View.Endgame!.ContextKey, room, discovered], null)!;
        var view = _session.View.Endgame!; var manifest = state.Endgame!.Manifest;
        foreach (string rule in manifest.RuleIds) _modifierFixtures.Add(rule);
        var run = new EndgameRunDisplay(manifest.RunId, view.PhaseName, manifest.Kind, "Active", manifest.Region,
            manifest.Tier, view.EncounterIndex + 1, view.EncounterCount, manifest.Kind == "GodHunt" ? 2 : 3, 0, 100,
            manifest.RuleIds, [view.Counterplay], view.BossModifiers, [], false, false, false, true,
            manifest.Rooms.Select(r => r.Name).ToArray());
        _board.SetView(new(true, false, 0, 0, new Dictionary<string, int>(), [], [], run, false, false, 0, "", 0, InExpedition: true));
        _board.SetOpen(false); Refresh(); _sandbox._Process(0);
    }
    private void Step(params CombatCommand[] commands)
    {
        var events = _recorder.Step(_session, commands); _commands++; _events.AddRange(events);
        string hash = _session.StateHash;
        _sandbox.PresentCombatEvents(events, _session); Refresh(); _sandbox._Process(0);
        Check(_context + "_presentation_preserves_state", _session.StateHash == hash);
    }
    private void Wait(int count) { for (int i = 0; i < count; i++) Step(); }
    private void Refresh()
    {
        var combat = _session.View;
        _sandbox.PresentAuthoredRoom(_session.Room, "endgame-polish-fixture:" + _context);
        _sandbox.PresentLocalMap(_map, combat.Endgame!.PhaseName, combat);
        _effects.Show(combat.Endgame, combat.Actors[0].Position, false);
        _sandbox.SetWorldSubtitle("ENDGAME · DETACHED COMBAT FIXTURE · " + _context.Replace('-', ' ').ToUpperInvariant());
    }
    private async Task Hunt(string hunt, int phase)
    {
        var manifest = _catalog.CreateHuntManifest(hunt, 42, phase + 1);
        string context = manifest.Rooms[phase].Pattern.ToLowerInvariant();
        if (_only.Length > 0 && _only != context) return;
        _phaseFixtures.Add(context);
        Load(Fixture(manifest, phase), context); Wait(46);
        var view = _session.View.Endgame!;
        Check(context + "_actual_phase_ai_warned", view.Hazards.Length > 0 && _events.Any(e => e.Kind == "EndgameHazardWarned"));
        Check(context + "_correct_phase", view.PhaseIndex == phase + 1 && view.PhaseCount == 3 && view.HuntId == hunt);
        await InspectPresets(context + "-warnings"); VerifyArchive(context + "-warnings");
        var initial = view.BossCue!;
        if (context == "vael2")
        {
            await Capture(context + "-priority-focused.png", initial.PriorityActorIds[0], "BREAK TO EXPOSE");
            GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await ResumeNative(); Refresh(); _sandbox._Process(0);
            await Capture(context + "-priority-focused-minimum.png", initial.PriorityActorIds[0], "BREAK TO EXPOSE");
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await ResumeNative(); Refresh(); _sandbox._Process(0);
        }
        _evidence.Add(new { kind = "detached-hunt-phase", hunt, phase = phase + 1, warnings = view.Hazards, cue = initial });
        if (initial.PriorityActorIds.Length > 0)
        {
            int target = initial.PriorityActorIds[0];
            await BreakWeakPoint(target);
            var broken = _session.View.Endgame!.BossCue!;
            Check(context + "_public_hit_removes_shield", _session.View.Actors.Single(a => a.Id == target).Health == 0 && !broken.Shielded);
            Check(context + "_weakpoint_event", _events.Any(e => e.Kind == "HuntWeakPointBroken" && e.TargetId == target));
            await InspectPresets(context + "-exposed"); VerifyArchive(context + "-exposed");
            if (broken.VulnerableTicks > 0)
            {
                Check(context + "_timed_exposure_has_other_weakpoint", !broken.PermanentlyVulnerable);
                for (int i = 0; i < 240 && !_session.View.Endgame!.BossCue!.Shielded; i++) Step();
                var restored = _session.View.Endgame!.BossCue!;
                Check(context + "_timed_shield_returns", restored.Shielded && restored.VulnerableTicks == 0 && restored.PriorityActorIds.Length > 0);
                VerifyArchive(context + "-shield-returned");
                foreach (int id in restored.PriorityActorIds) await BreakWeakPoint(id);
                var permanent = _session.View.Endgame!.BossCue!;
                Check(context + "_last_weakpoint_is_permanent", permanent.PermanentlyVulnerable && permanent.VulnerableTicks == 0 && !permanent.Shielded);
                await InspectPresets(context + "-permanent"); VerifyArchive(context + "-permanent");
            }
            else Check(context + "_marked_echo_is_permanent", broken.PermanentlyVulnerable);
        }
        else if (initial.PriorityMechanismIds.Length > 0)
        {
            bool bell = view.Mechanisms.Any(m => m.Kind == "SilentBell");
            if (bell)
            {
                await UseMechanism(initial.PriorityMechanismIds[0]);
                var exposed = _session.View.Endgame!.BossCue!;
                Check(context + "_bell_opens_actual_timer", !exposed.Shielded && exposed.VulnerableTicks > 0 && !exposed.PermanentlyVulnerable);
                await InspectPresets(context + "-bell-exposed"); VerifyArchive(context + "-bell-exposed");
                Wait(exposed.VulnerableTicks + 1);
                Check(context + "_bell_timer_expires", _session.View.Endgame!.BossCue is { Shielded: true, VulnerableTicks: 0 });
                VerifyArchive(context + "-bell-expired");
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    var cue = _session.View.Endgame!.BossCue!;
                    Check(context + "_term_priority_" + i, cue.PriorityMechanismIds.Length > 0);
                    await UseMechanism(cue.PriorityMechanismIds[0]);
                    VerifyArchive(context + "-term-" + i);
                }
                Check(context + "_deposited_terms_permanently_expose", _session.View.Endgame!.BossCue is { PermanentlyVulnerable: true, Shielded: false, VulnerableTicks: 0 });
                await InspectPresets(context + "-terms-deposited");
            }
        }
        else if (context == "ilyra3")
        {
            Check(context + "_feeding_pause_is_timed", initial.VulnerableTicks > 0 && !initial.PermanentlyVulnerable);
            Wait(initial.VulnerableTicks + 1);
            Check(context + "_feeding_pause_expires", _session.View.Endgame!.BossCue is { Shielded: true, VulnerableTicks: 0 });
            await InspectPresets(context + "-feeding-ended"); VerifyArchive(context + "-feeding-ended");
        }
    }
    private async Task BreakWeakPoint(int id)
    {
        WalkTo(_session.View.Actors.Single(a => a.Id == id).Position, 1700);
        for (int tick = 0; tick < 60 && _session.View.Skills.Single(s => s.Id == "skill.cleave").RemainingTicks > 0; tick++) Step();
        Step(new CombatCommand(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: id)); Wait(7);
        Check(_context + "_weakpoint_destroyed_" + id, _session.View.Actors.Single(a => a.Id == id).Health == 0);
        await Frames(1);
    }
    private async Task UseMechanism(int id)
    {
        var mechanism = _session.View.Endgame!.Mechanisms.Single(m => m.Id == id);
        WalkTo(mechanism.Position, mechanism.Radius - 100);
        Step(new CombatCommand(CombatCommandKind.InteractMechanism, TargetId: id));
        Check(_context + "_mechanism_used_" + id, _events.Any(e => e.Kind == "HuntMechanismUsed" && e.TargetId == id));
        await Frames(1);
    }
    private void WalkTo(CorePosition destination, int radius)
    {
        for (int i = 0; i < 400 && CorePosition.DistanceSquared(_session.View.Actors[0].Position, destination) > (long)radius * radius; i++)
        {
            var direction = CombatProductionSmoke.MovementDirection(_session.View.Actors[0].Position, destination, _session.Room,
                _session.View.Actors.Where(a => a.Id != 1 && a.Health > 0 && a.Position != destination).Select(a => a.Position).ToArray());
            Step(new CombatCommand(CombatCommandKind.Move, X: direction.X, Z: direction.Z));
        }
        Step(new CombatCommand(CombatCommandKind.Stop));
        _evidence.Add(new { kind = "public-walk", context = _context, destination, radius, actual = _session.View.Actors[0].Position });
        Check(_context + "_walk_reaches_" + destination.X + "_" + destination.Z,
            CorePosition.DistanceSquared(_session.View.Actors[0].Position, destination) <= (long)radius * radius);
    }
    private EndgameCombatManifest Fracture(params string[] rules)
        => _catalog.CreateFractureManifest(new(1, 42, "act.grey_march", rules.Contains("fracture.inherited_boss") ? 4 : 3, rules, "Orrun", "Materials"), 20);
    private CombatSnapshot Quiet(EndgameCombatManifest manifest, int room = 0, string discipline = "Vanguard")
    {
        var state = Fixture(manifest, room, discipline);
        foreach (var actor in state.Actors.Where(a => a.Faction == CombatFaction.Enemy)) actor.RecoveryUntil = 2500;
        return state;
    }
    private async Task Modifiers()
    {
        var state = Quiet(Fracture("fracture.healing_echoes")); state.Actors[0].Health = 80;
        Load(state, "healing-echoes"); Step(new CombatCommand(CombatCommandKind.Potion));
        Check("healing_actual_potion_creates_echo", _events.Any(e => e.Kind == "EndgameHealingEchoCreated"));
        await InspectPresets("healing-warning"); VerifyArchive("healing-warning");
        Wait((int)_session.View.Endgame!.Hazards.Single(h => h.ContentId == "endgame.healing_echo").RemainingTicks);
        await InspectPresets("healing-active"); VerifyArchive("healing-active"); Wait(2);
        Check("healing_echo_cleans_up", !_session.View.Endgame!.Hazards.Any(h => h.ContentId == "endgame.healing_echo"));

        state = Quiet(Fracture("fracture.elite_hazards"));
        var elite = state.Actors.Single(a => a.Faction == CombatFaction.Enemy && a.Elite); elite.Health = 1;
        state.Actors[0].Position = new(elite.Position.X - 1200, elite.Position.Z);
        Load(state, "elite-scars"); Step(new CombatCommand(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: elite.Id)); Wait(8);
        Check("scar_actual_elite_death", _events.Any(e => e.Kind == "EndgameEliteScarCreated"));
        await InspectPresets("scar-warning"); VerifyArchive("scar-warning");
        Wait((int)_session.View.Endgame!.Hazards.Single(h => h.ContentId == "endgame.elite_scar").RemainingTicks + 1);
        await InspectPresets("scar-active"); VerifyArchive("scar-active");
        Check("scar_persistent_and_bounded", _session.View.Endgame!.Hazards.Single(h => h.ContentId == "endgame.elite_scar").Stage == "Active");

        state = Quiet(Fracture("fracture.fragment_overcharge", "fracture.resistance_inversion"));
        Load(state, "surge-inversion");
        Check("surge_active_truth", _session.View.Endgame is { OverchargeActive: true, OverchargeRemainingTicks: 30 });
        await InspectPresets("surge-active"); VerifyArchive("surge-active"); Wait(31);
        Check("surge_inactive_truth", _session.View.Endgame is { OverchargeActive: false, OverchargeRemainingTicks: 0, NextOverchargeTicks: 89 });
        await InspectPresets("surge-next"); VerifyArchive("surge-next");
        Check("inversion_extremes_are_truthful", _session.View.Endgame is { HighestResistanceFamily: DamageFamily.Fire, HighestResistanceBasisPoints: 2200, LowestResistanceBasisPoints: -1500 });

        state = Fixture(Fracture("fracture.burning_haste"), discipline: "Arcanist");
        var target = state.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Role == "Melee");
        target.Position = new(-1500, 0); state.Actors[state.Actors.IndexOf(target)] = target with { Health = 1000, MaxHealth = 1000 };
        foreach (var actor in state.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Id != target.Id)) actor.RecoveryUntil = 2500;
        Load(state, "burning-haste"); Step(new CombatCommand(CombatCommandKind.Move, X: -1), new CombatCommand(CombatCommandKind.Cast, SkillId: "skill.fire_lance", TargetId: target.Id));
        for (int i = 0; i < 60 && _session.View.Endgame!.HastedActorIds?.Contains(target.Id) != true; i++) Step();
        Check("burning_haste_from_public_fire_lance", _events.Any(e => e.Kind == "StatusApplied" && e.TargetId == target.Id && e.ContentId == "Burning") && _session.View.Endgame!.HastedActorIds?.Contains(target.Id) == true);
        await InspectPresets("burning-haste"); VerifyArchive("burning-haste");

        var inherited = Fracture("fracture.inherited_boss");
        Load(Quiet(inherited, 3), "inherited-boss");
        Check("inheritance_actual_manifest_traits", _session.View.Endgame!.BossModifiers.SequenceEqual(inherited.Rooms[3].EliteModifiers) && inherited.Rooms[3].EliteModifiers.Length > 0);
        await InspectPresets("inherited-boss"); VerifyArchive("inherited-boss");
        await CombinedModifierStress();
    }
    private async Task CombinedModifierStress()
    {
        var manifest = Fracture("fracture.inherited_boss", "fracture.fragment_overcharge", "fracture.resistance_inversion");
        var state = Quiet(manifest, 3); var boss = state.Actors.Single(a => a.DefinitionId.StartsWith("boss.fracture_", StringComparison.Ordinal));
        Check("combined_fixture_has_two_inherited_traits", manifest.Rooms[3].EliteModifiers.Length == 2);
        // Fixture-only property projections exercise crowded HUD states. They are not earned items.
        state.ProgressionBuild = state.ProgressionBuild with { CinderCycle = true, WidowEcho = true, BarrierOnDodge = true };
        state.Momentum = 60; state.Actors[0].Position = new(boss.Position.X - 1200, boss.Position.Z);
        Load(state, "combined-modifiers"); Wait(46);
        Step(new CombatCommand(CombatCommandKind.Cast, SkillId: "skill.shield_breaker", TargetId: boss.Id)); Wait(25);
        Step(new CombatCommand(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: boss.Id)); Wait(7);
        Step(new CombatCommand(CombatCommandKind.Dodge, X: -1));
        _evidence.Add(new
        {
            kind = "combined-property-commands",
            readiness = _sandbox.LegendaryReadinessText,
            trigger = _sandbox.LegendaryTriggerText,
            events = _events.Where(e => e.Kind is "LegendaryReadied" or "LegendaryTriggered" or "CommandRejected" or "AbilityStarted" or "BarrierGranted").ToArray()
        });
        Check("combined_actual_legendary_trigger", _events.Any(e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.FurnacePower) &&
            _sandbox.LegendaryReadinessText.Contains("CINDER", StringComparison.Ordinal) && _sandbox.LegendaryReadinessText.Contains("WIDOW READY", StringComparison.Ordinal) &&
            _sandbox.LegendaryTriggerText.Contains("CINDER CYCLE", StringComparison.Ordinal));
        Check("combined_actual_barrier_status", _session.View.Actors[0].Barrier > 0 && SandboxField<CombatStatusStrip>("_statusStrip").ActiveCount > 0);
        await InspectPresets("combined-modifiers", boss.Id, "STORMBOUND"); VerifyArchive("combined-modifiers");
        _evidence.Add(new
        {
            kind = "detached-combined-modifier-stress",
            rules = manifest.RuleIds,
            traits = manifest.Rooms[3].EliteModifiers,
            fixtureProperties = new[] { "CinderCycle", "WidowEcho", "BarrierOnDodge" },
            readiness = _sandbox.LegendaryReadinessText,
            trigger = _sandbox.LegendaryTriggerText,
            barriers = _session.View.Actors[0].Barrier
        });
    }
    private async Task InspectPresets(string context, int focusedActor = 0, string expectedCondition = "")
    {
        string hash = _session.StateHash;
        _sandbox.SetModalPaused("endgame-polish-check", true); _sandbox._Process(0);
        Check(context + "_pause_preserves_hash", _sandbox.IsPaused && _session.StateHash == hash);
        _sandbox.SetModalPaused("endgame-polish-check", false);
        foreach (var (name, width, reduced) in new[] { ("high", 1280, false), ("performance", 1280, false), ("reduced", 1280, true), ("minimum", 780, true) })
        {
            _variant = name; GetWindow().Size = GetWindow().ContentScaleSize = new(width, width == 780 ? 720 : 800);
            _sandbox.SetGraphicsQuality(name is "performance" or "minimum" ? "Performance" : "High"); _reduced.ButtonPressed = reduced;
            await ResumeNative(); Refresh(); _sandbox._Process(0); await Frames(3);
            if (focusedActor != 0) await FocusActor(focusedActor);
            Check(context + "_preset_preserves_hash", _session.StateHash == hash);
            if (!_profiled && _context == "orrun3" && name == "minimum")
            {
                var samples = new double[100];
                for (int i = 0; i < samples.Length; i++)
                { var watch = System.Diagnostics.Stopwatch.StartNew(); _sandbox._Process(0); samples[i] = watch.Elapsed.TotalMilliseconds; }
                var sorted = samples.Order().ToArray();
                _evidence.Add(new
                {
                    kind = "whole-sandbox-presentation-cpu-sample",
                    context = _context,
                    variant = name,
                    frames = samples.Length,
                    meanMilliseconds = samples.Average(),
                    p95Milliseconds = sorted[94],
                    maximumMilliseconds = sorted[^1],
                    scope = "Entire manual Sandbox._Process(0), including label placement and other presentation work; excludes renderer/GPU and is not a frame-rate certification."
                });
                Check(context + "_profile_preserves_state", _session.StateHash == hash); _profiled = true;
            }
            await Capture(context + "-" + name + ".png", focusedActor, expectedCondition);
        }
        _variant = ""; _reduced.ButtonPressed = false; _sandbox.SetGraphicsQuality("High");
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await ResumeNative(); Refresh(); _sandbox._Process(0); await Frames(3);
    }
    private async Task ResumeNative()
    {
        if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
        await Frames(3);
        var resume = Descendants(_sandbox).OfType<Button>().SingleOrDefault(b => b.Text == "Resume playing" && b.IsVisibleInTree());
        resume?.EmitSignal(BaseButton.SignalName.Pressed); _sandbox.SetPaused(false);
    }
    private void InspectPresentation()
    {
        var view = _session.View.Endgame!;
        var camera = _sandbox.GetChildren().OfType<Camera3D>().Single();
        var labels = new List<Label3D>();
        foreach (var hazard in view.Hazards)
        {
            var label = Descendants(_effects).OfType<Label3D>().Single(n => n.Name == "EndgameHazard_" + hazard.Id && !n.IsQueuedForDeletion());
            Check(_context + "_named_hazard_" + hazard.Id, label.IsVisibleInTree() && label.Text.Length > 3 && label.NoDepthTest);
            Check(_context + "_hazard_stage_" + hazard.Id, label.Text.Contains(hazard.Stage == "Warning" ? Seconds(hazard.RemainingTicks) : hazard.ContentId == "endgame.elite_scar" ? "PERSISTENT" : "DANGER", StringComparison.Ordinal));
            if (hazard.SequenceCount > 0) Check(_context + "_hazard_sequence_" + hazard.Id, label.Text.Contains($"{hazard.SequenceIndex}/{hazard.SequenceCount}", StringComparison.Ordinal));
            if (hazard.LaneIndex > 0) Check(_context + "_hazard_lane_" + hazard.Id, label.Text.Contains("LANE " + hazard.LaneIndex, StringComparison.Ordinal));
            if (!hazard.ContentId.EndsWith(".strike", StringComparison.Ordinal))
                Check(_context + "_authored_hazard_title_" + hazard.Id, !label.Text.StartsWith("BOSS STRIKE", StringComparison.Ordinal));
            var root = label.GetParent<Node3D>();
            var start = new Vector3(hazard.Position.X * .001f, 0, hazard.Position.Z * .001f);
            var end = new Vector3(hazard.End.X * .001f, 0, hazard.End.Z * .001f);
            Check(_context + "_authoritative_hazard_center_" + hazard.Id, root.Position.IsEqualApprox(hazard.Kind == "Line" ? (start + end) * .5f : start));
            var fill = root.GetChildren().OfType<MeshInstance3D>().First();
            Check(_context + "_authoritative_hazard_radius_" + hazard.Id, hazard.Kind == "Line"
                ? fill.Mesh is BoxMesh box && Mathf.IsEqualApprox(box.Size.X, hazard.Radius * .002f) && Mathf.IsEqualApprox(box.Size.Z, (end - start).Length())
                : fill.Mesh is CylinderMesh cylinder && Mathf.IsEqualApprox(cylinder.TopRadius, hazard.Radius * .001f));
            labels.Add(label);
        }
        foreach (var mechanism in view.Mechanisms)
        {
            var label = Descendants(_effects).OfType<Label3D>().Single(n => n.Name == "MechanismLabel_" + mechanism.Id && !n.IsQueuedForDeletion());
            Check(_context + "_mechanism_visible_" + mechanism.Id, label.IsVisibleInTree());
            if (view.BossCue?.PriorityMechanismIds.Contains(mechanism.Id) == true)
                Check(_context + "_priority_mechanism_" + mechanism.Id, label.Text.Contains("USE TO EXPOSE", StringComparison.Ordinal));
            labels.Add(label);
        }
        if (view.BossCue is { } cue)
        {
            string text = ActorLabel(cue.BossId).Text;
            Check(_context + "_boss_state_text", cue.Shielded ? text.Contains("PROTECTED", StringComparison.Ordinal) :
                cue.VulnerableTicks > 0 ? text.Contains("VULNERABLE " + Seconds(cue.VulnerableTicks), StringComparison.Ordinal) :
                !cue.PermanentlyVulnerable || text.Contains("SHIELD BROKEN", StringComparison.Ordinal));
            Check(_context + "_no_false_vulnerability_countdown", cue.VulnerableTicks > 0 || !text.Contains("VULNERABLE ", StringComparison.Ordinal));
            if (cue.RecoveryTicks > 0) Check(_context + "_recovery_text", text.Contains("RECOVERING " + Seconds(cue.RecoveryTicks), StringComparison.Ordinal));
            foreach (int id in cue.PriorityActorIds)
                Check(_context + "_priority_actor_" + id, ActorLabel(id).Text.Contains("BREAK TO EXPOSE", StringComparison.Ordinal));
        }
        labels.AddRange(_session.View.Actors.Where(a => a.Health > 0).Select(a => ActorLabel(a.Id)).Where(l => l.IsVisibleInTree()));
        var rectangles = labels.Select(l => Sandbox.CampaignLabelScreenRect(camera, l)).ToArray();
        Check(_context + "_world_labels_in_viewport", rectangles.All(GetViewport().GetVisibleRect().Encloses));
        bool separate = rectangles.SelectMany((r, i) => rectangles.Skip(i + 1).Select(other => !r.Intersects(other))).All(clear => clear);
        if (!separate) _evidence.Add(new
        {
            kind = "overlapping-labels",
            context = _context,
            variant = _variant,
            labels = labels.Select((label, index) => new { name = label.Name.ToString(), label.Text, rectangle = rectangles[index].ToString() }).ToArray()
        });
        Check(_context + "_world_labels_separate", separate);
        var panel = Descendants(_sandbox).OfType<PanelContainer>().Single(n => n.Name == "EndgameRulePanel");
        var headline = Descendants(_sandbox).OfType<PanelContainer>().Single(n => n.Name == "HudExpeditionObjective");
        var map = _sandbox.LocalMapControl!;
        Check(_context + "_real_hud_visible", panel.IsVisibleInTree() && headline.IsVisibleInTree() && map.IsVisibleInTree());
        var overlays = new List<Rect2> { panel.GetGlobalRect(), headline.GetGlobalRect(), map.GetGlobalRect() };
        var card = Descendants(_sandbox).OfType<PanelContainer>().Single(n => n.Name == "CombatTargetDetail");
        if (card.IsVisibleInTree()) overlays.Add(card.GetGlobalRect());
        var auxiliary = new[] { "_legendaryReadiness", "_legendaryTrigger", "_statusStrip" }
            .Select(OptionalSandboxControl).Where(n => n is not null && n.IsVisibleInTree() && !n.IsQueuedForDeletion()).Cast<Control>().ToArray();
        overlays.AddRange(auxiliary.Select(n => n.GetGlobalRect()));
        Check(_context + "_hud_fits_viewport", overlays.All(GetViewport().GetVisibleRect().Encloses));
        bool hudSeparate = overlays.SelectMany((r, i) => overlays.Skip(i + 1).Select(other => !r.Intersects(other))).All(clear => clear);
        if (!hudSeparate) _evidence.Add(new
        {
            kind = "overlapping-hud",
            context = _context,
            variant = _variant,
            rules = panel.GetGlobalRect().ToString(),
            headline = headline.GetGlobalRect().ToString(),
            map = map.GetGlobalRect().ToString(),
            card = card.IsVisibleInTree() ? card.GetGlobalRect().ToString() : "hidden",
            auxiliary = auxiliary.Select(n => new { name = n.Name.ToString(), rectangle = n.GetGlobalRect().ToString() }).ToArray()
        });
        Check(_context + "_hud_panels_separate", hudSeparate);
        Check(_context + "_world_labels_clear_hud", rectangles.All(r => overlays.All(other => !r.Intersects(other))));
        var rules = Descendants(panel).OfType<Label>().Single(n => n.Name == "EndgameRuleCues");
        Check(_context + "_counterplay_visible", rules.IsVisibleInTree() && rules.Text.Length > 0);
        foreach (string id in view.RuleIds)
        {
            string token = id switch
            {
                "fracture.healing_echoes" => "HEAL",
                "fracture.elite_hazards" => "SCAR",
                "fracture.fragment_overcharge" => "SURGE",
                "fracture.resistance_inversion" => "SHELTER",
                "fracture.burning_haste" => "CINDERS",
                "fracture.inherited_boss" => "INHERIT",
                _ => throw new InvalidDataException("Unexpected modifier.")
            };
            Check(_context + "_modifier_visible_" + id, rules.Text.Contains(token, StringComparison.OrdinalIgnoreCase));
        }
    }
    private async Task FocusActor(int id)
    {
        string hash = _session.StateHash;
        // Native resize/focus changes can invoke the shipping interruption pause.
        // Release it through its real Resume control before exercising pointer focus.
        if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
        await Frames(5);
        if (_sandbox.IsPaused)
        {
            var resume = Descendants(_sandbox).OfType<Button>().SingleOrDefault(b => b.Text == "Resume playing" && b.IsVisibleInTree());
            Check(_context + "_focus_resume_control_available", resume is not null);
            resume!.EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
        }
        Check(_context + "_focus_is_unpaused", !_sandbox.IsPaused);
        var camera = _sandbox.GetChildren().OfType<Camera3D>().Single();
        var root = _sandbox.GetNode<Node3D>("Actor" + id);
        var points = new[] { camera.UnprojectPosition(root.GlobalPosition + Vector3.Up) }.Concat(
            Descendants(root).OfType<MeshInstance3D>().Where(m => m.IsVisibleInTree() && m.Mesh is not null)
                .Select(m => camera.UnprojectPosition(m.GlobalTransform * m.Mesh.GetAabb().GetCenter())));
        bool focused = false;
        foreach (var point in points)
        {
            if (!GetViewport().GetVisibleRect().HasPoint(point)) continue;
            if (DisplayServer.GetName() != "headless")
            {
                // Keep the physical cursor aligned with the injected viewport event.
                // Otherwise a native resize can deliver a later MouseExited event
                // that clears the successfully picked transient hover before capture.
                Input.WarpMouse(point); await Frames(2);
            }
            using var motion = new InputEventMouseMotion { Position = point };
            GetViewport().PushInput(motion, true);
            // Match the existing mouse-readability smoke: use the input adapter's
            // actual pick and its presentation pass, without stepping gameplay.
            typeof(Sandbox).GetMethod("UpdateWorldHover", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(_sandbox, [.1d]);
            _sandbox._Process(0); await Frames(2);
            int actual = SandboxField<int>("_targetDetailActor");
            int hovered = SandboxField<int>("_hoveredCombatActor");
            if (actual == id && hovered == id) { focused = true; break; }
        }
        Check(_context + "_real_pointer_focus_" + GetWindow().Size.X, focused);
        Check(_context + "_pointer_preserves_state_" + GetWindow().Size.X, hash == _session.StateHash);
    }
    private void VerifyArchive(string context)
    {
        _checkpoints++;
        string hash = _session.StateHash;
        var save = JsonData.Write(new CombatSave(1, hash, _session.Capture()));
        var restored = CombatSession.Restore(_content, CombatSaveStore.Read(save, _content));
        Check(context + "_save_restores_identically", restored.StateHash == hash);
        var replay = _recorder.Capture(); var verified = CombatReplayRunner.Run(_content, replay);
        Check(context + "_commands_replay_identically", verified.Success && verified.FinalHash == hash);
        System.IO.File.WriteAllText(Path.Combine(_output, context + ".save.json"), save);
        System.IO.File.WriteAllText(Path.Combine(_output, context + ".awcombat"), JsonData.Write(replay));
    }
    private Label3D ActorLabel(int id) => _sandbox.GetNode<Node3D>("Actor" + id).GetChildren().OfType<Label3D>().Single();
    private static string Seconds(long ticks) => (Math.Ceiling(ticks / 3d) / 10).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "s";
    private static IEnumerable<Node> Descendants(Node node)
    { foreach (var child in node.GetChildren()) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; } }
    private Control? OptionalSandboxControl(string name)
    {
        var field = typeof(Sandbox).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidDataException("Missing required diagnostic control: " + name);
        return field.GetValue(_sandbox) switch
        {
            null => null,
            Control control => control,
            _ => throw new InvalidDataException("Incompatible diagnostic control: " + name)
        };
    }
    private T SandboxField<T>(string name)
    {
        var field = typeof(Sandbox).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidDataException("Missing required diagnostic field: " + name);
        return field.GetValue(_sandbox) is T value ? value
            : throw new InvalidDataException("Uninitialized or incompatible diagnostic field: " + name);
    }
    private async Task Capture(string name, int focusedActor = 0, string expectedCondition = "")
    {
        bool requested = OS.GetCmdlineUserArgs().Contains("--capture-endgame-polish");
        bool nativeCapture = requested && DisplayServer.GetName() != "headless";
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (focusedActor != 0) await FocusActor(focusedActor);
            else if (attempt > 0) await ResumeNative();
            await Frames(3);
            if (nativeCapture)
            {
                // Native occluded/static windows can stop emitting FramePostDraw.
                // Match the regional diagnostics by rendering this requested frame
                // synchronously, then validate and read it without another await.
                RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
            }
            // Validate the exact frame being saved. All expected fields and controls
            // are required: a renamed or uninitialized modal must fail closed.
            var blocking = new[] { "_resumePanel", "_resumeBackdrop", "_settingsPanel", "_inventoryPanel", "_lootPanel" }
                .Where(field => SandboxField<Control>(field).IsVisibleInTree()).ToArray();
            bool current = !_sandbox.IsPaused;
            if (focusedActor != 0)
            {
                int actual = SandboxField<int>("_targetDetailActor"), hovered = SandboxField<int>("_hoveredCombatActor");
                var card = Descendants(_sandbox).OfType<PanelContainer>().Single(n => n.Name == "CombatTargetDetail");
                var detail = Descendants(card).OfType<Label>().Single(n => n.Name == "TargetConditions");
                bool visible = card.IsVisibleInTree(), contained = GetViewport().GetVisibleRect().Encloses(card.GetGlobalRect());
                current = !_sandbox.IsPaused && actual == focusedActor && hovered == focusedActor && visible &&
                    detail.Text.Contains(expectedCondition, StringComparison.Ordinal) && contained;
                _evidence.Add(new
                {
                    kind = "focused-capture-frame",
                    name,
                    attempt,
                    expectedActor = focusedActor,
                    actualActor = actual,
                    hoveredActor = hovered,
                    paused = _sandbox.IsPaused,
                    visible,
                    contained,
                    detail = detail.Text,
                    expectedCondition,
                    cardRect = card.GetGlobalRect().ToString(),
                    viewportRect = GetViewport().GetVisibleRect().ToString(),
                    pointer = GetViewport().GetMousePosition().ToString(),
                    gui = GetViewport().GuiGetHoveredControl()?.GetPath().ToString(),
                    blocking,
                    current
                });
            }
            if (blocking.Length > 0 || !current)
            {
                if (attempt < 3) continue; // Bounded native settle/resume/refocus; never save a rejected frame.
                GD.Print(JsonData.Write(_evidence.LastOrDefault()));
                Check("capture_unobstructed_" + name, blocking.Length == 0);
                Check("capture_current_focus_" + name, current);
            }
            Check("capture_unobstructed_" + name, true);
            Check("capture_current_frame_" + name, current);
            if (focusedActor != 0) Check("capture_current_focus_" + name, true);
            InspectPresentation();
            if (!requested) return;
            if (!nativeCapture) { if (!_skipped.Contains("native screenshots")) _skipped.Add("native screenshots"); return; }
            // No await separates these checks from the image read.
            using var image = GetViewport().GetTexture().GetImage();
            Check("capture_" + name, image.GetWidth() == GetWindow().ContentScaleSize.X && image.GetHeight() == GetWindow().ContentScaleSize.Y && image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
            return;
        }
    }
    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // Keep shipping cosmetic layout running after Control containers reflow,
            // while the detached command recorder remains the only gameplay clock.
            if (_session is not null && GodotObject.IsInstanceValid(_sandbox) && !_sandbox.IsQueuedForDeletion())
            { Refresh(); _sandbox._Process(0); }
        }
    }
    private void Check(string name, bool passed)
    {
        if (_variant.Length > 0) name += "_" + _variant;
        _checks[name] = passed;
        if (!passed) throw new InvalidDataException("Endgame polish check failed: " + name);
    }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "EndgameCombatPolishClientSmokePassed" : "EndgameCombatPolishClientSmokeFailed",
            passed,
            requestedFixture = _only,
            phaseFixtures = _phaseFixtures.Order().ToArray(),
            modifierFixtures = _modifierFixtures.Order().ToArray(),
            checks = _checks,
            commands = _commands,
            checkpoints = _checkpoints,
            evidence = _evidence,
            captures = _captures,
            skippedChecks = _skipped,
            error,
            scope = "Detached level20 Offense8 Defense8 fixtures, controlled health and invulnerability, no equipment/fragments; combined stress explicitly projects CinderCycle, WidowEcho, BarrierOnDodge properties. All 15 God Hunt phases and six Fracture modifiers use actual AI, public combat/mechanism commands, save/command replay. Shipping expedition headline/local map are shown with fixture-only full map reveal. High, Performance, Reduced effects and 780px captures verify readability, not earned progression, survival or human playtesting."
        };
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "endgame-polish-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
