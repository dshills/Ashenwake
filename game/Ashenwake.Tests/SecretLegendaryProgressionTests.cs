using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class SecretLegendaryProgressionTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static string Content => Read("combat.json");
    [Theory]
    [InlineData(LegendaryEquipment.Grief, LegendaryEquipment.GriefPower, "item.starter_head")]
    [InlineData(LegendaryEquipment.Widowthorn, LegendaryEquipment.WidowthornPower, "item.starter_mainhand")]
    [InlineData(LegendaryEquipment.Emberwake, LegendaryEquipment.EmberwakePower, "item.starter_shoulders")]
    public void SecretPowersCanBeExtractedAndEngravedWithSlotAndConfirmationRules(string item, string power, string receiver)
    {
        var content = ProductionContent.Resolve(Content, ProgressionContent.Parse(Read("progression.json")));
        var session = ProgressionSession.Create(content);
        foreach (string id in new[] { "mara", "torren", "cael", "oris", "kesh", "haven" }) Assert.True(session.CompleteObjective("quest." + id, "objective." + id).Success);
        Assert.True(session.EarnExperience("materials", 0, 500).Success);
        Assert.True(session.GrantItem("legendary", item, ItemRarity.Legendary).Success);
        Assert.True(session.GrantItem("receiver", receiver, ItemRarity.Rare).Success);
        Assert.True(session.GrantItem("wrong", "item.starter_ring1", ItemRarity.Rare).Success);
        string before = session.StateHash;
        Assert.False(session.Craft(new("extract", CraftingService.Extraction, 1)).Success); Assert.Equal(before, session.StateHash);
        Assert.True(session.Craft(new("extract", CraftingService.Extraction, 1, ConfirmPermanent: true)).Success);
        Assert.Contains(power, session.Capture().Character.PropertyLibrary);
        Assert.DoesNotContain(session.Capture().Character.Items, i => i.Id == 1);
        before = session.StateHash;
        Assert.False(session.Craft(new("wrong", CraftingService.Engraving, 3, PropertyId: power)).Success); Assert.Equal(before, session.StateHash);
        Assert.True(session.Craft(new("engrave", CraftingService.Engraving, 2, PropertyId: power)).Success);
        Assert.Equal(power, session.Capture().Character.Items.Single(i => i.Id == 2).Engraving);
        Assert.Equal(session.StateHash, ProgressionSession.Restore(content, session.Capture()).StateHash);
    }

    [Theory]
    [InlineData(LegendaryEquipment.Grief, LegendaryEquipment.GriefPower, "item.starter_head")]
    [InlineData(LegendaryEquipment.Widowthorn, LegendaryEquipment.WidowthornPower, "item.starter_gloves")]
    [InlineData(LegendaryEquipment.Emberwake, LegendaryEquipment.EmberwakePower, "item.starter_chest")]
    public void InnateAndEngravedSecretCopiesAreOnePowerWithIndependentEquipmentLifetimes(string item, string power, string receiver)
    {
        var adventure = AdventureContent.Parse(Read("adventure.json")); var policy = ProgressionContent.Parse(Read("progression.json"));
        var state = ProductionSession.Create(Content, adventure, policy).Capture(); var combat = state.Expedition.Combat;
        combat.Actors[0].Position = new(2000, -2000);
        foreach (string id in new[] { item, receiver })
        {
            var definition = CombatContent.Parse(Content).Items.Single(i => i.Id == id); long objectId = combat.NextObjectId++;
            combat.Loot.Add(new(objectId, combat.Actors[0].Position, new(objectId, id, definition.Name, definition.Slot,
                "Legendary", definition.Damage, definition.Armor, definition.CriticalBasisPoints)));
        }
        var session = ProductionSession.Restore(Content, adventure, policy, state);
        foreach (var loot in session.Combat.View.Loot.ToArray())
            Assert.Contains(session.Step(new CombatCommand(CombatCommandKind.Pickup, ItemId: loot.Id)), e => e.Kind == "LootPickedUp");
        state = session.Capture(); var character = state.Progression.Character;
        var innate = character.Items.Single(i => i.DefinitionId == item); var engraved = character.Items.Last(i => i.DefinitionId == receiver);
        engraved.Engraving = power; character.PropertyLibrary.Add(power);
        session = ProductionSession.Restore(Content, adventure, policy, state);
        var innateSlot = Enum.Parse<EquipmentSlot>(CombatContent.Parse(Content).Items.Single(i => i.Id == item).Slot);
        var engravedSlot = Enum.Parse<EquipmentSlot>(CombatContent.Parse(Content).Items.Single(i => i.Id == receiver).Slot);
        bool Active() => power switch
        {
            LegendaryEquipment.GriefPower => session.Combat.ProgressionBuild.GriefsReprieve,
            LegendaryEquipment.WidowthornPower => session.Combat.ProgressionBuild.Widowthorn,
            _ => session.Combat.ProgressionBuild.Emberwake
        };
        Assert.False(Active()); Assert.True(session.Equip(innate.Id, innateSlot).Success); Assert.True(Active());
        var once = session.Combat.ProgressionBuild;
        Assert.True(session.Equip(engraved.Id, engravedSlot).Success);
        var twice = session.Combat.ProgressionBuild;
        Assert.Equal((once.GriefsReprieve, once.Widowthorn, once.Emberwake), (twice.GriefsReprieve, twice.Widowthorn, twice.Emberwake));
        Assert.True(session.Unequip(innateSlot).Success); Assert.True(Active());
        Assert.True(session.Unequip(engravedSlot).Success); Assert.False(Active());
        Assert.Equal(session.StateHash, ProductionSession.Restore(Content, adventure, policy, session.Capture()).StateHash);
    }

}
