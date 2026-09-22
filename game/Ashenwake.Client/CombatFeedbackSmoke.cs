using Ashenwake.Core.Content;
using Godot;

namespace Ashenwake.Client;

/// <summary>Deterministic cosmetic diagnostics, isolated from gameplay clocks and player saves.</summary>
public partial class CombatFeedbackSmoke : Node3D
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private string _output = "";
    private bool _writeReport;
    private Camera3D _camera = null!;
    private Node3D _gallery = null!;
    private Label _heading = null!, _caption = null!;
    private static readonly (string Discipline, string Skill)[] Heroes =
    [
        ("Vanguard", "skill.cleave"), ("Veilwalker", "skill.venom_knife"), ("Arcanist", "skill.fire_lance"),
        ("Gravecaller", "skill.grave_bolt"), ("Warden", "skill.thorn_shot")
    ];

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--combat-feedback-smoke") || _output.Length == 0)
                throw new InvalidDataException("Combat feedback smoke requires --combat-feedback-smoke --output=<fresh-artifact-directory>.");
            if (Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Combat feedback smoke requires a fresh artifact directory.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            CheckCharacters();
            CheckBell();
            CheckEffects();
            CheckAudio();
            CheckLootDiscovery();
            BuildGallery();
            await LootDiscoveryGallery();
            await HeroGallery();
            await MonsterGallery();
            await BellGallery();
            await ReleaseGallery();
            await EarnedLegendaryDiscovery();
            await ReleaseGallery();
            await LegendaryPowerFeedback();
            await ReleaseGallery();
            await MidgameLegendaryPowerFeedback();
            await ReleaseGallery();
            Finish(true, "");
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); Finish(false, ex.Message); }
    }

    private void CheckCharacters()
    {
        var attackLeans = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (discipline, skill) in Heroes)
        {
            var action = CharacterVisual.Create("player." + discipline.ToLowerInvariant(), "", discipline);
            var idle = CharacterVisual.Create("player." + discipline.ToLowerInvariant(), "", discipline);
            try
            {
                action.Position = new(3, .2f, -2); action.Rotation = new(0, .3f, 0);
                var root = action.Transform;
                var idleColors = Colors(idle);
                action.React("attack", skill);
                Advance(action, .15); Advance(idle, .15);
                Check(discipline + "_attack_changes_articulated_pose", action.ActiveCue == "attack" && Differences(Pose(action), Pose(idle)) >= 3);
                Check(discipline + "_attack_has_no_root_motion", action.Transform.IsEqualApprox(root));
                Vector3 lean = action.GetChildren().OfType<Node3D>().First().Rotation - idle.GetChildren().OfType<Node3D>().First().Rotation;
                attackLeans.Add($"{MathF.Round(lean.X, 3)}:{MathF.Round(lean.Y, 3)}:{MathF.Round(lean.Z, 3)}");
                action.React("hit");
                Check(discipline + "_flinch_does_not_interrupt_attack", action.ActiveCue == "attack");
                var frozen = Pose(action);
                Advance(action, .8, paused: true);
                Check(discipline + "_attack_pauses", action.ActiveCue == "attack" && Same(frozen, Pose(action)));
                Advance(action, .9);
                Check(discipline + "_attack_releases_to_locomotion", action.ActiveCue == "" && !action.IsDying);
                action.React("dodge"); Advance(action, .15);
                Check(discipline + "_dodge_is_visible_without_root_motion", action.ActiveCue == "dodge" && action.Transform.IsEqualApprox(root) && Differences(Pose(action), frozen) >= 3);
                Advance(action, .7);
                action.React("hit"); Advance(action, .05);
                var hit = Pose(action);
                Check(discipline + "_hit_reacts_and_releases", action.ActiveCue == "hit" && !Same(hit, frozen));
                Advance(action, .4);
                Check(discipline + "_hit_finishes", action.ActiveCue == "");
                action.React("death"); Advance(action, .25);
                Check(discipline + "_death_remains_visible_during_fall", action.IsDying && !action.DeathFinished && action.Visible && action.Transform.IsEqualApprox(root));
                var falling = Pose(action); Advance(action, 2, paused: true);
                Check(discipline + "_death_pauses", !action.DeathFinished && Same(falling, Pose(action)));
                Advance(action, 1.5);
                Check(discipline + "_death_finishes_and_cannot_restart", action.DeathFinished && action.IsDying);
                var finished = Pose(action);
                action.React("attack", skill); action.React("hit"); action.React("death"); Advance(action, .2);
                Check(discipline + "_death_is_terminal", action.DeathFinished && Same(finished, Pose(action)) && action.Transform.IsEqualApprox(root));
                Check(discipline + "_reactions_preserve_other_actor_materials", idleColors.SequenceEqual(Colors(idle)));
                Check(discipline + "_reactions_keep_geometry_bounded", Descendants(action).OfType<MeshInstance3D>().Count() <= 100 &&
                    !Descendants(action).Any(n => n is CollisionObject3D or CollisionShape3D or NavigationRegion3D));
            }
            finally { action.Free(); idle.Free(); }
        }
        Check("five_disciplines_have_distinct_attack_body_poses", attackLeans.Count == Heroes.Length);
        foreach (var (id, role) in new[] { ("enemy.ash_ghoul", "Melee"), ("enemy.funeral_guard", "Armored"), ("enemy.memory_archer", "Ranged"), ("boss.bell_saint", "BellSaint") })
        {
            var monster = CharacterVisual.Create(id, role);
            try
            {
                var root = monster.Transform;
                Advance(monster, .4); var idle = Pose(monster);
                Advance(monster, .3, windup: true); var windup = Pose(monster);
                Check(id + "_windup_prepares_visible_attack", Differences(idle, windup) >= 3 && monster.Transform.IsEqualApprox(root));
                monster.React("hit");
                Check(id + "_flinch_preserves_authoritative_windup", monster.ActiveCue != "hit");
                Advance(monster, .3, state: "Recover");
                Check(id + "_recovery_differs_from_windup", Differences(windup, Pose(monster)) >= 3 && monster.Transform.IsEqualApprox(root));
                monster.React("death"); Advance(monster, .25);
                Check(id + "_death_is_not_instant", monster.IsDying && !monster.DeathFinished);
                Advance(monster, 1.8);
                Check(id + "_death_finishes", monster.DeathFinished);
            }
            finally { monster.Free(); }
        }
    }

    private void CheckBell()
    {
        var bell = BellSanctuaryVisual.Create(12, 10);
        try
        {
            var root = bell.Transform; var original = bell.BellPose;
            for (int i = 0; i < 30; i++) bell.Animate(1.0 / 60, false, false);
            Check("bell_swings_while_bound", bell.Phase == 1 && !original.IsEqualApprox(bell.BellPose) && bell.Transform.IsEqualApprox(root));
            var paused = bell.BellPose;
            for (int i = 0; i < 30; i++) bell.Animate(1.0 / 60, true, false);
            Check("bell_swing_pauses", paused.IsEqualApprox(bell.BellPose));
            bell.SetPhase(2); bell.Animate(.1, false, false);
            Check("bell_ritual_phase_updates", bell.Phase == 2 && !bell.Defeated);
            bell.SetPhase(3);
            for (int i = 0; i < 10; i++) bell.Animate(1.0 / 60, false, false);
            Check("bell_unbinding_has_bounded_chain_debris", bell.Phase == 3 && bell.IsTransitioning && bell.ActiveDebrisCount is > 0 && bell.ActiveDebrisCount <= bell.DebrisCapacity);
            var breaking = Pose(bell);
            bell.Animate(.1, true, false);
            Check("bell_unbinding_pauses_all_debris", Same(breaking, Pose(bell)));
            for (int i = 0; i < 240; i++) bell.Animate(1.0 / 60, false, false);
            Check("bell_unbinding_settles", !bell.IsTransitioning && bell.ActiveDebrisCount == 0);
            bell.SetPhase(3, defeated: true);
            for (int i = 0; i < 12; i++) bell.Animate(1.0 / 60, false, false);
            Check("bell_defeat_starts_finite_sequence", bell.Defeated && bell.IsTransitioning);
            for (int i = 0; i < 300; i++) bell.Animate(1.0 / 60, false, false);
            Check("bell_defeat_settles_without_new_geometry", !bell.IsTransitioning && bell.ActiveDebrisCount == 0 &&
                Descendants(bell).OfType<MeshInstance3D>().Count() < 80 && !Descendants(bell).Any(n => n is CollisionObject3D or CollisionShape3D));
        }
        finally { bell.Free(); }
        var reduced = BellSanctuaryVisual.Create(12, 10);
        try
        {
            reduced.SetPhase(3); reduced.Animate(.1, false, true);
            Check("reduced_effects_suppress_bell_debris", reduced.ActiveDebrisCount == 0 && !reduced.IsTransitioning);
        }
        finally { reduced.Free(); }
        var restored = BellSanctuaryVisual.Create(12, 10, phase: 3, defeated: true);
        try
        {
            Check("restoring_completed_sanctuary_does_not_replay_effects", restored.Phase == 3 && restored.Defeated && !restored.IsTransitioning && restored.ActiveDebrisCount == 0);
        }
        finally { restored.Free(); }
    }

    private void CheckEffects()
    {
        var effects = new CombatEffects();
        AddChild(effects);
        try
        {
            foreach (string cue in new[] { "slash", "thrust", "spell", "hit", "block", "dodge", "dust", "death", "phase", "victory", "loot_legendary", "loot_godwrought", "legendary_pyre", "legendary_oath", "legendary_widow", "legendary_ready", "legendary_rotwake", "legendary_chorus", "legendary_cinder" })
            {
                effects.Clear(); effects.Emit(cue, new(2, .1f, -1), Vector3.Forward, new Color("f7c786"));
                Check(cue + "_effect_builds_visible_geometry", effects.Count > 0 && Descendants(effects).OfType<MeshInstance3D>().Any(m => m.IsVisibleInTree()));
                effects.Advance(.05, false, false); var before = Pose(effects); int count = effects.Count;
                effects.Advance(.1, true, false);
                Check(cue + "_effect_pauses", effects.Count == count && Same(before, Pose(effects)));
                for (int frame = 0; frame < 360; frame++) effects.Advance(1.0 / 60, false, false);
                Check(cue + "_effect_expires", effects.Count == 0);
                effects.Emit(cue, Vector3.Zero, Vector3.Forward, new Color("f7c786"), reducedEffects: true);
                Check(cue + "_effect_respects_reduced_effects", effects.Count == 0);
            }
            for (int i = 0; i < 400; i++) effects.Emit("hit", new(i % 7, .1f, i % 5), Vector3.Forward, new Color("f7c786"));
            Check("effect_burst_is_bounded", effects.Count <= CombatEffects.Maximum && effects.PoolCount <= CombatEffects.Maximum &&
                Descendants(effects).OfType<MeshInstance3D>().Count() <= CombatEffects.Maximum * 6);
            effects.Advance(.1, false, true);
            Check("reduced_effects_clear_existing_transients", effects.Count == 0);
            effects.Emit("spell", Vector3.Zero, Vector3.Forward, new Color("a66fe2"), reducedEffects: true);
            Check("reduced_effects_suppress_new_transients", effects.Count == 0);
            Check("transient_effects_are_cosmetic", !Descendants(effects).Any(n => n is CollisionObject3D or CollisionShape3D or NavigationRegion3D));
        }
        finally { effects.Free(); }
    }

    private void CheckAudio()
    {
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        foreach (string cue in CombatAudio.CueNames)
        {
            byte[] samples = CombatAudio.CreateSamples(cue);
            Check(cue + "_audio_has_bounded_duration", samples.Length is > 100 and <= 22050 * 2 * 4 && samples.Length % 2 == 0);
            long squared = 0; int peak = 0;
            for (int i = 0; i < samples.Length; i += 2)
            {
                int sample = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(samples.AsSpan(i, 2));
                peak = Math.Max(peak, Math.Abs(sample)); squared += (long)sample * sample;
            }
            Check(cue + "_audio_is_audible_without_digital_clipping", squared / (samples.Length / 2) > 25 && peak < short.MaxValue);
            fingerprints.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(samples)));
        }
        Check("combat_sound_cues_have_distinct_waveforms", fingerprints.Count == CombatAudio.CueNames.Count());
    }

    private void BuildGallery()
    {
        GetWindow().Size = new(1440, 900); GetWindow().ContentScaleSize = new(1440, 900);
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new("111a25"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new("a8bfd0"),
                AmbientLightEnergy = .72f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic
            }
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new(-48, -35, 0), LightColor = new("ffe2b1"), LightEnergy = 1.1f, ShadowEnabled = true });
        AddChild(new DirectionalLight3D { RotationDegrees = new(-20, 145, 0), LightColor = new("80bfc9"), LightEnergy = .4f });
        AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new(200, 200) }, Position = new(0, -.06f, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = new("070e15"), Roughness = .95f } });
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Position = new(-2, 5.8f, -17), Size = 11.5f, Current = true };
        AddChild(_camera); _camera.LookAt(new(0, 1.25f, 0));
        _gallery = new Node3D(); AddChild(_gallery);
        var canvas = new CanvasLayer(); AddChild(canvas);
        _heading = new Label { Position = new(76, 60), Size = new(1290, 60) }; _heading.AddThemeFontSizeOverride("font_size", 32); canvas.AddChild(_heading);
        _caption = new Label { Position = new(76, 128), Size = new(1290, 70) }; _caption.AddThemeFontSizeOverride("font_size", 18); canvas.AddChild(_caption);
    }

    private async Task HeroGallery()
    {
        _heading.Text = "THE UNBOUND / ATTACKS"; _caption.Text = "Vanguard · Veilwalker · Arcanist · Gravecaller · Warden\nThe same cosmetic rigs and attack clips used in gameplay, frozen at impact.";
        for (int i = 0; i < Heroes.Length; i++)
        {
            var (discipline, skill) = Heroes[i];
            var visual = CharacterVisual.Create("player." + discipline.ToLowerInvariant(), "", discipline);
            visual.Position = new((2 - i) * 3.1f, 0, 0); _gallery.AddChild(visual);
            Advance(visual, .3); visual.React("attack", skill); Advance(visual, .15);
        }
        await Capture("hero-attack-poses.png");
    }

    private async Task MonsterGallery()
    {
        foreach (var child in _gallery.GetChildren()) child.Free();
        _heading.Text = "THE FIRST HUNT / READ THE INTENT";
        _caption.Text = "Ash ghoul and funeral guard: anticipation → recovery\nDistinct joint poses leave a readable opening after the strike.";
        for (int i = 0; i < 4; i++)
        {
            var visual = i < 2 ? CharacterVisual.Create("enemy.ash_ghoul", "Melee") : CharacterVisual.Create("enemy.funeral_guard", "Armored");
            visual.Position = new((1.5f - i) * 3.25f, 0, 0); _gallery.AddChild(visual);
            Advance(visual, .4, windup: true);
            if (i % 2 != 0) Advance(visual, .3, state: "Recover");
        }
        await Capture("enemy-windup-recovery.png");
    }

    private async Task BellGallery()
    {
        foreach (var child in _gallery.GetChildren()) child.Free();
        _heading.Text = "THE BELL SAINT / BROKEN BONDS";
        _caption.Text = "The hanging bell and chain fragments react to the authoritative phase change.\nThis diagnostic freezes the actual sanctuary rig during its unbinding sequence.";
        var bell = BellSanctuaryVisual.Create(12, 10); _gallery.AddChild(bell);
        bell.SetPhase(3);
        for (int i = 0; i < 18; i++) bell.Animate(1.0 / 60, false, false);
        foreach (var light in GetChildren().OfType<DirectionalLight3D>()) light.LightEnergy *= .48f;
        GetChildren().OfType<WorldEnvironment>().Single().Environment.AmbientLightEnergy = .3f;
        _camera.Position = new(-4, 7, 1); _camera.Size = 13; _camera.LookAt(new(0, 4.3f, -13.3f));
        await Capture("bell-chain-break.png");
    }

    private async Task Capture(string filename)
    {
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!OS.GetCmdlineUserArgs().Contains("--capture-combat-feedback") || DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + filename, image.GetWidth() > 0 && image.GetHeight() > 0 && image.SavePng(Path.Combine(_output, filename)) == Error.Ok);
        _captures.Add(filename);
    }
    private async Task ReleaseGallery()
    {
        // Release diagnostic meshes while Godot is still processing frames. Otherwise
        // Mono can finalize the short-lived galleries during native renderer shutdown.
        foreach (var child in GetChildren()) child.QueueFree();
        _gallery = null!; _camera = null!; _heading = null!; _caption = null!;
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GC.Collect();
        // Let native deferred disposal run without blocking the engine's main thread.
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private static void Advance(CharacterVisual visual, double seconds, bool paused = false, bool windup = false, string state = "")
    { for (int i = 0; i < (int)Math.Ceiling(seconds * 60); i++) visual.Animate(1.0 / 60, Vector3.Zero, windup, state, paused, new Vector3(.22f, 0, -1)); }
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static Transform3D[] Pose(Node visual) => Descendants(visual).OfType<Node3D>().Select(n => n.Transform).ToArray();
    private static int Differences(Transform3D[] a, Transform3D[] b) => a.Length == b.Length ? a.Zip(b, (x, y) => !x.IsEqualApprox(y)).Count(different => different) : int.MaxValue;
    private static bool Same(Transform3D[] a, Transform3D[] b) => Differences(a, b) == 0;
    private static Color[] Colors(Node visual) => Descendants(visual).OfType<MeshInstance3D>()
        .SelectMany(m => Enumerable.Range(0, m.Mesh.GetSurfaceCount()).Select(m.GetActiveMaterial)).OfType<StandardMaterial3D>()
        .SelectMany(m => new[] { m.AlbedoColor, m.Emission }).ToArray();
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Combat feedback check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "CombatFeedbackClientSmokePassed" : "CombatFeedbackClientSmokeFailed",
            passed,
            checks = _checks,
            captures = _captures,
            legendaryReplay = new { file = "legendary-drop.replay.json", commands = _legendaryCommands, hash = _legendaryHash },
            error,
            scope = "Cosmetic attack, dodge, hit, death, monster windup/recovery, pause, root motion, material isolation, sanctuary phase sequences, special-loot cues and Legendary equipment feedback. A fresh Vanguard fights the authored road at seed20, earning Pyrebound Treads through ordinary commands. Its independent replay verifies exact delivery, duplicate suppression and a quiet restored-loot baseline. Isolated equipped combat fixtures verify Pyre, Oath and Widow trigger presentation and readiness without advancing Core from the renderer. Godwrought drop visuals use detached presentation fixtures; no Godwrought reward is earned here."
        };
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "combat-feedback-smoke.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
