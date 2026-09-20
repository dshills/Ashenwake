using Ashenwake.Core.Combat;
using Ashenwake.Core.Progression;

namespace Ashenwake.Client;

/// <summary>Presentation identity only: instance IDs, rolled stats and gameplay rules stay in Core.</summary>
public sealed record ItemAppearance(string DefinitionId = "", string Rarity = "Common", string Evolution = "")
{
    public static ItemAppearance Empty { get; } = new();
    public string Key => $"{DefinitionId}/{Rarity}/{Evolution}";
}

public sealed record CharacterAppearance(string Discipline, ItemAppearance MainHand, ItemAppearance OffHand,
    ItemAppearance Head, ItemAppearance Chest, int ManifestationMask = 0, int AnatomyMask = 0)
{
    public ItemAppearance Shoulders { get; init; } = ItemAppearance.Empty;
    public ItemAppearance Gloves { get; init; } = ItemAppearance.Empty;
    public ItemAppearance Belt { get; init; } = ItemAppearance.Empty;
    public ItemAppearance Legs { get; init; } = ItemAppearance.Empty;
    public ItemAppearance Boots { get; init; } = ItemAppearance.Empty;
    public string Key => $"{Discipline}|{MainHand.Key}|{OffHand.Key}|{Head.Key}|{Chest.Key}|{Shoulders.Key}|{Gloves.Key}|{Belt.Key}|{Legs.Key}|{Boots.Key}|{ManifestationMask & 15}|{AnatomyMask & 15}";

    public static int AnatomyFragments(IEnumerable<string>? ids)
    {
        int mask = 0;
        if (ids is not null)
            foreach (string id in ids)
                mask |= id switch
                {
                    "fragment.eye_vael" => 1,
                    "fragment.heart_serath" => 2,
                    "fragment.nerve_ilyra" => 4,
                    "fragment.orrun_bone" => 8,
                    _ => 0
                };
        return mask;
    }

    public static int ManifestationBit(string id) => id switch
    {
        "manifestation.burning_blood" => 1,
        "manifestation.whispering_shadow" => 2,
        "manifestation.stone_memory" => 4,
        "manifestation.voracious_renewal" => 8,
        _ => 0
    };
    public static int Manifestations(IEnumerable<string>? ids)
    {
        int mask = 0;
        if (ids is not null) foreach (string id in ids) mask |= ManifestationBit(id);
        return mask;
    }

    public static CharacterAppearance FromCombat(CombatView view, IReadOnlyList<string>? manifestations = null, string ashcleaverEvolution = "")
    {
        ItemAppearance At(string slot)
        {
            if (!view.Equipment.TryGetValue(slot, out long id)) return ItemAppearance.Empty;
            var item = view.Inventory.FirstOrDefault(i => i.Id == id);
            return item is null ? ItemAppearance.Empty : new(item.DefinitionId, item.Rarity,
                item.DefinitionId == "item.ashcleaver" ? ashcleaverEvolution : "");
        }
        return new(view.Discipline, At("MainHand"), At("OffHand"), At("Head"), At("Chest"), Manifestations(manifestations),
            AnatomyFragments(view.Fragments.Where(f => f.Equipped).Select(f => f.Id)))
        { Shoulders = At("Shoulders"), Gloves = At("Gloves"), Belt = At("Belt"), Legs = At("Legs"), Boots = At("Boots") };
    }

    public static CharacterAppearance FromProgression(ProgressionSnapshot state, IReadOnlyList<string>? manifestations = null,
        IEnumerable<string>? fragments = null)
    {
        ItemAppearance At(EquipmentSlot slot)
        {
            if (!state.Character.Equipment.TryGetValue(slot, out long id)) return ItemAppearance.Empty;
            var item = state.Character.Items.FirstOrDefault(i => i.Id == id);
            return item is null ? ItemAppearance.Empty : new(item.DefinitionId, item.Rarity.ToString(),
                item.DefinitionId == "item.ashcleaver" ? item.Evolution.Length > 0 ? item.Evolution : item.Awakened ? "Awakened" : "" : "");
        }
        return new(state.Character.Discipline, At(EquipmentSlot.MainHand), At(EquipmentSlot.OffHand), At(EquipmentSlot.Head), At(EquipmentSlot.Chest),
            Manifestations(manifestations), AnatomyFragments(fragments))
        { Shoulders = At(EquipmentSlot.Shoulders), Gloves = At(EquipmentSlot.Gloves), Belt = At(EquipmentSlot.Belt), Legs = At(EquipmentSlot.Legs), Boots = At(EquipmentSlot.Boots) };
    }
}
