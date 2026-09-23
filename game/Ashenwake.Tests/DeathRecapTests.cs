using System.Collections;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class DeathRecapTests
{
    private static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file));
    private static readonly Lazy<string> Composed = new(() => EndgameCombatContent.Parse(
        CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), Endgame).CombatJson);
    private static string Json => Composed.Value;
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent Policy => ProgressionContent.Parse(Read("progression.json"));
    private static CampaignContent Campaign => CampaignContent.Parse(Read("campaign.json"));
    private static EndgameContent Endgame => EndgameContent.Parse(Read("endgame.json"));
    private static CampaignRuntimeSession Restore(CampaignRuntimeSnapshot snapshot) => CampaignRuntimeSession.Restore(Json, Adventure, Policy, Campaign, snapshot);
    private static EndgameRuntimeSession Restore(EndgameRuntimeSnapshot snapshot) => EndgameRuntimeSession.Restore(Json, Adventure, Policy, Campaign, Endgame, snapshot);
    private static CombatSnapshot Arena(int health = 30)
    {
        var state = CombatSession.Create(Json).Capture();
        Quiet(state, health);
        state.Fragments.Clear(); state.Equipment.Clear();
        return state;
    }
    private static void Quiet(CombatSnapshot state, int health)
    {
        var player = state.Actors.Single(a => a.Id == 1);
        player.Health = health; player.Barrier = 0;
        foreach (var actor in state.Actors.Where(a => a.Id != 1)) { actor.Pending = null; actor.RecoveryUntil = state.Tick + 3000; }
    }
    private static void Area(CombatSnapshot state, int damage, long tick = 0, long? expires = null, DamageFamily family = DamageFamily.Fire)
    {
        int source = state.Actors.First(a => a.Faction == CombatFaction.Enemy).Id;
        state.Areas.Add(new(state.NextObjectId++, source, source, state.Actors.Single(a => a.Id == 1).Position,
            1000, "enemy.projectile", damage, family, state.Tick + tick, state.Tick + (expires ?? tick + 1), state.NextActionId++, 0));
    }

    [Fact]
    public void ActualHealthLossExcludesAbsorptionAndOverkillAndSnapshotsAreUnaffected()
    {
        var state = Arena(); state.Actors.Single(a => a.Id == 1).Barrier = 20;
        Area(state, 10); Area(state, 15); Area(state, 100);
        var session = CombatSession.Restore(Json, state);
        var events = session.Step(); var recap = Assert.IsType<DeathRecap>(session.LastDeathRecap);
        Assert.Equal(new[] { 5, 25 }, recap.RecentDamage.Select(d => d.Damage));
        Assert.Equal(25, recap.KillingBlow.Damage); Assert.Equal(DamageFamily.Fire, recap.KillingBlow.Family);
        Assert.Equal("enemy.projectile", recap.KillingBlow.AttackId);
        Assert.Equal("enemy.ash_ghoul", recap.KillingBlow.SourceDefinitionId);
        Assert.Equal("Ash Ghoul", recap.KillingBlow.SourceName);
        Assert.Equal(recap.KillingBlow.SourceId, recap.KillingBlow.OwnerId);
        Assert.Equal(events.Where(e => e.Kind == "DamageApplied" && e.TargetId == 1).Sum(e => e.Amount), recap.RecentDamage.Sum(e => e.Damage));
        Assert.Equal(session.StateHash, CombatSession.Restore(Json, session.Capture()).StateHash);
        Assert.Null(CombatSession.Restore(Json, session.Capture()).LastDeathRecap);
        Assert.Throws<NotSupportedException>(() => ((IList)recap.RecentDamage).Clear());
        session.Step(); Assert.Same(recap, session.LastDeathRecap);
    }

    [Fact]
    public void FatalDotRetainsDeadSourceOriginAndActiveConditions()
    {
        var state = Arena(5); var source = state.Actors.First(a => a.Faction == CombatFaction.Enemy);
        source.Health = 0; source.DeathProcessed = true;
        var player = state.Actors.Single(a => a.Id == 1);
        player.Statuses.Add(new() { Id = "Burning", SourceId = source.Id, OwnerId = source.Id, OriginSkill = "enemy.detonate", ActionId = state.NextActionId++, ExpiresTick = 90, NextTick = 0 });
        player.Statuses.Add(new() { Id = "Rooted", SourceId = source.Id, OwnerId = source.Id, ActionId = state.NextActionId++, ExpiresTick = 30, NextTick = 30 });
        player.Statuses.Add(new() { Id = "Vulnerable", SourceId = source.Id, OwnerId = source.Id, ActionId = state.NextActionId++, ExpiresTick = 0, NextTick = 0 });
        var session = CombatSession.Restore(Json, state); session.Step();
        var recap = Assert.IsType<DeathRecap>(session.LastDeathRecap);
        Assert.True(recap.KillingBlow.DamageOverTime); Assert.Equal("Burning", recap.KillingBlow.EffectId);
        Assert.Equal("enemy.detonate", recap.KillingBlow.AttackId); Assert.Equal(source.DefinitionId, recap.KillingBlow.SourceDefinitionId);
        Assert.Equal(5, recap.KillingBlow.Damage); Assert.Equal(new[] { "Burning", "Rooted" }, recap.Conditions.Select(c => c.Id));
        Assert.Equal(30, recap.Conditions.Single(c => c.Id == "Rooted").RemainingTicks);
        Assert.Throws<NotSupportedException>(() => ((IList)recap.Conditions).Clear());
    }

    [Fact]
    public void InvulnerabilityDoesNotCreateDamageHistory()
    {
        var state = Arena(); state.Actors.Single(a => a.Id == 1).InvulnerableUntil = 1;
        Area(state, 999); Area(state, 999, tick: 1);
        var session = CombatSession.Restore(Json, state); session.Step(); Assert.Null(session.LastDeathRecap);
        session.Step(); var recap = Assert.IsType<DeathRecap>(session.LastDeathRecap);
        Assert.Equal(1, Assert.Single(recap.RecentDamage).Tick); Assert.Equal(30, recap.KillingBlow.Damage);
    }

    [Fact]
    public void RecentWindowDropsDamageOlderThanEightSeconds()
    {
        var state = Arena(); Area(state, 1); Area(state, 999, tick: 241);
        var session = CombatSession.Restore(Json, state);
        for (int i = 0; i <= 241; i++) session.Step();
        var recap = Assert.IsType<DeathRecap>(session.LastDeathRecap);
        Assert.Equal(241, Assert.Single(recap.RecentDamage).Tick); Assert.Equal(29, recap.KillingBlow.Damage);
    }

    [Fact]
    public void BurstHistoryIsBoundedAndRetainsTheExactFinalHit()
    {
        var state = Arena(90);
        for (int i = 0; i < 32; i++) Area(state, 1, expires: 61);
        var session = CombatSession.Restore(Json, state);
        for (int i = 0; i < 41; i++) session.Step();
        var recap = Assert.IsType<DeathRecap>(session.LastDeathRecap);
        Assert.Equal(CombatSession.DeathRecapMaximumHits, recap.RecentDamage.Count);
        Assert.Same(recap.RecentDamage[^1], recap.KillingBlow); Assert.Equal(40, recap.Tick);
    }

    [Fact]
    public void RestoredDeadSnapshotDoesNotFabricateARecapAndNewEncounterStartsEmpty()
    {
        var state = Arena(1); Area(state, 20); var session = CombatSession.Restore(Json, state); session.Step();
        Assert.NotNull(session.LastDeathRecap);
        Assert.Null(CombatSession.Restore(Json, session.Capture()).LastDeathRecap);
        var restarted = CombatSession.CreateEncounter(Json, state.Seed, "encounter.ossuary", session.Capture(), restoreAtAnchor: true);
        Assert.Null(restarted.LastDeathRecap);
    }

    [Fact]
    public void CampaignKeepsPreProjectionHistoryThroughRejectedCommandAndAnchorRecovery()
    {
        var session = CampaignRuntimeSession.Create(Json, Adventure, Policy, Campaign); Assert.True(session.EnterAct(1).Success);
        var state = session.Capture(); Quiet(state.Combat, 30); Area(state.Combat, 10); Area(state.Combat, 100, tick: 1);
        var enemy = state.Combat.Actors.First(a => a.Faction == CombatFaction.Enemy);
        enemy.Position = state.Combat.Actors.Single(a => a.Id == 1).Position;
        session = Restore(state); var before = session.Combat;
        var position = session.Combat.View.Actors.Single(a => a.Id == 1).Position;
        var first = session.Step(new CombatCommand(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: enemy.Id, X: position.X, Z: position.Z));
        Assert.Contains(first.CombatEvents, e => e.Kind == "AbilityStarted"); Assert.NotSame(before, session.Combat);
        Assert.Null(session.LastDeathRecap);
        string hash = session.StateHash; Assert.False(session.AdvanceEncounter().Success); Assert.Equal(hash, session.StateHash);
        var died = session.Step(); var recap = Assert.IsType<DeathRecap>(session.LastDeathRecap);
        Assert.Equal(new[] { 10, 20 }, recap.RecentDamage.Select(d => d.Damage));
        Assert.Equal("campaign.road", recap.RoomId);
        Assert.Contains("CampaignCombatRestoredAtAnchor", died.WorldEvents);
        Assert.True(session.Combat.View.Actors.Single(a => a.Id == 1).Health > 0); Assert.Null(session.Combat.LastDeathRecap);
        Assert.Same(recap, session.LastDeathRecap); Assert.False(session.AdvanceEncounter().Success); Assert.Same(recap, session.LastDeathRecap);
        Assert.Null(Restore(session.Capture()).LastDeathRecap);
        var replay = CampaignRuntimeReplayRunner.Run(Json, Adventure, Policy, Campaign, session.CaptureReplay()); Assert.True(replay.Success, replay.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EndgamePublishesCampaignDeathThroughBothTickRoutes(bool nested)
    {
        var session = EndgameRuntimeSession.Create(Json, Adventure, Policy, Campaign, Endgame);
        Assert.True(session.ExecuteCampaign(new(CampaignRuntimeAction.EnterAct, Act: 1)).Success);
        var state = session.Capture(); Quiet(state.Campaign.Combat, 5); Area(state.Campaign.Combat, 100);
        session = Restore(state);
        if (nested) Assert.True(session.ExecuteCampaign(new(CampaignRuntimeAction.Tick)).Success); else Assert.True(session.Step().Success);
        var recap = Assert.IsType<DeathRecap>(session.LastDeathRecap);
        Assert.Same(session.Campaign.LastDeathRecap, recap); Assert.Equal(5, recap.KillingBlow.Damage);
        Assert.False(session.RetryEncounter().Success); Assert.Same(recap, session.LastDeathRecap); Assert.Same(recap, session.Campaign.LastDeathRecap);
        Assert.Null(Restore(session.Capture()).LastDeathRecap);
    }

    [Fact]
    public void EndgameDeathRetryExhaustionAndReplayRetainOnlyTransientRecaps()
    {
        string previous = CampaignCombatContent.Parse(Read("fixtures/combat-phase4.json"), Read("fixtures/campaign-combat-phase4.json")).CombatJson;
        var session = EndgameRuntimeMigration.ImportPhaseFour(Read("fixtures/phase4-campaign-complete.json"), previous, Json, Adventure, Policy, Campaign, Endgame);
        AtGate(session, new(EndgameRuntimeAction.ClaimRecoverySigil));
        AtGate(session, new(EndgameRuntimeAction.StartFracture, session.View.AvailableSigils.Single().Id));
        for (int death = 1; death <= 3; death++)
        {
            DeathRecap? previousRecap = session.LastDeathRecap;
            for (int i = 0; i < 5000 && session.RunView!.Deaths < death; i++) Assert.True(session.Step().Success);
            var recap = Assert.IsType<DeathRecap>(session.LastDeathRecap);
            Assert.NotSame(previousRecap, recap); Assert.Equal(death, session.RunView!.Deaths);
            Assert.Equal(3 - death, session.RunView.AttemptsRemaining); Assert.True(recap.KillingBlow.Damage > 0);
            Assert.Equal(session.Combat.EncounterId, recap.RoomId);
            Assert.Null(Restore(session.Capture()).LastDeathRecap);
            Assert.False(session.StartGodHunt("hunt.false_vael").Success); Assert.Same(recap, session.LastDeathRecap);
            if (death < 3) { Assert.True(session.RetryEncounter().Success); Assert.Null(session.Combat.LastDeathRecap); Assert.Same(recap, session.LastDeathRecap); }
            else { Assert.False(session.RetryEncounter().Success); Assert.True(session.ReturnToHub().Success); Assert.Same(recap, session.LastDeathRecap); }
        }
        var replay = EndgameRuntimeReplayRunner.Run(Json, Adventure, Policy, Campaign, Endgame, session.CaptureReplay()); Assert.True(replay.Success, replay.ToString());
    }

    private static void AtGate(EndgameRuntimeSession session, EndgameRuntimeCommand command)
    {
        for (int i = 0; i < 500; i++)
        {
            var next = EndgameRuntimeSmoke.AtGate(session, command); var result = session.Execute(next); Assert.True(result.Success, result.Reason);
            if (next.Action != EndgameRuntimeAction.Tick) return;
        }
        Assert.Fail("Gate navigation failed.");
    }
}
