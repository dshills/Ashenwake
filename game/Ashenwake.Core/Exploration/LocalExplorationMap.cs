using System.Text.Json;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Exploration;

public sealed record LocalMapRoomState
{
    public int SchemaVersion { get; init; } = 1;
    public string LayoutHash { get; init; } = "";
    public int HalfWidth { get; init; }
    public int HalfDepth { get; init; }
    public int CellSize { get; init; }
    public int[] SeenCells { get; init; } = [];
}

public sealed record LocalMapAtlasState
{
    public int SchemaVersion { get; init; } = 1;
    public string RulesVersion { get; init; } = "local-map.1";
    public SortedDictionary<string, LocalMapRoomState> Rooms { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>A stable, immutable view; unchanged discovery reuses the same instance.</summary>
public sealed class LocalMapView
{
    private readonly RoomDefinition room;
    private readonly HashSet<int> seen;
    public string RoomId { get; }
    public string LayoutHash { get; }
    public RoomDefinition Room => room with { Obstacles = room.Obstacles.ToArray() };
    public int CellSize { get; }
    public int Columns { get; }
    public int Rows { get; }
    public IReadOnlyList<int> SeenCells { get; }
    internal LocalMapView(string id, RoomDefinition room, LocalMapRoomState state)
    {
        RoomId = id; this.room = room with { Obstacles = room.Obstacles.ToArray() };
        LayoutHash = state.LayoutHash; CellSize = state.CellSize;
        Columns = (room.HalfWidth * 2 + CellSize - 1) / CellSize;
        Rows = (room.HalfDepth * 2 + CellSize - 1) / CellSize;
        SeenCells = Array.AsReadOnly(state.SeenCells.ToArray()); seen = state.SeenCells.ToHashSet();
    }
    public bool IsExplored(Position point) => Math.Abs((long)point.X) <= room.HalfWidth && Math.Abs((long)point.Z) <= room.HalfDepth &&
        seen.Contains(Math.Min(Rows - 1, (point.Z + room.HalfDepth) / CellSize) * Columns + Math.Min(Columns - 1, (point.X + room.HalfWidth) / CellSize));
    public Position CellCenter(int cellId)
    {
        if (cellId < 0 || cellId >= Columns * Rows) throw new ArgumentOutOfRangeException(nameof(cellId));
        int x = cellId % Columns * CellSize, z = cellId / Columns * CellSize;
        return new(-room.HalfWidth + x + Math.Min(CellSize, room.HalfWidth * 2 - x) / 2,
            -room.HalfDepth + z + Math.Min(CellSize, room.HalfDepth * 2 - z) / 2);
    }
}

/// <summary>Bounded room memory. It changes only after explicit opt-in and successful runtime commands.</summary>
internal sealed class LocalMapAtlas
{
    internal const int MaximumRooms = 96, MaximumAxisCells = 96, RevealRadius = 3600;
    private readonly LocalMapAtlasState state;
    private readonly Dictionary<string, LocalMapView> views = new(StringComparer.Ordinal);
    public LocalMapAtlas() : this(new()) { }
    private LocalMapAtlas(LocalMapAtlasState state) => this.state = state;
    public LocalMapAtlasState Capture() => JsonData.Copy(state);
    internal static int CellSizeFor(RoomDefinition room) => Math.Max(500,
        ((Math.Max(room.HalfWidth, room.HalfDepth) * 2 + MaximumAxisCells - 1) / MaximumAxisCells + 499) / 500 * 500);
    private static LocalMapRoomState Empty(RoomDefinition room) => new()
    { LayoutHash = JsonData.Hash(room), HalfWidth = room.HalfWidth, HalfDepth = room.HalfDepth, CellSize = CellSizeFor(room) };
    public static LocalMapAtlas? Restore(LocalMapAtlasState? source, Func<string, RoomDefinition?> resolve)
    {
        if (source is null) return null;
        if (source.SchemaVersion != 1 || source.RulesVersion != "local-map.1" || source.Rooms is null || source.Rooms.Count > MaximumRooms)
            throw new InvalidDataException("Invalid local exploration atlas header or size.");
        foreach (var (id, saved) in source.Rooms)
        {
            var room = resolve(id) ?? throw new InvalidDataException("Unknown local exploration room identity.");
            int size = CellSizeFor(room), columns = (room.HalfWidth * 2 + size - 1) / size, rows = (room.HalfDepth * 2 + size - 1) / size;
            if (saved is null || saved.SchemaVersion != 1 || saved.LayoutHash != JsonData.Hash(room) || saved.HalfWidth != room.HalfWidth ||
                saved.HalfDepth != room.HalfDepth || saved.CellSize != size || saved.SeenCells is null || saved.SeenCells.Length > columns * rows ||
                saved.SeenCells.Any(cell => cell < 0 || cell >= columns * rows) || !saved.SeenCells.SequenceEqual(saved.SeenCells.Distinct().Order()))
                throw new InvalidDataException("Invalid local exploration room layout or discovered cells.");
        }
        return new(JsonData.Copy(source));
    }
    public LocalMapView? View(string id, RoomDefinition room)
    {
        if (!state.Rooms.TryGetValue(id, out var entry)) return null;
        if (!views.TryGetValue(id, out var view)) views[id] = view = new(id, room, entry);
        return view;
    }
    public void Reveal(string id, RoomDefinition room, Position player)
    {
        if (!state.Rooms.TryGetValue(id, out var entry))
        {
            if (state.Rooms.Count >= MaximumRooms) throw new InvalidDataException("Exploration atlas capacity requires migration.");
            state.Rooms[id] = entry = Empty(room);
        }
        var view = View(id, room)!;
        var seen = entry.SeenCells.ToHashSet(); int before = seen.Count;
        int radius = Math.Max(RevealRadius, view.CellSize * 2);
        int minX = Math.Clamp((player.X - radius + room.HalfWidth) / view.CellSize, 0, view.Columns - 1);
        int maxX = Math.Clamp((player.X + radius + room.HalfWidth) / view.CellSize, 0, view.Columns - 1);
        int minZ = Math.Clamp((player.Z - radius + room.HalfDepth) / view.CellSize, 0, view.Rows - 1);
        int maxZ = Math.Clamp((player.Z + radius + room.HalfDepth) / view.CellSize, 0, view.Rows - 1);
        var space = new SpatialWorld(room);
        for (int z = minZ; z <= maxZ; z++) for (int x = minX; x <= maxX; x++)
            {
                int cell = z * view.Columns + x; if (seen.Contains(cell)) continue;
                var center = view.CellCenter(cell);
                if (Position.DistanceSquared(player, center) > (long)radius * radius) continue;
                // A visible wall face is mapped too; a ray to a wall's interior would hide the wall itself.
                var target = center;
                foreach (var wall in room.Obstacles)
                    if (center.X > wall.MinX && center.X < wall.MaxX && center.Z > wall.MinZ && center.Z < wall.MaxZ)
                    {
                        int cellX = -room.HalfWidth + x * view.CellSize, cellZ = -room.HalfDepth + z * view.CellSize;
                        target = new(Math.Clamp(player.X, Math.Max(wall.MinX, cellX), Math.Min(wall.MaxX, cellX + view.CellSize)),
                            Math.Clamp(player.Z, Math.Max(wall.MinZ, cellZ), Math.Min(wall.MaxZ, cellZ + view.CellSize)));
                        break;
                    }
                if (space.HasLineOfSight(player, target)) seen.Add(cell);
            }
        int playerX = Math.Clamp((player.X + room.HalfWidth) / view.CellSize, 0, view.Columns - 1);
        int playerZ = Math.Clamp((player.Z + room.HalfDepth) / view.CellSize, 0, view.Rows - 1);
        seen.Add(playerZ * view.Columns + playerX);
        if (seen.Count != before)
        { state.Rooms[id] = entry with { SeenCells = seen.Order().ToArray() }; views.Remove(id); }
    }
    internal static LocalMapAtlasState? Rebind(LocalMapAtlasState? source, Func<string, RoomDefinition?> resolve)
    {
        if (source is null) return null;
        var result = new LocalMapAtlasState();
        foreach (var (id, entry) in source.Rooms)
        {
            var room = resolve(id) ?? throw new InvalidDataException("Migrated exploration room is unavailable.");
            result.Rooms.Add(id, entry.LayoutHash == JsonData.Hash(room) ? JsonData.Copy(entry) : Empty(room));
        }
        return result;
    }
    internal static void Inspect(JsonElement owner)
    {
        if (!owner.TryGetProperty("explorationMap", out var map) || map.ValueKind == JsonValueKind.Null) return;
        ArchiveHeaders.Require(map, 1, "local-map.1");
        foreach (var room in ArchiveHeaders.Object(map, "rooms").EnumerateObject()) ArchiveHeaders.Require(room.Value, 1);
    }
}
