using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

public sealed partial class CombatSession
{
    /// <summary>Reopens an authored room without enemies or encounter mechanics. Inventory,
    /// character state and identity counters follow the normal transition rules.</summary>
    public static CombatSession CreateClearedEncounter(string contentJson, ulong seed, string layoutEncounterId,
        CombatSnapshot? previous = null, bool restoreAtAnchor = false)
    {
        var cleared = CreateEncounter(contentJson, seed, "clear", previous, restoreAtAnchor);
        if (cleared._content.Campaign?.Encounters.Any(e => e.Id == layoutEncounterId && e.Room is not null) != true)
            throw new ArgumentException("Unknown authored campaign room.", nameof(layoutEncounterId));
        var state = cleared.Capture() with { RoomEncounterId = layoutEncounterId };
        var room = ResolveRoom(cleared._content, state);
        state.Actors[0].Position = room.PlayerSpawn;
        var session = new CombatSession(cleared._content, state);
        session.ValidateSnapshot();
        return session;
    }

    internal static void ValidateCampaignRoom(RoomDefinition room)
    {
        if (room is not { HalfWidth: >= 8000 and <= 100000, HalfDepth: >= 8000 and <= 100000, Obstacles: not null } ||
            room.Obstacles.Length > 24 || room.Obstacles.Any(b => b.MinX >= b.MaxX || b.MinZ >= b.MaxZ ||
                b.MinX < -room.HalfWidth || b.MaxX > room.HalfWidth || b.MinZ < -room.HalfDepth || b.MaxZ > room.HalfDepth))
            throw new InvalidDataException("Invalid authored campaign room.");
        var space = new SpatialWorld(room);
        if (!space.CanOccupy(room.PlayerSpawn, ActorRadius) || !space.CanOccupy(room.EnemySpawn, ActorRadius))
            throw new InvalidDataException("Campaign room entrance or enemy anchor is obstructed.");
    }

    private void MoveTowardTarget(CombatActor actor, Position target, int speed)
    {
        if (_navigation is not null)
        {
            if (!_navigation.TryWaypoint(actor.Position, target, out var waypoint)) return;
            target = waypoint;
        }
        MoveActor(actor, Toward(actor.Position, target, speed));
    }

    private bool ApproachAuthoredTarget(CombatActor actor)
    {
        if (_navigation is null || actor.Faction != CombatFaction.Enemy || actor.Role is "Anchor" or "Bell" ||
            Stunned(actor) || actor.Pending is not null || actor.RecoveryUntil > Tick || Player.Health <= 0) return false;
        var definition = _content.Enemies.Single(e => e.Id == actor.DefinitionId);
        if (Position.DistanceSquared(actor.Position, Player.Position) <= (long)definition.Range * definition.Range &&
            _spatial.HasLineOfSight(actor.Position, Player.Position)) return false;
        int speed = HasElite(actor, "Hunter") ? Math.Min(500, definition.Speed * 5 / 4) : definition.Speed;
        if (actor.Statuses.Any(s => s.Id == "Chilled" && s.ExpiresTick > Tick)) speed = speed * 2 / 3;
        actor.State = "Approach";
        MoveTowardTarget(actor, Player.Position, speed);
        return true;
    }
}

/// <summary>A bounded visibility graph over padded obstacle corners. Only immutable room
/// geometry is cached; route choices are recomputed from current positions after restore.</summary>
internal sealed class CombatRoomNavigation
{
    private readonly SpatialWorld _space, _padded;
    private readonly Position[] _corners;
    private readonly long[,] _edges;
    private readonly long[] _costs;
    private readonly bool[] _visited;

    public CombatRoomNavigation(RoomDefinition room)
    {
        _space = new(room);
        _padded = new(room with
        {
            Obstacles = room.Obstacles.Select(b => new Bounds(b.MinX - CombatSession.ActorRadius, b.MinZ - CombatSession.ActorRadius,
                b.MaxX + CombatSession.ActorRadius, b.MaxZ + CombatSession.ActorRadius)).ToArray()
        });
        const int clearance = CombatSession.ActorRadius + 200;
        var corners = new List<Position>();
        foreach (var obstacle in room.Obstacles)
            foreach (var point in new[]
            {
                new Position(obstacle.MinX - clearance, obstacle.MinZ - clearance),
                new Position(obstacle.MinX - clearance, obstacle.MaxZ + clearance),
                new Position(obstacle.MaxX + clearance, obstacle.MinZ - clearance),
                new Position(obstacle.MaxX + clearance, obstacle.MaxZ + clearance)
            })
                if (_space.CanOccupy(point, CombatSession.ActorRadius) && !corners.Contains(point)) corners.Add(point);
        _corners = corners.ToArray();
        _edges = new long[_corners.Length, _corners.Length];
        _costs = new long[_corners.Length]; _visited = new bool[_corners.Length];
        for (int from = 0; from < _corners.Length; from++)
            for (int to = 0; to < _corners.Length; to++)
                _edges[from, to] = Clear(_corners[from], _corners[to]) ? Distance(_corners[from], _corners[to]) : long.MaxValue;
    }

    public bool TryWaypoint(Position from, Position target, out Position waypoint)
    {
        waypoint = from;
        if (!_space.CanOccupy(from, CombatSession.ActorRadius) || !_space.CanOccupy(target, CombatSession.ActorRadius)) return false;
        if (Clear(from, target)) { waypoint = target; return true; }
        for (int i = 0; i < _corners.Length; i++)
        {
            _costs[i] = Clear(_corners[i], target) ? Distance(_corners[i], target) : long.MaxValue;
            _visited[i] = false;
        }
        // Reverse Dijkstra; ascending authored corner indices deterministically break ties.
        for (int count = 0; count < _corners.Length; count++)
        {
            int current = -1;
            for (int i = 0; i < _corners.Length; i++)
                if (!_visited[i] && _costs[i] < (current < 0 ? long.MaxValue : _costs[current])) current = i;
            if (current < 0) break;
            _visited[current] = true;
            for (int next = 0; next < _corners.Length; next++)
            {
                if (_visited[next] || _edges[current, next] == long.MaxValue) continue;
                long cost = _costs[current] + _edges[current, next];
                if (cost < _costs[next]) _costs[next] = cost;
            }
        }
        long best = long.MaxValue;
        for (int i = 0; i < _corners.Length; i++)
        {
            if (_costs[i] == long.MaxValue || !Clear(from, _corners[i])) continue;
            long cost = Distance(from, _corners[i]) + _costs[i];
            if (cost < best) { best = cost; waypoint = _corners[i]; }
        }
        return best != long.MaxValue;
    }

    private bool Clear(Position from, Position target) => _padded.HasLineOfSight(from, target);
    private static long Distance(Position from, Position target) => (long)Math.Ceiling(Math.Sqrt(Position.DistanceSquared(from, target)));
}
