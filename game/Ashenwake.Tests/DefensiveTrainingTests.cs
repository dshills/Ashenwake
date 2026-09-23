using System.Reflection;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Training;
using Xunit;

namespace Ashenwake.Tests;

public sealed class DefensiveTrainingTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static readonly Lazy<string> Json = new(() => EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson,
        Read("endgame-combat.json"), EndgameContent.Parse(Read("endgame.json"))).CombatJson);
    private static EndgameRuntimeSession AtGround()
    {
        var source = EndgameRuntimeSession.Create(Json.Value, AdventureContent.Parse(Read("adventure.json")), ProgressionContent.Parse(Read("progression.json")),
            CampaignContent.Parse(Read("campaign.json")), EndgameContent.Parse(Read("endgame.json")));
        for (int i = 0; i < 18; i++) Assert.True(source.Step(new CombatCommand(CombatCommandKind.Move, Z: 1)).Success);
        source.Step(new CombatCommand(CombatCommandKind.Stop)); return source;
    }
    private static TrainingSession Fixture(TrainingTargetMode mode, CombatProgressionBuild? build = null)
    {
        var body = CombatSession.CreateEncounter(Json.Value, 42, "hub");
        if (build is not null) body.ApplyProgressionBuild(build);
        return (TrainingSession)Activator.CreateInstance(typeof(TrainingSession), BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { Json.Value, body.Capture(), mode }, null)!;
    }
    private static CombatSnapshot MutableState(TrainingSession training) => (CombatSnapshot)typeof(CombatSession)
        .GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(training.Combat)!;
    private static void Partition(TrainingDefenseReport report)
    {
        Assert.Equal(report.RawDamage, report.MitigatedDamage + report.ImmuneDamage + report.BarrierAbsorbed + report.HealthLost + report.Overkill);
        foreach (var row in report.Damage)
            Assert.Equal(row.RawDamage, row.MitigatedDamage + row.ImmuneDamage + row.BarrierAbsorbed + row.HealthLost + row.Overkill);
        Assert.Equal(report.HealthLost, report.Damage.Sum(r => r.HealthLost));
        Assert.Equal(report.Hits, report.Damage.Sum(r => r.Hits));
    }
    [Theory]
    [InlineData(TrainingTargetMode.Melee)]
    [InlineData(TrainingTargetMode.Ranged)]
    [InlineData(TrainingTargetMode.Mixed)]
    public void OrdinarySparringAttacksResolveAndDeathsNeverChangeEarnedJourney(TrainingTargetMode mode)
    {
        var source = AtGround(); string hash = source.StateHash; var training = source.CreateTrainingSession(mode);
        var initial = training.Combat.Capture(); var events = new List<CombatEvent>();
        for (int i = 0; i < 3000 && !training.IsComplete; i++) events.AddRange(training.Step());
        Assert.True(training.IsComplete); Assert.Equal(0, training.Combat.View.Actors.Single(a => a.Id == 1).Health);
        Assert.Contains(events, e => e.Kind == "AbilityStarted" && e.TargetId == 1);
        Assert.Equal(initial.Actors.Single(a => a.Id == 1).Health, training.Report.Defense.HealthLost);
        Assert.Equal(events.Where(e => e.Kind == "DamageApplied" && e.TargetId == 1).Sum(e => e.Amount), training.Report.Defense.HealthLost);
        if (mode is TrainingTargetMode.Melee or TrainingTargetMode.Mixed)
            Assert.Contains(training.Report.Defense.Damage, r => r.SourceId == "enemy.ash_ghoul" && r.Family == DamageFamily.PhysicalCrush && r.HealthLost > 0);
        if (mode is TrainingTargetMode.Ranged or TrainingTargetMode.Mixed)
            Assert.Contains(training.Report.Defense.Damage, r => r.SourceId == "enemy.cinder_acolyte" && r.Family == DamageFamily.Fire && r.HealthLost > 0);
        Assert.Empty(training.Combat.View.Loot); Assert.Null(training.Combat.LastDeathRecap);
        Assert.DoesNotContain(events, e => e.Kind == "LootDropped"); Partition(training.Report.Defense);
        Assert.Equal(2, training.CaptureReplay().SchemaVersion); Assert.True(TrainingSession.VerifyReplay(Json.Value, training.CaptureReplay()));
        var final = training.Report; training.Step(new CombatCommand(CombatCommandKind.Potion)); Assert.Equal(JsonData.Hash(final), JsonData.Hash(training.Report));
        training.Reset(mode); Assert.Equal(JsonData.Hash(initial), training.Combat.StateHash);
        Assert.Equal(0, training.Report.Defense.HealthLost); Assert.Empty(training.Report.Defense.Damage);
        Assert.Empty(training.Report.Defense.Triggers); training.Close(); Assert.Equal(hash, source.StateHash);
    }
    [Fact]
    public void IncomingDamagePartitionsMitigationImmunityBarriersAndOverkillUsingRealResolver()
    {
        var training = Fixture(TrainingTargetMode.Single, new(Armor: 4500));
        var state = MutableState(training); var player = state.Actors.Single(a => a.Id == 1);
        player.Barrier = 10; player.Health = 12; player.InvulnerableUntil = state.Tick + 1;
        var enemy = state.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        void Area(int damage, int delay) => state.Areas.Add(new(state.NextObjectId++, enemy.Id, enemy.Id, player.Position, 1000,
            "enemy.strike", damage, DamageFamily.PhysicalCrush, state.Tick + delay, state.Tick + delay + 1, state.NextActionId++, 0));
        Area(20, 0); Area(20, 1); Area(100, 2);
        var events = new List<CombatEvent>(); events.AddRange(training.Step()); events.AddRange(training.Step()); events.AddRange(training.Step());
        var defense = training.Report.Defense; Partition(defense);
        Assert.Equal(140, defense.RawDamage); Assert.True(defense.MitigatedDamage > 0); Assert.True(defense.ImmuneDamage > 0);
        Assert.Equal(10, defense.BarrierAbsorbed); Assert.Equal(12, defense.HealthLost); Assert.True(defense.Overkill > 0); Assert.Equal(3, defense.Hits);
        Assert.Equal(events.Where(e => e.Kind == "BarrierAbsorbed" && e.TargetId == 1).Sum(e => e.Amount), defense.BarrierAbsorbed);
    }
    [Fact]
    public void DodgeImmunityAndOathReprisalUseActualEnemyDamageAndLegendaryEvents()
    {
        var training = Fixture(TrainingTargetMode.Melee, new(BarrierOnDodge: true) { OathReprisal = true });
        var events = new List<CombatEvent>();
        // Wait for an authored windup and dodge toward the attacker: immunity is timed, while the guard rune grants a real barrier.
        for (int i = 0; i < 120 && !training.Combat.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.TelegraphTicks is > 0 and <= 6); i++) events.AddRange(training.Step());
        events.AddRange(training.Step(new CombatCommand(CombatCommandKind.Dodge, X: 1)));
        for (int i = 0; i < 90; i++) events.AddRange(training.Step());
        var target = training.Combat.View.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        for (int i = 0; i < 30 && !training.IsComplete; i++) events.AddRange(training.Step(new CombatCommand(CombatCommandKind.Cast,
            SkillId: "skill.cleave", TargetId: target.Id, X: target.Position.X, Z: target.Position.Z)));
        Assert.Contains(events, e => e.Kind == "LegendaryCharged" && e.ContentId == "property.oath_reprisal");
        Assert.Contains(events, e => e.Kind == "LegendaryTriggered" && e.ContentId == "property.oath_reprisal");
        var report = training.Report.Defense;
        Assert.True(report.ImmuneDamage > 0); Assert.True(report.BarrierAbsorbed > 0);
        Assert.Contains(report.Triggers, r => r.Kind == "BarrierGranted" && r.SourceId == "rune.guard");
        Assert.Contains(report.Triggers, r => r.Kind == "LegendaryCharged" && r.SourceId == "property.oath_reprisal");
        Assert.Contains(report.Triggers, r => r.Kind == "LegendaryTriggered" && r.SourceId == "property.oath_reprisal");
        Partition(report); Assert.True(TrainingSession.VerifyReplay(Json.Value, training.CaptureReplay()));
    }
    [Fact]
    public void ReportSnapshotsSurviveResetAndVersionOnePassiveReplayRetainsItsHistoricalShape()
    {
        var training = AtGround().CreateTrainingSession(); var frames = new List<TrainingFrame>();
        for (int i = 0; i < 60; i++)
        {
            training.Step(); var report = training.Report;
            string oldHash = JsonData.Hash(new
            {
                report.Mode,
                report.ElapsedTicks,
                report.ElapsedSeconds,
                report.TotalDamage,
                report.DamagePerSecond,
                report.ResourceIncreased,
                report.ResourceDecreased,
                report.CurrentResource,
                report.ResourceName,
                report.TargetsDefeated,
                report.Complete,
                report.Damage,
                report.Triggers,
                report.Resources
            });
            frames.Add(new([], training.Combat.StateHash, oldHash));
        }
        var replay = training.CaptureReplay(); Assert.True(TrainingSession.VerifyReplay(Json.Value, replay with { SchemaVersion = 1, Frames = frames.ToArray() }));
        var previous = JsonData.Copy(training.Report); string previousHash = JsonData.Hash(previous);
        training.Reset(TrainingTargetMode.Mixed); for (int i = 0; i < 120; i++) training.Step();
        Assert.Equal(previousHash, JsonData.Hash(previous)); Assert.True(training.Report.Defense.HealthLost > 0);
        var altered = training.CaptureReplay(); altered.Frames[^1] = altered.Frames[^1] with { ReportHash = "tampered" };
        Assert.False(TrainingSession.VerifyReplay(Json.Value, altered));
        Assert.Throws<InvalidDataException>(() => TrainingSession.VerifyReplay(Json.Value, altered with { SchemaVersion = 1 }));
    }
}
