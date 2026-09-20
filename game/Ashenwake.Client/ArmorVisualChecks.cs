using Godot;

namespace Ashenwake.Client;

public static partial class AppearanceVisualChecks
{
    private static void CheckArmor(Action<bool, string> require)
    {
        string[] paired = ["Shoulders", "Gloves", "Legs", "Boots"];
        foreach (string discipline in Disciplines)
        {
            var dressed = Hero(Equipped(discipline));
            var second = Hero(Equipped(discipline));
            var bare = Hero(new(discipline, ItemAppearance.Empty, ItemAppearance.Empty, ItemAppearance.Empty, ItemAppearance.Empty));
            try
            {
                require(paired.All(s => HasGeometry(Module(dressed, "Equipment" + s + "Right")) && Module(bare, "Equipment" + s + "Right") is null),
                    "armor_" + discipline + "_both_sides_follow_slot_ownership");
                require(paired.All(s => MeshIdentities(Module(dressed, "Equipment" + s)!).SequenceEqual(MeshIdentities(Module(second, "Equipment" + s)!)) &&
                    MeshIdentities(Module(dressed, "Equipment" + s + "Right")!).SequenceEqual(MeshIdentities(Module(second, "Equipment" + s + "Right")!))),
                    "armor_" + discipline + "_paired_modules_reuse_meshes");
                foreach (string suffix in new[] { "", "Right" })
                {
                    require(Module(dressed, "EquipmentShoulders" + suffix)!.GetParent() == Module(dressed, "EquipmentGloves" + suffix)!.GetParent() &&
                        Module(dressed, "EquipmentLegs" + suffix)!.GetParent() == Module(dressed, "EquipmentBoots" + suffix)!.GetParent() &&
                        Module(dressed, "EquipmentLegs" + suffix)!.GetParent() != Module(dressed, "EquipmentBelt")!.GetParent(),
                        "armor_" + discipline + "_" + suffix + "_follows_matching_arm_and_leg_joints");
                }
                var root = dressed.Transform;
                var resources = MeshIdentities(dressed);
                var origins = paired.SelectMany(s => new[] { Module(dressed, "Equipment" + s)!, Module(dressed, "Equipment" + s + "Right")! }).ToArray();
                var local = origins.Select(n => n.Transform).ToArray();
                var joints = origins.Select(n => n.GetParent<Node3D>().Transform).ToArray();
                for (int i = 0; i < 30; i++) dressed.Animate(1.0 / 60, new(1, 0, 0), windup: true);
                require(origins.Select(n => n.Transform).SequenceEqual(local) && origins.Select(n => n.GetParent<Node3D>().Transform).Where((t, i) => !t.IsEqualApprox(joints[i])).Any() &&
                    dressed.Transform.IsEqualApprox(root) && resources.SequenceEqual(MeshIdentities(dressed)),
                    "armor_" + discipline + "_moves_with_joints_without_allocating_meshes_or_root_motion");
                require(Descendants(dressed).OfType<MeshInstance3D>().All(m => m.Mesh.GetFaces().All(v => v.IsFinite())) &&
                    Descendants(dressed).OfType<MeshInstance3D>().Count() <= 140,
                    "armor_" + discipline + "_finite_geometry_with_bounded_mesh_count");
                require(!Descendants(dressed).Any(n => n.Name == "EquipmentChestLeft" || n.Name == "EquipmentChestRight"),
                    "armor_" + discipline + "_chest_no_longer_owns_shoulders_or_gloves");
            }
            finally { dressed.Free(); second.Free(); bare.Free(); }
        }

        foreach (string rarity in Rarities)
        {
            var appearance = Equipped() with
            {
                Shoulders = new("item.starter_shoulders", rarity),
                Gloves = new("item.starter_gloves", rarity),
                Belt = new("item.starter_belt", rarity),
                Legs = new("item.starter_legs", rarity),
                Boots = new("item.starter_boots", rarity)
            };
            var visual = Hero(appearance);
            try
            {
                require(visual.AppearanceKey == appearance.Key && paired.All(s => HasGeometry(Module(visual, "Equipment" + s))) && CosmeticOnly(visual),
                    "armor_" + rarity + "_supports_every_new_slot_without_gameplay_nodes");
            }
            finally { visual.Free(); }
        }
        foreach (string discipline in new[] { "Arcanist", "Gravecaller" })
        {
            var visual = Hero(Equipped(discipline));
            try
            {
                // Check in body-local coordinates: the robe front must leave room for knees, toes and a belt.
                var chest = Module(visual, "EquipmentChest")!;
                var vertices = Descendants(chest).OfType<MeshInstance3D>().SelectMany(m => m.Mesh.GetFaces().Select(v => m.Transform * v));
                require(!vertices.Any(v => v.Y < .65f && Math.Abs(v.X) < .23f && v.Z < -.12f),
                    "armor_" + discipline + "_robe_front_leaves_lower_leg_equipment_visible");
                require(Descendants(chest).OfType<MeshInstance3D>().All(m => Enumerable.Range(0, m.Mesh.GetSurfaceCount()).All(i =>
                    m.Mesh.SurfaceGetArrays(i)[(int)Mesh.ArrayType.Normal].AsVector3Array().All(n => n.IsFinite() && n.LengthSquared() > .9f))),
                    "armor_" + discipline + "_double_sided_robe_has_valid_lighting_normals");
            }
            finally { visual.Free(); }
        }
    }
}
