using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace Ashenwake.Tests;

public sealed class LateCampaignCombatDepthTests(ITestOutputHelper output)
{
    private const string Ward = "campaign.oath_ward";
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static CampaignCombatContent Live() => CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json"));

    // Detached, valid geometry and shipping enemy definitions isolate the ward's
    // effects. These fixtures do not represent an earned campaign progression run.
    private static (CampaignCombatContent Content, CombatSnapshot State) Fixture(bool wall = false, string ally = "enemy.oath_giant", string? overlay = null)
    {
        var data = JsonData.Read<CampaignCombatDefinition>(overlay ?? Read("campaign-combat.json"));
        var encounter = data.Encounters.Single(e => e.Id == "campaign.monastery") with
        {
            Rule = "Ambush",
            Room = new(12000, 10000, new(-1800, 0), new(0, 0), wall ? [new(500, 500, 1300, 2200)] : []),
            Spawns = [new("enemy.contract_keeper", new(0, 0), []), new(ally, new(1800, 1400), []),
                new("enemy.bone_sentinel", new(3600, 0), []), new("enemy.bone_sentinel", new(0, -1500), [])]
        };
        data = data with { Encounters = data.Encounters.Select(e => e.Id == encounter.Id ? encounter : e).ToArray() };
        var content = CampaignCombatContent.Parse(Read("combat.json"), JsonData.Write(data));
        var state = content.CreateEncounter(encounter.Id).Capture();
        state.Fragments.Clear(); state.Equipment.Clear(); state.Campaign!.NextHazardTick = 0;
        state.Actors[0].InvulnerableUntil = 1000; state.Momentum = 100;
        foreach (var actor in state.Actors.Skip(2)) { actor.RecoveryUntil = 1000; actor.Health -= 30; }
        return (content, state);
    }

    private static List<CombatEvent> Advance(CombatSession session, int ticks)
    {
        List<CombatEvent> events = [];
        for (int tick = 0; tick < ticks; tick++) events.AddRange(session.Step());
        return events;
    }

    private static CombatHazardView StartWard(CombatSession session)
    {
        var events = new List<CombatEvent>();
        for (int tick = 0; tick < 4 && !session.View.CampaignHazards!.Any(h => h.ContentId == Ward); tick++) events.AddRange(session.Step());
        var warning = Assert.Single(session.View.CampaignHazards!, h => h.ContentId == Ward);
        var started = Assert.Single(events, e => e.Kind == "CampaignHazardWarned" && e.ContentId == Ward);
        Assert.Equal(45, started.Amount); Assert.Equal("Circle", warning.Kind); Assert.Equal(3000, warning.Radius);
        Assert.Equal(session.View.Actors[1].Position, warning.Position); Assert.Equal(warning.Position, warning.End);
        Assert.Equal(44, warning.RemainingTicks); Assert.Equal(started.Tick + 135, session.Capture().Actors[1].RecoveryUntil);
        Assert.Equal(1, session.Capture().Actors[1].SpecialCycle); Assert.True(CombatSession.IsSupportHazard(Ward));
        return warning;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    [InlineData(35)]
    [InlineData(36)]
    [InlineData(80)]
    public void WardCapsExistingBarrierWithoutHealingDamagingOrShieldingItsCaster(int before)
    {
        var (content, state) = Fixture(); var ally = state.Actors[2]; ally.Barrier = before;
        int allyHealth = ally.Health, sourceHealth = state.Actors[1].Health;
        var session = CombatSession.Restore(content.CombatJson, state); var warning = StartWard(session);
        var events = Advance(session, 45); int grant = Math.Max(0, 36 - before);
        var granted = events.Where(e => e.Kind == "BarrierGranted" && e.TargetId == ally.Id && e.ContentId == Ward).ToArray();
        if (grant == 0) Assert.Empty(granted);
        else
        {
            var effect = Assert.Single(granted); Assert.Equal(grant, effect.Amount); Assert.Equal(state.Actors[1].Id, effect.ActorId);
            Assert.Equal(events.Single(e => e.Kind == "CampaignHazardResolved" && e.ContentId == Ward).ActionId, effect.ActionId);
        }
        Assert.Equal(Math.Max(before, 36), session.View.Actors[2].Barrier); Assert.Equal(allyHealth, session.View.Actors[2].Health);
        Assert.Equal(sourceHealth, session.View.Actors[1].Health); Assert.Equal(0, session.View.Actors[1].Barrier);
        Assert.DoesNotContain(events, e => e.ContentId == Ward && e.Kind is "DamageApplied" or "Healed");
        Assert.DoesNotContain(session.View.CampaignHazards!, h => h.Id == warning.Id);
    }

    [Fact]
    public void GrantedWardAbsorbsAnActualPlayerAttackBeforeHealth()
    {
        var (content, state) = Fixture(); var session = CombatSession.Restore(content.CombatJson, state); StartWard(session); Advance(session, 45);
        state = session.Capture(); var ally = state.Actors[2]; state.Actors[0].Position = new(1800, 400);
        int startingHealth = ally.Health;
        var protectedSession = CombatSession.Restore(content.CombatJson, state);
        state.Actors[2].Barrier = 0; var bareSession = CombatSession.Restore(content.CombatJson, state);
        List<CombatEvent> Strike(CombatSession target)
        {
            var events = target.Step([new(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: ally.Id)]).ToList();
            events.AddRange(Advance(target, 20)); return events;
        }
        var warded = Strike(protectedSession); var bare = Strike(bareSession);
        int absorbed = warded.Where(e => e.Kind == "BarrierAbsorbed" && e.ActorId == ally.Id).Sum(e => e.Amount);
        Assert.InRange(absorbed, 1, 36); Assert.DoesNotContain(bare, e => e.Kind == "BarrierAbsorbed" && e.ActorId == ally.Id);
        Assert.Equal(absorbed, protectedSession.View.Actors[2].Health - bareSession.View.Actors[2].Health);
        Assert.Equal(36 - absorbed, protectedSession.View.Actors[2].Barrier); Assert.True(bareSession.View.Actors[2].Health < startingHealth);
    }

    [Fact]
    public void WardUsesAnnouncedFixedCircleAndCurrentRecipientRangeAndLineOfSight()
    {
        var (content, state) = Fixture(wall: true); var session = CombatSession.Restore(content.CombatJson, state); StartWard(session);
        var events = Advance(session, 45);
        Assert.DoesNotContain(events, e => e.Kind == "BarrierGranted" && e.TargetId == state.Actors[2].Id);
        Assert.Contains(events, e => e.Kind == "BarrierGranted" && e.TargetId == state.Actors[4].Id);
        (content, state) = Fixture(); session = CombatSession.Restore(content.CombatJson, state); var warning = StartWard(session);
        state = session.Capture(); state.Actors[1].Position = new(-1200, 0); state.Actors[2].Position = new(3200, 1400);
        state.Actors[3].Position = new(2900, 0); session = CombatSession.Restore(content.CombatJson, state);
        Assert.Equal(warning.Position, Assert.Single(session.View.CampaignHazards!).Position);
        events = Advance(session, 45);
        Assert.DoesNotContain(events, e => e.Kind == "BarrierGranted" && e.TargetId == state.Actors[2].Id);
        Assert.Contains(events, e => e.Kind == "BarrierGranted" && e.TargetId == state.Actors[3].Id);
    }

    [Theory]
    [InlineData("enemy.memory_archer", false)]
    [InlineData("enemy.contract_keeper", false)]
    [InlineData("boss.covenant_warden", false)]
    [InlineData("enemy.ritual_anchor", false)]
    [InlineData("enemy.broken_bell", false)]
    [InlineData("enemy.seal_channel", false)]
    [InlineData("enemy.oath_giant", true)]
    [InlineData("enemy.bone_sentinel", true)]
    public void WardExcludesOtherEnemyTypesMechanismsBossesAndCopies(string definition, bool echo)
    {
        var (content, state) = Fixture(ally: definition);
        if (echo) state.Campaign!.Actors[state.Actors[2].Id] = state.Campaign.Actors[state.Actors[2].Id] with { IsEcho = true, ExpiresTick = 180 };
        var session = CombatSession.Restore(content.CombatJson, state); StartWard(session); var events = Advance(session, 45);
        Assert.DoesNotContain(events, e => e.Kind == "BarrierGranted" && e.TargetId == state.Actors[2].Id);
        Assert.Contains(events, e => e.Kind == "BarrierGranted" && e.TargetId == state.Actors[4].Id);
    }

    [Fact]
    public void WardDoesNotProtectThePlayerOrAFriendlySummon()
    {
        var (content, state) = Fixture(); int summonId = state.NextActorId++;
        state.Actors.Add(state.Actors[2] with
        {
            Id = summonId,
            DefinitionId = "summon.ancestor",
            Faction = CombatFaction.Ally,
            OwnerId = 1,
            Position = new(-1200, 1500),
            ExpiresTick = 500
        });
        var session = CombatSession.Restore(content.CombatJson, state); StartWard(session); var events = Advance(session, 45);
        Assert.DoesNotContain(events, e => e.Kind == "BarrierGranted" && (e.TargetId == 1 || e.TargetId == summonId));
        Assert.Equal(0, session.View.Actors[0].Barrier); Assert.Equal(0, session.View.Actors.Single(a => a.Id == summonId).Barrier);
    }

    [Theory]
    [InlineData("full")]
    [InlineData("out-of-range")]
    [InlineData("dead")]
    [InlineData("copies")]
    [InlineData("copied-source")]
    public void NoEligibleWardRecipientFallsBackToOrdinaryOathMark(string reason)
    {
        var (content, state) = Fixture();
        foreach (var ally in state.Actors.Skip(2))
        {
            if (reason == "full") ally.Barrier = 36;
            if (reason == "out-of-range") ally.Position = new(5500, -2000 + 1200 * state.Actors.IndexOf(ally));
            if (reason == "dead") { ally.Health = 0; ally.DeathProcessed = true; }
            if (reason == "copies") state.Campaign!.Actors[ally.Id] = state.Campaign.Actors[ally.Id] with { IsEcho = true, ExpiresTick = 180 };
        }
        if (reason == "copied-source") state.Campaign!.Actors[state.Actors[1].Id] = state.Campaign.Actors[state.Actors[1].Id] with { IsEcho = true, ExpiresTick = 180 };
        var session = CombatSession.Restore(content.CombatJson, state); var events = Advance(session, 4);
        Assert.DoesNotContain(session.View.CampaignHazards!, h => h.ContentId == Ward);
        Assert.Contains(events, e => e.Kind == "CampaignHazardWarned" && e.ContentId == "campaign.oathmark");
        Assert.Equal(1, session.Capture().Actors[1].SpecialCycle);
    }

    [Fact]
    public void TwoWardChannelsTopUpOnceAndNeverMultiplyBarrier()
    {
        var (content, state) = Fixture(ally: "enemy.contract_keeper"); state.Actors[2].RecoveryUntil = 0;
        state.Actors[4].Position = new(1000, -1000); var session = CombatSession.Restore(content.CombatJson, state);
        Advance(session, 4); Assert.Equal(2, session.View.CampaignHazards!.Count(h => h.ContentId == Ward));
        var events = Advance(session, 45); int ally = state.Actors[4].Id;
        Assert.Equal(36, session.View.Actors[4].Barrier);
        Assert.Equal(36, Assert.Single(events, e => e.Kind == "BarrierGranted" && e.TargetId == ally && e.ContentId == Ward).Amount);
        Assert.Equal(2, events.Count(e => e.Kind == "CampaignHazardResolved" && e.ContentId == Ward));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HardControlOrCasterDeathCancelsWardAndRetainsRecovery(bool kill)
    {
        var (content, state) = Fixture(); if (kill) state.Actors[1].Health = 1;
        var session = CombatSession.Restore(content.CombatJson, state); var warning = StartWard(session);
        long recovery = session.Capture().Actors[1].RecoveryUntil;
        var events = session.Step([new(CombatCommandKind.Cast, SkillId: kill ? "skill.cleave" : "skill.shield_breaker", TargetId: state.Actors[1].Id)]).ToList();
        bool checkedInterruption = false;
        for (int tick = 0; tick < 50; tick++)
        {
            var next = session.Step(); events.AddRange(next);
            if (!next.Any(e => e.TargetId == state.Actors[1].Id && (kill ? e.Kind == "EntityKilled" :
                e.Kind == "StatusApplied" && e.ContentId == "Staggered"))) continue;
            checkedInterruption = true;
            Assert.DoesNotContain(session.View.CampaignHazards!, h => h.Id == warning.Id);
            if (!kill) Assert.Contains(session.View.Actors[1].Statuses, status => status.Id == "Staggered" && status.RemainingTicks > 0);
            Assert.Equal(session.StateHash, CombatSession.Restore(content.CombatJson, session.Capture()).StateHash);
        }
        Assert.True(checkedInterruption);
        Assert.Contains(events, e => kill ? e.Kind == "EntityKilled" && e.TargetId == state.Actors[1].Id :
            e.Kind == "StatusApplied" && e.ContentId == "Staggered" && e.TargetId == state.Actors[1].Id);
        Assert.DoesNotContain(events, e => e.ContentId == Ward && e.Kind == "BarrierGranted");
        Assert.DoesNotContain(session.View.CampaignHazards!, h => h.Id == warning.Id);
        Assert.Equal(recovery, session.Capture().Actors[1].RecoveryUntil);
        Assert.Equal(session.StateHash, CombatSession.Restore(content.CombatJson, session.Capture()).StateHash);
    }

    [Fact]
    public void OrdinaryDamageDoesNotCancelTheWardChannel()
    {
        var (content, state) = Fixture(); var session = CombatSession.Restore(content.CombatJson, state); StartWard(session);
        var events = session.Step([new(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: state.Actors[1].Id)]).ToList();
        events.AddRange(Advance(session, 50));
        Assert.Contains(events, e => e.Kind == "DamageApplied" && e.TargetId == state.Actors[1].Id);
        Assert.Contains(events, e => e.Kind == "BarrierGranted" && e.ContentId == Ward);
    }

    [Fact]
    public void ConsumingTheCasterRemovesItsWardBeforeImmediateSaveAndReplay()
    {
        var (content, state) = Fixture(); var session = CombatSession.Restore(content.CombatJson, state); StartWard(session);
        state = session.Capture(); var victim = state.Actors[1]; var devourer = state.Actors[3];
        state.Campaign!.Actors[devourer.Id] = state.Campaign.Actors[devourer.Id] with { Modifiers = ["Devourer"] };
        devourer.Pending = new("elite.devourer", victim.Id, victim.Position, state.Tick, state.NextActionId++);
        session = CombatSession.Restore(content.CombatJson, state); var recorder = new CombatRecorder(session);
        Assert.Contains(recorder.Step(session), e => e.Kind == "EliteDevoured" && e.TargetId == victim.Id);
        Assert.Contains(victim.Id, session.Capture().ConsumedCorpseIds);
        Assert.DoesNotContain(session.View.CampaignHazards!, h => h.SourceId == victim.Id && h.ContentId == Ward);
        var restored = CombatSession.Restore(content.CombatJson, session.Capture()); Assert.Equal(session.StateHash, restored.StateHash);
        for (int tick = 0; tick < 60; tick++) Assert.Equal(JsonData.Hash(recorder.Step(session)), JsonData.Hash(restored.Step()));
        var replay = CombatReplayRunner.Run(content.CombatJson, recorder.Capture()); Assert.True(replay.Success, replay.Detail);
        Assert.Equal(session.StateHash, replay.FinalHash);
    }

    [Fact]
    public void WardAlternatesWithOathMarkAndCannotRecastBeforeRecovery()
    {
        var (content, state) = Fixture(); var session = CombatSession.Restore(content.CombatJson, state); StartWard(session);
        long recovery = session.Capture().Actors[1].RecoveryUntil;
        Advance(session, 45); state = session.Capture(); state.Actors[2].Barrier = state.Actors[4].Barrier = 0;
        session = CombatSession.Restore(content.CombatJson, state);
        var warnings = Advance(session, 360).Where(e => e.ActorId == state.Actors[1].Id && e.Kind == "CampaignHazardWarned").ToArray();
        Assert.True(warnings.Length >= 2); Assert.True(warnings[0].Tick >= recovery);
        Assert.Equal("campaign.oathmark", warnings[0].ContentId); Assert.Equal(Ward, warnings[1].ContentId);
    }

    [Fact]
    public void WardSaveRestoreReplayAndRoomCleanupUseNoAdditionalRandomDraws()
    {
        var (content, state) = Fixture(); var session = CombatSession.Restore(content.CombatJson, state);
        var other = CombatSession.Restore(content.CombatJson, state); var recorder = new CombatRecorder(session); var rng = state.Rng;
        int grants = 0;
        for (int tick = 0; tick < 320; tick++)
        {
            var events = recorder.Step(session); Assert.Equal(JsonData.Hash(events), JsonData.Hash(other.Step()));
            grants += events.Count(e => e.Kind == "BarrierGranted" && e.ContentId == Ward);
            if (tick % 13 == 0) other = CombatSession.Restore(content.CombatJson, other.Capture());
            Assert.Equal(session.StateHash, other.StateHash);
        }
        Assert.True(grants >= 2); Assert.Equal(rng, session.Capture().Rng);
        var replay = CombatReplayRunner.Run(content.CombatJson, recorder.Capture()); Assert.True(replay.Success, replay.Detail);
        Assert.Equal(session.StateHash, replay.FinalHash);
        var hub = content.CreateEncounter("hub", previous: session.Capture()); Assert.Null(hub.Capture().Campaign); Assert.Empty(hub.View.CampaignHazards!);
    }

    [Fact]
    public void WardCannotExceedTheSharedWarningBudget()
    {
        var (content, state) = Fixture();
        for (int i = 0; i < 32; i++) state.Campaign!.Hazards.Add(new(state.NextObjectId++, "Circle", new(6000, 0), new(6000, 0),
            100, 100, "campaign.test_warning", state.Actors[1].Id, 0, DamageFamily.PhysicalCrush, "", state.NextActionId++));
        var session = CombatSession.Restore(content.CombatJson, state); Advance(session, 4);
        Assert.Equal(32, session.View.CampaignHazards!.Count); Assert.DoesNotContain(session.View.CampaignHazards!, h => h.ContentId == Ward);
        Assert.True(session.View.RejectedEffects > 0); Assert.InRange(session.View.PeakEffects, 0, 256);
        Assert.Equal(session.StateHash, CombatSession.Restore(content.CombatJson, session.Capture()).StateHash);
    }

    [Fact]
    public void PublishedMidgameCatalogRetainsOriginalOathMarkBehavior()
    {
        var (content, state) = Fixture(overlay: Read("fixtures/campaign-combat-midgame-depth.json"));
        var session = CombatSession.Restore(content.CombatJson, state); var recorder = new CombatRecorder(session);
        List<CombatEvent> events = [];
        for (int tick = 0; tick < 180; tick++) events.AddRange(recorder.Step(session));
        Assert.DoesNotContain(events, e => e.ContentId == Ward);
        Assert.Contains(events, e => e.Kind == "CampaignHazardResolved" && e.ContentId == "campaign.oathmark");
        var replay = CombatReplayRunner.Run(content.CombatJson, recorder.Capture()); Assert.True(replay.Success, replay.Detail);
        Assert.Equal(session.StateHash, replay.FinalHash);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("missing-source")]
    [InlineData("player-source")]
    [InlineData("dead-source")]
    [InlineData("stunned-source")]
    [InlineData("copied-source")]
    [InlineData("radius")]
    [InlineData("kind")]
    [InlineData("end")]
    [InlineData("damage")]
    [InlineData("status")]
    [InlineData("family")]
    [InlineData("delay")]
    public void RestoreRejectsForgedWardSourceGeometryPayloadOrDeadline(string field)
    {
        var (content, state) = Fixture(); var session = CombatSession.Restore(content.CombatJson, state); StartWard(session);
        state = session.Capture(); var hazard = Assert.Single(state.Campaign!.Hazards);
        state.Campaign.Hazards[0] = field switch
        {
            "source" => hazard with { SourceId = state.Actors[2].Id },
            "missing-source" => hazard with { SourceId = state.NextActorId },
            "player-source" => hazard with { SourceId = 1 },
            "radius" => hazard with { Radius = 2999 },
            "kind" => hazard with { Kind = "Line" },
            "end" => hazard with { End = new(600, 0) },
            "damage" => hazard with { Damage = 1 },
            "status" => hazard with { Status = "Burning" },
            "family" => hazard with { Family = DamageFamily.Fire },
            "delay" => hazard with { ResolveTick = state.Tick + 46 },
            _ => hazard
        };
        if (field == "dead-source") { state.Actors[1].Health = 0; state.Actors[1].DeathProcessed = true; }
        if (field == "stunned-source") state.Actors[1].Statuses.Add(new()
        {
            Id = "Staggered",
            OwnerId = 1,
            SourceId = 1,
            ActionId = state.NextActionId++,
            ExpiresTick = state.Tick + 30
        });
        if (field == "copied-source") state.Campaign.Actors[state.Actors[1].Id] = state.Campaign.Actors[state.Actors[1].Id] with { IsEcho = true, ExpiresTick = state.Tick + 180 };
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(content.CombatJson, state));
    }

    [Fact]
    public void RestoreRejectsAValidWardPayloadUnderTheOldBehaviorCatalog()
    {
        var (content, state) = Fixture(overlay: Read("fixtures/campaign-combat-midgame-depth.json"));
        state.Campaign!.Hazards.Add(new(state.NextObjectId++, "Circle", state.Actors[1].Position, state.Actors[1].Position,
            3000, state.Tick + 45, Ward, state.Actors[1].Id, 0, DamageFamily.PhysicalCrush, "", state.NextActionId++));
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(content.CombatJson, state));
    }

    public static IEnumerable<object[]> BossStates() =>
        from encounter in new[] { "campaign.covenant_warden", "campaign.breach_heart" }
        from state in new[] { "open", "warning", "due-warning", "pending", "guarded", "dead", "expired", "shielded", "copy" }
        select new object[] { encounter, state };

    [Theory, MemberData(nameof(BossStates))]
    public void LateBossRecoveryUsesRealProtectionAndUnresolvedAttackState(string encounter, string condition)
    {
        var content = Live(); var state = content.CreateEncounter(encounter).Capture();
        var boss = state.Actors[1]; boss.RecoveryUntil = 90; boss.State = "Idle";
        if (condition != "shielded")
            foreach (var channel in state.Actors.Where(a => a.DefinitionId == "enemy.seal_channel")) { channel.Health = 0; channel.DeathProcessed = true; }
        if (condition is "warning" or "due-warning") state.Campaign!.Hazards.Add(new(state.NextObjectId++, "Circle", boss.Position, boss.Position,
            1500, condition == "due-warning" ? state.Tick : state.Tick + 20, "campaign.test_warning", boss.Id, 10, DamageFamily.Void, "", state.NextActionId++));
        if (condition == "pending") boss.Pending = new("campaign.rush", 1, state.Actors[0].Position, 20, state.NextActionId++);
        if (condition == "guarded") state.Campaign!.Actors[boss.Id].GuardedUntil = 50;
        if (condition == "dead") { boss.Health = 0; boss.DeathProcessed = true; }
        if (condition == "expired") boss.RecoveryUntil = 0;
        if (condition == "copy") state.Campaign!.Actors[boss.Id] = state.Campaign.Actors[boss.Id] with { Modifiers = [], IsEcho = true, ExpiresTick = 180 };
        var session = CombatSession.Restore(content.CombatJson, state); string hash = session.StateHash;
        var view = session.View.Actors.Single(a => a.Id == boss.Id);
        bool open = condition == "open" || condition == "shielded" && encounter == "campaign.covenant_warden";
        Assert.Equal(open ? 90 : 0, view.BossRecoveryTicks);
        Assert.Equal(condition == "guarded" && encounter == "campaign.covenant_warden" ? 50 : 0, view.BossGuardedTicks);
        Assert.Equal(hash, session.StateHash); Assert.Equal(hash, CombatSession.Restore(content.CombatJson, session.Capture()).StateHash);
        Assert.All(session.View.Actors.Where(a => a.Id != boss.Id), a => Assert.Equal(0, a.BossRecoveryTicks));
    }

    [Theory]
    [InlineData("campaign.covenant_warden", 1)]
    [InlineData("campaign.covenant_warden", 2)]
    [InlineData("campaign.breach_heart", 1)]
    [InlineData("campaign.breach_heart", 2)]
    [InlineData("campaign.breach_heart", 3)]
    public void ActualBossSequenceKeepsStableOrderCountdownsAndRecoveryThroughSaveReplay(string encounter, int phase)
    {
        var content = Live(); var state = content.CreateEncounter(encounter).Capture(); var boss = state.Actors[1];
        state.Actors[0].InvulnerableUntil = 1000; state.Campaign!.NextHazardTick = 0; state.Campaign.BossPhase = phase;
        state.Campaign.Actors[boss.Id] = state.Campaign.Actors[boss.Id] with { Modifiers = [] };
        foreach (var actor in state.Actors.Skip(2)) { actor.Health = 0; actor.DeathProcessed = true; }
        var session = CombatSession.Restore(content.CombatJson, state); var recorder = new CombatRecorder(session);
        var expected = encounter == "campaign.covenant_warden" ? new[] { ("campaign.oath_mark", 36), ("campaign.covenant_fault", 54) } : phase switch
        {
            1 => [("campaign.breach_echo", 30)],
            2 => [("campaign.breach_echo", 30), ("campaign.returning_echo", 70)],
            _ => new[] { ("campaign.breach_echo", 30), ("campaign.seal_sweep", 50), ("campaign.returning_echo", 70) }
        };
        long startTick = -1; int deadlineViews = 0, openings = 0; HashSet<string> resolved = [];
        var other = CombatSession.Restore(content.CombatJson, state);
        for (int tick = 0; tick < 85; tick++)
        {
            var events = recorder.Step(session); Assert.Equal(JsonData.Hash(events), JsonData.Hash(other.Step()));
            if (events.Any(e => e.Kind == "BossPatternStarted")) startTick = events.Single(e => e.Kind == "BossPatternStarted").Tick;
            foreach (var e in events.Where(e => e.Kind == "CampaignHazardResolved")) resolved.Add(e.ContentId);
            var warnings = session.View.CampaignHazards!.Where(h => h.SourceId == boss.Id).ToArray();
            var bossView = session.View.Actors.Single(a => a.Id == boss.Id);
            if (warnings.Length > 0) Assert.Equal(0, bossView.BossRecoveryTicks);
            foreach (var warning in warnings)
            {
                int index = Array.FindIndex(expected, e => e.Item1 == warning.ContentId);
                Assert.True(index >= 0, warning.ContentId); Assert.Equal(index + 1, warning.SequenceIndex); Assert.Equal(expected.Length, warning.SequenceCount);
                Assert.Equal(Math.Max(0, startTick + expected[index].Item2 - session.Tick), warning.RemainingTicks);
                if (warning.RemainingTicks == 0) deadlineViews++;
            }
            if (bossView.BossRecoveryTicks > 0)
            {
                openings++; Assert.Empty(warnings); Assert.False(bossView.Guarded); Assert.False(bossView.Shielded);
                Assert.Equal(session.Capture().Actors[1].RecoveryUntil - session.Tick, bossView.BossRecoveryTicks);
            }
            if (tick % 11 == 0) other = CombatSession.Restore(content.CombatJson, other.Capture());
            Assert.Equal(session.StateHash, other.StateHash);
        }
        Assert.True(startTick >= 0); Assert.Equal(expected.Length, resolved.Count); Assert.True(openings > 0);
        Assert.Equal(expected.Length, deadlineViews);
        var replay = CombatReplayRunner.Run(content.CombatJson, recorder.Capture()); Assert.True(replay.Success, replay.Detail);
        Assert.Equal(session.StateHash, replay.FinalHash);
    }

    [Theory]
    [InlineData("campaign.covenant_warden", 1)]
    [InlineData("campaign.breach_heart", 1)]
    [InlineData("campaign.breach_heart", 2)]
    public void DamageDrivenPhaseTransitionCancelsOldSequenceAndNumbersTheNextSequence(string encounter, int phase)
    {
        var content = Live(); var state = content.CreateEncounter(encounter).Capture(); var boss = state.Actors[1];
        state.Actors[0].InvulnerableUntil = 1000; state.Actors[0].Position = new(boss.Position.X - 1000, boss.Position.Z);
        state.Campaign!.NextHazardTick = 0; state.Campaign.BossPhase = phase;
        state.Campaign.Actors[boss.Id] = state.Campaign.Actors[boss.Id] with { Modifiers = [] };
        foreach (var actor in state.Actors.Skip(2)) { actor.Health = 0; actor.DeathProcessed = true; }
        state.Fragments.Clear(); state.Equipment.Clear(); state.Momentum = 100; boss.Health = 1;
        var session = CombatSession.Restore(content.CombatJson, state); Advance(session, 2);
        var oldIds = session.View.CampaignHazards!.Where(h => h.SourceId == boss.Id).Select(h => h.Id).ToArray(); Assert.NotEmpty(oldIds);
        var recorder = new CombatRecorder(session);
        var events = recorder.Step(session, [new(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: boss.Id)]).ToList();
        for (int tick = 0; tick < 25 && !events.Any(e => e.Kind == "BossPhaseChanged"); tick++) events.AddRange(recorder.Step(session));
        Assert.Equal(phase + 1, Assert.Single(events, e => e.Kind == "BossPhaseChanged").Amount);
        Assert.DoesNotContain(session.View.CampaignHazards!, h => oldIds.Contains(h.Id));
        Assert.Equal(session.StateHash, CombatSession.Restore(content.CombatJson, session.Capture()).StateHash);
        for (int tick = 0; tick < 70 && !session.View.CampaignHazards!.Any(h => h.SourceId == boss.Id); tick++) recorder.Step(session);
        var next = session.View.CampaignHazards!.Where(h => h.SourceId == boss.Id).ToArray();
        int count = encounter == "campaign.covenant_warden" ? 2 : phase + 1;
        Assert.Equal(count, next.Length); Assert.All(next, h => Assert.Equal(count, h.SequenceCount));
        Assert.Equal(Enumerable.Range(1, count), next.Select(h => h.SequenceIndex).Order());
        Assert.DoesNotContain(next, h => oldIds.Contains(h.Id)); Assert.Equal(0, session.View.Actors[1].BossRecoveryTicks);
        var replay = CombatReplayRunner.Run(content.CombatJson, recorder.Capture()); Assert.True(replay.Success, replay.Detail);
        Assert.Equal(session.StateHash, replay.FinalHash);
    }

    public static IEnumerable<object[]> LateEncounters() =>
        from discipline in CombatSession.Disciplines
        from entry in new[] { ("campaign.bone_causeway", 10), ("campaign.contract_hall", 11), ("campaign.covenant_warden", 12),
            ("campaign.repeating_rooms", 13), ("campaign.identity_memory", 14), ("campaign.breach_heart", 15) }
        select new object[] { discipline, entry.Item1, entry.Item2 };

    [Theory, MemberData(nameof(LateEncounters))]
    public void AllFiveDisciplinesClearActualLateCampaignEncountersAndReplay(string discipline, string encounter, int level)
    {
        var content = Live(); var hub = content.CreateEncounter("hub"); hub.ApplyProgressionBuild(LegalBuild(discipline, level));
        var session = content.CreateEncounter(encounter, previous: hub.Capture()); var recorder = new CombatRecorder(session);
        for (int tick = 0; tick < 7500 && session.View.Actors[0].Health > 0 && session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0); tick++)
            recorder.Step(session, CampaignCombatSmoke.Commands(session.View, session.Room));
        output.WriteLine($"{discipline} {encounter}: tick={session.Tick}, health={session.View.Actors[0].Health}, hash={session.StateHash}");
        Assert.True(session.View.Actors[0].Health > 0, $"{discipline} died in {encounter} at {session.Tick}");
        Assert.DoesNotContain(session.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0); Assert.Empty(session.View.CampaignHazards!);
        var replay = CombatReplayRunner.Run(content.CombatJson, recorder.Capture()); Assert.True(replay.Success, replay.Detail);
        Assert.Equal(session.StateHash, replay.FinalHash);
    }

    private static CombatProgressionBuild LegalBuild(string discipline, int level)
    {
        var content = ProgressionContent.Parse(Read("progression.json")); var progression = ProgressionSession.Create(content, discipline);
        Assert.True(progression.EarnExperience("fixture.level", (int)progression.ExperienceForLevel(level)).Success);
        for (int i = 0; i < level - 1; i++) Assert.True(progression.AllocatePassive("fixture.passive." + i, i % 2 == 0 ? "Offense" : "Defense").Success);
        Assert.Equal(progression.StateHash, ProgressionSession.Restore(content, progression.Capture()).StateHash);
        return new(Discipline: discipline, Level: progression.Level, Offense: progression.View.Stats.GetValueOrDefault("passive.Offense"),
            Defense: progression.View.Stats.GetValueOrDefault("passive.Defense"), UltimateUnlocked: false, UnlockedMutations: []);
    }
}
