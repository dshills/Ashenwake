using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CampaignHollowExplorationTests
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
    private static readonly Lazy<CampaignRuntimeSnapshot> Rooms = new(() =>
    {
        var session = CampaignRuntimeSession.Create(Combat, Adventure, Policy, Content);
        for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && !(session.ActiveEncounterId == "campaign.repeating_rooms" && session.EncounterCleared); i++)
            Succeeded(session.Execute(CampaignRuntimeSmoke.Next(session)));
        Assert.Equal("campaign.repeating_rooms", session.ActiveEncounterId); Assert.True(session.EncounterCleared);
        Assert.NotEmpty(session.Combat.View.Loot); Succeeded(session.EnableExplorationMap());
        return RoundTrip(session).Capture();
    });
    private static readonly Lazy<CampaignRuntimeSnapshot> SecuredVault = new(() =>
    {
        var session = Restore(Rooms.Value); Interact(session, "hollow.vault.enter"); Fight(session, CampaignRuntimeSession.VaultEncounter);
        Assert.NotEmpty(session.Combat.View.Loot); return RoundTrip(session).Capture();
    });
    private static readonly Lazy<CampaignRuntimeSnapshot> Identity = new(() =>
    {
        var session = Restore(Rooms.Value); Advance(session); Fight(session, "campaign.identity_memory");
        return RoundTrip(session).Capture();
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
        Assert.Fail("Could not reach Hollow target " + target + " from " + session.Combat.View.Actors.Single(a => a.Id == 1).Position);
    }
    private static void Interact(CampaignRuntimeSession session, string id, CampaignRuntimeAction action = CampaignRuntimeAction.InteractHollow)
    {
        var target = session.Interactions.Single(i => i.ActionId == id);
        Approach(session, target.Position, target.Range - 80); Succeeded(session.Execute(new(action, Id: id)));
    }
    private static void Advance(CampaignRuntimeSession session)
    {
        Approach(session, HollowCampaignLayout.ForwardExit, HollowCampaignLayout.InteractionRange - 80); Succeeded(session.AdvanceEncounter());
    }

    [Fact]
    public void BranchesAndAdjoiningPassagesRequireTheCorrectSecuredAreaAndPhysicalApproach()
    {
        var session = Restore(Rooms.Value); Approach(session, HollowCampaignLayout.BackExit, 900); string before = session.StateHash;
        Assert.False(session.BeginExploration(CampaignRuntimeSession.VaultEvent).Success);
        Assert.False(session.AdvanceEncounter().Success); Assert.Equal(before, session.StateHash);
        Interact(session, "hollow.vault.enter"); before = session.StateHash;
        Assert.False(session.LeaveExploration().Success); Assert.False(session.Execute(new(CampaignRuntimeAction.InteractHollow, Id: "hollow.vault.treasure")).Success);
        Assert.Equal(before, session.StateHash);
        Interact(session, "hollow.vault.return"); Advance(session); Fight(session, "campaign.identity_memory");
        Assert.DoesNotContain(session.Interactions, i => i.ActionId == "hollow.vault.enter");
        Approach(session, HollowCampaignLayout.ForwardExit, HollowCampaignLayout.InteractionRange - 80);
        Assert.False(session.AdvanceEncounter().Success); // The future choice still gates the Breach Heart.
        Assert.False(session.Execute(new(CampaignRuntimeAction.RevisitEncounter, Id: "campaign.repeating_rooms")).Success);
        Interact(session, "hollow.back.rooms"); Assert.Equal("campaign.repeating_rooms", session.ActiveEncounterId);
        Assert.False(session.Execute(new(CampaignRuntimeAction.RevisitEncounter, Id: "campaign.breach_heart")).Success);
        RoundTrip(session);
    }

    [Fact]
    public void DirectAdvanceAndPhysicalPassageShareTheSameInclusiveInteractionBoundary()
    {
        var outside = Restore(Rooms.Value).Capture();
        outside.Combat.Actors.Single(a => a.Id == 1).Position = new(HollowCampaignLayout.ForwardExit.X - 1900, 0);
        var session = Restore(outside); string before = session.StateHash;
        Assert.False(session.AdvanceEncounter().Success);
        Assert.False(session.Execute(new(CampaignRuntimeAction.InteractHollow, Id: "hollow.forward.memory")).Success);
        Assert.Equal(before, session.StateHash);
        var boundary = session.Capture();
        boundary.Combat.Actors.Single(a => a.Id == 1).Position = new(HollowCampaignLayout.ForwardExit.X - HollowCampaignLayout.InteractionRange, 0);
        var direct = Restore(boundary); var physical = Restore(boundary);
        Succeeded(direct.AdvanceEncounter());
        Succeeded(physical.Execute(new(CampaignRuntimeAction.InteractHollow, Id: "hollow.forward.memory")));
        Assert.Equal("campaign.identity_memory", direct.ActiveEncounterId);
        Assert.Equal(direct.StateHash, physical.StateHash);
    }

    [Fact]
    public void VaultTestamentIsAtomicOneTimeAndRetainsAllUncollectedDropsAndRoomMemory()
    {
        var session = Restore(SecuredVault.Value); var initial = session.Capture();
        string vaultLoot = JsonData.Hash(initial.Combat.Loot), roomsLoot = JsonData.Hash(initial.ClearedRooms!["campaign.repeating_rooms"].Loot);
        int material = session.Production.ProgressionView.Materials;
        int[] fog = session.LocalMap!.SeenCells.ToArray();
        Interact(session, "hollow.vault.treasure");
        Assert.Contains("VaultTestamentClaimed", session.WorldEvents); Assert.Equal(material + 45, session.Production.ProgressionView.Materials);
        Assert.Contains(CampaignRuntimeSession.VaultEvent, session.Capture().Campaign.CompletedExploration);
        Assert.Contains("discovery.unremembered_vault", session.Capture().Production.Progression.Profile.Discoveries);
        var owned = initial.Production.Progression.Character.Items.Select(i => i.Id).ToHashSet();
        var reward = Assert.Single(session.Capture().Production.Progression.Character.Items, i => !owned.Contains(i.Id));
        Assert.Equal("item.echo_ring", reward.DefinitionId); Assert.Equal(ItemRarity.Legendary, reward.Rarity);
        string claimed = session.StateHash;
        Assert.False(session.Execute(new(CampaignRuntimeAction.InteractHollow, Id: "hollow.vault.treasure")).Success); Assert.Equal(claimed, session.StateHash);
        session = RoundTrip(session); Interact(session, "hollow.vault.return");
        Assert.Equal(roomsLoot, JsonData.Hash(session.Combat.Capture().Loot));
        Interact(session, "hollow.vault.enter"); Assert.True(session.EncounterCleared);
        Assert.Equal(vaultLoot, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.All(fog, cell => Assert.Contains(cell, session.LocalMap!.SeenCells));
        Assert.Equal(material + 45, session.Production.ProgressionView.Materials);
        Assert.Single(session.Capture().Production.Progression.Character.Items, i => i.Id == reward.Id);
        Assert.DoesNotContain(session.Interactions, i => i.ActionId == "hollow.vault.treasure");
        RoundTrip(session); Assert.True(CampaignRuntimeReplayRunner.Run(Combat, Adventure, Policy, Content, session.CaptureReplay()).Success);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("poisoned")]
    [InlineData("unearned")]
    public void VaultCompletionRejectsMissingPoisonedOrUnearnedEquipmentReceipts(string damage)
    {
        var session = Restore(SecuredVault.Value);
        if (damage != "unearned") Interact(session, "hollow.vault.treasure");
        var snapshot = session.Capture(); var receipts = snapshot.Production.Progression.Character.OperationReceipts;
        if (damage == "missing") receipts.Remove("campaign.vault.testament");
        else receipts["campaign.vault.testament"] = new string('A', 64);
        Assert.Throws<InvalidDataException>(() => Restore(snapshot));
    }

    [Fact]
    public void CachedDropsCannotImpersonateATestamentInPermanentInventoryOverflow()
    {
        var snapshot = Restore(SecuredVault.Value).Capture();
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
        var session = Restore(snapshot); Interact(session, "hollow.vault.treasure");
        var owned = permanent.Items.Select(i => i.Id).ToHashSet();
        var reward = Assert.Single(session.Capture().Production.Progression.Character.Items, i => !owned.Contains(i.Id));
        Assert.Equal("item.echo_ring", reward.DefinitionId); Assert.Equal(ItemRarity.Legendary, reward.Rarity);
        Assert.DoesNotContain(session.Combat.View.Inventory, i => i.Id == reward.Id);
        Interact(session, "hollow.vault.return"); snapshot = session.Capture();
        var vault = snapshot.ClearedRooms![CampaignRuntimeSession.VaultEncounter];
        definition = CombatContent.Parse(Combat).Items.Single(i => i.Id == reward.DefinitionId);
        var duplicate = new CombatItem(reward.Id, definition.Id, definition.Name, definition.Slot, "Legendary", definition.Damage, definition.Armor, definition.CriticalBasisPoints);
        vault.Loot.Add(new(duplicate.Id, vault.Actors.Single(a => a.Id == 1).Position, duplicate));
        CombatSession.Restore(Combat, vault); // Internally valid; only permanent overflow owns this identity.
        Assert.Throws<InvalidDataException>(() => Restore(snapshot));
    }

    [Fact]
    public void UnclaimedSecuredVaultCanBeLeftAndReenteredWithoutRespawningItsElite()
    {
        var session = Restore(SecuredVault.Value); string drops = JsonData.Hash(session.Combat.Capture().Loot);
        Interact(session, "hollow.vault.return"); session = RoundTrip(session); Interact(session, "hollow.vault.enter");
        Assert.True(session.EncounterCleared); Assert.Equal(drops, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.DoesNotContain(CampaignRuntimeSession.VaultEvent, session.Capture().Campaign.CompletedExploration);
        Interact(session, "hollow.vault.treasure"); RoundTrip(session);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VaultDeathRestoresItsParentAndKeepsOnlySecuredDrops(bool afterVictory)
    {
        var session = Restore(afterVictory ? SecuredVault.Value : Rooms.Value);
        if (afterVictory) Interact(session, "hollow.vault.treasure");
        else Interact(session, "hollow.vault.enter");
        var checkpoint = session.Capture();
        string parentLoot = JsonData.Hash(checkpoint.ClearedRooms!["campaign.repeating_rooms"].Loot);
        string vaultLoot = JsonData.Hash(checkpoint.Combat.Loot);
        int materials = session.Production.ProgressionView.Materials;
        var player = checkpoint.Combat.Actors.Single(a => a.Id == 1);
        player.Health = 0; player.DeathProcessed = true; player.Pending = null;
        session = Restore(checkpoint); Succeeded(session.Step());
        Assert.Equal("campaign.repeating_rooms", session.ActiveEncounterId);
        Assert.Equal(parentLoot, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.Null(session.Capture().Campaign.Exploration);
        Assert.Equal(materials, session.Production.ProgressionView.Materials);
        Assert.Equal(afterVictory, session.Capture().Campaign.CompletedExploration.Contains(CampaignRuntimeSession.VaultEvent));
        Assert.Equal(afterVictory, session.Capture().ClearedRooms?.ContainsKey(CampaignRuntimeSession.VaultEncounter) == true);
        Interact(session, "hollow.vault.enter");
        Assert.Equal(afterVictory, session.EncounterCleared);
        if (afterVictory) Assert.Equal(vaultLoot, JsonData.Hash(session.Combat.Capture().Loot));
        RoundTrip(session);
    }

    [Theory]
    [InlineData("share", "ending.shared_stewardship")]
    [InlineData("guard", "ending.guarded_transition")]
    public void BothFinalChoicesKeepThreeSealsEndingUnlocksAndBossLootAfterHubAndBacktracking(string choice, string ending)
    {
        var session = Restore(Identity.Value);
        Assert.Null(session.View.Ending);
        Succeeded(session.ReturnToHub()); Succeeded(session.EnterAct(5));
        Assert.Equal("campaign.identity_memory", session.ActiveEncounterId);
        Assert.True(session.EncounterCleared);
        Approach(session, HollowCampaignLayout.ForwardExit, HollowCampaignLayout.InteractionRange - 80);
        Assert.False(session.AdvanceEncounter().Success);
        Succeeded(session.Choose("choice.future", choice)); Advance(session);
        Assert.Equal(3, session.Combat.View.Actors.Count(a => a.DefinitionId == "enemy.seal_channel" && a.Health > 0));
        Fight(session, "campaign.breach_heart");
        Assert.Equal(ending, session.View.Ending!.Id);
        Assert.True(session.View.Ending.FracturesUnlocked);
        Assert.Contains("profile.fractures", session.Capture().Production.Progression.Profile.Unlocks);
        Assert.Contains("profile.god_hunts", session.Capture().Production.Progression.Profile.Unlocks);
        Assert.DoesNotContain(CampaignRuntimeSession.VaultEvent, session.Capture().Campaign.CompletedExploration);
        Assert.NotEmpty(session.Combat.View.Loot);
        string bossLoot = JsonData.Hash(session.Combat.Capture().Loot);
        int materials = session.Production.ProgressionView.Materials;
        long experience = session.Production.ProgressionView.Experience;
        Succeeded(session.ReturnToHub()); session = RoundTrip(session); Succeeded(session.EnterAct(5));
        Assert.Equal("campaign.repeating_rooms", session.ActiveEncounterId);
        Interact(session, "hollow.forward.memory"); Interact(session, "hollow.forward.breach");
        Assert.True(session.EncounterCleared);
        Assert.Equal(bossLoot, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.Empty(session.Combat.View.CampaignHazards!);
        var drop = session.Combat.View.Loot.First(); Approach(session, drop.Position, CombatSession.PickupRange - 80);
        Succeeded(session.Step(new CombatCommand(CombatCommandKind.Pickup, ItemId: drop.Id)));
        Interact(session, "hollow.back.memory"); Interact(session, "hollow.back.rooms");
        Interact(session, "hollow.forward.memory"); Interact(session, "hollow.forward.breach");
        Assert.DoesNotContain(session.Combat.View.Loot, item => item.Id == drop.Id);
        Assert.Single(session.Capture().Production.Progression.Character.Items, item => item.Id == drop.Id);
        Assert.Equal(materials, session.Production.ProgressionView.Materials);
        Assert.Equal(experience, session.Production.ProgressionView.Experience);
        Assert.Equal(ending, session.View.Ending!.Id);
        RoundTrip(session);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat, Adventure, Policy, Content, session.CaptureReplay()).Success);
    }

}
