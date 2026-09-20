using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ashenwake.Core.Content;
using Ashenwake.Core.Coop;
using Ashenwake.Server;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Ashenwake.Server.Tests;

public sealed class MatchHostTests
{
    private static async Task Until(Func<bool> condition, int milliseconds = 6000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < milliseconds) await Task.Delay(10);
        Assert.True(condition(), "Bounded server condition timed out.");
    }
    [Theory]
    [InlineData("futureRules")]
    [InlineData("futureSchema")]
    [InlineData("unknownAttribute")]
    [InlineData("corruptTotals")]
    public async Task Unsupported_character_state_is_rejected_before_any_checkpoint(string alteration)
    {
        await using var fixture = await ServerFixture.Create();
        var state = JsonNode.Parse(JsonData.Write(new OnlineCharacterState("coop.1", "old-match", 1, 10, 0, 0, [])))!;
        switch (alteration)
        {
            case "futureRules": state["rulesVersion"] = "coop.2"; break;
            case "futureSchema": state["schemaVersion"] = 2; break;
            case "unknownAttribute": state["futurePower"] = 500; break;
            default: state["experience"] = 500; break;
        }
        fixture.CharacterState(JsonSerializer.SerializeToElement(state)); string original = fixture.Allocation.Characters[0].State.GetRawText();
        var error = await Record.ExceptionAsync(fixture.Host);
        Assert.True(error is InvalidDataException or JsonException);
        Assert.Empty(fixture.Requests); Assert.Equal(0, fixture.Allocation.Revision);
        Assert.Equal(original, fixture.Allocation.Characters[0].State.GetRawText());
    }
    [Fact]
    public async Task A_stalled_outbound_peer_disconnects_even_while_it_keeps_sending_input()
    {
        await using var fixture = await ServerFixture.Create(); await using var host = await fixture.Host();
        using var socket = new TestSocket(blockSend: true);
        var serving = host.Serve(socket, fixture.Join(1), CancellationToken.None);
        await socket.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var stop = new CancellationTokenSource();
        var flood = Task.Run(async () =>
        {
            long sequence = 1;
            try { while (!stop.IsCancellationRequested) { socket.Feed(new(sequence++, 1)); await Task.Delay(25, stop.Token); } }
            catch (OperationCanceledException) { }
        });
        await serving.WaitAsync(TimeSpan.FromSeconds(4)); stop.Cancel(); await flood;
        Assert.Equal(0, host.ConnectedPeers);
        Assert.Equal(System.Net.WebSockets.WebSocketState.Aborted, socket.State);
        Assert.False(host.Draining);
    }
    [Fact]
    public async Task Replacing_a_socket_preserves_the_new_owner_and_disposal_awaits_both_peer_lifetimes()
    {
        await using var fixture = await ServerFixture.Create(); var host = await fixture.Host();
        using var oldSocket = new TestSocket(); using var replacement = new TestSocket(); using var second = new TestSocket();
        var old = host.Serve(oldSocket, fixture.Join(1), CancellationToken.None);
        await oldSocket.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var current = host.Serve(replacement, fixture.Join(1), CancellationToken.None);
        await replacement.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)); await old.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, host.ConnectedPeers); Assert.Equal(1, host.Reconnects);
        var other = host.Serve(second, fixture.Join(2), CancellationToken.None);
        await second.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var readDiagnostics = Task.Run(() => { for (int i = 0; i < 100000; i++) _ = host.Completed; });
        await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(6));
        await Task.WhenAll(current, other, readDiagnostics).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, host.ConnectedPeers); Assert.True(host.Draining);
        var saved = JsonData.Read<CoopSnapshot>(fixture.Allocation.Snapshot.GetRawText());
        Assert.All(saved.Players, p => Assert.False(p.Connected));
        await host.DisposeAsync(); // Disposal is idempotent.
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_checkpoint_disconnect_preserves_the_immutable_retry_and_replay_chain(bool committedBeforeFailure)
    {
        await using var fixture = await ServerFixture.Create();
        fixture.FailWrites = !committedBeforeFailure; fixture.LoseNextCommittedResponse = committedBeforeFailure;
        await using var host = await fixture.Host(); using var first = new TestSocket(); using var second = new TestSocket();
        var one = host.Serve(first, fixture.Join(1), CancellationToken.None); var two = host.Serve(second, fixture.Join(2), CancellationToken.None);
        await Until(() => host.PersistencePaused && fixture.Requests.Count > 0);
        var pending = fixture.Requests.First(); second.Abort(); await two.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(100); fixture.FailWrites = false;
        await Until(() => fixture.Allocation.Revision >= 2);
        var retries = fixture.Requests.Where(r => r.OperationId == pending.OperationId).ToArray();
        Assert.True(retries.Length >= 2); Assert.All(retries, r => Assert.Equal(JsonData.Hash(pending), JsonData.Hash(r)));
        string directory = Path.Combine(fixture.ArtifactDirectory, fixture.Allocation.AllocationId);
        var segment1 = JsonData.Read<CoopReplay>(File.ReadAllText(Path.Combine(directory, "segment.1.json")));
        var segment2 = JsonData.Read<CoopReplay>(File.ReadAllText(Path.Combine(directory, "segment.2.json")));
        var result1 = CoopReplayRunner.Run(ServerFixture.Content, segment1);
        Assert.True(result1.Success); Assert.Equal(result1.StateHash, JsonData.Hash(segment2.InitialState));
        Assert.True(CoopReplayRunner.Run(ServerFixture.Content, segment2).Success);
        Assert.Contains(segment2.Operations, o => o.Kind == "Connection" && o.PlayerId == 2 && !o.Connected);
        Assert.False(JsonData.Read<CoopSnapshot>(fixture.Allocation.Snapshot.GetRawText()).Players.Single(p => p.Id == 2).Connected);
        first.Abort(); await one.WaitAsync(TimeSpan.FromSeconds(2));
    }
    [Fact]
    public async Task Idle_eviction_waits_for_durability_and_restores_the_disconnected_world()
    {
        await using var fixture = await ServerFixture.Create(); var host = await fixture.Host(); fixture.FailWrites = true;
        Assert.False(await host.TryEvict(TimeSpan.Zero)); Assert.True(host.PersistencePaused); Assert.False(host.Draining);
        fixture.FailWrites = false; await Until(() => !host.PersistencePaused);
        Assert.True(await host.TryEvict(TimeSpan.Zero)); Assert.True(host.Draining);
        var saved = fixture.Allocation.Snapshot.GetRawText(); await host.DisposeAsync();
        await using var restored = await fixture.Host();
        Assert.Equal(0, restored.ConnectedPeers); Assert.Equal(0, JsonData.Read<CoopSnapshot>(saved).Tick);
        using var socket = new TestSocket(); var serving = restored.Serve(socket, fixture.Join(1), CancellationToken.None);
        var frame = await socket.Frames.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(fixture.Allocation.AllocationId, frame.View.MatchId); Assert.Equal(0, frame.View.Tick);
        socket.Abort(); await serving.WaitAsync(TimeSpan.FromSeconds(2));
    }
    [Fact]
    public async Task Restored_started_match_resumes_with_only_one_returning_player()
    {
        await using var fixture = await ServerFixture.Create();
        var core = CoopCombatSession.Create(ServerFixture.Content, matchId: fixture.Allocation.AllocationId);
        for (int tick = 0; tick < 5; tick++) core.Step();
        fixture.CompletedSnapshot(core.Capture());
        await using var host = await fixture.Host(); using var socket = new TestSocket();
        var serving = host.Serve(socket, fixture.Join(1), CancellationToken.None);
        var joined = await socket.Frames.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(5, joined.View.Tick); Assert.False(joined.View.Completed);
        await Until(() => fixture.Allocation.Revision >= 1);
        var saved = JsonData.Read<CoopSnapshot>(fixture.Allocation.Snapshot.GetRawText());
        Assert.True(saved.Tick > joined.View.Tick);
        Assert.Equal(1, host.ConnectedPeers);
        Assert.True(saved.Players.Single(p => p.Id == 1).Connected);
        Assert.False(saved.Players.Single(p => p.Id == 2).Connected);
        socket.Abort(); await serving.WaitAsync(TimeSpan.FromSeconds(2));
    }
    [Fact]
    public async Task Restored_unstarted_match_waits_for_both_players_before_advancing()
    {
        await using var fixture = await ServerFixture.Create();
        fixture.CompletedSnapshot(CoopCombatSession.Create(ServerFixture.Content, matchId: fixture.Allocation.AllocationId).Capture());
        await using var host = await fixture.Host(); using var first = new TestSocket(); using var second = new TestSocket();
        var one = host.Serve(first, fixture.Join(1), CancellationToken.None);
        await first.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Until(() => fixture.Allocation.Revision >= 1);
        Assert.Equal(0, JsonData.Read<CoopSnapshot>(fixture.Allocation.Snapshot.GetRawText()).Tick);
        while (first.Frames.Reader.TryRead(out var frame)) Assert.Equal(0, frame.View.Tick);
        var two = host.Serve(second, fixture.Join(2), CancellationToken.None);
        await Until(() => fixture.Allocation.Revision >= 2);
        Assert.True(JsonData.Read<CoopSnapshot>(fixture.Allocation.Snapshot.GetRawText()).Tick > 0);
        first.Abort(); second.Abort(); await Task.WhenAll(one, two).WaitAsync(TimeSpan.FromSeconds(2));
    }
    [Fact]
    public async Task Retained_personal_rewards_survive_slot_changes_and_idle_checkpoints()
    {
        await using var fixture = await ServerFixture.Create();
        var reward = new CoopRewardReceipt("old-match:0:player:2", 2, "coop.ossuary", 0, 100, 20,
            new(1, "item.ash_axe", "Ash Axe", "MainHand", "Tempered", 6, 0, 1200));
        fixture.CharacterState(JsonSerializer.SerializeToElement(new OnlineCharacterState("coop.1", "old-match", 2, 30, 100, 20, [reward]), JsonData.Options));
        await using var host = await fixture.Host(); using var socket = new TestSocket();
        var serving = host.Serve(socket, fixture.Join(1), CancellationToken.None);
        await Until(() => fixture.Allocation.Revision >= 1);
        var saved = JsonData.Read<OnlineCharacterState>(fixture.Allocation.Characters[0].State.GetRawText());
        Assert.Equal(1, saved.PlayerId); Assert.Equal(100, saved.Experience); Assert.Equal(20, saved.Ash); Assert.Equal(reward, Assert.Single(saved.Rewards));
        Assert.Empty(fixture.Requests.SelectMany(r => r.Rewards));
        socket.Abort(); await serving.WaitAsync(TimeSpan.FromSeconds(2));
    }
    [Fact]
    public async Task Replay_disk_failure_cannot_publish_uncommitted_rewards_when_a_peer_disconnects()
    {
        await using var fixture = await ServerFixture.Create();
        var core = CoopCombatSession.Create(ServerFixture.Content, matchId: fixture.Allocation.AllocationId);
        CoopSnapshot beforeReward = core.Capture();
        while (core.View.Rewards.Length == 0 && core.Tick < CoopSmoke.MaximumTicks)
        {
            var view = core.View;
            foreach (var player in view.Players) core.Submit(player.Id, CoopSmoke.Input(view, player.Id, player.AcceptedSequence + 1));
            beforeReward = core.Capture(); core.Step();
        }
        Assert.Equal(2, core.View.Rewards.Length);
        var expected = CoopCombatSession.Restore(ServerFixture.Content, beforeReward);
        foreach (int slot in new[] { 1, 2 }) { expected.SetConnected(slot, false); expected.SetConnected(slot, true); }
        expected.Step(); Assert.Equal(2, expected.View.Rewards.Length);
        fixture.CompletedSnapshot(beforeReward);
        // A regular file in place of the artifact directory fails the actual
        // AtomicFile write before an HTTP checkpoint can be prepared.
        File.WriteAllText(fixture.ArtifactDirectory, "blocked artifact directory");
        try
        {
            await using var host = await fixture.Host(); using var first = new TestSocket(); using var second = new TestSocket();
            var one = host.Serve(first, fixture.Join(1), CancellationToken.None);
            var two = host.Serve(second, fixture.Join(2), CancellationToken.None);
            await Until(() => host.Draining);
            Assert.False(host.PersistencePaused); Assert.Empty(fixture.Requests);
            second.Abort(); await two.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.Delay(100);
            var observed = new List<NetworkFrame>();
            while (first.Frames.Reader.TryRead(out var frame)) observed.Add(frame);
            Assert.NotEmpty(observed); Assert.All(observed, frame => Assert.Empty(frame.View.Rewards));
            Assert.Empty(JsonData.Read<CoopSnapshot>(fixture.Allocation.Snapshot.GetRawText()).Rewards);
            first.Abort(); await one.WaitAsync(TimeSpan.FromSeconds(2));
            File.Delete(fixture.ArtifactDirectory); // Permit the normal disposal drain to commit the preserved world.
        }
        finally { if (File.Exists(fixture.ArtifactDirectory)) File.Delete(fixture.ArtifactDirectory); }
    }
    [Fact]
    public async Task Completed_match_reconnects_share_one_hash_and_late_input_cannot_change_it()
    {
        await using var fixture = await ServerFixture.Create();
        var core = CoopCombatSession.Create(ServerFixture.Content, matchId: fixture.Allocation.AllocationId);
        for (int tick = 0; tick < CoopSmoke.MaximumTicks && !core.View.Completed; tick++)
        {
            var view = core.View;
            foreach (var player in view.Players) core.Submit(player.Id, CoopSmoke.Input(view, player.Id, player.AcceptedSequence + 1));
            core.Step();
        }
        Assert.True(core.View.Completed); fixture.CompletedSnapshot(core.Capture());
        await using var host = await fixture.Host(); using var first = new TestSocket(); using var second = new TestSocket();
        var one = host.Serve(first, fixture.Join(1), CancellationToken.None);
        await first.Frames.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        var two = host.Serve(second, fixture.Join(2), CancellationToken.None);
        var updated = await first.Frames.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        var joined = await second.Frames.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(joined.StateHash, updated.StateHash);
        var joinedPlayer = joined.View.Players.Single(p => p.Id == 2);
        second.Feed(new(joinedPlayer.AcceptedSequence + 1, joined.View.Tick + 1, 1, 0));
        NetworkFrame ack;
        do { ack = await second.Frames.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)); } while (ack.Kind != "ack");
        Assert.Equal("match_completed", ack.InputResult!.Code); Assert.False(ack.InputResult.Accepted);
        Assert.Equal(joined.StateHash, ack.StateHash);
        first.Abort(); second.Abort(); await Task.WhenAll(one, two).WaitAsync(TimeSpan.FromSeconds(2));
    }
}
