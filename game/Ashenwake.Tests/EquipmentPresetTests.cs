using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Content;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class EquipmentPresetTests
{
    private static ProgressionContent Policy => ProgressionContent.Default();
    private static ProgressionSession Fresh(string discipline = "Arcanist") => ProgressionSession.Create(Policy, discipline);
    private static long Grant(ProgressionSession session, string definition, ItemRarity rarity = ItemRarity.Common, IReadOnlyDictionary<string, int>? affixes = null)
    {
        long id = session.Capture().Character.NextItemId;
        Assert.True(session.GrantItem("grant." + id, definition, rarity, affixes).Success); return id;
    }
    private static void Equip(ProgressionSession session, long id, EquipmentSlot slot, string operation) => Assert.True(session.Equip(operation, id, slot).Success);
    private static void Apply(ProgressionSession session, string preset, string operation) => Assert.True(session.ApplyEquipmentPreset(operation, preset).Success);

    [Fact]
    public void WholeArrangementSwapsRingsBothHandsAndEmptySlotsAtomically()
    {
        var session = Fresh();
        long sword = Grant(session, "item.starter_mainhand"), shield = Grant(session, "item.starter_offhand"), staff = Grant(session, "item.greatstaff");
        long first = Grant(session, "item.echo_ring", ItemRarity.Legendary), second = Grant(session, "item.echo_ring", ItemRarity.Legendary);
        Equip(session, sword, EquipmentSlot.MainHand, "equip.sword"); Equip(session, shield, EquipmentSlot.OffHand, "equip.shield");
        Equip(session, first, EquipmentSlot.Ring1, "equip.first"); Equip(session, second, EquipmentSlot.Ring2, "equip.second");
        Assert.True(session.SaveEquipmentPreset("save.1", "preset.1", "Ashen Bulwark").Success);
        Assert.True(session.Unequip("empty.off", EquipmentSlot.OffHand).Success); Equip(session, staff, EquipmentSlot.MainHand, "equip.staff");
        Equip(session, second, EquipmentSlot.Ring1, "swap.second"); Equip(session, first, EquipmentSlot.Ring2, "swap.first");
        Assert.True(session.SaveEquipmentPreset("save.2", "preset.2", "Emberglass").Success);
        string owned = JsonData.Hash(session.Capture().Character.Items);
        Apply(session, "preset.1", "apply.1"); Assert.Equal(shield, session.View.Equipment[EquipmentSlot.OffHand]); Assert.Equal(first, session.View.Equipment[EquipmentSlot.Ring1]);
        Apply(session, "preset.2", "apply.2"); Assert.False(session.View.Equipment.ContainsKey(EquipmentSlot.OffHand)); Assert.Equal(staff, session.View.Equipment[EquipmentSlot.MainHand]); Assert.Equal(second, session.View.Equipment[EquipmentSlot.Ring1]);
        Assert.Equal(owned, JsonData.Hash(session.Capture().Character.Items));
    }

    [Fact]
    public void EmptyArrangementClearsAllEquipmentAndOverwritingCapturesCurrentArrangement()
    {
        var session = Fresh(); Assert.True(session.SaveEquipmentPreset("save.empty", "preset.1", "Unburdened").Success);
        long item = Grant(session, "item.starter_head"); Equip(session, item, EquipmentSlot.Head, "equip");
        Assert.True(session.SaveEquipmentPreset("save.armored", "preset.2", "Armored").Success);
        Apply(session, "preset.1", "apply.empty"); Assert.Empty(session.View.Equipment);
        Assert.True(session.SaveEquipmentPreset("overwrite", "preset.2", "Also Empty").Success);
        Assert.Empty(session.EquipmentPresets.Single(p => p.Id == "preset.2").Equipment);
    }

    [Fact]
    public void DiscardedItemsRemainDiagnosableAfterRestoreAndApplyLeavesStateUnchanged()
    {
        var session = Fresh(); long item = Grant(session, "item.starter_head");
        Equip(session, item, EquipmentSlot.Head, "equip"); Assert.True(session.SaveEquipmentPreset("save", "preset.1", "Lost Crown").Success);
        Assert.True(session.Unequip("unequip", EquipmentSlot.Head).Success); Assert.True(session.Discard("discard", item, true).Success);
        session = ProgressionSession.Restore(Policy, session.Capture()); string before = session.StateHash;
        var preview = session.PreviewEquipmentPreset("preset.1"); Assert.False(preview.Success); Assert.Contains("no longer owned", preview.Reason); Assert.Equal(item, Assert.Single(preview.Issues).ItemId);
        Assert.False(session.ApplyEquipmentPreset("apply", "preset.1").Success); Assert.Equal(before, session.StateHash);
    }

    [Fact]
    public void RetrainingKeepsPresetButExplainsIncompatibleDisciplineWithoutRespec()
    {
        var session = Fresh(); long staff = Grant(session, "item.greatstaff"); Equip(session, staff, EquipmentSlot.MainHand, "equip");
        Assert.True(session.SaveEquipmentPreset("save", "preset.1", "Winter's Spine").Success);
        Assert.True(session.EarnExperience("xp", 5000, 500).Success); Assert.True(session.Retrain("retrain", "Vanguard").Success);
        var restored = ProgressionSession.Restore(Policy, session.Capture()); string before = restored.StateHash;
        Assert.Contains("current discipline is Vanguard", restored.PreviewEquipmentPreset("preset.1").Reason);
        Assert.False(restored.ApplyEquipmentPreset("apply", "preset.1").Success); Assert.Equal(before, restored.StateHash);
    }

    [Fact]
    public void LaterAffixConflictBlocksWholePresetButAllowsRestoreAndRepair()
    {
        var session = Fresh(); long main = Grant(session, "item.starter_mainhand", ItemRarity.Relic, new Dictionary<string, int> { ["affix.fork"] = 1 });
        long off = Grant(session, "item.starter_offhand", ItemRarity.Relic);
        Equip(session, main, EquipmentSlot.MainHand, "equip.main"); Equip(session, off, EquipmentSlot.OffHand, "equip.off");
        Assert.True(session.SaveEquipmentPreset("save", "preset.1", "Forked Oath").Success); Assert.True(session.Unequip("off", EquipmentSlot.OffHand).Success);
        var later = session.Capture(); later.Character.Items.Single(i => i.Id == off).Affixes["affix.chain"] = 1;
        session = ProgressionSession.Restore(Policy, later); string before = session.StateHash;
        Assert.Contains("conflicts", session.PreviewEquipmentPreset("preset.1").Reason); Assert.False(session.ApplyEquipmentPreset("apply", "preset.1").Success); Assert.Equal(before, session.StateHash);
        Assert.True(session.SaveEquipmentPreset("repair", "preset.1", "Forked Oath").Success); Assert.True(session.PreviewEquipmentPreset("preset.1").Success);
    }

    [Fact]
    public void ExtractedLegendaryLeavesPresetReferenceAndCannotBeRecreatedByApply()
    {
        var session = Fresh(); long ring = Grant(session, "item.echo_ring", ItemRarity.Legendary);
        Equip(session, ring, EquipmentSlot.Ring1, "equip"); Assert.True(session.SaveEquipmentPreset("save", "preset.1", "Last Echo").Success);
        Assert.True(session.CompleteObjective("mara", "objective.mara").Success);
        Assert.True(session.CompleteObjective("torren", "objective.torren").Success);
        Assert.True(session.CompleteObjective("kesh", "objective.kesh").Success);
        Assert.True(session.Craft(new("extract", CraftingService.Extraction, ring, ConfirmPermanent: true)).Success);
        string before = session.StateHash; Assert.False(session.ApplyEquipmentPreset("apply", "preset.1").Success); Assert.Equal(before, session.StateHash);
        Assert.Contains("no longer owned", session.PreviewEquipmentPreset("preset.1").Reason);
        Assert.Empty(session.Capture().Character.Items); Assert.Contains("property.summon_burst", session.Capture().Character.PropertyLibrary);
        Assert.Equal(before, ProgressionSession.Restore(Policy, session.Capture()).StateHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Line\nBreak")]
    [InlineData("\nTrimmed")]
    [InlineData("Invisible\u200B")]
    [InlineData("123456789012345678901234567890123")]
    public void InvalidNamesFailWithoutMutation(string name)
    {
        var session = Fresh(); string before = session.StateHash;
        Assert.False(session.SaveEquipmentPreset("save", "preset.1", name).Success); Assert.Equal(before, session.StateHash);
    }

    [Fact]
    public void NamesSlotsRenameDeleteAndIdempotencyAreBounded()
    {
        var session = Fresh();
        for (int i = 1; i <= 8; i++) Assert.True(session.SaveEquipmentPreset("save." + i, "preset." + i, "  Set " + i + "  ").Success);
        Assert.Equal(8, session.EquipmentPresets.Count); Assert.Equal("Set 1", session.EquipmentPresets[0].Name);
        string before = session.StateHash;
        Assert.False(session.SaveEquipmentPreset("overflow", "preset.9", "Ninth").Success);
        Assert.False(session.RenameEquipmentPreset("duplicate", "preset.1", "set 2").Success); Assert.Equal(before, session.StateHash);
        Assert.True(session.RenameEquipmentPreset("rename", "preset.1", "Iron Testament").Success);
        string once = session.StateHash; Assert.True(session.RenameEquipmentPreset("rename", "preset.1", "Iron Testament").Success); Assert.Equal(once, session.StateHash);
        Assert.False(session.RenameEquipmentPreset("rename", "preset.1", "Different").Success); Assert.Equal(once, session.StateHash);
        for (int i = 1; i <= 8; i++) Assert.True(session.DeleteEquipmentPreset("delete." + i, "preset." + i).Success);
        Assert.Null(session.Capture().Character.EquipmentPresets);
    }

    [Fact]
    public void PreviewAndViewsAreDetachedWithoutReceiptsOrStateChanges()
    {
        var session = Fresh(); long item = Grant(session, "item.starter_head"); Equip(session, item, EquipmentSlot.Head, "equip");
        Assert.True(session.SaveEquipmentPreset("save", "preset.1", "Iron Crown").Success); string before = session.StateHash;
        var preview = session.PreviewEquipmentPreset("preset.1"); Assert.True(preview.Success); Assert.Equal(before, session.StateHash);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<EquipmentSlot, long>)preview.Equipment).Clear());
        var view = session.EquipmentPresets; Assert.True(session.DeleteEquipmentPreset("delete", "preset.1").Success); Assert.Single(view); Assert.Single(view[0].Equipment);
    }

    [Fact]
    public void LegacySnapshotsOmitNewFieldAndRetainOriginalLogicalHash()
    {
        var session = Fresh(); string json = JsonData.Write(session.Capture());
        Assert.DoesNotContain("equipmentPresets", json);
        var legacy = JsonData.Read<ProgressionSnapshot>(json); Assert.Equal(session.StateHash, ProgressionSession.Restore(Policy, legacy).StateHash);
        Assert.Equal(JsonData.Hash(System.Text.Json.JsonDocument.Parse(json).RootElement), session.StateHash);
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-name")]
    [InlineData("unknown-slot")]
    [InlineData("future-item")]
    [InlineData("zero-item")]
    [InlineData("duplicate-item")]
    [InlineData("wrong-slot")]
    [InlineData("hands")]
    [InlineData("empty-array")]
    [InlineData("null-equipment")]
    public void MalformedPresetSnapshotsAreRejected(string kind)
    {
        var session = Fresh(); long item = Grant(session, "item.greatstaff"), off = Grant(session, "item.starter_offhand");
        var state = session.Capture(); var preset = new EquipmentPreset("preset.1", "Valid", new() { [EquipmentSlot.MainHand] = item });
        state.Character.EquipmentPresets = [preset];
        switch (kind)
        {
            case "duplicate-id": state.Character.EquipmentPresets = [preset, preset with { Name = "Other" }]; break;
            case "duplicate-name": state.Character.EquipmentPresets = [preset, preset with { Id = "preset.2", Name = "valid" }]; break;
            case "unknown-slot": preset.Equipment[(EquipmentSlot)999] = off; break;
            case "future-item": preset.Equipment[EquipmentSlot.MainHand] = state.Character.NextItemId; break;
            case "zero-item": preset.Equipment[EquipmentSlot.MainHand] = 0; break;
            case "duplicate-item": preset.Equipment[EquipmentSlot.OffHand] = item; break;
            case "wrong-slot": preset.Equipment.Clear(); preset.Equipment[EquipmentSlot.Head] = item; break;
            case "hands": preset.Equipment[EquipmentSlot.OffHand] = off; break;
            case "empty-array": state.Character.EquipmentPresets = []; break;
            case "null-equipment": state.Character.EquipmentPresets = [preset with { Equipment = null! }]; break;
        }
        Assert.Throws<InvalidDataException>(() => ProgressionSession.Restore(Policy, state));
    }

    private static string CombatJson => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json"));
    private static AdventureContent Adventure => AdventureContent.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "adventure.json")));
    private static ProgressionContent ShippedPolicy => ProgressionContent.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "progression.json")));
    private static ProductionSession ProductionAtTorren()
    {
        var fresh = ProductionSession.Create(CombatJson, Adventure, ShippedPolicy); var state = fresh.Capture();
        state.Expedition.Combat.Actors.Single(a => a.Id == 1).Position = new(2000, -2000);
        return ProductionSession.Restore(CombatJson, Adventure, ShippedPolicy, state);
    }

    [Fact]
    public void ProductionCommandsPersistReplayAndProjectWithoutChangingRewardsOrBuildChoices()
    {
        var session = ProductionAtTorren(); var before = session.Capture().Progression;
        Assert.True(session.SaveEquipmentPreset("preset.1", "First Oath").Success);
        var slot = session.ProgressionView.Equipment.Keys.First(); Assert.True(session.Unequip(slot).Success);
        Assert.True(session.SaveEquipmentPreset("preset.2", "Light Step").Success);
        Assert.True(session.ApplyEquipmentPreset("preset.1").Success); Assert.Equal(before.Character.Equipment[slot], session.Combat.View.Equipment[slot.ToString()]);
        Assert.True(session.RenameEquipmentPreset("preset.2", "Freed Shoulder").Success); Assert.True(session.DeleteEquipmentPreset("preset.2").Success);
        var after = session.Capture().Progression;
        Assert.Equal(before.Character.Materials, after.Character.Materials); Assert.Equal(before.Character.Experience, after.Character.Experience);
        Assert.Equal(JsonData.Hash(before.Character.Items), JsonData.Hash(after.Character.Items));
        Assert.Equal(JsonData.Hash(before.Character.Mastery), JsonData.Hash(after.Character.Mastery));
        Assert.Equal(JsonData.Hash(before.Character.SelectedMutations), JsonData.Hash(after.Character.SelectedMutations));
        Assert.Equal(JsonData.Hash(before.Profile), JsonData.Hash(after.Profile));
        var snapshot = session.Capture(); var loaded = ProductionSaveStore.Read(CombatJson, Adventure, ShippedPolicy, JsonData.Write(new ProductionSave(1, session.StateHash, snapshot)));
        Assert.Equal(session.StateHash, loaded.StateHash); Assert.Equal("First Oath", Assert.Single(loaded.EquipmentPresets).Name);
        var replay = ProductionReplayRunner.Run(CombatJson, Adventure, ShippedPolicy, session.CaptureReplay()); Assert.True(replay.Success, replay.Detail);
    }

    [Fact]
    public void AllProductionPresetMutationsRequireNearbyTorrenAndPreviewAgrees()
    {
        var session = ProductionAtTorren(); Assert.True(session.SaveEquipmentPreset("preset.1", "Bound Oath").Success);
        var state = session.Capture(); state.Expedition.Combat.Actors.Single(a => a.Id == 1).Position = new(-4500, -1800);
        session = ProductionSession.Restore(CombatJson, Adventure, ShippedPolicy, state); string before = session.StateHash;
        foreach (var action in new[] { ProductionAction.SaveEquipmentPreset, ProductionAction.RenameEquipmentPreset, ProductionAction.DeleteEquipmentPreset, ProductionAction.ApplyEquipmentPreset })
        { var result = session.Execute(new(action, Id: "preset.1", Value: "Next")); Assert.False(result.Success); Assert.Contains("Torren", result.Reason); Assert.Equal(before, session.StateHash); }
        Assert.False(session.PreviewEquipmentPreset("preset.1").Success); Assert.Contains("Torren", session.PreviewEquipmentPreset("preset.1").Reason); Assert.Equal(before, session.StateHash);
    }
    [Fact]
    public void CampaignPresetCommandsRequireRescuedTorrenAndPersistThroughOuterSaveAndReplay()
    {
        static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));
        var campaign = CampaignContent.Parse(Read("campaign.json")); var endgame = EndgameContent.Parse(Read("endgame.json"));
        string combat = EndgameCombatContent.Parse(CampaignCombatContent.Parse(CombatJson, Read("campaign-combat.json")).CombatJson,
            Read("endgame-combat.json"), endgame).CombatJson;
        var snapshot = EndgameRuntimeSession.Create(combat, Adventure, ShippedPolicy, campaign, endgame).Capture();
        foreach (var body in new[] { snapshot.Campaign.Combat, snapshot.Campaign.Production.Expedition.Combat }) body.Actors.Single(a => a.Id == 1).Position = new(2000, -2000);
        var session = EndgameRuntimeSession.Restore(combat, Adventure, ShippedPolicy, campaign, endgame, snapshot);
        var save = new ProductionCommand(ProductionAction.SaveEquipmentPreset, Id: "preset.1", Value: "Greyhaven Oath");
        string before = session.StateHash; var locked = session.ExecuteProduction(save);
        Assert.False(locked.Success); Assert.Contains("Rescue this specialist", locked.Reason); Assert.Equal(before, session.StateHash);
        for (int commands = 0; commands < 2000 && !session.Campaign.Capture().Campaign.RescuedResidents.Contains("Torren Bale"); commands++)
        { var result = session.Execute(EndgameRuntimeSmoke.Next(session)); Assert.True(result.Success, result.Reason); }
        Assert.Contains("Torren Bale", session.Campaign.Capture().Campaign.RescuedResidents);
        Assert.False(session.InHub); before = session.StateHash; Assert.False(session.ExecuteProduction(save).Success); Assert.Equal(before, session.StateHash);
        Assert.True(session.Execute(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.ReturnToHub))).Success);
        var atTorren = new CampaignRuntimeCommand(CampaignRuntimeAction.Production, Production: save); bool reached = false;
        for (int commands = 0; commands < 300 && !reached; commands++)
        {
            var next = CampaignRuntimeSmoke.AtInteraction(session.Campaign, "service.torren", atTorren);
            if (next == atTorren) { reached = true; break; }
            Assert.True(session.Execute(new(EndgameRuntimeAction.Campaign, Campaign: next)).Success);
        }
        Assert.True(reached); Assert.True(session.ExecuteProduction(save).Success);
        var slot = session.Production.ProgressionView.Equipment.Keys.First();
        Assert.True(session.ExecuteProduction(new(ProductionAction.Unequip, Slot: slot)).Success);
        Assert.True(session.ExecuteProduction(new(ProductionAction.ApplyEquipmentPreset, Id: "preset.1")).Success);
        Assert.True(session.Combat.View.Equipment.ContainsKey(slot.ToString()));
        var state = session.Capture(); var loaded = EndgameRuntimeSaveStore.Read(combat, Adventure, ShippedPolicy, campaign, endgame,
            JsonData.Write(new EndgameRuntimeSave(1, JsonData.Hash(state), state)));
        Assert.Equal(session.StateHash, loaded.StateHash); Assert.Single(loaded.Production.EquipmentPresets);
        var replay = EndgameRuntimeReplayRunner.Run(combat, Adventure, ShippedPolicy, campaign, endgame, session.CaptureReplay());
        Assert.True(replay.Success, replay.Detail);
    }

}
