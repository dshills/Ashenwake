using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CombatTests
{
    private static string Content() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json"));
    private static CombatSession Close(ulong seed = 42)
    {
        var snapshot = CombatSession.Create(Content(), seed).Capture();
        snapshot.Actors[1].Position = new(-2400, 0);
        return CombatSession.Restore(Content(), snapshot);
    }
    private static CombatCommand Cast(string skill, int target = 2) => new(CombatCommandKind.Cast, SkillId: skill, TargetId: target);
    private static List<CombatEvent> Advance(CombatSession session, int ticks)
    {
        List<CombatEvent> events = [];
        for (int i = 0; i < ticks; i++) events.AddRange(session.Step());
        return events;
    }
    [Fact]
    public void SixSkillsAndDataDrivenMutationsAreValidated()
    {
        var content = CombatContent.Parse(Content());
        Assert.Equal(6, content.Skills.Length);
        Assert.Equal(6, CombatSession.Create(Content()).View.Skills.Count);
        var invalid = content with { Skills = content.Skills.Select(s => s.Id == "skill.cleave" ? s with { Cost = -1 } : s).ToArray() };
        Assert.Throws<InvalidDataException>(() => CombatSession.Create(JsonData.Write(invalid)));
        Assert.Throws<InvalidDataException>(() => CombatSession.Create(Content().Replace("\"shape\": \"Melee\"", "\"shape\": \"ArbitraryScript\"")));
        Assert.Throws<InvalidDataException>(() => CombatSession.Create(Content().Replace("\"contentVersion\": \"0.3.0\",", "\"contentVersion\": \"0.3.0\", \"unexpected\":true,")));
    }
    [Fact]
    public void DamagePipelineFloorsStagesAndMitigatesBeforeBarrier()
    {
        var damage = DamageRules.Resolve(new(100, 20, IncreasedBasisPoints: 2500, MoreBasisPoints: 12000, Critical: true,
            Conversion: DamageFamily.Fire, DefenseBasisPoints: 4000, PenetrationBasisPoints: 1000, VulnerabilityBasisPoints: 2500, Barrier: 40));
        Assert.Equal(DamageFamily.Fire, damage.Family);
        Assert.Equal(236, damage.BeforeBarrier);
        Assert.Equal(40, damage.Absorbed);
        Assert.Equal(196, damage.HealthDamage);
        Assert.Equal(100, DamageRules.Resolve(new(100, Critical: true, DamageOverTime: true)).HealthDamage);
        Assert.Equal(0, DamageRules.Resolve(new(100, Immune: true)).HealthDamage);
        Assert.Equal(25, DamageRules.Resolve(new(100, DefenseBasisPoints: 99999)).HealthDamage);
    }
    [Fact]
    public void InvalidActionsCannotSpendMomentumOrAdvanceCombatRng()
    {
        var session = Close(); var before = session.Capture();
        var events = session.Step([Cast("skill.shield_breaker"), Cast("missing"), new(CombatCommandKind.Move, X: int.MaxValue), Cast("skill.cleave", 999)]);
        Assert.Equal(4, events.Count(e => e.Kind == "CommandRejected"));
        Assert.Equal(0, session.View.Momentum);
        Assert.Equal(before.Rng.Combat, session.Capture().Rng.Combat);
        Assert.Empty(session.Capture().Cooldowns);
    }
    [Fact]
    public void CleaveGeneratesMomentumOnlyOnHitAndCooldownRejects()
    {
        var session = Close();
        Assert.Contains(session.Step([Cast("skill.cleave")]), e => e.Kind == "AbilityStarted");
        Assert.Equal(0, session.View.Momentum);
        Assert.Contains(session.Step([Cast("skill.cleave")]), e => e.Kind == "CommandRejected");
        var events = Advance(session, 5);
        Assert.Contains(events, e => e.Kind == "DamageApplied" && e.ActorId == 1 && e.Amount > 0);
        Assert.Equal(14, session.View.Momentum);
    }
    [Fact]
    public void DodgeCancelsWindupWithoutRefundAndGrantsInvulnerability()
    {
        var state = Close().Capture(); state.Momentum = 100;
        var session = CombatSession.Restore(Content(), state);
        session.Step([Cast("skill.shield_breaker")]);
        Assert.Equal(80, session.View.Momentum);
        Assert.Contains(session.Step([new(CombatCommandKind.Dodge, X: -1)]), e => e.Kind == "Dodged");
        Assert.Null(session.Capture().Actors[0].Pending);
        Assert.True(session.Capture().Actors[0].InvulnerableUntil > session.Tick);
        Assert.DoesNotContain(Advance(session, 12), e => e.Kind == "AbilityResolved" && e.ActorId == 1);
    }
    [Fact]
    public void StaggerRejectsSkillAndPotionSpendsOnlyForMissingHealth()
    {
        var session = Close();
        Assert.Contains(session.Step([new(CombatCommandKind.Potion)]), e => e.Kind == "CommandRejected");
        Assert.Equal(3, session.View.PotionCharges);
        var state = session.Capture(); state.Actors[0].Health = 100; state.NextActionId = Math.Max(2, state.NextActionId);
        state.Actors[0].Statuses.Add(new() { Id = "Staggered", SourceId = 2, OwnerId = 2, ActionId = 1, ExpiresTick = 10 });
        session = CombatSession.Restore(Content(), state);
        Assert.Contains(session.Step([Cast("skill.cleave")]), e => e.Kind == "CommandRejected" && e.ContentId == "staggered");
        Advance(session, 10);
        Assert.Contains(session.Step([new(CombatCommandKind.Potion)]), e => e.Kind == "Healed");
        Assert.Equal(2, session.View.PotionCharges);
    }
    [Fact]
    public void DotDeathProducesOneCorpseDropAndAttributedSpiritPoison()
    {
        var state = Close().Capture(); state.Actors[1].Health = 1;
        state.Actors[1].Statuses.Add(new() { Id = "Burning", SourceId = 1, OwnerId = 1, FragmentId = "fragment.eye_vael", NextTick = 0, ExpiresTick = 90, ActionId = 1, Depth = 1 });
        state.NextActionId = 2;
        var session = CombatSession.Restore(Content(), state);
        var events = Advance(session, 180);
        Assert.Single(events, e => e.Kind == "EntityKilled" && e.TargetId == 2);
        Assert.Single(events, e => e.Kind == "LootDropped" && e.TargetId == 2);
        Assert.Contains(events, e => e.Kind == "SummonSpawned" && e.ContentId == "fragment.heart_serath");
        Assert.Contains(events, e => e.Kind == "StatusApplied" && e.ContentId == "Poisoned");
        Assert.True(session.View.Actors.Count(a => a.Faction == CombatFaction.Ally) <= CombatSession.MaxSummons);
    }
    [Fact]
    public void UnequippingFragmentRemovesItsTriggersAndOwnedEffects()
    {
        var session = CombatSession.Create(Content(), preset: "summons");
        Assert.Contains(session.View.Actors, a => a.Faction == CombatFaction.Ally);
        session.Step([new(CombatCommandKind.UnequipFragment, ContentId: "fragment.heart_serath")]);
        Assert.DoesNotContain(session.View.Actors, a => a.Faction == CombatFaction.Ally);
        Assert.False(session.View.Fragments.Single(f => f.Id == "fragment.heart_serath").Equipped);
        session.Step([new(CombatCommandKind.EquipFragment, ContentId: "fragment.heart_serath")]);
        Assert.True(session.View.Fragments.Single(f => f.Id == "fragment.heart_serath").Equipped);
    }
    [Fact]
    public void MutationChangesAbilityBehaviorAndExcludesOtherMutation()
    {
        var state = Close().Capture(); state.Momentum = 100;
        var session = CombatSession.Restore(Content(), state);
        session.Step([new(CombatCommandKind.SetMutation, ContentId: "mutation.avalanche")]);
        Assert.Equal("Area", session.View.Skills.Single(s => s.Id == "skill.shield_breaker").Shape);
        session.Step([new(CombatCommandKind.SetMutation, ContentId: "mutation.no_ground_given")]);
        Assert.Equal("Guard", session.View.Skills.Single(s => s.Id == "skill.shield_breaker").Shape);
        session.Step([Cast("skill.shield_breaker")]);
        Advance(session, 10);
        Assert.True(session.View.Barrier > 0);
        Assert.Single(session.Capture().Mutations);
    }
    [Fact]
    public void ProjectileAndAreaHaveAuthoritativeLifetimeAndOwnership()
    {
        var state = Close().Capture(); state.Momentum = 100;
        var session = CombatSession.Restore(Content(), state);
        session.Step([Cast("skill.seismic_wave")]);
        var projectileEvents = Advance(session, 14);
        Assert.Contains(projectileEvents, e => e.Kind == "DamageApplied" && e.ContentId == "skill.seismic_wave");
        session.Step([Cast("skill.cataclysm")]);
        Advance(session, 16);
        Assert.Contains(session.View.Areas, a => a.ContentId == "skill.cataclysm" && a.OwnerId == 1);
        Advance(session, 65);
        Assert.DoesNotContain(session.View.Areas, a => a.ContentId == "skill.cataclysm");
    }
    [Fact]
    public void ChargeStopsBeforeTargetInsteadOfRejectingOccupiedDestination()
    {
        var session = Close(); var before = session.View.Actors[0].Position;
        session.Step([Cast("skill.charge")]);
        Advance(session, 3);
        var after = session.View.Actors[0].Position;
        Assert.True(after.X > before.X + 1000);
        Assert.True(Position.DistanceSquared(after, session.View.Actors[1].Position) >= 4L * CombatSession.ActorRadius * CombatSession.ActorRadius);
    }
    [Fact]
    public void MalformedSnapshotsRejectNullLootAndForgedEffectSources()
    {
        var state = Close().Capture();
        state.Loot.Add(null!);
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content(), state));
        state = Close().Capture(); state.NextActionId = 2;
        state.Projectiles.Add(new(100, 1, 999, state.Actors[0].Position, state.Actors[1].Position, 2, "skill.cleave", 20, DamageFamily.Fire, 50, 1, 0));
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content(), state));
        state = Close().Capture(); state.NextObjectId = 1;
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content(), state));
        var content = CombatContent.Parse(Content());
        Assert.Throws<InvalidDataException>(() => CombatSession.Create(JsonData.Write(content with { Enemies = content.Enemies.Select(e => e with { Role = "Melee" }).ToArray() })));
    }
    [Fact]
    public void MovementSweepRespectsObstacleAndRoomBounds()
    {
        var session = CombatSession.Create(Content());
        session.Step([new(CombatCommandKind.Move, X: -1)]);
        Advance(session, 200);
        Assert.InRange(session.View.Actors.Single(a => a.Id == 1).Position.X, -12000 + CombatSession.ActorRadius, 12000);
    }
    [Fact]
    public void SameSeedInputsAndSaveContinuationProduceSameEventsAndHashes()
    {
        var a = Close(55); var b = Close(55);
        for (int i = 0; i < 240; i++)
        {
            CombatCommand[] commands = i % 12 == 0 ? [Cast("skill.cleave")] : [];
            Assert.Equal(JsonData.Write(a.Step(commands)), JsonData.Write(b.Step(commands)));
            Assert.Equal(a.StateHash, b.StateHash);
            if (i == 70) b = CombatSession.Restore(Content(), b.Capture());
        }
    }
    [Fact]
    public void SnapshotsAndViewsCannotMutateAuthoritativeState()
    {
        var session = Close(); var hash = session.StateHash;
        session.Capture().Actors[0].Health = 1;
        ((CombatActorView[])session.View.Actors)[0] = session.View.Actors[0] with { Health = 1 };
        Assert.Equal(hash, session.StateHash);
        var invalid = session.Capture(); invalid.Actors[0].Health = int.MaxValue;
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content(), invalid));
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content(), session.Capture() with { ContentHash = "wrong" }));
    }
    [Theory]
    [InlineData("dense")]
    [InlineData("projectiles")]
    [InlineData("summons")]
    [InlineData("chain")]
    public void StressPresetsStayBoundedAndRestoreAtEveryBoundary(string preset)
    {
        var session = CombatSession.Create(Content(), 123, preset);
        for (int i = 0; i < 600; i++)
        {
            session.Step(i % 210 == 0 ? [Cast("skill.cataclysm")] : []);
            if (i % 30 == 0)
            {
                var before = session.StateHash;
                session = CombatSession.Restore(Content(), session.Capture());
                Assert.Equal(before, session.StateHash);
            }
        }
        Assert.InRange(session.View.Actors.Count, 1, CombatSession.MaxActors);
        Assert.InRange(session.View.Projectiles.Count, 0, CombatSession.MaxProjectiles);
        Assert.InRange(session.View.Areas.Count, 0, CombatSession.MaxAreas);
        Assert.InRange(session.View.Actors.Count(a => a.Faction == CombatFaction.Ally), 0, CombatSession.MaxSummons);
        Assert.InRange(session.View.PeakEffects, 0, CombatSession.MaxEffectsPerTick);
    }
}
