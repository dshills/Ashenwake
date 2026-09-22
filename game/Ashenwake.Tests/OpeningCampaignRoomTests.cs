using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;
using System.Text.Json.Nodes;
using Xunit;

namespace Ashenwake.Tests;

public sealed class OpeningCampaignRoomTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static string BaselineJson => File.ReadAllText(Path.Combine(Root, "content/combat.json"));
    private static string CampaignJson => File.ReadAllText(Path.Combine(Root, "content/campaign-combat.json"));
    private static CampaignCombatContent Content() => CampaignCombatContent.Parse(BaselineJson, CampaignJson);

    public static IEnumerable<object[]> OpeningRooms() => new[]
    {
        "campaign.road", "campaign.monastery", "campaign.bell_saint", "exploration.widow_crypt"
    }.Select(id => new object[] { id });

    [Fact]
    public void OptionalRoomFieldsPreserveLegacySerializationAlongsideAllAuthoredRegions()
    {
        var content = Content(); var registry = CombatContent.Parse(content.CombatJson);
        Assert.Equal("campaign-combat.midgame_depth.9", registry.Campaign!.Version);
        Assert.DoesNotContain("\"room\"", JsonData.Write(new CampaignCombatEncounter("campaign.example", "Example", "Memory", 0, [])));
        Assert.DoesNotContain("roomEncounterId", JsonData.Write(new CombatSnapshot()));
        var later = registry.Campaign.Encounters.Where(e => !OpeningCampaignLayout.Contains(e.Id) && !VerdantCampaignLayout.Contains(e.Id) && !CinderCampaignLayout.Contains(e.Id) && !SpineCampaignLayout.Contains(e.Id) && !HollowCampaignLayout.Contains(e.Id));
        Assert.Empty(later);
        var legacy = CampaignCombatContent.Parse(BaselineJson, File.ReadAllText(Path.Combine(Root, "fixtures/campaign-combat-phase4.json")));
        Assert.All(CombatContent.Parse(legacy.CombatJson).Campaign!.Encounters, encounter =>
        {
            Assert.Null(encounter.Room);
            Assert.Equal(JsonData.Hash(registry.Room), JsonData.Hash(legacy.CreateEncounter(encounter.Id).Room));
        });
        Assert.Equal(JsonData.Hash(registry.Room), JsonData.Hash(content.CreateEncounter("hub").Room));
        Assert.Equal(23, registry.Campaign.Encounters.Where(e => e.Room is not null).Select(e => JsonData.Hash(e.Room!)).Distinct().Count());
    }

    [Theory, MemberData(nameof(OpeningRooms))]
    public void AuthoredRoomsOwnTheirSpawnsCollisionAndRestore(string id)
    {
        var content = Content(); var session = content.CreateEncounter(id);
        var definition = CombatContent.Parse(content.CombatJson).Campaign!.Encounters.Single(e => e.Id == id);
        Assert.NotNull(definition.Room);
        Assert.Equal(JsonData.Hash(definition.Room!), JsonData.Hash(session.Room));
        Assert.Equal(12000, session.Room.HalfWidth); Assert.Equal(10000, session.Room.HalfDepth);
        Assert.Equal(new Position(-4500, 0), session.View.Actors[0].Position);
        Assert.Equal(definition.Spawns.Select(s => s.Position), session.View.Actors.Where(a => a.Faction == CombatFaction.Enemy).Select(a => a.Position));
        var copy = session.Room; copy.Obstacles[0] = new(-10000, -10000, 10000, 10000);
        Assert.Equal(JsonData.Hash(definition.Room!), JsonData.Hash(session.Room));
        var restored = CombatSession.Restore(content.CombatJson, session.Capture());
        Assert.Equal(session.StateHash, restored.StateHash);
        Assert.Equal(JsonData.Hash(session.Room), JsonData.Hash(restored.Room));
        var invalid = session.Capture(); var wall = session.Room.Obstacles[0];
        invalid.Actors[0].Position = new((wall.MinX + wall.MaxX) / 2, (wall.MinZ + wall.MaxZ) / 2);
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(content.CombatJson, invalid));
    }

    [Theory, MemberData(nameof(OpeningRooms))]
    public void ClearedBacktrackingRetainsLayoutAndCharacterWithoutRespawning(string id)
    {
        var content = Content(); var hub = content.CreateEncounter("hub").Capture();
        hub.Actors[0].Health = 231; hub.PotionCharges = 1;
        var session = content.CreateClearedEncounter(id, previous: hub);
        var saved = session.Capture();
        Assert.Equal("clear", saved.EncounterId); Assert.Equal(id, saved.RoomEncounterId);
        Assert.Null(saved.Campaign); Assert.Null(saved.Endgame);
        Assert.Equal(231, saved.Actors[0].Health); Assert.Equal(1, saved.PotionCharges);
        Assert.Equal(JsonData.Hash(hub.Inventory), JsonData.Hash(saved.Inventory));
        Assert.Equal(JsonData.Hash(hub.Rng), JsonData.Hash(saved.Rng));
        Assert.Equal(JsonData.Hash(content.CreateEncounter(id).Room), JsonData.Hash(session.Room));
        var restored = CombatSession.Restore(content.CombatJson, saved);
        for (int tick = 0; tick < 120; tick++)
        {
            Assert.Equal(JsonData.Hash(session.Step()), JsonData.Hash(restored.Step()));
            Assert.DoesNotContain(session.View.Actors, actor => actor.Faction == CombatFaction.Enemy);
            Assert.Empty(session.View.CampaignHazards!); Assert.Empty(session.View.Loot);
        }
        Assert.Equal(session.StateHash, restored.StateHash);
        var later = content.CreateEncounter("hub", previous: session.Capture());
        Assert.Null(later.Capture().RoomEncounterId);
        Assert.Equal(JsonData.Hash(CombatContent.Parse(content.CombatJson).Room), JsonData.Hash(later.Room));
        var returned = content.CreateEncounter("hub", previous: session.Capture());
        Assert.Null(returned.Capture().RoomEncounterId); Assert.Equal(350, returned.View.Actors[0].Health);
    }

    [Fact]
    public void RetainedRoomIdentityRejectsUnknownActiveAndNonAuthoredLayouts()
    {
        var content = Content(); var valid = content.CreateClearedEncounter("campaign.road").Capture();
        foreach (var id in new[] { "", "campaign.missing", "hub" })
        {
            Assert.Throws<ArgumentException>(() => content.CreateClearedEncounter(id));
            Assert.Throws<InvalidDataException>(() => CombatSession.Restore(content.CombatJson, valid with { RoomEncounterId = id }));
        }
        var legacy = CampaignCombatContent.Parse(BaselineJson, File.ReadAllText(Path.Combine(Root, "fixtures/campaign-combat-phase4.json")));
        Assert.Throws<ArgumentException>(() => legacy.CreateClearedEncounter("campaign.repeating_rooms"));
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(content.CombatJson, valid with { EncounterId = "hub" }));
        var active = content.CreateEncounter("campaign.road").Capture() with { RoomEncounterId = "campaign.road" };
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(content.CombatJson, active));
    }

    [Theory, MemberData(nameof(OpeningRooms))]
    public void AuthoredRoutesAndDoorwaysAreReachableWithRealMovement(string id)
    {
        var content = Content(); var session = content.CreateClearedEncounter(id);
        var room = session.Room; var space = new SpatialWorld(room);
        foreach (var target in OpeningCampaignLayout.Route(id)) WalkTo(session, room, target);
        if (id == "campaign.road") WalkTo(session, room, OpeningCampaignLayout.CryptEntrance);
        if (id == "exploration.widow_crypt") WalkTo(session, room, OpeningCampaignLayout.CryptReturn);
        else WalkTo(session, room, OpeningCampaignLayout.BackExit);
        Assert.All(session.View.Actors, actor => Assert.True(space.CanOccupy(actor.Position, CombatSession.ActorRadius)));
    }

    [Theory]
    [InlineData("enemy.ash_ghoul")]
    [InlineData("enemy.memory_archer")]
    [InlineData("enemy.funeral_guard")]
    [InlineData("enemy.cinder_priest")]
    public void EnemyApproachesAroundRoadDividerAndReplaysAfterRestore(string enemyId)
    {
        var content = Content(); var definition = CombatContent.Parse(content.CombatJson).Enemies.Single(e => e.Id == enemyId);
        var state = content.CreateEncounter("campaign.road").Capture();
        state.Actors[0].InvulnerableUntil = 2000;
        state.Actors.RemoveRange(2, state.Actors.Count - 2);
        state.Campaign!.Actors.Clear();
        state.Actors[1] = new CombatActor
        {
            Id = state.Actors[1].Id,
            DefinitionId = enemyId,
            Role = definition.Role,
            Faction = CombatFaction.Enemy,
            Position = new(3500, -3000),
            Health = definition.Health,
            MaxHealth = definition.Health,
            Armor = definition.Armor
        };
        int enemy = state.Actors[1].Id;
        state.Campaign.Actors.Add(enemy, new());
        var session = CombatSession.Restore(content.CombatJson, state);
        var replay = CombatSession.Restore(content.CombatJson, state);
        var room = session.Room; var space = new SpatialWorld(room);
        Assert.False(space.HasLineOfSight(state.Actors[1].Position, state.Actors[0].Position));
        int furthestSouth = int.MinValue; bool reached = false;
        for (int tick = 0; tick < 700; tick++)
        {
            Assert.Equal(JsonData.Hash(session.Step()), JsonData.Hash(replay.Step()));
            var actor = session.View.Actors.Single(a => a.Id == enemy);
            furthestSouth = Math.Max(furthestSouth, actor.Position.Z);
            Assert.True(space.CanOccupy(actor.Position, CombatSession.ActorRadius));
            if (tick % 47 == 0) replay = CombatSession.Restore(content.CombatJson, replay.Capture());
            if (space.HasLineOfSight(actor.Position, session.View.Actors[0].Position) &&
                Position.DistanceSquared(actor.Position, session.View.Actors[0].Position) <= (long)definition.Range * definition.Range)
            { reached = true; break; }
        }
        Assert.True(reached, enemyId + " never navigated around the divider.");
        Assert.True(furthestSouth > 1080, enemyId + " did not pass around the padded wall end.");
        Assert.Equal(session.StateHash, replay.StateHash);
    }

    [Fact]
    public void InvalidAndDisconnectedAuthoredRoomsAreRejected()
    {
        foreach (var mutation in new Action<JsonObject>[]
        {
            room => room["halfWidth"] = 7000,
            room => room["obstacles"] = JsonNode.Parse("[{\"minX\":-5000,\"minZ\":-1000,\"maxX\":-4000,\"maxZ\":1000}]"),
            room => room["obstacles"] = JsonNode.Parse("[{\"minX\":-800,\"minZ\":-10000,\"maxX\":800,\"maxZ\":10000}]")
        })
        {
            var campaign = JsonNode.Parse(CampaignJson)!;
            mutation(campaign["encounters"]![0]!["room"]!.AsObject());
            Assert.Throws<InvalidDataException>(() => CampaignCombatContent.Parse(BaselineJson, campaign.ToJsonString()));
        }
    }

    [Fact]
    public void CryptEliteDropsAtLeastRareEquipmentWithoutRespawnRewards()
    {
        var content = Content(); var initial = content.CreateEncounter("exploration.widow_crypt").Capture();
        var elite = initial.Actors.Single(actor => actor.Elite);
        Assert.Equal("enemy.funeral_guard", elite.DefinitionId);
        Assert.Equal("Memory", content.CreateEncounter("exploration.widow_crypt").View.CampaignRule);
        elite.Health = 1; elite.RecoveryUntil = 100;
        elite.Statuses.Add(new CombatStatus
        {
            Id = "Burning",
            SourceId = 1,
            OwnerId = 1,
            ActionId = initial.NextActionId++,
            NextTick = 0,
            ExpiresTick = 100
        });
        var session = CombatSession.Restore(content.CombatJson, initial);
        var events = session.Step();
        Assert.Contains(events, e => e.Kind == "EntityKilled" && e.TargetId == elite.Id);
        var loot = Assert.Single(session.View.Loot);
        Assert.Contains(loot.Item.Rarity, new[] { "Rare", "Relic", "Legendary" });
        Assert.DoesNotContain(session.Step(), e => e.Kind == "LootDropped");
    }

    private static void WalkTo(CombatSession session, RoomDefinition room, Position target)
    {
        Assert.True(new SpatialWorld(room).CanOccupy(target, CombatSession.ActorRadius));
        for (int tick = 0; tick < 650 && Position.DistanceSquared(session.View.Actors[0].Position, target) > 300L * 300; tick++)
        {
            var direction = CombatProductionSmoke.MovementDirection(session.View.Actors[0].Position, target, room);
            session.Step([new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]);
        }
        Assert.True(Position.DistanceSquared(session.View.Actors[0].Position, target) <= 300L * 300,
            "Unreachable route landmark " + target + " from " + session.View.Actors[0].Position);
        session.Step([new(CombatCommandKind.Stop)]);
    }
}
