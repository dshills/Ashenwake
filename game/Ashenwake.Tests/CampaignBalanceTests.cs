using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CampaignBalanceTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static string Combat => CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson;
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent Progression => ProgressionContent.Parse(Read("progression.json"));
    private static CampaignContent Content => CampaignContent.Parse(Read("campaign.json"));
    private static CampaignRuntimeSession Fresh() => CampaignRuntimeSession.Create(Combat, Adventure, Progression, Content);

    [Fact]
    public void MeasurementDoesNotChangePublicCommandsOrLoseSimulatedTime()
    {
        var session = Fresh(); var policy = new CampaignBalancePolicy(); var measured = new CampaignBalanceMeasurement(session);
        for (int i = 0; i < 160; i++)
        {
            var before = CampaignBalanceObservation.Capture(session); string hash = session.StateHash;
            var command = policy.Next(session);
            Assert.Equal(JsonData.Hash(CampaignRuntimeSmoke.Next(session)), JsonData.Hash(command));
            Assert.Equal(hash, session.StateHash);
            var result = session.Execute(command); Assert.True(result.Success, result.Reason);
            hash = session.StateHash; measured.Observe(before, command, result, session); Assert.Equal(hash, session.StateHash);
        }
        Assert.Equal(160, measured.Commands);
        Assert.Equal(session.Tick, measured.Rooms.Sum(r => r.CombatTicks + r.TravelTicks + r.LootTicks));
        Assert.Equal(measured.Commands, session.Tick + measured.Rooms.Sum(r => r.MenuCommands));
        Assert.Contains(measured.Rooms, r => r.CombatTicks > 0);
        Assert.Contains(measured.Rooms, r => r.TravelTicks > 0);
        Assert.All(measured.GearHistory.GroupBy(i => i.Id), group => Assert.Single(group));
        var replay = CampaignRuntimeReplayRunner.Run(Combat, Adventure, Progression, Content, session.CaptureReplay());
        Assert.True(replay.Success); Assert.Equal(session.StateHash, replay.FinalHash);
    }

    [Fact]
    public void MainPathUsesOnlyEarnedProgressionAndManagedPolicySpendsItsAvailablePoints()
    {
        var session = Fresh(); var reference = new CampaignBalancePolicy(mainPath: true);
        for (int i = 0; i < 8000 && session.Production.ProgressionView.AvailablePassivePoints == 0; i++)
        {
            var result = session.Execute(reference.Next(session)); Assert.True(result.Success, result.Reason);
            Assert.Empty(session.Capture().Campaign.CompletedExploration); Assert.Null(session.Capture().Campaign.Exploration);
        }
        int points = session.Production.ProgressionView.AvailablePassivePoints; Assert.True(points > 0);
        Assert.True(session.ReturnToHub().Success);
        long xp = session.Production.ProgressionView.Experience; int materials = session.Production.ProgressionView.Materials;
        var managed = new CampaignBalancePolicy(mainPath: true, managedBuild: true);
        for (int i = 0; i < 1000 && session.Production.ProgressionView.AvailablePassivePoints > 0; i++)
        { var result = session.Execute(managed.Next(session)); Assert.True(result.Success, result.Reason); }
        Assert.Equal(0, session.Production.ProgressionView.AvailablePassivePoints);
        Assert.Equal(points, session.Capture().Production.Progression.Character.Passives.Values.Sum());
        Assert.Equal(xp, session.Production.ProgressionView.Experience); Assert.Equal(materials, session.Production.ProgressionView.Materials);
        Assert.True(session.Capture().Production.Progression.Character.Passives.GetValueOrDefault("Offense") >= session.Capture().Production.Progression.Character.Passives.GetValueOrDefault("Defense"));
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat, Adventure, Progression, Content, session.CaptureReplay()).Success);
    }

    [Fact]
    public void ReportPoliciesDeclareDistinctReproducibleIdentities()
    {
        var baseline = new CampaignBalancePolicy(); var managed = new CampaignBalancePolicy(managedBuild: true);
        Assert.Equal(baseline.Hash, new CampaignBalancePolicy().Hash);
        Assert.NotEqual(baseline.Hash, managed.Hash);
        Assert.NotEqual(managed.Hash, new CampaignBalancePolicy(mainPath: true, managedBuild: true).Hash);
        Assert.Contains("unspent", baseline.Description); Assert.Contains("earned passive", managed.Description);
    }
    [Fact]
    public void ArcanistHeatLimitsCastingAtCapacityRatherThanAtZero()
    {
        var original = CampaignRuntimeSession.Create(Combat, Adventure, Progression, Content, discipline: "Arcanist").Capture();
        var heatSkills = CampaignRuntimeSession.Restore(Combat, Adventure, Progression, Content, original).Combat.View.Skills
            .Where(s => s.Available && s.ResourceMode == "Heat" && s.Cost > 0).Select(s => s.Id).ToArray();
        Assert.NotEmpty(heatSkills);
        foreach (int resource in new[] { 0, 100 })
        {
            original.Combat.Momentum = resource; original.Production.Expedition.Combat.Momentum = resource;
            var observed = CampaignBalanceObservation.Capture(CampaignRuntimeSession.Restore(Combat, Adventure, Progression, Content, original));
            Assert.Equal(resource == 100, observed.HeatSaturated);
            foreach (string skill in heatSkills) Assert.Equal(resource == 100, observed.ResourceLimitedSkills.Contains(skill));
        }
    }

    [Fact]
    public void EventAttributionCountsOnlyPlayerIncomingDamageAndPlayerActivations()
    {
        var session = Fresh(); var measurement = new CampaignBalanceMeasurement(session);
        var before = CampaignBalanceObservation.Capture(session);
        var result = new CampaignRuntimeResult(true, "", [
            new(0, "DamageApplied", ActorId: 2, TargetId: 1, Amount: 7),
            new(0, "DamageApplied", ActorId: 1, TargetId: 2, Amount: 900),
            new(0, "DamageApplied", ActorId: 3, TargetId: 4, Amount: 600),
            new(0, "AbilityStarted", ActorId: 1, ContentId: "skill.cleave"),
            new(0, "AbilityStarted", ActorId: 2, ContentId: "enemy.skill"),
            new(0, "Healed", ActorId: 1, TargetId: 1, Amount: 12, ContentId: "potion")], []);
        var row = measurement.Observe(before, new(CampaignRuntimeAction.Tick), result, session);
        Assert.Equal(7, row.DamageTaken); Assert.Equal(1, row.Potions);
        Assert.Single(row.SkillsActivated); Assert.Equal(1, row.SkillsActivated["skill.cleave"]);
    }

}
