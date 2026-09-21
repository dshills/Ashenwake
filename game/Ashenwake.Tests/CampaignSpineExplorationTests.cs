using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CampaignSpineExplorationTests
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
    private static readonly Lazy<CampaignRuntimeSnapshot> Causeway = new(() =>
    {
        var session = CampaignRuntimeSession.Create(Combat, Adventure, Policy, Content);
        for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && !(session.ActiveEncounterId == "campaign.bone_causeway" && session.EncounterCleared); i++)
            Succeeded(session.Execute(CampaignRuntimeSmoke.Next(session)));
        Assert.Equal("campaign.bone_causeway", session.ActiveEncounterId); Assert.True(session.EncounterCleared);
        Assert.NotEmpty(session.Combat.View.Loot); Succeeded(session.EnableExplorationMap());
        return RoundTrip(session).Capture();
    });
    private static readonly Lazy<CampaignRuntimeSnapshot> SecuredArchive = new(() =>
    {
        var session = Restore(Causeway.Value); Interact(session, "spine.archive.enter"); Fight(session, CampaignRuntimeSession.ArchiveEncounter);
        Assert.NotEmpty(session.Combat.View.Loot); return RoundTrip(session).Capture();
    });
    private static readonly Lazy<CampaignRuntimeSnapshot> Hall = new(() =>
    {
        var session = Restore(Causeway.Value); Advance(session); Fight(session, "campaign.contract_hall");
        return RoundTrip(session).Capture();
    });
    private static readonly Lazy<CampaignRuntimeSnapshot> SecuredMemory = new(() =>
    {
        var session = Restore(Hall.Value); Interact(session, "spine.memory.enter");
        Fight(session, CampaignRuntimeSession.MemoryEncounter);
        Assert.Contains(CampaignRuntimeSession.MemoryEvent, session.Capture().Campaign.CompletedExploration);
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
        Assert.Fail("Could not reach Spine target " + target + " from " + session.Combat.View.Actors.Single(a => a.Id == 1).Position);
    }
    private static void Interact(CampaignRuntimeSession session, string id, CampaignRuntimeAction action = CampaignRuntimeAction.InteractSpine)
    {
        var target = session.Interactions.Single(i => i.ActionId == id);
        Approach(session, target.Position, target.Range - 80); Succeeded(session.Execute(new(action, Id: id)));
    }
    private static void Advance(CampaignRuntimeSession session)
    {
        Approach(session, SpineCampaignLayout.ForwardExit, SpineCampaignLayout.InteractionRange - 80); Succeeded(session.AdvanceEncounter());
    }

    [Fact]
    public void BranchesAndAdjoiningPassagesRequireTheCorrectSecuredAreaAndPhysicalApproach()
    {
        var session = Restore(Causeway.Value); Approach(session, SpineCampaignLayout.BackExit, 900); string before = session.StateHash;
        Assert.False(session.BeginExploration(CampaignRuntimeSession.ArchiveEvent).Success);
        Assert.False(session.BeginExploration(CampaignRuntimeSession.MemoryEvent).Success);
        Assert.False(session.AdvanceEncounter().Success); Assert.Equal(before, session.StateHash);
        Interact(session, "spine.archive.enter"); before = session.StateHash;
        Assert.False(session.LeaveExploration().Success); Assert.False(session.Execute(new(CampaignRuntimeAction.InteractSpine, Id: "spine.archive.treasure")).Success);
        Assert.Equal(before, session.StateHash);
        Interact(session, "spine.archive.return"); Advance(session); Fight(session, "campaign.contract_hall");
        Assert.DoesNotContain(session.Interactions, i => i.ActionId == "spine.archive.enter");
        Approach(session, SpineCampaignLayout.ForwardExit, SpineCampaignLayout.InteractionRange - 80);
        Assert.False(session.AdvanceEncounter().Success); // The covenant choice still gates the Covenant Warden.
        Assert.False(session.Execute(new(CampaignRuntimeAction.RevisitEncounter, Id: "campaign.bone_causeway")).Success);
        Interact(session, "spine.back.causeway"); Assert.Equal("campaign.bone_causeway", session.ActiveEncounterId);
        Assert.False(session.Execute(new(CampaignRuntimeAction.RevisitEncounter, Id: "campaign.covenant_warden")).Success);
        RoundTrip(session);
    }

    [Fact]
    public void DirectAdvanceAndPhysicalPassageShareTheSameInclusiveInteractionBoundary()
    {
        var outside = Restore(Causeway.Value).Capture();
        outside.Combat.Actors.Single(a => a.Id == 1).Position = new(SpineCampaignLayout.ForwardExit.X - 1900, 0);
        var session = Restore(outside); string before = session.StateHash;
        Assert.False(session.AdvanceEncounter().Success);
        Assert.False(session.Execute(new(CampaignRuntimeAction.InteractSpine, Id: "spine.forward.hall")).Success);
        Assert.Equal(before, session.StateHash);
        var boundary = session.Capture();
        boundary.Combat.Actors.Single(a => a.Id == 1).Position = new(SpineCampaignLayout.ForwardExit.X - SpineCampaignLayout.InteractionRange, 0);
        var direct = Restore(boundary); var physical = Restore(boundary);
        Succeeded(direct.AdvanceEncounter());
        Succeeded(physical.Execute(new(CampaignRuntimeAction.InteractSpine, Id: "spine.forward.hall")));
        Assert.Equal("campaign.contract_hall", direct.ActiveEncounterId);
        Assert.Equal(direct.StateHash, physical.StateHash);
    }

    [Fact]
    public void ArchiveTestamentIsAtomicOneTimeAndRetainsAllUncollectedDropsAndRoomMemory()
    {
        var session = Restore(SecuredArchive.Value); var initial = session.Capture();
        string archiveLoot = JsonData.Hash(initial.Combat.Loot), causewayLoot = JsonData.Hash(initial.ClearedRooms!["campaign.bone_causeway"].Loot);
        int material = session.Production.ProgressionView.Materials;
        int[] fog = session.LocalMap!.SeenCells.ToArray();
        Interact(session, "spine.archive.treasure");
        Assert.Contains("ArchiveTestamentClaimed", session.WorldEvents); Assert.Equal(material + 40, session.Production.ProgressionView.Materials);
        Assert.Contains(CampaignRuntimeSession.ArchiveEvent, session.Capture().Campaign.CompletedExploration);
        Assert.Contains("discovery.oathkeeper_archive", session.Capture().Production.Progression.Profile.Discoveries);
        var reward = Assert.Single(session.Capture().Production.Progression.Character.Items, i => i.DefinitionId == "item.oath_plate" && i.Rarity == ItemRarity.Rare);
        string claimed = session.StateHash;
        Assert.False(session.Execute(new(CampaignRuntimeAction.InteractSpine, Id: "spine.archive.treasure")).Success); Assert.Equal(claimed, session.StateHash);
        session = RoundTrip(session); Interact(session, "spine.archive.return");
        Assert.Equal(causewayLoot, JsonData.Hash(session.Combat.Capture().Loot));
        Interact(session, "spine.archive.enter"); Assert.True(session.EncounterCleared);
        Assert.Equal(archiveLoot, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.All(fog, cell => Assert.Contains(cell, session.LocalMap!.SeenCells));
        Assert.Equal(material + 40, session.Production.ProgressionView.Materials);
        Assert.Single(session.Capture().Production.Progression.Character.Items, i => i.Id == reward.Id);
        Assert.DoesNotContain(session.Interactions, i => i.ActionId == "spine.archive.treasure");
        RoundTrip(session); Assert.True(CampaignRuntimeReplayRunner.Run(Combat, Adventure, Policy, Content, session.CaptureReplay()).Success);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("poisoned")]
    [InlineData("unearned")]
    public void ArchiveCompletionRejectsMissingPoisonedOrUnearnedEquipmentReceipts(string damage)
    {
        var session = Restore(SecuredArchive.Value);
        if (damage != "unearned") Interact(session, "spine.archive.treasure");
        var snapshot = session.Capture(); var receipts = snapshot.Production.Progression.Character.OperationReceipts;
        if (damage == "missing") receipts.Remove("campaign.archive.testament");
        else receipts["campaign.archive.testament"] = new string('A', 64);
        Assert.Throws<InvalidDataException>(() => Restore(snapshot));
    }

    [Fact]
    public void CachedDropsCannotImpersonateATestamentInPermanentInventoryOverflow()
    {
        var snapshot = Restore(SecuredArchive.Value).Capture();
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
        var session = Restore(snapshot); Interact(session, "spine.archive.treasure");
        var reward = Assert.Single(session.Capture().Production.Progression.Character.Items, i => i.DefinitionId == "item.oath_plate" && i.Rarity == ItemRarity.Rare);
        Assert.DoesNotContain(session.Combat.View.Inventory, i => i.Id == reward.Id);
        Interact(session, "spine.archive.return"); snapshot = session.Capture();
        var archive = snapshot.ClearedRooms![CampaignRuntimeSession.ArchiveEncounter];
        definition = CombatContent.Parse(Combat).Items.Single(i => i.Id == reward.DefinitionId);
        var duplicate = new CombatItem(reward.Id, definition.Id, definition.Name, definition.Slot, "Rare", definition.Damage, definition.Armor, definition.CriticalBasisPoints);
        archive.Loot.Add(new(duplicate.Id, archive.Actors.Single(a => a.Id == 1).Position, duplicate));
        CombatSession.Restore(Combat, archive); // Internally valid; only permanent overflow owns this identity.
        Assert.Throws<InvalidDataException>(() => Restore(snapshot));
    }

    [Fact]
    public void MemoryVictoryRecordsImmediatelyWithoutTimerAndRetainsLootAcrossSaveReturnRevisitAndPickup()
    {
        var session = Restore(SecuredMemory.Value); var initial = session.Capture();
        Assert.Null(initial.Campaign.Exploration); Assert.Equal("campaign.contract_hall", initial.ExplorationReturnEncounter);
        Assert.Contains("discovery.divine_seals", initial.Production.Progression.Profile.Discoveries);
        int material = session.Production.ProgressionView.Materials;
        string memoryLoot = JsonData.Hash(initial.Combat.Loot), hallLoot = JsonData.Hash(initial.ClearedRooms!["campaign.contract_hall"].Loot);
        int[] fog = session.LocalMap!.SeenCells.ToArray();
        for (int tick = 0; tick < 910; tick++) Succeeded(session.Execute(new(CampaignRuntimeAction.Tick), recordReplay: false));
        Assert.Equal(CampaignRuntimeSession.MemoryEncounter, session.ActiveEncounterId);
        Assert.Empty(session.Combat.View.CampaignHazards!); Assert.Equal(memoryLoot, JsonData.Hash(session.Combat.Capture().Loot));
        Interact(session, "spine.memory.return");
        Assert.Equal("campaign.contract_hall", session.ActiveEncounterId); Assert.Equal("", session.Capture().ExplorationReturnEncounter);
        Assert.Equal(material, session.Production.ProgressionView.Materials); Assert.Equal(hallLoot, JsonData.Hash(session.Combat.Capture().Loot));
        session = RoundTrip(session); Interact(session, "spine.memory.enter");
        Assert.True(session.EncounterCleared); Assert.Null(session.Capture().Campaign.Exploration);
        Assert.Equal(memoryLoot, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.All(fog, cell => Assert.Contains(cell, session.LocalMap!.SeenCells));
        var drop = session.Combat.View.Loot.First(); Approach(session, drop.Position, CombatSession.PickupRange - 80);
        Succeeded(session.Step(new CombatCommand(CombatCommandKind.Pickup, ItemId: drop.Id)));
        Interact(session, "spine.memory.return"); Interact(session, "spine.memory.enter");
        Assert.DoesNotContain(session.Combat.View.Loot, l => l.Id == drop.Id);
        Assert.Single(session.Capture().Production.Progression.Character.Items, i => i.Id == drop.Id);
        Assert.Equal(material, session.Production.ProgressionView.Materials);
        RoundTrip(session); Assert.True(CampaignRuntimeReplayRunner.Run(Combat, Adventure, Policy, Content, session.CaptureReplay()).Success);
    }

    [Fact]
    public void LiveMemoryHasNoStormTimeoutOrAutomaticReward()
    {
        var session = Restore(Hall.Value); Interact(session, "spine.memory.enter");
        var checkpoint = session.Capture(); int materials = session.Production.ProgressionView.Materials;
        checkpoint.Combat.Actors.Single(a => a.Id == 1).InvulnerableUntil = checkpoint.Combat.Tick + 1000;
        session = Restore(checkpoint);
        for (int tick = 0; tick < 910; tick++) Succeeded(session.Execute(new(CampaignRuntimeAction.Tick), recordReplay: false));
        Assert.Equal(CampaignRuntimeSession.MemoryEncounter, session.ActiveEncounterId);
        Assert.Equal(0, session.Capture().Campaign.Exploration!.RemainingTicks);
        Assert.DoesNotContain(CampaignRuntimeSession.MemoryEvent, session.Capture().Campaign.CompletedExploration);
        Assert.Equal(materials, session.Production.ProgressionView.Materials);
        Assert.Contains(session.Combat.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        RoundTrip(session);
    }

    [Fact]
    public void LeavingLiveMemoryReturnsToHallAndRestartsUntimedAttemptWithoutReward()
    {
        var session = Restore(Hall.Value); string drops = JsonData.Hash(session.Combat.Capture().Loot);
        Interact(session, "spine.memory.enter"); Interact(session, "spine.memory.return");
        Assert.Equal("campaign.contract_hall", session.ActiveEncounterId);
        Assert.Equal(drops, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.Null(session.Capture().Campaign.Exploration);
        Assert.DoesNotContain(CampaignRuntimeSession.MemoryEvent, session.Capture().Campaign.CompletedExploration);
        Interact(session, "spine.memory.enter"); Assert.Equal(0, session.Capture().Campaign.Exploration!.RemainingTicks);
        RoundTrip(session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WonMemorySurvivesHubTravelOrLaterDeathWithoutLosingDropsOrRepeatingReward(bool dieAfterVictory)
    {
        var session = Restore(SecuredMemory.Value); var won = session.Capture();
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
            Succeeded(session.ReturnToHub()); session = RoundTrip(session); Succeeded(session.EnterAct(4));
        }
        Assert.Equal("campaign.contract_hall", session.ActiveEncounterId);
        Assert.Equal(drops, JsonData.Hash(session.Capture().ClearedRooms![CampaignRuntimeSession.MemoryEncounter].Loot));
        Interact(session, "spine.memory.enter"); Assert.True(session.EncounterCleared);
        Assert.Equal(drops, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.Equal(items, JsonData.Hash(session.Capture().Production.Progression.Character.Items));
        Assert.Equal(materials, session.Production.ProgressionView.Materials);
        RoundTrip(session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveBranchDeathRestoresItsParentWithoutACompletionReward(bool memory)
    {
        var session = Restore(memory ? Hall.Value : Causeway.Value);
        string branch = memory ? CampaignRuntimeSession.MemoryEncounter : CampaignRuntimeSession.ArchiveEncounter;
        string parent = memory ? "campaign.contract_hall" : "campaign.bone_causeway";
        Interact(session, memory ? "spine.memory.enter" : "spine.archive.enter");
        var snapshot = session.Capture(); string loot = JsonData.Hash(snapshot.ClearedRooms![parent].Loot);
        var player = snapshot.Combat.Actors.Single(a => a.Id == 1); player.Health = 0; player.DeathProcessed = true; player.Pending = null;
        session = Restore(snapshot); Succeeded(session.Step());
        Assert.Equal(parent, session.ActiveEncounterId); Assert.True(session.EncounterCleared);
        Assert.Equal(loot, JsonData.Hash(session.Combat.Capture().Loot)); Assert.Null(session.Capture().Campaign.Exploration);
        Assert.DoesNotContain(memory ? CampaignRuntimeSession.MemoryEvent : CampaignRuntimeSession.ArchiveEvent, session.Capture().Campaign.CompletedExploration);
        Assert.False(session.Capture().ClearedRooms?.ContainsKey(branch) == true);
        Assert.Equal(session.Room.PlayerSpawn, session.Combat.View.Actors.Single(a => a.Id == 1).Position);
        RoundTrip(session);
    }

    [Fact]
    public void UnclaimedSecuredArchiveCanBeLeftAndReenteredWithoutRespawningItsElite()
    {
        var session = Restore(SecuredArchive.Value); string drops = JsonData.Hash(session.Combat.Capture().Loot);
        Interact(session, "spine.archive.return"); session = RoundTrip(session); Interact(session, "spine.archive.enter");
        Assert.True(session.EncounterCleared); Assert.Equal(drops, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.DoesNotContain(CampaignRuntimeSession.ArchiveEvent, session.Capture().Campaign.CompletedExploration);
        Interact(session, "spine.archive.treasure"); RoundTrip(session);
    }
}
