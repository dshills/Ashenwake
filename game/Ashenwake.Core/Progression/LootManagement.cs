namespace Ashenwake.Core.Progression;

/// <summary>Exact, deterministic salvage yield and the saved outfits affected by removing this item.</summary>
public sealed record SalvagePreview(bool Success, string Reason, long ItemId, int Materials, string[] PresetNames);

public sealed partial class ProgressionSession
{
    public ProgressionResult SetItemFavorite(string operationId, long itemId, bool value)
        => Change(operationId, new { Action = "SetItemFavorite", itemId, value }, (next, events) =>
        {
            var item = next.Character.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null) return "Select an item you own.";
            item.IsFavorite = value; events.Add("ItemFavoriteChanged:" + itemId + ":" + value); return null;
        });

    public ProgressionResult SetItemLocked(string operationId, long itemId, bool value)
        => Change(operationId, new { Action = "SetItemLocked", itemId, value }, (next, events) =>
        {
            var item = next.Character.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null) return "Select an item you own.";
            item.IsLocked = value; events.Add("ItemLockChanged:" + itemId + ":" + value); return null;
        });

    public static string ProtectionBlockedReason(PermanentItem item) => (item.IsFavorite, item.IsLocked) switch
    {
        (true, true) => "Remove this item's favorite and lock protection before destroying it.",
        (true, false) => "Remove this item's favorite protection before destroying it.",
        (false, true) => "Unlock this item before destroying it.",
        _ => ""
    };

    public static string[] ItemPresetNames(ProgressionSnapshot state, long itemId) =>
        (state.Character.EquipmentPresets ?? []).Where(preset => preset.Equipment.Values.Contains(itemId))
            .Select(preset => preset.Name).Concat((state.Character.BuildLoadouts ?? [])
                .Where(loadout => loadout.Equipment.Values.Contains(itemId)).Select(loadout => "Build: " + loadout.Name)).ToArray();

    public static SalvagePreview PreviewSalvage(ProgressionSnapshot state, long itemId)
    {
        var names = ItemPresetNames(state, itemId);
        SalvagePreview Blocked(string reason) => new(false, reason, itemId, 0, names);
        var item = state.Character.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return Blocked("Select an item you own.");
        if (ProtectionBlockedReason(item) is { Length: > 0 } protection) return Blocked(protection);
        if (state.Character.Equipment.Values.Contains(itemId)) return Blocked("Unequip this item before salvaging it.");
        if (item.Rarity == ItemRarity.Godwrought) return Blocked("Godwrought equipment cannot be salvaged; its progression belongs to this character.");
        int materials = item.Rarity switch
        {
            ItemRarity.Common => 1,
            ItemRarity.Tempered => 2,
            ItemRarity.Rare => 4,
            ItemRarity.Relic => 6,
            ItemRarity.Legendary => 10,
            _ => 0
        };
        if (materials == 0) return Blocked("This item cannot be salvaged.");
        if (state.Character.Materials > 1000000 - materials) return Blocked("Spend materials before salvaging; the full return would exceed the material limit.");
        return new(true, "", itemId, materials, names);
    }

    public SalvagePreview PreviewSalvage(long itemId) => PreviewSalvage(snapshot, itemId);

    public ProgressionResult Salvage(string operationId, long itemId, bool confirmPermanent = false)
        => Change(operationId, new { Action = "Salvage", itemId, confirmPermanent }, (next, events) =>
        {
            if (!confirmPermanent) return "Confirm permanent destruction and the material return before salvaging this item.";
            var preview = PreviewSalvage(next, itemId);
            if (!preview.Success) return preview.Reason;
            next.Character.Items = next.Character.Items.Where(item => item.Id != itemId).ToArray();
            next.Character.Materials += preview.Materials;
            events.Add("ItemSalvaged:" + itemId); events.Add("SalvageMaterials:" + preview.Materials); return null;
        });
}
