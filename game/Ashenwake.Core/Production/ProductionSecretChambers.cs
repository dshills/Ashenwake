using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Production;

public sealed partial class ProductionSession
{
    internal bool CanClaimSecretTreasure => CanClaimRegionalHunt;
    private static string SecretRewardKey(string id) => "secret-chamber.reward." + id;
    internal string[] GrantSecretChamber(string id)
    {
        var definition = SecretChamberCatalog.Find(id) ?? throw new InvalidDataException("Unknown secret chamber.");
        string key = SecretRewardKey(id);
        if (progression.CharacterState.OperationReceipts.ContainsKey(key)) throw new InvalidDataException("Secret treasure was already claimed.");
        SynchronizeItemSequence();
        var item = progression.GrantItem(key, definition.RewardItemId, ItemRarity.Legendary); Require(item);
        ProjectPermanentInventory(); return [.. item.Events, "SecretTreasureClaimed:" + id];
    }
    internal void RecordSecretGuardianVictory(string id)
    {
        var next = progression.Capture();
        string key = "secret-chamber.victory." + id;
        if (next.Character.OperationReceipts.ContainsKey(key)) return;
        next.Character.OperationReceipts.Add(key, JsonData.Hash(new { Action = "SecretGuardianVictory", Id = id }));
        progression.AdoptAuthoritativeState(next);
    }
    internal void ValidateSecretChamberRewards(string[] claimed, string[] defeated)
    {
        var receipts = progression.CharacterState.OperationReceipts;
        var expectedVictories = defeated.Select(id => "secret-chamber.victory." + id).ToHashSet(StringComparer.Ordinal);
        if (!expectedVictories.SetEquals(receipts.Keys.Where(k => k.StartsWith("secret-chamber.victory.", StringComparison.Ordinal))) ||
            defeated.Any(id => receipts["secret-chamber.victory." + id] != JsonData.Hash(new { Action = "SecretGuardianVictory", Id = id })))
            throw new InvalidDataException("Secret guardian victories differ from permanent receipts.");
        var expected = claimed.Select(SecretRewardKey).ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(receipts.Keys.Where(k => k.StartsWith("secret-chamber.reward.", StringComparison.Ordinal))))
            throw new InvalidDataException("Secret treasure ledger differs from permanent receipts.");
        foreach (string id in claimed)
        {
            var definition = SecretChamberCatalog.Find(id) ?? throw new InvalidDataException("Unknown secret reward.");
            if (receipts[SecretRewardKey(id)] != JsonData.Hash(new { Action = "GrantItem", definitionId = definition.RewardItemId, rarity = ItemRarity.Legendary, Affixes = new SortedDictionary<string, int>() }))
                throw new InvalidDataException("Secret treasure receipt payload differs from its chamber.");
        }
    }
}
