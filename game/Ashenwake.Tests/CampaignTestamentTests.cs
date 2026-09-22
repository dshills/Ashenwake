using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CampaignTestamentTests
{
    private const string Pacing = "campaign.pacing.7";
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static readonly Lazy<ProgressionContent> Resolved = new(() =>
        ProductionContent.Resolve(Read("combat.json"), ProgressionContent.Parse(Read("progression.json"))));
    private static ProgressionSession Session(string discipline = "Vanguard") => ProgressionSession.Create(Resolved.Value, discipline);
    private static void Success(ProgressionResult result) => Assert.True(result.Success, result.Reason);

    public static TheoryData<string, string, ItemRarity, EquipmentSlot, string, int, int> Rewards => new()
    {
        { "campaign.crypt.testament", "item.serath_shroud", ItemRarity.Rare, EquipmentSlot.Chest, "affix.armor", 150, 3 },
        { "campaign.briar.testament", "item.stone_seal", ItemRarity.Rare, EquipmentSlot.Amulet, "affix.critical", 500, 5 },
        { "campaign.foundry.testament", "item.cinder_edge", ItemRarity.Rare, EquipmentSlot.MainHand, "affix.damage", 8, 6 },
        { "campaign.archive.testament", "item.oath_plate", ItemRarity.Rare, EquipmentSlot.Chest, "affix.armor", 350, 8 },
        { "campaign.vault.testament", "item.echo_ring", ItemRarity.Legendary, EquipmentSlot.Ring1, "affix.critical", 1200, 12 }
    };

    [Theory]
    [MemberData(nameof(Rewards))]
    public void AuthoredRewardIsWearableAndTemperableForEveryDisciplineAndNeverReissuedAfterCrafting(
        string receipt, string definition, ItemRarity rarity, EquipmentSlot slot, string affix, int amount, int resource)
    {
        foreach (string discipline in CombatSession.Disciplines)
        {
            var session = Session(discipline);
            Success(CampaignTestaments.Grant(session, receipt, Pacing));
            var reward = Assert.Single(session.Capture().Character.Items);
            Assert.Equal(definition, reward.DefinitionId); Assert.Equal(rarity, reward.Rarity);
            Assert.Equal(2, reward.Affixes.Count); Assert.Equal(amount, reward.Affixes[affix]); Assert.Equal(resource, reward.Affixes["affix.resource"]);
            Success(session.Equip("equip", reward.Id, slot));
            Success(session.CompleteObjective("mara", "objective.mara")); Success(session.CompleteObjective("torren", "objective.torren"));
            int materials = session.View.Materials;
            Success(session.Craft(new("temper", CraftingService.Tempering, reward.Id, AffixId: affix)));
            Assert.Equal(materials - 5, session.View.Materials);
            Assert.True(Assert.Single(session.Capture().Character.Items).Affixes[affix] > amount);
            session = ProgressionSession.Restore(Resolved.Value, session.Capture());
            string crafted = session.StateHash;
            Success(CampaignTestaments.Grant(session, receipt, Pacing));
            Assert.Equal(crafted, session.StateHash); Assert.True(CampaignTestaments.HasReceipt(session.Capture().Character.OperationReceipts, receipt));
        }
    }

    [Theory]
    [MemberData(nameof(Rewards))]
    public void CombatDepthReleasesUseThePublishedPacingGrantWithoutChangingItsReceipt(
        string receipt, string definition, ItemRarity rarity, EquipmentSlot slot, string affix, int amount, int resource)
    {
        _ = definition; _ = rarity; _ = slot; _ = affix; _ = amount; _ = resource;
        var published = Session();
        Success(CampaignTestaments.Grant(published, receipt, Pacing));
        foreach (string version in new[] { "campaign.opening_depth.8", "campaign.midgame_depth.9" })
        {
            var current = Session();
            Success(CampaignTestaments.Grant(current, receipt, version));
            Assert.Equal(published.StateHash, current.StateHash);
        }
    }

    [Theory]
    [MemberData(nameof(Rewards))]
    public void ExactPublishedLegacyGrantRemainsUnchangedAcrossUpgradeAndDoesNotReplaceDiscardedEquipment(
        string receipt, string definition, ItemRarity rarity, EquipmentSlot slot, string affix, int amount, int resource)
    {
        _ = slot; _ = affix; _ = amount; _ = resource;
        var session = Session();
        Success(session.GrantItem(receipt, definition, rarity)); // Published pre-pacing empty-affix payload.
        var old = Assert.Single(session.Capture().Character.Items); Assert.Empty(old.Affixes);
        string original = session.StateHash;
        Success(CampaignTestaments.Grant(session, receipt, Pacing));
        Assert.Equal(original, session.StateHash); Assert.True(CampaignTestaments.HasReceipt(session.Capture().Character.OperationReceipts, receipt));
        Success(session.Discard("discard", old.Id, confirmPermanent: true));
        session = ProgressionSession.Restore(Resolved.Value, session.Capture()); string discarded = session.StateHash;
        Success(CampaignTestaments.Grant(session, receipt, Pacing));
        Assert.Equal(discarded, session.StateHash); Assert.Empty(session.Capture().Character.Items);
    }

    [Theory]
    [InlineData("campaign.greybox.1")]
    [InlineData("campaign.grey_march.2")]
    [InlineData("campaign.verdant.3")]
    [InlineData("campaign.cinder.4")]
    [InlineData("campaign.spine.5")]
    [InlineData("campaign.hollow.6")]
    public void PublishedCatalogsRetainTheOriginalPreClaimGrant(string version)
    {
        var session = Session();
        Success(CampaignTestaments.Grant(session, "campaign.crypt.testament", version));
        Assert.Empty(Assert.Single(session.Capture().Character.Items).Affixes);
        var published = Session(); Success(published.GrantItem("campaign.crypt.testament", "item.serath_shroud", ItemRarity.Rare));
        Assert.Equal(published.StateHash, session.StateHash);
    }

    [Theory]
    [InlineData("campaign.pacing.8")]
    [InlineData("campaign.unknown.1")]
    public void UnpublishedRewardPoliciesCannotSilentlySelectLegacyOrCurrentGrants(string version)
    {
        var session = Session(); string before = session.StateHash;
        Assert.Throws<InvalidDataException>(() => CampaignTestaments.Grant(session, "campaign.crypt.testament", version));
        Assert.Equal(before, session.StateHash);
    }

    [Theory]
    [MemberData(nameof(Rewards))]
    public void WrongDefinitionRarityAffixValueAndReceiptIdentityAreRejectedWithoutChangingOwnership(
        string receipt, string definition, ItemRarity rarity, EquipmentSlot slot, string affix, int amount, int resource)
    {
        _ = slot;
        string Payload(string definitionId, ItemRarity changedRarity, SortedDictionary<string, int> affixes)
            => JsonData.Hash(new { Action = "GrantItem", definitionId, rarity = changedRarity, Affixes = affixes });
        var correct = new SortedDictionary<string, int> { [affix] = amount, ["affix.resource"] = resource };
        string[] forged =
        [
            new('A', 64),
            Payload("item.starter_mainhand", rarity, correct),
            Payload(definition, ItemRarity.Relic, correct),
            Payload(definition, rarity, new() { [affix] = amount + 1, ["affix.resource"] = resource }),
            Payload(definition, rarity, new() { [affix] = amount }),
            Payload(definition, rarity, new() { ["affix.resource"] = resource }),
            JsonData.Hash(new { Action = "Experience", amount = 0, materials = 25 })
        ];
        foreach (string fingerprint in forged)
        {
            var snapshot = Session().Capture(); snapshot.Character.OperationReceipts[receipt] = fingerprint;
            var session = ProgressionSession.Restore(Resolved.Value, snapshot); string before = session.StateHash;
            Assert.False(CampaignTestaments.HasReceipt(snapshot.Character.OperationReceipts, receipt));
            Assert.False(CampaignTestaments.Grant(session, receipt, Pacing).Success);
            Assert.Equal(before, session.StateHash); Assert.Empty(session.Capture().Character.Items);
        }
        var valid = Session(); Success(CampaignTestaments.Grant(valid, receipt, Pacing));
        string otherReceipt = receipt == "campaign.crypt.testament" ? "campaign.vault.testament" : "campaign.crypt.testament";
        var misplaced = new Dictionary<string, string> { [otherReceipt] = valid.Capture().Character.OperationReceipts[receipt] };
        Assert.False(CampaignTestaments.HasReceipt(misplaced, otherReceipt));
    }

    [Fact]
    public void VaultRingIsAnImprovedStatVariantAndItsClaimCannotReplaceAnExtractedReward()
    {
        var session = Session();
        Success(session.GrantItem("campaign.kesh.legendary", "item.echo_ring", ItemRarity.Legendary));
        Success(CampaignTestaments.Grant(session, "campaign.vault.testament", Pacing));
        var items = session.Capture().Character.Items;
        Assert.Equal(2, items.Length); Assert.Equal(items[0].DefinitionId, items[1].DefinitionId);
        Assert.Empty(items[0].Affixes); Assert.Equal(1200, items[1].Affixes["affix.critical"]); Assert.Equal(12, items[1].Affixes["affix.resource"]);
        Assert.Equal(items[0].BaseCriticalBasisPoints, items[1].BaseCriticalBasisPoints);
        Success(session.CompleteObjective("mara", "objective.mara")); Success(session.CompleteObjective("torren", "objective.torren"));
        Success(session.CompleteObjective("kesh", "objective.kesh"));
        Success(session.Craft(new("extract", CraftingService.Extraction, items[1].Id, ConfirmPermanent: true)));
        session = ProgressionSession.Restore(Resolved.Value, session.Capture()); string extracted = session.StateHash;
        Success(CampaignTestaments.Grant(session, "campaign.vault.testament", Pacing));
        Assert.Equal(extracted, session.StateHash); Assert.Single(session.Capture().Character.Items);
        Assert.Contains("property.summon_burst", session.Capture().Character.PropertyLibrary);
    }

    [Fact]
    public void HistoricalCryptClaimAndReplayRetainTheirPublishedRewardAfterCurrentSaveUpgrade()
    {
        string legacyCombat = CampaignCombatContent.Parse(Read("combat.json"), Read("fixtures/campaign-combat-hollow.json")).CombatJson;
        var legacyCampaign = CampaignContent.Parse(Read("fixtures/campaign-hollow.json"));
        var adventure = AdventureContent.Parse(Read("adventure.json")); var policy = ProgressionContent.Parse(Read("progression.json"));
        var session = CampaignRuntimeSession.Create(legacyCombat, adventure, policy, legacyCampaign);
        for (int i = 0; i < 10000 && !session.Capture().Campaign.CompletedExploration.Contains(CampaignRuntimeSession.CryptEvent); i++)
        {
            var result = session.Execute(CampaignRuntimeSmoke.Next(session)); Assert.True(result.Success, result.Reason);
        }
        Assert.Contains(CampaignRuntimeSession.CryptEvent, session.Capture().Campaign.CompletedExploration);
        var legacy = session.Capture();
        var reward = Assert.Single(legacy.Production.Progression.Character.Items, item => item.DefinitionId == "item.serath_shroud" && item.Rarity == ItemRarity.Rare && item.Affixes.Count == 0);
        Assert.Contains(session.CaptureReplay().Frames, frame => frame.Command.Action == CampaignRuntimeAction.InteractOpening && frame.Command.Id == "opening.crypt.treasure");
        Assert.True(CampaignRuntimeReplayRunner.Run(legacyCombat, adventure, policy, legacyCampaign, session.CaptureReplay()).Success);
        string currentCombat = CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson;
        var campaign = CampaignContent.Parse(Read("campaign.json"));
        var upgraded = CampaignRuntimeSaveStore.Read(currentCombat, adventure, policy, campaign,
            JsonData.Write(new CampaignRuntimeSave(1, session.StateHash, legacy)));
        var permanent = upgraded.Capture().Production.Progression.Character;
        Assert.Equal(JsonData.Hash(reward), JsonData.Hash(Assert.Single(permanent.Items, item => item.Id == reward.Id)));
        Assert.Equal(legacy.Production.Progression.Character.OperationReceipts["campaign.crypt.testament"], permanent.OperationReceipts["campaign.crypt.testament"]);
        Assert.True(CampaignTestaments.HasReceipt(permanent.OperationReceipts, "campaign.crypt.testament"));
        var loaded = CampaignRuntimeSaveStore.Read(currentCombat, adventure, policy, campaign,
            JsonData.Write(new CampaignRuntimeSave(1, upgraded.StateHash, upgraded.Capture())));
        Assert.Equal(upgraded.StateHash, loaded.StateHash);
    }
}
