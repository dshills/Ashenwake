using Ashenwake.Core.Combat;
using Ashenwake.Core.Serialization;
using Xunit;

namespace Ashenwake.Tests;

public sealed class SmokeNavigationTests
{
    [Theory]
    [InlineData(43UL)]
    [InlineData(44UL)]
    public void VeilwalkerPolicyCanReachAnEnemyFrightenedBehindAPillar(ulong seed)
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json"));
        var content = CombatContent.Parse(json);
        var hub = CombatSession.CreateEncounter(json, seed, "hub");
        hub.ApplyProgressionBuild(new("Veilwalker", Level: 10, Offense: 3, Defense: 3));
        var session = CombatSession.CreateEncounter(json, seed, "bell_saint.2", hub.Capture());
        var recorder = new CombatRecorder(session);
        for (int tick = 0; tick < 3600 && session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0); tick++)
            recorder.Step(session, CombatProductionSmoke.Commands(session.View, content.Room));
        Assert.True(session.View.Actors.Single(a => a.Id == 1).Health > 0);
        Assert.DoesNotContain(session.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        Assert.True(CombatReplayRunner.Run(json, recorder.Capture()).Success);
    }
}
