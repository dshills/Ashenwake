using System.Security.Cryptography;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class EquipmentSetContentTests
{
    private static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));
    [Fact]
    public void CatalogOnlyAddsSixNonextractablePiecesAndPreservesPublishedPredecessorBytes()
    {
        Assert.Equal("087BE437B6EE47E73DCE927F126F76300455C273FD4689C07421300E784A8031", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures/combat-world-encounters.json")))));
        Assert.Equal("2CEEEF08F4A37DF1281964D8F701E01E3658BC8DD5F425A03721573207163881", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures/progression-world-encounters.json")))));
        var combat = CombatContent.Parse(Read("combat.json")); var before = CombatContent.Parse(Read("fixtures/combat-world-encounters.json"));
        Assert.Equal(6, combat.Items.Count(i => EquipmentSets.IsItem(i.Id)));
        Assert.Equal(JsonData.Hash(before), JsonData.Hash(combat with { Items = combat.Items.Where(i => !EquipmentSets.IsItem(i.Id)).ToArray() }));
        var policy = ProgressionContent.Parse(Read("progression.json")).Capture();
        Assert.Equal(JsonData.Hash(ProgressionContent.Parse(Read("fixtures/progression-world-encounters.json")).Capture()),
            JsonData.Hash(policy with { Items = policy.Items.Where(i => !EquipmentSets.IsItem(i.Id)).ToArray() }));
        Assert.All(policy.Items.Where(i => EquipmentSets.IsItem(i.Id)), i => Assert.Empty(i.Property));
    }
    [Fact]
    public void CountsRequireDistinctEquippedDefinitionsAndPreviewIsPure()
    {
        var state = new ProgressionState { Items = [new() { Id = 1, DefinitionId = EquipmentSets.VigilHead }, new() { Id = 2, DefinitionId = EquipmentSets.VigilHead }, new() { Id = 3, DefinitionId = EquipmentSets.VigilChest }, new() { Id = 4, DefinitionId = "item.starter_chest", Engraving = EquipmentSets.LastVigil }], Equipment = new() { [EquipmentSlot.Head] = 1 } };
        Assert.Equal(1, EquipmentSets.CountEquipped(EquipmentSets.LastVigil, state));
        Assert.False(EquipmentSets.Active(EquipmentSets.LastVigil, state));
        state.Equipment[EquipmentSlot.Chest] = 2; // Defensive projection: even duplicate or corrupted slot assignments cannot count a second piece.
        Assert.Equal(1, EquipmentSets.CountEquipped(EquipmentSets.LastVigil, state));
        state.Equipment[EquipmentSlot.Chest] = 4;
        Assert.Equal(1, EquipmentSets.CountEquipped(EquipmentSets.LastVigil, state));
        string hash = JsonData.Hash(state);
        Assert.Equal(2, EquipmentSets.PreviewCountEquipped(EquipmentSets.LastVigil, state, state.Items[2], EquipmentSlot.Chest));
        Assert.Equal(0, EquipmentSets.PreviewCountEquipped(EquipmentSets.LastVigil, state, state.Items[3], EquipmentSlot.Head));
        Assert.Equal(hash, JsonData.Hash(state));
        state.Equipment[EquipmentSlot.Chest] = 3; Assert.True(EquipmentSets.Active(EquipmentSets.LastVigil, state));
    }
    [Fact]
    public void CollectionRequiresOwnedSetPieceAndExtractionCannotConsumeIt()
    {
        Assert.Empty(LegendaryCollection.ItemForPower(""));
        var state = new ProgressionState { PropertyLibrary = ["", EquipmentSets.LastVigil] };
        Assert.Empty(LegendaryCollection.Observe(LegendaryCollection.Empty("wanderer"), state).DiscoveredItems);
        state.Items = [new() { Id = 1, DefinitionId = EquipmentSets.VigilHead }];
        var memory = LegendaryCollection.Observe(LegendaryCollection.Empty("wanderer"), state);
        Assert.Equal(new[] { EquipmentSets.VigilHead }, memory.DiscoveredItems);
        state.Items = []; Assert.Equal(memory.DiscoveredItems, LegendaryCollection.Observe(memory, state).DiscoveredItems);
        var session = ProgressionSession.Create(ProductionContent.Resolve(Read("combat.json"), ProgressionContent.Parse(Read("progression.json"))));
        foreach (string id in new[] { "mara", "torren", "cael", "oris", "kesh", "haven" }) Assert.True(session.CompleteObjective("quest." + id, "objective." + id).Success);
        Assert.True(session.GrantItem("grant", EquipmentSets.VigilHead, ItemRarity.Legendary).Success);
        string hash = session.StateHash;
        Assert.False(session.Craft(new("extract", CraftingService.Extraction, 1, ConfirmPermanent: true)).Success);
        Assert.Equal(hash, session.StateHash);
    }
    public static IEnumerable<object[]> Pieces => LegendaryCollectionCatalog.Entries.Where(e => EquipmentSets.IsItem(e.ItemId)).Select(e => new object[] { e.ItemId });
    [Theory]
    [MemberData(nameof(Pieces))]
    public void CampaignVictoriesEarnTheirSetPieceAlongsideExistingLegendary(string item)
    {
        var entry = LegendaryCollectionCatalog.Entries.Single(e => e.ItemId == item);
        var content = CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json"));
        var hub = content.CreateEncounter("hub"); hub.ApplyProgressionBuild(new(Level: 8, Offense: 2, Defense: 2));
        var session = content.CreateEncounter(entry.CampaignEncounterId, previous: hub.Capture());
        List<CombatEvent> events = [];
        for (int tick = 0; tick < 9000 && session.View.Actors[0].Health > 0 && session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0); tick++)
            events.AddRange(session.Step(CampaignCombatSmoke.Commands(session.View, session.Room)));
        Assert.True(session.View.Actors[0].Health > 0); Assert.DoesNotContain(session.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        Assert.Single(events, e => e.Kind == "LootDropped" && e.ContentId == item);
        Assert.Equal("Legendary", Assert.Single(session.View.Loot, l => l.Item.DefinitionId == item).Item.Rarity);
        string existing = LegendaryEquipment.EncounterReward(entry.CampaignEncounterId);
        if (existing.Length > 0) Assert.Single(session.View.Loot, l => l.Item.DefinitionId == existing);
        Assert.Equal(session.StateHash, CombatSession.Restore(content.CombatJson, session.Capture()).StateHash);
        session.Step(); Assert.Single(session.View.Loot, l => l.Item.DefinitionId == item);
    }
    [Theory]
    [MemberData(nameof(Pieces))]
    public void RegionalFractureSourcesGiveReplacementOnEveryNewRun(string item)
    {
        var entry = LegendaryCollectionCatalog.Entries.Single(e => e.ItemId == item);
        var content = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), EndgameContent.Parse(Read("endgame.json")));
        foreach (long runId in new long[] { 1, 2 })
        {
            var manifest = content.CreateFractureManifest(new(runId, 42, entry.FractureRegionId, 1, ["fracture.burning_haste"], "Vael", "Materials"), runId);
            var hub = CombatSession.CreateEncounter(content.CombatJson, 42, "hub");
            var state = content.CreateEncounter(manifest, entry.FractureEncounterIndex, 0, hub.Capture()).Capture();
            foreach (var enemy in state.Actors.Where(a => a.Faction == CombatFaction.Enemy))
            {
                enemy.Health = 1; enemy.Statuses.Add(new() { Id = "Burning", SourceId = 1, OwnerId = 1, ActionId = state.NextActionId++, NextTick = 0, ExpiresTick = 90 });
            }
            var session = CombatSession.Restore(content.CombatJson, state); var events = session.Step();
            Assert.Single(events, e => e.Kind == "LootDropped" && e.ContentId == item);
            Assert.Equal("Legendary", Assert.Single(session.View.Loot, l => l.Item.DefinitionId == item).Item.Rarity);
            session.Step(); Assert.Single(session.View.Loot, l => l.Item.DefinitionId == item);
        }
    }
}
