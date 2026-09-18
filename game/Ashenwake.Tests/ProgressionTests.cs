using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class ProgressionTests
{
    [Fact]
    public void WeightedAffixGenerationIsBoundedDeterministicAndEligibleAcrossThousandsOfInstances()
    {
        var content = ProgressionContent.Default(); var definition = content.Capture(); int advanced = 0;
        for (long id = 1; id <= 3000; id++)
        {
            var item = definition.Items[(int)(id % definition.Items.Length)];
            if (item.Id == "item.ashcleaver") continue;
            var rarity = item.Property != "" ? ItemRarity.Legendary : (ItemRarity)(id % 5);
            var affixes = ProgressionLoot.RollAffixes(content, item.Id, rarity, 42, id);
            Assert.Equal(JsonData.Hash(affixes), JsonData.Hash(ProgressionLoot.RollAffixes(content, item.Id, rarity, 42, id)));
            ProgressionSession.ValidateItem(content, new() { Id = id, DefinitionId = item.Id, Rarity = rarity, Affixes = affixes });
            advanced += affixes.Keys.Count(key => definition.Affixes.Single(a => a.Id == key).Advanced);
        }
        Assert.True(advanced > 0);
        var session = Session();
        session.GrantItem("fork", "item.starter_mainhand", ItemRarity.Relic, new Dictionary<string, int> { ["affix.fork"] = 1 });
        session.GrantItem("chain", "item.starter_offhand", ItemRarity.Relic, new Dictionary<string, int> { ["affix.chain"] = 1 });
        Assert.True(session.Equip("equip.fork", 1, EquipmentSlot.MainHand).Success);
        string before = session.StateHash; Assert.False(session.Equip("equip.chain", 2, EquipmentSlot.OffHand).Success); Assert.Equal(before, session.StateHash);
    }

    private static ProgressionSession Session(string discipline = "Vanguard") => ProgressionSession.Create(ProgressionContent.Default(), discipline);
    [Fact]
    public void DerivedStatsRemainDetachedAndRefreshAfterEquipmentAndRestoration()
    {
        var content = ProgressionContent.Default();
        var session = ProgressionSession.Create(content);
        Assert.True(session.GrantItem("weapon", "item.starter_mainhand", ItemRarity.Tempered, new Dictionary<string, int> { ["affix.damage"] = 2 }).Success);
        Assert.True(session.Equip("equip", 1, EquipmentSlot.MainHand).Success);
        var before = session.View;
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)before.Stats)["affix.damage"] = 999);
        Assert.True(session.Unequip("unequip", EquipmentSlot.MainHand).Success);
        Assert.Equal(2, before.Stats["affix.damage"]);
        Assert.Empty(session.View.Stats);
        var restored = ProgressionSession.Restore(content, session.Capture());
        Assert.True(restored.Equip("reequip", 1, EquipmentSlot.MainHand).Success);
        Assert.Equal(2, restored.View.Stats["affix.damage"]);
    }

    private static void UnlockServices(ProgressionSession session)
    {
        foreach (string id in new[] { "mara", "torren", "cael", "oris", "kesh", "haven" }) Assert.True(session.CompleteObjective("rescue." + id, "objective." + id).Success);
        Assert.True(session.EarnExperience("materials", 0, 500).Success);
    }
    [Fact]
    public void AllTwelveEquipmentSlotsHaveIndependentItemsAndDerivedStats()
    {
        var session = Session();
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            Assert.True(session.GrantItem("grant." + slot, "item.starter_" + slot.ToString().ToLowerInvariant(), ItemRarity.Tempered, new Dictionary<string, int> { ["affix.resource"] = 2 }).Success);
            Assert.True(session.Equip("equip." + slot, session.Capture().Character.Items.Last().Id, slot).Success);
        }
        Assert.Equal(12, session.View.Equipment.Count); Assert.Equal(24, session.View.Stats["affix.resource"]);
        Assert.True(session.Unequip("remove.boots", EquipmentSlot.Boots).Success); Assert.Equal(22, session.View.Stats["affix.resource"]);
        var detached = session.Capture(); detached.Character.Items[0].Affixes["affix.resource"] = 20;
        Assert.Equal(22, session.View.Stats["affix.resource"]);
    }
    [Fact]
    public void RewardAndCraftingReceiptsBindTheEntirePayloadAcrossRestore()
    {
        var content = ProgressionContent.Default(); var session = Session();
        Assert.True(session.EarnExperience("kill.1", 100, 10).Success);
        string hash = session.StateHash;
        Assert.True(session.EarnExperience("kill.1", 100, 10).Success); Assert.Equal(hash, session.StateHash);
        Assert.False(session.EarnExperience("kill.1", 200, 10).Success); Assert.Equal(hash, session.StateHash);
        session = ProgressionSession.Restore(content, JsonData.Copy(session.Capture()));
        Assert.True(session.EarnExperience("kill.1", 100, 10).Success); Assert.Equal(100, session.View.Experience);
    }
    [Fact]
    public void LevelMasteryPassivesRetrainingAndLocalProfileRemainSeparate()
    {
        var session = Session(); Assert.False(session.Retrain("early", "Arcanist").Success);
        session.EarnExperience("level", 4500); Assert.Equal(10, session.Level);
        Assert.True(session.AllocatePassive("passive", "Offense").Success); Assert.Equal(8, session.View.AvailablePassivePoints);
        Assert.True(session.GainMastery("mastery", "skill.cleave", 100).Success); Assert.Single(session.View.MasteredSkills);
        Assert.True(session.Respec("respec").Success); Assert.Equal(9, session.View.AvailablePassivePoints); Assert.Single(session.View.MasteredSkills);
        Assert.True(session.Retrain("arcanist", "Arcanist").Success); Assert.Equal("Instability", session.View.Resource);
        Assert.Equal(2, session.View.UltimateSkills.Length);
        Assert.True(session.UnlockProfile("unlock", "profile.fractures").Success);
        var secondCharacter = ProgressionSession.Create(ProgressionContent.Default(), "Warden", "another", session.Capture().Profile);
        Assert.Equal(1, secondCharacter.Level); Assert.Contains("profile.fractures", secondCharacter.View.ProfileUnlocks);
        Assert.Empty(secondCharacter.Capture().Character.Mastery);
    }
    [Fact]
    public void WeaponHandsRarityAndAffixIncompatibilityRejectWithoutMutation()
    {
        var session = Session("Arcanist");
        Assert.True(session.GrantItem("off", "item.starter_offhand", ItemRarity.Common).Success);
        Assert.True(session.GrantItem("staff", "item.greatstaff", ItemRarity.Relic, new Dictionary<string, int> { ["affix.fork"] = 1 }).Success);
        Assert.True(session.Equip("equip.off", 1, EquipmentSlot.OffHand).Success);
        string before = session.StateHash; Assert.False(session.Equip("equip.staff", 2, EquipmentSlot.MainHand).Success); Assert.Equal(before, session.StateHash);
        session.Unequip("remove.off", EquipmentSlot.OffHand); Assert.True(session.Equip("equip.staff", 2, EquipmentSlot.MainHand).Success);
        Assert.False(session.Equip("replace.off", 1, EquipmentSlot.OffHand).Success);
        Assert.False(session.GrantItem("bad", "item.greatstaff", ItemRarity.Relic, new Dictionary<string, int> { ["affix.fork"] = 1, ["affix.chain"] = 1 }).Success);
        Assert.False(session.GrantItem("common.affix", "item.starter_head", ItemRarity.Common, new Dictionary<string, int> { ["affix.armor"] = 2 }).Success);
        Assert.False(session.GrantItem("bad.rarity", "item.echo_ring", ItemRarity.Common).Success);
    }
    [Fact]
    public void AllCraftingServicesCommitCostsResultsAndPermanentConsumptionTogether()
    {
        var content = ProgressionContent.Default(); var session = Session(); UnlockServices(session);
        session.GrantItem("weapon", "item.starter_mainhand", ItemRarity.Rare, new Dictionary<string, int> { ["affix.damage"] = 2 });
        Assert.True(session.Craft(new("temper", CraftingService.Tempering, 1, AffixId: "affix.damage")).Success);
        Assert.True(session.Craft(new("rebind", CraftingService.Rebinding, 1, AffixId: "affix.damage", ReplacementId: "affix.resource")).Success);
        Assert.True(session.Craft(new("engrave", CraftingService.Engraving, 1, PropertyId: "rune.guard")).Success);
        session.GrantItem("legendary", "item.echo_ring", ItemRarity.Legendary); session.Equip("ring", 2, EquipmentSlot.Ring1);
        var extraction = new CraftingRequest("extract", CraftingService.Extraction, 2, ConfirmPermanent: true);
        Assert.True(session.Craft(extraction).Success); Assert.DoesNotContain(session.Capture().Character.Items, i => i.Id == 2); Assert.Empty(session.View.Equipment);
        Assert.Contains("property.summon_burst", session.Capture().Character.PropertyLibrary);
        string hash = session.StateHash; Assert.True(session.Craft(extraction).Success); Assert.Equal(hash, session.StateHash);
        Assert.False(session.Craft(extraction with { ItemId = 1 }).Success); Assert.Equal(hash, session.StateHash);
        session.GrantItem("godwrought", "item.ashcleaver", ItemRarity.Godwrought); session.Equip("axe", 3, EquipmentSlot.MainHand);
        var state = session.Capture(); state.Character.Items.Single(i => i.Id == 3).BurningKills = 999;
        Assert.False(state.Character.Items.Single(i => i.Id == 3).Awakened);
        session = ProgressionSession.Restore(content, state); Assert.True(session.RecordGodwroughtKill("burning.kill.1000", 3).Success);
        Assert.True(session.Capture().Character.Items.Single(i => i.Id == 3).Awakened);
        Assert.DoesNotContain("awakened", JsonData.Write(session.Capture()), StringComparison.OrdinalIgnoreCase);
        Assert.True(session.Craft(new("graft", CraftingService.DivineGrafting, 3, Lineage: "Orrun", ConfirmPermanent: true)).Success);
        Assert.False(session.Craft(new("regraft", CraftingService.DivineGrafting, 3, Lineage: "Serath", ConfirmPermanent: true)).Success);
        Assert.True(session.Craft(new("purify", CraftingService.Purification, FragmentId: "fragment.eye_vael")).Success);
        Assert.False(session.Craft(new("repurify", CraftingService.Purification, FragmentId: "fragment.eye_vael")).Success);
        Assert.Equal(483, session.View.Materials);
        Assert.Equal("Orrun", session.Capture().Character.Items.Single(i => i.Id == 3).Evolution);
    }
    [Fact]
    public void InvalidCraftDoesNotSpendMaterialsAndQuestGraphCannotSoftlock()
    {
        var content = ProgressionContent.Default(); var session = Session();
        Assert.False(session.CompleteObjective("later", "objective.haven").Success);
        UnlockServices(session); Assert.Equal(3, session.View.HubStage); Assert.Equal(6, session.View.Services.Length);
        session.GrantItem("armor", "item.starter_head", ItemRarity.Rare, new Dictionary<string, int> { ["affix.armor"] = 20 });
        string before = session.StateHash;
        Assert.False(session.Craft(new("invalid.rebind", CraftingService.Rebinding, 1, AffixId: "affix.armor", ReplacementId: "affix.damage")).Success);
        Assert.Equal(before, session.StateHash);
        var d = content.Capture();
        d.Objectives[0] = d.Objectives[0] with { Requires = ["objective.haven"] };
        Assert.Throws<InvalidDataException>(() => ProgressionContent.Create(d));
        d = content.Capture(); d.Strings.Remove("journal.mara"); Assert.Throws<InvalidDataException>(() => ProgressionContent.Create(d));
        var s = session.Capture(); s.Character.Services.Clear(); Assert.Throws<InvalidDataException>(() => ProgressionSession.Restore(content, s));
    }
}
