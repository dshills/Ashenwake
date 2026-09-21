using System.Text.Json.Nodes;
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

public sealed class VerdantCatalogMigrationTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent Policy => ProgressionContent.Parse(Read("progression.json"));
    private static CampaignContent Campaign(bool old) => CampaignContent.Parse(Read(old ? "fixtures/campaign-grey-march.json" : "campaign.json"));
    private static readonly Lazy<string> PreviousCombat = new(() => Compose(true));
    private static readonly Lazy<string> CurrentCombat = new(() => Compose(false));
    private static string Compose(bool old) => CampaignCombatContent.Parse(Read("combat.json"), Read(old ? "fixtures/campaign-combat-grey-march.json" : "campaign-combat.json")).CombatJson;
    private static string Combat(bool old) => old ? PreviousCombat.Value : CurrentCombat.Value;
    private static CampaignRuntimeSession RestoreOld(CampaignRuntimeSnapshot state) => CampaignRuntimeSession.Restore(Combat(true), Adventure, Policy, Campaign(true), state);
    private static string Save(CampaignRuntimeSession session) => JsonData.Write(new CampaignRuntimeSave(1, session.StateHash, session.Capture()));
    private static CampaignRuntimeSession Upgrade(string json) => CampaignRuntimeSaveStore.Read(Combat(false), Adventure, Policy, Campaign(false), json);
    private static readonly Lazy<CampaignRuntimeSnapshot> Ruins = new(() =>
    {
        var session = CampaignRuntimeSession.Create(Combat(true), Adventure, Policy, Campaign(true));
        Assert.True(session.EnableExplorationMap().Success);
        for (int i = 0; i < 16000 && session.ActiveEncounterId != "campaign.living_ruins"; i++)
        { var result = session.Execute(CampaignRuntimeSmoke.Next(session)); Assert.True(result.Success, result.Reason); }
        Assert.Equal("campaign.living_ruins", session.ActiveEncounterId);
        return session.Capture();
    });

    [Fact]
    public void PreviousReleaseKeepsOpeningLootAndFogWhileRelocatingOnlyNewlyBlockedVerdantPositions()
    {
        var snapshot = RestoreOld(Ruins.Value).Capture();
        var oldRoom = new SpatialWorld(RestoreOld(snapshot).Room);
        var room = CombatContent.Parse(Combat(false)).Campaign!.Encounters.Single(e => e.Id == "campaign.living_ruins").Room!;
        var space = new SpatialWorld(room);
        Position blocked = new(0, -6000);
        Assert.True(oldRoom.CanOccupy(blocked, CombatSession.ActorRadius)); Assert.False(space.CanOccupy(blocked, 0));
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
        Assert.Empty(after.ExplorationMap!.Rooms["campaign.living_ruins"].SeenCells);
        Assert.True(loaded.Step().Success);
        Assert.NotEmpty(loaded.LocalMap!.SeenCells);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(false), Adventure, Policy, Campaign(false), loaded.CaptureReplay()).Success);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyHuntTrackingBeforeVillageCompletionUsesNewGroveAndKeepsItsOriginalReturn(bool finish)
    {
        var original = RestoreOld(Ruins.Value);
        for (int i = 0; i < 8000 && original.Capture().Campaign.Exploration?.Id != "event.wake_hunt"; i++)
        { var result = original.Execute(CampaignRuntimeSmoke.Next(original)); Assert.True(result.Success, result.Reason); }
        Assert.Equal("event.wake_hunt", original.Capture().Campaign.Exploration?.Id);
        Assert.Equal("clear", original.ActiveEncounterId);
        Assert.Equal("campaign.living_ruins", original.Capture().ExplorationReturnEncounter);
        Assert.DoesNotContain("campaign.plague_village", original.Capture().Campaign.CompletedEncounters);
        var loaded = Upgrade(Save(original));
        Assert.Equal("exploration.antler_hunt", loaded.Combat.Capture().RoomEncounterId);
        Assert.Equal("exploration.antler_hunt", loaded.LocalMap!.RoomId);
        Assert.Equal(JsonData.Hash(original.Capture().Campaign.Exploration), JsonData.Hash(loaded.Capture().Campaign.Exploration));
        for (int i = 0; i < 8000 && loaded.ActiveEncounterId is "clear" or "exploration.antler_hunt"; i++)
        {
            var command = finish ? CampaignRuntimeSmoke.Next(loaded) : CampaignRuntimeSmoke.AtInteraction(loaded, "verdant.hunt.return",
                new(CampaignRuntimeAction.InteractVerdant, Id: "verdant.hunt.return"));
            var result = loaded.Execute(command); Assert.True(result.Success, result.Reason);
        }
        Assert.Equal("campaign.living_ruins", loaded.ActiveEncounterId);
        Assert.Null(loaded.Capture().Campaign.Exploration);
        Assert.Equal(finish, loaded.Capture().Campaign.CompletedExploration.Contains("event.wake_hunt"));
        Assert.DoesNotContain("campaign.plague_village", loaded.Capture().Campaign.CompletedEncounters);
        Assert.Equal(loaded.StateHash, Upgrade(Save(loaded)).StateHash);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(false), Adventure, Policy, Campaign(false), loaded.CaptureReplay()).Success);
    }

    [Fact]
    public void SaveAndSharedProfileStayUntouchedUntilExplicitWriteAndKeepPublishedBackups()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-verdant-migration-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "character.json");
        try
        {
            var original = RestoreOld(Ruins.Value);
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

    [Theory]
    [InlineData("checksum")]
    [InlineData("old-position")]
    [InlineData("future-version")]
    [InlineData("unknown-catalog")]
    public void PreviousArchiveMustAuthenticateBeforeAnyMigration(string tampering)
    {
        var node = JsonNode.Parse(Save(RestoreOld(Ruins.Value)))!;
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
