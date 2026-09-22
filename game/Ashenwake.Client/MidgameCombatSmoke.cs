using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;
using Godot;

namespace Ashenwake.Client;

/// <summary>Detached encounter fixtures isolate readable support and boss windows. Actual
/// public combat commands resolve each branch; no campaign completion or rewards are claimed.</summary>
public partial class MidgameCombatSmoke : Node
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
            if (!args.Contains("--midgame-combat-smoke") || _output.Length == 0)
                throw new InvalidDataException("Midgame combat smoke requires --midgame-combat-smoke --output=<fresh-artifact-directory>.");
            if (Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Midgame combat smoke requires a fresh artifact directory.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _content = CampaignCombatContent.Parse(Read("combat"), Read("campaign-combat")).CombatJson;
            _sandbox = new Sandbox { ContentJsonOverride = _content, AutomaticStep = false, AdvanceOverride = _ => [] };
            AddChild(_sandbox); _sandbox.SetProcess(false); _sandbox.EnableCampaign();
            _stage = new CampaignStage(); AddChild(_stage);
            _reduced = Descendants(_sandbox).OfType<CheckButton>().Single(b => b.Text == "Reduced visual effects");
            _reduced.ButtonPressed = false; _sandbox.SetGraphicsQuality("High");
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            await Support("campaign.plague_village", "enemy.bloom_carrier", "campaign.spore_mend", "ENEMY HEAL");
            await Support("campaign.extraction_floor", "enemy.heat_tender", "campaign.forge_bellows", "ENEMY POWER");
            await Boss("campaign.rootheart", "boss.rootheart");
            await Boss("campaign.furnace_spindle", "boss.furnace_spindle");
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
        bool verdant = _session.EncounterId is "campaign.plague_village" or "campaign.rootheart";
        int act = verdant ? 2 : 3;
        string style = _session.EncounterId switch
        {
            "campaign.plague_village" => "verdant_village",
            "campaign.rootheart" => "verdant_heart",
            "campaign.extraction_floor" => "cinder_extraction",
            _ => "cinder_furnace"
        };
        _sandbox.PresentAuthoredRoom(_session.Room, "midgame-fixture:" + _session.EncounterId, style);
        var state = new CampaignState { CurrentAct = act, HighestActVisited = act, InHub = false };
        var view = new CampaignView(act, verdant ? "Verdant Maw" : "Cinder Reach", "", "", "", [], _session.EncounterId, [], [], [], [], [], null);
        _stage.Show(state, view, _session.Room, [], [], 0, _session.View.Actors[0].Position, _session.View.BossPhase,
            combat: _session.View, activeEncounterId: _session.EncounterId);
        _sandbox.SetWorldSubtitle("ACT " + act + " · DETACHED COMBAT FIXTURE · " + _context.Replace('-', ' ').ToUpperInvariant());
    }

    private async Task Support(string encounter, string sourceDefinition, string power, string title)
    {
        string prefix = power.Split('.').Last();
        var state = Fixture(encounter);
        var source = state.Actors.First(a => a.DefinitionId == sourceDefinition);
        var allies = state.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Id != source.Id).Take(2).ToArray();
        source.Position = new(-2200, 0); source.SpecialCycle = 0;
        state.Actors[state.Actors.IndexOf(source)] = source with { MaxHealth = 1000, Health = 1000 };
        for (int i = 0; i < allies.Length; i++)
        {
            var ally = allies[i];
            state.Actors[state.Actors.IndexOf(ally)] = ally with
            { Position = new(-1300, i == 0 ? -1100 : 1100), MaxHealth = 1000, Health = 200, RecoveryUntil = 2500 };
        }
        foreach (var other in state.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Id != source.Id && !allies.Any(b => b.Id == a.Id)))
        { other.Health = 0; other.DeathProcessed = true; }
        Load(state, prefix);
        for (int i = 0; i < 180 && !_session.View.CampaignHazards!.Any(h => h.ContentId == power); i++) Step();
        var warning = _session.View.CampaignHazards!.Single(h => h.ContentId == power);
        Check(prefix + "_actual_ai_schedules_support", warning.RemainingTicks is > 30 and <= 42);
        var saved = _session.Capture();
        await InspectPresets(prefix + "-warning", () =>
        {
            var rim = Descendants(_sandbox).OfType<MeshInstance3D>().Single(n => n.Name == "MidgameSupport_" + warning.Id && !n.IsQueuedForDeletion());
            var label = rim.GetNode<Label3D>("SupportCountdown");
            Check(prefix + "_exact_hollow_support_radius", rim.Mesh is TorusMesh mesh && Mathf.IsEqualApprox(mesh.OuterRadius, warning.Radius * .001f));
            Check(prefix + "_exact_support_center", rim.Position.IsEqualApprox(new(warning.Position.X * .001f, .075f, warning.Position.Z * .001f)));
            Check(prefix + "_exact_support_countdown", label.Text == title + "\n" + Seconds(warning.RemainingTicks));
            Check(prefix + "_support_remains_visible", rim.IsVisibleInTree() && label.IsVisibleInTree());
            Check(prefix + "_clear_interrupt_instruction", ActorLabel(source.Id).Text.Contains("INTERRUPT", StringComparison.Ordinal) && !ActorLabel(source.Id).Text.Contains("ATTACK INCOMING", StringComparison.Ordinal));
            Check(prefix + "_no_filled_damage_warning", !Descendants(_sandbox).OfType<MeshInstance3D>().Any(n => n.Mesh is CylinderMesh &&
                n.Position.IsEqualApprox(new(warning.Position.X * .001f, .04f, warning.Position.Z * .001f)) && n.MaterialOverride is StandardMaterial3D mat && mat.AlbedoColor.R > .95f && mat.AlbedoColor.G < .65f));
        });
        VerifyArchive(prefix + "-warning");
        Wait((int)warning.RemainingTicks + 1);
        var resolution = _events.Where(e => e.ContentId == power && e.Kind is "Healed" or "EnemyOvercharged").ToArray();
        Check(prefix + "_actual_resolution_targets_only_allies", resolution.Length > 0 && resolution.All(e => e.TargetId != source.Id && allies.Any(a => a.Id == e.TargetId)));
        Check(prefix + "_warning_removed_after_resolution", !_session.View.CampaignHazards!.Any(h => h.Id == warning.Id) && !HasSupport(warning.Id));
        if (power == "campaign.spore_mend") Check(prefix + "_healing_amount_is_bounded", resolution.All(e => e.Amount is > 0 and <= 18));
        else
        {
            var ally = _session.View.Actors.Single(a => a.Id == resolution[0].TargetId);
            Check(prefix + "_actual_ninety_tick_power", ally.ForgeOverchargeTicks is > 0 and <= 90 && resolution.All(e => e.Amount == 90));
            await InspectPresets(prefix + "-overcharged", () =>
            {
                var strip = _sandbox.GetNode<Sprite3D>("Actor" + ally.Id + "/ActorHealthBar/ForgePowerStrip");
                Check(prefix + "_compact_power_strip_visible", strip.IsVisibleInTree() && strip.RegionRect.Size.IsEqualApprox(new Vector2(96 * ally.ForgeOverchargeTicks / 90f, 3)) && strip.Modulate == new Color("f5b565"));
                Check(prefix + "_unselected_power_does_not_force_names", allies.Count(a => ActorLabel(a.Id).IsVisibleInTree()) <= 1);
            });
            await FocusActor(ally.Id);
            var card = Descendants(_sandbox).OfType<PanelContainer>().Single(n => n.Name == "CombatTargetDetail");
            var detail = Descendants(card).OfType<Label>().Single(n => n.Name == "TargetConditions");
            Check(prefix + "_focus_shows_exact_effect_and_time", card.IsVisibleInTree() && detail.Text.Contains("OVERCHARGED +20% DAMAGE · " + Seconds(ally.ForgeOverchargeTicks), StringComparison.Ordinal));
            await Capture(prefix + "-focused.png", ally.Id, "OVERCHARGED +20% DAMAGE · " + Seconds(ally.ForgeOverchargeTicks));
            GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(3);
            await FocusActor(ally.Id);
            Check(prefix + "_minimum_focus_retains_exact_effect_and_time", card.IsVisibleInTree() && detail.Text.Contains("OVERCHARGED +20% DAMAGE · " + Seconds(ally.ForgeOverchargeTicks), StringComparison.Ordinal));
            Check(prefix + "_focused_detail_fits_minimum_viewport", GetViewport().GetVisibleRect().Encloses(card.GetGlobalRect()) && detail.GetLineCount() <= 2);
            await Capture(prefix + "-focused-minimum.png", ally.Id, "OVERCHARGED +20% DAMAGE · " + Seconds(ally.ForgeOverchargeTicks));
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames(3);
            Wait(90);
            Check(prefix + "_power_expiry_hides_strip", _session.View.Actors.Single(a => a.Id == ally.Id).ForgeOverchargeTicks == 0 &&
                !_sandbox.GetNode<Sprite3D>("Actor" + ally.Id + "/ActorHealthBar/ForgePowerStrip").Visible);
        }
        await Capture(prefix + "-resolved.png"); VerifyArchive(prefix + "-resolved");
        _evidence.Add(new { kind = "detached-support-resolution", encounter, warning, resolution });

        Load(saved, prefix + "-interrupt");
        Step(new CombatCommand(CombatCommandKind.Cast, SkillId: "skill.shield_breaker", TargetId: source.Id)); Wait(12);
        Check(prefix + "_skill_interrupt_removes_warning", !_session.View.CampaignHazards!.Any(h => h.ContentId == power) && !HasSupport(warning.Id));
        Wait(45);
        Check(prefix + "_interrupted_channel_does_not_resolve", !_events.Any(e => e.ContentId == power && e.Kind is "Healed" or "EnemyOvercharged"));
        VerifyArchive(prefix + "-interrupted"); await Capture(prefix + "-interrupted.png");
    }

    private async Task Boss(string encounter, string definition)
    {
        string prefix = definition.Split('.').Last();
        var state = Fixture(encounter); var boss = state.Actors.Single(a => a.DefinitionId == definition);
        boss.SpecialCycle = 1;
        foreach (var enemy in state.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Id != boss.Id)) enemy.RecoveryUntil = 2500;
        if (definition == "boss.rootheart")
        {
            var root = state.Actors.First(a => a.DefinitionId == "enemy.feeding_root");
            root.Position = new(-2400, 1200); root.Health = 1;
        }
        Load(state, prefix);
        for (int i = 0; i < 8 && !_session.View.CampaignHazards!.Any(h => h.SourceId == boss.Id); i++) Step();
        var warnings = _session.View.CampaignHazards!.Where(h => h.SourceId == boss.Id).ToArray();
        Check(prefix + "_actual_paired_boss_warnings", warnings.Length == 2);
        await InspectPresets(prefix + "-warnings", () =>
        {
            var view = _session.View.Actors.Single(a => a.Id == boss.Id);
            Check(prefix + "_no_recovery_during_hazards", view.BossRecoveryTicks == 0 && !ActorLabel(boss.Id).Text.Contains("RECOVERING", StringComparison.Ordinal));
            foreach (var warning in warnings)
            {
                var label = Descendants(_sandbox).OfType<Label3D>().Single(n => n.Name == "MidgameHazard_" + warning.Id && !n.IsQueuedForDeletion());
                Check(prefix + "_deadline_" + warning.ContentId, label.Text.EndsWith(Seconds(warning.RemainingTicks), StringComparison.Ordinal) && label.IsVisibleInTree());
            }
            if (definition == "boss.rootheart") Check(prefix + "_shield_instruction", view.Shielded && ActorLabel(boss.Id).Text.Contains("SEVER A ROOT", StringComparison.Ordinal));
            else Check(prefix + "_guard_countdown_is_authoritative", view.BossGuardedTicks > 0 && ActorLabel(boss.Id).Text.Split('\n').Contains("CORE GUARDED · " + Seconds(view.BossGuardedTicks)));
        });
        VerifyArchive(prefix + "-warnings");
        if (definition == "boss.rootheart")
        {
            var root = _session.View.Actors.First(a => a.DefinitionId == "enemy.feeding_root");
            Step(new CombatCommand(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: root.Id)); Wait(6);
            Check(prefix + "_public_hit_severs_root", _session.View.Actors.Single(a => a.Id == root.Id).Health == 0 && !_session.View.Actors.Single(a => a.Id == boss.Id).Shielded);
        }
        for (int i = 0; i < 100 && _session.View.Actors.Single(a => a.Id == boss.Id).BossRecoveryTicks == 0; i++) Step();
        var recovering = _session.View.Actors.Single(a => a.Id == boss.Id);
        Check(prefix + "_actual_safe_recovery_window", recovering.BossRecoveryTicks > 0 && !recovering.Shielded && !recovering.Guarded && !_session.View.CampaignHazards!.Any(h => h.SourceId == boss.Id));
        await InspectPresets(prefix + "-recovery", () =>
        {
            Check(prefix + "_exact_recovery_label", ActorLabel(boss.Id).Text.Contains("EXPOSED · RECOVERING " + Seconds(recovering.BossRecoveryTicks), StringComparison.Ordinal));
            Check(prefix + "_recovery_does_not_claim_damage_bonus", !ActorLabel(boss.Id).Text.Contains("DAMAGE", StringComparison.Ordinal));
        });
        _evidence.Add(new { kind = "detached-boss-window", encounter, warnings, recovering.BossRecoveryTicks, hash = _session.StateHash });
        VerifyArchive(prefix + "-recovery");
        Wait(recovering.BossRecoveryTicks);
        Check(prefix + "_recovery_expires", _session.View.Actors.Single(a => a.Id == boss.Id).BossRecoveryTicks == 0 && !ActorLabel(boss.Id).Text.Contains("RECOVERING", StringComparison.Ordinal));
    }

    private async Task InspectPresets(string context, Action inspect)
    {
        string hash = _session.StateHash;
        foreach (var (name, width, reduced) in new[] { ("high", 1280, false), ("reduced", 1280, true), ("minimum", 780, true) })
        {
            _variant = name;
            GetWindow().Size = GetWindow().ContentScaleSize = new(width, width == 780 ? 720 : 800);
            _sandbox.SetGraphicsQuality(reduced ? "Performance" : "High"); _reduced.ButtonPressed = reduced;
            _sandbox.SetModalPaused("midgame-check", true); _sandbox._Process(0); await Frames(3);
            inspect(); Check(context + "_" + name + "_pause_preserves_hash", _session.StateHash == hash);
            await Capture(context + "-" + name + ".png");
        }
        _variant = "";
        _sandbox.SetModalPaused("midgame-check", false); _reduced.ButtonPressed = false; _sandbox.SetGraphicsQuality("High");
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
        bool requested = OS.GetCmdlineUserArgs().Contains("--capture-midgame-combat");
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
        if (!passed) throw new InvalidDataException("Midgame combat check failed: " + name);
    }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "MidgameCombatClientSmokePassed" : "MidgameCombatClientSmokeFailed",
            passed,
            checks = _checks,
            commands = _commands,
            evidence = _evidence,
            captures = _captures,
            skippedChecks = _skipped,
            error,
            scope = "Detached, modified encounters with fixture invulnerability and controlled positions/health. Actual AI, skill commands, support resolutions, boss deadlines, save and command replay. High, Reduced/Performance and 780px presentation checks do not establish earned campaign balance or human playtesting."
        };
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "midgame-combat-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
