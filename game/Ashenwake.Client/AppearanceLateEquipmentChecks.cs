using Ashenwake.Core.Combat;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public static partial class AppearanceVisualChecks
{
    private static void CheckLateLegendaryArmor(Action<bool, string> require)
    {
        foreach (string discipline in Disciplines)
        {
            var common = Equipped(discipline) with { Amulet = new("item.starter_amulet") };
            var relics = common with { Head = new(LegendaryEquipment.Crown, "Legendary"), Amulet = new(LegendaryEquipment.Witness, "Legendary"), Legs = new(LegendaryEquipment.Hour, "Legendary") };
            var baseline = Hero(common); var equipped = Hero(relics); var bare = Hero(relics with { Amulet = ItemAppearance.Empty });
            try
            {
                foreach (string slot in new[] { "Head", "Amulet", "Legs" })
                    require(Geometry(Module(baseline, "Equipment" + slot)!) != Geometry(Module(equipped, "Equipment" + slot)!), "late_" + discipline + "_" + slot + "_distinct_geometry");
                require(Module(bare, "EquipmentAmulet") is null && bare.AppearanceKey != equipped.AppearanceKey, "late_" + discipline + "_amulet_unequips_visually");
                require(CosmeticOnly(equipped) && Descendants(equipped).OfType<MeshInstance3D>().Count() <= 100, "late_" + discipline + "_bounded_cosmetic_meshes");
                var meshes = MeshIdentities(equipped); var root = equipped.Transform;
                equipped.SetReducedEffects(true); equipped.Animate(.1, Vector3.Right);
                require(meshes.SequenceEqual(MeshIdentities(equipped)) && equipped.Transform.IsEqualApprox(root), "late_" + discipline + "_reduced_effects_retains_identity_without_root_motion");
            }
            finally { baseline.Free(); equipped.Free(); bare.Free(); }
        }
        foreach (var (id, slot) in new[] { (LegendaryEquipment.Crown, EquipmentSlot.Head), (LegendaryEquipment.Witness, EquipmentSlot.Amulet), (LegendaryEquipment.Hour, EquipmentSlot.Legs) })
        {
            var icon = new GearItemIcon(); var ordinary = new GearItemIcon();
            try
            {
                icon.Configure(id, slot, "Vanguard", ItemRarity.Legendary);
                ordinary.Configure("item.starter_" + slot.ToString().ToLowerInvariant(), slot, "Vanguard", ItemRarity.Legendary);
                require(icon.IconKey.Split('|').Last() != ordinary.IconKey.Split('|').Last(), "late_unique_icon_" + id);
            }
            finally { icon.Free(); ordinary.Free(); }
        }
    }
}
