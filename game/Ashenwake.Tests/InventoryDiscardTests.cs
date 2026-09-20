using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class InventoryDiscardTests
{
    private static string CombatJson => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json"));
    private static AdventureContent Adventure => AdventureContent.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "adventure.json")));
    private static ProgressionContent Policy => ProgressionContent.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "progression.json")));
    private static ProductionSession Fresh() => ProductionSession.Create(CombatJson, Adventure, Policy);
    private static ProductionSession Restore(ProductionSnapshot state) => ProductionSession.Restore(CombatJson, Adventure, Policy, state);

    private static ProductionSnapshot AtCapacity(int count, int drops = 0)
    {
        var state = Fresh().Capture(); var character = state.Progression.Character; var combat = state.Expedition.Combat;
        combat.Actors.Single(actor => actor.Id == 1).Position = new(2000, -2000);
        var definition = CombatContent.Parse(CombatJson).Items.Single(item => item.Id == "item.starter_head");
        var owned = character.Items.ToList();
        while (owned.Count < count)
        {
            long id = character.NextItemId++;
            var item = new CombatItem(id, definition.Id, definition.Name, definition.Slot, "Common", definition.Damage, definition.Armor, definition.CriticalBasisPoints);
            if (combat.Inventory.Count < 512) combat.Inventory.Add(item);
            owned.Add(new PermanentItem
            {
                Id = id,
                DefinitionId = item.DefinitionId,
                Rarity = ItemRarity.Common,
                BaseDamage = item.Damage,
                BaseArmor = item.Armor,
                BaseCriticalBasisPoints = item.CriticalBasisPoints
            });
        }
        character.Items = owned.ToArray();
        combat.NextObjectId = Math.Max(combat.NextObjectId, character.NextItemId);
        for (int index = 0; index < drops; index++)
        {
            long id = combat.NextObjectId++;
            combat.Loot.Add(new(id, new(2000, -2000), new(id, definition.Id, definition.Name, definition.Slot, "Common",
                definition.Damage, definition.Armor, definition.CriticalBasisPoints)));
        }
        return state;
    }

    [Fact]
    public void FullInventoryCanDiscardAndCollectAgainWithoutRewardOrOverflow()
    {
        var session = Restore(AtCapacity(511, drops: 2));
        var drops = session.Combat.View.Loot.Select(item => item.Id).ToArray();
        Assert.Contains(session.Step(new CombatCommand(CombatCommandKind.Pickup, ItemId: drops[0])), item => item.Kind == "LootPickedUp");
        Assert.Equal(512, session.Capture().Progression.Character.Items.Length);
        Assert.DoesNotContain(session.Step(new CombatCommand(CombatCommandKind.Pickup, ItemId: drops[1])), item => item.Kind == "LootPickedUp");
        var before = session.Capture().Progression;
        long unwanted = before.Character.Items.Last(item => item.Rarity == ItemRarity.Common && !before.Character.Equipment.Values.Contains(item.Id)).Id;
        var result = session.Discard(unwanted, confirmPermanent: true);
        Assert.True(result.Success, result.Reason); Assert.Contains("ItemDiscarded:" + unwanted, result.WorldEvents);
        Assert.Equal(511, session.Capture().Progression.Character.Items.Length);
        Assert.DoesNotContain(session.Combat.View.Inventory, item => item.Id == unwanted);
        Assert.Equal(before.Character.Materials, session.ProgressionView.Materials);
        Assert.Equal(before.Character.Experience, session.ProgressionView.Experience);
        Assert.Equal(JsonData.Hash(before.Profile), JsonData.Hash(session.Capture().Progression.Profile));
        Assert.Contains(session.Step(new CombatCommand(CombatCommandKind.Pickup, ItemId: drops[1])), item => item.Kind == "LootPickedUp");
        Assert.Equal(512, session.Combat.View.Inventory.Count); Assert.Equal(512, session.Capture().Progression.Character.Items.Length);
        VerifySaveAndReplay(session);
    }

    [Fact]
    public void DisposalWorksAtPermanentCapAndCannotBypassProjectedPickupLimit()
    {
        var session = Restore(AtCapacity(10512, drops: 1));
        long outsideProjection = session.Capture().Progression.Character.Items.Last().Id;
        Assert.DoesNotContain(session.Combat.View.Inventory, item => item.Id == outsideProjection);
        var result = session.Discard(outsideProjection, confirmPermanent: true);
        Assert.True(result.Success, result.Reason); Assert.Equal(10511, session.Capture().Progression.Character.Items.Length);
        Assert.Equal(512, session.Combat.View.Inventory.Count);
        string beforeRetry = session.StateHash;
        Assert.False(session.Discard(outsideProjection, confirmPermanent: true).Success); Assert.Equal(beforeRetry, session.StateHash);
        Assert.DoesNotContain(session.Step(new CombatCommand(CombatCommandKind.Pickup, ItemId: session.Combat.View.Loot.Single().Id)),
            item => item.Kind == "LootPickedUp");
        Assert.Equal(10511, session.Capture().Progression.Character.Items.Length);
        VerifySaveAndReplay(session);
    }

    [Fact]
    public void DisposalRequiresOwnershipConfirmationTorrenAndUnequippedItem()
    {
        var session = Restore(AtCapacity(30)); var state = session.Capture();
        long unwanted = state.Progression.Character.Items.Last().Id;
        long equipped = state.Progression.Character.Equipment.Values.First();
        foreach (var command in new[]
        {
            new ProductionCommand(ProductionAction.Discard, ItemId: unwanted),
            new ProductionCommand(ProductionAction.Discard, ItemId: long.MaxValue, ConfirmPermanent: true),
            new ProductionCommand(ProductionAction.Discard, ItemId: equipped, ConfirmPermanent: true)
        })
        {
            string before = session.StateHash; Assert.False(session.Execute(command).Success); Assert.Equal(before, session.StateHash);
        }
        var far = session.Capture(); far.Expedition.Combat.Actors.Single(actor => actor.Id == 1).Position = new(-4500, -1800);
        session = Restore(far); string outside = session.StateHash;
        Assert.False(session.Discard(unwanted, confirmPermanent: true).Success); Assert.Equal(outside, session.StateHash);
        VerifySaveAndReplay(session);
    }

    [Fact]
    public void DuplicateProgressionOperationCannotRemoveASecondItemOrGrantRewards()
    {
        var source = Fresh(); var state = AtCapacity(30).Progression;
        var session = ProgressionSession.Restore(source.Content, state);
        long unwanted = state.Character.Items.Last().Id;
        Assert.True(session.Discard("test.discard", unwanted, true).Success);
        string once = session.StateHash;
        var duplicate = session.Discard("test.discard", unwanted, true);
        Assert.True(duplicate.Success); Assert.Empty(duplicate.Events); Assert.Equal(once, session.StateHash);
        Assert.False(session.Discard("test.discard", unwanted - 1, true).Success); Assert.Equal(once, session.StateHash);
        Assert.Equal(state.Character.Materials, session.View.Materials);
    }

    [Fact]
    public void ExtraGodwroughtCopyCanBeDiscardedButLastCopyAndItsProgressSurvive()
    {
        var snapshot = AtCapacity(30); var character = snapshot.Progression.Character; var body = snapshot.Expedition.Combat;
        var retained = character.Items.Single(item => item.Rarity == ItemRarity.Godwrought);
        var extra = retained with { Id = character.NextItemId++, LegacyInstanceId = "ashcleaver.discard-test", BurningKills = 1000, Evolution = "Orrun" };
        character.Items = [.. character.Items, extra];
        var definition = CombatContent.Parse(CombatJson).Items.Single(item => item.Id == extra.DefinitionId);
        body.Inventory.Add(new(extra.Id, extra.DefinitionId, definition.Name, definition.Slot, "Godwrought", extra.BaseDamage, extra.BaseArmor, extra.BaseCriticalBasisPoints));
        body.NextObjectId = Math.Max(body.NextObjectId, character.NextItemId);
        snapshot.Expedition.GodwroughtItems[extra.LegacyInstanceId] = extra.Id;
        snapshot.Expedition.Adventure.Godwrought = [.. snapshot.Expedition.Adventure.Godwrought,
            new GodwroughtProgress { InstanceId = extra.LegacyInstanceId, BurningKills = extra.BurningKills, Evolution = extra.Evolution }];
        var session = Restore(snapshot);
        Assert.True(session.Discard(extra.Id, confirmPermanent: true).Success);
        var after = session.Capture();
        Assert.Single(after.Expedition.Adventure.Godwrought);
        Assert.DoesNotContain(extra.LegacyInstanceId, after.Expedition.GodwroughtItems.Keys);
        Assert.Equal(JsonData.Hash(retained), JsonData.Hash(after.Progression.Character.Items.Single(item => item.Id == retained.Id)));
        foreach (var slot in after.Progression.Character.Equipment.Where(pair => pair.Value == retained.Id).Select(pair => pair.Key))
            Assert.True(session.Unequip(slot).Success);
        string beforeLast = session.StateHash; var last = session.Discard(retained.Id, confirmPermanent: true);
        Assert.False(last.Success); Assert.Contains("last Godwrought", last.Reason); Assert.Equal(beforeLast, session.StateHash);
        VerifySaveAndReplay(session);
    }

    [Fact]
    public void ExistingProductionCommandJsonDoesNotAcquireANewDefaultField()
    {
        string old = JsonData.Write(new ProductionCommand(ProductionAction.Equip, ItemId: 42));
        Assert.DoesNotContain("confirmPermanent", old);
        Assert.False(JsonData.Read<ProductionCommand>(old).ConfirmPermanent);
        Assert.Equal(7, (int)ProductionAction.Mutation);
        Assert.Contains("confirmPermanent", JsonData.Write(new ProductionCommand(ProductionAction.Discard, ItemId: 42, ConfirmPermanent: true)));
    }

    private static void VerifySaveAndReplay(ProductionSession session)
    {
        var state = session.Capture();
        var restored = ProductionSaveStore.Read(CombatJson, Adventure, Policy, JsonData.Write(new ProductionSave(1, JsonData.Hash(state), state)));
        Assert.Equal(session.StateHash, restored.StateHash);
        var replay = JsonData.Read<ProductionReplay>(JsonData.Write(session.CaptureReplay()));
        var replayed = ProductionReplayRunner.Run(CombatJson, Adventure, Policy, replay);
        Assert.True(replayed.Success, replayed.Detail);
    }
}
