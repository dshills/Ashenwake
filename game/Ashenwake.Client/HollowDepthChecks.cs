using Godot;

namespace Ashenwake.Client;

/// <summary>Opt-in evidence for Hollow lighting, suspended slate and owned resource lifetime.</summary>
public static class HollowDepthChecks
{
    public sealed record Evidence(string Context, string Style, string Quality, int Lights, int ActiveLights,
        int Fragments, int ActiveFragments, int Meshes, double MotionTime);
    private static readonly string[] Styles = ["hollow_rooms", "hollow_memory", "hollow_vault", "hollow_breach"];
    // Godot's dummy headless renderer does not provide MultiMesh transform readback.
    // Actual instance geometry and movement are asserted by the rendered diagnostic.
    private static bool NativeInstances => DisplayServer.GetName() != "headless";

    public static void Detached(Action<string, bool> check)
    {
        check("hollow_depth_supports_exact_regional_styles", Styles.All(HollowAtmosphere.Supports) &&
            new[] { "", "road", "spine_memory", "hollow", "HOLLOW_MEMORY" }.All(style => !HollowAtmosphere.Supports(style)));
        check("hollow_depth_rejects_unknown_style", Rejects(() => HollowAtmosphere.Create("unknown", 12, 10)));
        foreach (var (name, value) in new[] { ("zero", 0f), ("negative", -1f), ("nan", float.NaN), ("infinity", float.PositiveInfinity), ("negative_infinity", float.NegativeInfinity) })
        {
            check("hollow_depth_rejects_width_" + name, Rejects(() => HollowAtmosphere.Create(Styles[0], value, 10)));
            check("hollow_depth_rejects_depth_" + name, Rejects(() => HollowAtmosphere.Create(Styles[0], 12, value)));
        }
        foreach (string style in Styles)
        {
            var root = HollowAtmosphere.Create(style, 12, 10);
            MultiMesh? retainedInstances = null;
            Mesh[] retainedMeshes = []; Material[] retainedMaterials = []; Texture2D[] cachedTextures = [];
            int textureCount = SurfaceMaterials.CachedTextureCount;
            try
            {
                void Check(string name, bool passed) => check("hollow_depth_" + style + "_" + name, passed);
                root.Animate(0, false, false, "High");
                var fragments = Fragments(root); retainedInstances = fragments.Multimesh;
                var shardVertices = Vertices(retainedInstances.Mesh).ToArray();
                retainedMeshes = Meshes(root).DistinctBy(mesh => mesh.GetInstanceId()).ToArray();
                retainedMaterials = Materials(root).DistinctBy(material => material.GetInstanceId()).ToArray();
                cachedTextures = retainedMaterials.OfType<StandardMaterial3D>().SelectMany(material =>
                    new[] { material.AlbedoTexture, material.NormalTexture, material.RoughnessTexture }).OfType<Texture2D>()
                    .DistinctBy(texture => texture.GetInstanceId()).ToArray();
                var lights = Descendants(root).OfType<OmniLight3D>().ToArray();
                var ids = Ids(root); var neutral = FragmentPose(root);
                var fixturePose = NodePose(root); var energies = lights.Select(light => light.LightEnergy).ToArray();
                Check("fixed_bounded_physics_free_resources", root.Style == style && root.LightCapacity == 4 && lights.Length == 4 && root.ActiveLightCount == 4 &&
                    lights.All(light => light.Visible) && root.FragmentCapacity == 8 && root.ActiveFragmentCount == 8 && retainedInstances.InstanceCount == 8 &&
                    retainedInstances.VisibleInstanceCount == 8 && fragments.Visible && Descendants(root).OfType<MeshInstance3D>().Count() is > 0 and <= 12 &&
                    Descendants(root).OfType<MultiMeshInstance3D>().Count() == 1 && PhysicsFree(root));
                Check("shared_slate_mesh_has_finite_geometry", shardVertices.Length > 0 && shardVertices.All(point => point.IsFinite()));
                Check("fixtures_and_lights_stay_peripheral", Peripheral(root, 12, 10, false) && ValidLights(lights));
                if (NativeInstances) Check("native_neutral_fragments_stay_peripheral", Peripheral(root, 12, 10, true));

                root.Animate(.1, false, false, "High");
                double time = root.MotionTime; var moving = FragmentPose(root);
                Check("play_advances_only_cosmetic_clock", time > 0 && Same(fixturePose, NodePose(root)) && Ids(root).SequenceEqual(ids));
                if (NativeInstances) Check("native_fragments_actually_drift", !Same(neutral, moving));
                root.Animate(10, true, false, "High");
                Check("pause_freezes_clock_and_light_energy", root.MotionTime == time && Same(fixturePose, NodePose(root)) && Same(moving, FragmentPose(root)) &&
                    lights.Select(light => light.LightEnergy).SequenceEqual(energies));
                root.Animate(10, true, false, "Performance");
                Check("paused_quality_switch_preserves_resources_and_pose", root.MotionTime == time && Same(moving, FragmentPose(root)) &&
                    root.ActiveLightCount == 2 && lights.Count(light => light.Visible) == 2 && root.ActiveFragmentCount == 4 &&
                    retainedInstances.VisibleInstanceCount == 4 && Ids(root).SequenceEqual(ids));
                if (NativeInstances) Check("native_performance_keeps_both_fragment_clusters", BothSides(root, 12));
                root.Animate(0, true, true, "High");
                Check("paused_reduced_effects_hides_and_neutralizes_fragments", root.MotionTime == 0 && root.ActiveFragmentCount == 0 && !fragments.Visible &&
                    root.ActiveLightCount == 4 && Same(neutral, FragmentPose(root)));
                root.Animate(10, false, true, "Performance");
                Check("reduced_effects_stays_hidden_at_lower_quality", root.MotionTime == 0 && root.ActiveFragmentCount == 0 && !fragments.Visible &&
                    root.ActiveLightCount == 2 && Same(neutral, FragmentPose(root)) && Ids(root).SequenceEqual(ids));
                root.Animate(.1, true, false, "High");
                Check("restoring_effects_while_paused_shows_neutral_fragments", root.MotionTime == 0 && root.ActiveFragmentCount == 8 && fragments.Visible &&
                    root.ActiveLightCount == 4 && Same(neutral, FragmentPose(root)));
                root.Animate(.1, false, false, "High");
                Check("resume_repeats_initial_motion", root.MotionTime == time && Same(moving, FragmentPose(root)));
                foreach (double delta in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
                    root.Animate(delta, false, false, "High");
                Check("invalid_delta_cannot_poison_pose", root.MotionTime == time && Same(moving, FragmentPose(root)) && ValidLights(lights));

                bool bounded = true, peripheral = true, firstCycle = false, secondCycle = false;
                for (int frame = 0; frame < 740; frame++)
                {
                    bool high = frame % 100 < 50;
                    root.Animate(.1, false, false, high ? "High" : "Performance");
                    bounded &= double.IsFinite(root.MotionTime) && root.MotionTime is >= 0 and < 36 && root.ActiveFragmentCount == (high ? 8 : 4) &&
                        root.ActiveLightCount == (high ? 4 : 2) && ValidLights(lights) && Same(fixturePose, NodePose(root)) &&
                        lights.Select(light => light.LightEnergy).SequenceEqual(energies);
                    if (NativeInstances) peripheral &= InstancesPeripheral(root, shardVertices, 12, 10);
                    if (frame % 20 == 0)
                    {
                        peripheral &= Peripheral(root, 12, 10, false);
                        bounded &= Ids(root).SequenceEqual(ids);
                    }
                    if (frame == 359) firstCycle = Same(moving, FragmentPose(root));
                    if (frame == 719) secondCycle = Same(moving, FragmentPose(root));
                }
                Check("two_cycles_keep_fixed_finite_resources", bounded && Ids(root).SequenceEqual(ids) && PhysicsFree(root));
                if (NativeInstances) Check("native_two_full_cycles_repeat_without_drift", firstCycle && secondCycle);
                Check(NativeInstances ? "native_all_fragment_vertices_remain_peripheral" : "headless_fixtures_remain_peripheral", peripheral && Peripheral(root, 12, 10, NativeInstances));
            }
            finally { root.Free(); }
            check("hollow_depth_" + style + "_releases_owned_instances_meshes_and_materials", retainedInstances is not null && !GodotObject.IsInstanceValid(retainedInstances) &&
                retainedMeshes.Length > 0 && retainedMaterials.Length > 0 && retainedMeshes.All(mesh => !GodotObject.IsInstanceValid(mesh)) &&
                retainedMaterials.All(material => !GodotObject.IsInstanceValid(material)));
            check("hollow_depth_" + style + "_preserves_shared_texture_cache", cachedTextures.Length > 0 && cachedTextures.All(GodotObject.IsInstanceValid) &&
                SurfaceMaterials.CachedTextureCount == textureCount);
        }
    }

    public static Evidence Inspect(Sandbox sandbox, string context, string style, float halfWidth, float halfDepth, Action<string, bool> check)
    {
        string hash = sandbox.Session.StateHash;
        var root = sandbox.HollowMotion;
        check("hollow_depth_live_" + context + "_matches_room", root is not null && root.Style == style && Descendants(sandbox).OfType<HollowAtmosphere>().Count() == 1);
        if (root is null) throw new InvalidDataException("Missing live Hollow atmosphere.");
        var lights = Descendants(root).OfType<OmniLight3D>().ToArray();
        var fragments = Fragments(root);
        int meshes = Descendants(root).OfType<MeshInstance3D>().Count(), instanceRenderers = Descendants(root).OfType<MultiMeshInstance3D>().Count();
        int activeFragments = sandbox.ReducedEffects ? 0 : sandbox.GraphicsQuality == "Performance" ? 4 : 8;
        check("hollow_depth_live_" + context + "_respects_quality_budget", root.LightCapacity == 4 && lights.Length == 4 && root.FragmentCapacity == 8 &&
            root.ActiveLightCount == (sandbox.GraphicsQuality == "Performance" ? 2 : 4) && lights.Count(light => light.IsVisibleInTree()) == root.ActiveLightCount &&
            root.ActiveFragmentCount == activeFragments && fragments.Visible == !sandbox.ReducedEffects && fragments.Multimesh.InstanceCount == 8 &&
            fragments.Multimesh.VisibleInstanceCount == (sandbox.GraphicsQuality == "Performance" ? 4 : 8) && meshes is > 0 and <= 12 && instanceRenderers == 1);
        check("hollow_depth_live_" + context + "_is_finite_and_cosmetic", PhysicsFree(root) && ValidLights(lights) &&
            double.IsFinite(root.MotionTime) && root.MotionTime is >= 0 and < 36 && (!sandbox.ReducedEffects || root.MotionTime == 0) && sandbox.Session.StateHash == hash);
        check("hollow_depth_live_" + context + "_fixtures_are_peripheral", Peripheral(root, halfWidth, halfDepth, false));
        if (NativeInstances)
        {
            check("hollow_depth_live_" + context + "_native_instances_are_peripheral", Peripheral(root, halfWidth, halfDepth, true));
            if (root.ActiveFragmentCount > 0) check("hollow_depth_live_" + context + "_native_both_clusters_visible", BothSides(root, halfWidth));
        }
        return new(context, style, sandbox.GraphicsQuality, lights.Length, root.ActiveLightCount,
            root.FragmentCapacity, root.ActiveFragmentCount, meshes + instanceRenderers, root.MotionTime);
    }

    private static MultiMeshInstance3D Fragments(Node root) => Descendants(root).OfType<MultiMeshInstance3D>().Single(node => node.Name == "SuspendedSlate");
    private static Transform3D[] FragmentPose(Node root)
    {
        if (!NativeInstances) return [];
        var fragments = Fragments(root); var parent = InRoot(fragments, root);
        return Enumerable.Range(0, fragments.Multimesh.InstanceCount).Select(index => parent * fragments.Multimesh.GetInstanceTransform(index)).ToArray();
    }
    private static bool BothSides(Node root, float halfWidth)
    {
        var fragments = Fragments(root); var pose = FragmentPose(root).Take(fragments.Multimesh.VisibleInstanceCount).ToArray();
        return pose.Any(transform => transform.Origin.X < -halfWidth) && pose.Any(transform => transform.Origin.X > halfWidth);
    }
    private static bool Peripheral(Node root, float halfWidth, float halfDepth, bool includeInstances)
    {
        bool Outside(Vector3 point) => point.IsFinite() && (Math.Abs(point.X) > halfWidth || Math.Abs(point.Z) > halfDepth);
        foreach (var mesh in Descendants(root).OfType<MeshInstance3D>())
        {
            var transform = InRoot(mesh, root); var vertices = Vertices(mesh.Mesh).ToArray();
            if (vertices.Length == 0 || !vertices.All(vertex => Outside(transform * vertex))) return false;
        }
        if (includeInstances && !InstancesPeripheral(root, Vertices(Fragments(root).Multimesh.Mesh).ToArray(), halfWidth, halfDepth)) return false;
        return Descendants(root).OfType<OmniLight3D>().All(light => Outside(InRoot(light, root).Origin));
    }
    private static bool InstancesPeripheral(Node root, Vector3[] vertices, float halfWidth, float halfDepth)
    {
        if (vertices.Length == 0) return false;
        foreach (var transform in FragmentPose(root))
            foreach (var vertex in vertices)
            {
                Vector3 point = transform * vertex;
                if (!point.IsFinite() || Math.Abs(point.X) <= halfWidth && Math.Abs(point.Z) <= halfDepth) return false;
            }
        return true;
    }
    private static IEnumerable<Vector3> Vertices(Mesh mesh) => Enumerable.Range(0, mesh.GetSurfaceCount())
        .SelectMany(surface => mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array());
    private static IEnumerable<Mesh> Meshes(Node root) => Descendants(root).OfType<MeshInstance3D>().Select(renderer => renderer.Mesh)
        .Concat(Descendants(root).OfType<MultiMeshInstance3D>().Select(renderer => renderer.Multimesh.Mesh));
    private static IEnumerable<Material> Materials(Node root)
    {
        foreach (var renderer in Descendants(root).OfType<GeometryInstance3D>())
            if (renderer.MaterialOverride is { } overrideMaterial) yield return overrideMaterial;
        foreach (var renderer in Descendants(root).OfType<MeshInstance3D>())
            for (int surface = 0; surface < renderer.Mesh.GetSurfaceCount(); surface++)
                if (renderer.GetSurfaceOverrideMaterial(surface) is { } surfaceOverride) yield return surfaceOverride;
        foreach (var mesh in Meshes(root))
            for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
                if (mesh.SurfaceGetMaterial(surface) is { } surfaceMaterial) yield return surfaceMaterial;
    }
    private static Transform3D InRoot(Node3D child, Node root)
    {
        var transform = Transform3D.Identity;
        for (Node? node = child; node is not null && node != root; node = node.GetParent())
            if (node is Node3D spatial) transform = spatial.Transform * transform;
        return transform;
    }
    private static ulong[] Ids(Node root) => Descendants(root).Select(node => node.GetInstanceId())
        .Concat(Meshes(root).Select(mesh => mesh.GetInstanceId())).Concat(Materials(root).Select(material => material.GetInstanceId()))
        .Concat(Descendants(root).OfType<MultiMeshInstance3D>().Select(renderer => renderer.Multimesh.GetInstanceId())).ToArray();
    private static bool ValidLights(IEnumerable<OmniLight3D> lights) => lights.All(light => !light.ShadowEnabled && light.Position.IsFinite() &&
        float.IsFinite(light.LightEnergy) && light.LightEnergy is > 0 and <= 3 && float.IsFinite(light.OmniRange) && light.OmniRange is > 0 and <= 6);
    private static bool PhysicsFree(Node root) => !Descendants(root).Any(node => node is CollisionObject3D or CollisionShape3D or CollisionPolygon3D or NavigationRegion3D or NavigationLink3D);
    private static Transform3D[] NodePose(Node root) => Descendants(root).OfType<Node3D>().Select(node => node.Transform).ToArray();
    private static bool Same(Transform3D[] before, Transform3D[] after) => before.Length == after.Length && before.Zip(after).All(pair => pair.First.IsEqualApprox(pair.Second));
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(node => new[] { node }.Concat(Descendants(node)));
    private static bool Rejects(Func<Node> create)
    {
        Node? value = null;
        try { value = create(); return false; }
        catch (ArgumentException) { return true; }
        finally { value?.Free(); }
    }
}
