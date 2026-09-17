using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Serialization;
using Xunit;

namespace Ashenwake.Tests;

public sealed class ExpeditionTests
{
    private static string CombatJson => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json"));
    private static AdventureContent Content => AdventureContent.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "adventure.json")));

    [Fact]
    public void ActualCombatCompletesAllBossPhasesAndReplaysTheRewardReturn()
    {
        var session = ExpeditionSession.Create(CombatJson, Content);
        var events = new List<string>();
        for (int i = 0; i < ExpeditionSmoke.MaximumCommands && !ExpeditionSmoke.Complete(session); i++)
        {
            var result = session.Execute(ExpeditionSmoke.Next(session));
            Assert.True(result.Success, result.Reason); events.AddRange(result.WorldEvents);
        }
        Assert.True(ExpeditionSmoke.Complete(session));
        Assert.Equal(0, session.Capture().Adventure.Deaths);
        Assert.Equal(1, session.View.Victories);
        Assert.Single(events, e => e == "FragmentAwarded:fragment.heart_serath");
        Assert.Equal(2, events.Count(e => e.StartsWith("RitualAnchorDestroyed:", StringComparison.Ordinal)));
        foreach (int phase in new[] { 1, 2, 3 }) Assert.Contains("EncounterCompleted:bell_saint." + phase, events);
        Assert.True(ExpeditionReplayRunner.Run(CombatJson, Content, session.CaptureReplay()).Success);
        Assert.Equal(session.StateHash, ExpeditionSession.Restore(CombatJson, Content, session.Capture()).StateHash);
    }

    [Fact]
    public void ServiceRangeAndFragmentOwnershipAreEnforcedByCore()
    {
        var session = ExpeditionSession.Create(CombatJson, Content);
        Assert.False(session.Travel("room.ossuary").Success);
        Assert.False(session.InstallFragment("Heart", "fragment.heart_serath").Success);
        session.Step(new CombatCommand(CombatCommandKind.EquipFragment, ContentId: "fragment.heart_serath"));
        Assert.DoesNotContain("fragment.heart_serath", session.Combat.Capture().Fragments.Values);
        Assert.False(session.Temper("ashcleaver.1").Success); // Torren is across the hub.
        Assert.True(session.Interact("npc.mara").Success);
        Assert.True(session.Travel("room.ossuary").Success);
        Assert.False(session.InstallFragment("Arms", "fragment.orrun_bone").Success);
        Assert.False(session.Travel("room.cloister").Success);
    }

    [Fact]
    public void MidEncounterSaveRestoresBothAuthoritiesAndRecoversBackup()
    {
        var session = ExpeditionSession.Create(CombatJson, Content);
        for (int i = 0; i < 100; i++) session.Execute(ExpeditionSmoke.Next(session));
        var restored = ExpeditionSession.Restore(CombatJson, Content, session.Capture());
        for (int i = 0; i < 60; i++)
        {
            var command = ExpeditionSmoke.Next(session);
            Assert.Equal(JsonData.Hash(session.Execute(command)), JsonData.Hash(restored.Execute(command)));
            Assert.Equal(session.StateHash, restored.StateHash);
        }
        string folder = Path.Combine(Path.GetTempPath(), "ashenwake-expedition-" + Guid.NewGuid().ToString("N")), path = Path.Combine(folder, "save.json");
        try
        {
            var before = session.StateHash;
            ExpeditionSaveStore.Write(path, CombatJson, Content, session.Capture());
            session.Execute(ExpeditionSmoke.Next(session));
            ExpeditionSaveStore.Write(path, CombatJson, Content, session.Capture());
            File.WriteAllText(path, "{truncated");
            var recovery = ExpeditionSaveStore.Load(path, CombatJson, Content);
            Assert.True(recovery.RecoveredBackup); Assert.Equal(before, recovery.Session.StateHash);
            File.WriteAllText(path, "{\"schemaVersion\":1,\"stateHash\":\"invalid\",\"state\":null}");
            Assert.True(ExpeditionSaveStore.Load(path, CombatJson, Content).RecoveredBackup);
            var newer = new ExpeditionSave(9, session.StateHash, session.Capture());
            File.WriteAllText(path, JsonData.Write(newer));
            Assert.Throws<SaveCompatibilityException>(() => ExpeditionSaveStore.Write(path, CombatJson, Content, session.Capture()));
            Assert.Throws<SaveCompatibilityException>(() => ExpeditionSaveStore.Load(path, CombatJson, Content));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void DeathRestartsAnchorCombatWithoutGrantingBossRewards()
    {
        var session = ExpeditionSession.Create(CombatJson, Content);
        for (int i = 0; i < 3000 && session.View.RoomId != "room.cloister"; i++) session.Execute(ExpeditionSmoke.Next(session));
        Assert.Equal("room.cloister", session.View.RoomId);
        var snapshot = session.Capture();
        var items = snapshot.Combat.Inventory.Select(i => i.Id).ToArray();
        var player = snapshot.Combat.Actors.Single(a => a.Id == 1);
        player.Health = 0; player.DeathProcessed = true; player.Pending = null;
        session = ExpeditionSession.Restore(CombatJson, Content, snapshot);
        session.Step();
        Assert.Equal("room.cloister", session.View.RoomId);
        Assert.Equal(1, session.Capture().Adventure.Deaths);
        Assert.Equal(0, session.View.Victories);
        Assert.DoesNotContain("fragment.heart_serath", session.Capture().Adventure.OwnedFragments);
        Assert.Equal(items, session.Combat.View.Inventory.Select(i => i.Id));
        Assert.Equal(session.Combat.View.Actors[0].MaxHealth, session.Combat.View.Actors[0].Health);
        Assert.True(ExpeditionReplayRunner.Run(CombatJson, Content, session.CaptureReplay()).Success);
    }
}
