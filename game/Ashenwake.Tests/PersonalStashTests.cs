using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class PersonalStashTests
{
    private static ProgressionContent Policy => ProgressionContent.Default();
    private static ProgressionSession Fresh() => ProgressionSession.Create(Policy);
    private static long Grant(ProgressionSession s, string definition = "item.starter_head", ItemRarity rarity = ItemRarity.Common)
    { long id = s.Capture().Character.NextItemId; Assert.True(s.GrantItem("grant." + id, definition, rarity).Success); return id; }
    [Fact]
    public void UnusedStashIsOmittedAndReadOnlyViewDoesNotCreateIt()
    {
        var s = Fresh(); string before = s.StateHash;
        Assert.Null(s.Capture().Character.Stash); Assert.DoesNotContain("\"stash\"", JsonData.Write(s.Capture()));
        Assert.Equal(4, s.Stash.Tabs.Length); Assert.All(s.Stash.Tabs, t => Assert.Equal(128, t.Capacity));
        Assert.Equal(before, s.StateHash);
    }
    [Fact]
    public void StoreMoveRetrievePreservesIdentityMetadataAndProtection()
    {
        var s = Fresh(); long id = Grant(s, "item.echo_ring", ItemRarity.Legendary);
        Assert.True(s.SetItemFavorite("favorite", id, true).Success); Assert.True(s.SetItemLocked("lock", id, true).Success);
        string item = JsonData.Hash(s.Capture().Character.Items.Single()); long sequence = s.Capture().Character.NextItemId;
        Assert.True(s.StoreItem("store", id, "stash.3").Success); Assert.True(s.MoveStashedItem("move", id, "stash.4").Success);
        Assert.Equal("stash.4", CharacterStash.TabForItem(s.Capture().Character, id));
        Assert.Equal(item, JsonData.Hash(s.Capture().Character.Items.Single())); Assert.Equal(sequence, s.Capture().Character.NextItemId);
        Assert.True(s.RetrieveItem("retrieve", id).Success); Assert.False(CharacterStash.IsStored(s.Capture().Character, id));
        Assert.Equal(item, JsonData.Hash(s.Capture().Character.Items.Single())); Assert.Equal(sequence, s.Capture().Character.NextItemId);
        string hash = s.StateHash; Assert.True(s.RetrieveItem("retrieve", id).Success); Assert.Equal(hash, s.StateHash);
        Assert.False(s.RetrieveItem("retrieve.again", id).Success); Assert.Equal(hash, s.StateHash);
    }
    [Fact]
    public void StoredItemsCannotBeEquippedCraftedExtractedSalvagedOrDiscarded()
    {
        var s = Fresh(); long id = Grant(s, "item.echo_ring", ItemRarity.Legendary);
        foreach (string npc in new[] { "mara", "torren", "kesh" }) Assert.True(s.CompleteObjective(npc, "objective." + npc).Success);
        Assert.True(s.StoreItem("store", id, "stash.3").Success); string before = s.StateHash;
        Assert.False(s.Equip("equip", id, EquipmentSlot.Ring1).Success);
        Assert.False(s.Craft(new("extract", CraftingService.Extraction, id, ConfirmPermanent: true)).Success);
        Assert.False(s.Craft(new("temper", CraftingService.Tempering, id)).Success);
        Assert.False(s.PreviewSalvage(id).Success); Assert.False(s.Salvage("salvage", id, true).Success);
        Assert.False(s.Discard("discard", id, true).Success); Assert.Equal(before, s.StateHash);
    }
    [Fact]
    public void EvolvedAffixedEngravedGodwroughtKeepsEveryOwnedFieldThroughTransfers()
    {
        var s = Fresh(); long id = Grant(s, "item.ashcleaver", ItemRarity.Godwrought);
        // A validated boundary fixture represents an already awakened and crafted legacy item.
        var snapshot = s.Capture(); var item = snapshot.Character.Items.Single();
        item.BurningKills = 1000; item.Evolution = "Orrun"; item.Engraving = "rune.guard";
        item.Affixes["affix.damage"] = 2; item.IsFavorite = true; item.IsLocked = true;
        s = ProgressionSession.Restore(Policy, snapshot); string fields = JsonData.Hash(item);
        Assert.True(s.StoreItem("store", id, "stash.1").Success); s = ProgressionSession.Restore(Policy, s.Capture());
        Assert.True(s.MoveStashedItem("move", id, "stash.4").Success); Assert.True(s.RetrieveItem("retrieve", id).Success);
        Assert.Equal(fields, JsonData.Hash(s.Capture().Character.Items.Single()));
    }
    [Fact]
    public void RestoreRejectsStoredItemsForgedBackIntoActiveCombat()
    {
        var s = Restore(EarnedTorren.Value); long id = s.Production.ProgressionView.Equipment[EquipmentSlot.Head];
        var combatItem = s.Combat.View.Inventory.Single(i => i.Id == id); StoreHead(s); var snapshot = s.Capture();
        snapshot.Campaign.Production.Expedition.Combat.Inventory.Add(combatItem);
        snapshot.Campaign.Combat.Inventory.Add(combatItem);
        Assert.Throws<InvalidDataException>(() => Restore(snapshot));
    }
    [Fact]
    public void EquippedDepositIsRejectedAndSavedOutfitShowsStoredInsteadOfMissing()
    {
        var s = Fresh(); long id = Grant(s); Assert.True(s.Equip("equip", id, EquipmentSlot.Head).Success);
        Assert.True(s.SaveEquipmentPreset("preset", "preset.1", "The Old Crown").Success);
        string before = s.StateHash; Assert.False(s.StoreItem("bad", id, "stash.2").Success); Assert.Equal(before, s.StateHash);
        Assert.True(s.Unequip("unequip", EquipmentSlot.Head).Success); Assert.True(s.StoreItem("store", id, "stash.2").Success);
        var preview = s.PreviewEquipmentPreset("preset.1"); Assert.False(preview.Success); Assert.Contains("stored in Armor", preview.Reason); Assert.DoesNotContain("no longer owned", preview.Reason);
        Assert.Equal("The Old Crown", Assert.Single(Assert.Single(s.Stash.Stored).References).Name);
        before = s.StateHash; Assert.False(s.ApplyEquipmentPreset("apply", "preset.1").Success); Assert.Equal(before, s.StateHash);
        Assert.True(s.RetrieveItem("retrieve", id).Success); Assert.True(s.ApplyEquipmentPreset("apply.after", "preset.1").Success);
    }
    [Fact]
    public void TabsHaveAtomicCapacityAndNamesRemainBounded()
    {
        var s = Fresh();
        for (int i = 0; i < CharacterStash.TabCapacity; i++) { long item = Grant(s); Assert.True(s.StoreItem("store." + i, item, "stash.1").Success); }
        long overflow = Grant(s); string full = s.StateHash; Assert.False(s.StoreItem("overflow", overflow, "stash.1").Success); Assert.Equal(full, s.StateHash);
        Assert.True(s.StoreItem("other", overflow, "stash.2").Success); full = s.StateHash;
        Assert.False(s.MoveStashedItem("fullmove", overflow, "stash.1").Success); Assert.Equal(full, s.StateHash);
        Assert.False(s.RenameStashTab("empty", "stash.1", " ").Success);
        Assert.False(s.RenameStashTab("control", "stash.1", "bad\nname").Success);
        Assert.False(s.RenameStashTab("duplicate", "stash.1", "armor").Success); Assert.Equal(full, s.StateHash);
        Assert.True(s.RenameStashTab("rename", "stash.1", "  Winter's Arsenal  ").Success);
        Assert.Equal("Winter's Arsenal", s.Stash.Tabs[0].Name);
        Assert.Equal(s.StateHash, ProgressionSession.Restore(Policy, s.Capture()).StateHash);
    }
    [Fact]
    public void FullBackpackRetrievalFailsWithoutLosingStoredItem()
    {
        var s = Fresh(); long stored = Grant(s); Assert.True(s.StoreItem("store", stored, "stash.1").Success);
        for (int i = 0; i < CharacterStash.BackpackCapacity; i++) Grant(s);
        string full = s.StateHash; Assert.False(s.RetrieveItem("full", stored).Success); Assert.Equal(full, s.StateHash);
        Assert.True(s.StoreItem("free", s.Capture().Character.Items.Last().Id, "stash.2").Success);
        Assert.True(s.RetrieveItem("retrieve", stored).Success); Assert.Equal(512, s.Stash.BackpackCount);
    }
    [Fact]
    public void InvalidLocationsAndEquippedStoredOverlapAreRejectedOnRestore()
    {
        var s = Fresh(); long id = Grant(s); Assert.True(s.StoreItem("store", id, "stash.1").Success); var state = s.Capture();
        void Invalid(PersonalStashState stash)
        { var copy = JsonData.Copy(state); copy.Character.Stash = stash; Assert.Throws<InvalidDataException>(() => ProgressionSession.Restore(Policy, copy)); }
        Invalid(state.Character.Stash! with { Locations = [new(id, "stash.5")] });
        Invalid(state.Character.Stash! with { Locations = [new(9999, "stash.1")] });
        Invalid(state.Character.Stash! with { Locations = [new(id, "stash.1"), new(id, "stash.2")] });
        Invalid(state.Character.Stash! with { Locations = [null!] });
        Invalid(state.Character.Stash! with { Tabs = [] });
        state.Character.Equipment[EquipmentSlot.Head] = id;
        Assert.Throws<InvalidDataException>(() => ProgressionSession.Restore(Policy, state));
    }

    private static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file));
    private static readonly AdventureContent Adventure = AdventureContent.Parse(Read("adventure.json"));
    private static readonly ProgressionContent FullPolicy = ProgressionContent.Parse(Read("progression.json"));
    private static readonly CampaignContent Campaign = CampaignContent.Parse(Read("campaign.json"));
    private static readonly EndgameContent Endgame = EndgameContent.Parse(Read("endgame.json"));
    private static readonly string Combat = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), Endgame).CombatJson;
    private static EndgameRuntimeSession Restore(EndgameRuntimeSnapshot state) => EndgameRuntimeSession.Restore(Combat, Adventure, FullPolicy, Campaign, Endgame, state);
    private static readonly Lazy<EndgameRuntimeSnapshot> EarnedTorren = new(() =>
    {
        var s = EndgameRuntimeSession.Create(Combat, Adventure, FullPolicy, Campaign, Endgame);
        for (int i = 0; i < 6000 && !(s.Campaign.ActiveEncounterId == "campaign.road" && s.EncounterCleared); i++)
        { var result = s.ExecuteCampaign(CampaignRuntimeSmoke.Next(s.Campaign)); Assert.True(result.Success, result.Reason); }
        Assert.True(s.Campaign.EncounterCleared); Assert.True(s.ExecuteCampaign(new(CampaignRuntimeAction.ReturnToHub)).Success);
        return s.Capture();
    });
    private static void Visit(EndgameRuntimeSession s, string interaction)
    {
        var target = s.Interactions.Single(i => i.ActionId == interaction);
        for (int i = 0; i < 400; i++)
        {
            var player = s.Combat.View.Actors.Single(a => a.Id == 1);
            if (Position.DistanceSquared(player.Position, target.Position) <= (long)(target.Range - 100) * (target.Range - 100)) return;
            var direction = CombatProductionSmoke.MovementDirection(player.Position, target.Position, s.Room);
            Assert.True(s.Step(new CombatCommand(CombatCommandKind.Move, X: direction.X, Z: direction.Z)).Success);
        }
        Assert.Fail("Could not approach " + interaction);
    }
    private static long StoreHead(EndgameRuntimeSession s)
    {
        long id = s.Production.ProgressionView.Equipment[EquipmentSlot.Head];
        Visit(s, "service.torren"); Assert.True(s.ExecuteProduction(new(ProductionAction.Unequip, Slot: EquipmentSlot.Head)).Success);
        Visit(s, PersonalStashCatalog.InteractionId); Assert.True(s.ExecuteProduction(new(ProductionAction.StoreItem, ItemId: id, Id: "stash.2")).Success);
        return id;
    }
    [Fact]
    public void PhysicalServiceRequiresRescueProximityAndGreyhaven()
    {
        var fresh = EndgameRuntimeSession.Create(Combat, Adventure, FullPolicy, Campaign, Endgame);
        Assert.False(fresh.Stash.CanUse); Assert.False(fresh.ExecuteProduction(new(ProductionAction.RenameStashTab, Id: "stash.1", Value: "Mine")).Success);
        var s = Restore(EarnedTorren.Value); string before = s.StateHash;
        Assert.False(s.Stash.CanUse); Assert.False(s.ExecuteProduction(new(ProductionAction.RenameStashTab, Id: "stash.1", Value: "Mine")).Success); Assert.Equal(before, s.StateHash);
        Visit(s, PersonalStashCatalog.InteractionId); Assert.True(s.Stash.CanUse);
        Assert.True(s.ExecuteProduction(new(ProductionAction.RenameStashTab, Id: "stash.1", Value: "Mine")).Success);
        Assert.True(s.ExecuteCampaign(new(CampaignRuntimeAction.EnterAct, Act: 1)).Success); Assert.False(s.Stash.CanUse);
        before = s.StateHash; Assert.False(s.ExecuteProduction(new(ProductionAction.RenameStashTab, Id: "stash.1", Value: "Away")).Success); Assert.Equal(before, s.StateHash);
    }
    [Fact]
    public void TransferProjectionReplayAndCachedRoomReentryNeverReimportStoredItem()
    {
        var s = Restore(EarnedTorren.Value); long id = StoreHead(s);
        Assert.DoesNotContain(s.Combat.View.Inventory, i => i.Id == id);
        Assert.All(s.Capture().Campaign.ClearedRooms!.Values, room => Assert.DoesNotContain(room.Inventory, i => i.Id == id));
        var snapshot = s.Capture(); Assert.Equal(s.StateHash, Restore(snapshot).StateHash);
        var replay = EndgameRuntimeReplayRunner.Run(Combat, Adventure, FullPolicy, Campaign, Endgame, s.CaptureReplay()); Assert.True(replay.Success, replay.ToString());
        Assert.True(s.ExecuteCampaign(new(CampaignRuntimeAction.EnterAct, Act: 1)).Success);
        Assert.DoesNotContain(s.Combat.View.Inventory, i => i.Id == id);
        Assert.True(s.ExecuteCampaign(new(CampaignRuntimeAction.ReturnToHub)).Success); Visit(s, PersonalStashCatalog.InteractionId);
        Assert.True(s.ExecuteProduction(new(ProductionAction.RetrieveItem, ItemId: id)).Success);
        Assert.Single(s.Combat.View.Inventory, i => i.Id == id); Assert.Single(s.Production.Capture().Progression.Character.Items, i => i.Id == id);
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
    }
    [Fact]
    public void StashedItemsRemainReferencedByCompleteBuildsAndApplyRequiresRetrieval()
    {
        var s = Restore(EarnedTorren.Value); Visit(s, "service.mara");
        Assert.True(s.ExecuteProduction(new(ProductionAction.SaveBuildLoadout, Id: "loadout.1", Value: "First Expedition")).Success);
        long id = StoreHead(s); Assert.Contains(s.Stash.Stored.Single(i => i.Item.Id == id).References, r => r.Kind == "Build" && r.Name == "First Expedition");
        Visit(s, "service.mara"); var preview = s.PreviewBuildLoadout("loadout.1"); Assert.False(preview.Success); Assert.Contains("stored", preview.Reason);
        string before = s.StateHash; Assert.False(s.ExecuteProduction(new(ProductionAction.ApplyBuildLoadout, Id: "loadout.1")).Success); Assert.Equal(before, s.StateHash);
    }
    [Theory]
    [InlineData(1800, true)]
    [InlineData(1801, false)]
    [InlineData(2600, false)]
    public void StashUsesItsExactAuthoredProximityBoundary(int distance, bool allowed)
    {
        // A position-only boundary fixture isolates the service radius from movement quantization.
        var snapshot = JsonData.Copy(EarnedTorren.Value.Campaign.Production);
        snapshot.Expedition.Combat.Actors.Single(a => a.Id == 1).Position = new(PersonalStashCatalog.Position.X + distance, PersonalStashCatalog.Position.Z);
        var s = ProductionSession.Restore(Combat, Adventure, CampaignRuntimeSession.ResolvePolicy(EndgameProgression.Resolve(FullPolicy), Campaign), snapshot);
        Assert.Equal(allowed, s.Stash.CanUse); string before = s.StateHash;
        Assert.Equal(allowed, s.Execute(new(ProductionAction.RenameStashTab, Id: "stash.1", Value: "Treasures")).Success);
        if (!allowed) Assert.Equal(before, s.StateHash);
    }
    private static EndgameRuntimeSession CompletedCampaign()
    {
        string previous = CampaignCombatContent.Parse(Read("fixtures/combat-phase4.json"), Read("fixtures/campaign-combat-phase4.json")).CombatJson;
        return EndgameRuntimeMigration.ImportPhaseFour(Read("fixtures/phase4-campaign-complete.json"), previous, Combat, Adventure, FullPolicy, Campaign, Endgame);
    }
    [Fact]
    public void StoredGodwroughtRemainsOwnedWithoutBeingMappedBackIntoCombat()
    {
        var s = CompletedCampaign(); var state = s.Production.Capture().Progression.Character;
        long id = state.Items.First(i => i.DefinitionId == "item.ashcleaver").Id;
        Visit(s, "service.torren");
        foreach (var slot in state.Equipment.Where(e => e.Value == id)) Assert.True(s.ExecuteProduction(new(ProductionAction.Unequip, Slot: slot.Key)).Success);
        Visit(s, PersonalStashCatalog.InteractionId); string item = JsonData.Hash(s.Production.Capture().Progression.Character.Items.Single(i => i.Id == id));
        Assert.True(s.ExecuteProduction(new(ProductionAction.StoreItem, ItemId: id, Id: "stash.1")).Success);
        Assert.DoesNotContain(s.Combat.View.Inventory, i => i.Id == id);
        Assert.DoesNotContain(s.Production.Capture().Expedition.GodwroughtItems, mapping => mapping.Value == id);
        Assert.NotEmpty(s.Production.Capture().Expedition.Adventure.Godwrought);
        s = Restore(s.Capture());
        string beforeFailure = s.StateHash; Assert.False(s.ExecuteProduction(new(ProductionAction.RetrieveItem, ItemId: -999)).Success); Assert.Equal(beforeFailure, s.StateHash);
        Assert.True(s.Step().Success);
        Assert.DoesNotContain(s.Combat.View.Inventory, i => i.Id == id);
        Assert.Equal(item, JsonData.Hash(s.Production.Capture().Progression.Character.Items.Single(i => i.Id == id)));
        Assert.True(s.ExecuteProduction(new(ProductionAction.RetrieveItem, ItemId: id)).Success);
        Assert.Single(s.Combat.View.Inventory, i => i.Id == id);
        Assert.Equal(item, JsonData.Hash(s.Production.Capture().Progression.Character.Items.Single(i => i.Id == id)));
        Assert.Contains(s.Production.Capture().Expedition.GodwroughtItems, mapping => mapping.Value == id);
    }
    [Fact]
    public void ActiveRegionalHuntCannotMutateStash()
    {
        var s = CompletedCampaign(); string hunt = RegionalHuntCatalog.Contracts[0].Id;
        for (int i = 0; i < 400 && s.RegionalHunts.Run?.Stage != "Tracking"; i++) Assert.True(s.Execute(RegionalHuntSmoke.Next(s, hunt)).Success);
        Assert.Equal("Tracking", s.RegionalHunts.Run?.Stage); Assert.False(s.Stash.CanUse); string before = s.StateHash;
        Assert.False(s.ExecuteProduction(new(ProductionAction.RenameStashTab, Id: "stash.1", Value: "Forbidden")).Success);
        Assert.Equal(before, s.StateHash);
    }
    [Fact]
    public void FutureStashVersionIsProtectedAndIndependentCharactersHaveIndependentStorage()
    {
        var s = Restore(EarnedTorren.Value); StoreHead(s);
        var other = Restore(EarnedTorren.Value); Assert.Null(other.Capture().Campaign.Production.Progression.Character.Stash);
        string dir = Path.Combine(Path.GetTempPath(), "ashenwake-stash-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "character.json"); EndgameRuntimeSaveStore.Write(path, Combat, Adventure, FullPolicy, Campaign, Endgame, s.Capture());
            Assert.Equal(s.StateHash, EndgameRuntimeSaveStore.Load(path, Combat, Adventure, FullPolicy, Campaign, Endgame).Session.StateHash);
            string original = File.ReadAllText(path); var json = JsonNode.Parse(original)!;
            json["state"]!["campaign"]!["production"]!["progression"]!["character"]!["stash"]!["schemaVersion"] = 999;
            File.WriteAllText(path, json.ToJsonString());
            Assert.Throws<SaveCompatibilityException>(() => EndgameRuntimeSaveStore.Load(path, Combat, Adventure, FullPolicy, Campaign, Endgame));
            string protectedBytes = File.ReadAllText(path);
            Assert.Throws<SaveCompatibilityException>(() => EndgameRuntimeSaveStore.Write(path, Combat, Adventure, FullPolicy, Campaign, Endgame, s.Capture()));
            Assert.Equal(protectedBytes, File.ReadAllText(path));
        }
        finally { Directory.Delete(dir, true); }
    }
}
