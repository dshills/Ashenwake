using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class EquipmentSetProjectionTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    [Theory]
    [InlineData("Vanguard")]
    [InlineData("Veilwalker")]
    [InlineData("Arcanist")]
    [InlineData("Gravecaller")]
    [InlineData("Warden")]
    public void PublicEquipAndUnequipProjectOnlyCompleteDistinctSetsForEveryDiscipline(string discipline)
    {
        string json = Read("combat.json"); var adventure = AdventureContent.Parse(Read("adventure.json")); var policy = ProgressionContent.Parse(Read("progression.json"));
        var state = ProductionSession.Create(json, adventure, policy, discipline: discipline).Capture();
        var combat = state.Expedition.Combat; combat.Actors[0].Position = new(2000, -2000);
        // Public pickups/import preserve real permanent item identity before the
        // same equip and unequip commands used by drag/drop update the build.
        foreach (string id in EquipmentSets.Catalog.SelectMany(s => s.PieceIds).Append(EquipmentSets.VigilHead))
        {
            var definition = CombatContent.Parse(json).Items.Single(i => i.Id == id); long objectId = combat.NextObjectId++;
            combat.Loot.Add(new(objectId, combat.Actors[0].Position, new(objectId, id, definition.Name, definition.Slot,
                "Legendary", definition.Damage, definition.Armor, definition.CriticalBasisPoints)));
        }
        var session = ProductionSession.Restore(json, adventure, policy, state);
        foreach (var loot in session.Combat.View.Loot.ToArray())
            Assert.Contains(session.Step(new CombatCommand(CombatCommandKind.Pickup, ItemId: loot.Id)), e => e.Kind == "LootPickedUp");
        bool Active(string id) => id switch
        { EquipmentSets.LastVigil => session.Combat.ProgressionBuild.LastVigilSet, EquipmentSets.Briarbound => session.Combat.ProgressionBuild.BriarboundSet, _ => session.Combat.ProgressionBuild.AshrunnerSet };
        foreach (var set in EquipmentSets.Catalog)
        {
            var first = session.Capture().Progression.Character.Items.First(i => i.DefinitionId == set.PieceIds[0]);
            var second = session.Capture().Progression.Character.Items.Single(i => i.DefinitionId == set.PieceIds[1]);
            var firstSlot = Enum.Parse<EquipmentSlot>(CombatContent.Parse(json).Items.Single(i => i.Id == first.DefinitionId).Slot);
            var secondSlot = Enum.Parse<EquipmentSlot>(CombatContent.Parse(json).Items.Single(i => i.Id == second.DefinitionId).Slot);
            Assert.False(Active(set.Id)); Assert.True(session.Equip(first.Id, firstSlot).Success); Assert.False(Active(set.Id));
            Assert.True(session.Equip(second.Id, secondSlot).Success); Assert.True(Active(set.Id));
            Assert.Equal(session.StateHash, ProductionSession.Restore(json, adventure, policy, session.Capture()).StateHash);
            Assert.True(session.Unequip(firstSlot).Success); Assert.False(Active(set.Id));
            Assert.True(session.Equip(first.Id, firstSlot).Success); Assert.True(Active(set.Id));
            Assert.True(session.Unequip(secondSlot).Success); Assert.False(Active(set.Id));
        }
        Assert.False(session.Combat.ProgressionBuild.LastVigilSet);
        Assert.False(session.Combat.ProgressionBuild.BriarboundSet);
        Assert.False(session.Combat.ProgressionBuild.AshrunnerSet);
    }
}
