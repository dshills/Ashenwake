using Ashenwake.Core.Combat;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class SmokeNavigationTests
{
    [Fact]
    public void GravecallerCanNavigatePastALivingBossToTheRemainingRitualAnchor()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json"));
        var hub = CombatSession.CreateEncounter(json, 42, "hub");
        hub.ApplyProgressionBuild(new("Gravecaller", Level: 10));
        var session = CombatSession.CreateEncounter(json, 42, "bell_saint.2", hub.Capture());
        var snapshot = session.Capture();
        snapshot.Actors.Single(a => a.Id == 1).Position = new(3800, 0);
        snapshot.Actors.Single(a => a.Role == "BellSaint").Position = new(3200, 0);
        var rightAnchor = snapshot.Actors.Single(a => a.Role == "Anchor" && a.Position.X > 0);
        rightAnchor.Health = 0; rightAnchor.DeathProcessed = true;
        int leftAnchorId = snapshot.Actors.Single(a => a.Role == "Anchor" && a.Position.X < 0).Id;
        foreach (var actor in snapshot.Actors.Where(a => a.Faction == CombatFaction.Enemy)) actor.RecoveryUntil = snapshot.Tick + 3000;
        session = CombatSession.Restore(json, snapshot);
        var recorder = new CombatRecorder(session);
        for (int tick = 0; tick < 300 && session.View.Actors.Single(a => a.Id == leftAnchorId).Health > 0; tick++)
            recorder.Step(session, CampaignCombatSmoke.Commands(session.View, session.Room));
        Assert.Equal(0, session.View.Actors.Single(a => a.Id == leftAnchorId).Health);
        Assert.True(Position.DistanceSquared(session.View.Actors.Single(a => a.Id == 1).Position, new(3800, 0)) > 3000L * 3000);
        Assert.True(CombatReplayRunner.Run(json, recorder.Capture()).Success);
    }
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
