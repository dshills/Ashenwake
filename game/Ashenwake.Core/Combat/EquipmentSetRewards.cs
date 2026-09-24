using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Combat;

public sealed partial class CombatSession
{
    // Additional authored drops leave ordinary loot rolls and existing legendary rewards intact.
    private void RewardEquipmentSet(CombatActor target, Hit hit)
    {
        if (_state.Loot.Count >= 512 || _state.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0 && CampaignRewardEligible(a))) return;
        var entry = LegendaryCollectionCatalog.Entries.FirstOrDefault(e => EquipmentSets.IsItem(e.ItemId) &&
            (_state.Endgame is { } run
                ? run.Manifest.Kind == "Fracture" && e.FractureRegionId == run.Manifest.Region && e.FractureEncounterIndex == run.EncounterIndex
                : e.CampaignEncounterId == _state.EncounterId));
        // Older authenticated content bundles do not contain set items and keep their exact rewards.
        var definition = entry is null ? null : _content.Items.FirstOrDefault(i => i.Id == entry.ItemId);
        if (definition is null) return;
        var item = new CombatItem(_state.NextObjectId++, definition.Id, definition.Name, definition.Slot, "Legendary", definition.Damage, definition.Armor, definition.CriticalBasisPoints);
        _state.Loot.Add(new(item.Id, target.Position, item));
        Emit("LootDropped", hit.OwnerId, target.Id, (int)item.Id, definition.Id, hit.ActionId, hit.Depth);
    }
}
