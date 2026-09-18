using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class ClickMovePlannerTests
{
    private static string Content() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json"));
    private static RoomDefinition Room() => CombatContent.Parse(Content()).Room;
    private static CombatSession EmptyArena(Position from)
    {
        var state = CombatSession.Create(Content()).Capture();
        state.Actors.RemoveAll(a => a.Id != 1);
        state.Actors[0].Position = from;
        return CombatSession.Restore(Content(), state);
    }

    [Theory]
    [InlineData(-3700)]
    [InlineData(3700)]
    public void ActualMoveCommandsReachTheOtherSideOfEachAuthoredPillar(int z)
    {
        var session = EmptyArena(new(-4500, z));
        var planner = new ClickMovePlanner(Room());
        var destination = new Position(4500, z);
        Assert.True(planner.TrySetDestination(Player(session), destination));
        var visited = Follow(session, planner);
        Assert.Contains(visited, p => Math.Abs(p.Z - z) >= 780);
        AssertArrived(session, planner, destination);
    }

    [Fact]
    public void ArrivalStopsWithoutOscillationAndReplacementAndCancelStopOldIntent()
    {
        var session = EmptyArena(new(-4500, 0));
        var planner = new ClickMovePlanner(Room());
        Assert.True(planner.TrySetDestination(Player(session), new(4500, 0)));
        Assert.True(planner.TrySetDestination(Player(session), new(-4500, 1200)));
        Follow(session, planner);
        AssertArrived(session, planner, new(-4500, 1200));
        var arrived = Player(session);
        for (int i = 0; i < 20; i++)
        {
            var direction = planner.NextDirection(arrived);
            Assert.Equal(default, direction);
            session.Step([new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]);
        }
        Assert.Equal(arrived, Player(session));
        Assert.True(planner.TrySetDestination(arrived, new(3000, 0)));
        planner.Cancel();
        Assert.Null(planner.Destination);
        Assert.Equal(default, planner.NextDirection(arrived));
    }

    [Fact]
    public void BoundaryClicksClampAndSolidOrOccupiedClicksRejectAndClearOldIntent()
    {
        var session = EmptyArena(new(-4500, 0));
        var planner = new ClickMovePlanner(Room());
        Assert.True(planner.TrySetDestination(Player(session), new(int.MinValue, int.MaxValue)));
        var edge = new Position(-Room().HalfWidth + CombatSession.ActorRadius, Room().HalfDepth - CombatSession.ActorRadius);
        Assert.Equal(edge, planner.Destination);
        Follow(session, planner);
        AssertArrived(session, planner, edge);
        Assert.False(planner.TrySetDestination(Player(session), new(0, 3700)));
        Assert.Null(planner.Destination);
        Assert.True(planner.TrySetDestination(Player(session), new(-4500, 0)));
        Assert.False(planner.TrySetDestination(Player(session), new(0, 0), [new(0, 0)]));
        Assert.Null(planner.Destination);
    }

    [Fact]
    public void LivingBodyDetourUsesActualCollisionAndFrozenTargetStaysInPlace()
    {
        var state = CombatSession.Create(Content()).Capture();
        state.Actors.RemoveAll(a => a.Id > 2);
        state.Actors[0].Position = new(-2500, 0);
        state.Actors[1].Position = new(0, 0);
        state.Actors[1].Statuses.Add(new() { Id = "Frozen", SourceId = 1, OwnerId = 1, ActionId = 1, ExpiresTick = 1000 });
        state.NextActionId = Math.Max(2, state.NextActionId);
        var session = CombatSession.Restore(Content(), state);
        var planner = new ClickMovePlanner(Room());
        Assert.True(planner.TrySetDestination(Player(session), new(2500, 0), Bodies(session)));
        var visited = Follow(session, planner);
        Assert.Contains(visited, p => Math.Abs(p.Z) >= CombatSession.ActorRadius * 2);
        Assert.All(visited, p => Assert.True(Position.DistanceSquared(p, new(0, 0)) >= 4L * CombatSession.ActorRadius * CombatSession.ActorRadius));
        Assert.Equal(new Position(0, 0), session.View.Actors.Single(a => a.Id == 2).Position);
        AssertArrived(session, planner, new(2500, 0));
    }

    [Fact]
    public void NewBodyAcrossAnExistingRouteTriggersADetour()
    {
        var room = Room();
        var planner = new ClickMovePlanner(room);
        var from = new Position(-2500, 0);
        var target = new Position(2500, 0);
        Assert.True(planner.TrySetDestination(from, target));
        var spatial = new SpatialWorld(room);
        for (int tick = 0; tick < 200 && planner.Destination is not null; tick++)
        {
            var move = planner.NextDirection(from, [new(0, 0)]);
            int speed = move.X != 0 && move.Z != 0 ? 106 : 150;
            from = spatial.Move(from, new(from.X + move.X * speed, from.Z + move.Z * speed), CombatSession.ActorRadius);
            Assert.True(Position.DistanceSquared(from, new(0, 0)) >= 4L * CombatSession.ActorRadius * CombatSession.ActorRadius);
        }
        Assert.Null(planner.Destination);
        Assert.True(Position.DistanceSquared(from, target) <= (long)ClickMovePlanner.ArrivalTolerance * ClickMovePlanner.ArrivalTolerance);
    }

    [Fact]
    public void UnreachableBarrierAndOverBoundInputsRejectWithoutMoving()
    {
        var room = Room() with { Obstacles = [new(-300, -10000, 300, 10000)] };
        var planner = new ClickMovePlanner(room);
        Assert.False(planner.TrySetDestination(new(-4500, 0), new(4500, 0)));
        Assert.Null(planner.Destination);
        Assert.Equal(default, planner.NextDirection(new(-4500, 0)));
        Assert.False(planner.TrySetDestination(new(-4500, 0), new(-3000, 0), Enumerable.Repeat(new Position(3000, 0), CombatSession.MaxActors + 1).ToArray()));
        Assert.False(planner.TrySetDestination(new(-4500, 0), new(-3000, 0), [new(int.MaxValue, int.MinValue)]));
        Assert.Throws<ArgumentException>(() => new ClickMovePlanner(Room() with { Obstacles = Enumerable.Repeat(new Bounds(-300, -300, 300, 300), 101).ToArray() }));
    }

    [Fact]
    public void RepeatedUnchangedAuthoritativePositionEventuallyCancels()
    {
        var planner = new ClickMovePlanner(Room());
        var from = new Position(-4500, 0);
        Assert.True(planner.TrySetDestination(from, new(4500, 0)));
        for (int tick = 0; tick <= ClickMovePlanner.StuckCallLimit; tick++) planner.NextDirection(from);
        Assert.Null(planner.Destination);
        Assert.Equal(default, planner.NextDirection(from));
    }

    [Fact]
    public void PlanningDoesNotMutateSimulationAndRecordedCommandsReplayExactly()
    {
        var session = EmptyArena(new(-4500, -3700));
        var original = session.Capture();
        var hash = session.StateHash;
        var planner = new ClickMovePlanner(Room());
        Assert.True(planner.TrySetDestination(Player(session), new(4500, -3700)));
        planner.NextDirection(Player(session));
        Assert.Equal(hash, session.StateHash);
        var replay = CombatSession.Restore(Content(), original);
        for (int tick = 0; tick < 500 && planner.Destination is not null; tick++)
        {
            var direction = planner.NextDirection(Player(session));
            CombatCommand[] commands = [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)];
            Assert.Equal(JsonData.Write(session.Step(commands)), JsonData.Write(replay.Step(commands)));
            Assert.Equal(session.StateHash, replay.StateHash);
        }
        AssertArrived(session, planner, new(4500, -3700));
        Assert.Equal(original.Rng, session.Capture().Rng);
    }

    [Theory]
    [InlineData(559, true)]
    [InlineData(560, false)]
    [InlineData(30000, false)]
    public void BodyClearanceHandlesTangencyAndLargeCrossProductsExactly(int bodyZ, bool detours)
    {
        var room = Room() with { HalfWidth = 100000, HalfDepth = 100000, Obstacles = [] };
        var spatial = new SpatialWorld(room);
        var planner = new ClickMovePlanner(room);
        var from = new Position(-99000, 0);
        var target = new Position(99000, 0);
        var body = new Position(0, bodyZ);
        Assert.True(planner.TrySetDestination(from, target, [body]));
        bool leftStraightLine = false;
        for (int tick = 0; tick < 1600 && planner.Destination is not null; tick++)
        {
            var direction = planner.NextDirection(from, [body]);
            int step = direction.X != 0 && direction.Z != 0 ? 106 : 150;
            from = spatial.Move(from, new(from.X + direction.X * step, from.Z + direction.Z * step), CombatSession.ActorRadius);
            leftStraightLine |= from.Z != 0;
            Assert.True(Position.DistanceSquared(from, body) >= 4L * CombatSession.ActorRadius * CombatSession.ActorRadius);
        }
        Assert.Null(planner.Destination);
        Assert.True(Position.DistanceSquared(from, target) <= (long)ClickMovePlanner.ArrivalTolerance * ClickMovePlanner.ArrivalTolerance);
        Assert.Equal(detours, leftStraightLine);
    }

    private static List<Position> Follow(CombatSession session, ClickMovePlanner planner)
    {
        List<Position> positions = [];
        for (int tick = 0; tick < 500 && planner.Destination is not null; tick++)
        {
            var direction = planner.NextDirection(Player(session), Bodies(session));
            Assert.InRange(direction.X, -1, 1);
            Assert.InRange(direction.Z, -1, 1);
            session.Step([new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]);
            positions.Add(Player(session));
            Assert.True(new SpatialWorld(Room()).CanOccupy(Player(session), CombatSession.ActorRadius));
        }
        return positions;
    }

    private static void AssertArrived(CombatSession session, ClickMovePlanner planner, Position destination)
    {
        Assert.Null(planner.Destination);
        Assert.True(Position.DistanceSquared(Player(session), destination) <= (long)ClickMovePlanner.ArrivalTolerance * ClickMovePlanner.ArrivalTolerance,
            $"Stopped at {Player(session)} before reaching {destination}.");
    }
    private static Position Player(CombatSession session) => session.View.Actors.Single(a => a.Id == 1).Position;
    private static Position[] Bodies(CombatSession session) => session.View.Actors.Where(a => a.Id != 1 && a.Health > 0).Select(a => a.Position).ToArray();
}
