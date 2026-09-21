using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Experiments;
using Ashenwake.Core.Exploration;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class LocalExplorationMapTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent Policy => ProgressionContent.Parse(Read("progression.json"));
    private static CampaignContent Campaign => CampaignContent.Parse(Read("campaign.json"));
    private static EndgameContent Endgame => EndgameContent.Parse(Read("endgame.json"));
    private static readonly Lazy<string> Composed = new(() => EndgameCombatContent.Parse(
        CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), Endgame).CombatJson);
    private static string CombatJson => Composed.Value;
    private static CampaignRuntimeSession Fresh() => CampaignRuntimeSession.Create(CombatJson, Adventure, Policy, Campaign);
    private static CampaignRuntimeSession Restore(CampaignRuntimeSnapshot state) => CampaignRuntimeSession.Restore(CombatJson, Adventure, Policy, Campaign, state);
    private static EndgameRuntimeSession Restore(EndgameRuntimeSnapshot state) => EndgameRuntimeSession.Restore(CombatJson, Adventure, Policy, Campaign, Endgame, state);
    private static EndgameRuntimeSession Imported()
    {
        var previous = CampaignCombatContent.Parse(Read("fixtures/combat-phase4.json"), Read("fixtures/campaign-combat-phase4.json")).CombatJson;
        return EndgameRuntimeMigration.ImportPhaseFour(Read("fixtures/phase4-campaign-complete.json"), previous, CombatJson, Adventure, Policy, Campaign, Endgame);
    }
    private static void AtGate(EndgameRuntimeSession session, EndgameRuntimeCommand target)
    {
        for (int i = 0; i < 500; i++)
        {
            var command = EndgameRuntimeSmoke.AtGate(session, target);
            var outcome = session.Execute(command); Assert.True(outcome.Success, outcome.Reason);
            if (command.Action != EndgameRuntimeAction.Tick) return;
        }
        Assert.Fail("Gate route did not finish.");
    }
    private static CampaignRuntimeSession RoadAt(Position position)
    {
        var session = Fresh(); Assert.True(session.EnterAct(1).Success);
        var snapshot = session.Capture();
        snapshot.Combat.Actors.Single(a => a.Id == 1).Position = position;
        return Restore(snapshot);
    }

    [Fact]
    public void MappingRemainsAbsentUntilRecordedEnableAndLegacyReplayHashesStillMatch()
    {
        var session = Fresh(); string initial = session.StateHash;
        Assert.Null(session.LocalMap); Assert.Null(session.Capture().ExplorationMap);
        Assert.DoesNotContain("explorationMap", JsonData.Write(session.Capture()));
        Assert.Equal(initial, Restore(session.Capture()).StateHash);
        session.Step(); Assert.True(session.EnterAct(1).Success); session.Step();
        Assert.Null(session.Capture().ExplorationMap);
        Assert.True(CampaignRuntimeReplayRunner.Run(CombatJson, Adventure, Policy, Campaign, session.CaptureReplay()).Success);
        Assert.True(session.EnableExplorationMap().Success);
        Assert.NotNull(session.LocalMap); Assert.NotEmpty(session.LocalMap.SeenCells);
        Assert.True(CampaignRuntimeReplayRunner.Run(CombatJson, Adventure, Policy, Campaign, session.CaptureReplay()).Success);
    }

    [Fact]
    public void WallFacesAreDiscoveredButRoadFogDoesNotRevealThroughTheDivider()
    {
        var session = RoadAt(new(-1400, -1000)); Assert.True(session.EnableExplorationMap().Success);
        var view = session.LocalMap!;
        Assert.True(view.IsExplored(new(-2250, -750)));
        Assert.True(view.IsExplored(new(-750, -750)));
        Assert.False(view.IsExplored(new(1250, -750)));
        Assert.False(view.IsExplored(new(9500, 0)));
        Assert.False(view.IsExplored(new(int.MaxValue, int.MaxValue)));
        Assert.Equal(500, view.CellSize); Assert.Equal(48, view.Columns); Assert.Equal(40, view.Rows);
        Assert.Throws<ArgumentOutOfRangeException>(() => view.CellCenter(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.CellCenter(view.Columns * view.Rows));
        // Callers cannot mutate authoritative geometry or visibility through a view.
        view.Room.Obstacles[0] = new(0, 0, 1, 1);
        Assert.NotEqual(new Bounds(0, 0, 1, 1), view.Room.Obstacles[0]);
        Assert.False(view.SeenCells is int[]);
    }

    [Fact]
    public void ActualMovementExpandsDiscoveryAndHubTravelRetainsTheRoomMemory()
    {
        var session = RoadAt(new(-4500, 0)); session.EnableExplorationMap();
        var initial = session.LocalMap!; var cells = initial.SeenCells.ToArray();
        for (int i = 0; i < 24; i++) Assert.True(session.Step(new CombatCommand(CombatCommandKind.Move, 1, X: 0, Z: 1)).Success);
        Assert.True(session.LocalMap!.SeenCells.Count > cells.Length);
        Assert.All(cells, cell => Assert.Contains(cell, session.LocalMap.SeenCells));
        var traveled = session.LocalMap.SeenCells.ToArray();
        Assert.True(session.ReturnToHub().Success); Assert.Equal("hub", session.LocalMap!.RoomId);
        Assert.True(session.EnterAct(1).Success); Assert.Equal("campaign.road", session.LocalMap!.RoomId);
        Assert.All(traveled, cell => Assert.Contains(cell, session.LocalMap.SeenCells));
        var saved = session.Capture(); var restored = Restore(saved);
        Assert.Equal(session.StateHash, restored.StateHash);
        Assert.Equal(session.LocalMap.SeenCells, restored.LocalMap!.SeenCells);
        Assert.True(CampaignRuntimeReplayRunner.Run(CombatJson, Adventure, Policy, Campaign, session.CaptureReplay()).Success);
    }

    [Fact]
    public void FailedCommandsDoNotRevealOrAlterExplorationAndUnchangedViewIsStable()
    {
        var session = Fresh(); session.EnableExplorationMap();
        var view = session.LocalMap; string hash = session.StateHash;
        Assert.Same(view, session.LocalMap);
        Assert.False(session.EnterAct(5).Success);
        Assert.Equal(hash, session.StateHash);
        Assert.Equal(view!.SeenCells, session.LocalMap!.SeenCells);
        Assert.True(session.EnableExplorationMap().Success);
        Assert.Equal(hash, session.StateHash);
    }

    [Fact]
    public void LargeValidCustomRoomsUseABoundedGridAndMappedArchivesRoundTrip()
    {
        var content = CombatContent.Parse(CombatJson);
        string wideJson = JsonData.Write(content with { Room = content.Room with { HalfWidth = 100000, HalfDepth = 100000 } });
        var session = CampaignRuntimeSession.Create(wideJson, Adventure, Policy, Campaign); session.EnableExplorationMap();
        Assert.InRange(session.LocalMap!.Columns, 1, 96); Assert.InRange(session.LocalMap.Rows, 1, 96);
        Assert.True(session.LocalMap.CellSize > 500); Assert.NotEmpty(session.LocalMap.SeenCells);
        var state = session.Capture(); string json = JsonData.Write(new CampaignRuntimeSave(1, JsonData.Hash(state), state));
        var restored = CampaignRuntimeSaveStore.Read(wideJson, Adventure, Policy, Campaign, json);
        Assert.Equal(session.StateHash, restored.StateHash);
        Assert.Equal(session.LocalMap.SeenCells, restored.LocalMap!.SeenCells);
    }

    [Fact]
    public void AdvancingGeneratedRoomsStoresSeparateDiscoveryAndRejectsFutureRoomMemory()
    {
        var session = Imported(); session.EnableExplorationMap();
        AtGate(session, new(EndgameRuntimeAction.ClaimRecoverySigil));
        AtGate(session, new(EndgameRuntimeAction.StartFracture, session.View.AvailableSigils.Single().Id));
        string firstRoom = session.LocalMap!.RoomId;
        for (int i = 0; i < 3000 && session.RunView!.EncounterIndex == 0; i++)
        {
            var result = session.Execute(EndgameRuntimeSmoke.Next(session)); Assert.True(result.Success, result.Reason);
        }
        Assert.Equal(1, session.RunView!.EncounterIndex);
        Assert.NotEqual(firstRoom, session.LocalMap!.RoomId); Assert.Equal(2, session.Capture().ExplorationMap!.Rooms.Count);
        var state = session.Capture(); Assert.Equal(session.StateHash, Restore(state).StateHash);
        state.ExplorationMap!.Rooms.Add("run." + session.RunView.Id + ".room.3", state.ExplorationMap.Rooms[firstRoom]);
        Assert.Throws<InvalidDataException>(() => Restore(state));
        Assert.True(EndgameRuntimeReplayRunner.Run(CombatJson, Adventure, Policy, Campaign, Endgame, session.CaptureReplay()).Success);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("rules")]
    [InlineData("room-schema")]
    [InlineData("layout")]
    [InlineData("bounds")]
    [InlineData("size")]
    [InlineData("negative")]
    [InlineData("overflow")]
    [InlineData("duplicate")]
    [InlineData("unsorted")]
    [InlineData("identity")]
    [InlineData("capacity")]
    public void RestoreRejectsInvalidAtlasWithoutSilentlyDiscardingDiscovery(string damage)
    {
        var session = Fresh(); session.EnableExplorationMap(); var snapshot = session.Capture();
        var atlas = snapshot.ExplorationMap!; var entry = atlas.Rooms["hub"];
        switch (damage)
        {
            case "schema": atlas = atlas with { SchemaVersion = 2 }; break;
            case "rules": atlas = atlas with { RulesVersion = "future" }; break;
            case "room-schema": atlas.Rooms["hub"] = entry with { SchemaVersion = 2 }; break;
            case "layout": atlas.Rooms["hub"] = entry with { LayoutHash = "other" }; break;
            case "bounds": atlas.Rooms["hub"] = entry with { HalfWidth = 100000 }; break;
            case "size": atlas.Rooms["hub"] = entry with { CellSize = 1 }; break;
            case "negative": atlas.Rooms["hub"] = entry with { SeenCells = [-1] }; break;
            case "overflow": atlas.Rooms["hub"] = entry with { SeenCells = [int.MaxValue] }; break;
            case "duplicate": atlas.Rooms["hub"] = entry with { SeenCells = [0, 0] }; break;
            case "unsorted": atlas.Rooms["hub"] = entry with { SeenCells = [1, 0] }; break;
            case "identity": atlas.Rooms.Add("unvisited.room", entry); break;
            case "capacity": for (int i = 0; i < 97; i++) atlas.Rooms.Add("room." + i, entry); break;
        }
        Assert.Throws<InvalidDataException>(() => Restore(snapshot with { ExplorationMap = atlas }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FutureMapHeadersPreventBackupFallbackAndOverwrite(bool nestedRoom)
    {
        var session = Fresh(); session.EnableExplorationMap(); var state = session.Capture();
        string valid = JsonData.Write(new CampaignRuntimeSave(1, JsonData.Hash(state), state));
        var envelope = JsonNode.Parse(valid)!;
        var header = nestedRoom ? envelope["state"]!["explorationMap"]!["rooms"]!["hub"]! : envelope["state"]!["explorationMap"]!;
        header["schemaVersion"] = 2; header["futureField"] = true;
        string future = envelope.ToJsonString();
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-future-map-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "character.json"); File.WriteAllText(path, future); File.WriteAllText(path + ".bak", valid);
            Assert.Throws<SaveCompatibilityException>(() => CampaignRuntimeSaveStore.Load(path, CombatJson, Adventure, Policy, Campaign));
            Assert.Throws<SaveCompatibilityException>(() => CampaignRuntimeSaveStore.Write(path, CombatJson, Adventure, Policy, Campaign, state));
            Assert.Equal(future, File.ReadAllText(path)); Assert.Equal(valid, File.ReadAllText(path + ".bak"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void GeneratedRoomsKeepDiscoveryOnRetryAndResetItForTheNextRun()
    {
        var session = Imported(); Assert.Null(session.Capture().ExplorationMap);
        session.EnableExplorationMap(); Assert.NotNull(session.Campaign.LocalMap);
        AtGate(session, new(EndgameRuntimeAction.ClaimRecoverySigil));
        AtGate(session, new(EndgameRuntimeAction.StartFracture, session.View.AvailableSigils.Single().Id));
        string firstRoom = session.LocalMap!.RoomId; long firstRun = session.RunView!.Id;
        for (int i = 0; i < 24; i++) Assert.True(session.Step(new CombatCommand(CombatCommandKind.Move, 1, X: 0, Z: 1)).Success);
        var discovered = session.LocalMap!.SeenCells.ToArray(); var dying = session.Capture();
        var player = dying.Combat!.Actors.Single(a => a.Id == 1); player.Health = 1; player.InvulnerableUntil = 0;
        session = Restore(dying);
        for (int i = 0; i < 5000 && !session.AwaitingRetry; i++) Assert.True(session.Step().Success);
        Assert.True(session.AwaitingRetry); Assert.True(session.RetryEncounter().Success);
        Assert.Equal(firstRoom, session.LocalMap!.RoomId); Assert.All(discovered, cell => Assert.Contains(cell, session.LocalMap.SeenCells));
        var before = session.Capture(); Assert.Equal(session.StateHash, Restore(before).StateHash);
        Assert.True(session.Abandon().Success); Assert.Empty(session.Capture().ExplorationMap!.Rooms);
        Assert.Equal("hub", session.LocalMap!.RoomId);
        AtGate(session, new(EndgameRuntimeAction.ClaimRecoverySigil));
        AtGate(session, new(EndgameRuntimeAction.StartFracture, session.View.AvailableSigils.Single().Id));
        Assert.NotEqual(firstRun, session.RunView!.Id); Assert.NotEqual(firstRoom, session.LocalMap!.RoomId);
        Assert.Single(session.Capture().ExplorationMap!.Rooms);
        var forged = session.Capture(); forged.ExplorationMap!.Rooms.Add(firstRoom, before.ExplorationMap!.Rooms[firstRoom]);
        Assert.Throws<InvalidDataException>(() => Restore(forged));
        Assert.True(EndgameRuntimeReplayRunner.Run(CombatJson, Adventure, Policy, Campaign, Endgame, session.CaptureReplay()).Success);
    }

    [Fact]
    public void EndgameAndEchoArchivesInspectNestedAtlasHeadersBeforeTypedDeserialization()
    {
        var session = Imported(); session.EnableExplorationMap(); var state = session.Capture();
        var envelope = JsonNode.Parse(JsonData.Write(new EndgameRuntimeSave(1, JsonData.Hash(state), state)))!;
        envelope["state"]!["campaign"]!["explorationMap"]!["rooms"]!["hub"]!["schemaVersion"] = 2;
        Assert.Throws<SaveCompatibilityException>(() => EndgameRuntimeSaveStore.Read(CombatJson, Adventure, Policy, Campaign, Endgame, envelope.ToJsonString()));
        var experiment = ExperimentContent.Parse(Read("experiments.json"));
        var echoes = ExperimentRuntimeSession.FromEndgame(CombatJson, Adventure, Policy, Campaign, Endgame, experiment, state);
        Assert.True(echoes.ExecuteEndgame(new(EndgameRuntimeAction.EnableExplorationMap)).Success);
        var echoState = echoes.Capture();
        var echoEnvelope = JsonNode.Parse(JsonData.Write(new ExperimentSave(1, JsonData.Hash(echoState), echoState)))!;
        echoEnvelope["state"]!["endgame"]!["explorationMap"]!["schemaVersion"] = 2;
        Assert.Throws<SaveCompatibilityException>(() => ExperimentSaveStore.Read(CombatJson, Adventure, Policy, Campaign, Endgame, experiment, echoEnvelope.ToJsonString()));
        var replay = ExperimentReplayRunner.Run(CombatJson, Adventure, Policy, Campaign, Endgame, experiment, echoes.CaptureReplay());
        Assert.True(replay.Success, replay.ToString());
    }
}
