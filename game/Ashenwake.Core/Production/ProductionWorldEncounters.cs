using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Production;

public sealed partial class ProductionSession
{
    internal bool CanClaimWorldEncounter => CanClaimRegionalHunt && CharacterStash.BackpackCount(progression.CharacterState) < CharacterStash.BackpackCapacity;
    private static string WorldRewardKey(string id) => "world-encounter.reward." + id;
    internal string[] GrantWorldEncounter(string id, string outcome)
    {
        var d = WorldEncounterCatalog.Find(id) ?? throw new InvalidDataException("Unknown world encounter.");
        string key = WorldRewardKey(id);
        if (progression.CharacterState.OperationReceipts.ContainsKey(key + ".item")) throw new InvalidDataException("World encounter treasure was already claimed.");
        SynchronizeItemSequence();
        var item = progression.GrantItem(key + ".item", d.RewardItemId, ItemRarity.Legendary); Require(item);
        var materials = progression.EarnExperience(key + ".materials", 0, WorldEncounterCatalog.Materials(id, outcome)); Require(materials);
        ProjectPermanentInventory(); return [.. item.Events, .. materials.Events, "WorldEncounterRewardClaimed:" + id];
    }
    internal void RecordWorldEncounterCompletion(string id, string outcome)
    {
        var next = progression.Capture();
        string key = "world-encounter.completion." + id;
        if (next.Character.OperationReceipts.ContainsKey(key)) throw new InvalidDataException("World encounter was already completed.");
        next.Character.OperationReceipts.Add(key, JsonData.Hash(new { Action = "WorldEncounterCompleted", Id = id, Outcome = outcome }));
        progression.AdoptAuthoritativeState(next);
    }
    internal void ValidateWorldEncounterRewards(string[] claimed, SortedDictionary<string, string> completed)
    {
        var receipts = progression.CharacterState.OperationReceipts;
        var expectedCompletions = completed.Keys.Select(id => "world-encounter.completion." + id).ToHashSet(StringComparer.Ordinal);
        if (!expectedCompletions.SetEquals(receipts.Keys.Where(k => k.StartsWith("world-encounter.completion.", StringComparison.Ordinal))) ||
            completed.Any(p => receipts["world-encounter.completion." + p.Key] != JsonData.Hash(new { Action = "WorldEncounterCompleted", Id = p.Key, Outcome = p.Value })))
            throw new InvalidDataException("World encounter outcomes differ from permanent receipts.");
        var expected = claimed.SelectMany(id => new[] { WorldRewardKey(id) + ".item", WorldRewardKey(id) + ".materials" }).ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(receipts.Keys.Where(k => k.StartsWith("world-encounter.reward.", StringComparison.Ordinal))))
            throw new InvalidDataException("World encounter treasure ledger differs from permanent receipts.");
        foreach (string id in claimed)
        {
            var d = WorldEncounterCatalog.Find(id) ?? throw new InvalidDataException("Unknown world encounter reward.");
            if (receipts[WorldRewardKey(id) + ".item"] != JsonData.Hash(new { Action = "GrantItem", definitionId = d.RewardItemId, rarity = ItemRarity.Legendary, Affixes = new SortedDictionary<string, int>() }) ||
                !HasCampaignReward(WorldRewardKey(id) + ".materials", 0, WorldEncounterCatalog.Materials(id, completed[id])))
                throw new InvalidDataException("World encounter reward payload differs from its earned outcome.");
        }
    }
}
