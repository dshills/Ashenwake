using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class MidgameLegendaryTests
{
    private static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));
    private static string Content => Read("combat.json");
    private static CombatCommand Cast(string skill, int target = 0) => new(CombatCommandKind.Cast, SkillId: skill, TargetId: target);
    private static CombatProgressionBuild Build(string discipline = "Vanguard") => new(Discipline: discipline)
    { VirulentWake = true, RallyingChorus = true, CinderCycle = true };

    private static CombatSession Session(CombatProgressionBuild build)
    {
        var hub = CombatSession.CreateEncounter(Content, 42, "hub"); hub.ApplyProgressionBuild(build);
        var state = CombatSession.CreateEncounter(Content, 42, "encounter.ossuary", hub.Capture()).Capture();
        state.Fragments.Clear(); state.Equipment.Clear(); state.Actors[0].Position = new(-4500, 0);
        int index = 0;
        foreach (var enemy in state.Actors.Where(a => a.Faction == CombatFaction.Enemy).ToArray())
        {
            state.Actors[state.Actors.IndexOf(enemy)] = enemy with
            { Position = new(-2500 + index++ * 1500, 0), Health = 1000, MaxHealth = 1000, Resistance = 0, Armor = 0, RecoveryUntil = 2500, Hidden = false };
        }
        return CombatSession.Restore(Content, state);
    }

    private static List<CombatEvent> Advance(CombatSession session, int ticks)
    {
        List<CombatEvent> result = [];
        for (int i = 0; i < ticks; i++) result.AddRange(session.Step());
        return result;
    }

    private static CombatStatus Poison(CombatSnapshot state, string origin = "skill.venom_knife", int source = 1, int owner = 1, int depth = 0, int generation = 0) => new()
    {
        Id = "Poisoned",
        SourceId = source,
        OwnerId = owner,
        OriginSkill = origin,
        SourceGeneration = generation,
        ActionId = state.NextActionId++,
        NextTick = state.Tick,
        ExpiresTick = state.Tick + 90,
        Depth = depth
    };

    private static CombatSnapshot PoisonKill(bool equipped = true)
    {
        var state = Session(new(Discipline: "Veilwalker") { VirulentWake = equipped }).Capture();
        state.Actors[1].Health = 1; state.Actors[1].Statuses.Add(Poison(state));
        return state;
    }

    private static CombatSnapshot WithAllies(CombatProgressionBuild build, int count = 1)
    {
        var state = Session(build).Capture();
        for (int i = 0; i < count; i++) state.Actors.Add(new()
        {
            Id = state.NextActorId++,
            DefinitionId = "summon.ancestor",
            Faction = CombatFaction.Ally,
            Role = "Spirit",
            OwnerId = 1,
            Generation = 1,
            Health = 60,
            MaxHealth = 60,
            ExpiresTick = 240,
            Position = new(-4000 + 700 * i, -1200),
            RecoveryUntil = 200
        });
        return state;
    }

    [Theory]
    [InlineData("Veilwalker", "skill.venom_knife")]
    [InlineData("Warden", "skill.thorn_shot")]
    public void AuthoredPoisonSkillsMakeRotwakeARealNearbyDamageUpgrade(string discipline, string skill)
    {
        var prepared = Session(new(Discipline: discipline) { VirulentWake = true }).Capture();
        prepared.Actors[1].Health = 31; prepared.Actors[2].Position = new(-500, 800);
        var powered = CombatSession.Restore(Content, prepared);
        prepared.ProgressionBuild = prepared.ProgressionBuild with { VirulentWake = false };
        var ordinary = CombatSession.Restore(Content, prepared);
        var poweredEvents = powered.Step([Cast(skill, prepared.Actors[1].Id)]).Concat(Advance(powered, 88)).ToArray();
        var ordinaryEvents = ordinary.Step([Cast(skill, prepared.Actors[1].Id)]).Concat(Advance(ordinary, 88)).ToArray();
        Assert.Contains(poweredEvents, e => e.Kind == "EntityKilled" && e.ContentId == "Poisoned");
        Assert.Single(poweredEvents, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.RotwakePower);
        Assert.DoesNotContain(ordinaryEvents, e => e.ContentId == LegendaryEquipment.RotwakePower);
        Assert.True(powered.Capture().Actors[2].Health < ordinary.Capture().Actors[2].Health);
        Assert.Equal(ordinary.Capture().Rng, powered.Capture().Rng);
        Assert.Empty(powered.Capture().ConsumedCorpseIds);
    }

    [Fact]
    public void RotwakeSelectsAtMostThreeLivingNearbyTargetsInStableOrderAndDoesNotClaimACorpse()
    {
        var state = PoisonKill(); var template = state.Actors[2];
        state.Actors[2].Position = new(-1500, 0);
        while (state.Actors.Count < 7) state.Actors.Add(template with
        { Id = state.NextActorId++, Position = new(-2200, 600 * (state.Actors.Count - 3)), Statuses = [] });
        int index = 0;
        foreach (var candidate in state.Actors.Skip(2)) candidate.Position = new(-1500, -1500 + index++ * 600);
        var session = CombatSession.Restore(Content, state); var rng = state.Rng;
        var events = session.Step();
        int[] poisoned = session.Capture().Actors.Where(a => a.Health > 0 && a.Statuses.Any(s => s.OriginSkill == "effect.virulent_wake")).Select(a => a.Id).ToArray();
        Assert.Equal(state.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Id != state.Actors[1].Id).OrderBy(a => a.Id).Take(3).Select(a => a.Id), poisoned);
        Assert.All(session.Capture().Actors.Where(a => poisoned.Contains(a.Id)), a =>
        {
            var status = Assert.Single(a.Statuses); Assert.Equal(1, status.Stacks); Assert.Equal(1, status.Depth);
            Assert.Equal(1, status.OwnerId); Assert.Equal("effect.virulent_wake", status.OriginSkill);
        });
        Assert.Single(events, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.RotwakePower);
        Assert.Empty(session.Capture().ConsumedCorpseIds);
        Assert.Equal(90, session.Capture().Legendary!.VirulentReadyTick);
        var baseline = CombatSession.Restore(Content, state with { ProgressionBuild = state.ProgressionBuild with { VirulentWake = false } });
        baseline.Step(); Assert.Equal(baseline.Capture().Rng, session.Capture().Rng);
        Assert.NotEqual(rng.Loot, session.Capture().Rng.Loot); // The ordinary kill still owns its normal loot draws.
    }

    [Theory]
    [InlineData("range")]
    [InlineData("wall")]
    [InlineData("dead")]
    public void RotwakeSkipsIneligibleNeighborsWithoutStartingACooldown(string reason)
    {
        var state = PoisonKill();
        foreach (var enemy in state.Actors.Skip(2)) enemy.Position = new(9000, 8000);
        var candidate = state.Actors[2];
        if (reason == "wall") { state.Actors[1].Position = new(-1100, -3700); candidate.Position = new(1100, -3700); }
        else if (reason == "dead") { candidate.Position = new(-1500, 0); candidate.Health = 0; candidate.DeathProcessed = true; }
        var session = CombatSession.Restore(Content, state); var events = session.Step();
        Assert.DoesNotContain(events, e => e.ContentId == LegendaryEquipment.RotwakePower);
        Assert.Null(session.Capture().Legendary);
    }

    [Theory]
    [InlineData("Burning")]
    [InlineData("enemy-owner")]
    [InlineData("spread-origin")]
    [InlineData("chain-depth")]
    [InlineData("cooldown")]
    public void RotwakeDoesNotSpreadFromUnqualifiedKills(string reason)
    {
        var state = PoisonKill(); var status = state.Actors[1].Statuses[0];
        if (reason == "Burning") status.Id = "Burning";
        if (reason == "enemy-owner") { status.SourceId = state.Actors[2].Id; status.OwnerId = state.Actors[2].Id; }
        if (reason == "spread-origin") { status.OriginSkill = "effect.virulent_wake"; status.Depth = 1; }
        if (reason == "chain-depth") status.Depth = CombatSession.MaxChainDepth;
        if (reason == "cooldown") state.Legendary = new() { VirulentReadyTick = 45 };
        var session = CombatSession.Restore(Content, state); var events = session.Step();
        Assert.DoesNotContain(events, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.RotwakePower);
        Assert.DoesNotContain(session.Capture().Actors.Skip(2), a => a.Statuses.Any(s => s.OriginSkill == "effect.virulent_wake"));
    }

    [Fact]
    public void RotwakeHonorsStatusBudgetAndRefreshesExistingPoisonWithoutReplacingItsCredit()
    {
        var state = PoisonKill(); var candidate = state.Actors[2];
        candidate.Statuses.Add(Poison(state, "skill.thorn_shot", depth: 0)); candidate.Statuses[0].NextTick = 40;
        var session = CombatSession.Restore(Content, state); session.Step();
        var poison = Assert.Single(session.Capture().Actors[2].Statuses);
        Assert.Equal(2, poison.Stacks); Assert.Equal("skill.thorn_shot", poison.OriginSkill);
        Assert.Equal(state.Actors[2].Statuses[0].ActionId, poison.ActionId);

        state = PoisonKill(); candidate = state.Actors[2];
        for (int i = 0; i < 32; i++) candidate.Statuses.Add(new()
        { Id = "Marked", SourceId = 1, OwnerId = 1, ActionId = state.NextActionId++, ExpiresTick = 90, NextTick = 30 });
        var full = CombatSession.Restore(Content, state); full.Step();
        Assert.Equal(32, full.Capture().Actors[2].Statuses.Count);
        Assert.DoesNotContain(full.Capture().Actors[2].Statuses, s => s.Id == "Poisoned");
    }

    [Fact]
    public void RotwakePreservesSecondGenerationOwnershipWithoutCreatingAnotherGeneration()
    {
        var state = PoisonKill(); var source = new CombatActor
        {
            Id = state.NextActorId++,
            DefinitionId = "summon.ancestor",
            Faction = CombatFaction.Ally,
            Role = "Spirit",
            OwnerId = 1,
            Generation = 2,
            Health = 60,
            MaxHealth = 60,
            ExpiresTick = 240,
            Position = new(-4000, -1200),
            RecoveryUntil = 200
        };
        state.Actors.Add(source);
        state.Actors[1].Statuses[0].SourceId = source.Id; state.Actors[1].Statuses[0].SourceGeneration = 2;
        var session = CombatSession.Restore(Content, state); session.Step();
        var spread = Assert.Single(session.Capture().Actors[2].Statuses);
        Assert.Equal(source.Id, spread.SourceId); Assert.Equal(1, spread.OwnerId); Assert.Equal(2, spread.SourceGeneration);
        Assert.Equal(2, session.Capture().Actors.Single(a => a.Id == source.Id).Generation);
        Assert.Equal(session.StateHash, CombatSession.Restore(Content, session.Capture()).StateHash);
    }

    [Fact]
    public void ChorusAddsThreeFlatNoncriticalHitsWithoutCreatingSummonsOrConsumingCriticalRandomness()
    {
        var state = WithAllies(new(FlatDamage: 300, CriticalBasisPoints: 7500) { RallyingChorus = true }, 5);
        var powered = CombatSession.Restore(Content, state);
        state.ProgressionBuild = state.ProgressionBuild with { RallyingChorus = false };
        var ordinary = CombatSession.Restore(Content, state);
        var events = powered.Step([Cast("skill.cleave", state.Actors[1].Id)]).Concat(Advance(powered, 7)).ToArray();
        ordinary.Step([Cast("skill.cleave", state.Actors[1].Id)]); Advance(ordinary, 7);
        var hits = events.Where(e => e.Kind == "DamageApplied" && e.ContentId == "effect.rallying_chorus").ToArray();
        Assert.Equal(3, hits.Length); Assert.All(hits, e => Assert.Equal(8, e.Amount));
        Assert.DoesNotContain(events, e => e.Kind == "CriticalHit" && e.ContentId == "effect.rallying_chorus");
        Assert.DoesNotContain(events, e => e.Kind == "SummonSpawned");
        Assert.Equal(5, powered.Capture().Actors.Count(a => a.Faction == CombatFaction.Ally));
        Assert.Equal(24, ordinary.Capture().Actors[1].Health - powered.Capture().Actors[1].Health);
        Assert.Equal(ordinary.Capture().Rng, powered.Capture().Rng);
        Assert.Equal(ordinary.View.Resource, powered.View.Resource);
        Assert.Single(events, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.MourningPower);
    }

    [Theory]
    [InlineData("no-summons")]
    [InlineData("far")]
    [InlineData("wall")]
    [InlineData("dead-target")]
    [InlineData("expired-summon")]
    [InlineData("stunned-summon")]
    public void ChorusRequiresALivingTargetAndAnAvailableNearbySummon(string reason)
    {
        var state = WithAllies(new() { RallyingChorus = true }); var ally = state.Actors.Last();
        if (reason == "no-summons") state.Actors.Remove(ally);
        if (reason == "far") ally.Position = new(9000, 8000);
        if (reason == "wall") { ally.Position = new(1100, -3700); state.Actors[1].Position = new(-1100, -3700); state.Actors[0].Position = new(-3100, -3700); }
        if (reason == "dead-target") state.Actors[1].Health = 1;
        if (reason == "expired-summon") ally.ExpiresTick = 0;
        if (reason == "stunned-summon") ally.Statuses.Add(new()
        { Id = "Frozen", SourceId = 1, OwnerId = 1, ActionId = state.NextActionId++, ExpiresTick = 30, NextTick = 15 });
        var session = CombatSession.Restore(Content, state);
        var events = session.Step([Cast("skill.cleave", state.Actors[1].Id)]).Concat(Advance(session, 7)).ToArray();
        Assert.DoesNotContain(events, e => e.ContentId is "effect.rallying_chorus" or LegendaryEquipment.MourningPower);
        Assert.Null(session.Capture().Legendary);
    }

    [Fact]
    public void ForgedSummonOwnershipIsRejectedBeforeChorusCanUseIt()
    {
        var state = WithAllies(new() { RallyingChorus = true });
        state.Actors[^1] = state.Actors[^1] with { OwnerId = state.Actors[1].Id };
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content, state));
    }

    [Fact]
    public void AnActualCompanionMakesChorusUsefulAndItsOwnAttacksDoNotRetriggerIt()
    {
        var session = Session(new(Discipline: "Warden") { RallyingChorus = true });
        var state = session.Capture(); state.Momentum = 40;
        session = CombatSession.Restore(Content, state);
        var events = session.Step([Cast("skill.feral_companion")]).Concat(Advance(session, 16)).ToList();
        Assert.Single(events, e => e.Kind == "SummonSpawned");
        events.AddRange(session.Step([Cast("skill.thorn_shot", state.Actors[1].Id)])); events.AddRange(Advance(session, 140));
        Assert.Single(events, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.MourningPower);
        Assert.Contains(events, e => e.Kind == "DamageApplied" && e.ContentId == "summon.companion_bite");
        Assert.Single(events, e => e.Kind == "DamageApplied" && e.ContentId == "effect.rallying_chorus");
    }

    [Theory]
    [InlineData("Vanguard", "skill.shield_breaker", "skill.cleave")]
    [InlineData("Veilwalker", "skill.shadow_execution", "skill.venom_knife")]
    [InlineData("Warden", "skill.barkskin", "skill.thorn_shot")]
    public void FurnaceRewardsASpenderThenARealGeneratorExactlyOnce(string discipline, string spender, string generator)
    {
        var state = Session(new(Discipline: discipline) { CinderCycle = true }).Capture(); state.Momentum = discipline == "Veilwalker" ? 80 : 50;
        var powered = CombatSession.Restore(Content, state);
        state.ProgressionBuild = state.ProgressionBuild with { CinderCycle = false };
        var ordinary = CombatSession.Restore(Content, state);
        List<CombatEvent> events = [];
        foreach (var session in new[] { powered, ordinary })
        {
            events.AddRange(session.Step([Cast(spender, state.Actors[1].Id)])); events.AddRange(Advance(session, 25));
        }
        Assert.Equal(180, powered.Capture().Legendary!.CinderUntil);
        foreach (var session in new[] { powered, ordinary })
        {
            events.AddRange(session.Step([Cast(generator, state.Actors[1].Id)])); events.AddRange(Advance(session, 20));
        }
        Assert.Equal(12, powered.View.Resource - ordinary.View.Resource);
        Assert.Single(events, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.FurnacePower);
        Assert.True(powered.Capture().Legendary is null || powered.Capture().Legendary!.CinderUntil == 0);
        foreach (var session in new[] { powered, ordinary })
        {
            events.AddRange(session.Step([Cast(generator, state.Actors[1].Id)])); events.AddRange(Advance(session, 20));
        }
        Assert.Equal(12, powered.View.Resource - ordinary.View.Resource);
        Assert.Single(events, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.FurnacePower);
        Assert.Equal(ordinary.Capture().Rng, powered.Capture().Rng);
    }

    [Fact]
    public void ArcanistConsumesFurnaceOnlyWhenVentResolvesAndCoolsTwelveExtra()
    {
        var state = Session(new(Discipline: "Arcanist") { CinderCycle = true }).Capture(); state.Momentum = 60;
        var powered = CombatSession.Restore(Content, state);
        state.ProgressionBuild = state.ProgressionBuild with { CinderCycle = false };
        var ordinary = CombatSession.Restore(Content, state);
        foreach (var session in new[] { powered, ordinary })
        { session.Step([Cast("skill.frost_nova")]); Advance(session, 18); }
        Assert.Equal(180, powered.Capture().Legendary!.CinderUntil);
        powered.Step([Cast("skill.vent")]); ordinary.Step([Cast("skill.vent")]);
        Assert.True(powered.Capture().Legendary!.CinderUntil > powered.Tick);
        var events = Advance(powered, 3); Advance(ordinary, 3);
        Assert.Equal(12, ordinary.View.Resource - powered.View.Resource);
        Assert.Single(events, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.FurnacePower);
        Assert.Null(powered.Capture().Legendary);
    }

    [Fact]
    public void GravecallerCanSpendThenHarvestAnotherCorpseForFurnaceWithoutDoubleClaiming()
    {
        var state = Session(new(Discipline: "Gravecaller") { CinderCycle = true }).Capture(); state.Momentum = 40;
        foreach (var corpse in state.Actors.Skip(1).Take(2)) { corpse.Health = 0; corpse.DeathProcessed = true; corpse.Position = new(-3500, corpse.Id * 100); }
        var powered = CombatSession.Restore(Content, state);
        state.ProgressionBuild = state.ProgressionBuild with { CinderCycle = false };
        var ordinary = CombatSession.Restore(Content, state);
        foreach (var session in new[] { powered, ordinary }) { session.Step([Cast("skill.raise_ancestor")]); Advance(session, 20); }
        var corpseId = powered.Capture().Actors.First(a => a.Health == 0 && !powered.Capture().ConsumedCorpseIds.Contains(a.Id)).Id;
        var command = new CombatCommand(CombatCommandKind.ConsumeCorpse, TargetId: corpseId);
        var events = powered.Step([command]); ordinary.Step([command]);
        Assert.Equal(12, powered.View.Resource - ordinary.View.Resource);
        Assert.Single(events, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.FurnacePower);
        Assert.Equal(2, powered.Capture().ConsumedCorpseIds.Count);
        int resource = powered.View.Resource; var repeated = powered.Step([command]);
        Assert.Contains(repeated, e => e.Kind == "CommandRejected"); Assert.Equal(resource, powered.View.Resource);
        Assert.DoesNotContain(repeated, e => e.ContentId == LegendaryEquipment.FurnacePower);
    }

    [Fact]
    public void FurnaceCapsAtOneHundredAndReportsOnlyTheActualResourceRestored()
    {
        var state = Session(new() { CinderCycle = true }).Capture(); state.Momentum = 90;
        state.Legendary = new() { CinderUntil = 180 };
        var session = CombatSession.Restore(Content, state);
        var events = session.Step([Cast("skill.cleave", state.Actors[1].Id)]).Concat(Advance(session, 6)).ToArray();
        Assert.Equal(100, session.View.Resource); Assert.Null(session.Capture().Legendary);
        Assert.DoesNotContain(events, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.FurnacePower && e.Amount > 0);
    }

    [Theory]
    [InlineData("unaffordable")]
    [InlineData("bad-target")]
    [InlineData("busy")]
    [InlineData("cheap")]
    public void FurnaceDoesNotArmFromRejectedOrCheapSkills(string reason)
    {
        var state = Session(new() { CinderCycle = true }).Capture(); state.Momentum = reason == "unaffordable" ? 0 : 50;
        if (reason == "busy") state.Actors[0].RecoveryUntil = 50;
        var session = CombatSession.Restore(Content, state);
        session.Step([Cast(reason == "cheap" ? "skill.seismic_wave" : "skill.shield_breaker", reason == "bad-target" ? 0 : state.Actors[1].Id)]);
        Assert.Null(session.Capture().Legendary);
    }

    [Fact]
    public void BufferedSpendersArmFurnaceOnlyWhenTheyActuallyStart()
    {
        var state = Session(new() { CinderCycle = true }).Capture(); state.Momentum = 50; state.Actors[0].RecoveryUntil = 4;
        var session = CombatSession.Restore(Content, state);
        Assert.Contains(session.Step([Cast("skill.shield_breaker", state.Actors[1].Id)]), e => e.Kind == "InputBuffered");
        Assert.Null(session.Capture().Legendary); Assert.Equal(50, session.View.Resource);
        Advance(session, 4);
        Assert.Equal(184, session.Capture().Legendary!.CinderUntil); Assert.Equal(30, session.View.Resource);
    }

    [Theory]
    [InlineData("Arcanist")]
    [InlineData("Gravecaller")]
    public void InterruptedVentAndRejectedHarvestPreserveTheFurnaceToken(string discipline)
    {
        var state = Session(new(Discipline: discipline) { CinderCycle = true }).Capture(); state.Momentum = 60;
        state.Legendary = new() { CinderUntil = 180 };
        var session = CombatSession.Restore(Content, state);
        if (discipline == "Arcanist")
        {
            session.Step([Cast("skill.vent")]); session.Step([new(CombatCommandKind.Dodge, Z: 1)]); Advance(session, 5);
        }
        else Assert.Contains(session.Step([new(CombatCommandKind.ConsumeCorpse, TargetId: state.Actors[1].Id)]), e => e.Kind == "CommandRejected");
        Assert.Equal(180, session.Capture().Legendary!.CinderUntil);
        Assert.True(session.View.Resource >= 57);
    }

    [Fact]
    public void FurnaceRefreshesWithoutStackingAndDoesNotConsumeOnAMissedGenerator()
    {
        var state = Session(new() { CinderCycle = true }).Capture(); state.Momentum = 100;
        var session = CombatSession.Restore(Content, state); session.Step([Cast("skill.shield_breaker", state.Actors[1].Id)]); Advance(session, 45);
        session.Step([Cast("skill.shield_breaker", state.Actors[1].Id)]);
        Assert.Equal(226, session.Capture().Legendary!.CinderUntil);
        Advance(session, 20); session.Step([Cast("skill.cleave", state.Actors[1].Id)]);
        state = session.Capture(); state.Actors[1].Position = new(8500, 8000);
        session = CombatSession.Restore(Content, state); Advance(session, 6);
        Assert.Equal(226, session.Capture().Legendary!.CinderUntil);
        Advance(session, 230); Assert.Null(session.Capture().Legendary);
    }

    [Theory]
    [InlineData("rotwake")]
    [InlineData("chorus")]
    [InlineData("furnace")]
    public void ActiveMidgameEffectsRoundTripThroughSaveAndDeterministicReplay(string power)
    {
        var state = power == "rotwake" ? PoisonKill() : power == "chorus" ? WithAllies(Build()) : Session(Build()).Capture();
        state.Momentum = 60; var session = CombatSession.Restore(Content, state);
        if (power == "rotwake") session.Step();
        else if (power == "chorus") { session.Step([Cast("skill.cleave", state.Actors[1].Id)]); Advance(session, 5); }
        else session.Step([Cast("skill.shield_breaker", state.Actors[1].Id)]);
        Assert.NotNull(session.Capture().Legendary);
        string save = JsonData.Write(new CombatSave(1, session.StateHash, session.Capture()));
        var restored = CombatSession.Restore(Content, CombatSaveStore.Read(save, Content));
        Assert.Equal(session.StateHash, restored.StateHash);
        var recorder = new CombatRecorder(session);
        for (int i = 0; i < 185; i++)
        {
            Assert.Equal(JsonData.Hash(restored.Step()), JsonData.Hash(recorder.Step(session)));
            Assert.Equal(session.StateHash, restored.StateHash);
        }
        Assert.Null(session.Capture().Legendary);
        Assert.True(CombatReplayRunner.Run(Content, JsonData.Read<CombatReplay>(JsonData.Write(recorder.Capture()))).Success);
    }

    [Theory]
    [InlineData("unequip")]
    [InlineData("death")]
    [InlineData("encounter")]
    public void MidgamePreparedStateClearsWhenPowersAreRemovedPlayerDiesOrRoomChanges(string reason)
    {
        var state = Session(Build()).Capture(); state.Legendary = new() { VirulentReadyTick = 90, ChorusReadyTick = 90, CinderUntil = 180 };
        var session = CombatSession.Restore(Content, state);
        if (reason == "unequip") session.ApplyProgressionBuild(new());
        else if (reason == "encounter") session = CombatSession.CreateEncounter(Content, 42, "hub", session.Capture());
        else
        {
            state.Actors[0].Health = 1; state.Actors[0].InvulnerableUntil = 0;
            state.Actors[1].Position = new(-3900, 0);
            state.Actors[1].Pending = new("enemy.strike", 1, state.Actors[0].Position, 0, state.NextActionId++);
            session = CombatSession.Restore(Content, state); session.Step(); Assert.Equal(0, session.View.Actors[0].Health);
        }
        Assert.Null(session.Capture().Legendary);
    }

    [Theory]
    [InlineData("unequip")]
    [InlineData("death")]
    [InlineData("encounter")]
    public void ExistingRotwakePoisonClearsWithItsEquipmentOrOwner(string reason)
    {
        var session = CombatSession.Restore(Content, PoisonKill()); session.Step();
        Assert.Contains(session.Capture().Actors.SelectMany(a => a.Statuses), s => s.OriginSkill == "effect.virulent_wake");
        if (reason == "unequip") session.ApplyProgressionBuild(new(Discipline: "Veilwalker"));
        else if (reason == "encounter") session = CombatSession.CreateEncounter(Content, 42, "hub", session.Capture());
        else
        {
            var state = session.Capture(); state.Actors[0].Health = 1; state.Actors[0].InvulnerableUntil = state.Tick;
            state.Actors[2].Position = new(-3900, 0);
            state.Actors[2].Pending = new("enemy.strike", 1, state.Actors[0].Position, state.Tick, state.NextActionId++);
            session = CombatSession.Restore(Content, state); session.Step(); Assert.Equal(0, session.View.Actors[0].Health);
        }
        Assert.DoesNotContain(session.Capture().Actors.SelectMany(a => a.Statuses), s => s.OriginSkill == "effect.virulent_wake");
        Assert.Equal(session.StateHash, CombatSession.Restore(Content, session.Capture()).StateHash);
    }

    [Theory]
    [InlineData("wrong-owner")]
    [InlineData("wrong-status")]
    [InlineData("recursive-depth")]
    [InlineData("future-expiry")]
    [InlineData("unequipped")]
    public void ForgedRotwakeStatusesAreRejected(string reason)
    {
        var session = CombatSession.Restore(Content, PoisonKill()); session.Step();
        var state = session.Capture(); var poison = state.Actors[2].Statuses.Single(s => s.OriginSkill == "effect.virulent_wake");
        if (reason == "wrong-owner") poison.OwnerId = state.Actors[2].Id;
        if (reason == "wrong-status") poison.Id = "Burning";
        if (reason == "recursive-depth") poison.Depth = 0;
        if (reason == "future-expiry") poison.ExpiresTick = state.Tick + 91;
        if (reason == "unequipped") state.ProgressionBuild = state.ProgressionBuild with { VirulentWake = false };
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content, state));
    }

    [Theory]
    [InlineData("rotwake-negative")]
    [InlineData("rotwake-future")]
    [InlineData("chorus-negative")]
    [InlineData("chorus-future")]
    [InlineData("furnace-negative")]
    [InlineData("furnace-future")]
    [InlineData("rotwake-unequipped")]
    [InlineData("chorus-unequipped")]
    [InlineData("furnace-unequipped")]
    [InlineData("dead")]
    public void InvalidMidgameTimersAndPowerOwnershipAreRejected(string reason)
    {
        var state = Session(Build()).Capture(); state.Legendary = new();
        switch (reason)
        {
            case "rotwake-negative": state.Legendary.VirulentReadyTick = -1; break;
            case "rotwake-future": state.Legendary.VirulentReadyTick = 91; break;
            case "chorus-negative": state.Legendary.ChorusReadyTick = -1; break;
            case "chorus-future": state.Legendary.ChorusReadyTick = 91; break;
            case "furnace-negative": state.Legendary.CinderUntil = -1; break;
            case "furnace-future": state.Legendary.CinderUntil = 181; break;
            case "rotwake-unequipped": state.ProgressionBuild = state.ProgressionBuild with { VirulentWake = false }; state.Legendary.VirulentReadyTick = 90; break;
            case "chorus-unequipped": state.ProgressionBuild = state.ProgressionBuild with { RallyingChorus = false }; state.Legendary.ChorusReadyTick = 90; break;
            case "furnace-unequipped": state.ProgressionBuild = state.ProgressionBuild with { CinderCycle = false }; state.Legendary.CinderUntil = 180; break;
            default: state.Actors[0].Health = 0; state.Actors[0].DeathProcessed = true; state.Legendary.CinderUntil = 180; break;
        }
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content, state));
    }

    [Fact]
    public void NewDefaultFieldsDoNotChangeHistoricalSerializedShape()
    {
        string build = JsonData.Write(new CombatProgressionBuild()); string state = JsonData.Write(new LegendaryCombatState { OathCharge = 5, OathUntil = 80 });
        Assert.DoesNotContain("virulentWake", build); Assert.DoesNotContain("rallyingChorus", build); Assert.DoesNotContain("cinderCycle", build);
        Assert.DoesNotContain("virulentReadyTick", state); Assert.DoesNotContain("chorusReadyTick", state); Assert.DoesNotContain("cinderUntil", state);
    }

    [Theory]
    [InlineData(LegendaryEquipment.Rotwake, LegendaryEquipment.RotwakePower, "item.starter_ring1")]
    [InlineData(LegendaryEquipment.Mourning, LegendaryEquipment.MourningPower, "item.starter_shoulders")]
    [InlineData(LegendaryEquipment.Furnace, LegendaryEquipment.FurnacePower, "item.starter_belt")]
    public void MidgamePowersCanBeExtractedAndEngravedWithSlotAndConfirmationRules(string item, string power, string receiver)
    {
        var content = ProductionContent.Resolve(Content, ProgressionContent.Parse(Read("progression.json")));
        var session = ProgressionSession.Create(content);
        foreach (string id in new[] { "mara", "torren", "cael", "oris", "kesh", "haven" }) Assert.True(session.CompleteObjective("quest." + id, "objective." + id).Success);
        Assert.True(session.EarnExperience("materials", 0, 500).Success);
        Assert.True(session.GrantItem("legendary", item, ItemRarity.Legendary).Success);
        Assert.True(session.GrantItem("receiver", receiver, ItemRarity.Rare).Success);
        Assert.True(session.GrantItem("wrong", "item.starter_head", ItemRarity.Rare).Success);
        string before = session.StateHash;
        Assert.False(session.Craft(new("extract", CraftingService.Extraction, 1)).Success); Assert.Equal(before, session.StateHash);
        Assert.True(session.Craft(new("extract", CraftingService.Extraction, 1, ConfirmPermanent: true)).Success);
        Assert.Contains(power, session.Capture().Character.PropertyLibrary);
        Assert.DoesNotContain(session.Capture().Character.Items, i => i.Id == 1);
        before = session.StateHash;
        Assert.False(session.Craft(new("wrong", CraftingService.Engraving, 3, PropertyId: power)).Success); Assert.Equal(before, session.StateHash);
        Assert.True(session.Craft(new("engrave", CraftingService.Engraving, 2, PropertyId: power)).Success);
        Assert.Equal(power, session.Capture().Character.Items.Single(i => i.Id == 2).Engraving);
        Assert.Equal(session.StateHash, ProgressionSession.Restore(content, session.Capture()).StateHash);
    }

    [Theory]
    [InlineData(LegendaryEquipment.Rotwake, LegendaryEquipment.RotwakePower, "item.starter_amulet")]
    [InlineData(LegendaryEquipment.Mourning, LegendaryEquipment.MourningPower, "item.starter_chest")]
    [InlineData(LegendaryEquipment.Furnace, LegendaryEquipment.FurnacePower, "item.starter_boots")]
    public void InnateAndEngravedMidgameCopiesAreOnePowerWithIndependentEquipmentLifetimes(string item, string power, string receiver)
    {
        var adventure = AdventureContent.Parse(Read("adventure.json")); var policy = ProgressionContent.Parse(Read("progression.json"));
        var state = ProductionSession.Create(Content, adventure, policy).Capture(); var combat = state.Expedition.Combat;
        combat.Actors[0].Position = new(2000, -2000);
        foreach (string id in new[] { item, receiver })
        {
            var definition = CombatContent.Parse(Content).Items.Single(i => i.Id == id); long objectId = combat.NextObjectId++;
            combat.Loot.Add(new(objectId, combat.Actors[0].Position, new(objectId, id, definition.Name, definition.Slot,
                "Legendary", definition.Damage, definition.Armor, definition.CriticalBasisPoints)));
        }
        var session = ProductionSession.Restore(Content, adventure, policy, state);
        foreach (var loot in session.Combat.View.Loot.ToArray())
            Assert.Contains(session.Step(new CombatCommand(CombatCommandKind.Pickup, ItemId: loot.Id)), e => e.Kind == "LootPickedUp");
        state = session.Capture(); var character = state.Progression.Character;
        var innate = character.Items.Single(i => i.DefinitionId == item); var engraved = character.Items.Last(i => i.DefinitionId == receiver);
        engraved.Engraving = power; character.PropertyLibrary.Add(power);
        session = ProductionSession.Restore(Content, adventure, policy, state);
        var innateSlot = Enum.Parse<EquipmentSlot>(CombatContent.Parse(Content).Items.Single(i => i.Id == item).Slot);
        var engravedSlot = Enum.Parse<EquipmentSlot>(CombatContent.Parse(Content).Items.Single(i => i.Id == receiver).Slot);
        bool Active() => power switch
        {
            LegendaryEquipment.RotwakePower => session.Combat.ProgressionBuild.VirulentWake,
            LegendaryEquipment.MourningPower => session.Combat.ProgressionBuild.RallyingChorus,
            _ => session.Combat.ProgressionBuild.CinderCycle
        };
        Assert.False(Active()); Assert.True(session.Equip(innate.Id, innateSlot).Success); Assert.True(Active());
        var once = session.Combat.ProgressionBuild;
        Assert.True(session.Equip(engraved.Id, engravedSlot).Success);
        var twice = session.Combat.ProgressionBuild;
        Assert.Equal((once.VirulentWake, once.RallyingChorus, once.CinderCycle), (twice.VirulentWake, twice.RallyingChorus, twice.CinderCycle));
        Assert.True(session.Unequip(innateSlot).Success); Assert.True(Active());
        Assert.True(session.Unequip(engravedSlot).Success); Assert.False(Active());
        Assert.Equal(session.StateHash, ProductionSession.Restore(Content, adventure, policy, session.Capture()).StateHash);
    }

    [Theory]
    [InlineData("campaign.plague_village", LegendaryEquipment.Rotwake)]
    [InlineData("campaign.extraction_floor", LegendaryEquipment.Mourning)]
    [InlineData("campaign.furnace_spindle", LegendaryEquipment.Furnace)]
    public void NewCampaignRewardsPreserveLootDrawCountsAndMissingOldCatalogItemsFallBackNormally(string encounter, string item)
    {
        CombatSession Clear(bool previous)
        {
            var content = CampaignCombatContent.Parse(previous ? Read("fixtures/combat-opening-depth.json") : Content, Read("campaign-combat.json"));
            var state = content.CreateEncounter(encounter).Capture();
            state.Campaign!.BossPhase = 2;
            foreach (var enemy in state.Actors.Where(a => a.Faction == CombatFaction.Enemy))
            {
                enemy.Health = 1; enemy.Barrier = 0; enemy.Statuses.Add(new()
                { Id = "Burning", SourceId = 1, OwnerId = 1, ActionId = state.NextActionId++, NextTick = 0, ExpiresTick = 90 });
            }
            var session = CombatSession.Restore(content.CombatJson, state);
            // Furnace's authored Martyr can ward its surviving Heat Tender on death.
            // Let the same ordinary DOT ticks finish that shield in both catalogs.
            for (int tick = 0; tick < 90 && session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0); tick++) session.Step();
            return session;
        }
        var previous = Clear(true); var current = Clear(false);
        Assert.DoesNotContain(current.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        Assert.Single(current.View.Loot, l => l.Item.DefinitionId == item);
        Assert.DoesNotContain(previous.View.Loot, l => l.Item.DefinitionId == item);
        Assert.Equal(previous.Capture().Rng, current.Capture().Rng);
        var additionalSets = LegendaryCollectionCatalog.Entries.Where(e => EquipmentSets.IsItem(e.ItemId) && e.CampaignEncounterId == encounter).Select(e => e.ItemId).ToArray();
        Assert.Equal(additionalSets, current.View.Loot.Where(l => EquipmentSets.IsItem(l.Item.DefinitionId)).Select(l => l.Item.DefinitionId));
        Assert.Equal(previous.Capture().NextObjectId + additionalSets.Length, current.Capture().NextObjectId);
        Assert.Equal(previous.View.Loot.Count + additionalSets.Length, current.View.Loot.Count);
    }

    [Theory]
    [InlineData("Vanguard")]
    [InlineData("Veilwalker")]
    [InlineData("Arcanist")]
    [InlineData("Gravecaller")]
    [InlineData("Warden")]
    public void EveryDisciplineCanEarnAllThreeThroughNormalCampaignCommandsAndEquipAtTorren(string discipline)
    {
        string combat = CampaignCombatContent.Parse(Content, Read("campaign-combat.json")).CombatJson;
        var adventure = AdventureContent.Parse(Read("adventure.json")); var progression = ProgressionContent.Parse(Read("progression.json"));
        var campaign = CampaignContent.Parse(Read("campaign.json"));
        var session = CampaignRuntimeSession.Create(combat, adventure, progression, campaign, discipline: discipline);
        string[] ids = [LegendaryEquipment.Rotwake, LegendaryEquipment.Mourning, LegendaryEquipment.Furnace];
        bool Earned() => ids.All(id => session.Capture().Production.Progression.Character.Items.Any(i => i.DefinitionId == id));
        var policy = new CampaignBalancePolicy(managedBuild: true);
        for (int i = 0; i < 24000 && !Earned(); i++)
        {
            var result = session.Execute(policy.Next(session)); Assert.True(result.Success, result.Reason);
            Assert.True(session.Combat.View.Actors[0].Health > 0, $"{discipline} died in {session.ActiveEncounterId}");
        }
        Assert.True(Earned(), $"{discipline} did not earn the three regional rewards");
        Assert.Contains("campaign.plague_village", session.Capture().Campaign.CompletedEncounters);
        Assert.Contains("campaign.extraction_floor", session.Capture().Campaign.CompletedEncounters);
        Assert.Contains("campaign.furnace_spindle", session.Capture().Campaign.CompletedEncounters);
        Assert.True(session.ReturnToHub().Success);
        foreach (string id in ids)
        {
            var earned = Assert.Single(session.Capture().Production.Progression.Character.Items, i => i.DefinitionId == id);
            Assert.Equal(ItemRarity.Legendary, earned.Rarity);
            var slot = Enum.Parse<EquipmentSlot>(CombatContent.Parse(Content).Items.Single(i => i.Id == id).Slot);
            for (int i = 0; i < 200 && session.Production.ProgressionView.Equipment.GetValueOrDefault(slot) != earned.Id; i++)
                Assert.True(session.Execute(CampaignRuntimeSmoke.AtInteraction(session, "service.torren", new(CampaignRuntimeAction.Production,
                    Production: new(ProductionAction.Equip, ItemId: earned.Id, Slot: slot)))).Success);
            Assert.Equal(earned.Id, session.Production.ProgressionView.Equipment[slot]);
        }
        Assert.True(session.Combat.ProgressionBuild.VirulentWake); Assert.True(session.Combat.ProgressionBuild.RallyingChorus); Assert.True(session.Combat.ProgressionBuild.CinderCycle);
        Assert.Equal(session.StateHash, CampaignRuntimeSession.Restore(combat, adventure, progression, campaign, session.Capture()).StateHash);
        var replay = CampaignRuntimeReplayRunner.Run(combat, adventure, progression, campaign, session.CaptureReplay());
        Assert.True(replay.Success); Assert.Equal(session.StateHash, replay.FinalHash);
    }

    [Theory]
    [InlineData("act.verdant_maw", 0, LegendaryEquipment.Rotwake)]
    [InlineData("act.verdant_maw", 1, LegendaryEquipment.Mourning)]
    [InlineData("act.verdant_maw", -1, LegendaryEquipment.Widow)]
    [InlineData("act.cinder_reach", -1, LegendaryEquipment.Furnace)]
    public void RepeatableFracturesPlaceEachNamedRewardInItsPublishedRoom(string region, int roomIndex, string item)
    {
        var content = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Content, Read("campaign-combat.json")).CombatJson,
            Read("endgame-combat.json"), EndgameContent.Parse(Read("endgame.json")));
        var manifest = content.CreateFractureManifest(new(1, 42, region, 1, ["fracture.burning_haste"], "Vael", "Materials"), 1);
        if (roomIndex == -1) roomIndex = manifest.Rooms.Length - 1;
        var hub = CombatSession.CreateEncounter(content.CombatJson, 42, "hub");
        var state = content.CreateEncounter(manifest, roomIndex, 0, hub.Capture()).Capture();
        if (manifest.Rooms[roomIndex].Boss) state.Campaign!.BossPhase = 2;
        foreach (var enemy in state.Actors.Where(a => a.Faction == CombatFaction.Enemy))
        {
            enemy.Health = 1; enemy.Statuses.Add(new()
            { Id = "Burning", SourceId = 1, OwnerId = 1, ActionId = state.NextActionId++, NextTick = 0, ExpiresTick = 90 });
        }
        var session = CombatSession.Restore(content.CombatJson, state); var events = session.Step();
        Assert.DoesNotContain(session.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        var rewardEvent = Assert.Single(events, e => e.Kind == "LootDropped" && e.ContentId == item);
        Assert.True(events.Count(e => e.Kind == "EntityKilled") > 1);
        Assert.Equal(events.Last(e => e.Kind == "EntityKilled").TargetId, rewardEvent.TargetId);
        Assert.Equal("Legendary", Assert.Single(session.View.Loot, l => l.Item.DefinitionId == item).Item.Rarity);
        Assert.Single(session.View.Loot, l => LegendaryEquipment.IsItem(l.Item.DefinitionId) && !EquipmentSets.IsItem(l.Item.DefinitionId));
        Advance(session, 2); Assert.Single(session.View.Loot, l => l.Item.DefinitionId == item);
    }
}
