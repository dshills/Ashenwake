namespace Ashenwake.Core.Progression;

public sealed partial class ProgressionSession
{
    /// <summary>Remove an owned, unequipped item permanently. Disposal grants no materials or other rewards.</summary>
    public ProgressionResult Discard(string operationId, long itemId, bool confirmPermanent = false)
        => Change(operationId, new { Action = "Discard", itemId, confirmPermanent }, (next, events) =>
        {
            if (!confirmPermanent) return "Confirm permanent destruction before discarding this item.";
            string blocked = DiscardBlockedReason(next, itemId);
            if (blocked.Length > 0) return blocked;
            next.Character.Items = next.Character.Items.Where(item => item.Id != itemId).ToArray();
            events.Add("ItemDiscarded:" + itemId); return null;
        });

    public static string DiscardBlockedReason(ProgressionSnapshot state, long itemId)
    {
        var item = state.Character.Items.FirstOrDefault(item => item.Id == itemId);
        if (item is null) return "Select an item you own.";
        if (ProtectionBlockedReason(item) is { Length: > 0 } protection) return protection;
        if (state.Character.Equipment.Values.Contains(itemId)) return "Unequip this item before discarding it.";
        // The adventure retains at least one Ashcleaver. Extra earned copies may be
        // discarded so even a very large Godwrought collection can free bag space.
        if (item.Rarity == ItemRarity.Godwrought && state.Character.Items.Count(other => other.Rarity == ItemRarity.Godwrought) <= 1)
            return "Keep your last Godwrought weapon; its progression belongs to this character.";
        return "";
    }
}
