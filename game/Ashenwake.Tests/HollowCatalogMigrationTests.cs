using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Experiments;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class HollowCatalogMigrationTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent Policy => ProgressionContent.Parse(Read("progression.json"));
    private static CampaignContent Campaign(bool old) => CampaignContent.Parse(Read(old ? "fixtures/campaign-spine.json" : "campaign.json"));
    private static readonly Lazy<string> PreviousCombat = new(() => Compose(true));
    private static readonly Lazy<string> CurrentCombat = new(() => Compose(false));
    private static string Compose(bool old) => CampaignCombatContent.Parse(Read("combat.json"), Read(old ? "fixtures/campaign-combat-spine.json" : "campaign-combat.json")).CombatJson;
    private static string Combat(bool old) => old ? PreviousCombat.Value : CurrentCombat.Value;
    private static CampaignRuntimeSession RestoreOld(CampaignRuntimeSnapshot state) => CampaignRuntimeSession.Restore(Combat(true), Adventure, Policy, Campaign(true), state);
    private static string Save(CampaignRuntimeSession session) => JsonData.Write(new CampaignRuntimeSave(1, session.StateHash, session.Capture()));
    private static CampaignRuntimeSession Upgrade(string json) => CampaignRuntimeSaveStore.Read(Combat(false), Adventure, Policy, Campaign(false), json);
    private static readonly Lazy<CampaignRuntimeSnapshot> RepeatingRooms = new(() =>
    {
        var session = CampaignRuntimeSession.Create(Combat(true), Adventure, Policy, Campaign(true));
        Assert.True(session.EnableExplorationMap().Success);
        for (int i = 0; i < 32000 && session.ActiveEncounterId != "campaign.repeating_rooms"; i++)
        { var result = session.Execute(CampaignRuntimeSmoke.Next(session)); Assert.True(result.Success, result.Reason); }
        Assert.Equal("campaign.repeating_rooms", session.ActiveEncounterId);
        return session.Capture();
    });

    [Fact]
    public void PreviousReleaseKeepsEarlierRoomsLootAndFogWhileRelocatingNewlyBlockedHollowPositions()
    {
        var snapshot = RestoreOld(RepeatingRooms.Value).Capture();
        var oldRoom = new SpatialWorld(RestoreOld(snapshot).Room);
        var room = CombatContent.Parse(Combat(false)).Campaign!.Encounters.Single(e => e.Id == "campaign.repeating_rooms").Room!;
        var space = new SpatialWorld(room);
        var blocked = (from x in Enumerable.Range(-22, 45)
                       from z in Enumerable.Range(-18, 37)
                       let p = new Position(x * 500, z * 500)
                       where oldRoom.CanOccupy(p, CombatSession.ActorRadius) && !space.CanOccupy(p, 0)
                       select p).First();
        var enemy = snapshot.Combat.Actors.First(a => a.Faction == CombatFaction.Enemy);
        enemy.Position = blocked;
        enemy.Pending = new("enemy.strike", 1, blocked, snapshot.Combat.Tick + 10, snapshot.Combat.NextActionId++);
        long id = snapshot.Combat.NextObjectId++;
        snapshot.Combat.Loot.Add(new(id, blocked, snapshot.Combat.Inventory[0] with { Id = id }));
        var original = RestoreOld(snapshot); string json = Save(original);
        var loaded = Upgrade(json); var after = loaded.Capture();
        Assert.Equal(loaded.StateHash, Upgrade(json).StateHash);
        Assert.Equal(snapshot.Combat.Rng, after.Combat.Rng); Assert.Equal(snapshot.Combat.Tick, after.Combat.Tick);
        Assert.Equal(snapshot.Combat.NextObjectId, after.Combat.NextObjectId);
        Assert.Equal(snapshot.Combat.Loot[0].Item, after.Combat.Loot[0].Item);
        Assert.True(space.CanOccupy(after.Combat.Loot[0].Position, 0));
        Assert.All(after.Combat.Actors, actor => Assert.True(space.CanOccupy(actor.Position, CombatSession.ActorRadius)));
        Assert.True(space.CanOccupy(after.Combat.Actors.Single(a => a.Id == enemy.Id).Pending!.Target, 0));
        Assert.Equal(JsonData.Hash(snapshot.Production.Progression.Character.Items), JsonData.Hash(after.Production.Progression.Character.Items));
        Assert.Equal(snapshot.Production.Progression.Character.Materials, after.Production.Progression.Character.Materials);
        Assert.NotEmpty(snapshot.ClearedRooms!);
        foreach (var (key, cached) in snapshot.ClearedRooms!)
        {
            Assert.Equal(JsonData.Hash(cached.Loot), JsonData.Hash(after.ClearedRooms![key].Loot));
            Assert.Equal(JsonData.Hash(snapshot.ExplorationMap!.Rooms[key]), JsonData.Hash(after.ExplorationMap!.Rooms[key]));
        }
        Assert.Empty(after.ExplorationMap!.Rooms["campaign.repeating_rooms"].SeenCells);
        Assert.True(loaded.Step().Success);
        Assert.NotEmpty(loaded.LocalMap!.SeenCells);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(false), Adventure, Policy, Campaign(false), loaded.CaptureReplay()).Success);
    }

    private static void Fight(CampaignRuntimeSession session, string encounter)
    {
        for (int i = 0; i < 8000 && session.ActiveEncounterId == encounter && !session.EncounterCleared; i++)
        { var result = session.Step(CampaignCombatSmoke.Commands(session.Combat.View, session.Room)); Assert.True(result.Success, result.Reason); }
        Assert.Equal(encounter, session.ActiveEncounterId); Assert.True(session.EncounterCleared);
    }

    private static readonly Lazy<CampaignRuntimeSnapshot> PendingFuture = new(() =>
    {
        var session = RestoreOld(RepeatingRooms.Value); Fight(session, "campaign.repeating_rooms");
        Assert.True(session.AdvanceEncounter().Success); Fight(session, "campaign.identity_memory");
        Assert.False(session.Capture().Campaign.Choices.ContainsKey("choice.future"));
        return session.Capture();
    });

    [Fact]
    public void PublishedGenericRoomBeforeFinalChoiceBecomesSecuredIdentityMemory()
    {
        var original = RestoreOld(PendingFuture.Value);
        Assert.True(original.ReturnToHub().Success); Assert.True(original.EnterAct(5).Success);
        Assert.Equal("clear", original.ActiveEncounterId);
        var loaded = Upgrade(Save(original));
        Assert.Equal("campaign.identity_memory", loaded.ActiveEncounterId);
        Assert.Equal("campaign.identity_memory", loaded.Combat.Capture().RoomEncounterId);
        Assert.False(loaded.Capture().Campaign.Choices.ContainsKey("choice.future"));
        Assert.Null(loaded.View.Ending);
        Assert.Contains(loaded.Interactions, i => i.ActionId == "hollow.back.rooms");
        Assert.DoesNotContain(loaded.Interactions, i => i.ActionId == "hollow.forward.breach");
        Assert.False(loaded.AdvanceEncounter().Success);
        Assert.Equal(original.Capture().Campaign.EarnedExperience, loaded.Capture().Campaign.EarnedExperience);
        Assert.Equal(original.Capture().Campaign.EarnedMaterials, loaded.Capture().Campaign.EarnedMaterials);
        Assert.Equal(loaded.StateHash, Upgrade(Save(loaded)).StateHash);
    }

    [Theory]
    [InlineData("share")]
    [InlineData("guard")]
    public void PublishedFinaleVictoryKeepsEndingDropsAndRevisitWithoutDuplicatingRewards(string outcome)
    {
        var original = RestoreOld(PendingFuture.Value);
        Assert.True(original.Choose("choice.future", outcome).Success);
        Assert.True(original.AdvanceEncounter().Success); Fight(original, "campaign.breach_heart");
        Assert.NotEmpty(original.Combat.View.Loot); Assert.True(original.View.Ending!.FracturesUnlocked);
        var loaded = Upgrade(Save(original)); var before = loaded.Capture();
        Assert.Equal("campaign.breach_heart", loaded.ActiveEncounterId);
        Assert.True(loaded.EncounterCleared);
        Assert.Equal(JsonData.Hash(original.View.Ending!), JsonData.Hash(loaded.View.Ending!));
        Assert.Equal(original.Combat.View.Loot.Select(l => l.Item), loaded.Combat.View.Loot.Select(l => l.Item));
        Assert.True(loaded.ReturnToHub().Success); Assert.True(loaded.EnterAct(5).Success);
        Assert.Equal("campaign.repeating_rooms", loaded.ActiveEncounterId);
        Assert.Equal(before.Combat.Loot.Select(l => l.Item), loaded.Capture().ClearedRooms!["campaign.breach_heart"].Loot.Select(l => l.Item));
        Assert.Equal(before.Production.Progression.Character.Materials, loaded.Capture().Production.Progression.Character.Materials);
        Assert.Equal(before.Campaign.EarnedExperience, loaded.Capture().Campaign.EarnedExperience);
        Assert.Equal(JsonData.Hash(before.Campaign.Choices), JsonData.Hash(loaded.Capture().Campaign.Choices));
        Assert.True(loaded.View.Ending!.FracturesUnlocked);
        Assert.Equal(loaded.StateHash, Upgrade(Save(loaded)).StateHash);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(false), Adventure, Policy, Campaign(false), loaded.CaptureReplay()).Success);
        // A prior completed-act revisit had no retained room identity at all.
        Assert.True(original.ReturnToHub().Success); Assert.True(original.EnterAct(5).Success);
        Assert.Equal("clear", original.ActiveEncounterId);
        var revisit = Upgrade(Save(original));
        Assert.Equal("campaign.repeating_rooms", revisit.ActiveEncounterId);
        Assert.True(revisit.EncounterCleared); Assert.True(revisit.View.Ending!.FracturesUnlocked);
        Assert.Contains(revisit.Interactions, i => i.ActionId == "hollow.vault.enter");
        Assert.Equal(original.Capture().Campaign.EarnedMaterials, revisit.Capture().Campaign.EarnedMaterials);
    }

    [Fact]
    public void PublishedLiveBreachPreservesThreeSealIdentitiesAndOriginalReplay()
    {
        var original = RestoreOld(PendingFuture.Value);
        Assert.True(original.Choose("choice.future", "share").Success); Assert.True(original.AdvanceEncounter().Success);
        for (int tick = 0; tick < 15; tick++) Assert.True(original.Step().Success);
        var before = original.Combat.Capture();
        var seals = before.Actors.Where(a => a.DefinitionId == "enemy.seal_channel").ToArray();
        Assert.Equal(3, seals.Length);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(true), Adventure, Policy, Campaign(true), original.CaptureReplay()).Success);
        var loaded = Upgrade(Save(original)); var after = loaded.Combat.Capture();
        Assert.Equal(seals.Select(a => a.Id), after.Actors.Where(a => a.DefinitionId == "enemy.seal_channel").Select(a => a.Id));
        Assert.Equal(before.Rng, after.Rng); Assert.Equal(before.Tick, after.Tick);
        Assert.Equal(JsonData.Hash(before.Campaign!), JsonData.Hash(after.Campaign!));
        Assert.Equal(before.NextObjectId, after.NextObjectId); Assert.Equal(before.NextActionId, after.NextActionId);
        Assert.Null(loaded.View.Ending);
        Assert.All(after.Actors, actor => Assert.True(new SpatialWorld(loaded.Room).CanOccupy(actor.Position, CombatSession.ActorRadius)));
        Assert.True(loaded.Step().Success);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(false), Adventure, Policy, Campaign(false), loaded.CaptureReplay()).Success);
    }

    [Fact]
    public void SaveAndSharedProfileStayUntouchedUntilExplicitWriteAndKeepPublishedBackups()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-hollow-migration-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "character.json");
        try
        {
            var original = RestoreOld(RepeatingRooms.Value);
            CampaignRuntimeSaveStore.Write(path, Combat(true), Adventure, Policy, Campaign(true), original.Capture());
            string profilePath = CampaignRuntimeSaveStore.ProfilePath(path), oldSave = File.ReadAllText(path), oldProfile = File.ReadAllText(profilePath);
            var loaded = CampaignRuntimeSaveStore.Load(path, Combat(false), Adventure, Policy, Campaign(false));
            Assert.False(loaded.RecoveredBackup); Assert.Equal(oldSave, File.ReadAllText(path)); Assert.Equal(oldProfile, File.ReadAllText(profilePath));
            Assert.Equal(JsonData.Hash(original.Production.Capture().Progression.Profile), JsonData.Hash(loaded.Session.Production.Capture().Progression.Profile));
            CampaignRuntimeSaveStore.Write(path, Combat(false), Adventure, Policy, Campaign(false), loaded.Session.Capture());
            Assert.Equal(oldSave, File.ReadAllText(path + ".bak")); Assert.Equal(oldProfile, File.ReadAllText(profilePath + ".bak"));
            Assert.Equal(loaded.Session.StateHash, CampaignRuntimeSaveStore.Load(path, Combat(false), Adventure, Policy, Campaign(false)).Session.StateHash);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void PublishedEndgameAndEchoesArchivesTraverseTheNewPredecessor()
    {
        var endgame = EndgameContent.Parse(Read("endgame.json")); var experiment = ExperimentContent.Parse(Read("experiments.json"));
        string oldCombat = EndgameCombatContent.Parse(Combat(true), Read("endgame-combat.json"), endgame).CombatJson;
        string newCombat = EndgameCombatContent.Parse(Combat(false), Read("endgame-combat.json"), endgame).CombatJson;
        var original = EndgameRuntimeMigration.ImportPhaseFour(Read("fixtures/phase4-campaign-complete.json"), Combat(true), oldCombat, Adventure, Policy, Campaign(true), endgame);
        var loaded = EndgameRuntimeSaveStore.Read(newCombat, Adventure, Policy, Campaign(false), endgame,
            JsonData.Write(new EndgameRuntimeSave(1, original.StateHash, original.Capture())));
        Assert.Equal(JsonData.Hash(original.Production.Capture().Progression.Character.Items), JsonData.Hash(loaded.Production.Capture().Progression.Character.Items));
        Assert.Equal(original.View.HighestClearedTier, loaded.View.HighestClearedTier);
        Assert.True(loaded.Step().Success);
        Assert.True(EndgameRuntimeReplayRunner.Run(newCombat, Adventure, Policy, Campaign(false), endgame, loaded.CaptureReplay()).Success);
        var echoes = ExperimentRuntimeSession.FromEndgame(oldCombat, Adventure, Policy, Campaign(true), endgame, experiment, original.Capture());
        var upgraded = ExperimentSaveStore.Read(newCombat, Adventure, Policy, Campaign(false), endgame, experiment,
            JsonData.Write(new ExperimentSave(1, echoes.StateHash, echoes.Capture())));
        Assert.Equal(Campaign(false).Hash, upgraded.Capture().Endgame.Campaign.Campaign.ContentHash);
        Assert.Equal(JsonData.Hash(echoes.Production.Capture().Progression.Character.Items), JsonData.Hash(upgraded.Production.Capture().Progression.Character.Items));
        Assert.True(upgraded.Step().Success);
        Assert.True(ExperimentReplayRunner.Run(newCombat, Adventure, Policy, Campaign(false), endgame, experiment, upgraded.CaptureReplay()).Success);
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("old-position")]
    [InlineData("future-version")]
    [InlineData("unknown-catalog")]
    public void PreviousArchiveMustAuthenticateBeforeAnyMigration(string tampering)
    {
        var node = JsonNode.Parse(Save(RestoreOld(RepeatingRooms.Value)))!;
        switch (tampering)
        {
            case "checksum": node["stateHash"] = new string('0', 64); break;
            case "old-position":
                node["state"]!["combat"]!["actors"]![0]!["position"]!["x"] = 999999;
                node["stateHash"] = JsonData.Hash(JsonData.Read<CampaignRuntimeSnapshot>(node["state"]!.ToJsonString())); break;
            case "future-version": node["state"]!["combat"]!["schemaVersion"] = 2; break;
            case "unknown-catalog": node["state"]!["campaign"]!["contentHash"] = new string('0', 64); break;
        }
        if (tampering is "future-version" or "unknown-catalog") Assert.Throws<SaveCompatibilityException>(() => Upgrade(node.ToJsonString()));
        else Assert.Throws<InvalidDataException>(() => Upgrade(node.ToJsonString()));
    }
}
