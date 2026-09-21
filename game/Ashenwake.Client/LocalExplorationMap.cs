using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Exploration;
using Ashenwake.Core.Simulation;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

public enum LocalMapMarkerKind { Exit, Service, Treasure, Loot }

/// <summary>Presentation-only map symbol. Clicking it can request movement, never its world action.</summary>
public sealed record LocalMapMarker(string Id, string Label, CorePosition Position, LocalMapMarkerKind Kind);

/// <summary>A shared compact/expanded projection of the current room's discovered ground.</summary>
public partial class LocalExplorationMap : Control
{
    private static readonly Color Gold = new("e5c897"), Muted = new("a8bbc0"), Floor = new("263c40"), Wall = new("718084");
    private static readonly Color ExitColor = new("eed4a0"), ServiceColor = new("9ecedf"), TreasureColor = new("ddb4ec"), LootColor = new("efbd70");
    private readonly StyleBoxFlat _frame = new()
    {
        BgColor = new("101e27f5"),
        BorderColor = new("64767b"),
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        BorderWidthTop = 2,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 5,
        CornerRadiusTopRight = 5,
        CornerRadiusBottomLeft = 5,
        CornerRadiusBottomRight = 5
    };
    private Control _terrain = null!, _symbols = null!;
    private Button _open = null!, _close = null!;
    private Label _title = null!, _progress = null!, _legend = null!, _hint = null!, _north = null!;
    private LocalMapView? _view;
    private RoomDefinition? _room;
    private SpatialWorld? _space;
    private int[] _seenCells = [];
    private LocalMapMarker[] _markers = [];
    private CorePosition _player;
    private CorePosition? _destination;
    private Rect2 _mapRect;
    private string _roomTitle = "Local surroundings", _openKeyLabel = "M";

    public event Action? ToggleRequested, CloseRequested;
    public event Action<CorePosition>? DestinationRequested;
    public bool Expanded { get; private set; }
    public bool HasView => _view is not null;
    public string RoomId => _view?.RoomId ?? "";
    public string LayoutHash => _view?.LayoutHash ?? "";
    public int SeenCellCount => _seenCells.Length;
    public int DrawnMarkerCount => _markers.Length;
    public IReadOnlyList<string> DrawnMarkerIds => _markers.Select(marker => marker.Id).ToArray();
    public int ExplorationPercentage => _view is null ? 0 : (int)((long)_seenCells.Length * 100 / Math.Max(1, _view.Columns * _view.Rows));
    public Rect2 MapGlobalRect => new(GlobalPosition + _mapRect.Position, _mapRect.Size);
    public string StatusText => _progress?.Text ?? "";

    public LocalExplorationMap()
    {
        Name = "LocalExplorationMap"; Size = new(256, 194); CustomMinimumSize = new(180, 144);
        MouseFilter = MouseFilterEnum.Stop; FocusMode = FocusModeEnum.None;
        TooltipText = "Local map. Revealed ground, nearby services, exits, treasure, and remaining loot. Click explored ground to move.";
    }

    public override void _Ready() => EnsureChildren();

    public override void _Notification(int what)
    {
        if (what == NotificationResized && _terrain is not null) LayoutChildren();
        if (what == NotificationVisibilityChanged) FocusExpandedMap();
    }

    public void SetOpenKeyLabel(string label)
    {
        label = string.IsNullOrWhiteSpace(label) ? "M" : label;
        if (_openKeyLabel == label) return;
        _openKeyLabel = label; EnsureChildren(); RefreshText();
    }

    public void SetExpanded(bool expanded)
    {
        EnsureChildren();
        if (Expanded == expanded) return;
        Expanded = expanded; RefreshText(); LayoutChildren(); FocusExpandedMap();
    }

    private void FocusExpandedMap()
    {
        if (!Expanded || _close is null || !IsInsideTree() || !IsVisibleInTree()) return;
        // The backdrop blocks mouse input, while this focus ring also keeps Tab/Enter inside the modal.
        var path = _close.GetPath();
        _close.FocusNext = _close.FocusPrevious = _close.FocusNeighborTop = _close.FocusNeighborBottom =
            _close.FocusNeighborLeft = _close.FocusNeighborRight = path;
        _close.GrabFocus();
    }

    public void SetView(LocalMapView? view, string roomTitle, CorePosition player,
        IReadOnlyList<LocalMapMarker> markers, CorePosition? destination = null)
    {
        EnsureChildren();
        bool roomChanged = _view?.RoomId != view?.RoomId || _view?.LayoutHash != view?.LayoutHash;
        bool discoveredChanged = !ReferenceEquals(_view, view) && !_seenCells.SequenceEqual(view?.SeenCells ?? Array.Empty<int>());
        bool titleChanged = _roomTitle != roomTitle;
        Vector2 oldPlayer = SymbolPoint(_player);
        Vector2? oldDestination = _destination is { } previous ? SymbolPoint(previous) : null;
        var visibleMarkers = view is null ? [] : markers.Where(marker => view.IsExplored(marker.Position))
            .OrderBy(marker => marker.Kind).ThenBy(marker => marker.Id, StringComparer.Ordinal).ToArray();
        bool markersChanged = !_markers.SequenceEqual(visibleMarkers);
        _view = view; _player = player; _destination = destination; _roomTitle = roomTitle; _markers = visibleMarkers;
        if (roomChanged || _room is null)
        {
            _room = view?.Room; _space = _room is null ? null : new(_room);
        }
        if (discoveredChanged) _seenCells = view?.SeenCells.ToArray() ?? [];
        if (roomChanged) LayoutChildren();
        if (roomChanged || discoveredChanged) _terrain.QueueRedraw();
        Vector2? nextDestination = destination is { } next ? SymbolPoint(next) : null;
        if (roomChanged || discoveredChanged || markersChanged || oldPlayer != SymbolPoint(player) || oldDestination != nextDestination)
            _symbols.QueueRedraw();
        if (roomChanged || discoveredChanged || titleChanged) RefreshText();
    }

    public bool IsDiscovered(CorePosition position) => _view?.IsExplored(position) == true;

    /// <summary>Returns a point in this control's coordinates, including the fitted map margins.</summary>
    public Vector2 WorldToMapPosition(CorePosition position) => _mapRect.Position + MapPoint(position);
    public Vector2 GlobalMapPosition(CorePosition position) => GlobalPosition + WorldToMapPosition(position);

    /// <summary>Only known, walkable terrain can become a destination; root still owns actual path planning.</summary>
    public bool TryMapDestination(Vector2 localPosition, out CorePosition destination)
    {
        destination = default;
        if (_room is null || _view is null || _space is null || !_mapRect.HasPoint(localPosition) || _mapRect.Size.X <= 0 || _mapRect.Size.Y <= 0)
            return false;
        Vector2 relative = (localPosition - _mapRect.Position) / _mapRect.Size;
        destination = new((int)Math.Round(relative.X * (2L * _room.HalfWidth) - _room.HalfWidth),
            (int)Math.Round(relative.Y * (2L * _room.HalfDepth) - _room.HalfDepth));
        return _view.IsExplored(destination) && _space.CanOccupy(destination, CombatSession.ActorRadius);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            TooltipText = HintAt(motion.Position);
            MouseDefaultCursorShape = TryMapDestination(motion.Position, out _) ? CursorShape.PointingHand : CursorShape.Arrow;
        }
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse) return;
        // Also consume clicks on fog and panel margins, so they never become attacks in the world beneath the map.
        AcceptEvent();
        if (TryMapDestination(mouse.Position, out var destination)) DestinationRequested?.Invoke(destination);
    }

    private string HintAt(Vector2 localPosition)
    {
        if (!_mapRect.HasPoint(localPosition)) return "Local map · " + _roomTitle + ". Press " + _openKeyLabel + " to " + (Expanded ? "close." : "expand.");
        var nearest = _markers.Select(marker => (Marker: marker, Distance: WorldToMapPosition(marker.Position).DistanceSquaredTo(localPosition)))
            .Where(candidate => candidate.Distance <= 100).OrderBy(candidate => candidate.Distance).FirstOrDefault();
        if (nearest.Marker is not null) return nearest.Marker.Label + ". Click walkable ground nearby to approach; use the world marker to interact.";
        if (TryMapDestination(localPosition, out _)) return "Explored ground · click to move here using your current route.";
        return "Unexplored or blocked ground. Explore nearby passages to reveal the map.";
    }

    private void EnsureChildren()
    {
        if (_terrain is not null) return;
        _terrain = new Control { Name = "LocalMapTerrain", MouseFilter = MouseFilterEnum.Ignore };
        _terrain.Draw += DrawTerrain; AddChild(_terrain);
        _symbols = new Control { Name = "LocalMapSymbols", MouseFilter = MouseFilterEnum.Ignore };
        _symbols.Draw += DrawSymbols; AddChild(_symbols);
        _open = new Button { Name = "LocalMapOpen", Alignment = HorizontalAlignment.Left, ClipText = true, FocusMode = FocusModeEnum.All };
        _open.AddThemeFontSizeOverride("font_size", 12); _open.AddThemeColorOverride("font_color", Gold);
        _open.Pressed += () => ToggleRequested?.Invoke(); AddChild(_open);
        _close = new Button { Name = "LocalMapClose", Text = "Close [Esc]", FocusMode = FocusModeEnum.All };
        _close.AddThemeFontSizeOverride("font_size", 13); _close.TooltipText = "Close the local map and return to play.";
        _close.Pressed += () => CloseRequested?.Invoke(); AddChild(_close);
        _title = Caption("LocalMapTitle", 21, Gold);
        _progress = Caption("LocalMapProgress", 11, Muted);
        _legend = Caption("LocalMapLegend", 13, new("d1ddda"));
        _hint = Caption("LocalMapHint", 12, Muted);
        _north = Caption("LocalMapNorth", 11, Gold); _north.Text = "N ↑";
        _north.TooltipText = "North. The local map keeps a fixed orientation.";
        RefreshText(); LayoutChildren();
    }

    private Label Caption(string name, int size, Color color)
    {
        var label = new Label { Name = name, MouseFilter = MouseFilterEnum.Ignore, ClipText = true };
        label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", color);
        AddChild(label); return label;
    }

    private void RefreshText()
    {
        if (_open is null) return;
        _open.Visible = !Expanded; _close.Visible = Expanded; _title.Visible = Expanded;
        _legend.Visible = Expanded; _hint.Visible = Expanded;
        _open.Text = "LOCAL MAP [" + _openKeyLabel + "]";
        _open.TooltipText = "Open the local exploration map. Shortcut: " + _openKeyLabel + ".";
        _title.Text = _roomTitle;
        _progress.Text = _view is null ? "No local map" : (Expanded ? "LOCAL EXPLORATION · " : _roomTitle + " · ") + ExplorationPercentage + "% revealed";
        _progress.TooltipText = _roomTitle + " · " + ExplorationPercentage + "% of this room revealed. Exploration remains recorded when you leave.";
        _legend.Text = "▲ You   ◇ Exit   ○ Service   ▣ Treasure   ◆ Loot   ⊙ Destination";
        _hint.Text = "Click explored ground to move · " + _openKeyLabel + " or Esc closes the map";
    }

    private void LayoutChildren()
    {
        if (_terrain is null) return;
        float width = Size.X, height = Size.Y;
        _title.AddThemeFontSizeOverride("font_size", width < 600 ? 18 : 21);
        _legend.AddThemeFontSizeOverride("font_size", width < 640 ? 11 : 13);
        _hint.AddThemeFontSizeOverride("font_size", width < 600 ? 11 : 12);
        _open.Position = new(8, 5); _open.Size = new(Math.Max(20, width - 16), 26);
        _title.Position = new(22, 14); _title.Size = new(Math.Max(20, width - 172), 32);
        _close.Position = new(width - 132, 16); _close.Size = new(110, 30);
        _progress.Position = Expanded ? new(23, 47) : new(10, height - 22);
        _progress.Size = new(Math.Max(20, width - (Expanded ? 46 : 20)), 18);
        _progress.HorizontalAlignment = Expanded ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        _legend.Position = new(22, height - 57); _legend.Size = new(Math.Max(20, width - 44), 22);
        _hint.Position = new(22, height - 32); _hint.Size = new(Math.Max(20, width - 44), 20);
        Rect2 available = Expanded ? new(new Vector2(24, 81), new Vector2(Math.Max(1, width - 48), Math.Max(1, height - 153)))
            : new(new Vector2(13, 37), new Vector2(Math.Max(1, width - 26), Math.Max(1, height - 64)));
        float aspect = _room is { HalfDepth: > 0 } ? (float)_room.HalfWidth / _room.HalfDepth : 1.2f;
        Vector2 fitted = available.Size;
        if (fitted.X / fitted.Y > aspect) fitted.X = fitted.Y * aspect; else fitted.Y = fitted.X / aspect;
        _mapRect = new(available.Position + (available.Size - fitted) * .5f, fitted);
        _terrain.Position = _symbols.Position = _mapRect.Position; _terrain.Size = _symbols.Size = fitted;
        _north.Position = new(Expanded ? width - 63 : width - 40, Expanded ? 65 : 37); _north.Size = new(35, 18);
        _terrain.QueueRedraw(); _symbols.QueueRedraw(); QueueRedraw();
    }

    public override void _Draw() => DrawStyleBox(_frame, new(Vector2.Zero, Size));

    private Vector2 MapPoint(CorePosition position) => _room is null ? Vector2.Zero : new(
        (float)((position.X + (double)_room.HalfWidth) / (2L * _room.HalfWidth) * _mapRect.Size.X),
        (float)((position.Z + (double)_room.HalfDepth) / (2L * _room.HalfDepth) * _mapRect.Size.Y));
    private Vector2 SymbolPoint(CorePosition position) => MapPoint(position).Round();

    private void DrawTerrain()
    {
        _terrain.DrawRect(new(Vector2.Zero, _mapRect.Size), new("0b141ce8"));
        if (_view is null || _room is null) return;
        // Every visible geometry fragment is clipped to a discovered grid cell. Undiscovered walls stay hidden.
        foreach (int cell in _seenCells)
        {
            int column = cell % _view.Columns, row = cell / _view.Columns;
            int minX = -_room.HalfWidth + column * _view.CellSize, minZ = -_room.HalfDepth + row * _view.CellSize;
            int maxX = Math.Min(_room.HalfWidth, minX + _view.CellSize), maxZ = Math.Min(_room.HalfDepth, minZ + _view.CellSize);
            var first = MapPoint(new(minX, minZ)); var last = MapPoint(new(maxX, maxZ));
            _terrain.DrawRect(new(first, last - first), Floor);
            foreach (var obstacle in _room.Obstacles)
            {
                int left = Math.Max(minX, obstacle.MinX), top = Math.Max(minZ, obstacle.MinZ);
                int right = Math.Min(maxX, obstacle.MaxX), bottom = Math.Min(maxZ, obstacle.MaxZ);
                if (left >= right || top >= bottom) continue;
                var at = MapPoint(new(left, top)); var end = MapPoint(new(right, bottom));
                _terrain.DrawRect(new(at, end - at), Wall.Darkened(.25f));
                if (obstacle.MinX >= minX && obstacle.MinX < maxX) _terrain.DrawLine(at, new(at.X, end.Y), Wall, 1);
                if (obstacle.MaxX > minX && obstacle.MaxX <= maxX) _terrain.DrawLine(new(end.X, at.Y), end, Wall, 1);
                if (obstacle.MinZ >= minZ && obstacle.MinZ < maxZ) _terrain.DrawLine(at, new(end.X, at.Y), Wall, 1);
                if (obstacle.MaxZ > minZ && obstacle.MaxZ <= maxZ) _terrain.DrawLine(new(at.X, end.Y), end, Wall, 1);
            }
        }
        _terrain.DrawRect(new(Vector2.Zero, _mapRect.Size), new("43585e"), false, 1);
    }

    private void DrawSymbols()
    {
        if (_view is null || _room is null) return;
        float radius = Expanded ? 6 : 4;
        foreach (var marker in _markers) DrawMarker(SymbolPoint(marker.Position), marker.Kind, radius);
        if (_destination is { } destination && IsDiscovered(destination))
        {
            Vector2 at = SymbolPoint(destination);
            _symbols.DrawCircle(at, radius + 3, new("172930"));
            _symbols.DrawArc(at, radius + 2, 0, Mathf.Tau, 24, Gold, 1.5f, true);
            _symbols.DrawLine(at - new Vector2(3, 0), at + new Vector2(3, 0), Gold, 1.2f, true);
            _symbols.DrawLine(at - new Vector2(0, 3), at + new Vector2(0, 3), Gold, 1.2f, true);
        }
        Vector2 player = SymbolPoint(_player);
        _symbols.DrawCircle(player, radius + 4, new("0c1925"));
        _symbols.DrawColoredPolygon([player + new Vector2(0, -radius - 2), player + new Vector2(radius, radius),
            player + new Vector2(0, radius - 2), player + new Vector2(-radius, radius)], new("edf7f4"));
    }

    private void DrawMarker(Vector2 at, LocalMapMarkerKind kind, float radius)
    {
        Color color = kind switch { LocalMapMarkerKind.Exit => ExitColor, LocalMapMarkerKind.Service => ServiceColor, LocalMapMarkerKind.Treasure => TreasureColor, _ => LootColor };
        _symbols.DrawCircle(at, radius + 2, new("0b1821"));
        switch (kind)
        {
            case LocalMapMarkerKind.Exit:
                _symbols.DrawPolyline([at + new Vector2(0, -radius), at + new Vector2(radius, 0), at + new Vector2(0, radius),
                    at + new Vector2(-radius, 0), at + new Vector2(0, -radius)], color, 1.5f, true);
                break;
            case LocalMapMarkerKind.Service:
                _symbols.DrawArc(at, radius, 0, Mathf.Tau, 20, color, 1.5f, true);
                _symbols.DrawCircle(at, 1.3f, color);
                break;
            case LocalMapMarkerKind.Treasure:
                _symbols.DrawRect(new(at - new Vector2(radius, radius * .8f), new(radius * 2, radius * 1.6f)), color, false, 1.5f);
                _symbols.DrawLine(at - new Vector2(radius, 0), at + new Vector2(radius, 0), color, 1, true);
                _symbols.DrawLine(at - new Vector2(0, radius * .6f), at + new Vector2(0, radius * .6f), color, 1, true);
                break;
            default:
                _symbols.DrawColoredPolygon([at + new Vector2(0, -radius), at + new Vector2(radius, 0), at + new Vector2(0, radius), at + new Vector2(-radius, 0)], color);
                break;
        }
    }
}
