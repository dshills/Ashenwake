using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CampaignDiscardTests
{
    [Fact]
    public void NestedDiscardRequiresRescuedTorrenAndPersistsThroughEndgameSaveAndReplay()
    {
        static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));
        var adventure = AdventureContent.Parse(Read("adventure.json"));
        var policy = ProgressionContent.Parse(Read("progression.json"));
        var campaign = CampaignContent.Parse(Read("campaign.json"));
        var endgame = EndgameContent.Parse(Read("endgame.json"));
        string combatJson = EndgameCombatContent.Parse(
            CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson,
            Read("endgame-combat.json"), endgame).CombatJson;
        var snapshot = EndgameRuntimeSession.Create(combatJson, adventure, policy, campaign, endgame).Capture();
        var character = snapshot.Campaign.Production.Progression.Character;
        var definition = CombatContent.Parse(combatJson).Items.Single(item => item.Id == "item.starter_head");
        long itemId = character.NextItemId++;
        var ordinary = new CombatItem(itemId, definition.Id, definition.Name, definition.Slot, "Common",
            definition.Damage, definition.Armor, definition.CriticalBasisPoints);
        character.Items = [.. character.Items, new PermanentItem
        {
            Id = itemId, DefinitionId = definition.Id, Rarity = ItemRarity.Common,
            BaseDamage = ordinary.Damage, BaseArmor = ordinary.Armor, BaseCriticalBasisPoints = ordinary.CriticalBasisPoints
        }];
        // Supply a valid owned-item boundary fixture without fabricating a rescue or its reward receipt.
        foreach (var body in new[] { snapshot.Campaign.Combat, snapshot.Campaign.Production.Expedition.Combat })
        {
            body.Inventory.Add(ordinary); body.NextObjectId = Math.Max(body.NextObjectId, character.NextItemId);
            body.Actors.Single(actor => actor.Id == 1).Position = new(2000, -2000);
        }
        var session = EndgameRuntimeSession.Restore(combatJson, adventure, policy, campaign, endgame, snapshot);
        var discard = new ProductionCommand(ProductionAction.Discard, ItemId: itemId, ConfirmPermanent: true);
        Assert.DoesNotContain(session.Interactions, interaction => interaction.ActionId == "service.torren");
        string beforeRescue = session.StateHash;
        var locked = session.ExecuteProduction(discard);
        Assert.False(locked.Success); Assert.Contains("Rescue this specialist", locked.Reason); Assert.Equal(beforeRescue, session.StateHash);

        for (int commands = 0; commands < 2000 && !session.Campaign.Capture().Campaign.RescuedResidents.Contains("Torren Bale"); commands++)
        {
            var result = session.Execute(EndgameRuntimeSmoke.Next(session));
            Assert.True(result.Success, result.Reason);
        }
        Assert.Contains("Torren Bale", session.Campaign.Capture().Campaign.RescuedResidents);
        Assert.False(session.InHub);
        string field = session.StateHash;
        Assert.False(session.ExecuteProduction(discard).Success); Assert.Equal(field, session.StateHash);
        Assert.True(session.Execute(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.ReturnToHub))).Success);
        Assert.Contains(session.Interactions, interaction => interaction.ActionId == "service.torren");

        int count = session.Production.Capture().Progression.Character.Items.Length;
        int materials = session.Production.ProgressionView.Materials;
        long experience = session.Production.ProgressionView.Experience;
        var atTorren = new CampaignRuntimeCommand(CampaignRuntimeAction.Production, Production: discard);
        bool reachedTorren = false;
        for (int commands = 0; commands < 300 && !reachedTorren; commands++)
        {
            var next = CampaignRuntimeSmoke.AtInteraction(session.Campaign, "service.torren", atTorren);
            if (next == atTorren) { reachedTorren = true; break; }
            var result = session.Execute(new(EndgameRuntimeAction.Campaign, Campaign: next));
            Assert.True(result.Success, result.Reason);
        }
        Assert.True(reachedTorren);
        var discarded = session.ExecuteProduction(discard);
        Assert.True(discarded.Success, discarded.Reason); Assert.Contains("ItemDiscarded:" + itemId, discarded.WorldEvents);
        Assert.Equal(count - 1, session.Production.Capture().Progression.Character.Items.Length);
        Assert.DoesNotContain(session.Combat.View.Inventory, item => item.Id == itemId);
        Assert.DoesNotContain(session.Production.Combat.View.Inventory, item => item.Id == itemId);
        Assert.Equal(materials, session.Production.ProgressionView.Materials);
        Assert.Equal(experience, session.Production.ProgressionView.Experience);
        string after = session.StateHash;
        Assert.False(session.ExecuteProduction(discard).Success); Assert.Equal(after, session.StateHash);

        var state = session.Capture();
        var loaded = EndgameRuntimeSaveStore.Read(combatJson, adventure, policy, campaign, endgame,
            JsonData.Write(new EndgameRuntimeSave(1, JsonData.Hash(state), state)));
        Assert.Equal(session.StateHash, loaded.StateHash);
        var replay = EndgameRuntimeReplayRunner.Run(combatJson, adventure, policy, campaign, endgame,
            JsonData.Read<EndgameRuntimeReplay>(JsonData.Write(session.CaptureReplay())));
        Assert.True(replay.Success, replay.Detail);
    }
}
