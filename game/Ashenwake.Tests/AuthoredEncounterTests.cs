using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class AuthoredEncounterTests
{
    private static CombatContent Content => CombatContent.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json")));
    [Fact]
    public void NewDataOnlyPackRunsAndReplaysWithoutAddingAHandler()
    {
        var content = Content with
        {
            Encounters = [new("encounter.test_pack", "Test pack",
            [new("enemy.ash_ghoul", new(-1000, -1500)), new("enemy.cinder_acolyte", new(4300, 2000)), new("enemy.cinder_priest", new(5000, -1500))])]
        };
        content.Validate(); string json = JsonData.Write(content);
        var session = CombatSession.CreateEncounter(json, 53, "encounter.test_pack"); var recorder = new CombatRecorder(session);
        Assert.Equal(3, session.View.Actors.Count(a => a.Faction == CombatFaction.Enemy));
        for (int i = 0; i < 1200 && session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0); i++) recorder.Step(session, CombatProductionSmoke.Commands(session.View));
        Assert.DoesNotContain(session.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        Assert.True(CombatReplayRunner.Run(json, recorder.Capture()).Success);
        Assert.Equal(session.StateHash, CombatSession.Restore(json, session.Capture()).StateHash);
    }
    [Fact]
    public void PacksRejectUnknownEnemiesReservedIdsAndBlockedOrOverlappingSpawns()
    {
        var content = Content;
        void Reject(string id, params CombatSpawn[] spawns) => Assert.Throws<InvalidDataException>(() => (content with { Encounters = [new(id, "Bad", spawns)] }).Validate());
        Reject("encounter.ossuary", new CombatSpawn("enemy.ash_ghoul", new(0, 0)));
        Reject("encounter.test_pack", new CombatSpawn("enemy.unknown", new(0, 0)));
        Reject("encounter.test_pack", new CombatSpawn("enemy.ash_ghoul", content.Room.PlayerSpawn));
        Reject("encounter.test_pack", new CombatSpawn("enemy.ash_ghoul", new(1000000, 0)));
        Reject("encounter.test_pack", new CombatSpawn("enemy.ash_ghoul", new(-1000, -1500)), new CombatSpawn("enemy.ash_ghoul", new(-1000, -1500)));
    }
}
