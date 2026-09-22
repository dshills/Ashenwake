using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace Ashenwake.Tests;

public sealed class MidgameCombatDepthTests(ITestOutputHelper output)
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static CampaignCombatContent Live() => CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json"));
    private static string SupportId(bool forge) => forge ? "campaign.forge_bellows" : "campaign.spore_mend";
    private static string SourceId(bool forge) => forge ? "enemy.heat_tender" : "enemy.bloom_carrier";

    // Shipping definitions and public commands in isolated, valid geometry: no test-only
    // combat hooks or modified enemy statistics. Actor recovery isolates support effects.
    private static (CampaignCombatContent Content, CombatSnapshot State) Fixture(bool forge, bool wall = false, string ally = "enemy.forge_sentinel", string? overlay = null)
    {
        var data = JsonData.Read<CampaignCombatDefinition>(overlay ?? Read("campaign-combat.json"));
        var encounter = data.Encounters.Single(e => e.Id == "campaign.monastery") with
        {
            Rule = "Ambush",
            Room = new(12000, 10000, new(-1800, 0), new(0, 0), wall ? [new(500, 500, 1300, 2200)] : []),
            Spawns = [new(SourceId(forge), new(0, 0), []), new(ally, new(1800, 1400), []),
                new("enemy.forge_sentinel", new(3600, 0), []), new("enemy.forge_sentinel", new(0, -1500), [])]
        };
        data = data with { Encounters = data.Encounters.Select(e => e.Id == encounter.Id ? encounter : e).ToArray() };
        var content = CampaignCombatContent.Parse(Read("combat.json"), JsonData.Write(data));
        var state = content.CreateEncounter(encounter.Id).Capture();
        state.Fragments.Clear(); state.Equipment.Clear(); state.Campaign!.NextHazardTick = 150;
        state.Actors[0].InvulnerableUntil = 1000; state.Momentum = 100;
        foreach (var actor in state.Actors.Skip(2)) { actor.RecoveryUntil = 1000; actor.Health -= 30; }
        return (content, state);
    }

    private static List<CombatEvent> Advance(CombatSession session, int ticks)
    {
        List<CombatEvent> events = [];
        for (int i = 0; i < ticks; i++) events.AddRange(session.Step());
        return events;
    }

    private static CombatHazardView Start(CombatSession session, bool forge)
    {
        var events = new List<CombatEvent>();
        for (int i = 0; i < 4 && !session.View.CampaignHazards!.Any(h => h.ContentId == SupportId(forge)); i++) events.AddRange(session.Step());
        var warning = Assert.Single(session.View.CampaignHazards!, h => h.ContentId == SupportId(forge));
        var started = Assert.Single(events, e => e.Kind == "CampaignHazardWarned" && e.ContentId == SupportId(forge));
        Assert.Equal(42, started.Amount); Assert.Equal("Circle", warning.Kind); Assert.Equal(3000, warning.Radius);
        Assert.Equal(session.View.Actors[1].Position, warning.Position); Assert.Equal(warning.Position, warning.End);
        Assert.Equal(41, warning.RemainingTicks); Assert.Equal(started.Tick + 132, session.Capture().Actors[1].RecoveryUntil);
        Assert.Equal(1, session.Capture().Actors[1].SpecialCycle);
        return warning;
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(18, 18)]
    [InlineData(30, 18)]
    public void SporeMendHealsActualMissingHealthWithoutShieldingCasterOrDamagingPlayer(int missing, int healed)
    {
        var (content, state) = Fixture(false); var ally = state.Actors[2]; ally.Health = ally.MaxHealth - missing;
        int sourceHealth = state.Actors[1].Health;
        var session = CombatSession.Restore(content.CombatJson, state); Start(session, false);
        var events = Advance(session, 42);
        Assert.Equal(healed, Assert.Single(events, e => e.Kind == "Healed" && e.TargetId == ally.Id && e.ContentId == SupportId(false)).Amount);
        Assert.Equal(ally.Health + healed, session.View.Actors[2].Health);
        Assert.Equal(sourceHealth, session.View.Actors[1].Health); Assert.Equal(0, session.View.Actors[2].Barrier);
        Assert.DoesNotContain(events, e => e.Kind == "DamageApplied" && e.ContentId == SupportId(false));
        Assert.DoesNotContain(events, e => e.Kind == "Healed" && e.TargetId == 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SupportUsesAnnouncedFixedCircleAndLiveRangeAndLineOfSight(bool forge)
    {
        var (content, state) = Fixture(forge, wall: true);
        var session = CombatSession.Restore(content.CombatJson, state); Start(session, forge);
        var events = Advance(session, 42);
        string effect = forge ? "EnemyOvercharged" : "Healed";
        Assert.DoesNotContain(events, e => e.Kind == effect && e.TargetId == state.Actors[2].Id);
        Assert.Contains(events, e => e.Kind == effect && e.TargetId == state.Actors[4].Id);

        (content, state) = Fixture(forge); session = CombatSession.Restore(content.CombatJson, state);
        var warning = Start(session, forge); state = session.Capture();
        state.Actors[1].Position = new(-1200, 0); state.Actors[2].Position = new(3200, 1400);
        state.Actors[3].Position = new(2900, 0);
        session = CombatSession.Restore(content.CombatJson, state);
        Assert.Equal(warning.Position, Assert.Single(session.View.CampaignHazards!).Position);
        events = Advance(session, 42);
        Assert.DoesNotContain(events, e => e.Kind == effect && e.TargetId == state.Actors[2].Id);
        Assert.Contains(events, e => e.Kind == effect && e.TargetId == state.Actors[3].Id);
    }

    [Theory]
    [InlineData("boss.bell_saint", false)]
    [InlineData("enemy.ritual_anchor", false)]
    [InlineData("enemy.broken_bell", false)]
    [InlineData("enemy.feeding_root", false)]
    [InlineData("enemy.forge_sentinel", true)]
    public void SporeMendNeverRepairsBossesMechanismsOrEchoCopies(string definition, bool echo)
    {
        var (content, state) = Fixture(false, ally: definition);
        if (echo) state.Campaign!.Actors[state.Actors[2].Id] = state.Campaign.Actors[state.Actors[2].Id] with { IsEcho = true, ExpiresTick = 180 };
        var session = CombatSession.Restore(content.CombatJson, state); Start(session, false);
        var events = Advance(session, 42);
        Assert.DoesNotContain(events, e => e.Kind == "Healed" && e.TargetId == state.Actors[2].Id);
        Assert.Contains(events, e => e.Kind == "Healed" && e.TargetId == state.Actors[4].Id);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AcceptedHardControlOrCasterKillCancelsSupportBeforeResolution(bool forge, bool kill)
    {
        var (content, state) = Fixture(forge); if (kill) state.Actors[1].Health = 1;
        var session = CombatSession.Restore(content.CombatJson, state); var warning = Start(session, forge);
        var events = session.Step([new(CombatCommandKind.Cast, SkillId: kill ? "skill.cleave" : "skill.shield_breaker", TargetId: state.Actors[1].Id)]).ToList();
        events.AddRange(Advance(session, 45));
        Assert.Contains(events, e => kill ? e.Kind == "EntityKilled" && e.TargetId == state.Actors[1].Id :
            e.Kind == "StatusApplied" && e.ContentId == "Staggered" && e.TargetId == state.Actors[1].Id);
        Assert.DoesNotContain(events, e => e.ContentId == SupportId(forge) && e.Kind is "Healed" or "EnemyOvercharged");
        Assert.DoesNotContain(session.View.CampaignHazards!, h => h.Id == warning.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SupportAlternatesWithDamageAndCannotRecastBeforeRecovery(bool forge)
    {
        var (content, state) = Fixture(forge); var session = CombatSession.Restore(content.CombatJson, state); Start(session, forge);
        long recovery = session.Capture().Actors[1].RecoveryUntil;
        var warnings = new List<CombatEvent>();
        for (int tick = 0; tick < 380; tick++) warnings.AddRange(session.Step().Where(e => e.ActorId == state.Actors[1].Id && e.Kind == "CampaignHazardWarned"));
        Assert.True(warnings.Count >= 2);
        Assert.True(warnings[0].Tick >= recovery);
        Assert.Equal(forge ? "campaign.heatvent" : "campaign.poisonburst", warnings[0].ContentId);
        Assert.Equal(SupportId(forge), warnings[1].ContentId);
        Assert.True(warnings[1].Tick > warnings[0].Tick);
    }

    [Theory]
    [InlineData(false, "full")]
    [InlineData(false, "range")]
    [InlineData(true, "wrong-type")]
    [InlineData(true, "echo")]
    public void IneligibleAlliesCauseAnOrdinaryAttackInsteadOfAnEmptyChant(bool forge, string reason)
    {
        var (content, state) = Fixture(forge, ally: "enemy.memory_archer");
        foreach (var ally in state.Actors.Skip(2))
        {
            if (reason == "full") ally.Health = ally.MaxHealth;
            else if (reason == "range" || reason == "wrong-type") ally.Position = new(5500, -2000 + 1200 * state.Actors.IndexOf(ally));
            else state.Campaign!.Actors[ally.Id] = state.Campaign.Actors[ally.Id] with { IsEcho = true, ExpiresTick = 180 };
        }
        var session = CombatSession.Restore(content.CombatJson, state); var events = Advance(session, 4);
        Assert.DoesNotContain(session.View.CampaignHazards!, h => h.ContentId == SupportId(forge));
        Assert.Contains(events, e => e.Kind == "CampaignHazardWarned" && e.ContentId == (forge ? "campaign.heatvent" : "campaign.poisonburst"));
        Assert.Equal(1, session.Capture().Actors[1].SpecialCycle);
    }

    [Fact]
    public void ForgeBuffAffectsOnlyLiveOriginalSentinelsAndAddsDamageWithoutStackingOrHealing()
    {
        var (content, state) = Fixture(true, ally: "enemy.memory_archer");
        int health = state.Actors[4].Health;
        var session = CombatSession.Restore(content.CombatJson, state); Start(session, true);
        var events = Advance(session, 42);
        var buff = Assert.Single(events, e => e.Kind == "EnemyOvercharged");
        Assert.Equal(state.Actors[4].Id, buff.TargetId); Assert.Equal(90, buff.Amount);
        Assert.Equal(health, session.View.Actors[4].Health); Assert.Equal(0, session.View.Actors[4].Barrier);
        Assert.Equal(0, session.View.Actors[1].ForgeOverchargeTicks); Assert.Equal(0, session.View.Actors[2].ForgeOverchargeTicks);
        state = session.Capture(); long deadline = state.Campaign!.Actors[state.Actors[4].Id].ForgeOverchargeUntil;
        Assert.Equal(buff.Tick + 90, deadline);
        int Damage(CombatSnapshot value)
        {
            value.Actors[0].InvulnerableUntil = value.Tick;
            var sentinel = value.Actors[4]; sentinel.Position = new(value.Actors[0].Position.X + 600, value.Actors[0].Position.Z);
            sentinel.Pending = new("enemy.strike", 1, value.Actors[0].Position, value.Tick, value.NextActionId++);
            return CombatSession.Restore(content.CombatJson, value).Step().Single(e => e.Kind == "DamageApplied" && e.ActorId == sentinel.Id && e.TargetId == 1).Amount;
        }
        int charged = Damage(JsonData.Copy(state));
        state.Campaign.Actors[state.Actors[4].Id].ForgeOverchargeUntil = 0;
        int normal = Damage(JsonData.Copy(state)); Assert.True(charged > normal); Assert.InRange(charged - normal, 1, 5);
        // No buff may outlive its exact deadline, even while its owner is recovering.
        state.Campaign.Actors[state.Actors[4].Id].ForgeOverchargeUntil = deadline;
        session = CombatSession.Restore(content.CombatJson, state);
        while (session.Tick < deadline) session.Step();
        Assert.Equal(0, session.View.Actors[4].ForgeOverchargeTicks); session.Step();
        Assert.Equal(0, session.Capture().Campaign!.Actors[state.Actors[4].Id].ForgeOverchargeUntil);
    }

    [Fact]
    public void TwoBellowsChannelsRefreshOneBuffWithoutMultiplyingItsDamage()
    {
        var (content, state) = Fixture(true, ally: "enemy.heat_tender"); state.Actors[2].RecoveryUntil = 0;
        state.Actors[4].Position = new(1000, -1000);
        var session = CombatSession.Restore(content.CombatJson, state); Advance(session, 4); state = session.Capture();
        Assert.Equal(2, state.Campaign!.Hazards.Count(h => h.ContentId == SupportId(true)));
        var twice = CombatSession.Restore(content.CombatJson, state);
        state.Campaign.Hazards.RemoveAll(h => h.SourceId == state.Actors[2].Id);
        var once = CombatSession.Restore(content.CombatJson, state);
        var oneEvents = Advance(once, 42); var twoEvents = Advance(twice, 42);
        int target = state.Actors[4].Id;
        Assert.Single(oneEvents, e => e.Kind == "EnemyOvercharged" && e.TargetId == target);
        Assert.Equal(2, twoEvents.Count(e => e.Kind == "EnemyOvercharged" && e.TargetId == target));
        Assert.InRange(twice.View.Actors[4].ForgeOverchargeTicks, 1, 90);
        int Strike(CombatSession ready)
        {
            var value = ready.Capture(); value.Actors[0].InvulnerableUntil = value.Tick;
            value.Actors[4].Position = new(value.Actors[0].Position.X + 600, value.Actors[0].Position.Z);
            value.Actors[4].Pending = new("enemy.strike", 1, value.Actors[0].Position, value.Tick, value.NextActionId++);
            return CombatSession.Restore(content.CombatJson, value).Step().Single(e => e.Kind == "DamageApplied" && e.ActorId == target && e.TargetId == 1).Amount;
        }
        Assert.Equal(Strike(once), Strike(twice));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SupportWarningsAndEffectsRestoreAndReplayExactlyWithoutExtraRandomDraws(bool forge)
    {
        var (content, state) = Fixture(forge); var session = CombatSession.Restore(content.CombatJson, state);
        var recorder = new CombatRecorder(session); var other = CombatSession.Restore(content.CombatJson, state);
        var rng = state.Rng; int resolutions = 0;
        for (int tick = 0; tick < 320; tick++)
        {
            var events = recorder.Step(session); Assert.Equal(JsonData.Hash(events), JsonData.Hash(other.Step()));
            resolutions += events.Count(e => e.Kind == "CampaignHazardResolved" && e.ContentId == SupportId(forge));
            if (tick % 13 == 0) other = CombatSession.Restore(content.CombatJson, other.Capture());
            Assert.Equal(session.StateHash, other.StateHash);
        }
        Assert.True(resolutions >= 2); Assert.Equal(rng, session.Capture().Rng);
        var replay = CombatReplayRunner.Run(content.CombatJson, recorder.Capture());
        Assert.True(replay.Success, replay.Detail); Assert.Equal(session.StateHash, replay.FinalHash);
        var hub = content.CreateEncounter("hub", previous: session.Capture());
        Assert.Null(hub.Capture().Campaign); Assert.Empty(hub.View.CampaignHazards!);
        Assert.All(hub.View.Actors, a => Assert.Equal(0, a.ForgeOverchargeTicks));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviousOpeningCatalogKeepsItsOriginalDamagingPattern(bool forge)
    {
        var old = Read("fixtures/campaign-combat-opening-depth.json");
        var (content, state) = Fixture(forge, overlay: old);
        var session = CombatSession.Restore(content.CombatJson, state); var events = Advance(session, 140);
        Assert.DoesNotContain(events, e => e.ContentId == SupportId(forge));
        Assert.Contains(events, e => e.Kind == "CampaignHazardResolved" && e.ContentId == (forge ? "campaign.heatvent" : "campaign.poisonburst"));
        Assert.All(session.Capture().Campaign!.Actors.Values, a => Assert.Equal(0, a.ForgeOverchargeUntil));
    }

    public static IEnumerable<object[]> InvalidSupportWarnings() =>
        from forge in new[] { false, true }
        from field in new[] { "source", "dead-source", "radius", "kind", "end", "damage", "status", "family", "delay" }
        select new object[] { forge, field };

    [Theory, MemberData(nameof(InvalidSupportWarnings))]
    public void RestoreRejectsForgedSupportOwnershipPayloadAndTiming(bool forge, string field)
    {
        var (content, state) = Fixture(forge); var session = CombatSession.Restore(content.CombatJson, state); Start(session, forge);
        state = session.Capture(); var hazard = Assert.Single(state.Campaign!.Hazards);
        state.Campaign.Hazards[0] = field switch
        {
            "source" => hazard with { SourceId = state.Actors[2].Id },
            "radius" => hazard with { Radius = 2999 },
            "kind" => hazard with { Kind = "Line" },
            "end" => hazard with { End = new(600, 0) },
            "damage" => hazard with { Damage = 1 },
            "status" => hazard with { Status = "Burning" },
            "family" => hazard with { Family = DamageFamily.PhysicalCrush },
            "delay" => hazard with { ResolveTick = state.Tick + 43 },
            _ => hazard
        };
        if (field == "dead-source") { state.Actors[1].Health = 0; state.Actors[1].DeathProcessed = true; }
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(content.CombatJson, state));
    }

    [Theory]
    [InlineData("negative")]
    [InlineData("past")]
    [InlineData("far-future")]
    [InlineData("wrong-type")]
    [InlineData("dead")]
    [InlineData("echo")]
    [InlineData("old-catalog")]
    public void ForgeDeadlineCannotBeForgedOntoAnotherActorOrCatalog(string reason)
    {
        var (content, state) = Fixture(true, overlay: reason == "old-catalog" ? Read("fixtures/campaign-combat-opening-depth.json") : null);
        state.Tick = 10; var actor = state.Actors[2]; var runtime = state.Campaign!.Actors[actor.Id];
        runtime.ForgeOverchargeUntil = reason switch { "negative" => -1, "past" => 9, "far-future" => 101, _ => 90 };
        if (reason == "wrong-type") state.Campaign.Actors[state.Actors[1].Id].ForgeOverchargeUntil = 90;
        if (reason == "dead") { actor.Health = 0; actor.DeathProcessed = true; }
        if (reason == "echo") state.Campaign.Actors[actor.Id] = runtime with { IsEcho = true, ExpiresTick = 180 };
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(content.CombatJson, state));
    }

    [Fact]
    public void KilledSentinelLosesForgeBuffImmediatelyAndZeroTimerPreservesLegacyShape()
    {
        var (content, state) = Fixture(true); var session = CombatSession.Restore(content.CombatJson, state); Start(session, true); Advance(session, 42);
        state = session.Capture(); var sentinel = state.Actors[4]; sentinel.Health = 1;
        sentinel.Statuses.Add(new() { Id = "Burning", OwnerId = 1, SourceId = 1, ActionId = state.NextActionId++, NextTick = state.Tick, ExpiresTick = state.Tick + 90 });
        session = CombatSession.Restore(content.CombatJson, state);
        Assert.Contains(session.Step(), e => e.Kind == "EntityKilled" && e.TargetId == sentinel.Id);
        Assert.Equal(0, session.Capture().Campaign!.Actors[sentinel.Id].ForgeOverchargeUntil);
        Assert.Equal(0, session.View.Actors.Single(a => a.Id == sentinel.Id).ForgeOverchargeTicks);
        Assert.DoesNotContain("forgeOverchargeUntil", JsonData.Write(new CampaignActorState()));
    }

    [Theory]
    [InlineData("buffed-sentinel")]
    [InlineData("spore-caster")]
    [InlineData("forge-caster")]
    public void DevourerConsumptionCleansNewSupportStateBeforeTheImmediateSave(string victim)
    {
        bool forge = victim != "spore-caster";
        var (content, state) = Fixture(forge); var session = CombatSession.Restore(content.CombatJson, state); Start(session, forge);
        if (victim == "buffed-sentinel") Advance(session, 42);
        state = session.Capture(); var target = state.Actors[victim == "buffed-sentinel" ? 4 : 1]; var devourer = state.Actors[3];
        state.Campaign!.Actors[devourer.Id] = state.Campaign.Actors[devourer.Id] with { Modifiers = ["Devourer"] };
        devourer.Pending = new("elite.devourer", target.Id, target.Position, state.Tick, state.NextActionId++);
        session = CombatSession.Restore(content.CombatJson, state); var recorder = new CombatRecorder(session);
        Assert.Contains(recorder.Step(session), e => e.Kind == "EliteDevoured" && e.TargetId == target.Id);
        Assert.Contains(target.Id, session.Capture().ConsumedCorpseIds);
        Assert.Equal(0, session.Capture().Campaign!.Actors[target.Id].ForgeOverchargeUntil);
        Assert.DoesNotContain(session.View.CampaignHazards!, h => h.SourceId == target.Id && CombatSession.IsSupportHazard(h.ContentId));
        var restored = CombatSession.Restore(content.CombatJson, session.Capture()); Assert.Equal(session.StateHash, restored.StateHash);
        for (int tick = 0; tick < 60; tick++) Assert.Equal(JsonData.Hash(recorder.Step(session)), JsonData.Hash(restored.Step()));
        var replay = CombatReplayRunner.Run(content.CombatJson, recorder.Capture()); Assert.True(replay.Success, replay.Detail);
        Assert.Equal(session.StateHash, replay.FinalHash);
    }

    public static IEnumerable<object[]> BossStates() =>
        from encounter in new[] { "campaign.rootheart", "campaign.furnace_spindle" }
        from state in new[] { "open", "warning", "pending", "guarded", "dead", "expired", "shielded" }
        select new object[] { encounter, state };

    [Theory, MemberData(nameof(BossStates))]
    public void BossOpeningProjectionUsesAuthoritativeTimersAndProtection(string encounter, string condition)
    {
        var content = Live(); var state = content.CreateEncounter(encounter).Capture();
        var boss = state.Actors[1]; boss.RecoveryUntil = 90; boss.State = "Idle";
        if (condition != "shielded")
            foreach (var root in state.Actors.Where(a => a.DefinitionId == "enemy.feeding_root")) { root.Health = 0; root.DeathProcessed = true; }
        if (condition == "warning") state.Campaign!.Hazards.Add(new(state.NextObjectId++, "Circle", boss.Position, boss.Position,
            1500, 20, "campaign.test_warning", boss.Id, 10, DamageFamily.Fire, "", state.NextActionId++));
        if (condition == "pending") boss.Pending = new("campaign.rush", 1, state.Actors[0].Position, 20, state.NextActionId++);
        if (condition == "guarded") state.Campaign!.Actors[boss.Id].GuardedUntil = 50;
        if (condition == "dead") { boss.Health = 0; boss.DeathProcessed = true; }
        if (condition == "expired") boss.RecoveryUntil = 0;
        var session = CombatSession.Restore(content.CombatJson, state); string hash = session.StateHash;
        var view = session.View.Actors.Single(a => a.Id == boss.Id);
        bool open = condition == "open" || condition == "shielded" && encounter == "campaign.furnace_spindle";
        Assert.Equal(open ? 90 : 0, view.BossRecoveryTicks);
        Assert.Equal(condition == "guarded" && encounter == "campaign.furnace_spindle" ? 50 : 0, view.BossGuardedTicks);
        Assert.Equal(hash, session.StateHash); Assert.Equal(hash, CombatSession.Restore(content.CombatJson, session.Capture()).StateHash);
        Assert.All(session.View.Actors.Where(a => a.Id != boss.Id), a => Assert.Equal(0, a.BossRecoveryTicks));
    }

    [Theory]
    [InlineData("campaign.rootheart")]
    [InlineData("campaign.furnace_spindle")]
    public void ActualBossSequenceExposesRecoveryAfterEveryAttackWarningHasResolved(string encounter)
    {
        var content = Live(); var state = content.CreateEncounter(encounter).Capture(); state.Actors[0].InvulnerableUntil = 1000;
        var boss = state.Actors[1]; state.Campaign!.Actors[boss.Id] = state.Campaign.Actors[boss.Id] with { Modifiers = [] };
        foreach (var actor in state.Actors.Skip(2))
        {
            actor.RecoveryUntil = 1000;
            if (actor.DefinitionId == "enemy.feeding_root") { actor.Health = 0; actor.DeathProcessed = true; }
        }
        var session = CombatSession.Restore(content.CombatJson, state); int openings = 0, unresolvedDeadlines = 0;
        for (int tick = 0; tick < 230; tick++)
        {
            session.Step(); var view = session.View.Actors.Single(a => a.Id == boss.Id);
            var warnings = session.View.CampaignHazards!.Where(h => h.SourceId == boss.Id).ToArray();
            if (warnings.Length > 0) Assert.Equal(0, view.BossRecoveryTicks);
            // A zero-countdown warning still exists before the next Step resolves it.
            if (warnings.Length > 0 && warnings.All(h => h.RemainingTicks == 0)) unresolvedDeadlines++;
            if (view.BossRecoveryTicks <= 0) continue;
            openings++; Assert.False(view.Shielded); Assert.False(view.Guarded);
            Assert.DoesNotContain(session.View.CampaignHazards!, h => h.SourceId == boss.Id && h.RemainingTicks > 0);
            Assert.Equal(session.Capture().Actors[1].RecoveryUntil - session.Tick, view.BossRecoveryTicks);
        }
        Assert.True(openings >= 25, encounter + " did not provide a usable recovery period");
        Assert.True(unresolvedDeadlines > 0, encounter + " did not exercise the unresolved deadline boundary");
    }

    public static IEnumerable<object[]> MidgameEncounters() =>
        from discipline in CombatSession.Disciplines
        from entry in new[] { ("campaign.living_ruins", 4), ("campaign.plague_village", 5), ("campaign.rootheart", 6),
            ("campaign.cinder_pack", 7), ("campaign.extraction_floor", 8), ("campaign.furnace_spindle", 9) }
        select new object[] { discipline, entry.Item1, entry.Item2 };

    [Theory, MemberData(nameof(MidgameEncounters))]
    public void AllFiveDisciplinesCanClearActualMidgameEncountersAndReplay(string discipline, string encounter, int level)
    {
        var content = Live(); var hub = content.CreateEncounter("hub");
        hub.ApplyProgressionBuild(LegalBuild(discipline, level));
        var session = content.CreateEncounter(encounter, previous: hub.Capture()); var recorder = new CombatRecorder(session);
        for (int tick = 0; tick < 7500 && session.View.Actors[0].Health > 0 && session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0); tick++)
            recorder.Step(session, CampaignCombatSmoke.Commands(session.View, session.Room));
        output.WriteLine($"{discipline} {encounter}: tick={session.Tick}, health={session.View.Actors[0].Health}, hash={session.StateHash}");
        Assert.True(session.View.Actors[0].Health > 0, $"{discipline} died in {encounter} at {session.Tick}");
        Assert.DoesNotContain(session.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        Assert.Empty(session.View.CampaignHazards!);
        var replay = CombatReplayRunner.Run(content.CombatJson, recorder.Capture()); Assert.True(replay.Success, replay.Detail);
        Assert.Equal(session.StateHash, replay.FinalHash);
    }

    // These are legal, equal-budget controlled level fixtures, not earned campaign runs.
    private static CombatProgressionBuild LegalBuild(string discipline, int level)
    {
        var progression = ProgressionSession.Create(ProgressionContent.Parse(Read("progression.json")), discipline);
        Assert.True(progression.EarnExperience("fixture.level", (int)progression.ExperienceForLevel(level)).Success);
        for (int i = 0; i < level - 1; i++) Assert.True(progression.AllocatePassive("fixture.passive." + i, i % 2 == 0 ? "Offense" : "Defense").Success);
        Assert.Equal(progression.StateHash, ProgressionSession.Restore(ProgressionContent.Parse(Read("progression.json")), progression.Capture()).StateHash);
        return new(Discipline: discipline, Level: progression.Level, Offense: progression.View.Stats.GetValueOrDefault("passive.Offense"),
            Defense: progression.View.Stats.GetValueOrDefault("passive.Defense"), UltimateUnlocked: false, UnlockedMutations: []);
    }
}
