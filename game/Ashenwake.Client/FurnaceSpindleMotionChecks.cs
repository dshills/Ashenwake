using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

/// <summary>Detached fixtures exercise cosmetic response to supplied defense and warning projections.</summary>
internal static class FurnaceSpindleMotionChecks
{
    public static void Run(Action<string, bool> check)
    {
        var guarded = View(true);
        var exposed = View(false);
        var furnace = FurnaceSpindleVisual.Create(12, 10, guarded);
        var repeated = FurnaceSpindleVisual.Create(12, 10, guarded);
        var restored = FurnaceSpindleVisual.Create(12, 10, guarded, true);
        try
        {
            var root = furnace.Transform;
            var ids = Ids(furnace);
            var axle = furnace.GetNode<Node3D>("FurnaceCoreAxle");
            var shutters = Enumerable.Range(0, 4).Select(i => furnace.GetNode<Node3D>("CoreArmorShutter" + i)).ToArray();
            var pistons = Enumerable.Range(0, 2).Select(i => furnace.GetNode<Node3D>("PressurePiston" + i)).ToArray();
            var light = Descendants(furnace).OfType<OmniLight3D>().Single();
            var core = Core(furnace);
            furnace.SetState(exposed); repeated.SetState(exposed);
            check("furnace_exposure_uses_immediate_authoritative_armor_pose", !furnace.Guarded &&
                shutters.All(shutter => Math.Abs(Math.Abs(shutter.Position.X) - 1) < .00001f) &&
                Math.Abs(light.LightEnergy - 1.65f) < .00001f);
            furnace.Animate(.1, false, false); repeated.Animate(.1, false, false);
            check("furnace_exposure_releases_core_and_kicks_armor", axle.Position.Z > .68f &&
                shutters.All(shutter => shutter.Position.Z > 1.38f));
            for (int frame = 0; frame < 8; frame++)
            {
                repeated.SetState(exposed);
                furnace.Animate(.05, false, false); repeated.Animate(.05, false, false);
            }
            check("furnace_repeated_snapshots_do_not_restart_defense_reaction", Same(Pose(furnace), Pose(repeated)));
            var paused = Pose(furnace);
            furnace.Animate(20, true, false);
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
                furnace.Animate(invalid, false, false);
            check("furnace_pause_and_invalid_delta_freeze_mechanical_reaction", Same(paused, Pose(furnace)));

            furnace.SetState(guarded);
            check("furnace_guard_return_closes_armor_immediately", furnace.Guarded &&
                shutters.All(shutter => Math.Abs(Math.Abs(shutter.Position.X) - .43f) < .00001f));
            furnace.Animate(.1, false, false);
            check("furnace_guard_return_braces_core_inward", axle.Position.Z < .64f);
            furnace.Animate(0, true, true);
            check("furnace_reduced_effects_consumes_guard_reaction_while_paused", Math.Abs(axle.Position.Z - .65f) < .00001f &&
                shutters.All(shutter => Math.Abs(shutter.Position.Z - 1.37f) < .00001f));
            furnace.Animate(.1, false, false);
            check("furnace_guard_recoil_never_replays_after_reduced_effects", Math.Abs(axle.Position.Z - .65f) < .00001f &&
                shutters.All(shutter => Math.Abs(shutter.Position.Z - 1.37f) < .00001f));

            var horizontal = guarded with { CampaignHazards = [new(1, "Line", new(-3000, 0), new(3000, 0), 200, 20, "campaign.furnace_vent", 2)] };
            var vertical = guarded with { CampaignHazards = [new(2, "Line", new(0, -3000), new(0, 3000), 200, 20, "campaign.furnace_vent", 2)] };
            furnace.SetState(horizontal);
            check("furnace_horizontal_warning_is_immediate", furnace.VentOrientation == "Horizontal" && Vents(furnace, "Horizontal"));
            furnace.SetState(vertical);
            check("furnace_vertical_warning_replaces_horizontal_immediately", furnace.VentOrientation == "Vertical" && Vents(furnace, "Vertical"));
            var inactive = guarded with
            {
                CampaignHazards = [new(3, "Line", new(-3000, 0), new(3000, 0), 200, 0, "campaign.furnace_vent", 2),
                new(4, "Line", new(-3000, 0), new(3000, 0), 200, 20, "campaign.furnace_vent", 99)]
            };
            furnace.SetState(inactive);
            check("furnace_expired_or_unrelated_hazards_cannot_warn", !furnace.VentWarning && Vents(furnace, "None"));

            furnace.SetState(exposed); furnace.Animate(.1, false, false);
            var axleBefore = axle.Transform;
            var shutterBefore = shutters.Select(node => node.Transform).ToArray();
            var pistonBefore = pistons.Select(node => node.Transform).ToArray();
            furnace.SetState(exposed, true);
            check("furnace_shutdown_preserves_current_hardware_pose_at_start", furnace.IsTransitioning && furnace.VictoryProgress == 0 &&
                axleBefore.IsEqualApprox(axle.Transform) && Same(shutterBefore, shutters.Select(node => node.Transform).ToArray()) &&
                Same(pistonBefore, pistons.Select(node => node.Transform).ToArray()));
            float energy = light.LightEnergy, range = light.OmniRange, emission = core.EmissionEnergyMultiplier;
            furnace.Animate(.1, false, false);
            check("furnace_shutdown_brakes_before_releasing_hardware", !axleBefore.Basis.IsEqualApprox(axle.Transform.Basis) &&
                Same(shutterBefore, shutters.Select(node => node.Transform).ToArray()) && Same(pistonBefore, pistons.Select(node => node.Transform).ToArray()));
            paused = Pose(furnace); furnace.Animate(10, true, false);
            check("furnace_pause_freezes_staged_shutdown", Same(paused, Pose(furnace)));
            for (int frame = 0; frame < 4; frame++) furnace.Animate(.1, false, false);
            check("furnace_shutdown_releases_shutters_in_order", shutters[0].Position.DistanceTo(shutterBefore[0].Origin) >
                shutters[3].Position.DistanceTo(shutterBefore[3].Origin) && Same(pistonBefore, pistons.Select(node => node.Transform).ToArray()));
            bool monotonic = true, embers = furnace.ActiveTransientCount > 0;
            for (int frame = 0; frame < 35; frame++)
            {
                furnace.Animate(.1, false, false);
                monotonic &= light.LightEnergy <= energy && light.OmniRange <= range && core.EmissionEnergyMultiplier <= emission;
                energy = light.LightEnergy; range = light.OmniRange; emission = core.EmissionEnergyMultiplier;
                embers |= furnace.ActiveTransientCount > 0;
            }
            check("furnace_shutdown_cools_without_flash_and_consumes_embers", monotonic && embers &&
                furnace.ActiveTransientCount == 0 && !furnace.IsTransitioning && furnace.VictoryProgress == 1);
            check("furnace_restored_defeat_matches_completed_shutdown", Same(Pose(furnace), Pose(restored)) && !restored.IsTransitioning && restored.ActiveTransientCount == 0);
            paused = Pose(furnace);
            furnace.SetState(exposed, true); furnace.Animate(.1, false, false);
            check("furnace_repeated_defeat_never_replays_shutdown", Same(paused, Pose(furnace)));
            furnace.SetState(horizontal); furnace.Animate(.1, false, false); furnace.SetState(horizontal, true);
            check("furnace_defeat_clears_live_warning_without_delay", !furnace.VentWarning && Vents(furnace, "None"));
            furnace.Animate(.1, false, false); furnace.Animate(0, true, true);
            furnace.Animate(.1, true, false); furnace.Animate(.1, false, false);
            check("furnace_reduced_effects_settles_shutdown_while_paused_without_replay", Same(Pose(furnace), Pose(restored)) &&
                !furnace.IsTransitioning && furnace.ActiveTransientCount == 0);
            check("furnace_staged_motion_keeps_fixed_resources_and_root", furnace.Transform.IsEqualApprox(root) && ids.SequenceEqual(Ids(furnace)) &&
                furnace.ArticulatedPartCount == 11 && furnace.TransientCapacity == 12 &&
                !Descendants(furnace).Any(node => node is CollisionObject3D or CollisionShape3D or NavigationRegion3D));
            check("furnace_cooling_does_not_mutate_other_room_materials", Core(repeated).AlbedoColor != core.AlbedoColor &&
                Core(repeated).EmissionEnergyMultiplier > 1 && Core(repeated).GetInstanceId() != core.GetInstanceId());
        }
        finally { furnace.Free(); repeated.Free(); restored.Free(); }
    }

    private static CombatView View(bool guarded) => new(Tick: 0, Preset: "standard",
        Actors: [new(2, new(0, 0), 100, 100, CombatFaction.Enemy, "Boss", 0, "Acquire", 0, [], "boss.furnace_spindle", Guarded: guarded)],
        Projectiles: [], Areas: [], Loot: [], Inventory: [], Skills: [], Fragments: [], Mutations: [], Equipment: new Dictionary<string, long>(),
        Momentum: 0, MaxMomentum: 100, Barrier: 0, PotionCharges: 3, PotionCooldownTicks: 0, DodgeCooldownTicks: 0, Resonance: 0,
        PendingEffects: 0, PeakEffects: 0, RejectedEffects: 0, ContentVersion: "cosmetic-fixture");

    private static bool Vents(FurnaceSpindleVisual furnace, string orientation) => Enumerable.Range(0, 4).All(i =>
        Math.Abs(furnace.GetNode<Node3D>("VentLouvers" + i).RotationDegrees.X -
            ((i < 2 ? orientation == "Horizontal" : orientation == "Vertical") ? -38 : 0)) < .001f);
    private static StandardMaterial3D Core(Node root) => (StandardMaterial3D)Descendants(root).OfType<MeshInstance3D>().Single(mesh => mesh.Name == "ExposedDivineCore").MaterialOverride;
    private static ulong[] Ids(Node root) => Descendants(root).Select(node => node.GetInstanceId()).Concat(Descendants(root).OfType<MeshInstance3D>()
        .SelectMany(mesh => new[] { mesh.Mesh.GetInstanceId(), (mesh.MaterialOverride ?? mesh.Mesh.SurfaceGetMaterial(0)).GetInstanceId() })).ToArray();
    private static Transform3D[] Pose(Node root) => Descendants(root).OfType<Node3D>().Select(node => node.Transform).ToArray();
    private static bool Same(Transform3D[] left, Transform3D[] right) => left.Length == right.Length && left.Zip(right).All(pair => pair.First.IsEqualApprox(pair.Second));
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
}
