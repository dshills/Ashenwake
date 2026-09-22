using Ashenwake.Core.Combat;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public static partial class AppearanceVisualChecks
{
    private static void CheckMidgameLegendaryArmor(Action<bool, string> require)
    {
        foreach (string discipline in Disciplines)
        {
            var common = Equipped(discipline);
            var legendary = common with
            {
                Shoulders = new(LegendaryEquipment.Mourning, "Legendary"),
                Belt = new(LegendaryEquipment.Furnace, "Legendary"),
                Ring1 = new(LegendaryEquipment.Rotwake, "Legendary")
            };
            var baseline = Hero(common); var equipped = Hero(legendary);
            var otherHand = Hero(legendary with { Ring1 = ItemAppearance.Empty, Ring2 = legendary.Ring1 });
            try
            {
                foreach (string slot in new[] { "Shoulders", "Belt" })
                    require(Geometry(Module(baseline, "Equipment" + slot)!) != Geometry(Module(equipped, "Equipment" + slot)!),
                        "appearance_" + discipline + "_midgame_" + slot + "_has_distinct_geometry");
                require(HasGeometry(Module(equipped, "EquipmentRing1")) && Module(equipped, "EquipmentRing2") is null &&
                    HasGeometry(Module(otherHand, "EquipmentRing2")) && Module(otherHand, "EquipmentRing1") is null &&
                    equipped.AppearanceKey != otherHand.AppearanceKey,
                    "appearance_" + discipline + "_rotwake_tracks_actual_ring_slot");
                require(CosmeticOnly(equipped) && Descendants(equipped).OfType<MeshInstance3D>().Count() <= 100,
                    "appearance_" + discipline + "_midgame_relics_are_bounded_and_cosmetic");
                var meshes = MeshIdentities(equipped);
                equipped.SetReducedEffects(true); equipped.Animate(.1, Vector3.Right);
                require(meshes.SequenceEqual(MeshIdentities(equipped)) && HasGeometry(Module(equipped, "EquipmentShoulders")) &&
                    HasGeometry(Module(equipped, "EquipmentBelt")) && HasGeometry(Module(equipped, "EquipmentRing1")),
                    "appearance_" + discipline + "_midgame_identity_survives_reduced_effects");
            }
            finally { baseline.Free(); equipped.Free(); otherHand.Free(); }
        }
        foreach (var (id, slot) in new[] { (LegendaryEquipment.Rotwake, EquipmentSlot.Ring1), (LegendaryEquipment.Mourning, EquipmentSlot.Shoulders), (LegendaryEquipment.Furnace, EquipmentSlot.Belt) })
        {
            var icon = new GearItemIcon(); var ordinary = new GearItemIcon();
            try
            {
                icon.Configure(id, slot, "Vanguard", ItemRarity.Legendary);
                ordinary.Configure("item.starter_" + slot.ToString().ToLowerInvariant(), slot, "Vanguard", ItemRarity.Legendary);
                require(icon.IconKey.Split('|').Last() != ordinary.IconKey.Split('|').Last(), "midgame_icon_has_unique_silhouette_" + id);
            }
            finally { icon.Free(); ordinary.Free(); }
        }
    }
}
