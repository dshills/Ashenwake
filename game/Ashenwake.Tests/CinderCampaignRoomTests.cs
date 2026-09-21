using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CinderCampaignRoomTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    public static IEnumerable<object[]> Rooms() => new[] { "campaign.cinder_pack", "campaign.extraction_floor", "campaign.furnace_spindle", "exploration.burning_rain", "exploration.sealed_foundry" }.Select(id => new object[] { id });

    [Theory, MemberData(nameof(Rooms))]
    public void AuthoredRoutesAndAllPassagesAreReachableAndRestoreWithTheirCollision(string id)
    {
        var content = CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json"));
        var active = content.CreateEncounter(id); var room = active.Room;
        Assert.True(CinderCampaignLayout.Contains(id)); Assert.NotEmpty(room.Obstacles);
        Assert.Equal(12000, room.HalfWidth); Assert.Equal(10000, room.HalfDepth);
        var space = new SpatialWorld(room);
        Assert.All(active.View.Actors, a => Assert.True(space.CanOccupy(a.Position, CombatSession.ActorRadius)));
        Assert.Equal(active.StateHash, CombatSession.Restore(content.CombatJson, active.Capture()).StateHash);
        var session = content.CreateClearedEncounter(id);
        Assert.Equal(JsonData.Hash(room), JsonData.Hash(session.Room));
        var landmarks = CinderCampaignLayout.Route(id).Concat(new[] { CinderCampaignLayout.BackExit, CinderCampaignLayout.ForwardExit });
        landmarks = id switch
        {
            "campaign.cinder_pack" => landmarks.Append(CinderCampaignLayout.FoundryEntrance),
            "campaign.extraction_floor" => landmarks.Append(CinderCampaignLayout.StormEntrance),
            "exploration.sealed_foundry" => landmarks.Append(CinderCampaignLayout.FoundryTreasure).Append(CinderCampaignLayout.BranchReturn),
            "exploration.burning_rain" => landmarks.Append(CinderCampaignLayout.BranchReturn),
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
            Assert.True(Position.DistanceSquared(session.View.Actors[0].Position, target) <= 300L * 300, "Unreachable Cinder landmark " + target);
            session.Step([new(CombatCommandKind.Stop)]);
        }
        Assert.Empty(session.View.CampaignHazards!);
        Assert.Equal(session.StateHash, CombatSession.Restore(content.CombatJson, session.Capture()).StateHash);
        var invalid = active.Capture(); var wall = room.Obstacles[0];
        invalid.Actors[0].Position = new((wall.MinX + wall.MaxX) / 2, (wall.MinZ + wall.MaxZ) / 2);
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(content.CombatJson, invalid));
    }
}
