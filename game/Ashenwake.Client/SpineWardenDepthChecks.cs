using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

/// <summary>Detached art checks fed actual Warden views. Release uses the existing cosmetic
/// defeat override; these checks never mint a hazard or advance a campaign.</summary>
public static class SpineWardenDepthChecks
{
    public static void Detached(CombatView combat, Action<string, bool> check, bool fullCycle = true)
    {
        var boss = combat.Actors.FirstOrDefault(actor => actor.DefinitionId == "boss.covenant_warden" && actor.Health > 0)
            ?? throw new ArgumentException("An observed living Covenant Warden view is required.", nameof(combat));
        string lane = "None"; bool oath = false;
        foreach (var hazard in combat.CampaignHazards ?? [])
        {
            if (hazard.SourceId != boss.Id || hazard.RemainingTicks <= 0) continue;
            if (hazard.ContentId == "campaign.oath_mark") oath = true;
            if (hazard.ContentId != "campaign.covenant_fault") continue;
            long center = (long)hazard.Position.Z + hazard.End.Z;
            lane = center < 0 ? "North" : center > 0 ? "South" : "None";
        }
        string context = (boss.Guarded ? "guarded_" : "exposed_") + lane.ToLowerInvariant() + (oath ? "_oath" : "_quiet");
        void Check(string name, bool passed) => check("spine_warden_depth_" + context + "_" + name, passed);
        Check("rejects_invalid_bounds", Rejects(() => CovenantWardenVisual.Create(float.NaN, 10, combat)) &&
            Rejects(() => CovenantWardenVisual.Create(12, float.PositiveInfinity, combat)) &&
            Rejects(() => CovenantWardenVisual.Create(0, 10, combat)) && Rejects(() => CovenantWardenVisual.Create(12, -1, combat)));
        var warden = CovenantWardenVisual.Create(12, 10, combat);
        var other = CovenantWardenVisual.Create(12, 10, combat);
        Mesh[] retainedMeshes = []; Material[] retainedMaterials = []; Texture2D? retainedTexture = null; OmniLight3D? retainedLight = null;
        try
        {
            var meshes = Descendants(warden).OfType<MeshInstance3D>().ToArray();
            retainedMeshes = meshes.Select(mesh => mesh.Mesh).DistinctBy(mesh => mesh.GetInstanceId()).ToArray();
            retainedMaterials = meshes.Select(mesh => mesh.MaterialOverride ?? mesh.Mesh.SurfaceGetMaterial(0)).DistinctBy(material => material.GetInstanceId()).ToArray();
            var samples = meshes.Select(mesh => (Node: mesh, Vertices: Vertices(mesh.Mesh).ToArray())).ToArray();
            var ids = Ids(warden);
            var law = Material(warden, "CovenantInscriptions"); var seal = Material(warden, "OathSealRing");
            var otherLaw = Material(other, "CovenantInscriptions"); var otherSeal = Material(other, "OathSealRing");
            float otherLawEnergy = otherLaw.EmissionEnergyMultiplier, otherSealEnergy = otherSeal.EmissionEnergyMultiplier;
            Color otherColor = otherLaw.AlbedoColor;
            retainedTexture = law.NormalTexture;
            var light = Descendants(warden).OfType<OmniLight3D>().Single(); retainedLight = light;
            Check("retains_rig_and_resource_budgets", warden.ArticulatedPartCount == 14 && warden.TransientCapacity == 16 &&
                meshes.Length <= 100 && warden.ActiveTransientCount == 0 && PhysicsFree(warden));
            Check("retains_observed_guard_fault_and_oath", !warden.Defeated && warden.Guarded == boss.Guarded && warden.FaultLane == lane && warden.OathMarkWarning == oath);
            Check("mutable_law_and_seal_materials_are_room_owned", law.NormalEnabled && retainedTexture is not null && law.AlbedoTexture is not null &&
                law.GetInstanceId() != otherLaw.GetInstanceId() && seal.GetInstanceId() != otherSeal.GetInstanceId() && retainedTexture.GetInstanceId() == otherLaw.NormalTexture.GetInstanceId());
            Check("one_steady_shadowless_law_light_matches_guard", light.Name == "LawGlowLight" && !light.ShadowEnabled && light.Visible &&
                Math.Abs(light.LightEnergy - (boss.Guarded ? .8f : 1.2f)) < .00001f && Math.Abs(light.OmniRange - (boss.Guarded ? 4.2f : 5f)) < .00001f &&
                RoomTransform(light, warden).Origin.Z < -10);
            CheckCarvings(warden, Check);
            bool safe = true, finite = true;
            void Sample()
            {
                finite &= double.IsFinite(warden.MotionTime) && warden.MotionTime >= 0 && warden.MotionTime < Math.Tau / 1.5 &&
                    float.IsFinite(light.LightEnergy) && light.LightEnergy is >= .039f and <= 1.201f &&
                    float.IsFinite(light.OmniRange) && light.OmniRange is >= 2.79f and <= 5.01f &&
                    float.IsFinite(law.EmissionEnergyMultiplier) && float.IsFinite(seal.EmissionEnergyMultiplier) && warden.ActiveTransientCount <= 16;
                foreach (var sample in samples)
                {
                    var transform = RoomTransform(sample.Node, warden);
                    foreach (var vertex in sample.Vertices)
                    {
                        var point = transform * vertex;
                        safe &= point.IsFinite() && point.Z < -10;
                    }
                }
            }
            float startingEnergy = light.LightEnergy, startingLaw = law.EmissionEnergyMultiplier;
            bool pulseObserved = false;
            // Only the first real state needs repeated phase wrapping; later states still
            // sample their exact warning poses and the entire release below.
            for (int frame = 0; frame < (fullCycle ? 140 : 20); frame++)
            {
                warden.Animate(.1, false, false);
                pulseObserved |= Math.Abs(law.EmissionEnergyMultiplier - startingLaw) > .001f;
                if (frame % 4 == 0) Sample();
            }
            Check(fullCycle ? "wrapped_idle_cycles_remain_finite_and_behind_wall" : "observed_warning_pose_remains_finite_and_behind_wall",
                safe && finite && pulseObserved && light.LightEnergy == startingEnergy && warden.ActiveTransientCount == 0);
            var pose = Pose(warden); double time = warden.MotionTime;
            float lawEnergy = law.EmissionEnergyMultiplier, sealEnergy = seal.EmissionEnergyMultiplier;
            warden.Animate(100, true, false);
            Check("pause_freezes_pose_emission_and_clock", warden.MotionTime == time && SamePose(pose, Pose(warden)) &&
                law.EmissionEnergyMultiplier == lawEnergy && seal.EmissionEnergyMultiplier == sealEnergy);
            foreach (double delta in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d }) warden.Animate(delta, false, false);
            Check("invalid_delta_cannot_poison_pose_or_emission", warden.MotionTime == time && SamePose(pose, Pose(warden)) &&
                law.EmissionEnergyMultiplier == lawEnergy && seal.EmissionEnergyMultiplier == sealEnergy);
            warden.Animate(0, true, true); pose = Pose(warden);
            Check("paused_reduced_effects_resets_motion_and_preserves_signals", warden.MotionTime == 0 && warden.Guarded == boss.Guarded &&
                warden.FaultLane == lane && warden.OathMarkWarning == oath && warden.ActiveTransientCount == 0 && light.LightEnergy == startingEnergy);
            CheckWarnings(warden, boss.Guarded, lane, oath, Check);
            warden.Animate(40, false, true);
            Check("reduced_effects_keeps_warnings_static", SamePose(pose, Pose(warden)) && warden.MotionTime == 0 && light.LightEnergy == startingEnergy);
            warden.Animate(10, true, false);
            Check("restoring_effects_does_not_advance_paused_pose", SamePose(pose, Pose(warden)) && warden.MotionTime == 0);
            warden.Animate(.1, false, false);
            float liveLaw = law.EmissionEnergyMultiplier, liveSeal = seal.EmissionEnergyMultiplier;
            float liveLight = light.LightEnergy, liveRange = light.OmniRange;
            var shields = Shields(warden);
            warden.SetState(combat, defeated: true);
            Check("release_starts_without_a_brightness_flash_or_shield_snap", warden.IsTransitioning && warden.VictoryProgress == 0 &&
                law.EmissionEnergyMultiplier <= liveLaw && seal.EmissionEnergyMultiplier <= liveSeal && light.LightEnergy <= liveLight &&
                light.OmniRange <= liveRange && SamePose(shields, Shields(warden)) && !warden.OathMarkWarning && warden.FaultLane == "None");
            float previousLaw = law.EmissionEnergyMultiplier, previousSeal = seal.EmissionEnergyMultiplier;
            float previousLight = light.LightEnergy, previousRange = light.OmniRange;
            bool fades = true, glyphsSeen = false;
            for (int frame = 0; frame < 34; frame++)
            {
                warden.Animate(.1, false, false); Sample();
                fades &= law.EmissionEnergyMultiplier <= previousLaw && seal.EmissionEnergyMultiplier <= previousSeal &&
                    light.LightEnergy <= previousLight && light.OmniRange <= previousRange;
                previousLaw = law.EmissionEnergyMultiplier; previousSeal = seal.EmissionEnergyMultiplier;
                previousLight = light.LightEnergy; previousRange = light.OmniRange;
                glyphsSeen |= warden.ActiveTransientCount > 0;
            }
            Check("release_fades_once_with_bounded_glyphs_and_geometry", fades && glyphsSeen && safe && finite && !warden.IsTransitioning &&
                warden.VictoryProgress == 1 && warden.ActiveTransientCount == 0 && Math.Abs(light.LightEnergy - .04f) < .00001f);
            pose = Pose(warden); warden.SetState(combat, defeated: true); warden.Animate(100, false, false);
            Check("settled_release_cannot_replay_on_refresh", SamePose(pose, Pose(warden)) && !warden.IsTransitioning && warden.ActiveTransientCount == 0);
            warden.SetState(combat); warden.SetState(combat, defeated: true); warden.Animate(.1, false, false);
            warden.Animate(0, true, true); pose = Pose(warden);
            warden.Animate(.1, true, false); warden.Animate(.1, false, false);
            Check("paused_reduced_effects_consumes_release_without_replay", !warden.IsTransitioning && warden.VictoryProgress == 1 &&
                warden.ActiveTransientCount == 0 && SamePose(pose, Pose(warden)));
            Check("all_states_reuse_resources_without_affecting_another_room", ids.SequenceEqual(Ids(warden)) &&
                otherLaw.AlbedoColor == otherColor && otherLaw.EmissionEnergyMultiplier == otherLawEnergy && otherSeal.EmissionEnergyMultiplier == otherSealEnergy);
        }
        finally { warden.Free(); other.Free(); }
        Check("free_releases_all_rig_resources", retainedMeshes.Length > 0 && retainedMaterials.Length > 0 &&
            retainedMeshes.All(mesh => !GodotObject.IsInstanceValid(mesh)) && retainedMaterials.All(material => !GodotObject.IsInstanceValid(material)) &&
            retainedLight is not null && !GodotObject.IsInstanceValid(retainedLight));
        Check("free_preserves_shared_surface_textures", retainedTexture is not null && GodotObject.IsInstanceValid(retainedTexture));
    }

    private static void CheckWarnings(CovenantWardenVisual warden, bool guarded, string lane, bool oath, Action<string, bool> check)
    {
        bool exact = warden.GetNode<Node3D>("AnnouncedOathMark").Visible == oath;
        for (int i = 0; i < 2; i++)
        {
            var pointer = warden.GetNode<Node3D>(i == 0 ? "NorthFaultPointer" : "SouthFaultPointer");
            var material = (StandardMaterial3D)pointer.GetChild<MeshInstance3D>(0).MaterialOverride;
            bool active = i == 0 ? lane == "North" : lane == "South";
            float direction = i == 0 ? 1 : -1;
            exact &= Math.Abs(pointer.Position.Y - (3.5f + direction * (2.2f + (active ? .12f : 0)))) < .00001f &&
                Math.Abs(material.EmissionEnergyMultiplier - (active ? 1.25f : .035f)) < .00001f;
        }
        exact &= Math.Abs(Material(warden, "OathSealRing").EmissionEnergyMultiplier - (oath ? 1.1f : guarded ? .5f : .18f)) < .00001f;
        for (int i = 0; i < 4; i++)
            exact &= Math.Abs(Math.Abs(warden.GetNode<Node3D>("WardenOathShield" + i).Position.X) - (guarded ? .59f : 1.86f)) < .00001f;
        check("reduced_effects_retains_exact_oath_fault_and_guard_geometry", exact);
    }

    private static void CheckCarvings(CovenantWardenVisual warden, Action<string, bool> check)
    {
        foreach (string name in new[] { "ChippedLawTablet", "LayeredBoneShield", "EngravedSealMedallion" })
        {
            var mesh = Descendants(warden).OfType<MeshInstance3D>().First(node => node.Name == name).Mesh;
            var arrays = mesh.SurfaceGetArrays(0);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            bool valid = indices.Length > 36 && vertices.Length > 24 && normals.Length == vertices.Length && uv.Length == vertices.Length &&
                vertices.All(vertex => vertex.IsFinite()) && uv.All(point => point.IsFinite()) &&
                normals.All(normal => normal.IsFinite() && normal.LengthSquared() is > .99f and < 1.01f) &&
                normals.Any(normal => Math.Abs(normal.Z) is > .01f and < .99f);
            for (int i = 0; i < indices.Length; i += 3)
            {
                var cross = (vertices[indices[i + 1]] - vertices[indices[i]]).Cross(vertices[indices[i + 2]] - vertices[indices[i]]);
                valid &= cross.LengthSquared() > .0000000001f && cross.Dot(normals[indices[i]]) < 0;
            }
            check("carved_" + name + "_has_outward_bevels_and_uv", valid);
            if (name == "ChippedLawTablet") check("tablet_fracture_is_authored_geometry", vertices.Where(vertex => vertex.Z < -.24f && vertex.X > .4f)
                .Select(vertex => MathF.Round(vertex.X, 3)).Distinct().Count() >= 5);
        }
    }

    private static StandardMaterial3D Material(Node root, string name) => (StandardMaterial3D)Descendants(root).OfType<MeshInstance3D>().First(mesh => mesh.Name == name).MaterialOverride;
    private static ulong[] Ids(Node root) => Descendants(root).Select(node => node.GetInstanceId()).Concat(Descendants(root).OfType<MeshInstance3D>().Select(mesh => mesh.Mesh.GetInstanceId())).ToArray();
    private static Transform3D[] Pose(Node root) => Descendants(root).OfType<Node3D>().Select(node => node.Transform).ToArray();
    private static Transform3D[] Shields(CovenantWardenVisual root) => Enumerable.Range(0, 4).Select(i => root.GetNode<Node3D>("WardenOathShield" + i).Transform).ToArray();
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
    private static bool Rejects(Func<CovenantWardenVisual> create)
    {
        CovenantWardenVisual? value = null;
        try { value = create(); return false; }
        catch (ArgumentException) { return true; }
        finally { value?.Free(); }
    }
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(node => new[] { node }.Concat(Descendants(node)));
}
