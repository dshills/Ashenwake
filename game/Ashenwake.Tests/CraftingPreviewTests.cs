using System.Text.Json;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CraftingPreviewTests
{
    private static readonly ProgressionContent Policy = ProgressionContent.Default();

    [Theory]
    [InlineData(CraftingService.Tempering)]
    [InlineData(CraftingService.Rebinding)]
    [InlineData(CraftingService.Engraving)]
    [InlineData(CraftingService.Extraction)]
    [InlineData(CraftingService.DivineGrafting)]
    [InlineData(CraftingService.Purification)]
    public void AllSixPreviewsMatchTheAuthoritativeCraftWithoutMutatingTheirSource(CraftingService service)
    {
        var session = Workshop(); var request = Recipe(service); string original = session.StateHash;
        var preview = session.PreviewCraft(request);
        Assert.True(preview.Success, preview.Reason); Assert.Empty(preview.Reason);
        Assert.Equal(service is CraftingService.Extraction or CraftingService.DivineGrafting, preview.RequiresConfirmation);
        Assert.Equal(original, session.StateHash); Assert.Equal(original, JsonData.Hash(preview.Before));
        Assert.Equal(preview.Before.Character.Materials - Policy.Capture().CraftingCosts[service], preview.After.Character.Materials);
        Assert.Single(preview.After.Character.OperationReceipts.Keys.Except(preview.Before.Character.OperationReceipts.Keys));
        Assert.Equal(preview.Before.Character.OperationReceipts.Count, session.Capture().Character.OperationReceipts.Count);
        switch (service)
        {
            case CraftingService.Tempering: Assert.Equal(4, preview.After.Character.Items.Single(i => i.Id == 1).Affixes["affix.damage"]); break;
            case CraftingService.Rebinding:
                Assert.DoesNotContain("affix.damage", preview.After.Character.Items.Single(i => i.Id == 1).Affixes.Keys);
                Assert.Equal(1, preview.After.Character.Items.Single(i => i.Id == 1).Affixes["affix.resource"]); break;
            case CraftingService.Engraving: Assert.Equal("rune.guard", preview.After.Character.Items.Single(i => i.Id == 1).Engraving); break;
            case CraftingService.Extraction:
                Assert.DoesNotContain(preview.After.Character.Items, i => i.Id == 2);
                Assert.DoesNotContain(EquipmentSlot.Ring1, preview.After.Character.Equipment.Keys);
                Assert.Contains("property.summon_burst", preview.After.Character.PropertyLibrary); break;
            case CraftingService.DivineGrafting: Assert.Equal("Orrun", preview.After.Character.Items.Single(i => i.Id == 3).Evolution); break;
            case CraftingService.Purification: Assert.Contains("fragment.eye_vael", preview.After.Character.PurifiedFragments); break;
        }
        var committed = session.Craft(request with { OperationId = "live." + service, ConfirmPermanent = true });
        Assert.True(committed.Success, committed.Reason);
        Assert.Equal(WithoutNewReceipts(preview.After, preview.Before), WithoutNewReceipts(session.Capture(), preview.Before));
    }

    [Theory]
    [InlineData(CraftingService.Extraction)]
    [InlineData(CraftingService.DivineGrafting)]
    public void DestructivePreviewsDoNotConfirmTheLiveTransaction(CraftingService service)
    {
        var session = Workshop(); var request = Recipe(service); string original = session.StateHash;
        var preview = session.PreviewCraft(request);
        Assert.True(preview.Success, preview.Reason); Assert.True(preview.RequiresConfirmation);
        Assert.False(request.ConfirmPermanent); Assert.False(session.Craft(request).Success);
        Assert.Equal(original, session.StateHash);
        Assert.True(session.PreviewCraft(request with { ConfirmPermanent = true }).RequiresConfirmation);
    }

    [Fact]
    public void BothReturnedSnapshotsAreDetachedFromEachOtherAndLiveState()
    {
        var session = Workshop(); string original = session.StateHash;
        var preview = session.PreviewCraft(Recipe(CraftingService.Tempering));
        string projected = JsonData.Hash(preview.After);
        preview.Before.Character.Items.Single(i => i.Id == 1).Affixes["affix.damage"] = 30;
        preview.Before.Character.Equipment.Clear(); preview.Before.Profile.Discoveries.Add("discovery.greyhaven");
        Assert.Equal(projected, JsonData.Hash(preview.After));
        preview.After.Character.Materials = 0; preview.After.Character.OperationReceipts.Clear();
        preview.After.Character.Items.Single(i => i.Id == 1).Affixes.Clear(); preview.After.Profile.Unlocks.Add("profile.fractures");
        Assert.Equal(original, session.StateHash);
    }

    [Fact]
    public void PreviewFindsAFreshReceiptEvenWhenItsPrefixAndRequestedIdWereAlreadyUsed()
    {
        var session = Workshop(); var request = Recipe(CraftingService.Tempering) with { OperationId = "materials" };
        for (int i = 0; i < 3; i++)
        {
            var preview = session.PreviewCraft(request);
            Assert.True(preview.Success, preview.Reason);
            string previewId = Assert.Single(preview.After.Character.OperationReceipts.Keys.Except(preview.Before.Character.OperationReceipts.Keys));
            Assert.True(session.EarnExperience(previewId, 0).Success);
        }
        string original = session.StateHash;
        var fresh = session.PreviewCraft(request);
        Assert.True(fresh.Success, fresh.Reason);
        Assert.Equal(4, fresh.After.Character.Items.Single(i => i.Id == 1).Affixes["affix.damage"]);
        Assert.Equal(original, session.StateHash);
        Assert.Equal(JsonData.Hash(fresh), JsonData.Hash(session.PreviewCraft(request)));
    }

    [Theory]
    [InlineData("item")]
    [InlineData("affix")]
    [InlineData("replacement")]
    [InlineData("engraving")]
    [InlineData("extraction")]
    [InlineData("lineage")]
    [InlineData("fragment")]
    [InlineData("catalyst-policy")]
    public void RejectedRecipePreviewsReturnTheExactCraftReasonAndUnchangedSnapshots(string scenario)
    {
        var session = Workshop();
        var request = scenario switch
        {
            "item" => Recipe(CraftingService.Tempering) with { ItemId = 999 },
            "affix" => Recipe(CraftingService.Tempering) with { AffixId = "affix.armor" },
            "replacement" => Recipe(CraftingService.Rebinding) with { ReplacementId = "affix.unknown" },
            "engraving" => Recipe(CraftingService.Engraving) with { PropertyId = "property.summon_burst" },
            "extraction" => Recipe(CraftingService.Extraction) with { ItemId = 1 },
            "lineage" => Recipe(CraftingService.DivineGrafting) with { Lineage = "Vael" },
            "fragment" => Recipe(CraftingService.Purification) with { FragmentId = "fragment.unknown" },
            _ => Recipe(CraftingService.Tempering) with { CatalystId = "material.vael_rib" }
        };
        AssertRejected(session, request);
    }

    [Fact]
    public void InvalidServiceValuesFollowCraftsStrictSerializationRejectionWithoutMutation()
    {
        var session = Workshop(); string original = session.StateHash;
        var request = new CraftingRequest("invalid", (CraftingService)999);
        Assert.Throws<JsonException>(() => session.PreviewCraft(request));
        Assert.Equal(original, session.StateHash);
        Assert.Throws<JsonException>(() => session.Craft(request));
        Assert.Equal(original, session.StateHash);
    }

    [Fact]
    public void LockedServiceAndInsufficientMaterialsRejectWithoutChangingTheSource()
    {
        AssertRejected(ProgressionSession.Create(Policy), Recipe(CraftingService.Tempering));
        foreach (var service in Enum.GetValues<CraftingService>())
        {
            var state = Workshop().Capture(); state.Character.Materials = 0;
            var session = ProgressionSession.Restore(Policy, state);
            var preview = AssertRejected(session, Recipe(service));
            Assert.Equal("Insufficient crafting materials.", preview.Reason);
        }
    }

    [Fact]
    public void InvalidRebindingCannotProjectAnIneligibleItemOrConsumeMaterials()
    {
        var session = Workshop();
        Assert.True(session.GrantItem("helmet", "item.starter_head", ItemRarity.Rare, new Dictionary<string, int> { ["affix.armor"] = 20 }).Success);
        AssertRejected(session, new("invalid-rebinding", CraftingService.Rebinding, 4, AffixId: "affix.armor", ReplacementId: "affix.damage"));
    }

    [Fact]
    public void CatalystTemperingProjectsTheSubstitutionWithoutSpendingCommonMaterials()
    {
        var (content, session, id) = EndgameWorkshop(materials: 0, catalystCount: 2);
        var request = new CraftingRequest("temper", CraftingService.Tempering, id, AffixId: "affix.damage", CatalystId: "material.vael_rib");
        string original = session.StateHash; var preview = session.PreviewCraft(request);
        Assert.True(preview.Success, preview.Reason); Assert.False(preview.RequiresConfirmation);
        Assert.Equal(0, preview.After.Character.Materials);
        Assert.Equal(1, preview.After.Character.Endgame!.Catalysts["material.vael_rib"]);
        Assert.Equal(1, preview.After.Character.Endgame.SpentCatalysts["material.vael_rib"]);
        Assert.Equal(2, preview.After.Character.Items.Single(i => i.Id == id).Affixes["affix.damage"]);
        Assert.Equal(original, session.StateHash);
        Assert.True(session.Craft(request).Success);
        Assert.Equal(WithoutNewReceipts(preview.After, preview.Before), WithoutNewReceipts(session.Capture(), preview.Before));
        Assert.Equal(JsonData.Hash(preview.After), ProgressionSession.Restore(content, preview.After).StateHash);
    }

    [Theory]
    [InlineData("Serath", "material.serath_memory")]
    [InlineData("Orrun", "material.orrun_oath")]
    public void EndgameGraftProjectsBothMaterialAndLineageCatalystPayment(string lineage, string catalyst)
    {
        var (_, session, id) = EndgameWorkshop(); string original = session.StateHash;
        var request = new CraftingRequest("graft", CraftingService.DivineGrafting, id, Lineage: lineage, CatalystId: catalyst);
        var preview = session.PreviewCraft(request);
        Assert.True(preview.Success, preview.Reason); Assert.True(preview.RequiresConfirmation);
        Assert.Equal(preview.Before.Character.Materials - 20, preview.After.Character.Materials);
        Assert.DoesNotContain(catalyst, preview.After.Character.Endgame!.Catalysts.Keys);
        Assert.Equal(1, preview.After.Character.Endgame.SpentCatalysts[catalyst]);
        Assert.Equal(lineage, preview.After.Character.Items.Single(i => i.Id == id).Evolution);
        Assert.Equal(original, session.StateHash);
        Assert.True(session.Craft(request with { ConfirmPermanent = true }).Success);
        Assert.Equal(WithoutNewReceipts(preview.After, preview.Before), WithoutNewReceipts(session.Capture(), preview.Before));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("material.orrun_oath")]
    [InlineData("material.serath_memory")]
    public void MissingUnselectedOrWrongLineageCatalystsCannotBeSpentInAPreview(string? catalyst)
    {
        var (content, session, id) = EndgameWorkshop();
        var state = session.Capture(); state.Character.Endgame!.Catalysts.Remove("material.serath_memory");
        session = ProgressionSession.Restore(content, state);
        AssertRejected(session, new("unavailable-catalyst", CraftingService.DivineGrafting, id, Lineage: "Serath", CatalystId: catalyst));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailureAfterCatalystValidationCannotConsumeTheProjectedOrLiveCatalyst(bool cappedTempering)
    {
        var (content, session, id) = EndgameWorkshop(materials: 0);
        var state = session.Capture();
        if (cappedTempering) state.Character.Items.Single(i => i.Id == id).Affixes["affix.damage"] = 10;
        session = ProgressionSession.Restore(content, state);
        var request = cappedTempering
            ? new CraftingRequest("capped", CraftingService.Tempering, id, AffixId: "affix.damage", CatalystId: "material.vael_rib")
            : new CraftingRequest("no-money", CraftingService.DivineGrafting, id, Lineage: "Serath", CatalystId: "material.serath_memory");
        var preview = AssertRejected(session, request);
        Assert.Equal(JsonData.Hash(preview.Before.Character.Endgame), JsonData.Hash(preview.After.Character.Endgame));
    }

    private static CraftingPreview AssertRejected(ProgressionSession session, CraftingRequest request)
    {
        string original = session.StateHash; var preview = session.PreviewCraft(request);
        Assert.False(preview.Success); Assert.NotEmpty(preview.Reason);
        Assert.Equal(original, JsonData.Hash(preview.Before)); Assert.Equal(original, JsonData.Hash(preview.After));
        Assert.Equal(original, session.StateHash); Assert.NotSame(preview.Before.Character, preview.After.Character);
        var actual = session.Craft(request with { OperationId = "rejected.live", ConfirmPermanent = true });
        Assert.False(actual.Success); Assert.Equal(actual.Reason, preview.Reason); Assert.Equal(original, session.StateHash);
        preview.Before.Character.Materials = 0;
        Assert.Equal(original, JsonData.Hash(preview.After)); Assert.Equal(original, session.StateHash);
        return preview;
    }

    private static string WithoutNewReceipts(ProgressionSnapshot value, ProgressionSnapshot before)
    {
        var detached = JsonData.Copy(value);
        detached.Character.OperationReceipts = new(before.Character.OperationReceipts);
        return JsonData.Hash(detached);
    }

    private static CraftingRequest Recipe(CraftingService service) => service switch
    {
        CraftingService.Tempering => new("craft", service, 1, AffixId: "affix.damage"),
        CraftingService.Rebinding => new("craft", service, 1, AffixId: "affix.damage", ReplacementId: "affix.resource"),
        CraftingService.Engraving => new("craft", service, 1, PropertyId: "rune.guard"),
        CraftingService.Extraction => new("craft", service, 2),
        CraftingService.DivineGrafting => new("craft", service, 3, Lineage: "Orrun"),
        _ => new("craft", service, FragmentId: "fragment.eye_vael")
    };

    private static ProgressionSession Workshop()
    {
        // The same public workshop-unlock sequence used by ProgressionTests, then its awakening fixture.
        var session = ProgressionSession.Create(Policy);
        foreach (string id in new[] { "mara", "torren", "cael", "oris", "kesh", "haven" })
            Assert.True(session.CompleteObjective("rescue." + id, "objective." + id).Success);
        Assert.True(session.EarnExperience("materials", 0, 500).Success);
        Assert.True(session.GrantItem("weapon", "item.starter_mainhand", ItemRarity.Rare, new Dictionary<string, int> { ["affix.damage"] = 2 }).Success);
        Assert.True(session.GrantItem("legendary", "item.echo_ring", ItemRarity.Legendary).Success);
        Assert.True(session.Equip("ring", 2, EquipmentSlot.Ring1).Success);
        Assert.True(session.GrantItem("godwrought", "item.ashcleaver", ItemRarity.Godwrought).Success);
        Assert.True(session.Equip("axe", 3, EquipmentSlot.MainHand).Success);
        var state = session.Capture(); state.Character.Items.Single(i => i.Id == 3).BurningKills = GodwroughtProgress.AwakeningKills;
        return ProgressionSession.Restore(Policy, state);
    }

    private static (ProgressionContent Content, ProgressionSession Session, long ItemId) EndgameWorkshop(int materials = 500, int catalystCount = 1)
    {
        // Reuse the canonical completed-campaign import and catalyst fixture pattern from EndgameRuntimeTests.
        static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
        var adventure = AdventureContent.Parse(Read("adventure.json"));
        var policy = ProgressionContent.Parse(Read("progression.json"));
        var campaign = CampaignContent.Parse(Read("campaign.json")); var endgame = EndgameContent.Parse(Read("endgame.json"));
        string previous = CampaignCombatContent.Parse(Read("fixtures/combat-phase4.json"), Read("fixtures/campaign-combat-phase4.json")).CombatJson;
        string composed = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), endgame).CombatJson;
        var imported = EndgameRuntimeMigration.ImportPhaseFour(Read("fixtures/phase4-campaign-complete.json"), previous, composed, adventure, policy, campaign, endgame);
        var state = imported.Production.Capture().Progression; long id = state.Character.NextItemId++;
        state.Character.Items = [.. state.Character.Items, new PermanentItem { Id = id, DefinitionId = "item.ashcleaver", Rarity = ItemRarity.Godwrought, BurningKills = GodwroughtProgress.AwakeningKills }];
        state.Character.Materials = materials;
        foreach (string catalyst in new[] { "material.vael_rib", "material.serath_memory", "material.orrun_oath" }) state.Character.Endgame!.Catalysts[catalyst] = catalystCount;
        return (imported.Production.Content, ProgressionSession.Restore(imported.Production.Content, state), id);
    }
}
