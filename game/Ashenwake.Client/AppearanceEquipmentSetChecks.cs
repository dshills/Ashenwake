using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public static partial class AppearanceVisualChecks
{
    public static void CheckEquipmentSets(Action<bool, string> require)
    {
        (string Id, EquipmentSlot Slot)[] pieces =
        [
            ("item.lanternkeepers_crown", EquipmentSlot.Head), ("item.vigil_of_the_unburied", EquipmentSlot.Chest),
            ("item.thornmother_mantle", EquipmentSlot.Shoulders), ("item.gravegarden_grasp", EquipmentSlot.Gloves),
            ("item.cinderpilgrim_girdle", EquipmentSlot.Belt), ("item.embers_without_end", EquipmentSlot.Boots)
        ];
        foreach (string discipline in Disciplines)
        {
            var common = Equipped(discipline);
            var set = common with
            {
                Head = new(pieces[0].Id, "Legendary"),
                Chest = new(pieces[1].Id, "Legendary"),
                Shoulders = new(pieces[2].Id, "Legendary"),
                Gloves = new(pieces[3].Id, "Legendary"),
                Belt = new(pieces[4].Id, "Legendary"),
                Boots = new(pieces[5].Id, "Legendary")
            };
            var baseline = Hero(common); var equipped = Hero(set); var repeated = Hero(set);
            var bare = Hero(set with { Head = ItemAppearance.Empty, Chest = ItemAppearance.Empty, Shoulders = ItemAppearance.Empty, Gloves = ItemAppearance.Empty, Belt = ItemAppearance.Empty, Boots = ItemAppearance.Empty });
            try
            {
                foreach (var piece in pieces)
                {
                    string moduleName = "Equipment" + piece.Slot;
                    require(Geometry(Module(baseline, moduleName)!) != Geometry(Module(equipped, moduleName)!), $"set_{discipline}_{piece.Slot}_distinct_geometry");
                    require(Geometry(Module(equipped, moduleName)!) == Geometry(Module(repeated, moduleName)!), $"set_{discipline}_{piece.Slot}_deterministic_geometry");
                    require(Module(bare, moduleName) is null, $"set_{discipline}_{piece.Slot}_unequips");
                    var icon = new GearItemIcon(); var ordinary = new GearItemIcon();
                    try
                    {
                        icon.Configure(piece.Id, piece.Slot, discipline, ItemRarity.Legendary);
                        ordinary.Configure("item.starter_" + piece.Slot.ToString().ToLowerInvariant(), piece.Slot, discipline, ItemRarity.Legendary);
                        require(icon.IconKey.Split('|').Last() != ordinary.IconKey.Split('|').Last(), $"set_{discipline}_{piece.Slot}_distinct_icon");
                    }
                    finally { icon.Free(); ordinary.Free(); }
                }
                require(CosmeticOnly(equipped) && Descendants(equipped).OfType<MeshInstance3D>().Count() <= 100, "set_" + discipline + "_bounded_cosmetic_meshes");
                var meshes = MeshIdentities(equipped); var transform = equipped.Transform;
                equipped.SetReducedEffects(true); equipped.Animate(.1, Vector3.Right);
                require(meshes.SequenceEqual(MeshIdentities(equipped)) && equipped.Transform.IsEqualApprox(transform), "set_" + discipline + "_reduced_effects_retains_identity");
            }
            finally { baseline.Free(); equipped.Free(); repeated.Free(); bare.Free(); }
        }
    }
}
