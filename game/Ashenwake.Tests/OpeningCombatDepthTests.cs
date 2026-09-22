using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class OpeningCombatDepthTests
{
    private static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));
    private static CampaignCombatContent Live() => CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json"));

    // Isolated geometry fixtures use the shipping actor definitions, skill pipeline,
    // elite scheduler and public restore/step APIs. Actual opening balance is below.
    private static (CampaignCombatContent Content, CombatSnapshot State) Fixture(bool wall = false, string allyDefinition = "enemy.memory_archer")
    {
        var data = JsonData.Read<CampaignCombatDefinition>(Read("campaign-combat.json"));
        var room = new RoomDefinition(12000, 10000, new(-2000, 0), new(0, 0),
            wall ? [new(500, 500, 1300, 2200)] : []);
        var encounter = data.Encounters.Single(e => e.Id == "campaign.monastery") with
        {
            Room = room,
            Spawns = [new("enemy.funeral_guard", new(0, 0), ["Dirgebound"]),
                new(allyDefinition, new(1800, 1400), []),
                new("enemy.memory_archer", new(3600, 0), []),
                new("enemy.funeral_guard", new(0, -1500), [])]
        };
        data = data with { Encounters = data.Encounters.Select(e => e.Id == encounter.Id ? encounter : e).ToArray() };
        var content = CampaignCombatContent.Parse(Read("combat.json"), JsonData.Write(data));
        var state = content.CreateEncounter(encounter.Id).Capture();
        state.Campaign!.NextHazardTick = 150;
        state.Campaign.Actors[state.Actors[1].Id].NextEliteTick = 0;
        state.Actors[0].InvulnerableUntil = 1000;
        state.Momentum = 100;
        foreach (var actor in state.Actors.Skip(2)) actor.RecoveryUntil = 1000;
        return (content, state);
    }

    private static CombatHazardView Start(CombatSession session)
    {
        var events = session.Step();
        var warning = Assert.Single(session.View.CampaignHazards!, h => h.ContentId == "elite.dirgebound");
        Assert.Equal("Circle", warning.Kind); Assert.Equal(3000, warning.Radius);
        Assert.Equal(session.View.Actors[1].Position, warning.Position); Assert.Equal(warning.Position, warning.End);
        Assert.Equal(36, Assert.Single(events, e => e.Kind == "CampaignHazardWarned" && e.ContentId == "elite.dirgebound").Amount);
        Assert.Equal(35, warning.RemainingTicks); // view is projected after the scheduling tick advanced
        return warning;
    }

    [Theory]
    [InlineData(0, 24, 24)]
    [InlineData(12, 24, 12)]
    [InlineData(24, 24, 0)]
    [InlineData(70, 70, 0)]
    public void DirgeCapsAllyBarrierWithoutStackingAndNeverWardsItsCaster(int initial, int expected, int increase)
    {
        var (content, state) = Fixture(); state.Actors[2].Barrier = initial;
        var session = CombatSession.Restore(content.CombatJson, state);
        var warning = Start(session); var events = new List<CombatEvent>();
        for (int i = 0; i < 36; i++) events.AddRange(session.Step());
        Assert.Equal(expected, session.View.Actors[2].Barrier);
        Assert.Equal(0, session.View.Actors[1].Barrier);
        Assert.Equal(0, session.View.Actors[3].Barrier);
        Assert.Equal(24, session.View.Actors[4].Barrier);
        Assert.DoesNotContain(events, e => e.Kind == "DamageApplied" && e.ContentId == "elite.dirgebound");
        Assert.Equal(increase, events.Where(e => e.Kind == "BarrierGranted" && e.TargetId == state.Actors[2].Id && e.ContentId == "elite.dirgebound").Sum(e => e.Amount));
        Assert.DoesNotContain(session.View.CampaignHazards!, h => h.Id == warning.Id);
        // Repeating the real scheduler must not grow the barrier beyond its cap.
        for (int i = 0; i < 260; i++) events.AddRange(session.Step());
        Assert.Equal(expected, session.View.Actors[2].Barrier);
        Assert.Equal(24, session.View.Actors[4].Barrier);
        Assert.Equal(increase, events.Where(e => e.Kind == "BarrierGranted" && e.TargetId == state.Actors[2].Id && e.ContentId == "elite.dirgebound").Sum(e => e.Amount));
    }

    [Fact]
    public void RallyUsesFixedCircleAndLiveAllyRangeAndLineOfSightAtResolution()
    {
        var (content, state) = Fixture(wall: true);
        var blocked = CombatSession.Restore(content.CombatJson, state); Start(blocked);
        for (int i = 0; i < 36; i++) blocked.Step();
        Assert.Equal(0, blocked.View.Actors[2].Barrier); // in circle, behind actual obstacle
        Assert.Equal(24, blocked.View.Actors[4].Barrier);

        (content, state) = Fixture(); var original = CombatSession.Restore(content.CombatJson, state);
        var warning = Start(original); var shifted = original.Capture();
        shifted.Actors[1].Position = new(-1200, 0);
        shifted.Actors[2].Position = new(3200, 1400); // leaves the warned area during chant
        shifted.Actors[3].Position = new(2900, 0); // enters the fixed circle, far from shifted caster
        var moving = CombatSession.Restore(content.CombatJson, shifted);
        Assert.Equal(warning.Position, Assert.Single(moving.View.CampaignHazards!).Position);
        for (int i = 0; i < 36; i++) moving.Step();
        Assert.Equal(0, moving.View.Actors[2].Barrier);
        Assert.Equal(24, moving.View.Actors[3].Barrier);
    }

    [Theory]
    [InlineData("boss.bell_saint", false)]
    [InlineData("enemy.ritual_anchor", false)]
    [InlineData("enemy.broken_bell", false)]
    [InlineData("enemy.memory_archer", true)]
    public void SupportWardExcludesBossesMechanismsAndEchoCopies(string definition, bool echo)
    {
        var (content, state) = Fixture(allyDefinition: definition);
        if (echo) state.Campaign!.Actors[state.Actors[2].Id] = state.Campaign.Actors[state.Actors[2].Id] with { IsEcho = true, ExpiresTick = 180 };
        var session = CombatSession.Restore(content.CombatJson, state); Start(session);
        for (int i = 0; i < 36; i++) session.Step();
        Assert.Equal(0, session.View.Actors[2].Barrier);
        Assert.Equal(24, session.View.Actors[4].Barrier);
    }

    [Theory]
    [InlineData("skill.shield_breaker", false)]
    [InlineData("skill.cleave", true)]
    public void ActualHardInterruptOrKillCancelsPendingDirge(string skill, bool kill)
    {
        var (content, state) = Fixture();
        if (kill) state.Actors[1].Health = 1;
        var session = CombatSession.Restore(content.CombatJson, state); var warning = Start(session);
        var events = session.Step([new(CombatCommandKind.Cast, SkillId: skill, TargetId: state.Actors[1].Id)]).ToList();
        for (int i = 0; i < 40; i++) events.AddRange(session.Step());
        Assert.Contains(events, e => kill ? e.Kind == "EntityKilled" && e.TargetId == state.Actors[1].Id :
            e.Kind == "StatusApplied" && e.ContentId == "Staggered" && e.TargetId == state.Actors[1].Id);
        Assert.DoesNotContain(events, e => e.Kind == "BarrierGranted" && e.ContentId == "elite.dirgebound");
        Assert.DoesNotContain(session.View.CampaignHazards!, h => h.Id == warning.Id);
        Assert.Null(session.Capture().Actors[1].Pending);
    }

    [Fact]
    public void DirgeRestoreAndRecordedCommandsMatchEveryWarningAndBarrierEvent()
    {
        var (content, state) = Fixture(); var session = CombatSession.Restore(content.CombatJson, state);
        var recorder = new CombatRecorder(session); var other = CombatSession.Restore(content.CombatJson, state);
        var warned = new List<long>();
        for (int tick = 0; tick < 300; tick++)
        {
            var events = recorder.Step(session);
            Assert.Equal(JsonData.Hash(events), JsonData.Hash(other.Step()));
            warned.AddRange(events.Where(e => e.Kind == "CampaignHazardWarned" && e.ContentId == "elite.dirgebound").Select(e => e.Tick));
            if (tick % 13 == 0) other = CombatSession.Restore(content.CombatJson, other.Capture());
            Assert.Equal(session.StateHash, other.StateHash);
        }
        Assert.Single(warned); // fully warded allies do not provoke empty repeat chants
        var replay = CombatReplayRunner.Run(content.CombatJson, recorder.Capture());
        Assert.True(replay.Success, replay.Detail); Assert.Equal(session.StateHash, replay.FinalHash);
    }

    [Fact]
    public void ConsumedAllyBarrierCannotBypassTheEliteCooldown()
    {
        var (content, state) = Fixture(); var session = CombatSession.Restore(content.CombatJson, state); Start(session);
        for (int i = 0; i < 36; i++) session.Step();
        state = session.Capture();
        long ready = state.Campaign!.Actors[state.Actors[1].Id].NextEliteTick;
        Assert.Equal(180, ready);
        state.Actors[2].Barrier = state.Actors[4].Barrier = 0;
        session = CombatSession.Restore(content.CombatJson, state);
        var recorder = new CombatRecorder(session); var warned = new List<CombatEvent>();
        for (int i = 0; i < 300 && warned.Count == 0; i++)
            warned.AddRange(recorder.Step(session).Where(e => e.Kind == "CampaignHazardWarned" && e.ContentId == "elite.dirgebound"));
        var renewed = Assert.Single(warned);
        Assert.True(renewed.Tick >= ready);
        var replay = CombatReplayRunner.Run(content.CombatJson, recorder.Capture());
        Assert.True(replay.Success, replay.Detail); Assert.Equal(session.StateHash, replay.FinalHash);
    }

    public static IEnumerable<object[]> EarlyEncounters() =>
        from discipline in new[] { "Vanguard", "Veilwalker", "Arcanist", "Gravecaller", "Warden" }
        from entry in new[] { ("campaign.road", 1), ("campaign.monastery", 1), ("campaign.bell_saint", 2), ("exploration.widow_crypt", 1) }
        select new object[] { discipline, entry.Item1, entry.Item2 };

    [Theory, MemberData(nameof(EarlyEncounters))]
    public void ActualOpeningEncountersRemainPlayableWithStarterBuilds(string discipline, string id, int level)
    {
        var content = Live(); var hub = content.CreateEncounter("hub");
        hub.ApplyProgressionBuild(new(Discipline: discipline, Level: level, UltimateUnlocked: false, UnlockedMutations: []));
        var session = content.CreateEncounter(id, previous: hub.Capture());
        var recorder = new CombatRecorder(session);
        for (int tick = 0; tick < 6500 && session.View.Actors[0].Health > 0 && session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0); tick++)
            recorder.Step(session, CampaignCombatSmoke.Commands(session.View, session.Room));
        Assert.True(session.View.Actors[0].Health > 0, $"{discipline} died in {id} at tick {session.Tick}");
        Assert.DoesNotContain(session.View.Actors, actor => actor.Faction == CombatFaction.Enemy && actor.Health > 0);
        Assert.Empty(session.View.CampaignHazards!);
        var replay = CombatReplayRunner.Run(content.CombatJson, recorder.Capture());
        Assert.True(replay.Success, replay.Detail); Assert.Equal(session.StateHash, replay.FinalHash);
    }

    [Fact]
    public void ArchivedPacingRegistryKeepsItsOriginalEightTraitsAndMirrorbornMonastery()
    {
        var old = CampaignCombatContent.Parse(Read("combat.json"), Read("fixtures/campaign-combat-pacing.json"));
        var definition = CombatContent.Parse(old.CombatJson).Campaign!;
        Assert.Equal(8, definition.Encounters.SelectMany(e => e.Spawns).SelectMany(s => s.Modifiers).Distinct().Count());
        Assert.Contains(definition.Encounters.Single(e => e.Id == "campaign.monastery").Spawns, spawn => spawn.Modifiers.Contains("Mirrorborn"));
        Assert.DoesNotContain(definition.Encounters.SelectMany(e => e.Spawns), spawn => spawn.Modifiers.Contains("Dirgebound"));
    }
}
