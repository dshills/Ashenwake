using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Xunit;

namespace Ashenwake.Tests;

public sealed class RoamingChampionCatalogTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static string Base(bool old) => Read(old ? "fixtures/combat-personal-stash.json" : "combat.json");
    private static ProgressionContent Policy(bool old) => ProgressionContent.Parse(Read(old ? "fixtures/progression-personal-stash.json" : "progression.json"));
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static CampaignContent Campaign => CampaignContent.Parse(Read("campaign.json"));
    private static EndgameContent Endgame => EndgameContent.Parse(Read("endgame.json"));
    private static string CampaignCombat(bool old) => CampaignCombatContent.Parse(Base(old), Read("campaign-combat.json")).CombatJson;
    private static string Combat(bool old) => EndgameCombatContent.Parse(CampaignCombat(old), Read("endgame-combat.json"), Endgame).CombatJson;
    private static readonly string[] NewItems = ["item.broodkeepers_knot", "item.last_toll", "item.tithebreakers_grasp"];
    private static EndgameRuntimeSession Previous() => EndgameRuntimeMigration.ImportPhaseFour(Read("fixtures/phase4-campaign-complete.json"),
        CampaignCombat(true), Combat(true), Adventure, Policy(true), Campaign, Endgame);

    [Fact]
    public void PublishedStashCatalogBytesRemainFrozenAndOnlyThreeRewardItemsAreAdded()
    {
        Assert.Equal("F9E105645F29784885DB90498881E0EA8C8590608D9D590C7A7A07E105FB63A3", Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Base(true)))));
        Assert.Equal("3E45D9913B5C801FE43BA733B3B7CABC30C731C0A3B5D82A90F1610BD0DDC0EB", Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Read("fixtures/progression-personal-stash.json")))));
        var old = CombatContent.Parse(Base(true)); var current = CombatContent.Parse(Base(false));
        Assert.Equal(NewItems, current.Items.Select(i => i.Id).Except(old.Items.Select(i => i.Id)).Order());
        Assert.Equal(JsonData.Hash(old), JsonData.Hash(current with { Items = current.Items.Where(i => !NewItems.Contains(i.Id)).ToArray() }));
        var policy = Policy(false).Capture();
        Assert.Equal(JsonData.Hash(Policy(true).Capture()), JsonData.Hash(policy with { Items = policy.Items.Where(i => !NewItems.Contains(i.Id)).ToArray() }));
        Assert.All(NewItems, id => Assert.True(LegendaryEquipment.IsItem(id)));
        Assert.DoesNotContain(CombatSession.Create(Base(false), 7).View.Inventory, i => NewItems.Contains(i.DefinitionId));
    }

    [Theory]
    [InlineData("item.last_toll", EquipmentSlot.OffHand, "property.unspoken_verdict")]
    [InlineData("item.broodkeepers_knot", EquipmentSlot.Amulet, "property.virulent_wake")]
    [InlineData("item.tithebreakers_grasp", EquipmentSlot.Gloves, "property.widow_echo")]
    public void SignatureGearUsesSupportedInnatePowersAndExistingEngravingSlots(string id, EquipmentSlot slot, string power)
    {
        var content = Policy(false); var definition = content.Capture();
        var item = definition.Items.Single(i => i.Id == id);
        Assert.Equal(power, item.Property); Assert.Contains(slot, item.Slots);
        Assert.Contains(slot, definition.Properties.Single(p => p.Id == power).Slots);
        var progression = ProgressionSession.Create(content);
        Assert.True(progression.GrantItem("champion.reward", id, ItemRarity.Legendary).Success);
        Assert.True(progression.Equip("equip", progression.Capture().Character.Items.Single().Id, slot).Success);
        Assert.Equal(progression.StateHash, ProgressionSession.Restore(content, progression.Capture()).StateHash);
    }

    private static void Visit(EndgameRuntimeSession session, string interaction)
    {
        for (int i = 0; i < 300; i++)
        {
            var target = session.Interactions.Single(p => p.ActionId == interaction);
            var player = session.Combat.View.Actors.Single(a => a.Id == 1);
            if (Ashenwake.Core.Simulation.Position.DistanceSquared(player.Position, target.Position) <= (long)target.Range * target.Range) return;
            var direction = CombatProductionSmoke.MovementDirection(player.Position, target.Position, session.Room);
            Assert.True(session.Step(new CombatCommand(CombatCommandKind.Move, X: direction.X, Z: direction.Z)).Success);
        }
        Assert.Fail("Interaction could not be reached.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MigrationPreservesEveryOwnedStashAndActiveArenaField(bool activeHunt)
    {
        var original = Previous(); long head = original.Production.ProgressionView.Equipment[EquipmentSlot.Head];
        Visit(original, "service.torren"); Assert.True(original.ExecuteProduction(new(ProductionAction.Unequip, Slot: EquipmentSlot.Head)).Success);
        Visit(original, PersonalStashCatalog.InteractionId); Assert.True(original.ExecuteProduction(new(ProductionAction.StoreItem, ItemId: head, Id: "stash.2")).Success);
        Assert.True(original.ExecuteProduction(new(ProductionAction.RenameStashTab, Id: "stash.2", Value: "Old Crowns")).Success);
        if (activeHunt)
        {
            for (int i = 0; i < 1000 && original.RegionalHunts.Run?.Stage != "Combat"; i++)
            { var result = original.Execute(RegionalHuntSmoke.Next(original, "hunt.regional.pallbearer")); Assert.True(result.Success, result.Reason); }
            Assert.Equal("Combat", original.RegionalHunts.Run?.Stage); Assert.True(original.Step().Success);
        }
        string bytes = JsonData.Write(new EndgameRuntimeSave(1, original.StateHash, original.Capture()));
        var loaded = EndgameRuntimeSaveStore.Read(Combat(false), Adventure, Policy(false), Campaign, Endgame, bytes);
        var expected = JsonNode.Parse(JsonData.Write(original.Capture()))!;
        string oldCombat = original.Combat.ContentHash, newCombat = loaded.Combat.ContentHash;
        string oldPolicy = original.Production.Content.Hash, newPolicy = loaded.Production.Content.Hash;
        void Rebind(JsonNode? node)
        {
            if (node is JsonObject obj)
                foreach (var pair in obj.ToArray())
                {
                    if (pair.Key == "contentHash" && pair.Value is JsonValue value && value.TryGetValue<string>(out var hash) && (hash == oldCombat || hash == oldPolicy))
                        obj[pair.Key] = hash == oldCombat ? newCombat : newPolicy;
                    else Rebind(pair.Value);
                }
            else if (node is JsonArray array) foreach (var item in array) Rebind(item);
        }
        Rebind(expected);
        Assert.Equal(expected.ToJsonString(), JsonNode.Parse(JsonData.Write(loaded.Capture()))!.ToJsonString());
        Assert.DoesNotContain(loaded.Combat.View.Inventory, i => i.Id == head);
        Assert.Equal("Old Crowns", loaded.Stash.Tabs.Single(t => t.Id == "stash.2").Name);
        Assert.True(loaded.Step().Success);
        Assert.True(EndgameRuntimeReplayRunner.Run(Combat(false), Adventure, Policy(false), Campaign, Endgame, loaded.CaptureReplay()).Success);
        Assert.Equal(bytes, JsonData.Write(new EndgameRuntimeSave(1, original.StateHash, original.Capture())));
        var forged = JsonNode.Parse(bytes)!; forged["stateHash"] = new string('0', 64);
        Assert.Throws<InvalidDataException>(() => EndgameRuntimeSaveStore.Read(Combat(false), Adventure, Policy(false), Campaign, Endgame, forged.ToJsonString()));
    }
}
