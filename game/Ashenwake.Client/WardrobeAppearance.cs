using Ashenwake.Core.Progression;

namespace Ashenwake.Client;

/// <summary>Armor-only cosmetic projection. The source appearance, items and combat build are never mutated.</summary>
public static class WardrobeAppearance
{
    public static CharacterAppearance Project(CharacterAppearance equipped, AppearanceWardrobeMemory? memory)
    {
        if (memory is null) return equipped;
        ItemAppearance At(EquipmentSlot slot, ItemAppearance actual)
        {
            if (slot == EquipmentSlot.Head && memory.HideHelmet) return ItemAppearance.Empty;
            if (actual.DefinitionId.Length == 0 || !memory.Overrides.TryGetValue(slot, out string? id)) return actual;
            var unlocked = memory.Unlocks.FirstOrDefault(u => u.ItemId == id);
            return unlocked is null ? actual : new(id, unlocked.Rarity.ToString());
        }
        return equipped with
        {
            Head = At(EquipmentSlot.Head, equipped.Head),
            Chest = At(EquipmentSlot.Chest, equipped.Chest),
            Shoulders = At(EquipmentSlot.Shoulders, equipped.Shoulders),
            Gloves = At(EquipmentSlot.Gloves, equipped.Gloves),
            Belt = At(EquipmentSlot.Belt, equipped.Belt),
            Legs = At(EquipmentSlot.Legs, equipped.Legs),
            Boots = At(EquipmentSlot.Boots, equipped.Boots)
        };
    }
}
