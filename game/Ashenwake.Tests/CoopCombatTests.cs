using System.Text.Json.Nodes;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Coop;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CoopCombatTests
{
    private static string Content => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json"));
    private static CoopCombatSession Fresh(ulong seed = 42, string encounter = "coop.ossuary") => CoopCombatSession.Create(Content, seed, encounter, "test-match");
    [Theory]
    [InlineData(42UL)]
    [InlineData(43UL)]
    [InlineData(44UL)]
    public void Two_ordinary_input_players_clear_the_actual_shared_slice_and_replay(ulong seed)
    {
        var session = Fresh(seed); var recorder = new CoopRecorder(session); var events = new List<CoopEvent>(); var seen = new HashSet<string>();
        for (int tick = 0; tick < CoopSmoke.MaximumTicks && !session.View.Completed; tick++)
        {
            var view = session.View; seen.Add(view.EncounterId);
            foreach (var player in view.Players) Assert.True(recorder.Submit(session, player.Id, CoopSmoke.Input(view, player.Id, player.AcceptedSequence + 1)).Accepted);
            events.AddRange(recorder.Step(session));
            if (tick % 137 == 0) Assert.Equal(session.StateHash, CoopCombatSession.Restore(Content, session.Capture()).StateHash);
        }
        Assert.True(session.View.Completed, $"Stalled {session.View.EncounterId}, tick{session.Tick}, actors:{JsonData.Write(session.View.Actors)}");
        Assert.Equal(5, seen.Count); Assert.DoesNotContain(events, e => e.Kind == "PartyWiped");
        Assert.Equal(10, session.View.Rewards.Length); Assert.All(session.View.Players, p => Assert.Equal(5, session.View.Rewards.Count(r => r.PlayerId == p.Id)));
        Assert.Contains(events, e => e.Kind == "CorpseResurrected");
        Assert.Contains(events, e => e.Kind == "AbilityStarted" && e.ContentId == "boss.chain");
        Assert.Contains(events, e => e.Kind == "AbilityStarted" && e.ContentId == "boss.sonic");
        Assert.Contains(events, e => e.Kind == "AbilityStarted" && e.ContentId == "boss.beast_rush");
        Assert.Contains(events, e => e.Kind == "AbilityStarted" && e.ContentId == "boss.bell_ring");
        Assert.True(session.View.Counters.PeakActors <= CoopCombatSession.MaxActors);
        Assert.True(session.View.Counters.PeakWarnings <= CoopCombatSession.MaxWarnings);
        Assert.True(session.View.Counters.PeakProjectiles <= CoopCombatSession.MaxProjectiles);
        Assert.True(CoopReplayRunner.Run(Content, recorder.Capture()).Success);
        string rewards = JsonData.Hash(session.View.Rewards); for (int i = 0; i < 10; i++) session.Step(); Assert.Equal(rewards, JsonData.Hash(session.View.Rewards));
    }
    [Fact]
    public void Inputs_never_accept_client_state_and_sequences_are_idempotent_and_bounded()
    {
        var session = Fresh(); var input = new CoopInput(1, 1, 1, 0);
        Assert.True(session.Submit(1, input).Accepted); string accepted = session.StateHash;
        Assert.Equal("duplicate", session.Submit(1, input).Code); Assert.Equal(accepted, session.StateHash);
        Assert.Equal("changed_duplicate", session.Submit(1, input with { MoveX = -1 }).Code); Assert.Equal(accepted, session.StateHash);
        Assert.False(session.Submit(3, input).Accepted);
        Assert.False(session.Submit(2, input with { MoveX = 2 }).Accepted);
        Assert.False(session.Submit(2, input with { ClientTick = 100 }).Accepted);
        Assert.False(session.SubmitJson(2, "{\"sequence\":1,\"clientTick\":1,\"actorId\":1,\"damage\":999}").Accepted);
        Assert.False(session.SubmitJson(2, "{\"sequence\":1}").Accepted);
        for (int i = 2; i <= 6; i++) Assert.True(session.Submit(1, new(i, i, 0, 1)).Accepted);
        Assert.False(session.Submit(1, new(7, 6)).Accepted);
        Assert.False(session.Submit(1, new(7, 2)).Accepted);
        Assert.True(session.View.Counters.QueuedInputs <= CoopCombatSession.MaxQueuedInputsPerPlayer * 2);
        for (int i = 0; i < 7; i++) session.Step();
        Assert.Equal(6, session.View.Players.Single(p => p.Id == 1).ProcessedSequence);
        Assert.False(session.Submit(1, new(1, session.Tick + 1)).Accepted);
    }
    [Fact]
    public void Disconnect_clears_queued_movement_and_reconnect_preserves_authoritative_state()
    {
        var session = Fresh(); var recorder = new CoopRecorder(session);
        recorder.Submit(session, 1, new(1, 1, 1, 0)); recorder.Submit(session, 1, new(2, 5, 0, 1)); recorder.Step(session);
        var before = session.View.Actors.Single(a => a.Id == 1); recorder.SetConnected(session, 1, false);
        Assert.DoesNotContain(session.Capture().Inputs, q => q.PlayerId == 1); Assert.False(session.Submit(1, new(3, session.Tick + 1)).Accepted);
        for (int i = 0; i < 5; i++) recorder.Step(session);
        Assert.Equal(before.Position, session.View.Actors.Single(a => a.Id == 1).Position);
        recorder.SetConnected(session, 1, true); Assert.Equal(2, session.View.Players[0].AcceptedSequence);
        Assert.True(recorder.Submit(session, 1, new(3, session.Tick + 1, -1, 0)).Accepted); recorder.Step(session);
        Assert.NotEqual(before.Position, session.View.Actors.Single(a => a.Id == 1).Position);
        Assert.True(CoopReplayRunner.Run(Content, recorder.Capture()).Success);
    }
    [Fact]
    public void Both_players_hit_one_enemy_health_pool_without_friendly_fire()
    {
        var session = Fresh(); var state = session.Capture();
        var enemy = state.Actors.First(a => a.PlayerId == 0); enemy.Position = new(0, 0);
        state.Actors.Single(a => a.Id == 1).Position = new(-700, 0); state.Actors.Single(a => a.Id == 2).Position = new(700, 0);
        foreach (var actor in state.Actors.Where(a => a.PlayerId == 0)) actor.RecoveryUntil = 1000;
        session = CoopCombatSession.Restore(Content, state); int initial = enemy.Health;
        Assert.True(session.Submit(1, new(1, 1, Action: CoopInputAction.Cast, SkillId: "skill.cleave", TargetId: enemy.Id)).Accepted);
        Assert.True(session.Submit(2, new(1, 1, Action: CoopInputAction.Cast, SkillId: "skill.cleave", TargetId: enemy.Id)).Accepted);
        var events = new List<CoopEvent>(); for (int i = 0; i < 8; i++) events.AddRange(session.Step());
        Assert.Contains(events, e => e.ActorId == 1 && e.TargetId == enemy.Id && e.Kind is "Damage" or "CriticalDamage");
        Assert.Contains(events, e => e.ActorId == 2 && e.TargetId == enemy.Id && e.Kind is "Damage" or "CriticalDamage");
        Assert.True(session.View.Actors.Single(a => a.Id == enemy.Id).Health < initial - 40);
        Assert.All(session.View.Actors.Where(a => a.PlayerId > 0), a => Assert.Equal(a.MaxHealth, a.Health));
    }
    [Fact]
    public void Readiness_does_not_grant_victory_and_only_both_players_can_retry_a_real_wipe()
    {
        var session = Fresh(); session.Submit(1, new(1, 1, Action: CoopInputAction.Ready)); session.Submit(2, new(1, 1, Action: CoopInputAction.Ready)); session.Step();
        Assert.False(session.View.Cleared); Assert.Empty(session.View.Rewards); Assert.Equal(0, session.View.EncounterIndex);
        for (int i = 0; i < 5000 && !session.View.AwaitingRetry; i++) session.Step();
        Assert.True(session.View.AwaitingRetry); int attempt = session.View.Attempt;
        session.Submit(1, new(2, session.Tick + 1, Action: CoopInputAction.Ready)); session.Step(); Assert.True(session.View.AwaitingRetry);
        session.Submit(2, new(2, session.Tick + 1, Action: CoopInputAction.Ready)); session.Step();
        Assert.False(session.View.AwaitingRetry); Assert.Equal(attempt + 1, session.View.Attempt); Assert.Empty(session.View.Rewards);
        Assert.All(session.View.Actors.Where(a => a.PlayerId > 0), a => Assert.Equal(a.MaxHealth, a.Health));
    }
    [Fact]
    public void Snapshot_validation_rejects_versions_ownership_counters_and_unearned_receipts()
    {
        var state = Fresh().Capture();
        Assert.Throws<InvalidDataException>(() => CoopCombatSession.Restore(Content, state with { SchemaVersion = 2 }));
        Assert.Throws<InvalidDataException>(() => CoopCombatSession.Restore(Content, state with { ContentHash = "changed" }));
        var broken = JsonData.Copy(state); broken.Actors[0] = broken.Actors[0] with { PlayerId = 2 }; Assert.Throws<InvalidDataException>(() => CoopCombatSession.Restore(Content, broken));
        broken = JsonData.Copy(state); broken.NextActorId = 1; Assert.Throws<InvalidDataException>(() => CoopCombatSession.Restore(Content, broken));
        broken = JsonData.Copy(state); broken.Cleared = true; Assert.Throws<InvalidDataException>(() => CoopCombatSession.Restore(Content, broken));
        broken = JsonData.Copy(state); broken.Inputs.Add(new(1, 10, new(999, 10))); Assert.Throws<InvalidDataException>(() => CoopCombatSession.Restore(Content, broken));
        var json = JsonNode.Parse(JsonData.Write(state))!; json["actors"]![0]!["clientDamage"] = 999;
        Assert.Throws<System.Text.Json.JsonException>(() => JsonData.Read<CoopSnapshot>(json.ToJsonString()));
    }
    [Fact]
    public void Movement_intents_expire_and_never_move_the_other_owned_actor()
    {
        var session = Fresh(); var state = session.Capture();
        foreach (var actor in state.Actors.Where(a => a.PlayerId == 0)) actor.RecoveryUntil = 1000;
        session = CoopCombatSession.Restore(Content, state);
        var second = session.View.Actors.Single(a => a.Id == 2).Position;
        session.Submit(1, new(1, 1, -1, 0));
        for (int i = 0; i < CoopCombatSession.InputTimeoutTicks + 2; i++) session.Step();
        var stopped = session.View.Actors.Single(a => a.Id == 1).Position;
        for (int i = 0; i < 10; i++) session.Step();
        Assert.Equal(stopped, session.View.Actors.Single(a => a.Id == 1).Position);
        Assert.Equal(second, session.View.Actors.Single(a => a.Id == 2).Position);
    }
    [Fact]
    public void Ritual_shield_requires_shared_targetable_anchor_deaths_and_corpse_resurrection_is_bounded()
    {
        var session = Fresh(encounter: "coop.bell_saint.2"); var state = session.Capture();
        var boss = state.Actors.Single(a => a.Role == "BellSaint");
        state.Actors.Single(a => a.Id == 1).Position = new(boss.Position.X - 1000, boss.Position.Z);
        session = CoopCombatSession.Restore(Content, state);
        session.Submit(1, new(1, 1, Action: CoopInputAction.Cast, SkillId: "skill.cleave", TargetId: boss.Id));
        var events = new List<CoopEvent>(); for (int i = 0; i < 8; i++) events.AddRange(session.Step());
        Assert.Equal(boss.MaxHealth, session.View.Actors.Single(a => a.Id == boss.Id).Health);
        Assert.Contains(events, e => e.Kind == "DamagePrevented" && e.TargetId == boss.Id);
        for (int tick = 0; tick < 3000 && !session.View.Cleared; tick++)
        {
            var view = session.View;
            foreach (var player in view.Players) session.Submit(player.Id, CoopSmoke.Input(view, player.Id, player.AcceptedSequence + 1));
            events.AddRange(session.Step());
        }
        Assert.True(session.View.Cleared);
        Assert.InRange(events.Count(e => e.Kind == "CorpseResurrected"), 1, 2);
        Assert.Equal(events.Where(e => e.Kind == "CorpseResurrected").Select(e => e.TargetId).Distinct().Count(), events.Count(e => e.Kind == "CorpseResurrected"));
        Assert.All(session.View.Actors.Where(a => a.Role == "Anchor"), a => Assert.Equal(0, a.Health));
        Assert.Equal(2, session.View.Rewards.Length);
    }
    [Fact]
    public void Saved_telegraphs_keep_locked_positions_and_dodge_protects_only_its_owner()
    {
        var session = Fresh(encounter: "coop.bell_saint.1");
        for (int i = 0; i < 5 && session.View.Warnings.Length == 0; i++) session.Step();
        Assert.NotEmpty(session.View.Warnings); var state = session.Capture(); var warning = state.Warnings[0];
        Assert.Equal("Line", warning.Shape); Assert.True(CoopCombatSession.WarningContains(warning, warning.End));
        var player = state.Actors.Single(a => a.Id == 1); var second = state.Actors.Single(a => a.Id == 2);
        player.Position = warning.End; second.Position = new(warning.End.X, warning.End.Z + 600);
        state.Warnings.Clear(); state.Warnings.Add(warning with { ResolveTick = state.Tick + 1 });
        session = CoopCombatSession.Restore(Content, state); session.Submit(1, new(1, session.Tick + 1, 0, -1, CoopInputAction.Dodge));
        var events = session.Step();
        Assert.Equal(player.Health, session.View.Actors.Single(a => a.Id == 1).Health);
        Assert.Contains(events, e => e.TargetId == 2 && e.Kind == "Damage");
        Assert.Equal(state.Players[1].DodgeReadyTick, session.Capture().Players[1].DodgeReadyTick);
    }
    [Fact]
    public void Replays_reject_changed_intents_and_snapshots_are_detached_from_the_live_world()
    {
        var session = Fresh(); var recorder = new CoopRecorder(session);
        recorder.Submit(session, 1, new(1, 1, 1, 0)); recorder.Step(session);
        var replay = recorder.Capture(); replay.Operations[0] = replay.Operations[0] with { Input = replay.Operations[0].Input! with { MoveX = -1 } };
        Assert.False(CoopReplayRunner.Run(Content, replay).Success);
        string hash = session.StateHash; var snapshot = session.Capture(); snapshot.Actors[0].Health = 1; snapshot.Players[0].Momentum = 100;
        var view = session.View; view.Actors[0] = view.Actors[0] with { Health = 1 }; view.Players[0].Skills[0] = view.Players[0].Skills[0] with { Cost = 0 };
        Assert.Equal(hash, session.StateHash);
    }
}
