using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class EndgameReadabilityTests
{
    private static string Source(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));
    private static readonly Lazy<EndgameCombatContent> Content = new(() => EndgameCombatContent.Parse(
        CampaignCombatContent.Parse(Source("combat.json"), Source("campaign-combat.json")).CombatJson,
        Source("endgame-combat.json"), EndgameContent.Parse(Source("endgame.json"))));
    private static EndgameCombatContent Catalog => Content.Value;

    // Detached fixtures isolate cues without changing shipping combat definitions.
    // Earned campaign-to-endgame balance is verified separately.
    private static CombatSession Hunt(string id, int phase, bool quiet = true)
    {
        var body = CombatSession.CreateEncounter(Catalog.CombatJson, 42, "hub");
        body.ApplyProgressionBuild(new(Level: 20, Offense: 10, Defense: 8));
        var session = Catalog.CreateEncounter(Catalog.CreateHuntManifest(id, 42, 1), phase, 0, body.Capture());
        var state = session.Capture(); state.Fragments.Clear(); state.Equipment.Clear();
        state.Actors[0].InvulnerableUntil = 3000;
        if (quiet) foreach (var actor in state.Actors.Skip(1)) actor.RecoveryUntil = 3000;
        return CombatSession.Restore(Catalog.CombatJson, state);
    }

    private static void Advance(CombatSession session, int ticks)
    { for (int i = 0; i < ticks; i++) session.Step(); }

    private static EndgameBossCueView Cue(CombatSession session) => Assert.IsType<EndgameBossCueView>(session.View.Endgame!.BossCue);

    public static IEnumerable<object[]> InitialPhases()
    {
        yield return ["hunt.false_vael", 0, false, 0, 0];
        yield return ["hunt.false_vael", 1, true, 2, 0];
        yield return ["hunt.false_vael", 2, false, 0, 0];
        yield return ["hunt.ilyra_teeth", 0, false, 0, 0];
        yield return ["hunt.ilyra_teeth", 1, true, 2, 0];
        yield return ["hunt.ilyra_teeth", 2, true, 2, 0];
        yield return ["hunt.thousand_memories", 0, false, 0, 0];
        yield return ["hunt.thousand_memories", 1, true, 1, 0];
        yield return ["hunt.thousand_memories", 2, true, 0, 1];
        yield return ["hunt.orrun_without_oath", 0, false, 0, 0];
        yield return ["hunt.orrun_without_oath", 1, true, 0, 2];
        yield return ["hunt.orrun_without_oath", 2, true, 2, 0];
        yield return ["hunt.nhal_reconstruction", 0, true, 2, 0];
        yield return ["hunt.nhal_reconstruction", 1, true, 1, 0];
        yield return ["hunt.nhal_reconstruction", 2, true, 0, 1];
    }

    [Theory]
    [MemberData(nameof(InitialPhases))]
    public void Every_hunt_phase_projects_actual_immunity_and_only_relevant_priority_targets(string hunt, int phase, bool shielded, int actors, int mechanisms)
    {
        var session = Hunt(hunt, phase); var cue = Cue(session);
        Assert.Equal(shielded, cue.Shielded); Assert.Equal(actors, cue.PriorityActorIds.Length);
        Assert.Equal(mechanisms, cue.PriorityMechanismIds.Length); Assert.False(cue.PermanentlyVulnerable);
        Assert.Equal(0, cue.VulnerableTicks);
        var state = session.Capture(); var boss = state.Actors.Single(a => a.Id == cue.BossId);
        state.Actors[0].Position = new(boss.Position.X - 1000, boss.Position.Z);
        session = CombatSession.Restore(Catalog.CombatJson, state);
        session.Step([new(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: boss.Id)]); Advance(session, 12);
        Assert.Equal(shielded, session.View.Actors.Single(a => a.Id == boss.Id).Health == boss.Health);
        Assert.DoesNotContain(cue.PriorityActorIds, id => state.Endgame!.ActorMechanics[id] == "FalseEcho");
        Assert.All(cue.PriorityMechanismIds, id => Assert.True(session.View.Endgame!.Mechanisms.Single(m => m.Id == id).Available));
        if (shielded) Assert.Equal(0, Cue(session).RecoveryTicks);
    }

    [Theory]
    [InlineData("hunt.orrun_without_oath", 0, "hunt.orrun.numbered_fault")]
    [InlineData("hunt.orrun_without_oath", 2, "hunt.orrun.oathless_slam")]
    [InlineData("hunt.nhal_reconstruction", 2, "hunt.nhal.remembered_rhythm")]
    public void Temporal_sequences_keep_original_ordinals_as_earlier_attacks_expire(string hunt, int phase, string contentId)
    {
        var session = Hunt(hunt, phase); var recorder = new CombatRecorder(session);
        for (int i = 0; i < 46; i++) recorder.Step(session, []);
        var first = session.View.Endgame!.Hazards.Where(h => h.ContentId == contentId).ToArray();
        Assert.Equal([1, 2, 3], first.Select(h => h.SequenceIndex));
        Assert.All(first, h => { Assert.Equal(3, h.SequenceCount); Assert.Equal(0, h.LaneIndex); });
        var ordered = session.Capture().Endgame!.Hazards.Where(h => h.ContentId == contentId).OrderBy(h => h.Sequence).ToArray();
        Assert.True(ordered[0].StartsTick < ordered[1].StartsTick && ordered[1].StartsTick < ordered[2].StartsTick);
        while (session.Tick <= ordered[0].EndsTick) recorder.Step(session, []);
        var remaining = session.View.Endgame!.Hazards.Where(h => first.Any(original => original.Id == h.Id)).ToArray();
        Assert.Equal([2, 3], remaining.Select(h => h.SequenceIndex)); Assert.All(remaining, h => Assert.Equal(3, h.SequenceCount));
        VerifyStableReadAndRestore(session);
        Assert.True(CombatReplayRunner.Run(Catalog.CombatJson, recorder.Capture()).Success);
    }

    [Theory]
    [InlineData("hunt.thousand_memories", 0, "hunt.serath.procession")]
    [InlineData("hunt.thousand_memories", 2, "hunt.serath.bell_pulse")]
    [InlineData("hunt.nhal_reconstruction", 0, "hunt.nhal.absent_lane")]
    public void Simultaneous_lanes_are_not_presented_as_temporal_attack_sequences(string hunt, int phase, string contentId)
    {
        var session = Hunt(hunt, phase); Advance(session, 46);
        var hazards = session.View.Endgame!.Hazards.Where(h => h.ContentId == contentId).ToArray();
        Assert.Equal(2, hazards.Length); Assert.All(hazards, h => { Assert.InRange(h.LaneIndex, 1, 3); Assert.Equal(0, h.SequenceIndex); Assert.Equal(0, h.SequenceCount); });
        Assert.Single(session.Capture().Endgame!.Hazards.Where(h => h.ContentId == contentId).Select(h => h.StartsTick).Distinct());
        VerifyStableReadAndRestore(session);
    }

    private static CombatSession KillWeakPoint(CombatSession session, int targetId)
    {
        var state = session.Capture(); var target = state.Actors.Single(a => a.Id == targetId); target.Health = 1;
        var player = state.Actors[0]; player.Position = new(target.Position.X - 1000, target.Position.Z);
        player.RecoveryUntil = state.Tick; player.Pending = null;
        session = CombatSession.Restore(Catalog.CombatJson, state); var recorder = new CombatRecorder(session);
        var events = recorder.Step(session, [new(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: targetId)]).ToList();
        for (int i = 0; i < 12; i++) events.AddRange(recorder.Step(session, []));
        Assert.Contains(events, e => e.Kind == "HuntWeakPointBroken" && e.TargetId == targetId);
        Assert.Equal(0, session.View.Actors.Single(a => a.Id == targetId).Health);
        Assert.True(CombatReplayRunner.Run(Catalog.CombatJson, recorder.Capture()).Success);
        return session;
    }

    [Theory]
    [InlineData("hunt.false_vael", 1)]
    [InlineData("hunt.ilyra_teeth", 1)]
    [InlineData("hunt.ilyra_teeth", 2)]
    [InlineData("hunt.orrun_without_oath", 2)]
    [InlineData("hunt.nhal_reconstruction", 0)]
    public void One_broken_weak_point_opens_a_real_window_that_recloses_while_another_survives(string hunt, int phase)
    {
        var session = Hunt(hunt, phase); int[] ids = Cue(session).PriorityActorIds;
        session = KillWeakPoint(session, ids[0]); var cue = Cue(session);
        Assert.False(cue.Shielded); Assert.InRange(cue.VulnerableTicks, 1, 150); Assert.False(cue.PermanentlyVulnerable);
        Assert.Empty(cue.PriorityActorIds); VerifyStableReadAndRestore(session);
        for (int i = 0; i < 200 && !Cue(session).Shielded; i++) session.Step();
        cue = Cue(session); Assert.True(cue.Shielded); Assert.Equal(0, cue.VulnerableTicks);
        Assert.Equal([ids[1]], cue.PriorityActorIds); Assert.Equal(0, cue.RecoveryTicks);
        VerifyStableReadAndRestore(session);
    }

    [Theory]
    [InlineData("hunt.false_vael", 1)]
    [InlineData("hunt.ilyra_teeth", 1)]
    [InlineData("hunt.ilyra_teeth", 2)]
    [InlineData("hunt.orrun_without_oath", 2)]
    [InlineData("hunt.nhal_reconstruction", 0)]
    [InlineData("hunt.thousand_memories", 1)]
    [InlineData("hunt.nhal_reconstruction", 1)]
    public void Destroying_all_actual_weak_points_never_shows_a_false_reclosing_countdown(string hunt, int phase)
    {
        var session = Hunt(hunt, phase); int[] ids = Cue(session).PriorityActorIds;
        foreach (int id in ids) session = KillWeakPoint(session, id);
        var cue = Cue(session); Assert.False(cue.Shielded); Assert.True(cue.PermanentlyVulnerable);
        Assert.Equal(0, cue.VulnerableTicks); Assert.Empty(cue.PriorityActorIds);
        Assert.True(session.Capture().Endgame!.ExposedUntil > session.Tick);
        Advance(session, 185); cue = Cue(session);
        Assert.True(cue.PermanentlyVulnerable); Assert.False(cue.Shielded); Assert.Equal(0, cue.VulnerableTicks);
        VerifyStableReadAndRestore(session);
    }

    [Theory]
    [InlineData("hunt.thousand_memories")]
    [InlineData("hunt.nhal_reconstruction")]
    public void Bell_interaction_reopens_exactly_at_zero_and_save_preserves_the_window(string hunt)
    {
        var session = Hunt(hunt, 2); int bell = Assert.Single(Cue(session).PriorityMechanismIds);
        var state = session.Capture(); state.Actors[0].Position = state.Endgame!.Mechanisms.Single(m => m.Id == bell).Position;
        session = CombatSession.Restore(Catalog.CombatJson, state); var recorder = new CombatRecorder(session);
        Assert.Contains(recorder.Step(session, [new(CombatCommandKind.InteractMechanism, TargetId: bell)]), e => e.Kind == "HuntMechanismUsed");
        Assert.Equal(179, Cue(session).VulnerableTicks); Assert.False(Cue(session).Shielded); Assert.False(Cue(session).PermanentlyVulnerable);
        Assert.Empty(Cue(session).PriorityMechanismIds); VerifyStableReadAndRestore(session);
        while (Cue(session).VulnerableTicks > 1) recorder.Step(session, []);
        Assert.Equal(1, Cue(session).VulnerableTicks); recorder.Step(session, []);
        Assert.Equal(0, Cue(session).VulnerableTicks); Assert.True(Cue(session).Shielded); Assert.Equal([bell], Cue(session).PriorityMechanismIds);
        Assert.True(session.View.Endgame!.Mechanisms.Single(m => m.Id == bell).Available);
        Assert.True(CombatReplayRunner.Run(Catalog.CombatJson, recorder.Capture()).Success);
    }

    [Fact]
    public void Contract_priority_switches_from_terms_to_plinths_then_to_permanent_vulnerability()
    {
        var session = Hunt("hunt.orrun_without_oath", 1);
        Assert.All(session.View.Endgame!.Mechanisms, mechanism => Assert.False(mechanism.Used));
        foreach (int id in new[] { 1000001, 1000003, 1000002, 1000004 })
        {
            Assert.Contains(id, Cue(session).PriorityMechanismIds);
            var state = session.Capture(); state.Actors[0].Position = state.Endgame!.Mechanisms.Single(m => m.Id == id).Position;
            session = CombatSession.Restore(Catalog.CombatJson, state); var recorder = new CombatRecorder(session);
            Assert.Contains(recorder.Step(session, [new(CombatCommandKind.InteractMechanism, TargetId: id)]), e => e.Kind == "HuntMechanismUsed");
            Assert.True(session.View.Endgame!.Mechanisms.Single(m => m.Id == id).Used);
            Assert.All(session.View.Endgame.Mechanisms.Where(m => !m.Used), m => Assert.DoesNotContain("\"used\"", JsonData.Write(m)));
            Assert.DoesNotContain(id, Cue(session).PriorityMechanismIds);
            Assert.All(Cue(session).PriorityMechanismIds, next => Assert.Equal(id < 1000003 ? "OathPlinth" : "BrokenTerm", session.View.Endgame!.Mechanisms.Single(m => m.Id == next).Kind));
            VerifyStableReadAndRestore(session); Assert.True(CombatReplayRunner.Run(Catalog.CombatJson, recorder.Capture()).Success);
        }
        Assert.False(Cue(session).Shielded); Assert.True(Cue(session).PermanentlyVulnerable);
        Assert.Equal(0, Cue(session).VulnerableTicks); Assert.Empty(Cue(session).PriorityMechanismIds);
    }

    [Theory]
    [InlineData("ready", 45)]
    [InlineData("expired", 0)]
    [InlineData("guard", 0)]
    [InlineData("pending", 0)]
    [InlineData("endgame-warning", 0)]
    [InlineData("campaign-warning", 0)]
    [InlineData("invulnerable", 0)]
    public void Recovery_never_hides_pending_attacks_defense_or_an_earlier_arena_pattern(string blocker, int expected)
    {
        var session = Hunt("hunt.orrun_without_oath", 0); var state = session.Capture();
        var boss = state.Actors.Single(a => a.Id == Cue(session).BossId); boss.RecoveryUntil = blocker == "expired" ? 0 : 150;
        if (blocker == "guard") state.Campaign!.Actors[boss.Id].GuardedUntil = 30;
        if (blocker == "invulnerable") boss.InvulnerableUntil = 30;
        if (blocker == "pending") boss.Pending = new("endgame.boss_tell", 1, state.Actors[0].Position, 20, state.NextActionId++);
        if (blocker == "endgame-warning") state.Endgame!.Hazards.Add(new(state.NextObjectId++, "Circle", boss.Position, boss.Position, 1000, 20, 21, 20,
            "hunt.orrun.numbered_fault", boss.Id, 18, DamageFamily.PhysicalCrush, "", state.NextActionId++, 1));
        if (blocker == "campaign-warning") state.Campaign!.Hazards.Add(new(state.NextObjectId++, "Circle", boss.Position, boss.Position, 1000, 20,
            "elite.riftborn", boss.Id, 12, DamageFamily.Void, "", state.NextActionId++));
        session = CombatSession.Restore(Catalog.CombatJson, state); var cue = Cue(session);
        Assert.Equal(expected, cue.RecoveryTicks); Assert.Equal(blocker == "guard" ? 30 : 0, cue.GuardedTicks);
        VerifyStableReadAndRestore(session);
    }

    [Fact]
    public void Actual_ordinary_boss_strike_has_recovery_only_after_its_warning_has_finished()
    {
        var session = Hunt("hunt.false_vael", 0, quiet: false); var recorder = new CombatRecorder(session);
        recorder.Step(session, []); Assert.Equal(0, Cue(session).RecoveryTicks);
        bool observed = false;
        for (int i = 0; i < 120 && !observed; i++)
        {
            recorder.Step(session, []); var cue = Cue(session);
            if (cue.RecoveryTicks <= 0) continue;
            var state = session.Capture(); Assert.Null(state.Actors.Single(a => a.Id == cue.BossId).Pending);
            Assert.DoesNotContain(state.Endgame!.Hazards, h => h.SourceId == cue.BossId);
            Assert.True(session.Tick + cue.RecoveryTicks <= state.Endgame.NextPatternTick); observed = true;
        }
        Assert.True(observed); VerifyStableReadAndRestore(session);
        Assert.True(CombatReplayRunner.Run(Catalog.CombatJson, recorder.Capture()).Success);
    }

    [Fact]
    public void Last_visible_zero_tick_warning_still_blocks_recovery_until_cleanup()
    {
        var session = Hunt("hunt.false_vael", 0, quiet: false);
        Advance(session, 2);
        var first = Assert.Single(session.Capture().Endgame!.Hazards);
        while (session.Tick < first.EndsTick) session.Step();
        var warning = Assert.Single(session.View.Endgame!.Hazards, h => h.Id == first.Id);
        Assert.Equal(0, warning.RemainingTicks); Assert.Equal(0, Cue(session).RecoveryTicks);
        session.Step(); Assert.DoesNotContain(session.View.Endgame!.Hazards, h => h.Id == first.Id);
        Assert.True(Cue(session).RecoveryTicks > 0); VerifyStableReadAndRestore(session);
    }

    [Theory]
    [InlineData("boss-dead")]
    [InlineData("player-dead")]
    [InlineData("copy-only")]
    public void Dead_objectives_and_surviving_copies_do_not_offer_boss_openings(string reason)
    {
        var session = Hunt("hunt.false_vael", 0); var state = session.Capture(); var boss = state.Actors.Single(a => a.Id == Cue(session).BossId);
        if (reason == "player-dead") { state.Actors[0].Health = 0; state.Actors[0].DeathProcessed = true; }
        else if (reason == "copy-only") state.Campaign!.Actors[boss.Id] = state.Campaign.Actors[boss.Id] with { IsEcho = true, ExpiresTick = 180 };
        else { boss.Health = 0; boss.DeathProcessed = true; }
        session = CombatSession.Restore(Catalog.CombatJson, state); Assert.Null(session.View.Endgame!.BossCue);
        Assert.DoesNotContain("bossCue", JsonData.Write(session.View.Endgame));
    }

    [Theory]
    [InlineData("Approach", true, true, true)]
    [InlineData("Flee", true, true, true)]
    [InlineData("Reposition", true, true, true)]
    [InlineData("Windup", true, true, false)]
    [InlineData("Approach", false, true, false)]
    [InlineData("Approach", true, false, false)]
    public void Burning_haste_marks_only_living_currently_moving_eligible_enemies(string actorState, bool alive, bool burning, bool expected)
    {
        var manifest = Catalog.CreateFractureManifest(new(1, 42, "act.grey_march", 1, ["fracture.burning_haste"], "Vael", "Materials"), 1);
        var session = Catalog.CreateEncounter(manifest, 0, 0); var state = session.Capture();
        var actor = state.Actors.First(a => a.Faction == CombatFaction.Enemy); actor.State = actorState;
        if (!alive) { actor.Health = 0; actor.DeathProcessed = true; }
        if (burning) actor.Statuses.Add(new() { Id = "Burning", SourceId = 1, OwnerId = 1, NextTick = 5, ExpiresTick = 10, ActionId = state.NextActionId++ });
        session = CombatSession.Restore(Catalog.CombatJson, state);
        if (expected) Assert.Equal([actor.Id], Assert.IsType<int[]>(session.View.Endgame!.HastedActorIds));
        else Assert.Null(session.View.Endgame!.HastedActorIds);
        Assert.Null(session.View.Endgame!.BossCue); VerifyStableReadAndRestore(session);
        state.Tick = 10; session = CombatSession.Restore(Catalog.CombatJson, state); Assert.Null(session.View.Endgame!.HastedActorIds);
    }

    [Theory]
    [InlineData("Rooted", false, false)]
    [InlineData("Frozen", false, false)]
    [InlineData("Staggered", false, false)]
    [InlineData("Terrified", true, false)]
    [InlineData("Rooted", false, true)]
    [InlineData("Frozen", false, true)]
    [InlineData("Staggered", true, true)]
    public void Burning_haste_suppresses_blocked_movement_but_keeps_actual_fear_fleeing(string control, bool moves, bool alsoTerrified)
    {
        var manifest = Catalog.CreateFractureManifest(new(1, 42, "act.grey_march", 1, ["fracture.burning_haste"], "Vael", "Materials"), 1);
        var state = Catalog.CreateEncounter(manifest, 0, 0).Capture();
        var actor = state.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Role == "Melee");
        state.Actors[0].Position = new(-3500, 0); actor.Position = new(-1500, 0); actor.State = "Approach";
        actor.Statuses.Add(new() { Id = "Burning", SourceId = 1, OwnerId = 1, NextTick = 5, ExpiresTick = 40, ActionId = state.NextActionId++ });
        actor.Statuses.Add(new() { Id = control, SourceId = 1, OwnerId = 1, NextTick = 0, ExpiresTick = 20, ActionId = state.NextActionId++ });
        if (alsoTerrified) actor.Statuses.Add(new() { Id = "Terrified", SourceId = 1, OwnerId = 1, NextTick = 0, ExpiresTick = 20, ActionId = state.NextActionId++ });
        var session = CombatSession.Restore(Catalog.CombatJson, state); var recorder = new CombatRecorder(session);
        Assert.Equal(moves, session.View.Endgame!.HastedActorIds?.Contains(actor.Id) == true);
        recorder.Step(session, []);
        var current = session.View.Actors.Single(a => a.Id == actor.Id);
        Assert.Equal(moves, current.Position != actor.Position);
        Assert.Equal(moves, session.View.Endgame!.HastedActorIds?.Contains(actor.Id) == true);
        if (moves) Assert.Equal("Flee", current.State);
        VerifyStableReadAndRestore(session); Assert.True(CombatReplayRunner.Run(Catalog.CombatJson, recorder.Capture()).Success);
    }

    private static void VerifyStableReadAndRestore(CombatSession session)
    {
        string snapshot = JsonData.Write(session.Capture()), hash = session.StateHash;
        string cues = JsonData.Write(session.View.Endgame);
        Assert.Equal(cues, JsonData.Write(session.View.Endgame)); Assert.Equal(hash, session.StateHash);
        Assert.Equal(snapshot, JsonData.Write(session.Capture()));
        Assert.DoesNotContain("bossCue", snapshot); Assert.DoesNotContain("hastedActorIds", snapshot); Assert.DoesNotContain("sequenceCount", snapshot);
        var restored = CombatSession.Restore(Catalog.CombatJson, session.Capture());
        Assert.Equal(hash, restored.StateHash); Assert.Equal(cues, JsonData.Write(restored.View.Endgame));
    }
}
