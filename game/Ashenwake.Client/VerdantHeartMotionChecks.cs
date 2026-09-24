using Godot;

namespace Ashenwake.Client;

/// <summary>Rootheart scenery follows only supplied root/victory state, using its fixed cosmetic rig.</summary>
internal static class VerdantHeartMotionChecks
{
    public static void Run(Action<string, bool> check)
    {
        var heart = VerdantHeartVisual.Create(12, 10);
        var restored = VerdantHeartVisual.Create(12, 10, 0, true);
        var repeated = VerdantHeartVisual.Create(12, 10);
        try
        {
            var parts = Descendants(heart).OfType<Node3D>().ToArray();
            var roots = parts.Where(node => node.Name.ToString().StartsWith("FeedingRoot", StringComparison.Ordinal)).ToArray();
            var petals = parts.Where(node => node.Name.ToString().StartsWith("HeartPetal", StringComparison.Ordinal)).ToArray();
            var growth = parts.Single(node => node.Name == "QuietNewGrowth");
            var meshIds = Descendants(heart).OfType<MeshInstance3D>().Select(node => node.Mesh.GetInstanceId()).ToArray();
            var rootPose = heart.Transform;
            var neutral = Pose(heart);
            heart.SetState(2, false);
            check("rootheart_root_loss_begins_without_pose_snap", Same(neutral, Pose(heart)));
            heart.Animate(.1, false, false);
            check("rootheart_only_destroyed_root_begins_wilting", roots.Length == 3 && roots[2].Scale.Y is > .42f and < 1 &&
                roots[0].Transform.IsEqualApprox(Transform3D.Identity) && roots[1].Transform.IsEqualApprox(Transform3D.Identity));
            check("rootheart_root_loss_has_seed_recoil", heart.HeartPose.Basis.GetEuler().X < -.001f);
            var frozen = Pose(heart);
            heart.Animate(20, true, false);
            check("rootheart_pause_freezes_root_release_and_seed_recoil", Same(frozen, Pose(heart)));
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
                heart.Animate(invalid, false, false);
            check("rootheart_invalid_delta_cannot_advance_release", Same(frozen, Pose(heart)));
            for (int frame = 0; frame < 10; frame++) heart.Animate(.1, false, false);
            check("rootheart_severed_root_settles_at_authored_wilt", Math.Abs(roots[2].Scale.Y - .42f) < .00001f &&
                Math.Abs(heart.HeartPose.Basis.GetEuler().X) < .00001f);

            heart.SetState(3, false);
            heart.Animate(0, false, true); heart.Animate(0, false, false);
            heart.SetState(0, false); repeated.SetState(0, false);
            for (int frame = 0; frame < 12; frame++)
            {
                heart.Animate(.05, false, false);
                repeated.SetState(0, false); repeated.Animate(.05, false, false);
            }
            check("rootheart_repeated_snapshots_do_not_restart_root_release", Same(Pose(heart), Pose(repeated)));
            check("rootheart_destroyed_roots_expose_seed", heart.HeartPose.Origin.Z > .39f);
            var exposed = heart.HeartPose;
            heart.Animate(.1, false, false);
            check("rootheart_exposed_seed_continues_breathing", !exposed.IsEqualApprox(heart.HeartPose));

            heart.SetState(0, true);
            var beforeBloom = petals.Select(petal => petal.RotationDegrees.X).ToArray();
            heart.Animate(.1, false, false);
            check("rootheart_victory_petals_open_in_staggered_order", petals.Length == 8 &&
                petals[0].RotationDegrees.X < beforeBloom[0] && Math.Abs(petals[3].RotationDegrees.X - beforeBloom[3]) < .00001f);
            check("rootheart_new_growth_waits_for_petals_to_release", growth.Visible && Math.Abs(growth.Scale.Y - .05f) < .00001f);
            for (int frame = 0; frame < 40; frame++) heart.Animate(.1, false, false);
            check("rootheart_victory_settles_to_same_pose_as_restored_victory", !heart.IsTransitioning && heart.VictoryProgress == 1 &&
                Same(Pose(heart), Pose(restored)) && Math.Abs(growth.Scale.Y - 1) < .00001f);
            var aftermath = Pose(heart);
            for (int frame = 0; frame < 80; frame++) heart.Animate(.1, false, false);
            check("rootheart_aftermath_remains_settled", Same(aftermath, Pose(heart)));

            heart.SetState(3, false); heart.SetState(1, false); heart.Animate(.1, false, false);
            heart.Animate(0, true, true);
            check("rootheart_reduced_effects_consumes_release_while_paused", Math.Abs(roots[1].Scale.Y - .42f) < .00001f &&
                Math.Abs(roots[2].Scale.Y - .42f) < .00001f && Math.Abs(heart.HeartPose.Basis.GetEuler().X) < .00001f);
            heart.Animate(.1, false, false);
            check("rootheart_restoring_effects_never_replays_root_recoil", Math.Abs(heart.HeartPose.Basis.GetEuler().X) < .00001f);
            check("rootheart_motion_keeps_fixed_cosmetic_resources", heart.ArticulatedPartCount == 13 && heart.TransientCapacity == 0 &&
                heart.Transform.IsEqualApprox(rootPose) && parts.Length == Descendants(heart).OfType<Node3D>().Count() &&
                meshIds.SequenceEqual(Descendants(heart).OfType<MeshInstance3D>().Select(node => node.Mesh.GetInstanceId())) &&
                !Descendants(heart).Any(node => node is CollisionObject3D or CollisionShape3D or NavigationRegion3D));
        }
        finally { heart.Free(); restored.Free(); repeated.Free(); }
    }

    private static Transform3D[] Pose(Node root) => Descendants(root).OfType<Node3D>().Select(node => node.Transform).ToArray();
    private static bool Same(Transform3D[] left, Transform3D[] right) => left.Length == right.Length &&
        left.Zip(right).All(pair => pair.First.IsEqualApprox(pair.Second));
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
}
