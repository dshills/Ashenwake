using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class PassiveEffectsTests
{
    private static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(9, 0)]
    [InlineData(10, 1)]
    [InlineData(19, 1)]
    [InlineData(20, 2)]
    [InlineData(99, 9)]
    [InlineData(1000, 100)]
    public void ResourceInvestmentOnlyAddsGenerationAtWholeTenThresholds(int investment, int expected)
    {
        Assert.Equal(expected, PassiveEffects.GenerationBonus(investment));
        var session = IsolatedCombat(new(ResourceBonus: investment));
        List<CombatEvent> events = [.. session.Step([new(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: 2)])];
        for (int i = 0; i < 6; i++) events.AddRange(session.Step());
        Assert.Contains(events, e => e.Kind == "DamageApplied" && e.ActorId == 1 && e.Amount > 0);
        Assert.Equal(Math.Min(100, 14 + expected), session.View.Momentum);
        Assert.Equal(100, session.View.MaxMomentum);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(4, 5, 9, 0)]
    [InlineData(4, 6, 10, 1)]
    [InlineData(997, 9, 1000, 100)]
    public void InspectionCombinesEquippedResourceAffixesAndPassiveRanksWithTheProductionCap(int affix, int rank, int expectedInvestment, int expectedBonus)
    {
        var view = ProgressionSession.Create(ProgressionContent.Default()).View with
        {
            Stats = new Dictionary<string, int>
            {
                ["passive.Offense"] = 3,
                ["passive.Defense"] = 4,
                ["passive.Resource"] = rank,
                ["affix.resource"] = affix,
                ["affix.damage"] = 90,
                ["affix.armor"] = 800
            }
        };
        var result = PassiveEffects.Inspect(view);
        Assert.Equal(600, result.DamageIncreaseBasisPoints); Assert.Equal(400, result.AddedArmor);
        Assert.Equal(expectedInvestment, result.ResourceInvestment); Assert.Equal(expectedBonus, result.GenerationBonus);
    }

    [Fact]
    public void PreviewEffectsMatchTheActualProductionProjectionAndRespec()
    {
        string combat = Read("combat.json"); var adventure = AdventureContent.Parse(Read("adventure.json"));
        var policy = ProgressionContent.Parse(Read("progression.json"));
        var snapshot = ProductionSession.Create(combat, adventure, policy).Capture();
        snapshot.Progression.Character.Experience = 4500;
        snapshot.Expedition.Combat.ProgressionBuild = snapshot.Expedition.Combat.ProgressionBuild with { Level = 10, UltimateUnlocked = true };
        var session = ProductionSession.Restore(combat, adventure, policy, snapshot);
        var baseline = PassiveEffects.Inspect(session.ProgressionView);
        foreach (string passive in new[] { "Offense", "Offense", "Defense", "Resource", "Resource", "Resource" })
        {
            var progression = ProgressionSession.Restore(ProductionContent.Resolve(combat, policy), session.Capture().Progression);
            var preview = progression.PreviewBuild(new(ProgressionBuildAction.AllocatePassive, passive)); Assert.True(preview.Success, preview.Reason);
            Assert.True(session.AllocatePassive(passive).Success);
            var expected = PassiveEffects.Inspect(preview.AfterView); var actual = session.Combat.ProgressionBuild;
            Assert.Equal(expected, PassiveEffects.Inspect(session.ProgressionView));
            Assert.Equal(expected.DamageIncreaseBasisPoints, PassiveEffects.OffenseBasisPoints(actual.Offense));
            Assert.Equal(expected.AddedArmor, PassiveEffects.DefenseArmor(actual.Defense));
            Assert.Equal(expected.ResourceInvestment, actual.ResourceBonus);
            Assert.Equal(expected.GenerationBonus, PassiveEffects.GenerationBonus(actual.ResourceBonus));
        }
        Assert.True(session.Respec().Success);
        Assert.Equal(baseline, PassiveEffects.Inspect(session.ProgressionView));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public void OffenseInspectionMatchesRealDamageIncludingTheActualCriticalRoll(int rank)
    {
        var session = IsolatedCombat(new(Offense: rank)); var target = session.Capture().Actors[1];
        List<CombatEvent> events = [.. session.Step([new(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: target.Id)])];
        for (int i = 0; i < 6; i++) events.AddRange(session.Step());
        var hit = Assert.Single(events, e => e.Kind == "DamageApplied" && e.ContentId == "skill.cleave" && e.ActorId == 1);
        bool critical = events.Any(e => e.Kind == "CriticalHit" && e.ActionId == hit.ActionId);
        int expected = DamageRules.Resolve(new(22, IncreasedBasisPoints: PassiveEffects.OffenseBasisPoints(rank),
            Critical: critical, DefenseBasisPoints: target.Armor)).HealthDamage;
        Assert.Equal(Math.Min(target.Health, expected), hit.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public void DefenseInspectionMatchesRealPhysicalDamageWithTheExistingMitigationCap(int rank)
    {
        string json = Read("combat.json"); var state = IsolatedCombat(new(Defense: rank)).Capture();
        var player = state.Actors[0]; var enemy = state.Actors[1];
        enemy.Position = new(player.Position.X + 1000, player.Position.Z);
        enemy.Pending = new("enemy.strike", player.Id, player.Position, state.Tick, state.NextActionId++);
        var session = CombatSession.Restore(json, state); var events = session.Step();
        var hit = Assert.Single(events, e => e.Kind == "DamageApplied" && e.ActorId == enemy.Id && e.TargetId == player.Id);
        int baseDamage = CombatContent.Parse(json).Enemies.Single(definition => definition.Id == enemy.DefinitionId).Damage;
        int expected = DamageRules.Resolve(new(baseDamage, DefenseBasisPoints: player.Armor + PassiveEffects.DefenseArmor(rank))).HealthDamage;
        Assert.Equal(expected, hit.Amount);
    }

    private static CombatSession IsolatedCombat(CombatProgressionBuild build)
    {
        string json = Read("combat.json"); var state = CombatSession.Create(json).Capture();
        state.Equipment.Clear(); state.Fragments.Clear(); state.Actors[1].Position = new(-2400, 0);
        foreach (var actor in state.Actors.Where(actor => actor.Faction == CombatFaction.Enemy)) actor.RecoveryUntil = 1000;
        var session = CombatSession.Restore(json, state); session.ApplyProgressionBuild(build); return session;
    }
}
