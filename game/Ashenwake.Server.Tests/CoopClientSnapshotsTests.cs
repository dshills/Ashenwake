using Ashenwake.Client;
using Ashenwake.Core.Coop;
using Xunit;

namespace Ashenwake.Server.Tests;

public sealed class CoopClientSnapshotsTests
{
    [Fact]
    public void Connection_changes_at_a_stopped_tick_replace_the_authoritative_view()
    {
        var core = CoopCombatSession.Create(ServerFixture.Content);
        core.SetConnected(2, false);
        var snapshots = new CoopClientSnapshots();
        Assert.True(snapshots.Apply(core.View, joined: true, receivedAt: 1));
        Assert.False(snapshots.Current!.Players.Single(p => p.Id == 2).Connected);
        core.SetConnected(2, true);
        Assert.True(snapshots.Apply(core.View, joined: false, receivedAt: 2));
        Assert.True(snapshots.Current!.Players.Single(p => p.Id == 2).Connected);
        core.SetConnected(2, false);
        Assert.True(snapshots.Apply(core.View, joined: false, receivedAt: 3));
        Assert.False(snapshots.Current!.Players.Single(p => p.Id == 2).Connected);
        Assert.Equal(0, snapshots.Current!.Tick);
        Assert.Null(snapshots.Previous);
        Assert.Equal(1, snapshots.InterpolationStartedAt);
        Assert.Equal(3, snapshots.ReceivedAt);
    }
    [Fact]
    public void Same_tick_acknowledgments_update_input_state_without_restarting_interpolation()
    {
        var core = CoopCombatSession.Create(ServerFixture.Content);
        var snapshots = new CoopClientSnapshots();
        var initial = core.View;
        snapshots.Apply(initial, joined: true, receivedAt: 1);
        core.Step();
        snapshots.Apply(core.View, joined: false, receivedAt: 2);
        var receipt = core.Submit(1, new(1, core.Tick));
        Assert.True(receipt.Accepted);
        var acknowledged = core.View;
        snapshots.Apply(acknowledged, joined: false, receivedAt: 2.01);
        snapshots.Apply(acknowledged, joined: false, receivedAt: 2.02);
        Assert.Equal(receipt.AcceptedSequence, snapshots.Current!.Players.Single(p => p.Id == 1).AcceptedSequence);
        Assert.Same(initial, snapshots.Previous);
        Assert.Equal(2, snapshots.InterpolationStartedAt);
        Assert.Equal(2.02, snapshots.ReceivedAt);
        core.Step();
        snapshots.Apply(core.View, joined: false, receivedAt: 3);
        Assert.Same(acknowledged, snapshots.Previous);
        Assert.Equal(3, snapshots.InterpolationStartedAt);
        Assert.Equal(receipt.Sequence, snapshots.Current!.Players.Single(p => p.Id == 1).ProcessedSequence);
    }
    [Fact]
    public void Older_frames_cannot_roll_back_state_but_a_new_join_resets_the_timeline()
    {
        var core = CoopCombatSession.Create(ServerFixture.Content);
        var snapshots = new CoopClientSnapshots();
        var initial = core.View;
        snapshots.Apply(initial, joined: true, receivedAt: 1);
        core.Step();
        var advanced = core.View;
        snapshots.Apply(advanced, joined: false, receivedAt: 2);
        Assert.False(snapshots.Apply(initial, joined: false, receivedAt: 3));
        Assert.Same(advanced, snapshots.Current);
        Assert.Equal(2, snapshots.ReceivedAt);
        Assert.True(snapshots.Apply(initial, joined: true, receivedAt: 4));
        Assert.Same(initial, snapshots.Current);
        Assert.Null(snapshots.Previous);
        Assert.Equal(4, snapshots.InterpolationStartedAt);
        Assert.Equal(4, snapshots.ReceivedAt);
    }
}
