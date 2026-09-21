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

public sealed class SpineCatalogMigrationTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent Policy => ProgressionContent.Parse(Read("progression.json"));
    private static CampaignContent Campaign(bool old) => CampaignContent.Parse(Read(old ? "fixtures/campaign-cinder.json" : "campaign.json"));
    private static readonly Lazy<string> PreviousCombat = new(() => Compose(true));
    private static readonly Lazy<string> CurrentCombat = new(() => Compose(false));
    private static string Compose(bool old) => CampaignCombatContent.Parse(Read("combat.json"), Read(old ? "fixtures/campaign-combat-cinder.json" : "campaign-combat.json")).CombatJson;
    private static string Combat(bool old) => old ? PreviousCombat.Value : CurrentCombat.Value;
    private static CampaignRuntimeSession RestoreOld(CampaignRuntimeSnapshot state) => CampaignRuntimeSession.Restore(Combat(true), Adventure, Policy, Campaign(true), state);
    private static string Save(CampaignRuntimeSession session) => JsonData.Write(new CampaignRuntimeSave(1, session.StateHash, session.Capture()));
    private static CampaignRuntimeSession Upgrade(string json) => CampaignRuntimeSaveStore.Read(Combat(false), Adventure, Policy, Campaign(false), json);
    private static readonly Lazy<CampaignRuntimeSnapshot> Causeway = new(() =>
    {
        var session = CampaignRuntimeSession.Create(Combat(true), Adventure, Policy, Campaign(true));
        Assert.True(session.EnableExplorationMap().Success);
        for (int i = 0; i < 32000 && session.ActiveEncounterId != "campaign.bone_causeway"; i++)
        { var result = session.Execute(CampaignRuntimeSmoke.Next(session)); Assert.True(result.Success, result.Reason); }
        Assert.Equal("campaign.bone_causeway", session.ActiveEncounterId);
        return session.Capture();
    });

    [Fact]
    public void PreviousReleaseKeepsEarlierRoomsLootAndFogWhileRelocatingNewlyBlockedSpinePositions()
    {
        var snapshot = RestoreOld(Causeway.Value).Capture();
        var oldRoom = new SpatialWorld(RestoreOld(snapshot).Room);
        var room = CombatContent.Parse(Combat(false)).Campaign!.Encounters.Single(e => e.Id == "campaign.bone_causeway").Room!;
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
        Assert.Empty(after.ExplorationMap!.Rooms["campaign.bone_causeway"].SeenCells);
        Assert.True(loaded.Step().Success);
        Assert.NotEmpty(loaded.LocalMap!.SeenCells);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(false), Adventure, Policy, Campaign(false), loaded.CaptureReplay()).Success);
    }

    [Theory]
    [InlineData("leave")]
    [InlineData("complete")]
    public void LegacyMemoryStartedBeforeContractHallKeepsItsOriginalReturn(string outcome)
    {
        var original = RestoreOld(Causeway.Value);
        for (int i = 0; i < 8000 && !original.EncounterCleared; i++)
        { var result = original.Execute(CampaignRuntimeSmoke.Next(original)); Assert.True(result.Success, result.Reason); }
        Assert.True(original.EncounterCleared); Assert.True(original.BeginExploration("event.divine_memory").Success);
        Assert.Equal("campaign.bone_causeway", original.Capture().ExplorationReturnEncounter);
        Assert.DoesNotContain("campaign.contract_hall", original.Capture().Campaign.CompletedEncounters);
        var loaded = Upgrade(Save(original));
        Assert.Equal(JsonData.Hash(original.Capture().Campaign.Exploration), JsonData.Hash(loaded.Capture().Campaign.Exploration));
        Assert.Equal("exploration.first_oath", loaded.LocalMap!.RoomId);
        for (int i = 0; i < 10000 && loaded.ActiveEncounterId == "exploration.first_oath"; i++)
        {
            var command = outcome == "complete" ? CampaignRuntimeSmoke.Next(loaded) : CampaignRuntimeSmoke.AtInteraction(loaded, "spine.memory.return",
                new(CampaignRuntimeAction.InteractSpine, Id: "spine.memory.return"));
            var result = loaded.Execute(command); Assert.True(result.Success, result.Reason);
        }
        Assert.Equal("campaign.bone_causeway", loaded.ActiveEncounterId);
        Assert.Null(loaded.Capture().Campaign.Exploration);
        Assert.Equal(outcome == "complete", loaded.Capture().Campaign.CompletedExploration.Contains("event.divine_memory"));
        Assert.DoesNotContain("campaign.contract_hall", loaded.Capture().Campaign.CompletedEncounters);
        Assert.Equal(loaded.StateHash, Upgrade(Save(loaded)).StateHash);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(false), Adventure, Policy, Campaign(false), loaded.CaptureReplay()).Success);
    }

    [Fact]
    public void LegacyClearedMemoryWithUncollectedDropsRecordsRewardOnceAndRetainsItsOriginalReturn()
    {
        var original = RestoreOld(Causeway.Value);
        for (int i = 0; i < 8000 && !original.EncounterCleared; i++)
            Assert.True(original.Execute(CampaignRuntimeSmoke.Next(original)).Success);
        Assert.True(original.BeginExploration("event.divine_memory").Success);
        for (int i = 0; i < 8000 && original.Combat.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0); i++)
            Assert.True(original.Step(CampaignCombatSmoke.Commands(original.Combat.View, original.Room)).Success);
        Assert.DoesNotContain(original.Combat.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        Assert.NotEmpty(original.Combat.View.Loot);
        Assert.NotNull(original.Capture().Campaign.Exploration);
        Assert.DoesNotContain("event.divine_memory", original.Capture().Campaign.CompletedExploration);
        var loaded = Upgrade(Save(original));
        var items = original.Combat.View.Loot.Select(l => l.Item).ToArray();
        int materials = loaded.Production.Capture().Progression.Character.Materials;
        Assert.True(loaded.Step().Success);
        Assert.Equal("exploration.first_oath", loaded.ActiveEncounterId);
        Assert.Null(loaded.Capture().Campaign.Exploration);
        Assert.Equal("campaign.bone_causeway", loaded.Capture().ExplorationReturnEncounter);
        Assert.Equal(items, loaded.Combat.View.Loot.Select(l => l.Item));
        Assert.Equal(materials + 15, loaded.Production.Capture().Progression.Character.Materials);
        loaded = Upgrade(Save(loaded));
        Assert.True(loaded.Step().Success);
        Assert.Equal(materials + 15, loaded.Production.Capture().Progression.Character.Materials);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(false), Adventure, Policy, Campaign(false), loaded.CaptureReplay()).Success);
    }

    [Fact]
    public void LegacyGenericClearedRoomAndItsMemoryReturnGainTheSecuredContractLayout()
    {
        var original = RestoreOld(Causeway.Value);
        for (int i = 0; i < 8000 && !original.EncounterCleared; i++)
            Assert.True(original.Execute(CampaignRuntimeSmoke.Next(original)).Success);
        Assert.True(original.AdvanceEncounter().Success);
        Assert.Equal("campaign.contract_hall", original.ActiveEncounterId);
        for (int i = 0; i < 8000 && !original.EncounterCleared; i++)
            Assert.True(original.Execute(CampaignRuntimeSmoke.Next(original)).Success);
        Assert.True(original.ReturnToHub().Success); Assert.True(original.EnterAct(4).Success);
        Assert.Equal("clear", original.ActiveEncounterId);
        Assert.False(original.Capture().Campaign.Choices.ContainsKey("choice.oath"));
        var cleared = Upgrade(Save(original));
        Assert.Equal("campaign.contract_hall", cleared.ActiveEncounterId);
        Assert.Equal("campaign.contract_hall", cleared.Combat.Capture().RoomEncounterId);
        Assert.Contains(cleared.Interactions, i => i.ActionId == "spine.memory.enter");
        Assert.DoesNotContain(cleared.Interactions, i => i.ActionId == "spine.forward.warden");
        Assert.True(original.BeginExploration("event.divine_memory").Success);
        Assert.Equal("clear", original.Capture().ExplorationReturnEncounter);
        var loaded = Upgrade(Save(original));
        Assert.Equal("campaign.contract_hall", loaded.Capture().ExplorationReturnEncounter);
        for (int i = 0; i < 2000 && loaded.ActiveEncounterId == "exploration.first_oath"; i++)
            Assert.True(loaded.Execute(CampaignRuntimeSmoke.AtInteraction(loaded, "spine.memory.return",
                new(CampaignRuntimeAction.InteractSpine, Id: "spine.memory.return"))).Success);
        Assert.Equal("campaign.contract_hall", loaded.ActiveEncounterId);
        Assert.DoesNotContain("event.divine_memory", loaded.Capture().Campaign.CompletedExploration);
        Assert.Equal(loaded.StateHash, Upgrade(Save(loaded)).StateHash);
    }

    [Fact]
    public void SaveAndSharedProfileStayUntouchedUntilExplicitWriteAndKeepPublishedBackups()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-spine-migration-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "character.json");
        try
        {
            var original = RestoreOld(Causeway.Value);
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
        var node = JsonNode.Parse(Save(RestoreOld(Causeway.Value)))!;
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
