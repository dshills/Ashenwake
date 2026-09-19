using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>A cosmetic approach target for the HUD's existing contextual action; it never advances the campaign itself.</summary>
public partial class CampaignStage
{
    private Node3D? _wayForward;
    private RoomDefinition? _wayForwardRoom;
    private CorePosition? _wayForwardPoint;

    public WorldInteractionTarget? PresentWayForward(RoomDefinition room, bool available, string label)
    {
        if (!available)
        {
            if (_wayForward is not null) _wayForward.Visible = false;
            return null;
        }
        if (_wayForwardRoom is null || _wayForwardRoom.HalfWidth != room.HalfWidth || _wayForwardRoom.HalfDepth != room.HalfDepth ||
            !_wayForwardRoom.Obstacles.SequenceEqual(room.Obstacles))
        {
            _wayForwardRoom = room with { Obstacles = room.Obstacles.ToArray() };
            _wayForwardPoint = FindWayForwardPoint(room);
        }
        if (_wayForwardPoint is not { } point)
        {
            if (_wayForward is not null) _wayForward.Visible = false;
            return null;
        }
        _wayForward ??= BuildWayForward();
        _wayForward.Position = new(point.X * .001f, 0, point.Z * .001f);
        _wayForward.Visible = true;
        // Cosmetic input threshold for approaching this map/Story handle; Core owns all travel and choice rules.
        return new("journey.next", "Way forward · " + label, point, 1800, _wayForward);
    }

    private static CorePosition? FindWayForwardPoint(RoomDefinition room)
    {
        var space = new SpatialWorld(room);
        int east = Math.Max(0, room.HalfWidth - 1400), north = Math.Max(0, room.HalfDepth - 1400);
        // Stay inside the authored floor. Prefer the east/north perimeter, then the other edges.
        CorePosition[] edges =
        [
            new(east, 0), new(east, -north / 2), new(east, north / 2),
            new(0, -north), new(east / 2, -north), new(-east / 2, -north),
            new(-east, 0), new(0, north)
        ];
        foreach (var candidate in edges)
            if (space.CanOccupy(candidate, Math.Max(CombatSession.ActorRadius, 900))) return candidate;
        // Bounded fallback for unusual obstacle layouts, still with authoritative body clearance.
        for (int x = 4; x >= -4; x--)
            for (int z = -4; z <= 4; z++)
            {
                var candidate = new CorePosition(east * x / 4, north * z / 4);
                if (space.CanOccupy(candidate, Math.Max(CombatSession.ActorRadius, 900))) return candidate;
            }
        return null;
    }

    private Node3D BuildWayForward()
    {
        var root = new Node3D { Name = "WayForward" }; AddChild(root);
        var material = new StandardMaterial3D { AlbedoColor = new("e8dda7"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        root.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = .80f, OuterRadius = .87f, Rings = 24, RingSegments = 8 },
            Position = new(0, .035f, 0),
            Scale = new(1, .2f, 1),
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });
        foreach (var (position, length, angle) in new[]
        {
            (new Vector3(-.13f, .04f, 0), .77f, 0f),
            (new Vector3(.25f, .04f, -.17f), .49f, -Mathf.Pi / 4),
            (new Vector3(.25f, .04f, .17f), .49f, Mathf.Pi / 4)
        })
            root.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new(length, .045f, .09f) },
                Position = position,
                Rotation = new(0, angle, 0),
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
        root.AddChild(new Label3D
        {
            Text = "WAY FORWARD",
            Position = new(0, .62f, 0),
            FontSize = 36,
            PixelSize = .009f,
            Modulate = new("fff0bd"),
            OutlineModulate = new("141c23"),
            OutlineSize = 7,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true
        });
        return root;
    }
}
