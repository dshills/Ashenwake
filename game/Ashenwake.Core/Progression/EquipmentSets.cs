namespace Ashenwake.Core.Progression;

public sealed record EquipmentSetDefinition(string Id, string Name, IReadOnlyList<string> PieceIds, string Bonus);

/// <summary>Set membership comes only from distinct equipped item definitions, never engravings or stored copies.</summary>
public static class EquipmentSets
{
    public const string LastVigil = "set.last_vigil", Briarbound = "set.briarbound", Ashrunner = "set.ashrunner";
    public const string VigilHead = "item.lanternkeepers_crown", VigilChest = "item.vigil_of_the_unburied";
    public const string BriarShoulders = "item.thornmother_mantle", BriarGloves = "item.gravegarden_grasp";
    public const string AshBelt = "item.cinderpilgrim_girdle", AshBoots = "item.embers_without_end";
    public static IReadOnlyList<EquipmentSetDefinition> Catalog { get; } = Array.AsReadOnly(new EquipmentSetDefinition[]
    {
        new(LastVigil, "Vestments of the Last Vigil", Array.AsReadOnly(new[] { VigilHead, VigilChest }),
            "Absorbing hostile damage with your barrier readies a spectral counter for 4 seconds. Your next direct skill hit deals 36 additional Void damage. 3-second cooldown; does not stack."),
        new(Briarbound, "Briarbound Covenant", Array.AsReadOnly(new[] { BriarShoulders, BriarGloves }),
            "Your poison kills heal up to eight nearby living companions by 20 health and grow a thorn patch for 2 seconds, dealing 8 Physical Pierce damage every 0.67 seconds. 3-second cooldown."),
        new(Ashrunner, "Ashrunner’s Oath", Array.AsReadOnly(new[] { AshBelt, AshBoots }),
            "Avoiding a hostile hit during a dodge readies your next direct skill hit for 4 seconds. That hit lays three ember patches for 2 seconds, dealing 6 Fire damage every 0.67 seconds. 3-second cooldown.")
    });

    public static EquipmentSetDefinition? FindForItem(string itemId) => Catalog.FirstOrDefault(s => s.PieceIds.Contains(itemId));
    public static bool IsItem(string itemId) => FindForItem(itemId) is not null;
    public static int CountEquipped(string setId, ProgressionState state)
    {
        var set = Catalog.FirstOrDefault(s => s.Id == setId);
        if (set is null) return 0;
        var equipped = state.Equipment.Values.ToHashSet();
        return state.Items.Where(i => equipped.Contains(i.Id) && set.PieceIds.Contains(i.DefinitionId))
            .Select(i => i.DefinitionId).Distinct(StringComparer.Ordinal).Count();
    }
    public static bool Active(string setId, ProgressionState state) => CountEquipped(setId, state) == 2;
    public static int PreviewCountEquipped(string setId, ProgressionState state, PermanentItem candidate, EquipmentSlot slot)
    {
        var set = Catalog.FirstOrDefault(s => s.Id == setId);
        if (set is null) return 0;
        var retained = state.Equipment.Where(p => p.Key != slot && p.Value != candidate.Id).Select(p => p.Value).ToHashSet();
        return state.Items.Where(i => retained.Contains(i.Id)).Select(i => i.DefinitionId).Append(candidate.DefinitionId)
            .Where(set.PieceIds.Contains).Distinct(StringComparer.Ordinal).Count();
    }
}
