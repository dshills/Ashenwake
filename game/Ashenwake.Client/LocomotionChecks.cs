using Godot;

namespace Ashenwake.Client;

internal static class LocomotionChecks
{
    public static void Run(Action<string, bool> check)
    {
        var slowFrames = CharacterVisual.Create("player.vanguard", "", "Vanguard");
        var fastFrames = CharacterVisual.Create("player.vanguard", "", "Vanguard");
        try
        {
            slowFrames.Position = new(3, .4f, -2);
            slowFrames.Rotation = new(0, .7f, 0);
            var actorTransform = slowFrames.Transform;
            var movement = new Vector3(0, 0, -.15f);
            Advance(slowFrames, 2, 30, movement);
            Advance(fastFrames, 2, 144, movement);
            check("locomotion_render_frequency_preserves_gait", Similar(Parts(slowFrames), Parts(fastFrames), .003f));
            check("locomotion_never_moves_actor_root", slowFrames.Transform.IsEqualApprox(actorTransform));

            var paused = Parts(slowFrames);
            for (int i = 0; i < 60; i++) slowFrames.Animate(1d / 60, Vector3.Right * 5, true, "Recover", true, Vector3.Right);
            check("locomotion_pause_freezes_all_cosmetic_transforms", Similar(paused, Parts(slowFrames), .000001f));

            Advance(slowFrames, 1, 60, Vector3.Zero);
            var body = slowFrames.GetChildren().OfType<Node3D>().First();
            var legs = body.GetChildren().OfType<Node3D>().Where(n => n is not MeshInstance3D).Take(2).ToArray();
            Vector3[] settledFeet = legs.Select(leg => body.Transform * leg.Transform * new Vector3(0, -.92f, 0)).ToArray();
            Advance(slowFrames, .6, 60, Vector3.Zero);
            Vector3[] restingFeet = legs.Select(leg => body.Transform * leg.Transform * new Vector3(0, -.92f, 0)).ToArray();
            check("locomotion_stopped_feet_remain_planted", legs.Length == 2 && settledFeet.Zip(restingFeet).All(p => p.First.DistanceTo(p.Second) < .001f));
            check("locomotion_stopped_legs_settle_to_rest", legs.All(leg => Math.Abs(leg.Rotation.X) < .001f));

            Vector3 Facing(float angle) => new(-MathF.Sin(angle), 0, -MathF.Cos(angle));
            for (int i = 0; i < 120; i++) slowFrames.Animate(1d / 60, Vector3.Zero, facing: Facing(3.1f));
            float beforeTurn = body.Rotation.Y;
            slowFrames.Animate(1d / 60, Vector3.Zero, facing: Facing(-3.1f));
            check("locomotion_turn_crosses_angle_wrap_by_shortest_path", Math.Abs(Mathf.AngleDifference(beforeTurn, body.Rotation.Y)) < .04f);
            slowFrames.Animate(1d / 60, Vector3.One * 10000);
            check("locomotion_large_displacement_keeps_pose_finite", Parts(slowFrames).All(Finite));
            check("locomotion_large_displacement_preserves_actor_root", slowFrames.Transform.IsEqualApprox(actorTransform));

            slowFrames.React("attack", "skill.cleave");
            Advance(slowFrames, .15, 60, movement);
            slowFrames.React("dodge");
            check("locomotion_dodge_retains_cue_priority", slowFrames.ActiveCue == "dodge");
            slowFrames.React("death");
            Advance(slowFrames, 2, 60, movement);
            check("locomotion_death_bypasses_gait_and_finishes", slowFrames.IsDying && slowFrames.DeathFinished && slowFrames.Transform.IsEqualApprox(actorTransform));
        }
        finally { slowFrames.Free(); fastFrames.Free(); }
    }

    private static void Advance(CharacterVisual model, double seconds, int frequency, Vector3 movement)
    {
        for (int i = 0; i < (int)Math.Round(seconds * frequency); i++) model.Animate(1d / frequency, movement);
    }

    private static Transform3D[] Parts(CharacterVisual model)
    {
        var result = new List<Transform3D>();
        void Visit(Node node)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is Node3D spatial) result.Add(spatial.Transform);
                Visit(child);
            }
        }
        Visit(model); return result.ToArray();
    }

    private static bool Similar(Transform3D[] left, Transform3D[] right, float tolerance) => left.Length == right.Length &&
        left.Zip(right).All(p => p.First.Origin.DistanceTo(p.Second.Origin) <= tolerance &&
            p.First.Basis.X.DistanceTo(p.Second.Basis.X) <= tolerance &&
            p.First.Basis.Y.DistanceTo(p.Second.Basis.Y) <= tolerance && p.First.Basis.Z.DistanceTo(p.Second.Basis.Z) <= tolerance);

    private static bool Finite(Transform3D transform) => new[] { transform.Origin, transform.Basis.X, transform.Basis.Y, transform.Basis.Z }
        .All(v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z));
}
