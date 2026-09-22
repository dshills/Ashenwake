using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CampaignVerdantExplorationTests
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
    private static readonly Lazy<CampaignRuntimeSnapshot> Ruins = new(() =>
    {
        var session = CampaignRuntimeSession.Create(Combat, Adventure, Policy, Content);
        for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && !(session.ActiveEncounterId == "campaign.living_ruins" && session.EncounterCleared); i++)
            Succeeded(session.Execute(CampaignRuntimeSmoke.Next(session)));
        Assert.Equal("campaign.living_ruins", session.ActiveEncounterId); Assert.True(session.EncounterCleared);
        Assert.NotEmpty(session.Combat.View.Loot); Succeeded(session.EnableExplorationMap());
        return RoundTrip(session).Capture();
    });
    private static readonly Lazy<CampaignRuntimeSnapshot> SecuredShrine = new(() =>
    {
        var session = Restore(Ruins.Value); Interact(session, "verdant.shrine.enter"); Fight(session, CampaignRuntimeSession.ShrineEncounter);
        Assert.NotEmpty(session.Combat.View.Loot); return RoundTrip(session).Capture();
    });
    private static readonly Lazy<CampaignRuntimeSnapshot> Village = new(() =>
    {
        var session = Restore(Ruins.Value); Advance(session); Fight(session, "campaign.plague_village");
        return RoundTrip(session).Capture();
    });
    private static readonly Lazy<CampaignRuntimeSnapshot> SecuredHunt = new(() =>
    {
        var session = Restore(Village.Value); Interact(session, "verdant.hunt.enter");
        while (session.ActiveEncounterId == "clear")
        {
            string clue = session.Interactions.Single(i => i.ActionId.StartsWith("clue.", StringComparison.Ordinal)).ActionId;
            Interact(session, clue, CampaignRuntimeAction.TrackClue);
        }
        Fight(session, CampaignRuntimeSession.HuntEncounter);
        Assert.Contains(CampaignRuntimeSession.HuntEvent, session.Capture().Campaign.CompletedExploration);
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
        Assert.Fail("Could not reach Verdant target " + target + " from " + session.Combat.View.Actors.Single(a => a.Id == 1).Position);
    }
    private static void Interact(CampaignRuntimeSession session, string id, CampaignRuntimeAction action = CampaignRuntimeAction.InteractVerdant)
    {
        var target = session.Interactions.Single(i => i.ActionId == id);
        Approach(session, target.Position, target.Range - 80); Succeeded(session.Execute(new(action, Id: id)));
    }
    private static void Advance(CampaignRuntimeSession session)
    {
        Approach(session, VerdantCampaignLayout.ForwardExit, 1920); Succeeded(session.AdvanceEncounter());
    }

    [Fact]
    public void BranchesAndAdjoiningPassagesRequireTheCorrectSecuredAreaAndPhysicalApproach()
    {
        var session = Restore(Ruins.Value); Approach(session, VerdantCampaignLayout.BackExit, 900); string before = session.StateHash;
        Assert.False(session.BeginExploration(CampaignRuntimeSession.ShrineEvent).Success);
        Assert.False(session.BeginExploration(CampaignRuntimeSession.HuntEvent).Success);
        Assert.False(session.AdvanceEncounter().Success); Assert.Equal(before, session.StateHash);
        Interact(session, "verdant.shrine.enter"); before = session.StateHash;
        Assert.False(session.LeaveExploration().Success); Assert.False(session.Execute(new(CampaignRuntimeAction.InteractVerdant, Id: "verdant.shrine.treasure")).Success);
        Assert.Equal(before, session.StateHash);
        Interact(session, "verdant.shrine.return"); Advance(session); Fight(session, "campaign.plague_village");
        Assert.DoesNotContain(session.Interactions, i => i.ActionId == "verdant.shrine.enter");
        Approach(session, VerdantCampaignLayout.ForwardExit, 1920);
        Assert.False(session.AdvanceEncounter().Success); // Central transformation choice still gates Rootheart.
        Assert.False(session.Execute(new(CampaignRuntimeAction.RevisitEncounter, Id: "campaign.living_ruins")).Success);
        Interact(session, "verdant.back.ruins"); Assert.Equal("campaign.living_ruins", session.ActiveEncounterId);
        Assert.False(session.Execute(new(CampaignRuntimeAction.RevisitEncounter, Id: "campaign.rootheart")).Success);
        RoundTrip(session);
    }

    [Fact]
    public void ShrineTestamentIsAtomicOneTimeAndRetainsAllUncollectedDropsAndRoomMemory()
    {
        var session = Restore(SecuredShrine.Value); var initial = session.Capture();
        string shrineLoot = JsonData.Hash(initial.Combat.Loot), ruinsLoot = JsonData.Hash(initial.ClearedRooms!["campaign.living_ruins"].Loot);
        int material = session.Production.ProgressionView.Materials;
        int[] fog = session.LocalMap!.SeenCells.ToArray();
        Interact(session, "verdant.shrine.treasure");
        Assert.Contains("BriarTestamentClaimed", session.WorldEvents); Assert.Equal(material + 30, session.Production.ProgressionView.Materials);
        Assert.Contains(CampaignRuntimeSession.ShrineEvent, session.Capture().Campaign.CompletedExploration);
        Assert.Contains("discovery.briar_shrine", session.Capture().Production.Progression.Profile.Discoveries);
        var reward = Assert.Single(session.Capture().Production.Progression.Character.Items, i => i.DefinitionId == "item.stone_seal" && i.Rarity == ItemRarity.Rare);
        Assert.Equal(500, reward.Affixes["affix.critical"]); Assert.Equal(5, reward.Affixes["affix.resource"]);
        string claimed = session.StateHash;
        Assert.False(session.Execute(new(CampaignRuntimeAction.InteractVerdant, Id: "verdant.shrine.treasure")).Success); Assert.Equal(claimed, session.StateHash);
        session = RoundTrip(session); Interact(session, "verdant.shrine.return");
        Assert.Equal(ruinsLoot, JsonData.Hash(session.Combat.Capture().Loot));
        Interact(session, "verdant.shrine.enter"); Assert.True(session.EncounterCleared);
        Assert.Equal(shrineLoot, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.All(fog, cell => Assert.Contains(cell, session.LocalMap!.SeenCells));
        Assert.Equal(material + 30, session.Production.ProgressionView.Materials);
        Assert.Single(session.Capture().Production.Progression.Character.Items, i => i.Id == reward.Id);
        Assert.DoesNotContain(session.Interactions, i => i.ActionId == "verdant.shrine.treasure");
        RoundTrip(session); Assert.True(CampaignRuntimeReplayRunner.Run(Combat, Adventure, Policy, Content, session.CaptureReplay()).Success);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("poisoned")]
    [InlineData("unearned")]
    public void ShrineCompletionRejectsMissingPoisonedOrUnearnedEquipmentReceipts(string damage)
    {
        var session = Restore(SecuredShrine.Value);
        if (damage != "unearned") Interact(session, "verdant.shrine.treasure");
        var snapshot = session.Capture(); var receipts = snapshot.Production.Progression.Character.OperationReceipts;
        if (damage == "missing") receipts.Remove("campaign.briar.testament");
        else receipts["campaign.briar.testament"] = new string('A', 64);
        Assert.Throws<InvalidDataException>(() => Restore(snapshot));
    }

    [Fact]
    public void CachedDropsCannotImpersonateATestamentInPermanentInventoryOverflow()
    {
        var snapshot = Restore(SecuredShrine.Value).Capture();
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
        var session = Restore(snapshot); Interact(session, "verdant.shrine.treasure");
        var reward = Assert.Single(session.Capture().Production.Progression.Character.Items, i => i.DefinitionId == "item.stone_seal" && i.Rarity == ItemRarity.Rare);
        Assert.DoesNotContain(session.Combat.View.Inventory, i => i.Id == reward.Id);
        Interact(session, "verdant.shrine.return"); snapshot = session.Capture();
        var shrine = snapshot.ClearedRooms![CampaignRuntimeSession.ShrineEncounter];
        definition = CombatContent.Parse(Combat).Items.Single(i => i.Id == reward.DefinitionId);
        var duplicate = new CombatItem(reward.Id, definition.Id, definition.Name, definition.Slot, "Rare", definition.Damage, definition.Armor, definition.CriticalBasisPoints);
        shrine.Loot.Add(new(duplicate.Id, shrine.Actors.Single(a => a.Id == 1).Position, duplicate));
        CombatSession.Restore(Combat, shrine); // Internally valid; only permanent overflow owns this identity.
        Assert.Throws<InvalidDataException>(() => Restore(snapshot));
    }

    [Fact]
    public void HuntCluesAndCombatShareTheAuthoredGroveWithoutTeleportingOrResettingFog()
    {
        var session = Restore(Village.Value); Interact(session, "verdant.hunt.enter");
        Assert.Equal("clear", session.ActiveEncounterId); Assert.Equal(CampaignRuntimeSession.HuntEncounter, session.Combat.Capture().RoomEncounterId);
        Assert.Equal(CampaignRuntimeSession.HuntEncounter, session.LocalMap!.RoomId); string layout = session.LocalMap.LayoutHash;
        Assert.False(session.TrackClue("clue.heartwood_nest").Success);
        Interact(session, "clue.shed_bark", CampaignRuntimeAction.TrackClue);
        Interact(session, "clue.reversed_tracks", CampaignRuntimeAction.TrackClue);
        var final = session.Interactions.Single(i => i.ActionId == "clue.heartwood_nest"); Approach(session, final.Position, final.Range - 80);
        session = RoundTrip(session); var before = session.Combat.View.Actors.Single(a => a.Id == 1); int[] fog = session.LocalMap!.SeenCells.ToArray();
        Succeeded(session.TrackClue(final.ActionId));
        Assert.Equal(CampaignRuntimeSession.HuntEncounter, session.ActiveEncounterId);
        var after = session.Combat.View.Actors.Single(a => a.Id == 1);
        Assert.Equal(before.Position, after.Position); Assert.Equal(before.Health, after.Health);
        Assert.Equal(layout, session.LocalMap!.LayoutHash); Assert.Equal(fog, session.LocalMap.SeenCells);
        Assert.Contains(session.Combat.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        RoundTrip(session); Assert.True(CampaignRuntimeReplayRunner.Run(Combat, Adventure, Policy, Content, session.CaptureReplay()).Success);
    }

    [Fact]
    public void SecuredHuntReturnsToCachedVillageAndCanBeRevisitedWithoutRewardsEnemiesOrClues()
    {
        var session = Restore(SecuredHunt.Value); var initial = session.Capture();
        int material = session.Production.ProgressionView.Materials;
        string huntLoot = JsonData.Hash(initial.Combat.Loot), villageLoot = JsonData.Hash(initial.ClearedRooms!["campaign.plague_village"].Loot);
        int[] fog = session.LocalMap!.SeenCells.ToArray();
        Interact(session, "verdant.hunt.return");
        Assert.Equal("campaign.plague_village", session.ActiveEncounterId); Assert.Null(session.Capture().Campaign.Exploration);
        Assert.Contains(CampaignRuntimeSession.HuntEvent, session.Capture().Campaign.CompletedExploration);
        Assert.Equal(material, session.Production.ProgressionView.Materials); Assert.Equal(villageLoot, JsonData.Hash(session.Combat.Capture().Loot));
        session = RoundTrip(session); Interact(session, "verdant.hunt.enter");
        Assert.True(session.EncounterCleared); Assert.Equal(huntLoot, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.All(fog, cell => Assert.Contains(cell, session.LocalMap!.SeenCells));
        Assert.DoesNotContain(session.Interactions, i => i.ActionId.StartsWith("clue.", StringComparison.Ordinal));
        var drop = session.Combat.View.Loot.First(); Approach(session, drop.Position, CombatSession.PickupRange - 80);
        Succeeded(session.Step(new CombatCommand(CombatCommandKind.Pickup, ItemId: drop.Id)));
        Interact(session, "verdant.hunt.return"); Interact(session, "verdant.hunt.enter");
        Assert.DoesNotContain(session.Combat.View.Loot, l => l.Id == drop.Id);
        Assert.Single(session.Capture().Production.Progression.Character.Items, i => i.Id == drop.Id);
        Assert.Equal(material, session.Production.ProgressionView.Materials);
        RoundTrip(session); Assert.True(CampaignRuntimeReplayRunner.Run(Combat, Adventure, Policy, Content, session.CaptureReplay()).Success);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefeatedHuntSurvivesHubTravelOrLaterDeathWithoutLosingDropsOrRepeatingItsReward(bool dieAfterVictory)
    {
        var session = Restore(SecuredHunt.Value); var won = session.Capture();
        string drops = JsonData.Hash(won.Combat.Loot), items = JsonData.Hash(won.Production.Progression.Character.Items);
        int materials = session.Production.ProgressionView.Materials;
        int[] fog = session.LocalMap!.SeenCells.ToArray();
        if (dieAfterVictory)
        {
            var player = won.Combat.Actors.Single(a => a.Id == 1);
            player.Health = 0; player.DeathProcessed = true; player.Pending = null;
            session = Restore(won); Succeeded(session.Step());
        }
        else
        {
            Succeeded(session.ReturnToHub()); session = RoundTrip(session); Succeeded(session.EnterAct(2));
        }
        Assert.Equal("campaign.plague_village", session.ActiveEncounterId);
        Assert.Equal(drops, JsonData.Hash(session.Capture().ClearedRooms![CampaignRuntimeSession.HuntEncounter].Loot));
        Interact(session, "verdant.hunt.enter"); Assert.True(session.EncounterCleared);
        Assert.Equal(drops, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.Equal(items, JsonData.Hash(session.Capture().Production.Progression.Character.Items));
        Assert.Equal(materials, session.Production.ProgressionView.Materials);
        Assert.All(fog, cell => Assert.Contains(cell, session.LocalMap!.SeenCells));
        Assert.DoesNotContain(session.Interactions, i => i.ActionId.StartsWith("clue.", StringComparison.Ordinal));
        RoundTrip(session);
    }

    [Fact]
    public void AbandoningHuntTrackingKeepsVillageDropsAndDiscoveredFogButRestartsClues()
    {
        var session = Restore(Village.Value); string loot = JsonData.Hash(session.Combat.Capture().Loot);
        Interact(session, "verdant.hunt.enter"); Interact(session, "clue.shed_bark", CampaignRuntimeAction.TrackClue);
        int[] fog = session.LocalMap!.SeenCells.ToArray(); Interact(session, "verdant.hunt.return");
        Assert.Equal(loot, JsonData.Hash(session.Combat.Capture().Loot)); Assert.DoesNotContain(CampaignRuntimeSession.HuntEvent, session.Capture().Campaign.CompletedExploration);
        Interact(session, "verdant.hunt.enter"); Assert.Equal(0, session.Capture().Campaign.Exploration!.TrackedClues);
        Assert.All(fog, cell => Assert.Contains(cell, session.LocalMap!.SeenCells)); RoundTrip(session);
    }

    [Fact]
    public void LiveShrineDeathRestoresTheParentAnchorAndRetainsItsLootWithoutReward()
    {
        var session = Restore(Ruins.Value); Interact(session, "verdant.shrine.enter");
        var snapshot = session.Capture(); string loot = JsonData.Hash(snapshot.ClearedRooms!["campaign.living_ruins"].Loot);
        var player = snapshot.Combat.Actors.Single(a => a.Id == 1); player.Health = 0; player.DeathProcessed = true; player.Pending = null;
        session = Restore(snapshot); Succeeded(session.Step());
        Assert.Equal("campaign.living_ruins", session.ActiveEncounterId); Assert.True(session.EncounterCleared);
        Assert.Equal(loot, JsonData.Hash(session.Combat.Capture().Loot)); Assert.Null(session.Capture().Campaign.Exploration);
        Assert.DoesNotContain(CampaignRuntimeSession.ShrineEvent, session.Capture().Campaign.CompletedExploration);
        Assert.False(session.Capture().ClearedRooms?.ContainsKey(CampaignRuntimeSession.ShrineEncounter) == true);
        Assert.Equal(session.Room.PlayerSpawn, session.Combat.View.Actors.Single(a => a.Id == 1).Position);
        RoundTrip(session);
    }
}
