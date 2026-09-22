using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CampaignOpeningExplorationTests
{
    private const string Crypt = "exploration.widow_crypt", Event = "event.widow_crypt";
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static string Combat => CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson;
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent Policy => ProgressionContent.Parse(Read("progression.json"));
    private static CampaignContent Content => CampaignContent.Parse(Read("campaign.json"));
    private static CampaignRuntimeSession Restore(CampaignRuntimeSnapshot snapshot)
        => CampaignRuntimeSession.Restore(Combat, Adventure, Policy, Content, snapshot);
    private static CampaignRuntimeSession RoundTrip(CampaignRuntimeSession session)
    {
        var loaded = CampaignRuntimeSaveStore.Read(Combat, Adventure, Policy, Content,
            JsonData.Write(new CampaignRuntimeSave(1, session.StateHash, session.Capture())));
        Assert.Equal(session.StateHash, loaded.StateHash); return loaded;
    }

    private static readonly Lazy<CampaignRuntimeSnapshot> SecuredRoad = new(() =>
    {
        var session = CampaignRuntimeSession.Create(Combat, Adventure, Policy, Content);
        for (int i = 0; i < 6000 && !(session.ActiveEncounterId == "campaign.road" && session.EncounterCleared); i++)
            Succeeded(session.Execute(CampaignRuntimeSmoke.Next(session)));
        Assert.Equal("campaign.road", session.ActiveEncounterId); Assert.True(session.EncounterCleared);
        Assert.NotEmpty(session.Combat.View.Loot);
        return RoundTrip(session).Capture();
    });
    private static readonly Lazy<CampaignRuntimeSnapshot> EnteredCrypt = new(() =>
    {
        var session = Restore(SecuredRoad.Value); Interact(session, "opening.crypt.enter");
        Assert.Equal(Crypt, session.ActiveEncounterId); Assert.False(session.EncounterCleared);
        return RoundTrip(session).Capture();
    });
    private static readonly Lazy<CampaignRuntimeSnapshot> SecuredCrypt = new(() =>
    {
        var session = Restore(EnteredCrypt.Value); Fight(session, Crypt);
        Assert.NotEmpty(session.Combat.View.Loot); Assert.DoesNotContain(Event, session.Capture().Campaign.CompletedExploration);
        return RoundTrip(session).Capture();
    });
    private static readonly Lazy<CampaignRuntimeSnapshot> SecuredMonastery = new(() =>
    {
        var session = Restore(SecuredRoad.Value); Succeeded(session.AdvanceEncounter()); Fight(session, "campaign.monastery");
        return RoundTrip(session).Capture();
    });

    private static void Succeeded(CampaignRuntimeResult result) => Assert.True(result.Success, result.Reason);
    private static void Fight(CampaignRuntimeSession session, string encounter)
    {
        for (int i = 0; i < 6000 && session.ActiveEncounterId == encounter && !session.EncounterCleared; i++)
            Succeeded(session.Step(CampaignCombatSmoke.Commands(session.Combat.View, session.Room)));
        Assert.Equal(encounter, session.ActiveEncounterId); Assert.True(session.EncounterCleared);
    }
    private static void Approach(CampaignRuntimeSession session, Position target, int range)
    {
        for (int i = 0; i < 1200; i++)
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
        Assert.Fail("Could not reach opening target " + target + " from " + session.Combat.View.Actors.Single(a => a.Id == 1).Position);
    }
    private static void Interact(CampaignRuntimeSession session, string id)
    {
        var target = session.Interactions.Single(i => i.ActionId == id);
        Approach(session, target.Position, target.Range - 80);
        Succeeded(session.Execute(new(CampaignRuntimeAction.InteractOpening, Id: id)));
    }

    [Fact]
    public void CryptPassagesRequireSecuredRoadAndPhysicalApproachIncludingLegacyActions()
    {
        var fresh = CampaignRuntimeSession.Create(Combat, Adventure, Policy, Content); Succeeded(fresh.EnterAct(1));
        string initial = fresh.StateHash;
        Assert.False(fresh.BeginExploration(Event).Success);
        Assert.False(fresh.Execute(new(CampaignRuntimeAction.InteractOpening, Id: "opening.crypt.enter")).Success);
        Assert.Equal(initial, fresh.StateHash);
        var road = Restore(SecuredRoad.Value); Approach(road, OpeningCampaignLayout.BackExit, 1000);
        initial = road.StateHash;
        Assert.False(road.BeginExploration(Event).Success);
        Assert.False(road.Execute(new(CampaignRuntimeAction.InteractOpening, Id: "opening.crypt.enter")).Success);
        Assert.Equal(initial, road.StateHash);
        Interact(road, "opening.crypt.enter"); Assert.Equal(Crypt, road.ActiveEncounterId);
        initial = road.StateHash;
        Assert.False(road.LeaveExploration().Success);
        Assert.False(road.Execute(new(CampaignRuntimeAction.InteractOpening, Id: "opening.crypt.return")).Success);
        Assert.False(road.Execute(new(CampaignRuntimeAction.InteractOpening, Id: "opening.crypt.treasure")).Success);
        Assert.Equal(initial, road.StateHash);
    }

    [Fact]
    public void AbandoningLiveCryptRestoresRoadLootWithoutGrantingTheTestament()
    {
        var session = Restore(EnteredCrypt.Value); var before = session.Capture();
        string roadLoot = JsonData.Hash(before.ClearedRooms!["campaign.road"].Loot);
        Interact(session, "opening.crypt.return");
        Assert.Equal("campaign.road", session.ActiveEncounterId); Assert.Null(session.Capture().Campaign.Exploration);
        Assert.DoesNotContain(Event, session.Capture().Campaign.CompletedExploration);
        Assert.DoesNotContain("campaign.crypt.testament", session.Capture().Production.Progression.Character.OperationReceipts.Keys);
        Assert.Equal(before.Production.Progression.Character.Materials, session.Capture().Production.Progression.Character.Materials);
        Assert.Equal(JsonData.Hash(before.Production.Progression.Character.Items), JsonData.Hash(session.Capture().Production.Progression.Character.Items));
        Assert.Equal(roadLoot, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.False(session.Capture().ClearedRooms?.ContainsKey(Crypt) == true);
        session = RoundTrip(session); Interact(session, "opening.crypt.enter");
        Assert.False(session.EncounterCleared);
    }

    [Fact]
    public void CryptDeathRestoresDepletedCachedRoadAtItsAnchorWithoutLosingLoot()
    {
        var session = Restore(SecuredRoad.Value);
        var passage = session.Interactions.Single(i => i.ActionId == "opening.crypt.enter");
        Approach(session, passage.Position, passage.Range - 80);
        var depleted = session.Capture(); var road = depleted.Combat;
        var player = road.Actors.Single(a => a.Id == 1);
        player.Health = 7; player.Barrier = 0; player.Statuses.Clear();
        road.PotionCharges = 0; road.PotionReadyTick = road.Tick + 400; road.DodgeReadyTick = road.Tick + 300;
        road.Cooldowns["skill.cleave"] = road.Tick + 200;
        session = Restore(depleted);
        Succeeded(session.Execute(new(CampaignRuntimeAction.InteractOpening, Id: "opening.crypt.enter")));
        var entered = session.Capture(); var cached = entered.ClearedRooms!["campaign.road"];
        Assert.Equal(7, cached.Actors.Single(a => a.Id == 1).Health); Assert.Equal(0, cached.PotionCharges);
        Assert.True(cached.Cooldowns["skill.cleave"] > cached.Tick);
        string loot = JsonData.Hash(cached.Loot), items = JsonData.Hash(entered.Production.Progression.Character.Items);
        int materials = entered.Production.Progression.Character.Materials, deaths = entered.Campaign.Deaths;
        // Restore a valid one-health combat snapshot with a real lethal enemy strike
        // due next tick; the ordinary simulation and campaign death handler do the rest.
        var body = entered.Combat; player = body.Actors.Single(a => a.Id == 1);
        player.Health = 1; player.Barrier = 0; player.InvulnerableUntil = body.Tick;
        player.Pending = null; player.Statuses.Clear(); player.MoveX = player.MoveZ = 0;
        var enemy = body.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        enemy.Position = new(player.Position.X + 1000, player.Position.Z);
        enemy.Pending = new("enemy.strike", 1, player.Position, body.Tick + 1, body.NextActionId++, StartTick: body.Tick);
        enemy.RecoveryUntil = body.Tick + 20;
        session = Restore(entered);
        var deathEvents = new List<string>();
        for (int i = 0; i < 3 && session.ActiveEncounterId == Crypt; i++)
        {
            var result = session.Step(new CombatCommand(CombatCommandKind.Stop)); Succeeded(result);
            deathEvents.AddRange(result.WorldEvents);
        }
        Assert.Contains("CampaignCombatRestoredAtAnchor", deathEvents);
        Assert.Equal("campaign.road", session.ActiveEncounterId); Assert.True(session.EncounterCleared);
        var restored = session.Combat.View; var survivor = restored.Actors.Single(a => a.Id == 1);
        Assert.Equal(survivor.MaxHealth, survivor.Health); Assert.Equal(session.Room.PlayerSpawn, survivor.Position);
        Assert.Equal(3, restored.PotionCharges); Assert.Equal(0, restored.PotionCooldownTicks); Assert.Equal(0, restored.DodgeCooldownTicks);
        Assert.All(restored.Skills, skill => Assert.Equal(0, skill.RemainingTicks));
        Assert.Equal(deaths + 1, session.Capture().Campaign.Deaths); Assert.Null(session.Capture().Campaign.Exploration);
        Assert.DoesNotContain(Event, session.Capture().Campaign.CompletedExploration);
        Assert.Equal(loot, JsonData.Hash(session.Combat.Capture().Loot));
        Assert.Equal(items, JsonData.Hash(session.Capture().Production.Progression.Character.Items));
        Assert.Equal(materials, session.Capture().Production.Progression.Character.Materials);
        RoundTrip(session);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat, Adventure, Policy, Content, session.CaptureReplay()).Success);
    }

    [Fact]
    public void CachingSecuredRoadDiscardsStatusesBufferedInputAndPendingEffects()
    {
        var session = Restore(SecuredRoad.Value);
        var passage = session.Interactions.Single(i => i.ActionId == "opening.crypt.enter");
        Approach(session, passage.Position, passage.Range - 80);
        var snapshot = session.Capture(); var body = snapshot.Combat;
        var player = body.Actors.Single(a => a.Id == 1);
        int source = body.Actors.First(a => a.Faction == CombatFaction.Enemy).Id;
        player.Statuses.Add(new()
        {
            Id = "Burning",
            SourceId = source,
            OwnerId = source,
            NextTick = body.Tick + 15,
            ExpiresTick = body.Tick + 300,
            ActionId = body.NextActionId++
        });
        player.Pending = new("skill.iron_guard", 0, player.Position, body.Tick + 30, body.NextActionId++, StartTick: body.Tick);
        player.State = "Windup"; player.RecoveryUntil = body.Tick + 30; player.MoveX = 1;
        body.BufferedCommand = new(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: source);
        body.BufferExpiresTick = body.Tick + 20;
        body.Areas.Add(new(body.NextObjectId++, 1, 1, player.Position, 800, "skill.cataclysm", 12,
            DamageFamily.Fire, body.Tick + 5, body.Tick + 60, body.NextActionId++, 0));
        session = Restore(snapshot);
        Succeeded(session.Execute(new(CampaignRuntimeAction.InteractOpening, Id: "opening.crypt.enter")));
        var cached = session.Capture().ClearedRooms!["campaign.road"];
        Assert.Null(cached.BufferedCommand); Assert.Empty(cached.Projectiles); Assert.Empty(cached.Areas);
        Assert.All(cached.Actors, actor =>
        {
            Assert.Empty(actor.Statuses); Assert.Null(actor.Pending); Assert.Equal(0, actor.MoveX); Assert.Equal(0, actor.MoveZ);
        });
        Assert.Equal(JsonData.Hash(snapshot.Combat.Loot), JsonData.Hash(cached.Loot));
        session = RoundTrip(session); Interact(session, "opening.crypt.return");
        Assert.Null(session.Combat.Capture().BufferedCommand);
        Assert.All(session.Combat.View.Actors, actor => Assert.Empty(actor.Statuses));
        RoundTrip(session);
    }

    [Fact]
    public void TestamentGrantsOneRareArmorAndTwentyFiveMaterialsOnceAcrossSaveAndReentry()
    {
        var session = Restore(SecuredCrypt.Value);
        var before = session.Capture().Production.Progression.Character;
        var ownedIds = before.Items.Select(i => i.Id).ToHashSet();
        Approach(session, OpeningCampaignLayout.CryptReturn, 1000);
        string beforeRejected = session.StateHash;
        Assert.False(session.Execute(new(CampaignRuntimeAction.InteractOpening, Id: "opening.crypt.treasure")).Success);
        Assert.Equal(beforeRejected, session.StateHash);
        Interact(session, "opening.crypt.treasure");
        var after = session.Capture().Production.Progression.Character;
        var reward = Assert.Single(after.Items, i => !ownedIds.Contains(i.Id));
        Assert.Equal("item.serath_shroud", reward.DefinitionId); Assert.Equal(ItemRarity.Rare, reward.Rarity);
        Assert.Equal(150, reward.Affixes["affix.armor"]); Assert.Equal(3, reward.Affixes["affix.resource"]);
        Assert.Equal(before.Materials + 25, after.Materials);
        Assert.Contains(Event, session.Capture().Campaign.CompletedExploration);
        Assert.Contains("discovery.widow_crypt", session.Capture().Production.Progression.Profile.Discoveries);
        session = RoundTrip(session);
        beforeRejected = session.StateHash;
        Assert.False(session.Execute(new(CampaignRuntimeAction.InteractOpening, Id: "opening.crypt.treasure")).Success);
        Assert.Equal(beforeRejected, session.StateHash);
        Interact(session, "opening.crypt.return"); Interact(session, "opening.crypt.enter");
        Assert.True(session.EncounterCleared); Assert.DoesNotContain(session.Interactions, i => i.ActionId == "opening.crypt.treasure");
        Assert.Equal(after.Materials, session.Capture().Production.Progression.Character.Materials);
        Assert.Single(session.Capture().Production.Progression.Character.Items, i => i.Id == reward.Id);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat, Adventure, Policy, Content, session.CaptureReplay()).Success);
    }

    [Fact]
    public void UncollectedLootSurvivesCryptBacktrackingHubSaveAndLaterPickupWithoutDuplication()
    {
        var session = Restore(SecuredCrypt.Value); var initial = session.Capture();
        string roadLoot = JsonData.Hash(initial.ClearedRooms!["campaign.road"].Loot), cryptLoot = JsonData.Hash(initial.Combat.Loot);
        Interact(session, "opening.crypt.treasure"); Interact(session, "opening.crypt.return");
        Succeeded(session.AdvanceEncounter()); Fight(session, "campaign.monastery");
        string monasteryLoot = JsonData.Hash(session.Combat.Capture().Loot);
        Interact(session, "opening.back.road"); Assert.Equal(roadLoot, JsonData.Hash(session.Combat.Capture().Loot));
        Succeeded(session.ReturnToHub()); session = RoundTrip(session);
        Assert.Equal(roadLoot, JsonData.Hash(session.Capture().ClearedRooms!["campaign.road"].Loot));
        Assert.Equal(cryptLoot, JsonData.Hash(session.Capture().ClearedRooms![Crypt].Loot));
        Assert.Equal(monasteryLoot, JsonData.Hash(session.Capture().ClearedRooms!["campaign.monastery"].Loot));
        Succeeded(session.EnterAct(1)); Assert.Equal("campaign.monastery", session.ActiveEncounterId);
        Interact(session, "opening.back.road");
        var loot = session.Combat.View.Loot.First(); Approach(session, loot.Position, CombatSession.PickupRange - 80);
        Succeeded(session.Step(new CombatCommand(CombatCommandKind.Pickup, ItemId: loot.Id)));
        Assert.DoesNotContain(session.Combat.View.Loot, item => item.Id == loot.Id);
        Assert.Single(session.Capture().Production.Progression.Character.Items, i => i.Id == loot.Id);
        Interact(session, "opening.forward.monastery"); Interact(session, "opening.back.road");
        Assert.DoesNotContain(session.Combat.View.Loot, item => item.Id == loot.Id);
        Succeeded(session.ReturnToHub()); session = RoundTrip(session);
        Assert.Single(session.Capture().Production.Progression.Character.Items, i => i.Id == loot.Id);
        Assert.DoesNotContain(session.Capture().ClearedRooms!["campaign.road"].Loot, item => item.Id == loot.Id);
    }

    [Theory]
    [InlineData("owned-item")]
    [InlineData("other-room")]
    public void CachedLootCannotDuplicatePermanentOwnershipOrAnotherRoom(string duplicate)
    {
        var session = Restore(SecuredCrypt.Value); Interact(session, "opening.crypt.return");
        Succeeded(session.AdvanceEncounter()); var snapshot = session.Capture();
        var road = snapshot.ClearedRooms!["campaign.road"]; var crypt = snapshot.ClearedRooms[Crypt];
        if (duplicate == "owned-item")
        {
            var item = snapshot.Production.Expedition.Combat.Inventory.First();
            // Remove the cache's stale projected inventory copy so its own combat
            // validator cannot catch this cross-owner collision on our behalf.
            crypt.Inventory.RemoveAll(i => i.Id == item.Id);
            foreach (string slot in crypt.Equipment.Where(p => p.Value == item.Id).Select(p => p.Key).ToArray()) crypt.Equipment.Remove(slot);
            crypt.Loot.Add(new(item.Id, crypt.Actors.Single(a => a.Id == 1).Position, item));
        }
        else
        {
            var loot = road.Loot.First();
            crypt.Loot.Add(loot with { Position = crypt.Actors.Single(a => a.Id == 1).Position });
        }
        // The altered cached combat is internally valid; the campaign must reject
        // the duplicated identity across its independent owners.
        CombatSession.Restore(Combat, crypt);
        Assert.Throws<InvalidDataException>(() => Restore(snapshot));
    }

    [Fact]
    public void SecuredRoomTravelIsAdjacentAndPreservesTheStoryChoiceGate()
    {
        var session = Restore(SecuredMonastery.Value);
        Assert.False(session.AdvanceEncounter().Success);
        Assert.False(session.Execute(new(CampaignRuntimeAction.RevisitEncounter, Id: "campaign.bell_saint")).Success);
        Interact(session, "opening.back.road");
        Assert.False(session.Execute(new(CampaignRuntimeAction.RevisitEncounter, Id: "campaign.bell_saint")).Success);
        Interact(session, "opening.forward.monastery");
        Succeeded(session.Choose("choice.fragment", "reliquary")); Succeeded(session.AdvanceEncounter()); Fight(session, "campaign.bell_saint");
        string before = session.StateHash;
        Assert.False(session.Execute(new(CampaignRuntimeAction.RevisitEncounter, Id: "campaign.road")).Success);
        Assert.Equal(before, session.StateHash);
        Interact(session, "opening.back.monastery"); Interact(session, "opening.back.road");
        Assert.Equal("campaign.road", session.ActiveEncounterId); Assert.True(session.EncounterCleared);
        Assert.Equal("reliquary", session.Capture().Campaign.Choices["choice.fragment"]);
        RoundTrip(session);
    }
}
