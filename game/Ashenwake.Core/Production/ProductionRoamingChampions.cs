using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Production;

public sealed partial class ProductionSession
{
    internal bool CanClaimRoamingChampionReward => CanClaimRegionalHunt && CharacterStash.BackpackCount(progression.CharacterState) < CharacterStash.BackpackCapacity;
    private static string RoamingRewardKey(string id) => "roaming-champion.reward." + id;
    internal string[] GrantRoamingChampion(string id)
    {
        var definition = RoamingChampionCatalog.Find(id) ?? throw new InvalidDataException("Unknown roaming champion.");
        string key = RoamingRewardKey(id);
        if (progression.CharacterState.OperationReceipts.ContainsKey(key)) throw new InvalidDataException("Roaming champion treasure was already claimed.");
        SynchronizeItemSequence();
        var item = progression.GrantItem(key, definition.RewardItemId, ItemRarity.Legendary); Require(item);
        ProjectPermanentInventory(); return [.. item.Events, "RoamingChampionRewardClaimed:" + id];
    }
    internal void RecordRoamingChampionVictory(string id)
    {
        var next = progression.Capture();
        string key = "roaming-champion.victory." + id;
        if (next.Character.OperationReceipts.ContainsKey(key)) return;
        next.Character.OperationReceipts.Add(key, JsonData.Hash(new { Action = "RoamingChampionVictory", Id = id }));
        progression.AdoptAuthoritativeState(next);
    }
    internal void ValidateRoamingChampionRewards(string[] claimed, string[] defeated)
    {
        var receipts = progression.CharacterState.OperationReceipts;
        var expectedVictories = defeated.Select(id => "roaming-champion.victory." + id).ToHashSet(StringComparer.Ordinal);
        if (!expectedVictories.SetEquals(receipts.Keys.Where(k => k.StartsWith("roaming-champion.victory.", StringComparison.Ordinal))) ||
            defeated.Any(id => receipts["roaming-champion.victory." + id] != JsonData.Hash(new { Action = "RoamingChampionVictory", Id = id })))
            throw new InvalidDataException("Roaming champion victories differ from permanent receipts.");
        var expected = claimed.Select(RoamingRewardKey).ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(receipts.Keys.Where(k => k.StartsWith("roaming-champion.reward.", StringComparison.Ordinal))))
            throw new InvalidDataException("Roaming champion treasure ledger differs from permanent receipts.");
        foreach (string id in claimed)
        {
            var definition = RoamingChampionCatalog.Find(id) ?? throw new InvalidDataException("Unknown roaming champion reward.");
            if (receipts[RoamingRewardKey(id)] != JsonData.Hash(new { Action = "GrantItem", definitionId = definition.RewardItemId, rarity = ItemRarity.Legendary, Affixes = new SortedDictionary<string, int>() }))
                throw new InvalidDataException("Roaming champion treasure receipt payload differs from its champion.");
        }
    }
}
