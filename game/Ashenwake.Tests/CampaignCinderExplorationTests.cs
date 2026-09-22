using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CampaignCinderExplorationTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static readonly Lazy<string> Composed = new(() => CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson);
    private static string Combat => Composed.Value;
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent Policy => ProgressionContent.Parse(Read("progression.json"));
    private static CampaignContent Content => CampaignContent.Parse(Read("campaign.json"));
    private static CampaignRuntimeSession Restore(CampaignRuntimeSnapshot snapshot) => CampaignRuntimeSession.Restore(Combat, Adventure, Policy, Content, snapshot);
    private static CampaignRuntimeSession RoundTrip(CampaignRuntimeSession session)
    {
        var loaded = CampaignRuntimeSaveStore.Read(Combat, Adventure, Policy, Content,
            JsonData.Write(new CampaignRuntimeSave(1, session.StateHash, session.Capture())));
        Assert.Equal(session.StateHash, loaded.StateHash); return loaded;
    }
    private static void Succeeded(CampaignRuntimeResult result) => Assert.True(result.Success, result.Reason);
    private static readonly Lazy<CampaignRuntimeSnapshot> Fields = new(() =>
    {
        var session = CampaignRuntimeSession.Create(Combat, Adventure, Policy, Content);
        for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && !(session.ActiveEncounterId == "campaign.cinder_pack" && session.EncounterCleared); i++)
            Succeeded(session.Execute(CampaignRuntimeSmoke.Next(session)));
        Assert.Equal("campaign.cinder_pack", session.ActiveEncounterId); Assert.True(session.EncounterCleared);
        Assert.NotEmpty(session.Combat.View.Loot); Succeeded(session.EnableExplorationMap());
        return RoundTrip(session).Capture();
    });
    private static readonly Lazy<CampaignRuntimeSnapshot> SecuredFoundry = new(() =>
    {
        var session = Restore(Fields.Value); Interact(session, "cinder.foundry.enter"); Fight(session, CampaignRuntimeSession.FoundryEncounter);
        Assert.NotEmpty(session.Combat.View.Loot); return RoundTrip(session).Capture();
    });
    private static readonly Lazy<CampaignRuntimeSnapshot> Floor = new(() =>
    {
        var session = Restore(Fields.Value); Advance(session); Fight(session, "campaign.extraction_floor");
        return RoundTrip(session).Capture();
    });
    private static readonly Lazy<CampaignRuntimeSnapshot> SecuredStorm = new(() =>
    {
        var session = Restore(Floor.Value); Interact(session, "cinder.storm.enter");
        Fight(session, CampaignRuntimeSession.StormEncounter);
        Assert.Contains(CampaignRuntimeSession.StormEvent, session.Capture().Campaign.CompletedExploration);
        Assert.NotEmpty(session.Combat.View.Loot); return RoundTrip(session).Capture();
    });
    private static void Fight(CampaignRuntimeSession session, string encounter)
    {
        for (int i = 0; i < 8000 && session.ActiveEncounterId == encounter && !session.EncounterCleared; i++)
            Succeeded(session.Step(CampaignCombatSmoke.Commands(session.Combat.View, session.Room)));
        Assert.Equal(encounter, session.ActiveEncounterId); Assert.True(session.EncounterCleared);
    }
    private static void Approach(CampaignRuntimeSession session, Position target, int range)
    {
        for (int i = 0; i < 1600; i++)
        {
            var view = session.Combat.View; var player = view.Actors.Single(a => a.Id == 1);
            if (Position.DistanceSquared(player.Position, target) <= (long)range * range)
            { Succeeded(session.Step(new CombatCommand(CombatCommandKind.Stop))); return; }
            var blockers = view.Actors.Where(a => a.Id != 1 && a.Health > 0).Select(a => a.Position).ToArray();
            var direction = CombatProductionSmoke.MovementDirection(player.Position, target, session.Room, blockers);
            var commands = new List<CombatCommand> { new(CombatCommandKind.Move, X: direction.X, Z: direction.Z) };
            if (player.Health < player.MaxHealth / 2 && view.PotionCharges > 0 && view.PotionCooldownTicks == 0) commands.Add(new(CombatCommandKind.Potion));
            Succeeded(session.Step(commands.ToArray()));
        }
        Assert.Fail("Could not reach Cinder target " + target + " from " + session.Combat.View.Actors.Single(a => a.Id == 1).Position);
    }
    private static void Interact(CampaignRuntimeSession session, string id, CampaignRuntimeAction action = CampaignRuntimeAction.InteractCinder)
    {
        var target = session.Interactions.Single(i => i.ActionId == id);
        Approach(session, target.Position, target.Range - 80); Succeeded(session.Execute(new(action, Id: id)));
    }
    private static void Advance(CampaignRuntimeSession session)
    {
        Approach(session, CinderCampaignLayout.ForwardExit, CinderCampaignLayout.InteractionRange - 80); Succeeded(session.AdvanceEncounter());
    }

    [Fact]
    public void BranchesAndAdjoiningPassagesRequireTheCorrectSecuredAreaAndPhysicalApproach()
    {
        var session = Restore(Fields.Value); Approach(session, CinderCampaignLayout.BackExit, 900); string before = session.StateHash;
        Assert.False(session.BeginExploration(CampaignRuntimeSession.FoundryEvent).Success);
        Assert.False(session.BeginExploration(CampaignRuntimeSession.StormEvent).Success);
        Assert.False(session.AdvanceEncounter().Success); Assert.Equal(before, session.StateHash);
        Interact(session, "cinder.foundry.enter"); before = session.StateHash;
        Assert.False(session.LeaveExploration().Success); Assert.False(session.Execute(new(CampaignRuntimeAction.InteractCinder, Id: "cinder.foundry.treasure")).Success);
        Assert.Equal(before, session.StateHash);
        Interact(session, "cinder.foundry.return"); Advance(session); Fight(session, "campaign.extraction_floor");
        Assert.DoesNotContain(session.Interactions, i => i.ActionId == "cinder.foundry.enter");
        Approach(session, CinderCampaignLayout.ForwardExit, CinderCampaignLayout.InteractionRange - 80);
        Assert.False(session.AdvanceEncounter().Success); // The extraction choice still gates the Furnace Spindle.
        Assert.False(session.Execute(new(CampaignRuntimeAction.RevisitEncounter, Id: "campaign.cinder_pack")).Success);
        Interact(session, "cinder.back.fields"); Assert.Equal("campaign.cinder_pack", session.ActiveEncounterId);
        Assert.False(session.Execute(new(CampaignRuntimeAction.RevisitEncounter, Id: "campaign.furnace_spindle")).Success);
        RoundTrip(session);
    }

    [Fact]
    public void DirectAdvanceAndPhysicalPassageShareTheSameInclusiveInteractionBoundary()
    {
        var outside = Restore(Fields.Value).Capture();
        outside.Combat.Actors.Single(a => a.Id == 1).Position = new(CinderCampaignLayout.ForwardExit.X - 1900, 0);
        var session = Restore(outside); string before = session.StateHash;
        Assert.False(session.AdvanceEncounter().Success);
        Assert.False(session.Execute(new(CampaignRuntimeAction.InteractCinder, Id: "cinder.forward.floor")).Success);
        Assert.Equal(before, session.StateHash);
        var boundary = session.Capture();
        boundary.Combat.Actors.Single(a => a.Id == 1).Position = new(CinderCampaignLayout.ForwardExit.X - CinderCampaignLayout.InteractionRange, 0);
        var direct = Restore(boundary); var physical = Restore(boundary);
        Succeeded(direct.AdvanceEncounter());
        Succeeded(physical.Execute(new(CampaignRuntimeAction.InteractCinder, Id: "cinder.forward.floor")));
        Assert.Equal("campaign.extraction_floor", direct.ActiveEncounterId);
        Assert.Equal(direct.StateHash, physical.StateHash);
    }

    [Fact]
    public void FoundryTestamentIsAtomicOneTimeAndRetainsAllUncollectedDropsAndRoomMemory()
    {
        var session = Restore(SecuredFoundry.Value); var initial = session.Capture();
        string foundryLoot = JsonData.Hash(initial.Combat.Loot), fieldsLoot = JsonData.Hash(initial.ClearedRooms!["campaign.cinder_pack"].Loot);
        int material = session.Production.ProgressionView.Materials;
        int[] fog = session.LocalMap!.SeenCells.ToArray();
        Interact(session, "cinder.foundry.treasure");
        Assert.Contains("FoundryTestamentClaimed", session.WorldEvents); Assert.Equal(material + 35, session.Production.ProgressionView.Materials);
        Assert.Contains(CampaignRuntimeSession.FoundryEvent, session.Capture().Campaign.CompletedExploration);
        Assert.Contains("discovery.sealed_foundry", session.Capture().Production.Progression.Profile.Discoveries);
        var reward = Assert.Single(session.Capture().Production.Progression.Character.Items, i => i.DefinitionId == "item.cinder_edge" && i.Rarity == ItemRarity.Rare);
        Assert.Equal(8, reward.Affixes["affix.damage"]); Assert.Equal(6, reward.Affixes["affix.resource"]);
        string claimed = session.StateHash;
        Assert.False(session.Execute(new(CampaignRuntimeAction.InteractCinder, Id: "cinder.foundry.treasure")).Success); Assert.Equal(claimed, session.StateHash);
        session = RoundTrip(session); Interact(session, "cinder.foundry.return");
        Assert.Equal(fieldsLoot, JsonData.Hash(session.Combat.Capture().Loot));
        Interact(session, "cinder.foundry.enter"); Assert.True(session.EncounterCleared);
        Assert.Equal(foundryLoot, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.All(fog, cell => Assert.Contains(cell, session.LocalMap!.SeenCells));
        Assert.Equal(material + 35, session.Production.ProgressionView.Materials);
        Assert.Single(session.Capture().Production.Progression.Character.Items, i => i.Id == reward.Id);
        Assert.DoesNotContain(session.Interactions, i => i.ActionId == "cinder.foundry.treasure");
        RoundTrip(session); Assert.True(CampaignRuntimeReplayRunner.Run(Combat, Adventure, Policy, Content, session.CaptureReplay()).Success);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("poisoned")]
    [InlineData("unearned")]
    public void FoundryCompletionRejectsMissingPoisonedOrUnearnedEquipmentReceipts(string damage)
    {
        var session = Restore(SecuredFoundry.Value);
        if (damage != "unearned") Interact(session, "cinder.foundry.treasure");
        var snapshot = session.Capture(); var receipts = snapshot.Production.Progression.Character.OperationReceipts;
        if (damage == "missing") receipts.Remove("campaign.foundry.testament");
        else receipts["campaign.foundry.testament"] = new string('A', 64);
        Assert.Throws<InvalidDataException>(() => Restore(snapshot));
    }

    [Fact]
    public void CachedDropsCannotImpersonateATestamentInPermanentInventoryOverflow()
    {
        var snapshot = Restore(SecuredFoundry.Value).Capture();
        var permanent = snapshot.Production.Progression.Character;
        var projection = snapshot.Production.Expedition.Combat;
        var definition = CombatContent.Parse(Combat).Items.Single(i => i.Id == "item.starter_head");
        while (projection.Inventory.Count < 512)
        {
            long id = permanent.NextItemId++;
            var item = new CombatItem(id, definition.Id, definition.Name, definition.Slot, "Common", definition.Damage, definition.Armor, definition.CriticalBasisPoints);
            projection.Inventory.Add(item); snapshot.Combat.Inventory.Add(item);
            permanent.Items = [.. permanent.Items, new PermanentItem { Id = id, DefinitionId = definition.Id, Rarity = ItemRarity.Common,
                BaseDamage = item.Damage, BaseArmor = item.Armor, BaseCriticalBasisPoints = item.CriticalBasisPoints }];
        }
        projection.NextObjectId = Math.Max(projection.NextObjectId, permanent.NextItemId);
        snapshot.Combat.NextObjectId = Math.Max(snapshot.Combat.NextObjectId, permanent.NextItemId);
        var session = Restore(snapshot); Interact(session, "cinder.foundry.treasure");
        var reward = Assert.Single(session.Capture().Production.Progression.Character.Items, i => i.DefinitionId == "item.cinder_edge" && i.Rarity == ItemRarity.Rare);
        Assert.DoesNotContain(session.Combat.View.Inventory, i => i.Id == reward.Id);
        Interact(session, "cinder.foundry.return"); snapshot = session.Capture();
        var foundry = snapshot.ClearedRooms![CampaignRuntimeSession.FoundryEncounter];
        definition = CombatContent.Parse(Combat).Items.Single(i => i.Id == reward.DefinitionId);
        var duplicate = new CombatItem(reward.Id, definition.Id, definition.Name, definition.Slot, "Rare", definition.Damage, definition.Armor, definition.CriticalBasisPoints);
        foundry.Loot.Add(new(duplicate.Id, foundry.Actors.Single(a => a.Id == 1).Position, duplicate));
        CombatSession.Restore(Combat, foundry); // Internally valid; only permanent overflow owns this identity.
        Assert.Throws<InvalidDataException>(() => Restore(snapshot));
    }

    [Fact]
    public void StormVictoryStopsTimerAndRetainsLootAcrossSaveReturnRevisitAndPickup()
    {
        var session = Restore(SecuredStorm.Value); var initial = session.Capture();
        Assert.Null(initial.Campaign.Exploration); Assert.Equal("campaign.extraction_floor", initial.ExplorationReturnEncounter);
        Assert.Contains("discovery.storm", initial.Production.Progression.Profile.Discoveries);
        int material = session.Production.ProgressionView.Materials;
        string stormLoot = JsonData.Hash(initial.Combat.Loot), floorLoot = JsonData.Hash(initial.ClearedRooms!["campaign.extraction_floor"].Loot);
        int[] fog = session.LocalMap!.SeenCells.ToArray();
        for (int tick = 0; tick < 910; tick++) Succeeded(session.Execute(new(CampaignRuntimeAction.Tick), recordReplay: false));
        Assert.Equal(CampaignRuntimeSession.StormEncounter, session.ActiveEncounterId);
        Assert.Empty(session.Combat.View.CampaignHazards!); Assert.Equal(stormLoot, JsonData.Hash(session.Combat.Capture().Loot));
        Interact(session, "cinder.storm.return");
        Assert.Equal("campaign.extraction_floor", session.ActiveEncounterId); Assert.Equal("", session.Capture().ExplorationReturnEncounter);
        Assert.Equal(material, session.Production.ProgressionView.Materials); Assert.Equal(floorLoot, JsonData.Hash(session.Combat.Capture().Loot));
        session = RoundTrip(session); Interact(session, "cinder.storm.enter");
        Assert.True(session.EncounterCleared); Assert.Null(session.Capture().Campaign.Exploration);
        Assert.Equal(stormLoot, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.All(fog, cell => Assert.Contains(cell, session.LocalMap!.SeenCells));
        var drop = session.Combat.View.Loot.First(); Approach(session, drop.Position, CombatSession.PickupRange - 80);
        Succeeded(session.Step(new CombatCommand(CombatCommandKind.Pickup, ItemId: drop.Id)));
        Interact(session, "cinder.storm.return"); Interact(session, "cinder.storm.enter");
        Assert.DoesNotContain(session.Combat.View.Loot, l => l.Id == drop.Id);
        Assert.Single(session.Capture().Production.Progression.Character.Items, i => i.Id == drop.Id);
        Assert.Equal(material, session.Production.ProgressionView.Materials);
        RoundTrip(session); Assert.True(CampaignRuntimeReplayRunner.Run(Combat, Adventure, Policy, Content, session.CaptureReplay()).Success);
    }

    [Fact]
    public void StormExpiryReturnsToSavedFloorAndRetryRestartsTimerWithoutReward()
    {
        var session = Restore(Floor.Value); string drops = JsonData.Hash(session.Combat.Capture().Loot);
        int materials = session.Production.ProgressionView.Materials;
        Interact(session, "cinder.storm.enter"); var checkpoint = session.Capture();
        checkpoint.Combat.Actors.Single(a => a.Id == 1).InvulnerableUntil = checkpoint.Combat.Tick + 1000;
        session = RoundTrip(Restore(checkpoint));
        for (int tick = 0; tick < 900; tick++) Succeeded(session.Execute(new(CampaignRuntimeAction.Tick), recordReplay: false));
        Assert.Contains("ExplorationEnded:event.resonance_storm:expired", session.WorldEvents);
        Assert.Equal("campaign.extraction_floor", session.ActiveEncounterId);
        Assert.Equal(drops, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.Null(session.Capture().Campaign.Exploration); Assert.Empty(session.Combat.View.CampaignHazards!);
        Assert.DoesNotContain(CampaignRuntimeSession.StormEvent, session.Capture().Campaign.CompletedExploration);
        Assert.False(session.Capture().ClearedRooms?.ContainsKey(CampaignRuntimeSession.StormEncounter) == true);
        Assert.Equal(materials, session.Production.ProgressionView.Materials);
        Interact(session, "cinder.storm.enter"); Assert.Equal(900, session.Capture().Campaign.Exploration!.RemainingTicks);
        Assert.Contains(session.Combat.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        RoundTrip(session);
    }

    [Fact]
    public void LeavingLiveStormReturnsToFloorAndRestartsAttemptWithoutReward()
    {
        var session = Restore(Floor.Value); string drops = JsonData.Hash(session.Combat.Capture().Loot);
        Interact(session, "cinder.storm.enter"); Interact(session, "cinder.storm.return");
        Assert.Equal("campaign.extraction_floor", session.ActiveEncounterId);
        Assert.Equal(drops, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.Null(session.Capture().Campaign.Exploration);
        Assert.DoesNotContain(CampaignRuntimeSession.StormEvent, session.Capture().Campaign.CompletedExploration);
        Interact(session, "cinder.storm.enter"); Assert.Equal(900, session.Capture().Campaign.Exploration!.RemainingTicks);
        RoundTrip(session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WonStormSurvivesHubTravelOrLaterDeathWithoutLosingDropsOrRepeatingReward(bool dieAfterVictory)
    {
        var session = Restore(SecuredStorm.Value); var won = session.Capture();
        string drops = JsonData.Hash(won.Combat.Loot), items = JsonData.Hash(won.Production.Progression.Character.Items);
        int materials = session.Production.ProgressionView.Materials;
        if (dieAfterVictory)
        {
            var player = won.Combat.Actors.Single(a => a.Id == 1);
            player.Health = 0; player.DeathProcessed = true; player.Pending = null;
            session = Restore(won); Succeeded(session.Step());
        }
        else
        {
            Succeeded(session.ReturnToHub()); session = RoundTrip(session); Succeeded(session.EnterAct(3));
        }
        Assert.Equal("campaign.extraction_floor", session.ActiveEncounterId);
        Assert.Equal(drops, JsonData.Hash(session.Capture().ClearedRooms![CampaignRuntimeSession.StormEncounter].Loot));
        Interact(session, "cinder.storm.enter"); Assert.True(session.EncounterCleared);
        Assert.Equal(drops, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.Equal(items, JsonData.Hash(session.Capture().Production.Progression.Character.Items));
        Assert.Equal(materials, session.Production.ProgressionView.Materials);
        RoundTrip(session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveBranchDeathRestoresItsParentWithoutACompletionReward(bool storm)
    {
        var session = Restore(storm ? Floor.Value : Fields.Value);
        string branch = storm ? CampaignRuntimeSession.StormEncounter : CampaignRuntimeSession.FoundryEncounter;
        string parent = storm ? "campaign.extraction_floor" : "campaign.cinder_pack";
        Interact(session, storm ? "cinder.storm.enter" : "cinder.foundry.enter");
        var snapshot = session.Capture(); string loot = JsonData.Hash(snapshot.ClearedRooms![parent].Loot);
        var player = snapshot.Combat.Actors.Single(a => a.Id == 1); player.Health = 0; player.DeathProcessed = true; player.Pending = null;
        session = Restore(snapshot); Succeeded(session.Step());
        Assert.Equal(parent, session.ActiveEncounterId); Assert.True(session.EncounterCleared);
        Assert.Equal(loot, JsonData.Hash(session.Combat.Capture().Loot)); Assert.Null(session.Capture().Campaign.Exploration);
        Assert.DoesNotContain(storm ? CampaignRuntimeSession.StormEvent : CampaignRuntimeSession.FoundryEvent, session.Capture().Campaign.CompletedExploration);
        Assert.False(session.Capture().ClearedRooms?.ContainsKey(branch) == true);
        Assert.Equal(session.Room.PlayerSpawn, session.Combat.View.Actors.Single(a => a.Id == 1).Position);
        RoundTrip(session);
    }

    [Fact]
    public void UnclaimedSecuredFoundryCanBeLeftAndReenteredWithoutRespawningItsElite()
    {
        var session = Restore(SecuredFoundry.Value); string drops = JsonData.Hash(session.Combat.Capture().Loot);
        Interact(session, "cinder.foundry.return"); session = RoundTrip(session); Interact(session, "cinder.foundry.enter");
        Assert.True(session.EncounterCleared); Assert.Equal(drops, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.DoesNotContain(CampaignRuntimeSession.FoundryEvent, session.Capture().Campaign.CompletedExploration);
        Interact(session, "cinder.foundry.treasure"); RoundTrip(session);
    }
}
