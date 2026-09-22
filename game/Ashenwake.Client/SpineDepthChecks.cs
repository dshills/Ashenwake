using Godot;

namespace Ashenwake.Client;

/// <summary>Opt-in checks for room-owned Spine lighting, cloth motion and resource lifetime.</summary>
public static class SpineDepthChecks
{
    public sealed record Evidence(string Context, string Style, string Quality, int Lights, int ActiveLights, int Banners, int Meshes, double MotionTime);
    private static readonly string[] Styles = ["spine_causeway", "spine_hall", "spine_archive", "spine_memory", "spine_warden"];

    public static void Detached(Action<string, bool> check)
    {
        check("spine_depth_supports_exact_regional_styles", Styles.All(SpineAtmosphere.Supports) &&
            new[] { "", "road", "cinder_storm", "spine", "SPINE_MEMORY" }.All(style => !SpineAtmosphere.Supports(style)));
        check("spine_depth_rejects_unknown_style", Rejects(() => SpineAtmosphere.Create("unknown", 12, 10)));
        foreach (var (name, value) in new[] { ("zero", 0f), ("negative", -1f), ("nan", float.NaN), ("infinity", float.PositiveInfinity), ("negative_infinity", float.NegativeInfinity) })
        {
            check("spine_depth_rejects_width_" + name, Rejects(() => SpineAtmosphere.Create(Styles[0], value, 10)));
            check("spine_depth_rejects_depth_" + name, Rejects(() => SpineAtmosphere.Create(Styles[0], 12, value)));
        }
        foreach (string style in Styles)
        {
            var root = SpineAtmosphere.Create(style, 12, 10);
            Mesh[] retainedMeshes = []; Material[] retainedMaterials = [];
            try
            {
                void Check(string name, bool passed) => check("spine_depth_" + style + "_" + name, passed);
                root.Animate(0, false, false, "High");
                var meshes = Descendants(root).OfType<MeshInstance3D>().ToArray();
                retainedMeshes = meshes.Select(mesh => mesh.Mesh).DistinctBy(mesh => mesh.GetInstanceId()).ToArray();
                retainedMaterials = meshes.SelectMany(Materials).DistinctBy(material => material.GetInstanceId()).ToArray();
                var lights = Descendants(root).OfType<OmniLight3D>().ToArray();
                var neutral = Pose(root); var ids = Ids(root);
                var lightPose = lights.Select(light => InRoot(light, root)).ToArray();
                var energies = lights.Select(light => light.LightEnergy).ToArray();
                Check("fixed_bounded_physics_free_resources", root.Style == style && root.LightCapacity == 4 && lights.Length == 4 &&
                    root.ActiveLightCount == 4 && lights.All(light => light.Visible) && root.BannerCount == 2 && meshes.Length is > 0 and <= 16 && PhysicsFree(root));
                Check("panels_share_owned_materials", retainedMeshes.Length > 0 && retainedMaterials.Length > 0 &&
                    retainedMaterials.Length < meshes.SelectMany(Materials).Count());
                Check("neutral_geometry_and_lights_are_peripheral", Peripheral(root, 12, 10) && ValidLights(lights));

                root.Animate(.1, false, false, "High");
                var moving = Pose(root); double time = root.MotionTime;
                Check("play_moves_banners_without_reallocation", time > 0 && !Same(neutral, moving) && Ids(root).SequenceEqual(ids));
                root.Animate(10, true, false, "High");
                Check("pause_freezes_pose_and_light_energy", root.MotionTime == time && Same(moving, Pose(root)) &&
                    lights.Select(light => light.LightEnergy).SequenceEqual(energies));
                root.Animate(10, true, false, "Performance");
                Check("paused_quality_switch_applies_immediately", root.MotionTime == time && Same(moving, Pose(root)) &&
                    root.ActiveLightCount == 2 && lights.Count(light => light.Visible) == 2 && Ids(root).SequenceEqual(ids));
                root.Animate(0, true, true, "High");
                Check("paused_reduced_effects_neutralizes_cloth", root.MotionTime == 0 && Same(neutral, Pose(root)) && root.ActiveLightCount == 4);
                root.Animate(10, false, true, "Performance");
                Check("reduced_effects_remains_static_at_lower_quality", root.MotionTime == 0 && Same(neutral, Pose(root)) && root.ActiveLightCount == 2);
                root.Animate(.1, true, false, "High");
                Check("restoring_effects_keeps_paused_pose", root.MotionTime == 0 && Same(neutral, Pose(root)) && root.ActiveLightCount == 4);
                root.Animate(.1, false, false, "High");
                Check("resume_repeats_initial_motion", root.MotionTime == time && Same(moving, Pose(root)));
                foreach (double delta in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
                    root.Animate(delta, false, false, "High");
                Check("invalid_delta_cannot_poison_motion", root.MotionTime == time && Same(moving, Pose(root)) && ValidLights(lights));

                bool bounded = true, peripheral = true, firstCycle = false, secondCycle = false;
                for (int frame = 0; frame < 500; frame++)
                {
                    root.Animate(.1, false, false, frame % 100 < 50 ? "High" : "Performance");
                    bounded &= double.IsFinite(root.MotionTime) && root.MotionTime is >= 0 and < 24 && ValidLights(lights) &&
                        Same(lightPose, lights.Select(light => InRoot(light, root)).ToArray()) && lights.Select(light => light.LightEnergy).SequenceEqual(energies);
                    if (frame % 20 == 0) peripheral &= Peripheral(root, 12, 10);
                    if (frame == 239) firstCycle = Same(moving, Pose(root));
                    if (frame == 479) secondCycle = Same(moving, Pose(root));
                }
                Check("two_full_wind_cycles_repeat_without_drift", firstCycle && secondCycle);
                Check("two_cycles_keep_fixed_finite_resources", bounded && Ids(root).SequenceEqual(ids) && PhysicsFree(root));
                Check("animated_banners_and_lights_remain_outside_room", peripheral && Peripheral(root, 12, 10));
            }
            finally { root.Free(); }
            check("spine_depth_" + style + "_releases_deduplicated_owned_resources", retainedMeshes.Length > 0 && retainedMaterials.Length > 0 &&
                retainedMeshes.All(mesh => !GodotObject.IsInstanceValid(mesh)) && retainedMaterials.All(material => !GodotObject.IsInstanceValid(material)));
        }
    }

    public static Evidence Inspect(Sandbox sandbox, string context, string style, float halfWidth, float halfDepth, Action<string, bool> check)
    {
        string hash = sandbox.Session.StateHash;
        var root = sandbox.SpineMotion;
        check("spine_depth_live_" + context + "_matches_room", root is not null && root.Style == style &&
            Descendants(sandbox).OfType<SpineAtmosphere>().Count() == 1);
        if (root is null) throw new InvalidDataException("Missing live Spine atmosphere.");
        var lights = Descendants(root).OfType<OmniLight3D>().ToArray();
        int meshes = Descendants(root).OfType<MeshInstance3D>().Count();
        check("spine_depth_live_" + context + "_respects_quality_budget", root.LightCapacity == 4 && lights.Length == 4 && root.BannerCount == 2 &&
            root.ActiveLightCount == (sandbox.GraphicsQuality == "Performance" ? 2 : 4) && lights.Count(light => light.IsVisibleInTree()) == root.ActiveLightCount && meshes is > 0 and <= 16);
        check("spine_depth_live_" + context + "_is_finite_peripheral_and_cosmetic", Peripheral(root, halfWidth, halfDepth) && PhysicsFree(root) &&
            ValidLights(lights) && double.IsFinite(root.MotionTime) && root.MotionTime is >= 0 and < 24 && sandbox.Session.StateHash == hash);
        return new(context, style, sandbox.GraphicsQuality, lights.Length, root.ActiveLightCount, root.BannerCount, meshes, root.MotionTime);
    }

    private static bool Peripheral(Node root, float halfWidth, float halfDepth)
    {
        bool Outside(Vector3 point) => point.IsFinite() && (Math.Abs(point.X) > halfWidth || Math.Abs(point.Z) > halfDepth);
        foreach (var mesh in Descendants(root).OfType<MeshInstance3D>())
        {
            Transform3D transform = InRoot(mesh, root);
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                var vertices = mesh.Mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                if (vertices.Length == 0 || !vertices.All(vertex => Outside(transform * vertex))) return false;
            }
        }
        return Descendants(root).OfType<OmniLight3D>().All(light => Outside(InRoot(light, root).Origin));
    }

    private static Transform3D InRoot(Node3D child, Node root)
    {
        var transform = Transform3D.Identity;
        for (Node? node = child; node is not null && node != root; node = node.GetParent())
            if (node is Node3D spatial) transform = spatial.Transform * transform;
        return transform;
    }

    private static bool ValidLights(IEnumerable<OmniLight3D> lights) => lights.All(light => !light.ShadowEnabled &&
        light.Position.IsFinite() && float.IsFinite(light.LightEnergy) && light.LightEnergy is > 0 and <= 3 && light.OmniRange is > 0 and <= 6);
    private static bool PhysicsFree(Node root) => !Descendants(root).Any(node => node is CollisionObject3D or CollisionShape3D or NavigationRegion3D or NavigationLink3D);
    private static Transform3D[] Pose(Node root) => Descendants(root).OfType<Node3D>().Select(node => node.Transform).ToArray();
    private static bool Same(Transform3D[] before, Transform3D[] after) => before.Length == after.Length && before.Zip(after).All(pair => pair.First.IsEqualApprox(pair.Second));
    private static ulong[] Ids(Node root) => Descendants(root).Select(node => node.GetInstanceId())
        .Concat(Descendants(root).OfType<MeshInstance3D>().SelectMany(mesh => new[] { mesh.Mesh.GetInstanceId() }.Concat(Materials(mesh).Select(material => material.GetInstanceId())))).ToArray();
    private static IEnumerable<Material> Materials(MeshInstance3D mesh)
    {
        if (mesh.MaterialOverride is { } material) yield return material;
        for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
        {
            if (mesh.GetSurfaceOverrideMaterial(surface) is { } replacement) yield return replacement;
            if (mesh.Mesh.SurfaceGetMaterial(surface) is { } basis) yield return basis;
        }
    }
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(node => new[] { node }.Concat(Descendants(node)));
    private static bool Rejects(Func<Node> create)
    {
        Node? value = null;
        try { value = create(); return false; }
        catch (ArgumentException) { return true; }
        finally { value?.Free(); }
    }
}
