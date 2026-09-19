using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

/// <summary>
/// A local input aid, never simulation state: turns a floor destination into ordinary digital Move input.
/// Call once per eligible movement tick with the current authoritative position and other living bodies.
/// </summary>
public sealed class ClickMovePlanner
{
    public const int ArrivalTolerance = 180;
    public const int StuckCallLimit = 60;
    public const int MaximumRouteCalls = 4096;
    private const int MaximumObstacles = 100;
    private const int MaximumDetourBodies = 8;
    private const int ReplanInterval = 8;
    private const int MaximumEdgeQueries = 131072;
    private const int BodySeparation = CombatSession.ActorRadius * 2;
    private static readonly Position[] Directions = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1), new(1, 1), new(-1, 1), new(-1, -1), new(1, -1)];
    private readonly RoomDefinition _room;
    private readonly SpatialWorld _spatial;
    private readonly SpatialWorld _padded;
    private readonly Position[] _corners;
    private readonly long[,] _staticEdges;
    private Position[] _route = [];
    private long[] _remaining = [];
    private Position? _lastPosition;
    private int _unchangedCalls;
    private int _calls;
    private int _sincePlan;

    public Position? Destination { get; private set; }

    public ClickMovePlanner(RoomDefinition room)
    {
        ArgumentNullException.ThrowIfNull(room);
        if (room.HalfWidth is <= CombatSession.ActorRadius or > 100000 || room.HalfDepth is <= CombatSession.ActorRadius or > 100000 ||
            room.Obstacles is null || room.Obstacles.Length > MaximumObstacles || room.Obstacles.Any(b => b.MinX >= b.MaxX || b.MinZ >= b.MaxZ ||
                b.MinX < -room.HalfWidth || b.MaxX > room.HalfWidth || b.MinZ < -room.HalfDepth || b.MaxZ > room.HalfDepth))
            throw new ArgumentException("Invalid click movement room.", nameof(room));
        _room = room with { Obstacles = room.Obstacles.ToArray() };
        _spatial = new(_room);
        _padded = new(_room with
        {
            Obstacles = _room.Obstacles.Select(b => new Bounds(b.MinX - CombatSession.ActorRadius, b.MinZ - CombatSession.ActorRadius,
            b.MaxX + CombatSession.ActorRadius, b.MaxZ + CombatSession.ActorRadius)).ToArray()
        });
        var corners = new List<Position>();
        foreach (var obstacle in _room.Obstacles)
            foreach (var sign in new[] { new Position(-1, -1), new Position(-1, 1), new Position(1, -1), new Position(1, 1) })
            {
                // Leave room for digital steering. Near a wall, retain the tighter legal corner instead.
                var corner = Corner(obstacle, sign, CombatSession.ActorRadius + 200);
                if (!_spatial.CanOccupy(corner, CombatSession.ActorRadius)) corner = Corner(obstacle, sign, CombatSession.ActorRadius + 1);
                if (_spatial.CanOccupy(corner, CombatSession.ActorRadius)) corners.Add(corner);
            }
        _corners = corners.Distinct().ToArray();
        _staticEdges = new long[_corners.Length, _corners.Length];
        for (int i = 0; i < _corners.Length; i++)
            for (int j = 0; j < _corners.Length; j++) _staticEdges[i, j] = -1;
    }

    public bool TrySetDestination(Position from, Position requested, IReadOnlyList<Position>? occupied = null)
    {
        Cancel();
        occupied ??= [];
        if (!_spatial.CanOccupy(from, CombatSession.ActorRadius) || !ValidBodies(occupied)) return false;
        var target = new Position(Math.Clamp(requested.X, -_room.HalfWidth + CombatSession.ActorRadius, _room.HalfWidth - CombatSession.ActorRadius),
            Math.Clamp(requested.Z, -_room.HalfDepth + CombatSession.ActorRadius, _room.HalfDepth - CombatSession.ActorRadius));
        if (!_spatial.CanOccupy(target, CombatSession.ActorRadius) || Occupied(target, occupied)) return false;
        if (Position.DistanceSquared(from, target) <= (long)ArrivalTolerance * ArrivalTolerance) return true;
        Destination = target;
        if (Plan(from, occupied)) return true;
        Cancel();
        return false;
    }

    public Position NextDirection(Position from, IReadOnlyList<Position>? occupied = null)
    {
        if (Destination is not { } target) return default;
        occupied ??= [];
        if (!ValidBodies(occupied) || !_spatial.CanOccupy(from, CombatSession.ActorRadius) ||
            Position.DistanceSquared(from, target) <= (long)ArrivalTolerance * ArrivalTolerance || ++_calls > MaximumRouteCalls)
        {
            Cancel();
            return default;
        }
        _unchangedCalls = _lastPosition == from ? _unchangedCalls + 1 : 0;
        _lastPosition = from;
        if (_unchangedCalls >= StuckCallLimit) { Cancel(); return default; }
        _sincePlan++;
        var direction = ChooseDirection(from, occupied);
        // Static edges are cached; moving bodies trigger at most one bounded graph search per eight calls.
        if (direction == default && _sincePlan >= ReplanInterval)
        {
            Plan(from, occupied);
            direction = ChooseDirection(from, occupied);
        }
        return direction;
    }

    /// <summary>Choose a reachable approach inside an authoritative action radius. This is input intent only.</summary>
    public bool TrySetApproach(Position from, Position target, int range, IReadOnlyList<Position>? occupied = null)
    {
        Cancel();
        if (range <= ArrivalTolerance || range > 100000) return false;
        if (Position.DistanceSquared(from, target) <= (long)range * range) return true;
        int radius = Math.Max(0, range - ArrivalTolerance - 150);
        double angle = Math.Atan2(from.Z - target.Z, from.X - target.X);
        var candidates = Enumerable.Range(0, 16).Select(i =>
        {
            double bearing = angle + i * Math.PI / 8;
            return new Position(target.X + (int)Math.Round(Math.Cos(bearing) * radius), target.Z + (int)Math.Round(Math.Sin(bearing) * radius));
        }).Append(target).OrderBy(p => Position.DistanceSquared(from, p)).ThenBy(p => p.X).ThenBy(p => p.Z);
        foreach (var candidate in candidates)
            if (TrySetDestination(from, candidate, occupied) &&
                Position.DistanceSquared(Destination ?? from, target) <= (long)(range - ArrivalTolerance) * (range - ArrivalTolerance)) return true;
        Cancel();
        return false;
    }

    public void Cancel()
    {
        Destination = null;
        _route = [];
        _remaining = [];
        _lastPosition = null;
        _unchangedCalls = _calls = _sincePlan = 0;
    }

    private bool Plan(Position from, IReadOnlyList<Position> occupied)
    {
        _sincePlan = 0;
        _route = [];
        _remaining = [];
        if (Destination is not { } target || Occupied(target, occupied)) return false;
        if (Clear(from, target, occupied)) { SetRoute([target]); return true; }
        // At most 466 vertices: target, start, four corners per rectangle, eight points per nearby body.
        // Every body still participates in collision checks, including bodies without a detour vertex.
        var points = new List<Position> { target, from };
        points.AddRange(_corners);
        foreach (var body in occupied.OrderBy(p => Position.DistanceSquared(from, p)).ThenBy(p => p.X).ThenBy(p => p.Z).Take(MaximumDetourBodies))
            foreach (var direction in Directions)
            {
                int offset = direction.X != 0 && direction.Z != 0 ? 538 : 760;
                var point = new Position(body.X + direction.X * offset, body.Z + direction.Z * offset);
                if (_spatial.CanOccupy(point, CombatSession.ActorRadius) && !Occupied(point, occupied)) points.Add(point);
            }
        var costs = Enumerable.Repeat(long.MaxValue, points.Count).ToArray();
        var previous = Enumerable.Repeat(-1, points.Count).ToArray();
        var visited = new bool[points.Count];
        costs[0] = 0;
        int queries = 0;
        for (int count = 0; count < points.Count; count++)
        {
            int current = -1;
            for (int i = 0; i < points.Count; i++)
                if (!visited[i] && costs[i] < (current < 0 ? long.MaxValue : costs[current])) current = i;
            if (current < 0 || current == 1) break;
            visited[current] = true;
            for (int next = 0; next < points.Count; next++)
            {
                if (visited[next] || current == next) continue;
                if (++queries > MaximumEdgeQueries) return false;
                long edge;
                if (current is >= 2 && current < _corners.Length + 2 && next is >= 2 && next < _corners.Length + 2)
                {
                    int a = current - 2, b = next - 2;
                    edge = _staticEdges[a, b];
                    if (edge == -1) _staticEdges[a, b] = _staticEdges[b, a] = edge = _padded.HasLineOfSight(points[current], points[next]) ? Distance(points[current], points[next]) : -2;
                    if (edge < 0 || !BodyClear(points[current], points[next], occupied)) continue;
                }
                else
                {
                    if (!Clear(points[current], points[next], occupied)) continue;
                    edge = Distance(points[current], points[next]);
                }
                long cost = costs[current] + edge;
                if (cost < costs[next]) { costs[next] = cost; previous[next] = current; }
            }
        }
        if (previous[1] < 0) return false;
        var route = new List<Position>();
        for (int next = previous[1]; next >= 0; next = previous[next]) route.Add(points[next]);
        SetRoute(route.ToArray());
        return true;
    }

    private void SetRoute(Position[] route)
    {
        _route = route;
        _remaining = new long[route.Length];
        for (int i = route.Length - 2; i >= 0; i--) _remaining[i] = _remaining[i + 1] + Distance(route[i], route[i + 1]);
    }

    private Position ChooseDirection(Position from, IReadOnlyList<Position> occupied)
    {
        long bestCost = RemainingCost(from, occupied);
        Position best = default;
        foreach (var direction in Directions)
        {
            int step = direction.X != 0 && direction.Z != 0 ? 106 : 150;
            var moved = _spatial.Move(from, new(from.X + direction.X * step, from.Z + direction.Z * step), CombatSession.ActorRadius);
            if (moved == from || !BodyClear(from, moved, occupied)) continue;
            long cost = RemainingCost(moved, occupied);
            if (cost < bestCost) { best = direction; bestCost = cost; }
        }
        return best;
    }

    private long RemainingCost(Position from, IReadOnlyList<Position> occupied)
    {
        long best = long.MaxValue;
        for (int i = _route.Length - 1; i >= 0; i--)
        {
            long cost = Distance(from, _route[i]) + _remaining[i];
            if (cost < best && Clear(from, _route[i], occupied)) best = cost;
        }
        return best;
    }

    private bool Clear(Position from, Position to, IReadOnlyList<Position> occupied) => _padded.HasLineOfSight(from, to) && BodyClear(from, to, occupied);
    private static bool ValidBodies(IReadOnlyList<Position> occupied) => occupied.Count <= CombatSession.MaxActors && occupied.All(p => Math.Abs((long)p.X) <= 100000 && Math.Abs((long)p.Z) <= 100000);
    private static bool Occupied(Position position, IReadOnlyList<Position> occupied) => occupied.Any(p => Position.DistanceSquared(position, p) < (long)BodySeparation * BodySeparation);
    private static long Distance(Position a, Position b) => (long)Math.Ceiling(Math.Sqrt(Position.DistanceSquared(a, b)));
    private static Position Corner(Bounds obstacle, Position sign, int clearance) => new(sign.X < 0 ? obstacle.MinX - clearance : obstacle.MaxX + clearance,
        sign.Z < 0 ? obstacle.MinZ - clearance : obstacle.MaxZ + clearance);

    private static bool BodyClear(Position from, Position to, IReadOnlyList<Position> occupied)
    {
        long dx = (long)to.X - from.X, dz = (long)to.Z - from.Z, length = dx * dx + dz * dz;
        foreach (var body in occupied)
        {
            long px = (long)body.X - from.X, pz = (long)body.Z - from.Z;
            long projection = px * dx + pz * dz;
            const long clearanceSquared = (long)BodySeparation * BodySeparation;
            if (length == 0 || projection <= 0)
            { if (px * px + pz * pz < clearanceSquared) return false; }
            else if (projection >= length)
            { long x = (long)body.X - to.X, z = (long)body.Z - to.Z; if (x * x + z * z < clearanceSquared) return false; }
            else
            {
                // Squared cross products can exceed Int64 for valid 200 m room diagonals.
                // Int128 keeps the strict tangent comparison exact without decimal division.
                long cross = px * dz - pz * dx;
                if ((Int128)cross * cross < (Int128)clearanceSquared * length) return false;
            }
        }
        return true;
    }
}
