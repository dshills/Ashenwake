using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Experiments;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class OpeningCatalogMigrationTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent Policy => ProgressionContent.Parse(Read("progression.json"));
    private static CampaignContent Campaign(bool old) => CampaignContent.Parse(Read(old ? "fixtures/campaign-phase4.json" : "campaign.json"));
    private static EndgameContent Endgame => EndgameContent.Parse(Read("endgame.json"));
    private static ExperimentContent Experiment => ExperimentContent.Parse(Read("experiments.json"));
    // These two frozen overlays are byte-for-byte catalogs from the d9ae4bb release.
    // The base registry already contains its three legendary items and is unchanged by this upgrade.
    private static string Journey(bool old) => CampaignCombatContent.Parse(Read("combat.json"),
        Read(old ? "fixtures/campaign-combat-phase4.json" : "campaign-combat.json")).CombatJson;
    private static string Combat(bool old) => EndgameCombatContent.Parse(Journey(old), Read("endgame-combat.json"), Endgame).CombatJson;
    private static CampaignRuntimeSession OldJourney() => CampaignRuntimeSession.Create(Journey(true), Adventure, Policy, Campaign(true));
    private static string Save(CampaignRuntimeSession session) => JsonData.Write(new CampaignRuntimeSave(1, session.StateHash, session.Capture()));
    private static CampaignRuntimeSession Upgrade(string json) => CampaignRuntimeSaveStore.Read(Journey(false), Adventure, Policy, Campaign(false), json);
    private static EndgameRuntimeSession OldEndgame() => EndgameRuntimeMigration.ImportPhaseFour(
        Read("fixtures/phase4-campaign-complete.json"), Journey(true), Combat(true), Adventure, Policy, Campaign(true), Endgame);

    private static void SameExceptCatalogsAndAuthoredPacing<T>(T before, T after)
    {
        string Normalize(T value, bool applyExpectedPacing)
        {
            var node = JsonNode.Parse(JsonData.Write(value))!;
            void RemoveIdentities(JsonNode? current)
            {
                if (current is JsonObject obj)
                    foreach (var pair in obj.ToArray())
                        if (pair.Key is "contentHash" or "adventureHash") obj.Remove(pair.Key);
                        else RemoveIdentities(pair.Value);
                else if (current is JsonArray array) foreach (var child in array) RemoveIdentities(child);
            }
            if (applyExpectedPacing) ApplyExpectedPacing(node);
            RemoveIdentities(node); return node.ToJsonString();
        }
        Assert.Equal(Normalize(before, true), Normalize(after, false));
    }

    // Build an expected archive from the published XP contract. No changed field
    // is ignored: the full before/after comparison still checks inventory, receipts,
    // combat actions, caches and every field unrelated to identities and pacing.
    private static void ApplyExpectedPacing(JsonNode node)
    {
        var previous = Campaign(true).Capture().Acts.SelectMany(act => act.Encounters).ToDictionary(e => e.Id);
        var current = Campaign(false).Capture().Acts.SelectMany(act => act.Encounters).ToDictionary(e => e.Id);
        IEnumerable<JsonObject> Objects(JsonNode? item)
        {
            if (item is JsonObject obj)
            {
                yield return obj;
                foreach (var pair in obj) foreach (var descendant in Objects(pair.Value)) yield return descendant;
            }
            else if (item is JsonArray array)
                foreach (var child in array) foreach (var descendant in Objects(child)) yield return descendant;
        }
        var objects = Objects(node).ToArray();
        int? level = null;
        foreach (var character in objects.Where(obj => obj["operationReceipts"] is JsonObject && obj.ContainsKey("experience")))
        {
            var receipts = character["operationReceipts"]!.AsObject();
            long difference = 0;
            foreach (var pair in receipts.ToArray().Where(pair => pair.Key.StartsWith("campaign.encounter.", StringComparison.Ordinal) &&
                previous.ContainsKey(pair.Key["campaign.encounter.".Length..])))
            {
                string id = pair.Key["campaign.encounter.".Length..];
                var old = previous[id]; var next = current[id];
                Assert.Equal(old.Materials, next.Materials);
                Assert.Equal(JsonData.Hash(new { Action = "Experience", amount = old.Experience, materials = old.Materials }), pair.Value!.GetValue<string>());
                receipts[pair.Key] = JsonData.Hash(new { Action = "Experience", amount = next.Experience, materials = next.Materials });
                difference += next.Experience - old.Experience;
            }
            Assert.True(difference >= 0);
            var progression = ProgressionSession.Create(Policy);
            long xp = Math.Min(progression.ExperienceForLevel(Policy.Capture().LevelCap), character["experience"]!.GetValue<long>() + difference);
            character["experience"] = xp;
            level = Enumerable.Range(1, Policy.Capture().LevelCap).Last(candidate => xp >= progression.ExperienceForLevel(candidate));
        }
        foreach (var narrative in objects.Where(obj => obj["completedEncounters"] is JsonArray && obj.ContainsKey("earnedExperience") && obj.ContainsKey("earnedMaterials")))
        {
            var completed = narrative["completedEncounters"]!.AsArray().Select(id => id!.GetValue<string>()).ToArray();
            Assert.Equal(completed.Sum(id => previous[id].Experience), narrative["earnedExperience"]!.GetValue<int>());
            narrative["earnedExperience"] = completed.Sum(id => current[id].Experience);
        }
        if (level is not null)
            foreach (var combat in objects.Where(obj => obj["progressionBuild"] is JsonObject))
            {
                combat["progressionBuild"]!["level"] = level.Value;
                combat["progressionBuild"]!["ultimateUnlocked"] = level.Value >= 10;
            }
    }

    [Fact]
    public void PublishedHubSavePreservesEveryCharacterAndCampaignFieldAndCreatesNoRoomCache()
    {
        var original = OldJourney(); string json = Save(original);
        Assert.DoesNotContain("clearedRooms", json); Assert.DoesNotContain("roomEncounterId", json);
        var loaded = Upgrade(json);
        Assert.True(loaded.InHub); Assert.Null(loaded.Capture().ClearedRooms);
        Assert.Equal(Campaign(false).Hash, loaded.Capture().Campaign.ContentHash);
        Assert.NotEqual(original.Production.Content.Hash, loaded.Production.Content.Hash);
        SameExceptCatalogsAndAuthoredPacing(original.Capture(), loaded.Capture());
        Assert.True(loaded.Step(new CombatCommand(CombatCommandKind.Move, X: 1)).Success);
        Assert.True(CampaignRuntimeReplayRunner.Run(Journey(false), Adventure, Policy, Campaign(false), loaded.CaptureReplay()).Success);
    }

    [Fact]
    public void NewlyObstructedActorsLootAndPendingEffectsRelocateWithoutLosingState()
    {
        var original = OldJourney(); Assert.True(original.EnterAct(1).Success);
        var oldRoom = new SpatialWorld(original.Room);
        var room = CombatContent.Parse(Journey(false)).Campaign!.Encounters.Single(e => e.Id == "campaign.road").Room!;
        var space = new SpatialWorld(room);
        var blocked = (from x in Enumerable.Range(-22, 45)
                       from z in Enumerable.Range(-18, 37)
                       let p = new Position(x * 500, z * 500)
                       where oldRoom.CanOccupy(p, CombatSession.ActorRadius) && !space.CanOccupy(p, 0)
                       select p).First();
        var snapshot = original.Capture(); var body = snapshot.Combat;
        var enemy = body.Actors.First(a => a.Faction == CombatFaction.Enemy);
        enemy.Position = blocked;
        enemy.Pending = new("enemy.strike", 1, blocked, body.Tick + 10, body.NextActionId++);
        long itemId = body.NextObjectId++; var item = body.Inventory[0] with { Id = itemId };
        body.Loot.Add(new(itemId, blocked, item));
        body.Areas.Add(new(body.NextObjectId++, 1, 1, blocked, 800, "skill.cataclysm", 12, DamageFamily.Fire,
            body.Tick + 5, body.Tick + 60, body.NextActionId++, 0));
        body.Projectiles.Add(new(body.NextObjectId++, 1, 1, blocked, blocked, enemy.Id, "skill.seismic_wave", 12,
            DamageFamily.PhysicalCrush, body.Tick + 90, body.NextActionId++, 0));
        original = CampaignRuntimeSession.Restore(Journey(true), Adventure, Policy, Campaign(true), snapshot);
        string json = Save(original); var loaded = Upgrade(json); var after = loaded.Capture().Combat;
        Assert.Equal(loaded.StateHash, Upgrade(json).StateHash);
        Assert.Equal(body.Rng, after.Rng); Assert.Equal(body.Tick, after.Tick);
        Assert.Equal(body.NextObjectId, after.NextObjectId); Assert.Equal(body.NextActionId, after.NextActionId);
        Assert.Equal(JsonData.Hash(body.Inventory), JsonData.Hash(after.Inventory));
        SameExceptCatalogsAndAuthoredPacing(snapshot.Production, loaded.Capture().Production);
        SameExceptCatalogsAndAuthoredPacing(snapshot.Campaign, loaded.Capture().Campaign);
        foreach (var actor in body.Actors)
        {
            var migrated = after.Actors.Single(a => a.Id == actor.Id);
            Assert.True(space.CanOccupy(migrated.Position, CombatSession.ActorRadius));
            if (space.CanOccupy(actor.Position, CombatSession.ActorRadius)) Assert.Equal(actor.Position, migrated.Position);
            var normalized = migrated with
            {
                Position = actor.Position,
                Pending = migrated.Pending is null ? null : migrated.Pending with { Target = actor.Pending!.Target }
            };
            Assert.Equal(JsonData.Hash(actor), JsonData.Hash(normalized));
            if (migrated.Pending is not null) Assert.True(space.CanOccupy(migrated.Pending.Target, 0));
        }
        Assert.NotEqual(blocked, after.Actors.Single(a => a.Id == enemy.Id).Position);
        Assert.All(after.Loot, loot => Assert.True(space.CanOccupy(loot.Position, 0)));
        Assert.Equal(body.Loot[0], after.Loot[0] with { Position = blocked });
        Assert.True(space.CanOccupy(after.Areas[0].Position, 0)); Assert.Equal(body.Areas[0], after.Areas[0] with { Position = blocked });
        Assert.True(space.CanOccupy(after.Projectiles[0].Position, 0)); Assert.True(space.CanOccupy(after.Projectiles[0].Target, 0));
        Assert.Equal(body.Projectiles[0], after.Projectiles[0] with { Position = blocked, Target = blocked });
        Assert.True(loaded.Step().Success);
        Assert.True(CampaignRuntimeReplayRunner.Run(Journey(false), Adventure, Policy, Campaign(false), loaded.CaptureReplay()).Success);
    }

    [Fact]
    public void ClearedOpeningUsesItsAuthoredRoomWithoutReplayingTheEncounterOrDiscardingLoot()
    {
        var original = CampaignRuntimeSaveStore.Read(Journey(true), Adventure, Policy, Campaign(true), Read("fixtures/phase4-campaign-complete.json"));
        Assert.True(original.EnterAct(1).Success);
        var snapshot = original.Capture();
        // Published revisits represented their cleared arena without a layout identity.
        snapshot = snapshot with
        {
            ActiveEncounterId = "campaign.road",
            ClearedRooms = null,
            Combat = snapshot.Combat with { EncounterId = "clear", RoomEncounterId = null }
        };
        original = CampaignRuntimeSession.Restore(Journey(true), Adventure, Policy, Campaign(true), snapshot);
        var loaded = Upgrade(Save(original));
        Assert.True(loaded.EncounterCleared); Assert.Equal("clear", loaded.Combat.EncounterId);
        Assert.Equal("campaign.road", loaded.Combat.Capture().RoomEncounterId);
        Assert.DoesNotContain(loaded.Combat.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        SameExceptCatalogsAndAuthoredPacing(original.Capture().Production, loaded.Capture().Production);
        SameExceptCatalogsAndAuthoredPacing(original.Capture().Campaign, loaded.Capture().Campaign);
        Assert.Equal(JsonData.Hash(original.Combat.Capture().Inventory), JsonData.Hash(loaded.Combat.Capture().Inventory));
    }

    [Fact]
    public void ActiveFinalActMigratesItsRoomAndOldReplayRemainsBoundToTheOldCatalog()
    {
        var original = OldJourney();
        for (int i = 0; i < 40000 && original.ActiveEncounterId != "campaign.repeating_rooms"; i++)
            Assert.True(original.Execute(CampaignRuntimeSmoke.Next(original)).Success);
        Assert.Equal("campaign.repeating_rooms", original.ActiveEncounterId);
        Assert.False(original.EncounterCleared);
        Assert.True(CampaignRuntimeReplayRunner.Run(Journey(true), Adventure, Policy, Campaign(true), original.CaptureReplay()).Success);
        var loaded = Upgrade(Save(original));
        SameExceptCatalogsAndAuthoredPacing(original.Capture().Production, loaded.Capture().Production);
        SameExceptCatalogsAndAuthoredPacing(original.Capture().Campaign, loaded.Capture().Campaign);
        Assert.Equal(original.ActiveEncounterId, loaded.ActiveEncounterId);
        Assert.Equal(JsonData.Hash(original.Combat.Capture().Inventory), JsonData.Hash(loaded.Combat.Capture().Inventory));
        Assert.Equal(original.Combat.Capture().Rng, loaded.Combat.Capture().Rng);
        Assert.NotEqual(JsonData.Hash(original.Room), JsonData.Hash(loaded.Room));
        var space = new SpatialWorld(loaded.Room);
        Assert.All(loaded.Combat.View.Actors, actor => Assert.True(space.CanOccupy(actor.Position, CombatSession.ActorRadius)));
        Assert.True(loaded.Step().Success);
        Assert.True(CampaignRuntimeReplayRunner.Run(Journey(false), Adventure, Policy, Campaign(false), loaded.CaptureReplay()).Success);
    }

    [Fact]
    public void FutureCachedCombatIsCompatibilityFailureBeforeTypedDeserializationOrBackupRecovery()
    {
        var original = CampaignRuntimeSaveStore.Read(Journey(true), Adventure, Policy, Campaign(true), Read("fixtures/phase4-campaign-complete.json"));
        var loaded = Upgrade(Save(original));
        Assert.True(loaded.EnterAct(1).Success);
        for (int i = 0; i < 1200 && loaded.ActiveEncounterId != "exploration.widow_crypt"; i++)
            Assert.True(loaded.Execute(CampaignRuntimeSmoke.AtInteraction(loaded, "opening.crypt.enter",
                new(CampaignRuntimeAction.InteractOpening, Id: "opening.crypt.enter"))).Success);
        Assert.Equal("exploration.widow_crypt", loaded.ActiveEncounterId);
        Assert.NotNull(loaded.Capture().ClearedRooms);
        var node = JsonNode.Parse(Save(loaded))!;
        var room = node["state"]!["clearedRooms"]!["campaign.road"]!;
        room["schemaVersion"] = 2; room["futureField"] = "preserve this archive";
        Assert.Throws<SaveCompatibilityException>(() => Upgrade(node.ToJsonString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EndgameHubAndActiveFracturePreserveProgressionAndEveryUnchangedRoomPosition(bool active)
    {
        var original = OldEndgame();
        if (active)
        {
            for (int i = 0; i < 500 && original.InHub; i++) Assert.True(original.Execute(EndgameRuntimeSmoke.Next(original)).Success);
            Assert.False(original.InHub);
            for (int i = 0; i < 12; i++) Assert.True(original.Step().Success);
        }
        var loaded = EndgameRuntimeSaveStore.Read(Combat(false), Adventure, Policy, Campaign(false), Endgame,
            JsonData.Write(new EndgameRuntimeSave(1, original.StateHash, original.Capture())));
        SameExceptCatalogsAndAuthoredPacing(original.Capture(), loaded.Capture());
        Assert.Equal(Campaign(false).Hash, loaded.Capture().Campaign.Campaign.ContentHash);
        Assert.True(loaded.Step().Success);
        Assert.True(EndgameRuntimeReplayRunner.Run(Combat(false), Adventure, Policy, Campaign(false), Endgame, loaded.CaptureReplay()).Success);
    }

    [Fact]
    public void BoundEchoesSavePreservesMemoryTimersAndThePermanentCharacter()
    {
        var original = ExperimentRuntimeSession.FromEndgame(Combat(true), Adventure, Policy, Campaign(true), Endgame, Experiment, OldEndgame().Capture());
        for (int i = 0; i < 4000 && original.View.Memory?.Status != "Bound"; i++)
            Assert.True(original.Execute(ExperimentRuntimeSmoke.Next(original)).Success);
        Assert.Equal("Bound", original.View.Memory?.Status);
        var loaded = ExperimentSaveStore.Read(Combat(false), Adventure, Policy, Campaign(false), Endgame, Experiment,
            JsonData.Write(new ExperimentSave(1, original.StateHash, original.Capture())));
        SameExceptCatalogsAndAuthoredPacing(original.Capture(), loaded.Capture());
        Assert.True(loaded.Step().Success);
        Assert.True(ExperimentReplayRunner.Run(Combat(false), Adventure, Policy, Campaign(false), Endgame, Experiment, loaded.CaptureReplay()).Success);
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("invalid-old-position")]
    [InlineData("future-version")]
    [InlineData("unknown-catalog")]
    public void OriginalAuthenticationPrecedesRelocationOrIdentityChanges(string tampering)
    {
        var original = OldJourney(); Assert.True(original.EnterAct(1).Success);
        var node = JsonNode.Parse(Save(original))!;
        switch (tampering)
        {
            case "checksum": node["stateHash"] = new string('0', 64); break;
            case "invalid-old-position":
                node["state"]!["combat"]!["actors"]![0]!["position"]!["x"] = 999999;
                node["stateHash"] = JsonData.Hash(JsonData.Read<CampaignRuntimeSnapshot>(node["state"]!.ToJsonString())); break;
            case "future-version": node["state"]!["combat"]!["schemaVersion"] = 2; break;
            case "unknown-catalog": node["state"]!["campaign"]!["contentHash"] = new string('0', 64); break;
        }
        string originalBytes = node.ToJsonString();
        if (tampering is "future-version" or "unknown-catalog") Assert.Throws<SaveCompatibilityException>(() => Upgrade(originalBytes));
        else Assert.Throws<InvalidDataException>(() => Upgrade(originalBytes));
        Assert.Equal(originalBytes, node.ToJsonString());
    }

    [Fact]
    public void LoadIsReadOnlyAndFirstExplicitWritePreservesBothOriginalArchivesAsBackups()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-opening-migration-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "character.json");
        try
        {
            var original = OldJourney();
            CampaignRuntimeSaveStore.Write(path, Journey(true), Adventure, Policy, Campaign(true), original.Capture());
            string profilePath = CampaignRuntimeSaveStore.ProfilePath(path), oldSave = File.ReadAllText(path), oldProfile = File.ReadAllText(profilePath);
            var loaded = CampaignRuntimeSaveStore.Load(path, Journey(false), Adventure, Policy, Campaign(false));
            Assert.False(loaded.RecoveredBackup); Assert.Equal(oldSave, File.ReadAllText(path)); Assert.Equal(oldProfile, File.ReadAllText(profilePath));
            Assert.Equal(JsonData.Hash(original.Production.Capture().Progression.Profile), JsonData.Hash(loaded.Session.Production.Capture().Progression.Profile));
            CampaignRuntimeSaveStore.Write(path, Journey(false), Adventure, Policy, Campaign(false), loaded.Session.Capture());
            Assert.Equal(oldSave, File.ReadAllText(path + ".bak")); Assert.Equal(oldProfile, File.ReadAllText(profilePath + ".bak"));
            Assert.Equal(loaded.Session.StateHash, CampaignRuntimeSaveStore.Load(path, Journey(false), Adventure, Policy, Campaign(false)).Session.StateHash);
            File.WriteAllText(path, "{");
            var recovered = CampaignRuntimeSaveStore.Load(path, Journey(false), Adventure, Policy, Campaign(false));
            Assert.True(recovered.RecoveredBackup); Assert.Equal(loaded.Session.StateHash, recovered.Session.StateHash);
            Assert.Equal(oldSave, File.ReadAllText(path + ".bak"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void LocalProfileRequiresItsExactOldIdentityAndOriginalChecksum()
    {
        var original = OldJourney(); var loaded = Upgrade(Save(original)); var profile = original.Production.Capture().Progression.Profile;
        var node = JsonNode.Parse(JsonData.Write(new LocalProfileEnvelope(1, original.Production.Content.Hash, JsonData.Hash(profile), profile)))!;
        Assert.Equal(JsonData.Hash(profile), JsonData.Hash(LocalProfileStore.Read(loaded.Production.Content, node.ToJsonString())));
        node["stateHash"] = new string('0', 64);
        Assert.Throws<InvalidDataException>(() => LocalProfileStore.Read(loaded.Production.Content, node.ToJsonString()));
        node["contentHash"] = new string('0', 64);
        Assert.Throws<SaveCompatibilityException>(() => LocalProfileStore.Read(loaded.Production.Content, node.ToJsonString()));
    }
}
