using Godot;

namespace Ashenwake.Client;

/// <summary>Opt-in scene diagnostics for the Verdant art pass; no campaign or combat commands.</summary>
public static class VerdantDepthChecks
{
    public sealed record Evidence(string Context, string Style, string Quality, int FoliageCapacity, int ActiveFoliage,
        int LightCapacity, int ActiveLights, double MotionTime, bool NativeGeometryInspected);
    private static readonly string[] Styles = ["verdant_ruins", "verdant_village", "verdant_hunt", "verdant_heart", "verdant_shrine"];
    private static bool NativeGeometry => DisplayServer.GetName() != "headless";

    public static void Detached(Action<string, bool> check)
    {
        CheckLeaf(check);
        CheckHeart(check);
        check("verdant_depth_supports_exact_styles", Styles.All(VerdantAtmosphere.Supports) &&
            new[] { "", "road", "crypt", "hollow_breach", "VERDANT_RUINS" }.All(style => !VerdantAtmosphere.Supports(style)));
        check("verdant_depth_rejects_unknown_style", Rejects(() => VerdantAtmosphere.Create("unknown", 12, 10)));
        foreach (var (name, value) in new[] { ("zero", 0f), ("negative", -1f), ("nan", float.NaN), ("infinity", float.PositiveInfinity) })
        {
            check("verdant_depth_rejects_width_" + name, Rejects(() => VerdantAtmosphere.Create(Styles[0], value, 10)));
            check("verdant_depth_rejects_depth_" + name, Rejects(() => VerdantAtmosphere.Create(Styles[0], 12, value)));
        }
        foreach (string style in Styles)
        {
            var root = VerdantAtmosphere.Create(style, 12, 10);
            MultiMesh? retainedInstances = null; Mesh? retainedLeaf = null; Material? retainedMaterial = null;
            try
            {
                void Check(string name, bool passed) => check("verdant_depth_" + style + "_" + name, passed);
                root.Animate(0, false, false, "High");
                var foliage = Foliage(root); var lights = Lights(root);
                retainedInstances = foliage.Multimesh; retainedLeaf = retainedInstances.Mesh; retainedMaterial = foliage.MaterialOverride;
                var ids = Ids(root); var lightPose = lights.Select(light => light.Transform).ToArray();
                var neutral = FoliagePose(foliage); var baseEnergy = Energies(lights);
                Check("fixed_bounded_physics_free_resources", root.Style == style && root.FoliageCapacity == 72 && root.ActiveFoliageCount == 72 &&
                    retainedInstances.InstanceCount == 72 && retainedInstances.VisibleInstanceCount == 72 && root.LightCapacity == 4 &&
                    root.ActiveLightCount == 4 && lights.Length == 4 && lights.All(light => light.Visible && !light.ShadowEnabled) && PhysicsFree(root));
                Check("finite_lights_and_geometry", ValidLights(lights) && MeshVertices(root).All(point => point.IsFinite()));
                if (NativeGeometry) Check("native_neutral_foliage_and_fixtures_are_peripheral", Peripheral(root, 12, 10));
                root.Animate(.1, false, false, "High");
                double time = root.MotionTime; var moving = FoliagePose(foliage); var movingEnergy = Energies(lights);
                Check("motion_advances_without_reallocation", time > 0 && SameIds(root, ids));
                if (NativeGeometry) Check("native_foliage_actually_sways", !SamePose(neutral, moving));
                root.Animate(20, true, false, "High");
                Check("pause_freezes_motion_and_energy", root.MotionTime == time && SamePose(moving, FoliagePose(foliage)) && SameEnergy(lights, movingEnergy));
                root.Animate(20, true, false, "Performance");
                Check("paused_quality_switch_applies_immediately", root.MotionTime == time && root.ActiveFoliageCount == 36 &&
                    foliage.Multimesh.VisibleInstanceCount == 36 && root.ActiveLightCount == 2 && lights.Count(light => light.Visible) == 2 && SameIds(root, ids));
                root.Animate(20, true, true, "High");
                Check("paused_reduced_effects_retains_neutral_scenery", root.MotionTime == 0 && root.ActiveFoliageCount == 72 && root.ActiveLightCount == 4 &&
                    SamePose(neutral, FoliagePose(foliage)) && SameEnergy(lights, baseEnergy));
                root.Animate(20, false, true, "Performance");
                Check("reduced_effects_stays_static_at_lower_quality", root.MotionTime == 0 && root.ActiveFoliageCount == 36 && root.ActiveLightCount == 2 &&
                    SamePose(neutral, FoliagePose(foliage)) && SameEnergy(lights, baseEnergy));
                root.Animate(20, true, false, "High");
                Check("restoring_effects_does_not_advance_paused_motion", root.MotionTime == 0 && SamePose(neutral, FoliagePose(foliage)));
                root.Animate(.1, false, false, "High");
                Check("resume_repeats_initial_motion", root.MotionTime == time && SamePose(moving, FoliagePose(foliage)) && SameEnergy(lights, movingEnergy));
                foreach (double delta in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
                {
                    root.Animate(delta, false, false, "High");
                    Check("invalid_delta_" + delta + "_cannot_poison_motion", root.MotionTime == time && SamePose(moving, FoliagePose(foliage)) && ValidLights(lights));
                }
                bool finite = true, outside = true;
                for (int frame = 0; frame < 500; frame++)
                {
                    root.Animate(.1, false, false, frame % 100 < 50 ? "High" : "Performance");
                    finite &= double.IsFinite(root.MotionTime) && root.MotionTime is >= 0 and < 24 && ValidLights(lights) &&
                        SamePose(lightPose, lights.Select(light => light.Transform).ToArray());
                    if (NativeGeometry && frame % 10 == 0) outside &= Peripheral(root, 12, 10);
                }
                Check("two_full_wind_periods_remain_finite_and_bounded", finite && SameIds(root, ids) && PhysicsFree(root));
                if (NativeGeometry) Check("native_sway_never_enters_authoritative_room", outside);
            }
            finally { root.Free(); }
            check("verdant_depth_" + style + "_releases_owned_resources", retainedInstances is not null && retainedLeaf is not null && retainedMaterial is not null &&
                !GodotObject.IsInstanceValid(retainedInstances) && !GodotObject.IsInstanceValid(retainedLeaf) && !GodotObject.IsInstanceValid(retainedMaterial));
        }
    }

    public static Evidence Inspect(Sandbox sandbox, string context, string expectedStyle, float halfWidth, float halfDepth, Action<string, bool> check)
    {
        string hash = sandbox.Session.StateHash;
        var root = sandbox.VerdantMotion;
        check("verdant_depth_live_" + context + "_matches_room", root is not null && root.Style == expectedStyle &&
            Descendants(sandbox).OfType<VerdantAtmosphere>().Count() == 1);
        if (root is null) throw new InvalidDataException("Missing live Verdant atmosphere.");
        var foliage = Foliage(root); var lights = Lights(root);
        bool performance = sandbox.GraphicsQuality == "Performance";
        check("verdant_depth_live_" + context + "_respects_quality_budgets", root.FoliageCapacity == 72 && root.LightCapacity == 4 &&
            root.ActiveFoliageCount == (performance ? 36 : 72) && foliage.Multimesh.VisibleInstanceCount == root.ActiveFoliageCount &&
            root.ActiveLightCount == (performance ? 2 : 4) && lights.Count(light => light.IsVisibleInTree()) == root.ActiveLightCount);
        check("verdant_depth_live_" + context + "_is_finite_and_cosmetic", ValidLights(lights) && PhysicsFree(root) &&
            double.IsFinite(root.MotionTime) && root.MotionTime is >= 0 and < 24 && sandbox.Session.StateHash == hash);
        if (NativeGeometry) check("verdant_depth_live_" + context + "_native_geometry_is_peripheral", Peripheral(root, halfWidth, halfDepth));
        return new(context, root.Style, sandbox.GraphicsQuality, root.FoliageCapacity, root.ActiveFoliageCount,
            root.LightCapacity, root.ActiveLightCount, root.MotionTime, NativeGeometry);
    }

    private static void CheckHeart(Action<string, bool> check)
    {
        var heart = VerdantHeartVisual.Create(12, 10);
        var other = VerdantHeartVisual.Create(12, 10);
        Material? retainedSeedMaterial = null; Mesh? retainedSeedMesh = null;
        try
        {
            var meshes = Descendants(heart).OfType<MeshInstance3D>().ToArray();
            var samples = meshes.Select(mesh => (Mesh: mesh, Vertices: LocalVertices(mesh.Mesh).ToArray())).ToArray();
            var ids = Ids(heart);
            var seedNode = Descendants(heart).OfType<MeshInstance3D>().Single(mesh => mesh.Name == "LivingSeed");
            var seed = (StandardMaterial3D)seedNode.MaterialOverride;
            retainedSeedMaterial = seed; retainedSeedMesh = seedNode.Mesh;
            var otherSeed = (StandardMaterial3D)Descendants(other).OfType<MeshInstance3D>().Single(mesh => mesh.Name == "LivingSeed").MaterialOverride;
            var otherColor = otherSeed.AlbedoColor;
            check("verdant_depth_heart_keeps_thirteen_joints_and_mesh_budget", heart.ArticulatedPartCount == 13 && heart.TransientCapacity == 0 && meshes.Length <= 80 && PhysicsFree(heart));
            check("verdant_depth_heart_skin_is_textured_and_room_owned", seed.NormalEnabled && seed.NormalTexture is not null && seed.AlbedoTexture is not null &&
                seed.GetInstanceId() != otherSeed.GetInstanceId() && seed.NormalTexture.GetInstanceId() == otherSeed.NormalTexture.GetInstanceId());
            bool safe = true;
            void Sample()
            {
                safe &= double.IsFinite(heart.MotionTime) && heart.MotionTime >= 0 && heart.MotionTime < Math.Tau / 1.35;
                foreach (var sample in samples)
                {
                    var transform = RelativeTransform(sample.Mesh, heart);
                    foreach (var vertex in sample.Vertices)
                    {
                        Vector3 point = transform * vertex;
                        safe &= point.IsFinite() && point.Z < -10 && Math.Abs(point.X) <= 2.25f;
                    }
                }
            }
            for (int roots = 3; roots >= 0; roots--)
            {
                heart.SetState(roots, false);
                for (int frame = 0; frame < 50; frame++) { heart.Animate(.1, false, false); Sample(); }
            }
            heart.SetState(0, true);
            for (int frame = 0; frame < 36; frame++) { Sample(); heart.Animate(.1, false, false); }
            check("verdant_depth_heart_every_root_and_victory_pose_stays_behind_wall", safe && heart.VictoryProgress == 1 && !heart.IsTransitioning);
            check("verdant_depth_heart_motion_reuses_all_geometry", SameIds(heart, ids) && otherSeed.AlbedoColor == otherColor);
            heart.SetState(3, false); heart.Animate(.1, false, false);
            var pose = NodePose(heart); double time = heart.MotionTime;
            heart.Animate(100, true, false);
            check("verdant_depth_heart_pause_freezes_full_pose", heart.MotionTime == time && SamePose(pose, NodePose(heart)));
            foreach (double delta in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d }) heart.Animate(delta, false, false);
            check("verdant_depth_heart_invalid_deltas_leave_finite_pose", heart.MotionTime == time && SamePose(pose, NodePose(heart)) && double.IsFinite(heart.MotionTime));
            heart.SetState(0, true); heart.Animate(.1, false, false);
            check("verdant_depth_heart_real_state_change_starts_one_bloom", heart.IsTransitioning);
            heart.Animate(0, true, true);
            pose = NodePose(heart);
            check("verdant_depth_heart_reduced_effects_settles_even_while_paused", heart.MotionTime == 0 && heart.VictoryProgress == 1 && !heart.IsTransitioning);
            heart.Animate(.1, true, false); heart.Animate(.1, false, false);
            check("verdant_depth_heart_restoring_effects_cannot_replay_bloom", !heart.IsTransitioning && SamePose(pose, NodePose(heart)));
        }
        finally { heart.Free(); other.Free(); }
        check("verdant_depth_heart_releases_owned_seed_resources", retainedSeedMaterial is not null && retainedSeedMesh is not null &&
            !GodotObject.IsInstanceValid(retainedSeedMaterial) && !GodotObject.IsInstanceValid(retainedSeedMesh));
    }

    private static void CheckLeaf(Action<string, bool> check)
    {
        using var leaf = BotanicalGeometry.Leaf(2.18f, 1.04f, .24f);
        var arrays = leaf.SurfaceGetArrays(0);
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        bool paired = indices.Length == 120, nondegenerate = true;
        for (int i = 0; i + 5 < indices.Length; i += 6)
        {
            nondegenerate &= (vertices[indices[i + 1]] - vertices[indices[i]]).Cross(vertices[indices[i + 2]] - vertices[indices[i]]).LengthSquared() > .0000001f;
            for (int corner = 0; corner < 3; corner++)
            {
                int a = indices[i + corner], b = indices[i + 5 - corner];
                paired &= vertices[a].IsEqualApprox(vertices[b]) && normals[a].Dot(normals[b]) < -.99f;
            }
        }
        check("verdant_depth_leaf_has_curved_indexed_double_sided_geometry", vertices.Length > 12 && vertices.All(vertex => vertex.IsFinite()) && paired && nondegenerate);
        check("verdant_depth_leaf_has_finite_normals_and_uv", normals.Length == vertices.Length && uv.Length == vertices.Length &&
            normals.All(normal => normal.IsFinite() && normal.LengthSquared() is > .99f and < 1.01f) &&
            uv.All(point => point.IsFinite() && point.X is >= 0 and <= 1 && point.Y is >= 0 and <= 1));
        check("verdant_depth_leaf_tapers_to_authored_curled_tip", vertices.Max(vertex => vertex.Y) == 2.18f &&
            vertices.Where(vertex => vertex.Y > 2.179f).All(vertex => Math.Abs(vertex.X) < .02f && Math.Abs(vertex.Z - .24f) < .001f));
    }

    private static MultiMeshInstance3D Foliage(Node root) => Descendants(root).OfType<MultiMeshInstance3D>().Single();
    private static OmniLight3D[] Lights(Node root) => Descendants(root).OfType<OmniLight3D>().ToArray();
    private static float[] Energies(IEnumerable<OmniLight3D> lights) => lights.Select(light => light.LightEnergy).ToArray();
    private static bool SameEnergy(IEnumerable<OmniLight3D> lights, float[] before) => lights.Select((light, i) => Math.Abs(light.LightEnergy - before[i]) < .000001f).All(same => same);
    private static Transform3D[] NodePose(Node root) => Descendants(root).OfType<Node3D>().Select(node => node.Transform).ToArray();
    private static Transform3D[] FoliagePose(MultiMeshInstance3D foliage) => NativeGeometry
        ? Enumerable.Range(0, foliage.Multimesh.InstanceCount).Select(foliage.Multimesh.GetInstanceTransform).ToArray() : [];
    private static bool SamePose(Transform3D[] a, Transform3D[] b) => a.Length == b.Length && a.Zip(b).All(pair => pair.First.IsEqualApprox(pair.Second));
    private static ulong[] Ids(Node root) => Descendants(root).Select(node => node.GetInstanceId())
        .Concat(Descendants(root).OfType<MeshInstance3D>().Select(mesh => mesh.Mesh.GetInstanceId()))
        .Concat(Descendants(root).OfType<MultiMeshInstance3D>().SelectMany(mesh => new[] { mesh.Multimesh.GetInstanceId(), mesh.Multimesh.Mesh.GetInstanceId(), mesh.MaterialOverride.GetInstanceId() })).ToArray();
    private static bool SameIds(Node root, ulong[] ids) => ids.SequenceEqual(Ids(root));
    private static bool PhysicsFree(Node root) => !Descendants(root).Any(node => node is CollisionObject3D or CollisionShape3D or CollisionPolygon3D or NavigationRegion3D or NavigationLink3D);
    private static bool ValidLights(IEnumerable<OmniLight3D> lights) => lights.All(light => light.Position.IsFinite() &&
        float.IsFinite(light.LightEnergy) && light.LightEnergy is > 0 and <= 3 && float.IsFinite(light.OmniRange) && light.OmniRange is > 0 and <= 8 && !light.ShadowEnabled);
    private static bool Peripheral(Node root, float halfWidth, float halfDepth)
    {
        bool Outside(Vector3 point) => point.IsFinite() && (Math.Abs(point.X) > halfWidth || Math.Abs(point.Z) > halfDepth);
        if (!MeshVertices(root).All(Outside)) return false;
        var foliage = Foliage(root); var parent = RelativeTransform(foliage, root); var vertices = LocalVertices(foliage.Multimesh.Mesh).ToArray();
        for (int i = 0; i < foliage.Multimesh.InstanceCount; i++)
        {
            var transform = parent * foliage.Multimesh.GetInstanceTransform(i);
            if (!vertices.All(vertex => Outside(transform * vertex))) return false;
        }
        return true;
    }
    private static IEnumerable<Vector3> MeshVertices(Node root)
    {
        foreach (var mesh in Descendants(root).OfType<MeshInstance3D>())
        {
            var transform = RelativeTransform(mesh, root);
            foreach (var vertex in LocalVertices(mesh.Mesh)) yield return transform * vertex;
        }
    }
    private static IEnumerable<Vector3> LocalVertices(Mesh mesh)
    {
        for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            foreach (var vertex in mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array()) yield return vertex;
    }
    private static Transform3D RelativeTransform(Node3D node, Node root)
    {
        var transform = Transform3D.Identity;
        for (Node? current = node; current is not null && current != root; current = current.GetParent())
            if (current is Node3D spatial) transform = spatial.Transform * transform;
        return transform;
    }
    private static bool Rejects(Func<Node> create)
    {
        Node? value = null;
        try { value = create(); return false; }
        catch (ArgumentException) { return true; }
        finally { value?.Free(); }
    }
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(node => new[] { node }.Concat(Descendants(node)));
}
