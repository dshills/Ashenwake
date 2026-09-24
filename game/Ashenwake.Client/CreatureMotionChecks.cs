using Godot;

namespace Ashenwake.Client;

/// <summary>Exercises the rendered creature rigs without a world, combat clock, or player save.</summary>
internal static class CreatureMotionChecks
{
    internal static readonly (string Id, string Role, string Kind)[] Creatures =
    [
        ("enemy.carnivorous_vine", "Ranged", "Vine"),
        ("enemy.feeding_root", "Anchor", "Vine"),
        ("enemy.needle_swarm", "Melee", "Swarm"),
        ("enemy.bloom_carrier", "Rusher", "Carrier"),
        ("boss.antler", "Beast", "Antler"),
        ("boss.rootheart", "BellSaint", "Rootheart"),
        ("enemy.emberling", "Rusher", "Emberling"),
        ("enemy.furnace_brute", "Armored", "Brute"),
        ("enemy.forge_sentinel", "Armored", "Sentinel"),
        ("boss.furnace_spindle", "BellSaint", "Spindle"),
        ("enemy.oath_giant", "Armored", "Giant"),
        ("enemy.contract_keeper", "Ranged", "Keeper"),
        ("enemy.bone_sentinel", "Armored", "BoneSentinel"),
        ("boss.covenant_warden", "BellSaint", "Warden"),
        ("enemy.doubled_shadow", "Melee", "Shadow"),
        ("enemy.breach_echo", "Ranged", "Echo"),
        ("boss.breach_heart", "BellSaint", "Breach")
    ];

    public static void Run(Action<string, bool> check)
    {
        foreach (var (id, role, kind) in Creatures)
        {
            var visual = CharacterVisual.Create(id, role);
            var idle = CharacterVisual.Create(id, role);
            try
            {
                string prefix = id + "_creature_";
                visual.Position = new(3, .2f, -2); visual.Rotation = new(0, .7f, 0);
                var root = visual.Transform;
                var nodes = Descendants(visual).Select(n => n.GetInstanceId()).ToArray();
                var meshes = Meshes(visual);
                check(prefix + "uses_authored_family", visual.CreatureMotionKind == kind);
                check(prefix + "geometry_is_bounded_and_cosmetic", meshes.Length is > 0 and <= 160 &&
                    !Descendants(visual).Any(n => n is CollisionObject3D or CollisionShape3D or NavigationRegion3D));

                var initial = Pose(visual);
                Advance(visual, .4); Advance(idle, .4);
                check(prefix + "idle_breathes", !Same(initial, Pose(visual)));
                Advance(visual, .4, movement: new(0, 0, -.12f)); Advance(idle, .4);
                check(prefix + "movement_changes_pose", !Same(Pose(visual), Pose(idle)));
                Advance(visual, .6);
                var resting = Pose(visual);
                visual.BeginAttackWindup(); Advance(visual, .3, windup: true);
                var windup = Pose(visual);
                check(prefix + "windup_is_visible", !Same(resting, windup));
                visual.React("hit");
                check(prefix + "flinch_preserves_windup", visual.ActiveCue != "hit");
                visual.Animate(.1, Vector3.Right, windup: true, paused: true, facing: Vector3.Right);
                check(prefix + "pause_freezes_anticipation", Same(windup, Pose(visual)));

                visual.React("attack"); visual.Animate(0, Vector3.Zero, windup: true);
                var contact = Pose(visual);
                check(prefix + "contact_is_immediate", visual.ActiveCue == "attack" && !Same(windup, contact));
                visual.Animate(.016, Vector3.Zero, windup: true);
                check(prefix + "stale_windup_preserves_resolved_attack", visual.ActiveCue == "attack");
                Advance(visual, .18, state: "Recover");
                check(prefix + "follow_through_changes_contact", !Same(contact, Pose(visual)));
                var frozen = Pose(visual);
                visual.Animate(.1, Vector3.Right, paused: true);
                check(prefix + "pause_freezes_follow_through", Same(frozen, Pose(visual)));
                visual.BeginAttackWindup(); visual.Animate(.016, Vector3.Zero, windup: true);
                check(prefix + "new_start_replaces_old_attack", visual.ActiveCue == "");
                Advance(visual, .35, state: "Recover");
                check(prefix + "recovery_differs_from_anticipation", !Same(windup, Pose(visual)));
                Advance(visual, .8);
                check(prefix + "recovery_releases_cue", visual.ActiveCue == "");
                var beforeHit = Pose(visual);
                visual.React("hit"); Advance(visual, .06);
                check(prefix + "unprotected_hit_recoils", visual.ActiveCue == "hit" && !Same(beforeHit, Pose(visual)));
                Advance(visual, .4);
                check(prefix + "hit_releases", visual.ActiveCue == "");

                visual.React("dodge"); Advance(visual, .08);
                check(prefix + "defensive_tuck_preserves_root_and_priority", visual.ActiveCue == "dodge" && visual.Transform.IsEqualApprox(root));
                visual.React("attack");
                check(prefix + "attack_cannot_interrupt_defensive_tuck", visual.ActiveCue == "dodge");
                Advance(visual, .6);
                var standing = Pose(visual);
                visual.React("death"); Advance(visual, .25);
                var falling = Pose(visual);
                check(prefix + "death_has_visible_finite_fall", visual.IsDying && !visual.DeathFinished && !Same(standing, falling) && falling.All(Finite));
                visual.Animate(.1, Vector3.One, paused: true);
                check(prefix + "death_pauses", Same(falling, Pose(visual)));
                Advance(visual, 2);
                var finished = Pose(visual);
                check(prefix + "collapse_settles", visual.DeathFinished && !Same(falling, finished) && finished.All(Finite));
                visual.React("attack"); visual.React("hit"); visual.React("death"); visual.BeginAttackWindup();
                Advance(visual, .4, movement: Vector3.Right, windup: true);
                check(prefix + "death_is_terminal", visual.ActiveCue == "death" && visual.DeathFinished && Same(finished, Pose(visual)));
                check(prefix + "all_animation_preserves_actor_transform", visual.Transform.IsEqualApprox(root));
                check(prefix + "animation_reuses_nodes_and_meshes", nodes.SequenceEqual(Descendants(visual).Select(n => n.GetInstanceId())) && meshes.SequenceEqual(Meshes(visual)));
            }
            finally { visual.Free(); idle.Free(); }

            var lowRate = CharacterVisual.Create(id, role);
            var highRate = CharacterVisual.Create(id, role);
            try
            {
                Advance(lowRate, 2, 30, new(0, 0, -.12f));
                Advance(highRate, 2, 144, new(0, 0, -.12f));
                check(id + "_creature_gait_is_render_frequency_independent", Similar(Pose(lowRate), Pose(highRate), .006f));
            }
            finally { lowRate.Free(); highRate.Free(); }
        }
        CheckBossExposure(check, "boss.furnace_spindle", "spindle", (visual, exposed) => visual.SetFurnaceExposed(exposed));
        CheckBossExposure(check, "boss.covenant_warden", "warden", (visual, exposed) => visual.SetWardenExposed(exposed));
        CheckBossExposure(check, "boss.breach_heart", "breach", (visual, exposed) => visual.SetBreachExposed(exposed));
        var unrelated = CharacterVisual.Create("enemy.ash_ghoul", "Melee");
        try { check("verdant_motion_does_not_select_unrelated_enemies", unrelated.CreatureMotionKind == "None"); }
        finally { unrelated.Free(); }
    }

    private static void CheckBossExposure(Action<string, bool> check, string id, string prefix, Action<CharacterVisual, bool> setExposed)
    {
        var guarded = CharacterVisual.Create(id, "BellSaint");
        var exposed = CharacterVisual.Create(id, "BellSaint");
        try
        {
            setExposed(guarded, false); setExposed(exposed, true);
            var materials = Descendants(guarded).OfType<MeshInstance3D>()
                .SelectMany(m => Enumerable.Range(0, m.Mesh.GetSurfaceCount()).Select(m.GetActiveMaterial))
                .OfType<StandardMaterial3D>().DistinctBy(m => m.GetInstanceId()).ToArray();
            var palette = materials.Select(m => (m.AlbedoColor, m.Emission, m.EmissionEnergyMultiplier)).ToArray();
            var root = exposed.Transform;
            Advance(guarded, 1); Advance(exposed, 1);
            check(prefix + "_guard_and_exposure_have_distinct_actor_poses", !Same(Pose(guarded), Pose(exposed)));
            var frozen = Pose(exposed);
            setExposed(exposed, false); exposed.Animate(.1, Vector3.Zero, paused: true);
            check(prefix + "_guard_change_does_not_advance_paused_pose", Same(frozen, Pose(exposed)));
            Advance(exposed, .8);
            check(prefix + "_exposure_response_preserves_actor_transform", !Same(frozen, Pose(exposed)) && exposed.Transform.IsEqualApprox(root));
            check(prefix + "_exposure_never_mutates_shared_materials", palette.SequenceEqual(materials.Select(m => (m.AlbedoColor, m.Emission, m.EmissionEnergyMultiplier))));
        }
        finally { guarded.Free(); exposed.Free(); }
    }

    private static void Advance(CharacterVisual visual, double seconds, int frequency = 60, Vector3 movement = default, bool windup = false, string state = "")
    {
        for (int frame = 0; frame < (int)Math.Round(seconds * frequency); frame++)
            visual.Animate(1d / frequency, movement, windup, state, facing: Vector3.Forward);
    }

    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static ulong[] Meshes(Node node) => Descendants(node).OfType<MeshInstance3D>().Select(m => m.Mesh.GetInstanceId()).ToArray();
    private static Transform3D[] Pose(Node node) => Descendants(node).OfType<Node3D>().Select(n => n.Transform).ToArray();
    private static bool Same(Transform3D[] a, Transform3D[] b) => a.Length == b.Length && a.Zip(b).All(p => p.First.IsEqualApprox(p.Second));
    private static bool Similar(Transform3D[] a, Transform3D[] b, float tolerance) => a.Length == b.Length && a.Zip(b).All(p =>
        p.First.Origin.DistanceTo(p.Second.Origin) <= tolerance && p.First.Basis.X.DistanceTo(p.Second.Basis.X) <= tolerance &&
        p.First.Basis.Y.DistanceTo(p.Second.Basis.Y) <= tolerance && p.First.Basis.Z.DistanceTo(p.Second.Basis.Z) <= tolerance);
    private static bool Finite(Transform3D pose) => pose.Origin.IsFinite() && pose.Basis.X.IsFinite() && pose.Basis.Y.IsFinite() && pose.Basis.Z.IsFinite();
}
