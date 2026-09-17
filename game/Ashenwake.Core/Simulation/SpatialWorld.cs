using Ashenwake.Core.Content;
using System.Text.Json.Serialization;

namespace Ashenwake.Core.Simulation;

// Integer millimeters on a flat X/Z plane. No engine geometry or floating-point physics.
public readonly record struct Position([property: JsonRequired] int X, [property: JsonRequired] int Z)
{
    public static long DistanceSquared(Position a, Position b)
    {
        long x = (long)a.X - b.X, z = (long)a.Z - b.Z;
        return checked(x * x + z * z);
    }
}
public readonly record struct Bounds([property: JsonRequired] int MinX, [property: JsonRequired] int MinZ, [property: JsonRequired] int MaxX, [property: JsonRequired] int MaxZ);

public interface ISpatialQueries
{
    bool CanOccupy(Position position, int radius);
    bool HasLineOfSight(Position from, Position to);
    IReadOnlyList<int> Overlap(Position center, int radius, IEnumerable<EntityState> entities);
    Position Move(Position from, Position desired, int radius);
}

public sealed class SpatialWorld(RoomDefinition room) : ISpatialQueries
{
    public bool CanOccupy(Position p, int radius)
    {
        if (Math.Abs((long)p.X) + radius > room.HalfWidth || Math.Abs((long)p.Z) + radius > room.HalfDepth) return false;
        return !room.Obstacles.Any(b => p.X + radius > b.MinX && p.X - radius < b.MaxX &&
            p.Z + radius > b.MinZ && p.Z - radius < b.MaxZ);
    }

    public Position Move(Position from, Position desired, int radius)
    {
        // Sweep prevents tunneling through thin obstacles. Slide deterministically, X before Z.
        var x = new Position(desired.X, from.Z);
        if (!CanOccupy(x, radius) || IntersectsAny(from, x, radius)) x = from;
        var z = new Position(x.X, desired.Z);
        return CanOccupy(z, radius) && !IntersectsAny(x, z, radius) ? z : x;
    }

    public bool HasLineOfSight(Position from, Position to) => !IntersectsAny(from, to, 0);

    public IReadOnlyList<int> Overlap(Position center, int radius, IEnumerable<EntityState> entities) => entities
        .Where(e => e.Health > 0 && Position.DistanceSquared(center, e.Position) <= (long)radius * radius)
        .Select(e => e.Id).Order().ToArray();

    private bool IntersectsAny(Position from, Position to, int padding) => room.Obstacles.Any(b =>
        SegmentIntersects(from, to, new(b.MinX - padding, b.MinZ - padding, b.MaxX + padding, b.MaxZ + padding)));

    private static bool SegmentIntersects(Position a, Position b, Bounds box)
    {
        // Decimal slabs avoid machine-dependent floating-point geometry at collision boundaries.
        decimal low = 0, high = 1;
        bool Clip(int start, int end, int min, int max)
        {
            var delta = (long)end - start;
            if (delta == 0) return start > min && start < max;
            var first = ((decimal)min - start) / delta;
            var last = ((decimal)max - start) / delta;
            if (first > last) (first, last) = (last, first);
            low = Math.Max(low, first); high = Math.Min(high, last);
            return low < high;
        }
        return Clip(a.X, b.X, box.MinX, box.MaxX) && Clip(a.Z, b.Z, box.MinZ, box.MaxZ) && high > 0 && low < 1;
    }
}
