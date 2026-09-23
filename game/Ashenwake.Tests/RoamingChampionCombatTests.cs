using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class RoamingChampionCombatTests
{
    private static readonly Lazy<string> Json = new(() => CampaignCombatContent.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json")),
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "campaign-combat.json"))).CombatJson);
    private static string Content => Json.Value;
    private static CombatSession Arena(string id, int cycle = 0)
    {
        var state = CombatSession.CreateEncounter(Content, 17, "championarena." + id).Capture();
        state.Fragments.Clear(); state.Equipment.Clear(); state.Momentum = 100;
        state.Actors[0].Position = new(-1000, 0); state.Actors[1].Position = new(1000, 0); state.Actors[1].SpecialCycle = cycle;
        return CombatSession.Restore(Content, state);
    }
    private static CombatEvent[] Advance(CombatSession session, int ticks)
    { List<CombatEvent> events = []; for (int i = 0; i < ticks; i++) events.AddRange(session.Step()); return events.ToArray(); }

    [Fact]
    public void BellWarningCanBeInterruptedAndDoesNotResolveAfterRestore()
    {
        var session = Arena("pilgrim", 1); session.Step();
        Assert.Contains(session.View.CampaignHazards!, h => h.ContentId == "campaign.champion_bell");
        var recorder = new CombatRecorder(session);
        recorder.Step(session, [new(CombatCommandKind.Cast, SkillId: "skill.shield_breaker", TargetId: session.View.Actors[1].Id)]);
        for (int i = 0; i < 16; i++) recorder.Step(session, []);
        Assert.DoesNotContain(session.View.CampaignHazards!, h => h.ContentId == "campaign.champion_bell");
        var restored = CombatSession.Restore(Content, session.Capture());
        Assert.DoesNotContain(Advance(restored, 40), e => e.Kind == "DamageApplied" && e.ContentId == "campaign.champion_bell");
        Assert.True(CombatReplayRunner.Run(Content, recorder.Capture()).Success);
    }

    [Fact]
    public void ChainWarnsFixedLaneThenPullsAndRootsHitPlayer()
    {
        var session = Arena("pilgrim"); var original = session.View.Actors[0].Position;
        session.Step(); var warning = Assert.Single(session.View.CampaignHazards!);
        Assert.Equal("Line", warning.Kind); Assert.Equal(original, warning.End);
        var events = Advance(session, 34);
        Assert.Contains(events, e => e.Kind == "ChampionChainPulled");
        Assert.True(session.View.Actors[0].Position.X > original.X);
        Assert.Contains(session.View.Actors[0].Statuses, s => s.Id == "Rooted");
    }

    [Theory]
    [InlineData(10200, 8200, 8200, 6200, true)]
    [InlineData(-10200, -8200, -8200, -6200, true)]
    [InlineData(10200, 0, 9300, 0, false)]
    [InlineData(-10200, 0, -9300, 0, false)]
    public void ChainNearArenaBoundaryKeepsActorRadiusAndCannotOverlapSource(int sourceX, int sourceZ, int playerX, int playerZ, bool canMove)
    {
        var state = Arena("pilgrim").Capture();
        var original = new Position(playerX, playerZ); var source = new Position(sourceX, sourceZ);
        state.Actors[0].Position = original; state.Actors[1].Position = source;
        var session = CombatSession.Restore(Content, state);
        var events = Advance(session, 35);
        Assert.Contains(events, e => e.Kind == "ChampionChainPulled");
        var position = session.View.Actors[0].Position;
        Assert.InRange(Math.Abs(position.X) + CombatSession.ActorRadius, 0, 10500);
        Assert.InRange(Math.Abs(position.Z) + CombatSession.ActorRadius, 0, 8500);
        Assert.True(Position.DistanceSquared(position, source) >= 4L * CombatSession.ActorRadius * CombatSession.ActorRadius);
        if (canMove) Assert.True(Position.DistanceSquared(position, source) < Position.DistanceSquared(original, source));
        else Assert.Equal(original, position);
        Assert.Equal(session.StateHash, CombatSession.Restore(Content, session.Capture()).StateHash);
    }

    [Fact]
    public void MovingClearOfChainAvoidsDamageAndPull()
    {
        var session = Arena("pilgrim"); session.Step();
        session.Step([new(CombatCommandKind.Move, Z: 1)]);
        var events = Advance(session, 34);
        Assert.DoesNotContain(events, e => e.Kind == "ChampionChainPulled");
        Assert.DoesNotContain(events, e => e.Kind == "DamageApplied" && e.ContentId == "campaign.champion_chain");
    }

    [Fact]
    public void DestroyingNestCancelsItsPoisonAndFutureWarnings()
    {
        var session = Arena("rootwidow"); session.Step();
        var state = session.Capture(); var nest = state.Actors.First(a => a.DefinitionId == "enemy.feeding_root");
        Assert.Contains(state.Campaign!.Hazards, h => h.SourceId == nest.Id && h.ContentId == "campaign.champion_poison");
        // A one-health wounded nest is killed by a real player-owned poison tick.
        nest.Health = 1;
        nest.Statuses.Add(new() { Id = "Poisoned", SourceId = 1, OwnerId = 1, OriginSkill = "skill.venom_knife", ActionId = state.NextActionId++, NextTick = state.Tick, ExpiresTick = state.Tick + 90 });
        session = CombatSession.Restore(Content, state); session.Step();
        Assert.Equal(0, session.View.Actors.Single(a => a.Id == nest.Id).Health);
        Assert.DoesNotContain(session.View.CampaignHazards!, h => h.SourceId == nest.Id);
        Assert.DoesNotContain(Advance(session, 120), e => e.Kind == "CampaignHazardWarned" && e.ActorId == nest.Id);
        Assert.Empty(session.View.Loot);
    }

    [Fact]
    public void PoisonNestTargetsPriorPositionAndSurvivesRoundTrip()
    {
        var session = Arena("rootwidow"); Advance(session, 31);
        var poisons = session.View.CampaignHazards!.Where(h => h.ContentId == "campaign.champion_poison").ToArray();
        Assert.Equal(3, poisons.Length); Assert.All(poisons, h => Assert.Equal(new Position(-1000, 0), h.Position));
        var restored = CombatSession.Restore(Content, session.Capture());
        Assert.Equal(session.StateHash, restored.StateHash);
        var events = Advance(restored, 37);
        Assert.Contains(events, e => e.Kind == "DamageApplied" && e.ContentId == "campaign.champion_poison");
        Assert.Contains(restored.View.Actors[0].Statuses, s => s.Id == "Poisoned");
    }

    private static int DirectDamage(CombatSession session)
    {
        int target = session.View.Actors[1].Id;
        var events = session.Step([new(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: target)]).ToList();
        events.AddRange(Advance(session, 12));
        return events.Where(e => e.Kind == "DamageApplied" && e.TargetId == target && e.ContentId == "skill.cleave").Sum(e => e.Amount);
    }

    [Fact]
    public void FurnaceHasActualGuardAndTimedDamageVulnerability()
    {
        var guarded = Arena("tithekeeper"); guarded.Step();
        Assert.True(guarded.View.Actors[1].Guarded); Assert.True(guarded.View.Actors[1].BossGuardedTicks > 0);
        int resisted = DirectDamage(guarded); Assert.True(resisted > 0);
        var open = Arena("tithekeeper"); Advance(open, 47);
        Assert.False(open.View.Actors[1].Guarded); Assert.True(open.View.Actors[1].BossRecoveryTicks > 0);
        int exposed = DirectDamage(open); Assert.True(exposed > resisted * 2, $"Guarded={resisted}, exposed={exposed}");
        Advance(open, 50); Assert.True(open.View.Actors[1].Guarded);
        Assert.Equal(open.StateHash, CombatSession.Restore(Content, open.Capture()).StateHash);
    }

    [Theory]
    [InlineData("damage")]
    [InlineData("source")]
    [InlineData("kind")]
    public void CorruptChampionHazardCannotChangeItsMechanicOnRestore(string field)
    {
        var session = Arena("rootwidow"); session.Step(); var state = session.Capture();
        int index = state.Campaign!.Hazards.FindIndex(h => h.ContentId == "campaign.champion_poison");
        var hazard = state.Campaign.Hazards[index];
        state.Campaign.Hazards[index] = field switch
        {
            "damage" => hazard with { Damage = 999 },
            "source" => hazard with { SourceId = state.Actors[1].Id },
            _ => hazard with { Kind = "Line" }
        };
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content, state));
    }

    [Theory]
    [InlineData("pilgrim")]
    [InlineData("rootwidow")]
    [InlineData("tithekeeper")]
    public void ArenaReplayIncludesMechanicsAndNoGenericLoot(string id)
    {
        var session = Arena(id); var recorder = new CombatRecorder(session);
        for (int i = 0; i < 140; i++) recorder.Step(session, []);
        Assert.True(CombatReplayRunner.Run(Content, recorder.Capture()).Success);
        Assert.Equal(session.StateHash, CombatSession.Restore(Content, session.Capture()).StateHash);
        Assert.Empty(session.View.Loot);
    }
}
