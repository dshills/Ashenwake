using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Production;

public sealed partial class ProductionSession
{
    internal bool CanClaimRegionalHunt => progression.CharacterState.Items.Length < 10512 && progression.CharacterState.NextItemId < long.MaxValue && CanReserveEndgameRun;
    private static string RegionalRewardKey(long id) => "regional-hunt.reward." + id;
    internal string[] GrantRegionalHunt(RegionalHuntReceipt receipt)
    {
        var contract = RegionalHuntCatalog.Find(receipt.ContractId) ?? throw new InvalidDataException("Unknown regional hunt reward.");
        string key = RegionalRewardKey(receipt.RunId);
        if (progression.CharacterState.OperationReceipts.ContainsKey(key)) throw new InvalidDataException("Regional hunt reward was already claimed.");
        SynchronizeItemSequence();
        var item = progression.GrantItem(key + ".item", contract.RewardItemId, ItemRarity.Legendary); Require(item);
        var materials = progression.EarnExperience(key + ".materials", 0, contract.Materials); Require(materials);
        var next = progression.Capture(); next.Character.OperationReceipts.Add(key, JsonData.Hash(receipt));
        progression.AdoptAuthoritativeState(next); ProjectPermanentInventory();
        return [.. item.Events, .. materials.Events, "RegionalHuntRewardClaimed:" + receipt.ContractId];
    }
    internal void ValidateRegionalHuntRewards(RegionalHuntReceipt[] rewards)
    {
        var receipts = progression.CharacterState.OperationReceipts;
        var expected = new HashSet<string>(StringComparer.Ordinal);
        bool Matches(string key, object payload) => receipts.TryGetValue(key, out var hash) && hash == JsonData.Hash(payload);
        foreach (var reward in rewards)
        {
            var contract = RegionalHuntCatalog.Find(reward.ContractId) ?? throw new InvalidDataException("Unknown regional reward contract.");
            string key = RegionalRewardKey(reward.RunId); expected.UnionWith([key, key + ".item", key + ".materials"]);
            if (!Matches(key, reward) || !Matches(key + ".item", new { Action = "GrantItem", definitionId = contract.RewardItemId, rarity = ItemRarity.Legendary, Affixes = new SortedDictionary<string, int>() }) ||
                !HasCampaignReward(key + ".materials", 0, contract.Materials))
                throw new InvalidDataException("Regional hunt reward differs from permanent receipts.");
        }
        if (!expected.SetEquals(receipts.Keys.Where(k => k.StartsWith("regional-hunt.reward.", StringComparison.Ordinal))))
            throw new InvalidDataException("Regional hunt receipt lacks its reward ledger.");
    }
}
