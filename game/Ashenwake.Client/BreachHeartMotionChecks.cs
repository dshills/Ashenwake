using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

/// <summary>Detached breach motion contracts; cosmetic fixtures never advance Core combat.</summary>
public static class BreachHeartMotionChecks
{
    public static void Run(Action<string, bool> check)
    {
        var shielded = View(true, 7); var exposed = View(false, 5);
        var breach = BreachHeartVisual.Create(12, 10, shielded);
        var repeated = BreachHeartVisual.Create(12, 10, shielded);
        var restored = BreachHeartVisual.Create(12, 10, exposed, true);
        try
        {
            var root = breach.Transform; var ids = Ids(breach);
            var rings = Enumerable.Range(0, 3).Select(i => breach.GetNode<Node3D>("BreachOrbitalSeal" + i)).ToArray();
            var shutters = Enumerable.Range(0, 6).Select(i => breach.GetNode<Node3D>("ContainmentShutter" + i)).ToArray();
            var channels = Enumerable.Range(0, 3).Select(i => breach.GetNode<Node3D>("BreachChannelSeal" + i)).ToArray();
            var heart = breach.GetNode<Node3D>("UnclosedBreach");
            var ringMaterial = Material(breach, "OrbitingSealInscriptions");
            var heartMaterial = Material(breach, "FacetedVoidHeart");
            var light = Descendants(breach).OfType<OmniLight3D>().Single();
            var samples = Descendants(breach).OfType<MeshInstance3D>().Select(mesh => (Node: mesh, Vertices: Vertices(mesh.Mesh).ToArray())).ToArray();
            bool safe = true;
            void Sample()
            {
                foreach (var sample in samples)
                {
                    var transform = RoomTransform(sample.Node, breach);
                    safe &= sample.Vertices.All(vertex => (transform * vertex).IsFinite() && (transform * vertex).Z < -10);
                }
            }
            breach.SetState(exposed); repeated.SetState(exposed);
            check("breach_channel_loss_and_exposure_signals_are_immediate", !breach.Shielded && breach.LivingChannels == 2 &&
                Math.Abs(shutters[0].Position.Y - 5.43f) < .00001f && Math.Abs(channels[1].RotationDegrees.Z - 30) < .00001f &&
                ((StandardMaterial3D)channels[1].GetNode<MeshInstance3D>("ChannelLivingMark").MaterialOverride).EmissionEnergyMultiplier == 0);
            breach.Animate(.1, false, false); repeated.Animate(.1, false, false); Sample();
            check("breach_only_lost_channel_recoils_and_heart_answers", channels[1].Position.Y < 1.04f && channels[0].Position.Y == 1.05f && channels[2].Position.Y == 1.05f && heart.Position.Z < .5f);
            check("breach_exposure_recoils_shutters_outward", shutters.All(node => node.Position.Z > 1.05f));
            for (int frame = 0; frame < 8; frame++)
            {
                repeated.SetState(exposed with { Actors = exposed.Actors.Reverse().ToArray() });
                breach.Animate(.05, false, false); repeated.Animate(.05, false, false); Sample();
            }
            check("breach_repeated_reordered_snapshots_keep_reaction_and_channel_identity", Same(Pose(breach), Pose(repeated)));
            var paused = Pose(breach); float energy = light.LightEnergy;
            breach.Animate(10, true, false);
            foreach (double delta in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d }) breach.Animate(delta, false, false);
            check("breach_pause_and_invalid_delta_freeze_reaction", Same(paused, Pose(breach)) && light.LightEnergy == energy);
            breach.SetState(shielded); breach.Animate(.1, false, false); Sample();
            check("breach_shield_return_braces_closed_shutters", breach.Shielded && Math.Abs(shutters[0].Position.Y - 4.88f) < .00001f && shutters.All(node => node.Position.Z < 1.05f));
            breach.Animate(0, true, true);
            check("breach_reduced_effects_consumes_secondary_reactions_while_paused", shutters.All(node => node.Position.Z == 1.05f) && channels.All(node => node.Position.Y == 1.05f));
            breach.Animate(.1, false, false);
            check("breach_reduced_reactions_never_replay", shutters.All(node => node.Position.Z == 1.05f) && channels.All(node => node.Position.Y == 1.05f));

            var warnings = exposed with
            {
                CampaignHazards = [
                new(1, "Circle", new(0, 0), new(0, 0), 200, 20, "campaign.breach_echo", 2),
                new(2, "Circle", new(0, 0), new(0, 0), 200, 20, "campaign.returning_echo", 2),
                new(3, "Line", new(-2000, 0), new(2000, 0), 200, 20, "campaign.seal_sweep", 2)]
            };
            breach.SetState(warnings);
            check("breach_all_authoritative_warnings_appear_immediately", Warnings(breach, true));
            breach.SetState(exposed with
            {
                CampaignHazards = [
                new(1, "Circle", new(0, 0), new(0, 0), 200, 0, "campaign.breach_echo", 2),
                new(2, "Circle", new(0, 0), new(0, 0), 200, 20, "campaign.returning_echo", 99)]
            });
            check("breach_expired_and_unrelated_warnings_clear_immediately", Warnings(breach, false));
            breach.SetState(warnings); breach.Animate(.1, false, false);
            // A basis round trip preserves the mesh pose while permitting Godot to normalize
            // authored Euler angles. Closure must still take the short 24-degree hinge arc.
            foreach (var shutter in shutters) shutter.RotationDegrees = shutter.Basis.GetEuler() * (180 / Mathf.Pi);
            var heartBefore = heart.Transform;
            var ringBefore = rings.Select(node => node.Transform).ToArray();
            var shutterBefore = shutters.Select(node => node.Transform).ToArray();
            var channelBefore = channels.Select(node => node.Transform).ToArray();
            breach.SetState(warnings, true);
            check("breach_containment_preserves_inflight_reaction_pose", breach.IsTransitioning && breach.VictoryProgress == 0 &&
                heart.Transform.IsEqualApprox(heartBefore) && Same(ringBefore, rings.Select(node => node.Transform).ToArray()) &&
                Same(shutterBefore, shutters.Select(node => node.Transform).ToArray()) && Same(channelBefore, channels.Select(node => node.Transform).ToArray()));
            check("breach_containment_clears_warnings_without_delay", Warnings(breach, false));
            float range = light.OmniRange, ringEnergy = ringMaterial.EmissionEnergyMultiplier, heartEnergy = heartMaterial.EmissionEnergyMultiplier;
            energy = light.LightEnergy;
            bool monotonic = true, shards = false;
            float shutterArc = 0;
            var previousShutterRotation = shutters[5].Basis.GetRotationQuaternion();
            for (int frame = 0; frame < 35; frame++)
            {
                breach.Animate(.1, false, false); Sample();
                var shutterRotation = shutters[5].Basis.GetRotationQuaternion();
                shutterArc += previousShutterRotation.AngleTo(shutterRotation);
                previousShutterRotation = shutterRotation;
                monotonic &= light.LightEnergy <= energy && light.OmniRange <= range && ringMaterial.EmissionEnergyMultiplier <= ringEnergy && heartMaterial.EmissionEnergyMultiplier <= heartEnergy;
                energy = light.LightEnergy; range = light.OmniRange; ringEnergy = ringMaterial.EmissionEnergyMultiplier; heartEnergy = heartMaterial.EmissionEnergyMultiplier;
                shards |= breach.ActiveTransientCount > 0;
                if (frame == 0)
                    check("breach_first_orbital_lock_precedes_other_rings_and_shutters", !rings[0].Transform.IsEqualApprox(ringBefore[0]) && rings[2].Transform.IsEqualApprox(ringBefore[2]) && Same(shutterBefore, shutters.Select(node => node.Transform).ToArray()));
                if (frame == 6)
                {
                    check("breach_shutters_close_in_sequence_before_heart_contracts", !shutters[0].Transform.IsEqualApprox(shutterBefore[0]) && shutters[5].Transform.IsEqualApprox(shutterBefore[5]) && heart.Transform.IsEqualApprox(heartBefore));
                    paused = Pose(breach); breach.Animate(10, true, false);
                    check("breach_pause_freezes_staged_containment", Same(paused, Pose(breach)));
                }
            }
            check("breach_normalized_shutter_closes_along_short_hinge_arc", shutterArc > Mathf.DegToRad(22) && shutterArc < Mathf.DegToRad(26));
            check("breach_containment_fades_once_and_consumes_shards", monotonic && shards && !breach.IsTransitioning && breach.VictoryProgress == 1 && breach.ActiveTransientCount == 0);
            check("breach_restored_defeat_matches_completed_containment", Same(Pose(breach), Pose(restored)) && !restored.IsTransitioning);
            check("breach_containment_preserves_surviving_wound", heart.Scale.IsEqualApprox(new(.22f, .59f, .5f)) && heart.GetNode<MeshInstance3D>("UnhealedCentralFracture").Visible);
            paused = Pose(breach); breach.SetState(warnings, true); breach.Animate(.1, false, false);
            check("breach_repeated_defeat_never_replays_containment", Same(paused, Pose(breach)));
            breach.SetState(warnings); breach.SetState(warnings, true); breach.Animate(.1, false, false);
            breach.Animate(0, true, true); breach.Animate(.1, true, false); breach.Animate(.1, false, false); Sample();
            check("breach_reduced_effects_settles_containment_without_replay", Same(Pose(breach), Pose(restored)) && !breach.IsTransitioning && breach.ActiveTransientCount == 0);
            check("breach_staged_motion_keeps_wall_clearance_and_light_budget", safe && !light.ShadowEnabled && light.LightEnergy is >= .049f and <= 1.21f && light.OmniRange is >= 2.49f and <= 5.11f);
            check("breach_staged_motion_reuses_resources_and_preserves_root", root.IsEqualApprox(breach.Transform) && ids.SequenceEqual(Ids(breach)) && breach.ArticulatedPartCount == 13 && breach.TransientCapacity == 16 &&
                !Descendants(breach).Any(node => node is CollisionObject3D or CollisionShape3D or NavigationRegion3D));
            check("breach_containment_preserves_other_room_materials", Material(repeated, "OrbitingSealInscriptions").GetInstanceId() != ringMaterial.GetInstanceId() && Material(repeated, "OrbitingSealInscriptions").EmissionEnergyMultiplier > .4f);
        }
        finally { breach.Free(); repeated.Free(); restored.Free(); }
    }

    private static CombatView View(bool shielded, int mask) => new(Tick: 0, Preset: "standard",
        Actors: new[] { new CombatActorView(2, new(0, 0), 100, 100, CombatFaction.Enemy, "Boss", 0, "Acquire", 0, [], "boss.breach_heart", Shielded: shielded) }
            .Concat(Enumerable.Range(0, 3).Select(i => new CombatActorView(6 + i, new(0, 0), (mask & (1 << i)) != 0 ? 100 : 0, 100, CombatFaction.Enemy, "Caster", 0, "Acquire", 0, [], "enemy.seal_channel"))).ToArray(),
        Projectiles: [], Areas: [], Loot: [], Inventory: [], Skills: [], Fragments: [], Mutations: [], Equipment: new Dictionary<string, long>(),
        Momentum: 0, MaxMomentum: 100, Barrier: 0, PotionCharges: 3, PotionCooldownTicks: 0, DodgeCooldownTicks: 0, Resonance: 0,
        PendingEffects: 0, PeakEffects: 0, RejectedEffects: 0, ContentVersion: "cosmetic-fixture", BossPhase: 3);
    private static bool Warnings(BreachHeartVisual breach, bool visible) => breach.EchoWarning == visible && breach.ReturningEchoWarning == visible && breach.SweepWarning == visible &&
        new[] { "AnnouncedBreachEcho", "AnnouncedReturningEcho", "AnnouncedSealSweep" }.All(name => breach.GetNode<Node3D>(name).Visible == visible);
    private static StandardMaterial3D Material(Node root, string name) => (StandardMaterial3D)Descendants(root).OfType<MeshInstance3D>().First(mesh => mesh.Name == name).MaterialOverride;
    private static ulong[] Ids(Node root) => Descendants(root).Select(node => node.GetInstanceId()).Concat(Descendants(root).OfType<MeshInstance3D>()
        .SelectMany(mesh => new[] { mesh.Mesh.GetInstanceId(), (mesh.MaterialOverride ?? mesh.Mesh.SurfaceGetMaterial(0)).GetInstanceId() })).ToArray();
    private static Transform3D[] Pose(Node root) => Descendants(root).OfType<Node3D>().Select(node => node.Transform).ToArray();
    private static bool Same(Transform3D[] left, Transform3D[] right) => left.Length == right.Length && left.Zip(right).All(pair => pair.First.IsEqualApprox(pair.Second));
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
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
}
