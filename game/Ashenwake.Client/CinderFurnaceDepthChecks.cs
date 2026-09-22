using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

/// <summary>Detached presentation checks driven by an observed living furnace view. Call for
/// each distinct observed guard/vent state; shutdown is a cosmetic override, never a Core command.</summary>
public static class CinderFurnaceDepthChecks
{
    public static void Detached(CombatView combat, Action<string, bool> check, bool fullRotation = true)
    {
        var boss = combat.Actors.FirstOrDefault(actor => actor.DefinitionId == "boss.furnace_spindle" && actor.Health > 0)
            ?? throw new ArgumentException("A real living Furnace Spindle view is required.", nameof(combat));
        var warning = (combat.CampaignHazards ?? []).FirstOrDefault(hazard => hazard.SourceId == boss.Id &&
            hazard.ContentId == "campaign.furnace_vent" && hazard.RemainingTicks > 0);
        string orientation = warning is null ? "None" : Math.Abs((long)warning.End.X - warning.Position.X) >= Math.Abs((long)warning.End.Z - warning.Position.Z) ? "Horizontal" : "Vertical";
        string context = (boss.Guarded ? "guarded_" : "exposed_") + orientation.ToLowerInvariant();
        void Check(string name, bool passed) => check("cinder_furnace_depth_" + context + "_" + name, passed);
        Check("rejects_nonfinite_and_nonpositive_bounds", Rejects(() => FurnaceSpindleVisual.Create(float.NaN, 10, combat)) &&
            Rejects(() => FurnaceSpindleVisual.Create(12, float.PositiveInfinity, combat)) && Rejects(() => FurnaceSpindleVisual.Create(0, 10, combat)) &&
            Rejects(() => FurnaceSpindleVisual.Create(12, -1, combat)));
        var furnace = FurnaceSpindleVisual.Create(12, 10, combat);
        var other = FurnaceSpindleVisual.Create(12, 10, combat);
        Mesh[] retainedMeshes = []; Material[] retainedMaterials = []; Texture2D? retainedTexture = null; OmniLight3D? retainedLight = null;
        try
        {
            var meshes = Descendants(furnace).OfType<MeshInstance3D>().ToArray();
            retainedMeshes = meshes.Select(mesh => mesh.Mesh).DistinctBy(mesh => mesh.GetInstanceId()).ToArray();
            retainedMaterials = meshes.Select(mesh => mesh.MaterialOverride ?? mesh.Mesh.SurfaceGetMaterial(0)).DistinctBy(material => material.GetInstanceId()).ToArray();
            var samples = meshes.Select(mesh => (Node: mesh, Vertices: Vertices(mesh.Mesh).ToArray())).ToArray();
            var ids = Ids(furnace);
            var core = Core(furnace); var otherCore = Core(other);
            var otherColor = otherCore.AlbedoColor; float otherEmission = otherCore.EmissionEnergyMultiplier;
            retainedTexture = core.NormalTexture;
            var light = Descendants(furnace).OfType<OmniLight3D>().Single(); retainedLight = light;
            Check("keeps_bounded_rig_and_draw_resources", furnace.ArticulatedPartCount == 11 && furnace.TransientCapacity == 12 && meshes.Length <= 100 &&
                furnace.ActiveTransientCount == 0 && PhysicsFree(furnace));
            Check("preserves_observed_core_state", furnace.Guarded == boss.Guarded && furnace.VentOrientation == orientation && !furnace.Defeated);
            Check("metal_grain_and_mutable_materials_are_room_owned", core.NormalEnabled && retainedTexture is not null && core.AlbedoTexture is not null &&
                core.GetInstanceId() != otherCore.GetInstanceId() && retainedTexture.GetInstanceId() == otherCore.NormalTexture.GetInstanceId());
            Check("one_steady_shadowless_core_light_matches_defense", light.Name == "CoreGlowLight" && !light.ShadowEnabled && light.Visible &&
                Math.Abs(light.LightEnergy - (boss.Guarded ? .9f : 1.65f)) < .00001f && Math.Abs(light.OmniRange - (boss.Guarded ? 4.2f : 5.2f)) < .00001f &&
                RoomTransform(light, furnace).Origin.Z < -10);
            CheckArmor(furnace, Check);
            bool safe = true, finite = true;
            void Sample()
            {
                finite &= double.IsFinite(furnace.MotionTime) && furnace.MotionTime is >= 0 and < 40 &&
                    float.IsFinite(light.LightEnergy) && light.LightEnergy is >= .07f and <= 1.66f &&
                    float.IsFinite(light.OmniRange) && light.OmniRange is >= 2.79f and <= 5.21f &&
                    furnace.ActiveTransientCount <= 12 && float.IsFinite(core.EmissionEnergyMultiplier);
                foreach (var sample in samples)
                {
                    var transform = RoomTransform(sample.Node, furnace);
                    foreach (var vertex in sample.Vertices)
                    {
                        var point = transform * vertex;
                        safe &= point.IsFinite() && point.Z < -10;
                    }
                }
            }
            var startingPose = Pose(furnace); float startingLight = light.LightEnergy;
            // The first observed state crosses a complete axle rotation and its wrapped
            // clock. Later views sample their changed armor/vent pose without repeating it.
            // All pooled ember vertices are included, even while their instances are hidden.
            for (int frame = 0; frame < (fullRotation ? 450 : 20); frame++)
            {
                furnace.Animate(.1, false, false);
                if (frame % 5 == 0) Sample();
            }
            Check(fullRotation ? "full_rotation_wrap_is_finite_and_behind_wall" : "observed_pose_is_finite_and_behind_wall", finite && safe && !SamePose(startingPose, Pose(furnace)) &&
                furnace.ActiveTransientCount == 0 && light.LightEnergy == startingLight);
            var pose = Pose(furnace); double time = furnace.MotionTime; float emission = core.EmissionEnergyMultiplier;
            furnace.Animate(100, true, false);
            Check("pause_freezes_geometry_heat_and_clock", furnace.MotionTime == time && SamePose(pose, Pose(furnace)) && core.EmissionEnergyMultiplier == emission);
            foreach (double delta in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d }) furnace.Animate(delta, false, false);
            Check("invalid_delta_cannot_poison_geometry_or_heat", furnace.MotionTime == time && SamePose(pose, Pose(furnace)) && core.EmissionEnergyMultiplier == emission);
            furnace.Animate(0, true, true); pose = Pose(furnace);
            Check("paused_reduced_effects_resets_only_cosmetic_motion", furnace.MotionTime == 0 && furnace.Guarded == boss.Guarded &&
                furnace.VentOrientation == orientation && light.LightEnergy == startingLight && furnace.ActiveTransientCount == 0);
            CheckVents(furnace, orientation, Check);
            furnace.Animate(30, false, true);
            Check("reduced_effects_keeps_static_gameplay_cues", SamePose(pose, Pose(furnace)) && furnace.MotionTime == 0 && light.LightEnergy == startingLight);
            furnace.Animate(20, true, false);
            Check("reenabling_effects_while_paused_keeps_neutral_pose", SamePose(pose, Pose(furnace)) && furnace.MotionTime == 0);
            furnace.Animate(.1, false, false);
            float liveEnergy = light.LightEnergy, liveRange = light.OmniRange, liveEmission = core.EmissionEnergyMultiplier;
            furnace.SetState(combat, defeated: true);
            Check("presentation_shutdown_begins_once", furnace.IsTransitioning && furnace.VictoryProgress == 0 && !furnace.VentWarning);
            Check("shutdown_starts_from_current_heat_without_a_flash", light.LightEnergy <= liveEnergy && light.OmniRange <= liveRange && core.EmissionEnergyMultiplier <= liveEmission);
            float previousEnergy = light.LightEnergy, previousRange = light.OmniRange, previousEmission = core.EmissionEnergyMultiplier;
            bool cooling = true, embersSeen = false;
            for (int frame = 0; frame < 34; frame++)
            {
                furnace.Animate(.1, false, false); Sample();
                cooling &= light.LightEnergy <= previousEnergy && light.OmniRange <= previousRange && core.EmissionEnergyMultiplier <= previousEmission;
                previousEnergy = light.LightEnergy; previousRange = light.OmniRange; previousEmission = core.EmissionEnergyMultiplier;
                embersSeen |= furnace.ActiveTransientCount > 0;
            }
            Check("shutdown_cools_once_with_bounded_embers_and_geometry", cooling && embersSeen && safe && finite &&
                !furnace.IsTransitioning && furnace.VictoryProgress == 1 && furnace.ActiveTransientCount == 0 && Math.Abs(light.LightEnergy - .08f) < .00001f);
            pose = Pose(furnace); furnace.SetState(combat, defeated: true); furnace.Animate(10, false, false);
            Check("settled_shutdown_never_replays_on_refresh", SamePose(pose, Pose(furnace)) && furnace.ActiveTransientCount == 0 && !furnace.IsTransitioning);
            furnace.SetState(combat); furnace.SetState(combat, defeated: true); furnace.Animate(.1, false, false);
            furnace.Animate(0, true, true); pose = Pose(furnace);
            furnace.Animate(.1, true, false); furnace.Animate(.1, false, false);
            Check("paused_reduced_effects_consumes_shutdown_without_replay", furnace.VictoryProgress == 1 && !furnace.IsTransitioning &&
                furnace.ActiveTransientCount == 0 && SamePose(pose, Pose(furnace)));
            Check("all_states_reuse_resources_without_affecting_another_room", ids.SequenceEqual(Ids(furnace)) &&
                otherCore.AlbedoColor == otherColor && otherCore.EmissionEnergyMultiplier == otherEmission);
        }
        finally { furnace.Free(); other.Free(); }
        Check("free_releases_all_room_owned_resources", retainedMeshes.Length > 0 && retainedMaterials.Length > 0 &&
            retainedMeshes.All(mesh => !GodotObject.IsInstanceValid(mesh)) && retainedMaterials.All(material => !GodotObject.IsInstanceValid(material)) &&
            retainedLight is not null && !GodotObject.IsInstanceValid(retainedLight));
        Check("free_preserves_shared_grain_textures", retainedTexture is not null && GodotObject.IsInstanceValid(retainedTexture));
    }

    private static void CheckVents(FurnaceSpindleVisual furnace, string orientation, Action<string, bool> check)
    {
        bool cues = true;
        for (int i = 0; i < 4; i++)
        {
            var vent = furnace.GetNode<Node3D>("VentLouvers" + i);
            var material = (StandardMaterial3D)vent.GetChild<MeshInstance3D>(0).MaterialOverride;
            bool active = i < 2 ? orientation == "Horizontal" : orientation == "Vertical";
            cues &= Math.Abs(vent.RotationDegrees.X - (active ? -38 : 0)) < .001f &&
                Math.Abs(material.EmissionEnergyMultiplier - (active ? 1.25f : .13f)) < .00001f;
        }
        check("reduced_effects_preserves_exact_warning_louvers", cues);
    }

    private static void CheckArmor(FurnaceSpindleVisual furnace, Action<string, bool> check)
    {
        var shell = Descendants(furnace).OfType<MeshInstance3D>().First(mesh => mesh.Name == "ChamferedArmorShell");
        var arrays = shell.Mesh.SurfaceGetArrays(0);
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        bool valid = indices.Length == 144 && vertices.Length > 24 && normals.Length == vertices.Length && uv.Length == vertices.Length &&
            vertices.All(vertex => vertex.IsFinite()) && normals.All(normal => normal.IsFinite() && normal.LengthSquared() is > .99f and < 1.01f) && uv.All(point => point.IsFinite());
        for (int i = 0; i < indices.Length; i += 3)
        {
            var cross = (vertices[indices[i + 1]] - vertices[indices[i]]).Cross(vertices[indices[i + 2]] - vertices[indices[i]]);
            valid &= cross.LengthSquared() > .00000001f && cross.Dot(normals[indices[i]]) < 0;
        }
        check("shaped_armor_has_indexed_outward_chamfers_and_uv", valid);
    }

    private static StandardMaterial3D Core(Node root) => (StandardMaterial3D)Descendants(root).OfType<MeshInstance3D>().Single(mesh => mesh.Name == "ExposedDivineCore").MaterialOverride;
    private static ulong[] Ids(Node root) => Descendants(root).Select(node => node.GetInstanceId()).Concat(Descendants(root).OfType<MeshInstance3D>().Select(mesh => mesh.Mesh.GetInstanceId())).ToArray();
    private static Transform3D[] Pose(Node root) => Descendants(root).OfType<Node3D>().Select(node => node.Transform).ToArray();
    private static bool SamePose(Transform3D[] a, Transform3D[] b) => a.Length == b.Length && a.Zip(b).All(pair => pair.First.IsEqualApprox(pair.Second));
    private static bool PhysicsFree(Node root) => !Descendants(root).Any(node => node is CollisionObject3D or CollisionShape3D or CollisionPolygon3D or NavigationRegion3D or NavigationLink3D);
    private static IEnumerable<Vector3> Vertices(Mesh mesh)
    {
        for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            foreach (var vertex in mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array()) yield return vertex;
    }
    private static Transform3D RoomTransform(Node3D node, Node3D root)
    {
        var transform = Transform3D.Identity;
        for (Node? current = node; current is not null && current != root; current = current.GetParent())
            if (current is Node3D spatial) transform = spatial.Transform * transform;
        return root.Transform * transform;
    }
    private static bool Rejects(Func<FurnaceSpindleVisual> create)
    {
        FurnaceSpindleVisual? value = null;
        try { value = create(); return false; }
        catch (ArgumentException) { return true; }
        finally { value?.Free(); }
    }
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(node => new[] { node }.Concat(Descendants(node)));
}
