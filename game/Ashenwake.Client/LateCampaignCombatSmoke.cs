using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;
using Godot;

namespace Ashenwake.Client;

/// <summary>Detached encounter fixtures isolate readable support and boss windows. Actual
/// public combat commands resolve each branch; no campaign completion or rewards are claimed.</summary>
public partial class LateCampaignCombatSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [], _skipped = [];
    private readonly List<object> _evidence = [];
    private string _output = "", _content = "", _context = "", _variant = "";
    private bool _writeReport;
    private int _commands;
    private Sandbox _sandbox = null!;
    private CampaignStage _stage = null!;
    private CombatSession _session = null!;
    private CombatRecorder _recorder = null!;
    private CheckButton _reduced = null!;
    private readonly List<CombatEvent> _events = [];

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--late-campaign-combat-smoke") || _output.Length == 0)
                throw new InvalidDataException("Late campaign combat smoke requires --late-campaign-combat-smoke --output=<fresh-artifact-directory>.");
            if (Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Late campaign combat smoke requires a fresh artifact directory.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _content = CampaignCombatContent.Parse(Read("combat"), Read("campaign-combat")).CombatJson;
            _sandbox = new Sandbox { ContentJsonOverride = _content, AutomaticStep = false, AdvanceOverride = _ => [] };
            AddChild(_sandbox); _sandbox.SetProcess(false); _sandbox.EnableCampaign();
            _stage = new CampaignStage(); AddChild(_stage);
            _reduced = Descendants(_sandbox).OfType<CheckButton>().Single(b => b.Text == "Reduced visual effects");
            _reduced.ButtonPressed = false; _sandbox.SetGraphicsQuality("High");
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            await Support();
            await OrdinaryWarnings("campaign.contract_hall", ["campaign.oathmark"]);
            await OrdinaryWarnings("campaign.repeating_rooms", ["campaign.memoryarrow", "campaign.shadowdouble"]);
            await OrdinaryWarnings("campaign.identity_memory", ["campaign.causalecho", "rule.causalechoes"]);
            for (int phase = 1; phase <= 2; phase++) await Boss("campaign.covenant_warden", "boss.covenant_warden", phase);
            for (int phase = 1; phase <= 3; phase++) await Boss("campaign.breach_heart", "boss.breach_heart", phase);
            _sandbox.QueueFree(); _stage.QueueFree(); await Frames(3); Finish(true, "");
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); Finish(false, ex.Message); }
    }

    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://" + name + ".json");
    private CombatSnapshot Fixture(string encounter)
    {
        var state = CombatSession.CreateEncounter(_content, 42, encounter).Capture();
        state.Fragments.Clear(); state.Equipment.Clear(); state.Momentum = 100;
        state.Actors[0].Position = new(-4200, 0);
        // Fixture-only invulnerability and disabled room hazards isolate presentation
        // from survival. This is not an earned build or campaign route.
        state.Actors[0].InvulnerableUntil = 2500;
        foreach (var info in state.Campaign!.Actors.Values) info.NextEliteTick = 180;
        state.Campaign.NextHazardTick = 0;
        return state;
    }

    private void Load(CombatSnapshot state, string context)
    {
        _context = context; _session = CombatSession.Restore(_content, state); _recorder = new(_session); _events.Clear();
        _sandbox.SetSession(_session); _sandbox.SetPaused(false); Refresh(); _sandbox._Process(0);
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
        int act = _session.EncounterId is "campaign.contract_hall" or "campaign.covenant_warden" ? 4 : 5;
        string style = _session.EncounterId switch
        {
            "campaign.contract_hall" => "spine_hall",
            "campaign.covenant_warden" => "spine_warden",
            "campaign.repeating_rooms" => "hollow_rooms",
            "campaign.identity_memory" => "hollow_memory",
            _ => "hollow_breach"
        };
        _sandbox.PresentAuthoredRoom(_session.Room, "late-depth-fixture:" + _session.EncounterId, style);
        var state = new CampaignState { CurrentAct = act, HighestActVisited = act, InHub = false };
        var view = new CampaignView(act, act == 4 ? "Shattered Spine" : "Hollow Night", "", "", "", [], _session.EncounterId, [], [], [], [], [], null);
        _stage.Show(state, view, _session.Room, [], [], 0, _session.View.Actors[0].Position, _session.View.BossPhase,
            combat: _session.View, activeEncounterId: _session.EncounterId);
        _sandbox.SetWorldSubtitle("ACT " + act + " · DETACHED COMBAT FIXTURE · " + _context.Replace('-', ' ').ToUpperInvariant());
    }

    private async Task Support()
    {
        const string power = "campaign.oath_ward", prefix = "oath-ward";
        var state = Fixture("campaign.contract_hall");
        var source = state.Actors.First(a => a.DefinitionId == "enemy.contract_keeper");
        var allies = state.Actors.Where(a => a.DefinitionId is "enemy.bone_sentinel" or "enemy.oath_giant").Take(2).ToArray();
        Check("ward_fixture_has_eligible_allies", allies.Length > 0);
        source.Position = new(-2000, 0); source.SpecialCycle = 0;
        state.Actors[state.Actors.IndexOf(source)] = source with { MaxHealth = 1000, Health = 1000 };
        for (int i = 0; i < allies.Length; i++)
        {
            var ally = allies[i];
            state.Actors[state.Actors.IndexOf(ally)] = ally with
            { Position = new(-3300, i == 0 ? -1000 : 1000), MaxHealth = 1000, Health = 200, RecoveryUntil = 2500 };
        }
        foreach (var other in state.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Id != source.Id && !allies.Any(b => b.Id == a.Id)))
        { other.Health = 0; other.DeathProcessed = true; }
        Load(state, prefix);
        for (int i = 0; i < 8 && !_session.View.CampaignHazards!.Any(h => h.ContentId == power); i++) Step();
        var warning = _session.View.CampaignHazards!.Single(h => h.ContentId == power);
        Check("ward_actual_ai_schedules_channel", warning.RemainingTicks is > 40 and <= 45);
        var saved = _session.Capture();
        await InspectPresets("ward-warning", () =>
        {
            var rim = Descendants(_sandbox).OfType<MeshInstance3D>().Single(n => n.Name == "MidgameSupport_" + warning.Id && !n.IsQueuedForDeletion());
            var label = rim.GetNode<Label3D>("SupportCountdown");
            Check("ward_exact_hollow_radius", rim.Mesh is TorusMesh mesh && Mathf.IsEqualApprox(mesh.OuterRadius, 3));
            Check("ward_exact_fixed_center", rim.Position.IsEqualApprox(new(warning.Position.X * .001f, .075f, warning.Position.Z * .001f)));
            Check("ward_exact_countdown", label.Text == "ENEMY WARD\n" + Seconds(warning.RemainingTicks));
            Check("ward_channel_visible", rim.IsVisibleInTree() && label.IsVisibleInTree());
            Check("ward_interrupt_instruction", ActorLabel(source.Id).Text.Contains("OATH WARD · INTERRUPT", StringComparison.Ordinal) && !ActorLabel(source.Id).Text.Contains("ATTACK INCOMING", StringComparison.Ordinal));
            Check("ward_no_filled_damage_warning", !Descendants(_sandbox).OfType<MeshInstance3D>().Any(n => n.Mesh is CylinderMesh &&
                n.Position.IsEqualApprox(new(warning.Position.X * .001f, .04f, warning.Position.Z * .001f)) && n.MaterialOverride is StandardMaterial3D mat && mat.AlbedoColor.R > .95f && mat.AlbedoColor.G < .65f));
        });
        VerifyArchive("ward-warning");
        Wait((int)warning.RemainingTicks + 1);
        var resolution = _events.Where(e => e.ContentId == power && e.Kind == "BarrierGranted").ToArray();
        Check("ward_resolution_targets_only_eligible_allies", resolution.Length == allies.Length && resolution.All(e => e.TargetId != source.Id && allies.Any(a => a.Id == e.TargetId)));
        Check("ward_grants_thirty_six_without_healing", resolution.All(e => e.Amount == 36) && allies.All(a => _session.View.Actors.Single(b => b.Id == a.Id) is { Barrier: 36, Health: 200 }));
        Check("ward_warning_removed_after_resolution", !_session.View.CampaignHazards!.Any(h => h.Id == warning.Id) && !HasSupport(warning.Id));
        int allyId = allies[0].Id;
        await InspectPresets("ward-resolved", () =>
        {
            Check("ward_barrier_strips_visible", allies.All(a => _sandbox.GetNode<Sprite3D>("Actor" + a.Id + "/ActorHealthBar/BarrierStrip").IsVisibleInTree()));
        });
        VerifyArchive("ward-resolved");
        await FocusActor(allyId);
        await Capture("ward-focused.png", allyId, "BARRIER 36");
        GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(3);
        await Capture("ward-focused-minimum.png", allyId, "BARRIER 36");
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames(3);
        int before = _session.View.Actors.Single(a => a.Id == allyId).Barrier;
        Step(new CombatCommand(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: allyId)); Wait(6);
        var absorbed = _events.Where(e => e.Kind == "BarrierAbsorbed" && e.TargetId == allyId).ToArray();
        Check("ward_actual_hit_consumes_barrier_before_health", absorbed.Length > 0 && absorbed.Sum(e => e.Amount) == before - _session.View.Actors.Single(a => a.Id == allyId).Barrier && _session.View.Actors.Single(a => a.Id == allyId).Health == 200);
        VerifyArchive("ward-hit"); await Capture("ward-absorbed.png");
        _evidence.Add(new { kind = "detached-ward-resolution-and-hit", warning, resolution, absorbed });

        Load(saved, "ward-interrupt");
        Step(new CombatCommand(CombatCommandKind.Cast, SkillId: "skill.shield_breaker", TargetId: source.Id)); Wait(12);
        Check("ward_skill_interrupt_removes_warning", !_session.View.CampaignHazards!.Any(h => h.ContentId == power) && !HasSupport(warning.Id));
        Wait(45);
        Check("ward_interrupted_channel_grants_nothing", !_events.Any(e => e.ContentId == power && e.Kind == "BarrierGranted") && allies.All(a => _session.View.Actors.Single(b => b.Id == a.Id).Barrier == 0));
        VerifyArchive("ward-interrupted"); await Capture("ward-interrupted.png");
    }

    private async Task OrdinaryWarnings(string encounter, string[] expected)
    {
        var state = Fixture(encounter);
        string context = encounter.Split('.').Last();
        int i = 0;
        foreach (var enemy in state.Actors.Where(a => a.Faction == CombatFaction.Enemy).ToArray())
        {
            bool active = enemy.DefinitionId is "enemy.memory_archer" or "enemy.doubled_shadow" or "enemy.breach_echo" or "enemy.contract_keeper";
            state.Actors[state.Actors.IndexOf(enemy)] = enemy with
            { Position = new(-2300 + i * 650, i % 2 == 0 ? -900 : 900), RecoveryUntil = active ? 0 : 2500, SpecialCycle = 1, Hidden = false };
            i++;
        }
        if (encounter == "campaign.identity_memory") state.Campaign!.NextHazardTick = 1;
        Load(state, context);
        for (int tick = 0; tick < 15 && !expected.All(id => _session.View.CampaignHazards!.Any(h => h.ContentId == id)); tick++) Step();
        var warnings = _session.View.CampaignHazards!.Where(h => expected.Contains(h.ContentId)).ToArray();
        Check(context + "_actual_ai_warnings", expected.All(id => warnings.Any(h => h.ContentId == id)));
        await InspectPresets(context, () => InspectWarnings(warnings));
        _evidence.Add(new { kind = "detached-ordinary-warnings", encounter, warnings });
        VerifyArchive(context + "-warnings");
    }

    private async Task Boss(string encounter, string definition, int phase)
    {
        string prefix = definition.Split('.').Last() + "-phase" + phase;
        var state = Fixture(encounter); var boss = state.Actors.Single(a => a.DefinitionId == definition);
        state.Campaign!.BossPhase = phase; boss.SpecialCycle = phase % 2;
        foreach (var enemy in state.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Id != boss.Id)) enemy.RecoveryUntil = 2500;
        bool breach = definition == "boss.breach_heart";
        if (breach)
        {
            var seal = state.Actors.First(a => a.DefinitionId == "enemy.seal_channel");
            seal.Position = new(-2400, 1200); seal.Health = 1;
        }
        Load(state, prefix);
        for (int tick = 0; tick < 8 && !_session.View.CampaignHazards!.Any(h => h.SourceId == boss.Id); tick++) Step();
        var warnings = _session.View.CampaignHazards!.Where(h => h.SourceId == boss.Id).ToArray();
        int count = breach ? phase : 2;
        Check(prefix + "_actual_boss_sequence", warnings.Length == count && warnings.OrderBy(h => h.RemainingTicks).Select(h => h.SequenceIndex).SequenceEqual(Enumerable.Range(1, count)) && warnings.All(h => h.SequenceCount == count));
        await InspectPresets(prefix + "-warnings", () =>
        {
            var view = _session.View.Actors.Single(a => a.Id == boss.Id);
            Check(prefix + "_no_recovery_during_hazards", view.BossRecoveryTicks == 0 && !ActorLabel(boss.Id).Text.Contains("RECOVERING", StringComparison.Ordinal));
            InspectWarnings(warnings);
            if (breach) Check(prefix + "_shield_instruction", view.Shielded && ActorLabel(boss.Id).Text.Contains("PROTECTED · BREAK A SEAL", StringComparison.Ordinal));
            else Check(prefix + "_guard_countdown_is_authoritative", view.BossGuardedTicks > 0 && ActorLabel(boss.Id).Text.Split('\n').Contains("OATH GUARDED · " + Seconds(view.BossGuardedTicks)));
        });
        VerifyArchive(prefix + "-warnings");
        if (breach)
        {
            var seal = _session.View.Actors.First(a => a.DefinitionId == "enemy.seal_channel");
            Step(new CombatCommand(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: seal.Id)); Wait(6);
            Check(prefix + "_public_hit_breaks_protection", _session.View.Actors.Single(a => a.Id == seal.Id).Health == 0 && !_session.View.Actors.Single(a => a.Id == boss.Id).Shielded);
        }
        foreach (var original in warnings.OrderBy(h => h.RemainingTicks))
        {
            var live = _session.View.CampaignHazards!.Single(h => h.Id == original.Id);
            Wait((int)live.RemainingTicks);
            var due = _session.View.CampaignHazards!.Single(h => h.Id == original.Id);
            Check(prefix + "_queued_deadline_" + due.SequenceIndex, due.RemainingTicks == 0 && _session.View.Actors.Single(a => a.Id == boss.Id).BossRecoveryTicks == 0);
            InspectWarnings(_session.View.CampaignHazards!.Where(h => h.SourceId == boss.Id).ToArray());
            if (due.SequenceIndex == count) { VerifyArchive(prefix + "-last-deadline"); await Capture(prefix + "-last-deadline.png"); }
            Step();
            Check(prefix + "_deadline_resolves_" + due.SequenceIndex, !_session.View.CampaignHazards!.Any(h => h.Id == due.Id) && _events.Any(e => e.Kind == "CampaignHazardResolved" && e.ContentId == due.ContentId));
        }
        var recovering = _session.View.Actors.Single(a => a.Id == boss.Id);
        Check(prefix + "_actual_recovery_window", recovering.BossRecoveryTicks > 0 && !recovering.Shielded && !recovering.Guarded && !_session.View.CampaignHazards!.Any(h => h.SourceId == boss.Id));
        await InspectPresets(prefix + "-recovery", () =>
        {
            Check(prefix + "_exact_recovery_label", ActorLabel(boss.Id).Text.Contains("EXPOSED · RECOVERING " + Seconds(recovering.BossRecoveryTicks), StringComparison.Ordinal));
            Check(prefix + "_recovery_does_not_claim_damage_bonus", !ActorLabel(boss.Id).Text.Contains("DAMAGE", StringComparison.Ordinal));
        });
        _evidence.Add(new { kind = "detached-boss-phase", encounter, phase, warnings, recovering.BossRecoveryTicks, hash = _session.StateHash });
        VerifyArchive(prefix + "-recovery");
        Wait(recovering.BossRecoveryTicks);
        Check(prefix + "_recovery_expires", _session.View.Actors.Single(a => a.Id == boss.Id).BossRecoveryTicks == 0 && !ActorLabel(boss.Id).Text.Contains("RECOVERING", StringComparison.Ordinal));
    }

    private void InspectWarnings(CombatHazardView[] warnings)
    {
        foreach (var warning in warnings)
        {
            var label = WarningLabel(warning);
            string title = warning.ContentId switch
            {
                "campaign.oathmark" or "campaign.oath_mark" => "OATH MARK",
                "campaign.covenant_fault" => "COVENANT FAULT",
                "campaign.breach_echo" => "FIRST ECHO",
                "campaign.returning_echo" => "RETURNING ECHO",
                "campaign.seal_sweep" => "SEAL SWEEP",
                "campaign.causalecho" => "CAUSAL ECHO",
                "rule.causalechoes" => "ROOM ECHO",
                "campaign.memoryarrow" => "MEMORY ARROW",
                "campaign.shadowdouble" => "SHADOW STRIKE",
                _ => throw new InvalidDataException("Unexpected warning under inspection: " + warning.ContentId)
            };
            if (warning.SequenceCount > 0) title += $" {warning.SequenceIndex}/{warning.SequenceCount}";
            Check(_context + "_warning_text_" + warning.Id, label.Text == title + "\n" + Seconds(warning.RemainingTicks) && label.IsVisibleInTree());
            Check(_context + "_warning_label_owns_lifetime_" + warning.Id, label.GetParent() is MeshInstance3D && label.NoDepthTest && label.OutlineSize > 0);
        }
        var camera = _sandbox.GetChildren().OfType<Camera3D>().Single();
        var labels = warnings.Select(WarningLabel).ToArray();
        var rectangles = labels.Select(label => Sandbox.CampaignLabelScreenRect(camera, label)).ToArray();
        var actorRectangles = _session.View.Actors.Select(actor => ActorLabel(actor.Id)).Where(label => label.IsVisibleInTree())
            .Select(label => Sandbox.CampaignLabelScreenRect(camera, label)).ToArray();
        Check(_context + "_warning_labels_clear_actor_instructions", rectangles.All(rect => actorRectangles.All(actor => !rect.Intersects(actor))));
        Check(_context + "_warning_labels_do_not_overlap", rectangles.SelectMany((rect, index) => rectangles.Skip(index + 1).Select(other => !rect.Intersects(other))).All(clear => clear));
        Check(_context + "_warning_labels_fit_viewport", rectangles.All(GetViewport().GetVisibleRect().Encloses));

    }
    private Label3D WarningLabel(CombatHazardView warning) => Descendants(_sandbox).OfType<Label3D>().Single(n =>
        (n.Name == "MidgameHazard_" + warning.Id || n.Name == "EchoWarning_" + warning.Id) && !n.IsQueuedForDeletion());

    private async Task InspectPresets(string context, Action inspect)
    {
        string hash = _session.StateHash;
        foreach (var (name, width, reduced) in new[] { ("high", 1280, false), ("performance", 1280, false), ("reduced", 1280, true), ("minimum", 780, true) })
        {
            _variant = name;
            GetWindow().Size = GetWindow().ContentScaleSize = new(width, width == 780 ? 720 : 800);
            _sandbox.SetGraphicsQuality(name is "performance" or "minimum" ? "Performance" : "High"); _reduced.ButtonPressed = reduced;
            _sandbox.SetModalPaused("late-depth-check", true); _sandbox._Process(0); await Frames(3);
            inspect(); Check(context + "_" + name + "_pause_preserves_hash", _session.StateHash == hash);
            await Capture(context + "-" + name + ".png");
        }
        _variant = "";
        _sandbox.SetModalPaused("late-depth-check", false); _reduced.ButtonPressed = false; _sandbox.SetGraphicsQuality("High");
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); _sandbox._Process(0); await Frames(3);
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
        string hash = _session.StateHash;
        var save = JsonData.Write(new CombatSave(1, hash, _session.Capture()));
        var restored = CombatSession.Restore(_content, CombatSaveStore.Read(save, _content));
        Check(context + "_save_restores_identically", restored.StateHash == hash);
        var replay = _recorder.Capture(); var verified = CombatReplayRunner.Run(_content, replay);
        Check(context + "_commands_replay_identically", verified.Success && verified.FinalHash == hash);
        System.IO.File.WriteAllText(Path.Combine(_output, context + ".save.json"), save);
        System.IO.File.WriteAllText(Path.Combine(_output, context + ".awcombat"), JsonData.Write(replay));
    }
    private bool HasSupport(long id) => Descendants(_sandbox).Any(n => n.Name == "MidgameSupport_" + id && !n.IsQueuedForDeletion());
    private Label3D ActorLabel(int id) => _sandbox.GetNode<Node3D>("Actor" + id).GetChildren().OfType<Label3D>().Single();
    private static string Seconds(long ticks) => (Math.Ceiling(ticks / 3d) / 10).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "s";
    private static IEnumerable<Node> Descendants(Node node)
    { foreach (var child in node.GetChildren()) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; } }
    private T SandboxField<T>(string name)
    {
        var field = typeof(Sandbox).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidDataException("Missing required diagnostic field: " + name);
        return field.GetValue(_sandbox) is T value ? value
            : throw new InvalidDataException("Uninitialized or incompatible diagnostic field: " + name);
    }
    private async Task Capture(string name, int focusedActor = 0, string expectedCondition = "")
    {
        bool requested = OS.GetCmdlineUserArgs().Contains("--capture-late-campaign-combat");
        bool nativeCapture = requested && DisplayServer.GetName() != "headless";
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (focusedActor != 0) await FocusActor(focusedActor);
            else if (attempt > 0)
            {
                if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
                await Frames(5);
                var resume = Descendants(_sandbox).OfType<Button>().SingleOrDefault(b => b.Text == "Resume playing" && b.IsVisibleInTree());
                resume?.EmitSignal(BaseButton.SignalName.Pressed); _sandbox._Process(0);
            }
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
            bool current = true;
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
            if (focusedActor != 0) Check("capture_current_focus_" + name, true);
            if (!requested) return;
            if (!nativeCapture) { if (!_skipped.Contains("native screenshots")) _skipped.Add("native screenshots"); return; }
            // No await separates these checks from the image read.
            using var image = GetViewport().GetTexture().GetImage();
            Check("capture_" + name, image.GetWidth() > 0 && image.GetHeight() > 0 && image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
            return;
        }
    }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void Check(string name, bool passed)
    {
        if (_variant.Length > 0) name += "_" + _variant;
        _checks[name] = passed;
        if (!passed) throw new InvalidDataException("Late campaign combat check failed: " + name);
    }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "LateCampaignCombatClientSmokePassed" : "LateCampaignCombatClientSmokeFailed",
            passed,
            checks = _checks,
            commands = _commands,
            evidence = _evidence,
            captures = _captures,
            skippedChecks = _skipped,
            error,
            scope = "Detached, modified encounters with fixture invulnerability and controlled positions/health. Actual AI, skill commands, ward interruption and barrier consumption, five boss phase fixtures and exact deadlines, save and command replay. High, Performance, Reduced effects and 780px presentation checks do not establish earned campaign balance or human playtesting."
        };
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "late-campaign-combat-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
