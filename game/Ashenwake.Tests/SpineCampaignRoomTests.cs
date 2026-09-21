using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class SpineCampaignRoomTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    public static IEnumerable<object[]> Rooms() => new[] { "campaign.bone_causeway", "campaign.contract_hall", "campaign.covenant_warden", "exploration.first_oath", "exploration.oathkeeper_archive" }.Select(id => new object[] { id });

    [Theory, MemberData(nameof(Rooms))]
    public void AuthoredRoutesAndAllPassagesAreReachableAndRestoreWithTheirCollision(string id)
    {
        var content = CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json"));
        var active = content.CreateEncounter(id); var room = active.Room;
        Assert.True(SpineCampaignLayout.Contains(id)); Assert.NotEmpty(room.Obstacles);
        Assert.Equal(12000, room.HalfWidth); Assert.Equal(10000, room.HalfDepth);
        var space = new SpatialWorld(room);
        Assert.All(active.View.Actors, a => Assert.True(space.CanOccupy(a.Position, CombatSession.ActorRadius)));
        Assert.Equal(active.StateHash, CombatSession.Restore(content.CombatJson, active.Capture()).StateHash);
        var session = content.CreateClearedEncounter(id);
        Assert.Equal(JsonData.Hash(room), JsonData.Hash(session.Room));
        var landmarks = SpineCampaignLayout.Route(id).Concat(new[] { SpineCampaignLayout.BackExit, SpineCampaignLayout.ForwardExit });
        landmarks = id switch
        {
            "campaign.bone_causeway" => landmarks.Append(SpineCampaignLayout.ArchiveEntrance),
            "campaign.contract_hall" => landmarks.Append(SpineCampaignLayout.MemoryEntrance),
            "exploration.oathkeeper_archive" => landmarks.Append(SpineCampaignLayout.ArchiveTreasure).Append(SpineCampaignLayout.BranchReturn),
            "exploration.first_oath" => landmarks.Append(SpineCampaignLayout.BranchReturn),
            _ => landmarks
        };
        foreach (var target in landmarks)
        {
            Assert.True(space.CanOccupy(target, CombatSession.ActorRadius));
            for (int tick = 0; tick < 800 && Position.DistanceSquared(session.View.Actors[0].Position, target) > 300L * 300; tick++)
            {
                var direction = CombatProductionSmoke.MovementDirection(session.View.Actors[0].Position, target, room);
                session.Step([new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]);
                Assert.True(space.CanOccupy(session.View.Actors[0].Position, CombatSession.ActorRadius));
            }
            Assert.True(Position.DistanceSquared(session.View.Actors[0].Position, target) <= 300L * 300, "Unreachable Spine landmark " + target);
            session.Step([new(CombatCommandKind.Stop)]);
        }
        Assert.Empty(session.View.CampaignHazards!);
        Assert.Equal(session.StateHash, CombatSession.Restore(content.CombatJson, session.Capture()).StateHash);
        var invalid = active.Capture(); var wall = room.Obstacles[0];
        invalid.Actors[0].Position = new((wall.MinX + wall.MaxX) / 2, (wall.MinZ + wall.MaxZ) / 2);
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(content.CombatJson, invalid));
    }
    [Fact]
    public void DivineMemoryKeepsTheReversedFaultOrderWithReachableSafeGround()
    {
        var content = CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json"));
        var session = content.CreateEncounter("exploration.first_oath");
        for (int tick = 0; tick < 45; tick++) session.Step([]);
        // Step processes the current tick, then advances the externally visible clock.
        var warnings = session.Step([]).Where(e => e.Kind == "CampaignHazardWarned" && e.ContentId.StartsWith("rule.fault.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[] { 30, 50, 70 }, warnings.Select(e => e.Amount));
        var faults = session.View.CampaignHazards!.Where(h => h.ContentId.StartsWith("rule.fault.", StringComparison.Ordinal)).OrderBy(h => h.RemainingTicks).ToArray();
        Assert.Equal(new[] { "rule.fault.1", "rule.fault.2", "rule.fault.3" }, faults.Select(h => h.ContentId));
        Assert.Equal(new[] { 3000, 0, -3000 }, faults.Select(h => h.Position.Z));
        Assert.Equal(new long[] { 29, 49, 69 }, faults.Select(h => h.RemainingTicks));
        Assert.Equal(0, session.Capture().Campaign!.RuleUntil);
        var safe = new Position(6000, 6500); var space = new SpatialWorld(session.Room);
        Assert.True(space.CanOccupy(safe, CombatSession.ActorRadius));
        Assert.All(faults, h => Assert.False(CombatSession.HazardContains(h, safe)));
        Assert.Equal(session.StateHash, CombatSession.Restore(content.CombatJson, session.Capture()).StateHash);
    }

}
