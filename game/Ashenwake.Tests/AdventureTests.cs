using Ashenwake.Core.Adventure;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;
using Xunit;

namespace Ashenwake.Tests;

public sealed class AdventureTests
{
    private static AdventureSession Session() => AdventureSession.Create(AdventureContent.Default(), 42);
    private static void Complete(AdventureSession session)
    {
        Assert.True(session.Interact("npc.mara").Success);
        Assert.True(session.EnterRoom("room.ossuary").Success);
        Assert.True(session.EncounterCompleted("encounter.ossuary").Success);
        Assert.True(session.EnterRoom("room.cloister").Success);
        Assert.True(session.EncounterCompleted("encounter.cloister").Success);
        Assert.True(session.EnterRoom("room.bell_sanctum").Success);
        Assert.True(session.EncounterCompleted("bell_saint.1").Success);
        Assert.False(session.EncounterCompleted("bell_saint.2").Success);
        Assert.True(session.Interact("ritual.anchor_left").Success);
        Assert.True(session.Interact("ritual.anchor_right").Success);
        Assert.True(session.EncounterCompleted("bell_saint.2").Success);
        Assert.True(session.EncounterCompleted("bell_saint.3").Success);
    }
    [Fact]
    public void MaterialCapCannotBlockBossVictoryOrItsUniqueFragment()
    {
        var content = AdventureContent.Default();
        var state = AdventureSession.Create(content).Capture(); state.Materials = 1_000_000;
        var session = AdventureSession.Restore(content, state);
        Complete(session);
        Assert.Equal(1_000_000, session.View.Materials);
        Assert.Equal(1, session.View.Victories);
        Assert.Contains("fragment.heart_serath", session.Capture().OwnedFragments);
    }
    [Fact]
    public void FullDungeonReturnsRewardRebuildAndReplayWithoutDuplicateRewards()
    {
        var session = Session(); Complete(session);
        Assert.Contains("fragment.heart_serath", session.Capture().OwnedFragments);
        string hash = session.StateHash;
        Assert.False(session.EncounterCompleted("bell_saint.3").Success); Assert.Equal(hash, session.StateHash);
        Assert.True(session.EnterRoom("room.greyhaven").Success);
        Assert.True(session.Interact("npc.mara").Success);
        Assert.True(session.InstallFragment("Heart", "fragment.heart_serath").Success);
        Assert.Contains("concordance.funeral_flame", session.Capture().Concordances);
        Assert.True(session.InstallFragment("Spine", "fragment.nerve_ilyra").Success);
        Assert.True(session.SelectManifestation("manifestation.burning_blood").Success);
        Assert.Contains("manifestation.burning_blood", session.View.ActiveManifestations);
        Assert.True(session.InstallFragment("Heart", null).Success);
        Assert.Empty(session.View.ActiveManifestations); Assert.Single(session.Capture().Manifestations);
        Assert.True(session.Interact("dungeon.replay").Success); Complete(session);
        Assert.Equal(2, session.View.Victories); Assert.Equal(55, session.View.Materials);
    }
    [Fact]
    public void DeathAndLeavingResetBossAtLatestAnchor()
    {
        var session = Session(); session.Interact("npc.mara"); session.EnterRoom("room.ossuary"); session.EncounterCompleted("encounter.ossuary");
        session.EnterRoom("room.cloister"); session.EncounterCompleted("encounter.cloister"); session.EnterRoom("room.bell_sanctum"); session.EncounterCompleted("bell_saint.1"); session.Interact("ritual.anchor_left");
        Assert.True(session.PlayerDied().Success); Assert.Equal("room.cloister", session.View.RoomId);
        Assert.Equal(0, session.View.BellPhase); Assert.Empty(session.Capture().DestroyedAnchors);
        Assert.False(session.EnterRoom("room.bell_sanctum").Success);
        session.EncounterCompleted("encounter.cloister"); session.EnterRoom("room.bell_sanctum");
        Assert.Equal("bell_saint.1", session.View.EncounterId);
        session.EncounterCompleted("bell_saint.1"); session.EnterRoom("room.greyhaven");
        Assert.Equal(0, session.View.BellPhase);
    }
    [Fact]
    public void AshcleaverTracksPerInstancePersistsAndRequiresPermanentGraftConfirmation()
    {
        var content = AdventureContent.Default(); var session = Session(); Complete(session); session.EnterRoom("room.greyhaven");
        for (int i = 0; i < 999; i++) Assert.True(session.RecordBurningKill(i, "ashcleaver.1").Success);
        Assert.False(session.Capture().Godwrought[0].Awakened);
        session = AdventureSaveStore.Deserialize(content, AdventureSaveStore.Serialize(content, session));
        Assert.False(session.RecordBurningKill(998, "ashcleaver.1").Success);
        Assert.True(session.RecordBurningKill(999, "ashcleaver.1").Success);
        Assert.True(session.Capture().Godwrought[0].FlameWaveReady);
        string before = session.StateHash; Assert.False(session.Graft("ashcleaver.1", "Serath", false).Success); Assert.Equal(before, session.StateHash);
        Assert.True(session.Graft("ashcleaver.1", "Serath", true).Success);
        Assert.False(session.Graft("ashcleaver.1", "Orrun", true).Success);
        Assert.Equal("Serath", AdventureSaveStore.Deserialize(content, AdventureSaveStore.Serialize(content, session)).Capture().Godwrought[0].Evolution);
        session.ElapseCombatTicks(150); Assert.False(session.Capture().Godwrought[0].FlameWaveReady);
    }
    [Fact]
    public void InvalidTransactionsAndMutableSnapshotsCannotChangeLiveState()
    {
        var content = AdventureContent.Default(); var source = content.Capture(); var session = AdventureSession.Create(content);
        string hash = session.StateHash; source.Rooms[0].Exits[0] = "room.invalid"; session.Capture().Materials = 900;
        Assert.False(session.EnterRoom("room.bell_sanctum").Success);
        Assert.False(session.InstallFragment("Mind", "fragment.eye_vael").Success);
        Assert.False(session.SelectManifestation("manifestation.burning_blood").Success);
        Assert.False(session.Graft("ashcleaver.1", "Orrun", true).Success);
        Assert.Equal(hash, session.StateHash);
        Assert.True(session.Temper("ashcleaver.1").Success); Assert.Equal(20, session.View.Materials);
    }
    [Fact]
    public void SaveRoundTripCorruptionRecoveryMigrationAndNewerVersionProtection()
    {
        var content = AdventureContent.Default(); var session = Session(); Complete(session);
        Assert.Equal(session.StateHash, AdventureSaveStore.Deserialize(content, AdventureSaveStore.Serialize(content, session)).StateHash);
        // Maintained v1 fixture represents the former hub-only prototype, which had no character inventory.
        var legacy = JsonData.Read<AdventureLegacyState>("{\"seed\":17,\"materials\":12,\"questAccepted\":true}");
        var migrated = AdventureSaveStore.Deserialize(content, JsonData.Write(new AdventureLegacySave(1, content.Hash, JsonData.Hash(legacy), legacy)));
        Assert.Equal(12, migrated.View.Materials); Assert.True(migrated.Capture().QuestAccepted); Assert.Equal(1, migrated.View.Expedition);
        var dir = Path.Combine(Path.GetTempPath(), "ashenwake-adventure-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "character.json");
        try
        {
            AdventureSaveStore.Write(path, content, session); session.EnterRoom("room.greyhaven"); AdventureSaveStore.Write(path, content, session);
            File.WriteAllText(path, "{truncated"); Assert.True(AdventureSaveStore.Load(path, content).RecoveredBackup);
            var newer = JsonData.Read<AdventureSave>(AdventureSaveStore.Serialize(content, session)) with { SchemaVersion = 99 };
            File.WriteAllText(path, JsonData.Write(newer));
            Assert.Throws<SaveCompatibilityException>(() => AdventureSaveStore.Load(path, content));
            Assert.Throws<SaveCompatibilityException>(() => AdventureSaveStore.Write(path, content, session));
            Assert.Equal(JsonData.Write(newer), File.ReadAllText(path));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Fact]
    public void ContentAndStateValidationRejectBrokenReferencesAndForgedRewardState()
    {
        var content = AdventureContent.Default(); var d = content.Capture();
        Assert.Throws<InvalidDataException>(() => AdventureContent.Create(d with { AwakeningKills = 2 }));
        Assert.Throws<InvalidDataException>(() => AdventureContent.Create(d with { RewardFragment = "missing" }));
        Assert.Throws<InvalidDataException>(() => AdventureContent.Create(d with { Rooms = [d.Rooms[0], d.Rooms[0]] }));
        var s = Session().Capture(); s.BellVictories = 1;
        Assert.Throws<InvalidDataException>(() => AdventureSession.Restore(content, s));
        s = Session().Capture(); s.Anatomy["Eyes"] = "fragment.missing";
        Assert.Throws<InvalidDataException>(() => AdventureSession.Restore(content, s));
        s = Session().Capture(); s.CompletedEncounters.Add("bell_saint.3");
        Assert.Throws<InvalidDataException>(() => AdventureSession.Restore(content, s));
    }
}
