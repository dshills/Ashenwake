using System.Collections.ObjectModel;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Campaign;

/// <summary>Published one-time branch rewards. The catalog selects new claims; an existing
/// receipt keeps the original grant even after the player crafts or consumes its item.</summary>
public static class CampaignTestaments
{
    private sealed record Testament(string DefinitionId, ItemRarity Rarity, string FirstAffix, int FirstValue, int Resource);

    private static Testament Definition(string receipt) => receipt switch
    {
        "campaign.crypt.testament" => new("item.serath_shroud", ItemRarity.Rare, "affix.armor", 150, 3),
        "campaign.briar.testament" => new("item.stone_seal", ItemRarity.Rare, "affix.critical", 500, 5),
        "campaign.foundry.testament" => new("item.cinder_edge", ItemRarity.Rare, "affix.damage", 8, 6),
        "campaign.archive.testament" => new("item.oath_plate", ItemRarity.Rare, "affix.armor", 350, 8),
        "campaign.vault.testament" => new("item.echo_ring", ItemRarity.Legendary, "affix.critical", 1200, 12),
        _ => throw new ArgumentException("Unknown campaign testament receipt.", nameof(receipt))
    };

    public static bool UsesAuthoredAffixes(string campaignVersion) => campaignVersion switch
    {
        "campaign.greybox.1" or "campaign.grey_march.2" or "campaign.verdant.3" or
            "campaign.cinder.4" or "campaign.spine.5" or "campaign.hollow.6" => false,
        "campaign.pacing.7" or "campaign.opening_depth.8" or "campaign.midgame_depth.9" => true,
        _ => throw new InvalidDataException("Campaign testament policy is not declared for this catalog version.")
    };

    public static IReadOnlyDictionary<string, int> AuthoredAffixes(string receipt)
        => new ReadOnlyDictionary<string, int>(Affixes(Definition(receipt), authored: true));

    private static SortedDictionary<string, int> Affixes(Testament reward, bool authored)
        => authored ? new(StringComparer.Ordinal) { [reward.FirstAffix] = reward.FirstValue, ["affix.resource"] = reward.Resource }
            : new(StringComparer.Ordinal);

    private static string Fingerprint(Testament reward, bool authored)
        => JsonData.Hash(new { Action = "GrantItem", definitionId = reward.DefinitionId, rarity = reward.Rarity, Affixes = Affixes(reward, authored) });

    public static bool HasReceipt(IReadOnlyDictionary<string, string> receipts, string receipt)
    {
        var reward = Definition(receipt);
        return receipts.TryGetValue(receipt, out string? hash) &&
            (hash == Fingerprint(reward, authored: false) || hash == Fingerprint(reward, authored: true));
    }

    public static ProgressionResult Grant(ProgressionSession progression, string receipt, string campaignVersion)
    {
        var reward = Definition(receipt); bool authored = UsesAuthoredAffixes(campaignVersion);
        if (progression.CharacterState.OperationReceipts.TryGetValue(receipt, out string? hash))
        {
            if (hash == Fingerprint(reward, authored: false)) authored = false;
            else if (hash == Fingerprint(reward, authored: true)) authored = true;
            else return new(false, "Campaign testament receipt differs from its published reward.", []);
        }
        return progression.GrantItem(receipt, reward.DefinitionId, reward.Rarity, Affixes(reward, authored));
    }
}
