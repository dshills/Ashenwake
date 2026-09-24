using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

/// <summary>Detached presentation checks; fixtures never advance or modify Core combat.</summary>
public static class CovenantWardenMotionChecks
{
    public static void Run(Action<string, bool> check)
    {
        var guarded = View(true); var exposed = View(false);
        var warden = CovenantWardenVisual.Create(12, 10, guarded);
        var repeated = CovenantWardenVisual.Create(12, 10, guarded);
        var restored = CovenantWardenVisual.Create(12, 10, exposed, true);
        try
        {
            var ids = Ids(warden); var root = warden.Transform;
            var shields = Enumerable.Range(0, 4).Select(i => warden.GetNode<Node3D>("WardenOathShield" + i)).ToArray();
            var tablets = Enumerable.Range(0, 2).Select(i => warden.GetNode<Node3D>("CrackedCovenantTablet" + i)).ToArray();
            var seals = Enumerable.Range(0, 6).Select(i => warden.GetNode<Node3D>("BindingOathSeal" + i)).ToArray();
            var law = Material(warden, "CovenantInscriptions"); var seal = Material(warden, "OathSealRing");
            var light = Descendants(warden).OfType<OmniLight3D>().Single();
            var samples = Descendants(warden).OfType<MeshInstance3D>().Select(mesh => (Node: mesh, Vertices: Vertices(mesh.Mesh).ToArray())).ToArray();
            bool safe = true;
            void Sample()
            {
                foreach (var sample in samples)
                {
                    var transform = RoomTransform(sample.Node, warden);
                    safe &= sample.Vertices.All(vertex => (transform * vertex).IsFinite() && (transform * vertex).Z < -10);
                }
            }
            warden.SetState(exposed); repeated.SetState(exposed);
            check("warden_exposure_opens_all_shields_immediately", !warden.Guarded && shields.All(node => Math.Abs(Math.Abs(node.Position.X) - 1.86f) < .00001f));
            warden.Animate(.1, false, false); repeated.Animate(.1, false, false); Sample();
            check("warden_exposure_reacts_in_staggered_shield_order", shields[0].Position.Z > shields[1].Position.Z && Math.Abs(shields[3].Position.Z - .9f) < .00001f);
            for (int frame = 0; frame < 8; frame++)
            {
                repeated.SetState(exposed);
                warden.Animate(.05, false, false); repeated.Animate(.05, false, false); Sample();
            }
            check("warden_repeated_snapshots_preserve_shield_reaction", Same(Pose(warden), Pose(repeated)));
            var paused = Pose(warden); float emission = law.EmissionEnergyMultiplier;
            warden.Animate(10, true, false);
            foreach (double delta in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d }) warden.Animate(delta, false, false);
            check("warden_pause_and_invalid_delta_freeze_reaction", Same(paused, Pose(warden)) && emission == law.EmissionEnergyMultiplier);
            warden.SetState(guarded);
            check("warden_guard_closes_all_shields_immediately", warden.Guarded && shields.All(node => Math.Abs(Math.Abs(node.Position.X) - .59f) < .00001f));
            warden.Animate(.1, false, false); Sample();
            check("warden_guard_reaction_braces_shields_inward", shields[0].Position.Z < .88f);
            warden.Animate(0, true, true);
            check("warden_reduced_effects_consumes_shield_reaction_while_paused", shields.All(node => Math.Abs(node.Position.Z - .9f) < .00001f));
            warden.Animate(.1, false, false);
            check("warden_reduced_shield_reaction_never_replays", shields.All(node => Math.Abs(node.Position.Z - .9f) < .00001f));

            var north = guarded with
            {
                CampaignHazards = [new(1, "Line", new(-3000, -1000), new(3000, -1000), 200, 20, "campaign.covenant_fault", 2),
                new(2, "Circle", new(0, 0), new(0, 0), 200, 20, "campaign.oath_mark", 2)]
            };
            var south = guarded with { CampaignHazards = [new(3, "Line", new(-3000, 1000), new(3000, 1000), 200, 20, "campaign.covenant_fault", 2)] };
            warden.SetState(north);
            check("warden_north_fault_and_oath_warnings_are_immediate", Warnings(warden, "North", true));
            warden.SetState(south);
            check("warden_south_fault_immediately_replaces_north_and_oath", Warnings(warden, "South", false));
            var invalid = guarded with
            {
                CampaignHazards = [new(4, "Line", new(-3000, -1000), new(3000, -1000), 200, 0, "campaign.covenant_fault", 2),
                new(5, "Circle", new(0, 0), new(0, 0), 200, 20, "campaign.oath_mark", 99)]
            };
            warden.SetState(invalid);
            check("warden_expired_or_unrelated_hazards_cannot_warn", Warnings(warden, "None", false));

            warden.SetState(exposed); warden.Animate(.1, false, false);
            var shieldBefore = shields.Select(node => node.Transform).ToArray();
            var tabletBefore = tablets.Select(node => node.Transform).ToArray();
            var sealBefore = seals.Select(node => node.Transform).ToArray();
            warden.SetState(exposed, true);
            check("warden_release_preserves_current_pose_at_start", warden.IsTransitioning && warden.VictoryProgress == 0 &&
                Same(shieldBefore, shields.Select(node => node.Transform).ToArray()) && Same(tabletBefore, tablets.Select(node => node.Transform).ToArray()) && Same(sealBefore, seals.Select(node => node.Transform).ToArray()));
            float energy = light.LightEnergy, range = light.OmniRange, lawEnergy = law.EmissionEnergyMultiplier, sealEnergy = seal.EmissionEnergyMultiplier;
            bool monotonic = true, glyphs = false;
            for (int frame = 0; frame < 34; frame++)
            {
                warden.Animate(.1, false, false); Sample();
                monotonic &= light.LightEnergy <= energy && light.OmniRange <= range && law.EmissionEnergyMultiplier <= lawEnergy && seal.EmissionEnergyMultiplier <= sealEnergy;
                energy = light.LightEnergy; range = light.OmniRange; lawEnergy = law.EmissionEnergyMultiplier; sealEnergy = seal.EmissionEnergyMultiplier;
                glyphs |= warden.ActiveTransientCount > 0;
                if (frame == 2)
                {
                    check("warden_seals_break_in_order_before_tablets_or_shields_release", !seals[0].Transform.IsEqualApprox(sealBefore[0]) && seals[5].Transform.IsEqualApprox(sealBefore[5]) &&
                        Same(shieldBefore, shields.Select(node => node.Transform).ToArray()) && Same(tabletBefore, tablets.Select(node => node.Transform).ToArray()));
                    paused = Pose(warden); warden.Animate(10, true, false);
                    check("warden_pause_freezes_staged_release", Same(paused, Pose(warden)));
                }
                if (frame == 10)
                    check("warden_tablet_halves_release_in_order_after_seals", !tablets[0].Transform.IsEqualApprox(tabletBefore[0]) && tablets[1].Transform.IsEqualApprox(tabletBefore[1]));
            }
            check("warden_release_fades_monotonically_and_consumes_glyphs", monotonic && glyphs && !warden.IsTransitioning && warden.VictoryProgress == 1 && warden.ActiveTransientCount == 0);
            check("warden_restored_defeat_matches_completed_release", Same(Pose(warden), Pose(restored)) && !restored.IsTransitioning && restored.ActiveTransientCount == 0);
            paused = Pose(warden); warden.SetState(exposed, true); warden.Animate(.1, false, false);
            check("warden_repeated_defeat_never_replays_release", Same(paused, Pose(warden)));
            warden.SetState(north); warden.SetState(north, true);
            check("warden_defeat_clears_live_warnings_without_delay", Warnings(warden, "None", false));
            warden.Animate(.1, false, false); warden.Animate(0, true, true);
            warden.Animate(.1, true, false); warden.Animate(.1, false, false); Sample();
            check("warden_reduced_effects_settles_release_while_paused_without_replay", Same(Pose(warden), Pose(restored)) && !warden.IsTransitioning && warden.ActiveTransientCount == 0);
            check("warden_staged_motion_remains_behind_wall", safe && !light.ShadowEnabled && light.LightEnergy is >= .039f and <= 1.201f && light.OmniRange is >= 2.79f and <= 5.01f);
            check("warden_staged_motion_keeps_fixed_resources_and_root", root.IsEqualApprox(warden.Transform) && ids.SequenceEqual(Ids(warden)) &&
                warden.ArticulatedPartCount == 14 && warden.TransientCapacity == 16 && !Descendants(warden).Any(node => node is CollisionObject3D or CollisionShape3D or NavigationRegion3D));
            check("warden_release_preserves_other_room_materials", Material(repeated, "CovenantInscriptions").GetInstanceId() != law.GetInstanceId() &&
                Material(repeated, "CovenantInscriptions").EmissionEnergyMultiplier > .7f && Material(repeated, "OathSealRing").EmissionEnergyMultiplier == .18f);
        }
        finally { warden.Free(); repeated.Free(); restored.Free(); }
    }

    private static CombatView View(bool guarded) => new(Tick: 0, Preset: "standard",
        Actors: [new(2, new(0, 0), 100, 100, CombatFaction.Enemy, "Boss", 0, "Acquire", 0, [], "boss.covenant_warden", Guarded: guarded)],
        Projectiles: [], Areas: [], Loot: [], Inventory: [], Skills: [], Fragments: [], Mutations: [], Equipment: new Dictionary<string, long>(),
        Momentum: 0, MaxMomentum: 100, Barrier: 0, PotionCharges: 3, PotionCooldownTicks: 0, DodgeCooldownTicks: 0, Resonance: 0,
        PendingEffects: 0, PeakEffects: 0, RejectedEffects: 0, ContentVersion: "cosmetic-fixture");

    private static bool Warnings(CovenantWardenVisual warden, string lane, bool oath) => warden.FaultLane == lane && warden.OathMarkWarning == oath &&
        warden.GetNode<Node3D>("AnnouncedOathMark").Visible == oath && Enumerable.Range(0, 2).All(i =>
        {
            float direction = i == 0 ? 1 : -1;
            bool active = i == 0 ? lane == "North" : lane == "South";
            return Math.Abs(warden.GetNode<Node3D>(i == 0 ? "NorthFaultPointer" : "SouthFaultPointer").Position.Y - (3.5f + direction * (2.2f + (active ? .12f : 0)))) < .00001f;
        });
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
