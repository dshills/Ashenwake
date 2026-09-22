using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

/// <summary>Detached art diagnostics driven by actual live Breach views. Actor-order probes only
/// reorder the same records; containment uses a cosmetic override and never creates Core hazards.</summary>
public static class HollowBreachDepthChecks
{
    public static void Detached(CombatView combat, Action<string, bool> check, bool fullCycle = true)
    {
        var boss = combat.Actors.Where(actor => actor.DefinitionId == "boss.breach_heart").OrderBy(actor => actor.Id).FirstOrDefault();
        if (boss is not { Health: > 0 }) throw new ArgumentException("An observed living canonical Breach Heart is required.", nameof(combat));
        var channels = combat.Actors.Where(actor => actor.DefinitionId == "enemy.seal_channel").OrderBy(actor => actor.Id).Take(3).ToArray();
        int living = combat.Actors.Count(actor => actor.DefinitionId == "enemy.seal_channel" && actor.Health > 0);
        int mask = 0; for (int i = 0; i < channels.Length; i++) if (channels[i].Health > 0) mask |= 1 << i;
        var hazards = (combat.CampaignHazards ?? []).Where(hazard => hazard.SourceId == boss.Id && hazard.RemainingTicks > 0).ToArray();
        bool echo = hazards.Any(hazard => hazard.ContentId == "campaign.breach_echo");
        bool returning = hazards.Any(hazard => hazard.ContentId == "campaign.returning_echo");
        bool sweep = hazards.Any(hazard => hazard.ContentId == "campaign.seal_sweep");
        string context = $"phase{combat.BossPhase}_{(boss.Shielded ? "shielded" : "exposed")}_channels{mask}_{echo}_{returning}_{sweep}";
        void Check(string name, bool passed) => check("hollow_breach_depth_" + context + "_" + name, passed);
        Check("rejects_invalid_bounds", Rejects(() => BreachHeartVisual.Create(float.NaN, 10, combat)) &&
            Rejects(() => BreachHeartVisual.Create(12, float.PositiveInfinity, combat)) &&
            Rejects(() => BreachHeartVisual.Create(0, 10, combat)) && Rejects(() => BreachHeartVisual.Create(12, -1, combat)));
        var breach = BreachHeartVisual.Create(12, 10, combat);
        var other = BreachHeartVisual.Create(12, 10, combat);
        Mesh[] retainedMeshes = []; Material[] retainedMaterials = []; Texture2D? retainedTexture = null; OmniLight3D? retainedLight = null;
        try
        {
            var meshes = Descendants(breach).OfType<MeshInstance3D>().ToArray();
            retainedMeshes = meshes.Select(mesh => mesh.Mesh).DistinctBy(mesh => mesh.GetInstanceId()).ToArray();
            retainedMaterials = meshes.Select(mesh => mesh.MaterialOverride ?? mesh.Mesh.SurfaceGetMaterial(0)).DistinctBy(material => material.GetInstanceId()).ToArray();
            var samples = meshes.Select(mesh => (Node: mesh, Vertices: Vertices(mesh.Mesh).ToArray())).ToArray();
            var ids = Ids(breach);
            var ring = Surface(breach, "OrbitingSealInscriptions"); var heart = Surface(breach, "FacetedVoidHeart");
            var otherRing = Surface(other, "OrbitingSealInscriptions"); var otherHeart = Surface(other, "FacetedVoidHeart");
            var otherColor = otherRing.AlbedoColor; var otherEmission = Emissions(other);
            retainedTexture = ring.NormalTexture;
            var light = Descendants(breach).OfType<OmniLight3D>().Single(); retainedLight = light;
            Check("retains_rig_shard_and_mesh_budgets", breach.ArticulatedPartCount == 13 && breach.TransientCapacity == 16 &&
                breach.ActiveTransientCount == 0 && meshes.Length <= 100 && PhysicsFree(breach));
            Check("retains_actual_canonical_state", !breach.Defeated && breach.Phase == combat.BossPhase && breach.LivingChannels == living &&
                breach.Shielded == boss.Shielded && breach.EchoWarning == echo && breach.ReturningEchoWarning == returning && breach.SweepWarning == sweep);
            Check("mutable_textured_surfaces_are_room_owned", ring.NormalEnabled && retainedTexture is not null && ring.AlbedoTexture is not null &&
                ring.GetInstanceId() != otherRing.GetInstanceId() && heart.GetInstanceId() != otherHeart.GetInstanceId() &&
                retainedTexture.GetInstanceId() == otherRing.NormalTexture.GetInstanceId());
            int phase = Math.Clamp(combat.BossPhase, 1, 3);
            float liveEnergy = (.5f + phase * .18f) * (boss.Shielded ? .68f : 1) + Math.Clamp(living, 0, 3) * .05f;
            float liveRange = 3.8f + phase * .3f + (boss.Shielded ? 0 : .4f);
            Check("one_steady_shadowless_aperture_light_matches_state", light.Name == "BreachApertureLight" && light.Visible && !light.ShadowEnabled &&
                Math.Abs(light.LightEnergy - liveEnergy) < .00001f && Math.Abs(light.OmniRange - liveRange) < .00001f && RoomTransform(light, breach).Origin.Z < -10);
            CheckGeometry(breach, Check);
            // The same actor records in another enumeration order must retain the original
            // boss and each dead/live channel's physical seal slot.
            other.SetState(combat with { Actors = combat.Actors.Reverse().ToArray() });
            Check("actor_order_cannot_replace_canonical_boss_or_channel_slots", other.Phase == breach.Phase && other.Shielded == breach.Shielded &&
                other.Defeated == breach.Defeated && other.LivingChannels == living && other.EchoWarning == echo && other.ReturningEchoWarning == returning &&
                other.SweepWarning == sweep && SamePose(Joints(other), Joints(breach)) && SameValues(Emissions(other), Emissions(breach)));
            bool safe = true, finite = true;
            void Sample()
            {
                finite &= double.IsFinite(breach.MotionTime) && breach.MotionTime >= 0 && breach.MotionTime < Math.Tau / 1.35 &&
                    float.IsFinite(light.LightEnergy) && light.LightEnergy is >= .049f and <= 1.21f &&
                    float.IsFinite(light.OmniRange) && light.OmniRange is >= 2.49f and <= 5.11f &&
                    Emissions(breach).All(float.IsFinite) && breach.ActiveTransientCount <= 16;
                foreach (var sample in samples)
                {
                    var transform = RoomTransform(sample.Node, breach);
                    foreach (var vertex in sample.Vertices)
                    {
                        var point = transform * vertex;
                        safe &= point.IsFinite() && point.Z < -10;
                    }
                }
            }
            var initialPose = Joints(breach);
            for (int frame = 0; frame < (fullCycle ? 140 : 20); frame++)
            {
                breach.Animate(.1, false, false);
                if (frame % 4 == 0) Sample();
            }
            Check(fullCycle ? "wrapped_live_cycles_remain_finite_and_behind_wall" : "observed_live_pose_remains_finite_and_behind_wall",
                safe && finite && !SamePose(initialPose, Joints(breach)) && breach.ActiveTransientCount == 0 && light.LightEnergy == liveEnergy);
            var pose = Pose(breach); double time = breach.MotionTime; var emission = Emissions(breach);
            breach.Animate(100, true, false);
            Check("pause_freezes_pose_emission_and_clock", breach.MotionTime == time && SamePose(pose, Pose(breach)) && SameValues(emission, Emissions(breach)));
            foreach (double delta in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d }) breach.Animate(delta, false, false);
            Check("invalid_delta_cannot_poison_pose_or_emission", breach.MotionTime == time && SamePose(pose, Pose(breach)) && SameValues(emission, Emissions(breach)));
            breach.Animate(0, true, true); pose = Pose(breach);
            Check("paused_reduced_effects_restores_neutral_motion", breach.MotionTime == 0 && breach.ActiveTransientCount == 0 && light.LightEnergy == liveEnergy);
            CheckSignals(breach, combat.BossPhase, boss.Shielded, mask, echo, returning, sweep, Check);
            breach.Animate(20, false, true);
            Check("reduced_effects_keeps_every_cue_static", SamePose(pose, Pose(breach)) && breach.MotionTime == 0);
            breach.Animate(20, true, false);
            Check("restoring_effects_does_not_advance_paused_pose", SamePose(pose, Pose(breach)) && breach.MotionTime == 0);
            breach.Animate(.1, false, false);
            var livePose = Joints(breach); var liveEmissions = Emissions(breach);
            float beforeLight = light.LightEnergy, beforeRange = light.OmniRange;
            Color beforeRingColor = ring.AlbedoColor, beforeLightColor = light.LightColor;
            breach.SetState(combat, defeated: true);
            Check("containment_begins_without_pose_snap_or_flash", breach.IsTransitioning && breach.VictoryProgress == 0 &&
                SamePose(livePose, Joints(breach)) && Emissions(breach).Zip(liveEmissions).All(pair => pair.First <= pair.Second) &&
                light.LightEnergy <= beforeLight && light.OmniRange <= beforeRange && ring.AlbedoColor == beforeRingColor && light.LightColor == beforeLightColor &&
                !breach.EchoWarning && !breach.ReturningEchoWarning && !breach.SweepWarning);
            var previousEmission = Emissions(breach); float previousLight = light.LightEnergy, previousRange = light.OmniRange;
            bool fades = true, shardsSeen = false;
            for (int frame = 0; frame < 36; frame++)
            {
                breach.Animate(.1, false, false); Sample();
                var current = Emissions(breach);
                fades &= current.Zip(previousEmission).All(pair => pair.First <= pair.Second) && light.LightEnergy <= previousLight && light.OmniRange <= previousRange;
                previousEmission = current; previousLight = light.LightEnergy; previousRange = light.OmniRange;
                shardsSeen |= breach.ActiveTransientCount > 0;
            }
            Check("containment_fades_once_with_bounded_shards_and_geometry", safe && finite && fades && shardsSeen && !breach.IsTransitioning &&
                breach.VictoryProgress == 1 && breach.ActiveTransientCount == 0 && Math.Abs(light.LightEnergy - .05f) < .00001f);
            Check("final_containment_preserves_visible_unclosed_fracture", breach.GetNode<Node3D>("UnclosedBreach").Scale.IsEqualApprox(new(.22f, .59f, .5f)) &&
                breach.GetNode<MeshInstance3D>("UnclosedBreach/UnhealedCentralFracture").Visible && Math.Abs(ring.EmissionEnergyMultiplier - .1f) < .00001f);
            pose = Pose(breach); breach.SetState(combat, defeated: true); breach.Animate(100, false, false);
            Check("settled_containment_cannot_replay_on_refresh", SamePose(pose, Pose(breach)) && !breach.IsTransitioning && breach.ActiveTransientCount == 0);
            breach.SetState(combat); breach.SetState(combat, defeated: true); breach.Animate(.1, false, false);
            breach.Animate(0, true, true); pose = Pose(breach);
            breach.Animate(.1, true, false); breach.Animate(.1, false, false);
            Check("paused_reduced_effects_settles_without_replay", !breach.IsTransitioning && breach.VictoryProgress == 1 &&
                breach.ActiveTransientCount == 0 && SamePose(pose, Pose(breach)));
            Check("all_states_reuse_resources_and_preserve_another_room", ids.SequenceEqual(Ids(breach)) && otherRing.AlbedoColor == otherColor && SameValues(otherEmission, Emissions(other)));
        }
        finally { breach.Free(); other.Free(); }
        Check("free_releases_all_rig_owned_resources", retainedMeshes.Length > 0 && retainedMaterials.Length > 0 &&
            retainedMeshes.All(mesh => !GodotObject.IsInstanceValid(mesh)) && retainedMaterials.All(material => !GodotObject.IsInstanceValid(material)) &&
            retainedLight is not null && !GodotObject.IsInstanceValid(retainedLight));
        Check("free_preserves_factory_surface_textures", retainedTexture is not null && GodotObject.IsInstanceValid(retainedTexture));
    }

    private static void CheckSignals(BreachHeartVisual breach, int actualPhase, bool shielded, int mask, bool echo, bool returning, bool sweep, Action<string, bool> check)
    {
        int phase = Math.Clamp(actualPhase, 1, 3);
        Color phaseColor = new(phase == 3 ? "c0a8c5" : phase == 2 ? "91b8c5" : "a7a2ca");
        bool exact = breach.Phase == actualPhase && breach.Shielded == shielded && Surface(breach, "OrbitingSealInscriptions").AlbedoColor == phaseColor &&
            breach.GetNode<Node3D>("AnnouncedBreachEcho").Visible == echo && breach.GetNode<Node3D>("AnnouncedReturningEcho").Visible == returning &&
            breach.GetNode<Node3D>("AnnouncedSealSweep").Visible == sweep;
        for (int i = 0; i < 3; i++)
        {
            bool alive = (mask & (1 << i)) != 0;
            var channel = breach.GetNode<Node3D>("BreachChannelSeal" + i);
            var material = (StandardMaterial3D)channel.GetNode<MeshInstance3D>("ChannelLivingMark").MaterialOverride;
            exact &= channel.Position.IsEqualApprox(new((i - 1) * 2.3f, 1.05f, .68f)) && Math.Abs(channel.RotationDegrees.Z - (alive ? 0 : 30)) < .001f &&
                material.AlbedoColor == new Color(alive ? "b0c1c9" : "3b3b4e") && Math.Abs(material.EmissionEnergyMultiplier - (alive ? .38f : 0)) < .00001f;
            float side = i == 1 ? -1 : 1;
            Vector3 rotation = new(side * (phase - 1) * (i + 1) * 9, side * (phase - 1) * 11, i * 30 + side * (phase - 1) * 17);
            var expected = new Transform3D(Basis.FromEuler(rotation * (Mathf.Pi / 180)).ScaledLocal(Vector3.One * (1 + (phase - 1) * .025f)), new(0, 3.63f, .36f + i * .11f));
            exact &= breach.GetNode<Node3D>("BreachOrbitalSeal" + i).Transform.IsEqualApprox(expected);
        }
        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.Tau / 6, radius = shielded ? 1.25f : 1.8f;
            exact &= breach.GetNode<Node3D>("ContainmentShutter" + i).Position.IsEqualApprox(new(Mathf.Sin(angle) * radius, 3.63f + Mathf.Cos(angle) * radius, 1.05f));
        }
        check("reduced_effects_keeps_exact_phase_channel_and_warning_signals", exact);
    }

    private static void CheckGeometry(BreachHeartVisual breach, Action<string, bool> check)
    {
        foreach (string name in new[] { "SealFace", "ChannelStone", "UnhealedCentralFracture" })
        {
            var mesh = Descendants(breach).OfType<MeshInstance3D>().First(node => node.Name == name).Mesh;
            var arrays = mesh.SurfaceGetArrays(0);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            bool valid = vertices.Length > 24 && indices.Length > 36 && normals.Length == vertices.Length && uv.Length == vertices.Length &&
                vertices.All(vertex => vertex.IsFinite()) && uv.All(point => point.IsFinite()) &&
                normals.All(normal => normal.IsFinite() && normal.LengthSquared() is > .99f and < 1.01f);
            for (int i = 0; i < indices.Length; i += 3)
            {
                var cross = (vertices[indices[i + 1]] - vertices[indices[i]]).Cross(vertices[indices[i + 2]] - vertices[indices[i]]);
                valid &= cross.LengthSquared() > .00000000001f && cross.Dot(normals[indices[i]]) < 0;
            }
            check("shaped_" + name + "_has_finite_outward_faces_and_uv", valid);
            if (name == "UnhealedCentralFracture") check("fracture_follows_aperture_lens_curvature", vertices.All(vertex =>
            {
                float offset = vertex.Z - MathF.Sqrt(Math.Max(.04f, .72f * .72f - vertex.X * vertex.X - vertex.Y * vertex.Y));
                return offset is > -.017f and < .041f;
            }));
        }
        check("all_three_orbital_rings_have_layered_rims", Descendants(breach).OfType<MeshInstance3D>().Count(mesh => mesh.Name == "OrbitalInnerRim") == 3);
    }

    private static StandardMaterial3D Surface(Node root, string name) => (StandardMaterial3D)Descendants(root).OfType<MeshInstance3D>().First(mesh => mesh.Name == name).MaterialOverride;
    private static float[] Emissions(BreachHeartVisual root) => new[] { Surface(root, "OrbitingSealInscriptions").EmissionEnergyMultiplier, Surface(root, "FacetedVoidHeart").EmissionEnergyMultiplier }
        .Concat(Enumerable.Range(0, 3).Select(i => ((StandardMaterial3D)root.GetNode<MeshInstance3D>("BreachChannelSeal" + i + "/ChannelLivingMark").MaterialOverride).EmissionEnergyMultiplier)).ToArray();
    private static ulong[] Ids(Node root) => Descendants(root).Select(node => node.GetInstanceId()).Concat(Descendants(root).OfType<MeshInstance3D>().Select(mesh => mesh.Mesh.GetInstanceId())).ToArray();
    private static Transform3D[] Pose(Node root) => Descendants(root).OfType<Node3D>().Select(node => node.Transform).ToArray();
    private static Transform3D[] Joints(BreachHeartVisual root) => new[] { root.GetNode<Node3D>("UnclosedBreach").Transform }
        .Concat(Enumerable.Range(0, 3).Select(i => root.GetNode<Node3D>("BreachOrbitalSeal" + i).Transform))
        .Concat(Enumerable.Range(0, 6).Select(i => root.GetNode<Node3D>("ContainmentShutter" + i).Transform))
        .Concat(Enumerable.Range(0, 3).Select(i => root.GetNode<Node3D>("BreachChannelSeal" + i).Transform)).ToArray();
    private static bool SamePose(Transform3D[] a, Transform3D[] b) => a.Length == b.Length && a.Zip(b).All(pair => pair.First.IsEqualApprox(pair.Second));
    private static bool SameValues(float[] a, float[] b) => a.Length == b.Length && a.Zip(b).All(pair => Math.Abs(pair.First - pair.Second) < .000001f);
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
    private static bool Rejects(Func<BreachHeartVisual> create)
    {
        BreachHeartVisual? value = null;
        try { value = create(); return false; }
        catch (ArgumentException) { return true; }
        finally { value?.Free(); }
    }
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(node => new[] { node }.Concat(Descendants(node)));
}
