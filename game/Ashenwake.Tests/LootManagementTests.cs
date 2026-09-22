using System.Text.Json;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class LootManagementTests
{
    private static ProgressionContent Policy => ProgressionContent.Default();
    private static ProgressionSession Fresh() => ProgressionSession.Create(Policy);
    private static long Grant(ProgressionSession session, ItemRarity rarity = ItemRarity.Common, string definition = "item.starter_head")
    {
        long id = session.Capture().Character.NextItemId;
        Assert.True(session.GrantItem("grant." + id, definition, rarity).Success); return id;
    }

    [Theory]
    [InlineData(ItemRarity.Common, 1)]
    [InlineData(ItemRarity.Tempered, 2)]
    [InlineData(ItemRarity.Rare, 4)]
    [InlineData(ItemRarity.Relic, 6)]
    [InlineData(ItemRarity.Legendary, 10)]
    public void SalvagePreviewMatchesExactMaterialReturnAndDuplicateReceiptCannotAwardTwice(ItemRarity rarity, int expected)
    {
        var session = Fresh(); long item = Grant(session, rarity); string before = session.StateHash;
        var preview = session.PreviewSalvage(item); Assert.True(preview.Success); Assert.Equal(expected, preview.Materials); Assert.Equal(before, session.StateHash);
        Assert.False(session.Salvage("salvage", item).Success); Assert.Equal(before, session.StateHash);
        int materials = session.View.Materials;
        Assert.True(session.Salvage("salvage", item, true).Success); Assert.Equal(materials + expected, session.View.Materials); Assert.Empty(session.Capture().Character.Items);
        string once = session.StateHash; Assert.True(session.Salvage("salvage", item, true).Success); Assert.Equal(once, session.StateHash);
        Assert.False(session.Salvage("salvage.again", item, true).Success); Assert.Equal(once, session.StateHash);
        Assert.Equal(once, ProgressionSession.Restore(Policy, session.Capture()).StateHash);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EitherFlagProtectsEveryDestructiveOperationAndPreview(bool favorite, bool locked)
    {
        var session = Fresh(); long item = Grant(session, ItemRarity.Legendary, "item.echo_ring");
        foreach (string objective in new[] { "mara", "torren", "kesh" }) Assert.True(session.CompleteObjective(objective, "objective." + objective).Success);
        Assert.True(session.SetItemFavorite("favorite", item, favorite).Success); Assert.True(session.SetItemLocked("locked", item, locked).Success);
        string before = session.StateHash;
        Assert.False(session.Discard("discard", item, true).Success); Assert.False(session.Salvage("salvage", item, true).Success);
        Assert.False(session.PreviewSalvage(item).Success);
        var extraction = new CraftingRequest("extract", CraftingService.Extraction, item, ConfirmPermanent: true);
        Assert.False(session.Craft(extraction).Success); Assert.False(session.PreviewCraft(extraction).Success); Assert.Equal(before, session.StateHash);
        Assert.True(session.Equip("equip", item, EquipmentSlot.Ring1).Success);
        Assert.True(session.SetItemFavorite("unfavorite", item, false).Success); Assert.True(session.SetItemLocked("unlock", item, false).Success);
        Assert.True(session.Craft(extraction).Success); Assert.Empty(session.Capture().Character.Items);
    }

    [Fact]
    public void ProtectedItemsStillAcceptNonconsumingCrafting()
    {
        var session = Fresh();
        Assert.True(session.GrantItem("grant", "item.starter_head", ItemRarity.Tempered, new Dictionary<string, int> { ["affix.armor"] = 100 }).Success);
        long id = Assert.Single(session.Capture().Character.Items).Id;
        Assert.True(session.CompleteObjective("mara", "objective.mara").Success); Assert.True(session.CompleteObjective("torren", "objective.torren").Success);
        Assert.True(session.SetItemFavorite("favorite", id, true).Success); Assert.True(session.SetItemLocked("lock", id, true).Success);
        Assert.True(session.Craft(new("temper", CraftingService.Tempering, id, AffixId: "affix.armor")).Success);
        var item = Assert.Single(session.Capture().Character.Items); Assert.True(item.IsFavorite); Assert.True(item.IsLocked); Assert.True(item.Affixes["affix.armor"] > 100);
    }

    [Fact]
    public void PreviewReportsEveryPresetAndSalvagePreservesDiagnosableMissingReferences()
    {
        var session = Fresh(); long id = Grant(session); Assert.True(session.Equip("equip", id, EquipmentSlot.Head).Success);
        Assert.True(session.SaveEquipmentPreset("save1", "preset.1", "Iron Crown").Success); Assert.True(session.SaveEquipmentPreset("save2", "preset.2", "Ashen Warden").Success);
        Assert.False(session.PreviewSalvage(id).Success); Assert.True(session.Unequip("unequip", EquipmentSlot.Head).Success);
        Assert.Equal(new[] { "Iron Crown", "Ashen Warden" }, session.PreviewSalvage(id).PresetNames);
        Assert.True(session.Salvage("salvage", id, true).Success); session = ProgressionSession.Restore(Policy, session.Capture());
        Assert.Equal(2, session.EquipmentPresets.Count); Assert.False(session.PreviewEquipmentPreset("preset.1").Success);
        Assert.Equal(2, ProgressionSession.ItemPresetNames(session.Capture(), id).Length);
    }

    [Fact]
    public void EquippedGodwroughtAndMaterialOverflowNeverConsumeAnItem()
    {
        var session = Fresh(); long item = Grant(session); Assert.True(session.Equip("equip", item, EquipmentSlot.Head).Success);
        string before = session.StateHash; Assert.False(session.Salvage("equipped", item, true).Success); Assert.Equal(before, session.StateHash);
        Assert.True(session.Unequip("unequip", EquipmentSlot.Head).Success);
        long god = Grant(session, ItemRarity.Godwrought, "item.ashcleaver"); Grant(session, ItemRarity.Godwrought, "item.ashcleaver");
        before = session.StateHash; Assert.False(session.Salvage("god", god, true).Success); Assert.Equal(before, session.StateHash);
        var cap = session.Capture(); cap.Character.Materials = 1000000; session = ProgressionSession.Restore(Policy, cap);
        before = session.StateHash; Assert.False(session.PreviewSalvage(item).Success); Assert.False(session.Salvage("overflow", item, true).Success); Assert.Equal(before, session.StateHash);
        cap.Character.Materials = 999999; session = ProgressionSession.Restore(Policy, cap);
        Assert.True(session.Salvage("boundary", item, true).Success); Assert.Equal(1000000, session.View.Materials);
    }

    [Fact]
    public void LegacyItemsOmitFalseFlagsTrueFlagsPersistAndMalformedBooleanIsRejected()
    {
        var session = Fresh(); long id = Grant(session); string legacy = JsonData.Write(session.Capture());
        Assert.DoesNotContain("isFavorite", legacy); Assert.DoesNotContain("isLocked", legacy);
        Assert.Equal(session.StateHash, ProgressionSession.Restore(Policy, JsonData.Read<ProgressionSnapshot>(legacy)).StateHash);
        Assert.True(session.SetItemFavorite("favorite", id, true).Success); Assert.True(session.SetItemLocked("locked", id, true).Success);
        string saved = JsonData.Write(session.Capture()); Assert.Contains("\"isFavorite\":true", saved); Assert.Contains("\"isLocked\":true", saved);
        Assert.Equal(session.StateHash, ProgressionSession.Restore(Policy, JsonData.Read<ProgressionSnapshot>(saved)).StateHash);
        Assert.Throws<JsonException>(() => JsonData.Read<ProgressionSnapshot>(saved.Replace("\"isFavorite\":true", "\"isFavorite\":\"true\"")));
        Assert.True(session.SetItemFavorite("clear.favorite", id, false).Success); Assert.True(session.SetItemLocked("clear.lock", id, false).Success);
        string cleared = JsonData.Write(session.Capture().Character.Items); Assert.DoesNotContain("isFavorite", cleared); Assert.DoesNotContain("isLocked", cleared);
    }

    [Fact]
    public void FlagOperationsRejectUnknownOwnershipAndReceiptPayloadReuse()
    {
        var session = Fresh(); long item = Grant(session); string before = session.StateHash;
        Assert.False(session.SetItemFavorite("missing", item + 1, true).Success); Assert.False(session.SetItemLocked("missing2", -1, true).Success); Assert.Equal(before, session.StateHash);
        Assert.True(session.SetItemFavorite("flag", item, true).Success); before = session.StateHash;
        Assert.True(session.SetItemFavorite("flag", item, true).Success); Assert.False(session.SetItemFavorite("flag", item, false).Success); Assert.Equal(before, session.StateHash);
    }

    private static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent ShippedPolicy => ProgressionContent.Parse(Read("progression.json"));
    private static ProductionSession ProductionAtTorren()
    {
        var fresh = ProductionSession.Create(Read("combat.json"), Adventure, ShippedPolicy); var state = fresh.Capture();
        state.Expedition.Combat.Actors.Single(a => a.Id == 1).Position = new(2000, -2000);
        return ProductionSession.Restore(Read("combat.json"), Adventure, ShippedPolicy, state);
    }

    [Fact]
    public void ProductionSalvageUpdatesCombatProjectionPersistsAndReplays()
    {
        var session = ProductionAtTorren(); long item = session.ProgressionView.Equipment[EquipmentSlot.Head];
        Assert.True(session.SetItemFavorite(item, true).Success); Assert.True(session.SetItemLocked(item, true).Success);
        Assert.True(session.Unequip(EquipmentSlot.Head).Success); Assert.False(session.Salvage(item, true).Success);
        Assert.True(session.SetItemFavorite(item, false).Success); Assert.True(session.SetItemLocked(item, false).Success);
        int materials = session.ProgressionView.Materials, expected = session.PreviewSalvage(item).Materials;
        Assert.True(session.Salvage(item, true).Success); Assert.Equal(materials + expected, session.ProgressionView.Materials);
        Assert.DoesNotContain(session.Combat.View.Inventory, i => i.Id == item);
        var loaded = ProductionSaveStore.Read(Read("combat.json"), Adventure, ShippedPolicy, JsonData.Write(new ProductionSave(1, session.StateHash, session.Capture())));
        Assert.Equal(session.StateHash, loaded.StateHash);
        var replay = ProductionReplayRunner.Run(Read("combat.json"), Adventure, ShippedPolicy, session.CaptureReplay()); Assert.True(replay.Success, replay.Detail);
    }

    [Fact]
    public void OrganizationDoesNotRequireNearbyTorrenButSalvageDoesAndBothRejectDefeat()
    {
        var session = ProductionSession.Create(Read("combat.json"), Adventure, ShippedPolicy); long item = session.ProgressionView.Equipment[EquipmentSlot.Head];
        var snapshot = session.Capture(); snapshot.Expedition.Combat.Actors.Single(a => a.Id == 1).Position = new(-7000, -7000);
        session = ProductionSession.Restore(Read("combat.json"), Adventure, ShippedPolicy, snapshot);
        Assert.True(session.SetItemFavorite(item, true).Success); Assert.True(session.SetItemLocked(item, true).Success);
        string before = session.StateHash; Assert.False(session.PreviewSalvage(item).Success); Assert.Contains("Torren", session.PreviewSalvage(item).Reason);
        Assert.False(session.Salvage(item, true).Success); Assert.Equal(before, session.StateHash);
        foreach (string invalid in new[] { "True", "1", "", " false " })
        { Assert.False(session.Execute(new(ProductionAction.SetItemFavorite, ItemId: item, Value: invalid)).Success); Assert.Equal(before, session.StateHash); }
        snapshot = session.Capture(); snapshot.Expedition.Combat.Actors.Single(a => a.Id == 1).Health = 0; snapshot.Expedition.Combat.Actors.Single(a => a.Id == 1).DeathProcessed = true;
        session = ProductionSession.Restore(Read("combat.json"), Adventure, ShippedPolicy, snapshot); before = session.StateHash;
        Assert.False(session.SetItemFavorite(item, false).Success); Assert.False(session.SetItemLocked(item, false).Success); Assert.Equal(before, session.StateHash);
    }

    [Fact]
    public void CampaignOrganizationWorksBeforeRescueAndOutsideHubWithoutChangingCombatAndOuterReplayPersists()
    {
        var campaign = CampaignContent.Parse(Read("campaign.json")); var endgame = EndgameContent.Parse(Read("endgame.json"));
        string combat = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), endgame).CombatJson;
        var session = EndgameRuntimeSession.Create(combat, Adventure, ShippedPolicy, campaign, endgame);
        long item = session.Production.ProgressionView.Equipment[EquipmentSlot.Head];
        Assert.True(session.ExecuteProduction(new(ProductionAction.SetItemFavorite, ItemId: item, Value: "true")).Success);
        string before = session.StateHash; Assert.False(session.ExecuteProduction(new(ProductionAction.Salvage, ItemId: item, ConfirmPermanent: true)).Success); Assert.Equal(before, session.StateHash);
        Assert.True(session.Execute(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.EnterAct, Act: 1))).Success);
        Assert.False(session.InHub); string combatBefore = JsonData.Hash(session.Combat.Capture());
        Assert.True(session.ExecuteProduction(new(ProductionAction.SetItemLocked, ItemId: item, Value: "true")).Success);
        Assert.Equal(combatBefore, JsonData.Hash(session.Combat.Capture()));
        int owned = session.Production.Capture().Progression.Character.Items.Length;
        for (int step = 0; step < 1500 && session.Production.Capture().Progression.Character.Items.Length == owned; step++)
        { var result = session.Execute(EndgameRuntimeSmoke.Next(session)); Assert.True(result.Success, result.Reason); }
        Assert.True(session.Production.Capture().Progression.Character.Items.Length > owned);
        var retained = session.Production.Capture().Progression.Character.Items.Single(i => i.Id == item);
        Assert.True(retained.IsFavorite); Assert.True(retained.IsLocked);
        Assert.True(session.Campaign.ExecuteProduction(new(ProductionAction.SetItemLocked, ItemId: item, Value: "true")).Success);
        // Direct nested calls are intentionally excluded from the parent replay; start a fresh baseline.
        session = EndgameRuntimeSession.Restore(combat, Adventure, ShippedPolicy, campaign, endgame, session.Capture());
        Assert.True(session.ExecuteProduction(new(ProductionAction.SetItemFavorite, ItemId: item, Value: "false")).Success);
        Assert.True(session.ExecuteProduction(new(ProductionAction.SetItemFavorite, ItemId: item, Value: "true")).Success);
        var saved = new EndgameRuntimeSave(1, session.StateHash, session.Capture());
        Assert.Equal(session.StateHash, EndgameRuntimeSaveStore.Read(combat, Adventure, ShippedPolicy, campaign, endgame, JsonData.Write(saved)).StateHash);
        var replay = EndgameRuntimeReplayRunner.Run(combat, Adventure, ShippedPolicy, campaign, endgame, session.CaptureReplay()); Assert.True(replay.Success, replay.Detail);
    }
    [Fact]
    public void ActiveFractureOrganizationLeavesArenaUntouchedAndRejectsDefeatedCharacter()
    {
        var campaign = CampaignContent.Parse(Read("campaign.json")); var endgame = EndgameContent.Parse(Read("endgame.json"));
        string previous = CampaignCombatContent.Parse(Read("fixtures/combat-phase4.json"), Read("fixtures/campaign-combat-phase4.json")).CombatJson;
        string combat = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), endgame).CombatJson;
        var session = EndgameRuntimeMigration.ImportPhaseFour(Read("fixtures/phase4-campaign-complete.json"), previous, combat, Adventure, ShippedPolicy, campaign, endgame);
        void AtGate(EndgameRuntimeCommand command)
        {
            for (int step = 0; step < 500; step++)
            {
                var next = EndgameRuntimeSmoke.AtGate(session, command); var result = session.Execute(next); Assert.True(result.Success, result.Reason);
                if (next.Action != EndgameRuntimeAction.Tick) return;
            }
            Assert.Fail("Could not reach Fracture gate.");
        }
        AtGate(new(EndgameRuntimeAction.ClaimRecoverySigil));
        AtGate(new(EndgameRuntimeAction.StartFracture, session.View.AvailableSigils.Single().Id));
        long item = session.Production.Capture().Progression.Character.Items.First().Id;
        string arenaBefore = JsonData.Hash(session.Combat.Capture());
        Assert.True(session.ExecuteProduction(new(ProductionAction.SetItemFavorite, ItemId: item, Value: "true")).Success);
        Assert.True(session.ExecuteProduction(new(ProductionAction.SetItemLocked, ItemId: item, Value: "true")).Success);
        Assert.Equal(arenaBefore, JsonData.Hash(session.Combat.Capture()));
        Assert.Equal(session.StateHash, EndgameRuntimeSession.Restore(combat, Adventure, ShippedPolicy, campaign, endgame, session.Capture()).StateHash);
        var replay = EndgameRuntimeReplayRunner.Run(combat, Adventure, ShippedPolicy, campaign, endgame, session.CaptureReplay()); Assert.True(replay.Success, replay.Detail);
        for (int step = 0; step < 5000 && !session.AwaitingRetry; step++) Assert.True(session.Step().Success);
        Assert.True(session.AwaitingRetry); string before = session.StateHash;
        Assert.False(session.ExecuteProduction(new(ProductionAction.SetItemFavorite, ItemId: item, Value: "false")).Success);
        Assert.False(session.ExecuteProduction(new(ProductionAction.SetItemLocked, ItemId: item, Value: "false")).Success); Assert.Equal(before, session.StateHash);
    }

}
