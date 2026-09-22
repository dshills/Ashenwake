using Godot;

namespace Ashenwake.Client;

/// <summary>Opt-in checks for actual room-owned lighting, machinery and resource lifetime.</summary>
public static class CinderDepthChecks
{
    public sealed record Evidence(string Context, string Style, string Quality, int Lights, int ActiveLights, int Mechanisms, int Meshes, double MotionTime);
    private static readonly string[] Styles = ["cinder_fields", "cinder_extraction", "cinder_furnace", "cinder_foundry", "cinder_storm"];

    public static void Detached(Action<string, bool> check)
    {
        check("cinder_depth_supports_only_regional_styles", Styles.All(CinderAtmosphere.Supports) &&
            new[] { "", "road", "verdant_heart", "CINDER_FIELDS" }.All(style => !CinderAtmosphere.Supports(style)));
        check("cinder_depth_rejects_invalid_style", Rejects(() => CinderAtmosphere.Create("unknown", 12, 10)));
        foreach (var (name, value) in new[] { ("zero", 0f), ("negative", -1f), ("nan", float.NaN), ("infinite", float.PositiveInfinity) })
        {
            check("cinder_depth_rejects_width_" + name, Rejects(() => CinderAtmosphere.Create(Styles[0], value, 10)));
            check("cinder_depth_rejects_depth_" + name, Rejects(() => CinderAtmosphere.Create(Styles[0], 12, value)));
        }
        foreach (string style in Styles)
        {
            var root = CinderAtmosphere.Create(style, 12, 10);
            Mesh[] retainedMeshes = []; Material[] retainedMaterials = [];
            try
            {
                void Check(string name, bool passed) => check("cinder_depth_" + style + "_" + name, passed);
                var meshes = Descendants(root).OfType<MeshInstance3D>().ToArray();
                retainedMeshes = meshes.Select(mesh => mesh.Mesh).ToArray();
                retainedMaterials = meshes.Select(mesh => mesh.Mesh.SurfaceGetMaterial(0)).ToArray();
                var ids = Ids(root); var neutral = Pose(root);
                var lights = Descendants(root).OfType<OmniLight3D>().ToArray();
                var energies = lights.Select(light => light.LightEnergy).ToArray();
                Check("bounded_physics_free_scene", root.LightCapacity == 4 && root.ActiveLightCount == 4 &&
                    root.MechanismCount == (style == "cinder_fields" ? 1 : style is "cinder_extraction" or "cinder_furnace" ? 2 : 0) && meshes.Length <= 12 && PhysicsFree(root));
                Check("fixtures_stay_outside_room", Peripheral(root, 12, 10));
                root.Animate(.1, false, false, "High");
                var moving = Pose(root); double time = root.MotionTime;
                Check("play_advances_only_cosmetic_mechanisms", time > 0 && (root.MechanismCount == 0 ? Same(neutral, moving) : !Same(neutral, moving)) && Ids(root).SequenceEqual(ids));
                root.Animate(10, true, false, "Performance");
                Check("paused_quality_changes_lights_without_motion", root.MotionTime == time && Same(moving, Pose(root)) && root.ActiveLightCount == 2);
                root.Animate(0, true, true, "High");
                Check("paused_reduced_effects_neutralizes_machinery", root.MotionTime == 0 && Same(neutral, Pose(root)) && root.ActiveLightCount == 4);
                root.Animate(.1, true, false, "High");
                Check("restoring_effects_keeps_paused_pose", root.MotionTime == 0 && Same(neutral, Pose(root)));
                root.Animate(.1, false, false, "High");
                Check("resume_repeats_initial_motion", root.MotionTime == time && Same(moving, Pose(root)));
                foreach (double delta in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
                    root.Animate(delta, false, false, "High");
                Check("invalid_delta_cannot_poison_motion", root.MotionTime == time && Same(moving, Pose(root)));
                bool bounded = true;
                for (int frame = 0; frame < 810; frame++)
                {
                    root.Animate(.1, false, false, frame % 100 < 50 ? "High" : "Performance");
                    bounded &= root.MotionTime is >= 0 and < 40 && double.IsFinite(root.MotionTime);
                    if (frame % 40 == 0) bounded &= Peripheral(root, 12, 10);
                }
                Check("two_rotations_keep_fixed_finite_resources", bounded && Ids(root).SequenceEqual(ids) && ValidLights(lights) &&
                    lights.Select(light => light.LightEnergy).SequenceEqual(energies));
            }
            finally { root.Free(); }
            check("cinder_depth_" + style + "_releases_owned_meshes_and_materials", retainedMeshes.Length > 0 &&
                retainedMeshes.All(mesh => !GodotObject.IsInstanceValid(mesh)) && retainedMaterials.All(material => !GodotObject.IsInstanceValid(material)));
        }
    }

    public static Evidence Inspect(Sandbox sandbox, string context, string style, float halfWidth, float halfDepth, Action<string, bool> check)
    {
        string hash = sandbox.Session.StateHash;
        var root = sandbox.CinderMotion;
        check("cinder_depth_live_" + context + "_matches_room", root is not null && root.Style == style &&
            Descendants(sandbox).OfType<CinderAtmosphere>().Count() == 1);
        if (root is null) throw new InvalidDataException("Missing live Cinder atmosphere.");
        var lights = Descendants(root).OfType<OmniLight3D>().ToArray();
        int meshes = Descendants(root).OfType<MeshInstance3D>().Count();
        check("cinder_depth_live_" + context + "_respects_quality_budget", root.LightCapacity == 4 &&
            root.ActiveLightCount == (sandbox.GraphicsQuality == "Performance" ? 2 : 4) &&
            lights.Count(light => light.IsVisibleInTree()) == root.ActiveLightCount && meshes <= 12);
        check("cinder_depth_live_" + context + "_remains_peripheral_and_cosmetic", Peripheral(root, halfWidth, halfDepth) && PhysicsFree(root) &&
            ValidLights(lights) && sandbox.Session.StateHash == hash);
        return new(context, style, sandbox.GraphicsQuality, lights.Length, root.ActiveLightCount, root.MechanismCount, meshes, root.MotionTime);
    }

    private static bool Peripheral(Node root, float halfWidth, float halfDepth)
    {
        bool Outside(Vector3 p) => p.IsFinite() && (Math.Abs(p.X) > halfWidth || Math.Abs(p.Z) > halfDepth);
        foreach (var mesh in Descendants(root).OfType<MeshInstance3D>())
        {
            var transform = Transform3D.Identity;
            for (Node? node = mesh; node is not null && node != root; node = node.GetParent())
                if (node is Node3D spatial) transform = spatial.Transform * transform;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                if (!mesh.Mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array().All(vertex => Outside(transform * vertex))) return false;
        }
        return Descendants(root).OfType<OmniLight3D>().All(light => Outside(light.Position));
    }
    private static bool ValidLights(IEnumerable<OmniLight3D> lights) => lights.All(light => !light.ShadowEnabled &&
        light.Position.IsFinite() && float.IsFinite(light.LightEnergy) && light.LightEnergy is > 0 and <= 3 && light.OmniRange is > 0 and <= 6);
    private static bool PhysicsFree(Node root) => !Descendants(root).Any(node => node is CollisionObject3D or CollisionShape3D or NavigationRegion3D or NavigationLink3D);
    private static Transform3D[] Pose(Node root) => Descendants(root).OfType<Node3D>().Select(node => node.Transform).ToArray();
    private static bool Same(Transform3D[] before, Transform3D[] after) => before.Length == after.Length && before.Zip(after).All(pair => pair.First.IsEqualApprox(pair.Second));
    private static ulong[] Ids(Node root) => Descendants(root).Select(node => node.GetInstanceId())
        .Concat(Descendants(root).OfType<MeshInstance3D>().SelectMany(mesh => new[] { mesh.Mesh.GetInstanceId(), mesh.Mesh.SurfaceGetMaterial(0).GetInstanceId() })).ToArray();
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(node => new[] { node }.Concat(Descendants(node)));
    private static bool Rejects(Func<Node> create)
    {
        Node? value = null;
        try { value = create(); return false; }
        catch (ArgumentException) { return true; }
        finally { value?.Free(); }
    }
}
