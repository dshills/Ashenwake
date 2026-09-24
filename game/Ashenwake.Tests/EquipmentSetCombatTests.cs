using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Ashenwake.Core.Training;
using System.Reflection;
using Xunit;

namespace Ashenwake.Tests;

public sealed class EquipmentSetCombatTests
{
    private static string Content => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json"));
    private static CombatSession Session(CombatProgressionBuild build)
    {
        var hub = CombatSession.CreateEncounter(Content, 42, "hub"); hub.ApplyProgressionBuild(build);
        var state = CombatSession.CreateEncounter(Content, 42, "encounter.ossuary", hub.Capture()).Capture();
        state.Fragments.Clear(); state.Equipment.Clear(); state.Actors[0].Position = new(-4500, 0); state.Momentum = 100;
        int index = 0;
        foreach (var enemy in state.Actors.Where(a => a.Faction == CombatFaction.Enemy).ToArray())
            state.Actors[state.Actors.IndexOf(enemy)] = enemy with
            { Position = new(-2500 + index++ * 800, 0), Health = 10000, MaxHealth = 10000, Armor = 0, Resistance = 0, RecoveryUntil = 2500, Hidden = false };
        return CombatSession.Restore(Content, state);
    }
    private static CombatEvent[] Advance(CombatSession session, int ticks)
    { List<CombatEvent> events = []; for (int i = 0; i < ticks; i++) events.AddRange(session.Step()); return events.ToArray(); }
    private static CombatEvent[] Attack(CombatSession session)
        => [.. session.Step([new(CombatCommandKind.Cast, SkillId: "skill.shield_breaker", TargetId: session.View.Actors[1].Id)]), .. Advance(session, 12)];
    private static void Incoming(CombatSnapshot state, int damage = 30)
    {
        int source = state.Actors[1].Id;
        state.Areas.Add(new(state.NextObjectId++, source, source, state.Actors[0].Position, 5000, "enemy.detonate", damage,
            DamageFamily.Fire, state.Tick, state.Tick + 1, state.NextActionId++, 0));
    }
    private static CombatSession Vigil()
    {
        var state = Session(new() { LastVigilSet = true }).Capture(); state.Actors[0].Barrier = 50; Incoming(state);
        var session = CombatSession.Restore(Content, state); session.Step(); return session;
    }
    private static CombatSession Runner()
    {
        var state = Session(new() { AshrunnerSet = true }).Capture(); Incoming(state);
        var session = CombatSession.Restore(Content, state); session.Step([new(CombatCommandKind.Dodge, Z: 1)]);
        state = session.Capture(); state.Actors[0].Position = new(-4500, 0);
        session = CombatSession.Restore(Content, state); Advance(session, 6); return session;
    }
    private static CombatSnapshot PoisonKill()
    {
        var state = Session(new() { BriarboundSet = true }).Capture();
        state.Actors[1].Health = 1;
        state.Actors[1].Statuses.Add(new() { Id = "Poisoned", SourceId = 1, OwnerId = 1, OriginSkill = "skill.venom_knife", ActionId = state.NextActionId++, NextTick = state.Tick, ExpiresTick = state.Tick + 90 });
        return state;
    }

    [Fact]
    public void InactiveStateAndBuildKeepHistoricalSerializationUnchanged()
    {
        var session = Session(new());
        Assert.DoesNotContain("equipmentSets", JsonData.Write(session.Capture()));
        Assert.DoesNotContain("lastVigilSet", JsonData.Write(session.ProgressionBuild));
        Assert.DoesNotContain("briarboundSet", JsonData.Write(session.ProgressionBuild));
        Assert.DoesNotContain("ashrunnerSet", JsonData.Write(session.ProgressionBuild));
        Assert.Null(session.View.EquipmentSets);
    }

    [Fact]
    public void AbsorbedDamageReadiesOneSpectralCounterWithReplayAndNoAdditionalCombatRng()
    {
        var session = Vigil();
        Assert.Equal(119, session.View.EquipmentSets!.VigilReadyTicks);
        var before = session.Capture(); var baselineState = JsonData.Copy(before); baselineState.EquipmentSets = null;
        var baseline = CombatSession.Restore(Content, baselineState); Attack(baseline);
        var recorder = new CombatRecorder(session);
        List<CombatEvent> events = [.. recorder.Step(session, [new(CombatCommandKind.Cast, SkillId: "skill.shield_breaker", TargetId: session.View.Actors[1].Id)])];
        for (int i = 0; i < 12; i++) events.AddRange(recorder.Step(session, []));
        Assert.Equal(36, Assert.Single(events, e => e.Kind == "DamageApplied" && e.ContentId == "effect.set_vigil_strike").Amount);
        Assert.Single(events, e => e.Kind == "EquipmentSetTriggered" && e.ContentId == EquipmentSets.LastVigil);
        Assert.Equal(0, session.View.EquipmentSets!.VigilReadyTicks);
        Assert.InRange(session.View.EquipmentSets.VigilCooldownTicks, 70, 90);
        Assert.Equal(baseline.Capture().Rng, session.Capture().Rng);
        Assert.Equal(session.StateHash, CombatSession.Restore(Content, session.Capture()).StateHash);
        Assert.True(CombatReplayRunner.Run(Content, recorder.Capture()).Success);
    }

    [Theory]
    [InlineData("no-barrier")]
    [InlineData("zero-damage")]
    [InlineData("cooldown")]
    [InlineData("unequipped")]
    [InlineData("dead-source")]
    public void VigilRequiresQualifyingAbsorption(string reason)
    {
        var state = Session(new() { LastVigilSet = reason != "unequipped" }).Capture();
        state.Actors[0].Barrier = reason == "no-barrier" ? 0 : 50; Incoming(state, reason == "zero-damage" ? 0 : 30);
        if (reason == "cooldown") state.EquipmentSets = new() { VigilReadyTick = 90 };
        if (reason == "dead-source") { state.Actors[1].Health = 0; state.Actors[1].DeathProcessed = true; }
        var session = CombatSession.Restore(Content, state);
        Assert.DoesNotContain(session.Step(), e => e.Kind == "EquipmentSetReadied");
    }

    [Fact]
    public void PoisonTickCannotConsumeReadiedVigilAndReadinessExpires()
    {
        var state = Vigil().Capture();
        state.Actors[1].Statuses.Add(new() { Id = "Poisoned", SourceId = 1, OwnerId = 1, ActionId = state.NextActionId++, NextTick = state.Tick, ExpiresTick = state.Tick + 60 });
        var session = CombatSession.Restore(Content, state);
        Assert.DoesNotContain(session.Step(), e => e.Kind == "EquipmentSetTriggered");
        Assert.True(session.View.EquipmentSets!.VigilReadyTicks > 0);
        Advance(session, 121); Assert.Null(session.Capture().EquipmentSets);
    }

    [Fact]
    public void SuccessfulDodgeReadiesNextHitAndProducesThreeBoundedEmberPatches()
    {
        var session = Runner(); Assert.True(session.View.EquipmentSets!.AshrunnerReadyTicks > 0);
        var events = Attack(session);
        Assert.Equal(3, Assert.Single(events, e => e.Kind == "EquipmentSetTriggered" && e.ContentId == EquipmentSets.Ashrunner).Amount);
        var patches = session.Capture().Areas.Where(a => a.SkillId == "effect.set_ashrunner_trail").ToArray();
        Assert.Equal(3, patches.Length);
        Assert.All(patches, p => { Assert.Equal(650, p.Radius); Assert.Equal(6, p.Damage); Assert.Equal(1, p.Depth); Assert.Equal(DamageFamily.Fire, p.Family); });
        Assert.Equal(0, session.View.EquipmentSets.AshrunnerReadyTicks);
        Assert.Equal(session.StateHash, CombatSession.Restore(Content, session.Capture()).StateHash);
        Advance(session, 61); Assert.DoesNotContain(session.Capture().Areas, a => a.SkillId == "effect.set_ashrunner_trail");
    }

    [Theory]
    [InlineData("empty-dodge")]
    [InlineData("ordinary-hit")]
    [InlineData("other-immunity")]
    [InlineData("zero-damage")]
    [InlineData("dot")]
    [InlineData("cooldown")]
    public void AshrunnerRequiresActualDamageAvoidedDuringItsDodgeWindow(string reason)
    {
        var state = Session(new() { AshrunnerSet = true }).Capture();
        if (reason != "empty-dodge" && reason != "dot") Incoming(state, reason == "zero-damage" ? 0 : 30);
        if (reason == "other-immunity") state.Actors[0].InvulnerableUntil = 7;
        if (reason == "cooldown") state.EquipmentSets = new() { AshrunnerReadyTick = 90 };
        if (reason == "dot") state.Actors[0].Statuses.Add(new() { Id = "Burning", SourceId = state.Actors[1].Id, OwnerId = state.Actors[1].Id, NextTick = 0, ExpiresTick = 90, ActionId = state.NextActionId++ });
        var session = CombatSession.Restore(Content, state);
        var events = session.Step(reason is "ordinary-hit" or "other-immunity" ? [] : [new(CombatCommandKind.Dodge, Z: 1)]);
        Assert.DoesNotContain(events, e => e.Kind == "EquipmentSetReadied");
        Assert.Equal(0, session.View.EquipmentSets!.AshrunnerReadyTicks);
    }

    [Fact]
    public void BriarPoisonKillHealsOnlyLivingOwnedNearbyCompanionsWithoutOverhealAndGrowsOnePatch()
    {
        var state = PoisonKill();
        foreach (var health in new[] { 5, 25, 0 }) state.Actors.Add(new()
        {
            Id = state.NextActorId++,
            DefinitionId = "summon.companion",
            Faction = CombatFaction.Ally,
            Role = "Companion",
            Position = new(-2600, 1400 + state.Actors.Count * 30),
            Health = health,
            MaxHealth = 30,
            OwnerId = 1,
            ExpiresTick = 500,
            RecoveryUntil = 500,
            DeathProcessed = health == 0
        });
        state.Actors.Add(new()
        {
            Id = state.NextActorId++,
            DefinitionId = "summon.companion",
            Faction = CombatFaction.Ally,
            Role = "Companion",
            Position = new(7000, 6000),
            Health = 5,
            MaxHealth = 30,
            OwnerId = 1,
            ExpiresTick = 500,
            RecoveryUntil = 500
        });
        var session = CombatSession.Restore(Content, state); var recorder = new CombatRecorder(session); var events = recorder.Step(session, []);
        Assert.Equal(25, events.Where(e => e.Kind == "Healed" && e.ContentId == EquipmentSets.Briarbound).Sum(e => e.Amount));
        Assert.Equal(25, Assert.Single(events, e => e.Kind == "EquipmentSetTriggered" && e.ContentId == EquipmentSets.Briarbound).Amount);
        var patch = Assert.Single(session.Capture().Areas, a => a.SkillId == "effect.set_briar_thorns");
        Assert.Equal(new Position(-2500, 0), patch.Position); Assert.Equal(1100, patch.Radius); Assert.Equal(8, patch.Damage);
        Assert.Equal(5, session.View.Actors.Single(a => a.Position == new Position(7000, 6000)).Health);
        for (int i = 0; i < 65; i++) recorder.Step(session, []);
        Assert.True(CombatReplayRunner.Run(Content, recorder.Capture()).Success);
        Assert.DoesNotContain(session.Capture().Areas, a => a.SkillId == "effect.set_briar_thorns");
    }

    [Theory]
    [InlineData("not-poison")]
    [InlineData("not-owned")]
    [InlineData("cooldown")]
    [InlineData("recursive")]
    public void BriarRejectsUnqualifiedKills(string reason)
    {
        var state = PoisonKill();
        if (reason == "not-poison") state.Actors[1].Statuses[0].Id = "Burning";
        if (reason == "not-owned") { state.Actors[1].Statuses[0].SourceId = state.Actors[2].Id; state.Actors[1].Statuses[0].OwnerId = state.Actors[2].Id; }
        if (reason == "cooldown") state.EquipmentSets = new() { BriarReadyTick = 90 };
        if (reason == "recursive") state.Actors[1].Statuses[0].OriginSkill = "effect.set_briar_thorns";
        var session = CombatSession.Restore(Content, state);
        Assert.DoesNotContain(session.Step(), e => e.Kind == "EquipmentSetTriggered");
    }

    [Theory]
    [InlineData("unequip")]
    [InlineData("transition")]
    [InlineData("death")]
    public void BreakingSetDeathOrRoomTransitionClearsReadinessAndGroundEffects(string reason)
    {
        var session = Runner(); Attack(session);
        if (reason == "unequip") session.ApplyProgressionBuild(new());
        if (reason == "transition") session = CombatSession.CreateEncounter(Content, 42, "hub", session.Capture());
        if (reason == "death")
        {
            var state = session.Capture(); state.Actors[0].Health = 1; Incoming(state, 1000);
            session = CombatSession.Restore(Content, state); session.Step(); Assert.Equal(0, session.View.Actors[0].Health);
        }
        Assert.Null(session.Capture().EquipmentSets);
        Assert.DoesNotContain(session.Capture().Areas, a => a.SkillId.StartsWith("effect.set_", StringComparison.Ordinal));
        Assert.Equal(session.StateHash, CombatSession.Restore(Content, session.Capture()).StateHash);
    }

    [Theory]
    [InlineData("future-readiness")]
    [InlineData("missing-set")]
    [InlineData("forged-dodge")]
    public void InvalidReadinessArchivesAreRejected(string reason)
    {
        var state = Session(new() { LastVigilSet = reason != "missing-set", AshrunnerSet = true }).Capture();
        state.EquipmentSets = reason == "forged-dodge" ? new() { AshrunnerDodgeUntil = 7 } : new() { VigilUntil = reason == "future-readiness" ? 121 : 120 };
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content, state));
    }

    [Fact]
    public void VanishImmunityOutlivingDodgeDoesNotReadyAshrunner()
    {
        var session = Session(new() { AshrunnerSet = true }); session.Step([new(CombatCommandKind.Dodge, Z: 1)]);
        var state = session.Capture(); state.Actors[0].InvulnerableUntil = 30;
        session = CombatSession.Restore(Content, state); Advance(session, 8);
        state = session.Capture(); Incoming(state);
        session = CombatSession.Restore(Content, state);
        Assert.DoesNotContain(session.Step(), e => e.Kind == "EquipmentSetReadied");
        Assert.Equal(0, session.View.EquipmentSets!.AshrunnerReadyTicks);
        Assert.Null(session.Capture().EquipmentSets);
    }

    [Fact]
    public void EmberTrailStopsAtWallsWhenAnEarlierProjectileHitsAfterRepositioning()
    {
        var definition = CombatContent.Parse(Content);
        definition = definition with { Room = definition.Room with { Obstacles = [new(-3800, -1000, -3500, 1000)] } };
        string json = JsonData.Write(definition);
        var state = Session(new() { AshrunnerSet = true }).Capture() with { ContentHash = definition.Identity };
        state.EquipmentSets = new() { AshrunnerUntil = 120 };
        var target = state.Actors[1];
        state.Projectiles.Add(new(state.NextObjectId++, 1, 1, target.Position, target.Position, target.Id,
            "skill.fire_lance", 40, DamageFamily.Fire, 90, state.NextActionId++, 0));
        var session = CombatSession.Restore(json, state);
        var events = session.Step();
        Assert.Equal(1, Assert.Single(events, e => e.Kind == "EquipmentSetTriggered" && e.ContentId == EquipmentSets.Ashrunner).Amount);
        var area = Assert.Single(session.Capture().Areas, a => a.SkillId == "effect.set_ashrunner_trail");
        Assert.Equal(state.Actors[0].Position, area.Position);
        Assert.Equal(session.StateHash, CombatSession.Restore(json, session.Capture()).StateHash);
    }

    [Fact]
    public void SimultaneousPoisonKillsCreateOnlyOnePatchAndHealAtMostEightCompanions()
    {
        var state = PoisonKill();
        state.Actors[2].Health = 1;
        state.Actors[2].Statuses.Add(new() { Id = "Poisoned", SourceId = 1, OwnerId = 1, ActionId = state.NextActionId++, NextTick = 0, ExpiresTick = 90 });
        for (int i = 0; i < 10; i++) state.Actors.Add(new()
        {
            Id = state.NextActorId++,
            DefinitionId = "summon.companion",
            Faction = CombatFaction.Ally,
            Role = "Companion",
            Position = new(-2600, 1200 + i * 50),
            Health = 1,
            MaxHealth = 40,
            OwnerId = 1,
            ExpiresTick = 500,
            RecoveryUntil = 500
        });
        var session = CombatSession.Restore(Content, state); var events = session.Step();
        Assert.Equal(8, events.Count(e => e.Kind == "Healed" && e.ContentId == EquipmentSets.Briarbound));
        Assert.Single(events, e => e.Kind == "EquipmentSetTriggered" && e.ContentId == EquipmentSets.Briarbound);
        Assert.Single(session.Capture().Areas, a => a.SkillId == "effect.set_briar_thorns");
        Assert.Equal(session.StateHash, CombatSession.Restore(Content, session.Capture()).StateHash);
    }

    [Fact]
    public void GroundDamageDoesNotDrawAdditionalCombatRngOrRearmSets()
    {
        var session = Runner(); Attack(session);
        var state = session.Capture(); var baselineState = JsonData.Copy(state); baselineState.Areas.RemoveAll(a => a.SkillId == "effect.set_ashrunner_trail");
        var baseline = CombatSession.Restore(Content, baselineState);
        var events = Advance(session, 65); Advance(baseline, 65);
        Assert.DoesNotContain(events, e => e.Kind is "EquipmentSetReadied" or "EquipmentSetTriggered");
        Assert.Equal(baseline.Capture().Rng, session.Capture().Rng);
    }

    [Fact]
    public void TrainingCountsActualSetReadinessAndDamageAndResetClearsItsTransientState()
    {
        var source = CombatSession.CreateEncounter(Content, 42, "hub"); source.ApplyProgressionBuild(new() { LastVigilSet = true });
        string original = source.StateHash;
        // Same isolated diagnostic constructor used by defensive training tests;
        // every attack and barrier in the practice session uses ordinary commands.
        var training = (TrainingSession)Activator.CreateInstance(typeof(TrainingSession), BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { Content, source.Capture(), TrainingTargetMode.Mixed }, null)!;
        for (int i = 0; i < 600 && !training.IsComplete && !training.Report.Damage.Any(d => d.SourceId == "effect.set_vigil_strike"); i++)
        {
            var view = training.Combat.View; var player = view.Actors.Single(a => a.Id == 1);
            var target = view.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0).OrderByDescending(a => a.Health).First();
            bool ready = view.EquipmentSets!.VigilReadyTicks > 0;
            bool guard = !ready && view.Resource >= 15 && view.Skills.Single(s => s.Id == "skill.iron_guard").RemainingTicks == 0;
            bool attack = ready || view.Resource < 15;
            List<CombatCommand> commands = [];
            if (attack && Position.DistanceSquared(player.Position, target.Position) > 2000L * 2000)
                commands.Add(new(CombatCommandKind.Move, X: Math.Sign(target.Position.X - player.Position.X), Z: Math.Sign(target.Position.Z - player.Position.Z)));
            else commands.Add(new(CombatCommandKind.Stop));
            if (guard) commands.Add(new(CombatCommandKind.Cast, SkillId: "skill.iron_guard", TargetId: target.Id));
            else if (attack) commands.Add(new(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: target.Id));
            training.Step(commands.ToArray());
        }
        Assert.Contains(training.Report.Triggers, t => t.Kind == "EquipmentSetReadied" && t.SourceId == EquipmentSets.LastVigil && t.Name == "Vestments of the Last Vigil");
        Assert.Contains(training.Report.Triggers, t => t.Kind == "EquipmentSetTriggered" && t.SourceId == EquipmentSets.LastVigil);
        Assert.Contains(training.Report.Damage, d => d.Category == "Equipment set" && d.SourceId == "effect.set_vigil_strike" && d.Damage > 0);
        Assert.True(TrainingSession.VerifyReplay(Content, training.CaptureReplay()));
        training.Reset(TrainingTargetMode.Mixed); Assert.Null(training.Combat.Capture().EquipmentSets);
        Assert.Empty(training.Report.Triggers); Assert.Equal(original, source.StateHash);
    }

    [Fact]
    public void ActualDodgeThenDirectHitAndEmberExpiryReplayWithoutChangingStateMidRun()
    {
        var state = Session(new() { AshrunnerSet = true }).Capture(); Incoming(state);
        var session = CombatSession.Restore(Content, state); var recorder = new CombatRecorder(session);
        List<CombatEvent> events = [.. recorder.Step(session, [new(CombatCommandKind.Dodge, Z: 1)])];
        Assert.True(session.View.EquipmentSets!.AshrunnerReadyTicks > 0);
        Assert.Equal(session.StateHash, CombatSession.Restore(Content, session.Capture()).StateHash);
        for (int i = 0; i < 6; i++) events.AddRange(recorder.Step(session, [new(CombatCommandKind.Move, Z: -1)]));
        events.AddRange(recorder.Step(session, [new(CombatCommandKind.Stop), new(CombatCommandKind.Cast, SkillId: "skill.shield_breaker", TargetId: session.View.Actors[1].Id)]));
        for (int i = 0; i < 12; i++) events.AddRange(recorder.Step(session, []));
        Assert.Equal(3, session.Capture().Areas.Count(a => a.SkillId == "effect.set_ashrunner_trail"));
        Assert.Equal(session.StateHash, CombatSession.Restore(Content, session.Capture()).StateHash);
        for (int i = 0; i < 100; i++) events.AddRange(recorder.Step(session, []));
        Assert.Single(events, e => e.Kind == "EquipmentSetReadied" && e.ContentId == EquipmentSets.Ashrunner);
        Assert.Single(events, e => e.Kind == "EquipmentSetTriggered" && e.ContentId == EquipmentSets.Ashrunner);
        Assert.Contains(events, e => e.Kind == "DamageApplied" && e.ContentId == "effect.set_ashrunner_trail" && e.Amount > 0);
        Assert.DoesNotContain(session.Capture().Areas, a => a.SkillId == "effect.set_ashrunner_trail");
        Assert.Null(session.Capture().EquipmentSets);
        Assert.True(CombatReplayRunner.Run(Content, recorder.Capture()).Success);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("owner")]
    [InlineData("source")]
    [InlineData("damage")]
    [InlineData("radius")]
    [InlineData("family")]
    [InlineData("duration")]
    [InlineData("unequipped")]
    public void ForgedGroundEffectsCannotChangeSetOwnershipGeometryDamageOrBudget(string reason)
    {
        var session = Runner(); Attack(session); var state = session.Capture();
        int index = state.Areas.FindIndex(a => a.SkillId == "effect.set_ashrunner_trail"); var area = state.Areas[index];
        state.Areas[index] = reason switch
        {
            "owner" => area with { OwnerId = state.Actors[1].Id },
            "source" => area with { SourceId = state.Actors[1].Id },
            "damage" => area with { Damage = 7 },
            "radius" => area with { Radius = 651 },
            "family" => area with { Family = DamageFamily.Void },
            "duration" => area with { ExpiresTick = state.Tick + 61 },
            _ => area
        };
        if (reason == "count") state.Areas.Add(area with { Id = state.NextObjectId++ });
        if (reason == "unequipped") state.ProgressionBuild = new();
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content, state));
    }

    [Fact]
    public void FullAreaBudgetConsumesThePreparedAttemptAndReportsZeroEmberPatches()
    {
        var state = Runner().Capture(); int enemy = state.Actors[1].Id;
        while (state.Areas.Count < CombatSession.MaxAreas)
            state.Areas.Add(new(state.NextObjectId++, enemy, enemy, state.Actors[0].Position, 100, "enemy.detonate", 0,
                DamageFamily.Fire, state.Tick + 100, state.Tick + 200, state.NextActionId++, 0));
        var session = CombatSession.Restore(Content, state);
        Assert.True(session.View.EquipmentSets!.AshrunnerReadyTicks > 0);
        var events = Attack(session);
        Assert.Equal(0, Assert.Single(events, e => e.Kind == "EquipmentSetTriggered" && e.ContentId == EquipmentSets.Ashrunner).Amount);
        Assert.Single(events, e => e.Kind == "EffectBudgetExceeded");
        Assert.Equal(CombatSession.MaxAreas, session.Capture().Areas.Count);
        Assert.DoesNotContain(session.Capture().Areas, a => a.SkillId == "effect.set_ashrunner_trail");
        Assert.Equal(0, session.View.EquipmentSets.AshrunnerReadyTicks);
        Assert.InRange(session.View.EquipmentSets.AshrunnerCooldownTicks, 70, 90);
        Assert.Equal(session.StateHash, CombatSession.Restore(Content, session.Capture()).StateHash);
    }
}
