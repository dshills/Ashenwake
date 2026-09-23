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

public sealed class LateLegendaryAcquisitionTests
{
    private static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));
    private static string Content => Read("combat.json");
    [Theory]
    [InlineData("campaign.contract_hall", LegendaryEquipment.Crown)]
    [InlineData("campaign.identity_memory", LegendaryEquipment.Witness)]
    [InlineData("campaign.breach_heart", LegendaryEquipment.Hour)]
    public void NewCampaignRewardsPreserveLootDrawCountsAndMissingOldCatalogItemsFallBackNormally(string encounter, string item)
    {
        CombatSession Clear(bool previous)
        {
            var content = CampaignCombatContent.Parse(previous ? Read("fixtures/combat-midgame-legendary.json") : Content, Read("campaign-combat.json"));
            var state = content.CreateEncounter(encounter).Capture();
            state.Campaign!.BossPhase = 3;
            foreach (var enemy in state.Actors.Where(a => a.Faction == CombatFaction.Enemy))
            {
                enemy.Health = 1; enemy.Barrier = 0; enemy.Statuses.Add(new()
                { Id = "Burning", SourceId = 1, OwnerId = 1, ActionId = state.NextActionId++, NextTick = 0, ExpiresTick = 90 });
            }
            var session = CombatSession.Restore(content.CombatJson, state);
            // Let normal DOT damage exhaust any death-triggered ally barrier in both catalogs.
            for (int tick = 0; tick < 90 && session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0); tick++) session.Step();
            return session;
        }
        var previous = Clear(true); var current = Clear(false);
        Assert.DoesNotContain(current.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        Assert.Single(current.View.Loot, l => l.Item.DefinitionId == item);
        Assert.DoesNotContain(previous.View.Loot, l => l.Item.DefinitionId == item);
        Assert.Equal(previous.Capture().Rng, current.Capture().Rng);
        Assert.Equal(previous.Capture().NextObjectId, current.Capture().NextObjectId);
        Assert.Equal(previous.View.Loot.Count, current.View.Loot.Count);
    }

    [Theory]
    [InlineData("Vanguard")]
    [InlineData("Veilwalker")]
    [InlineData("Arcanist")]
    [InlineData("Gravecaller")]
    [InlineData("Warden")]
    public void EveryDisciplineCanEarnAllThreeThroughNormalCampaignCommandsAndEquipAtTorren(string discipline)
    {
        string combat = CampaignCombatContent.Parse(Content, Read("campaign-combat.json")).CombatJson;
        var adventure = AdventureContent.Parse(Read("adventure.json")); var progression = ProgressionContent.Parse(Read("progression.json"));
        var campaign = CampaignContent.Parse(Read("campaign.json"));
        var session = CampaignRuntimeSession.Create(combat, adventure, progression, campaign, discipline: discipline);
        string[] ids = [LegendaryEquipment.Crown, LegendaryEquipment.Witness, LegendaryEquipment.Hour];
        bool Earned() => ids.All(id => session.Capture().Production.Progression.Character.Items.Any(i => i.DefinitionId == id));
        var policy = new CampaignBalancePolicy(managedBuild: true);
        for (int i = 0; i < 36000 && !Earned(); i++)
        {
            var result = session.Execute(policy.Next(session)); Assert.True(result.Success, result.Reason);
            Assert.True(session.Combat.View.Actors[0].Health > 0, $"{discipline} died in {session.ActiveEncounterId}");
        }
        Assert.True(Earned(), $"{discipline} did not earn the three regional rewards");
        Assert.Contains("campaign.contract_hall", session.Capture().Campaign.CompletedEncounters);
        Assert.Contains("campaign.identity_memory", session.Capture().Campaign.CompletedEncounters);
        Assert.Contains("campaign.breach_heart", session.Capture().Campaign.CompletedEncounters);
        Assert.True(session.ReturnToHub().Success);
        foreach (string id in ids)
        {
            var earned = Assert.Single(session.Capture().Production.Progression.Character.Items, i => i.DefinitionId == id);
            Assert.Equal(ItemRarity.Legendary, earned.Rarity);
            var slot = Enum.Parse<EquipmentSlot>(CombatContent.Parse(Content).Items.Single(i => i.Id == id).Slot);
            for (int i = 0; i < 200 && session.Production.ProgressionView.Equipment.GetValueOrDefault(slot) != earned.Id; i++)
                Assert.True(session.Execute(CampaignRuntimeSmoke.AtInteraction(session, "service.torren", new(CampaignRuntimeAction.Production,
                    Production: new(ProductionAction.Equip, ItemId: earned.Id, Slot: slot)))).Success);
            Assert.Equal(earned.Id, session.Production.ProgressionView.Equipment[slot]);
        }
        Assert.True(session.Combat.ProgressionBuild.UnspokenVerdict); Assert.True(session.Combat.ProgressionBuild.WitnessVow); Assert.True(session.Combat.ProgressionBuild.BorrowedHour);
        Assert.Equal(session.StateHash, CampaignRuntimeSession.Restore(combat, adventure, progression, campaign, session.Capture()).StateHash);
        var replay = CampaignRuntimeReplayRunner.Run(combat, adventure, progression, campaign, session.CaptureReplay());
        Assert.True(replay.Success); Assert.Equal(session.StateHash, replay.FinalHash);
    }

    [Theory]
    [InlineData("hunt.orrun_without_oath", LegendaryEquipment.Crown)]
    [InlineData("hunt.thousand_memories", LegendaryEquipment.Witness)]
    [InlineData("hunt.nhal_reconstruction", LegendaryEquipment.Hour)]
    public void HuntRewardsDropOnlyAtFinalVictoryAndPreserveOldCatalogLootDraws(string hunt, string item)
    {
        (CombatSession Session, List<CombatEvent> Events) Run(bool previous, int phase)
        {
            var catalog = EndgameCombatContent.Parse(CampaignCombatContent.Parse(previous ? Read("fixtures/combat-midgame-legendary.json") : Content,
                Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), EndgameContent.Parse(Read("endgame.json")));
            var hub = CombatSession.CreateEncounter(catalog.CombatJson, 42, "hub");
            hub.ApplyProgressionBuild(new("Vanguard", Level: 20, Offense: 10, Defense: 8, FlatDamage: 12, Armor: 800, CriticalBasisPoints: 600)
            { Resistances = new() { [DamageFamily.Fire] = 2200, [DamageFamily.Frost] = 900, [DamageFamily.PhysicalCrush] = 1000 } });
            var session = catalog.CreateEncounter(catalog.CreateHuntManifest(hunt, 42, 1), phase, 0, hub.Capture());
            List<CombatEvent> events = [];
            for (int tick = 0; tick < 9000 && session.View.Actors[0].Health > 0 && session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0); tick++)
                events.AddRange(session.Step(EndgameCombatSmoke.Commands(session.View, session.Room)));
            Assert.True(session.View.Actors[0].Health > 0);
            Assert.DoesNotContain(session.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
            return (session, events);
        }
        for (int phase = 0; phase < 3; phase++)
        {
            var current = Run(false, phase); var previous = Run(true, phase);
            Assert.Equal(previous.Session.Capture().Rng, current.Session.Capture().Rng);
            Assert.Equal(previous.Session.Capture().NextObjectId, current.Session.Capture().NextObjectId);
            Assert.Equal(previous.Session.View.Loot.Count, current.Session.View.Loot.Count);
            Assert.DoesNotContain(previous.Session.View.Loot, l => l.Item.DefinitionId == item);
            if (phase < 2) Assert.DoesNotContain(current.Session.View.Loot, l => l.Item.DefinitionId == item);
            else
            {
                Assert.Equal("Legendary", Assert.Single(current.Session.View.Loot, l => l.Item.DefinitionId == item).Item.Rarity);
                var drop = Assert.Single(current.Events, e => e.Kind == "LootDropped" && e.ContentId == item);
                Assert.Equal(current.Events.Last(e => e.Kind == "EntityKilled").TargetId, drop.TargetId);
                current.Session.Step(); Assert.Single(current.Session.View.Loot, l => l.Item.DefinitionId == item);
            }
        }
    }
}
