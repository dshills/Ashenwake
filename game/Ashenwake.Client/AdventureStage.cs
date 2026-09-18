using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;
using Godot;

namespace Ashenwake.Client;

/// <summary>Procedural slice environment kit. Decoration never participates in authoritative collision.</summary>
public partial class AdventureStage : Node3D
{
    private readonly Dictionary<string, Node3D> _points = [];
    private Node3D _decoration = null!;
    private string _signature = "";

    public void ShowRoom(string roomId, int bellPhase, RoomDefinition room, IReadOnlyList<string> manifestations,
        IReadOnlyDictionary<string, Position> interactions, IReadOnlySet<string> spentInteractions, int hubStage = 0)
    {
        string signature = roomId + bellPhase + hubStage + string.Join('|', manifestations) + string.Join('|', spentInteractions.Order()) + string.Join('|', interactions.Keys.Order());
        if (signature == _signature) return;
        _signature = signature;
        if (_decoration is not null) { RemoveChild(_decoration); _decoration.QueueFree(); }
        _decoration = new Node3D(); AddChild(_decoration); _points.Clear();
        float halfWidth = room.HalfWidth * .001f, halfDepth = room.HalfDepth * .001f;
        switch (roomId)
        {
            case "room.greyhaven": Greyhaven(halfWidth, halfDepth, hubStage); break;
            case "room.ossuary": Ossuary(halfWidth, halfDepth); break;
            case "room.cloister": Cloister(halfWidth, halfDepth); break;
            case "room.bell_sanctum": Sanctum(halfWidth, halfDepth, bellPhase); break;
        }
        var occupiedMarkers = new HashSet<Position>();
        foreach (var pair in interactions)
        {
            if (!occupiedMarkers.Add(pair.Value)) continue;
            bool spent = spentInteractions.Contains(pair.Key);
            var point = new Node3D { Position = new(pair.Value.X * .001f, 0, pair.Value.Z * .001f) };
            _decoration.AddChild(point); _points[pair.Key] = point;
            Color color = spent ? new("46515b") : pair.Key.StartsWith("ritual", StringComparison.Ordinal) ? new("e3a1ef") : new("85dfc7");
            AddMesh(new CylinderMesh { TopRadius = .52f, BottomRadius = .65f, Height = .5f }, new(0, .25f, 0), color, point);
            AddMesh(new TorusMesh { InnerRadius = .55f, OuterRadius = .64f }, new(0, .05f, 0), color, point);
            Label(pair.Key switch
            {
                "npc.mara" => "MARA VEY · ANATOMY",
                "npc.torren" or "service.torren" => "TORREN · THE FORGE",
                "npc.cael" => "SISTER CAEL · PURIFICATION",
                "npc.oris" => "ORIS · REBINDING",
                "npc.kesh" => "KESH · EXTRACTION",
                "hub.workshops" => "GREYHAVEN WORKSHOPS",
                "dungeon.replay" => "EXPEDITION GATE",
                "ritual.anchor_left" => spent ? "SILENCED ANCHOR" : "RITUAL ANCHOR · LEFT",
                "ritual.anchor_right" => spent ? "SILENCED ANCHOR" : "RITUAL ANCHOR · RIGHT",
                _ => pair.Key.Replace('.', ' ').ToUpperInvariant()
            }, new(0, 1.5f, 0), color, point);
        }
        if (manifestations.Count > 0)
        {
            var label = string.Join(" · ", manifestations.Select(m => m.Replace("manifestation.", "").Replace('_', ' ')));
            Label(label.ToUpperInvariant(), new(0, .25f, halfDepth + .8f), new("f4bd87"));
        }
    }

    public void FocusNearestInteraction(Position playerPosition)
    {
        if (_points.Count == 0) return;
        var player = new Vector3(playerPosition.X * .001f, 0, playerPosition.Z * .001f);
        string nearest = _points.MinBy(pair => pair.Value.Position.DistanceSquaredTo(player)).Key;
        foreach (var pair in _points)
            foreach (var label in pair.Value.GetChildren().OfType<Label3D>()) label.Visible = pair.Key == nearest;
    }

    private void Greyhaven(float x, float z, int hubStage)
    {
        for (int i = -2; i <= 2; i++)
        {
            float px = i * 4.2f;
            Box(new(3.2f, 2.8f, 2.3f), new(px, 1.4f, -z - 1.5f), new("677479"));
            var roof = Box(new(3.7f, .35f, 2.9f), new(px, 3f, -z - 1.5f), new("596d7d"));
            roof.RotationDegrees = new(0, 0, i % 2 == 0 ? 9 : -9);
            Box(new(.75f, 1.55f, .08f), new(px, .8f, -z - .29f), new("283b45"));
            Box(new(.55f, .6f, .09f), new(px + 1, 1.65f, -z - .28f), new("edc687"));
        }
        Banner(new(-x - .7f, 0, -4), new("86b9b6")); Banner(new(-x - .7f, 0, 4), new("86b9b6"));
        Label("GREYHAVEN · A FIRE THAT STILL BURNS", new(0, 4.4f, -z - 1), new("dbc7a0"));
        Box(new(3.2f, .15f, .7f), new(x + .2f, .08f, 0), new("907c61"));
        for (int i = 0; i < hubStage; i++)
        {
            Banner(new(x + .8f, 0, -5 + i * 4), new("c9ac78"));
            Box(new(1.7f, .75f, 1.1f), new(x + 1.4f, .4f, -3 + i * 4), new("8c987b"));
        }
    }

    private void Ossuary(float x, float z)
    {
        for (int i = -2; i <= 2; i++)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                var position = new Vector3(side * (x + .9f), .35f, i * 3.4f);
                Box(new(1.35f, .7f, 2.4f), position, new("858579"));
                Box(new(1.48f, .16f, 2.55f), position + Vector3.Up * .44f, new("b7ac90"));
            }
        }
        for (int i = -2; i <= 2; i++) Arch(new(i * 4.5f, 0, -z - .9f), new("807d73"));
        Label("THE OSSUARY · THE NAMES WERE CHANGED", new(0, 4.5f, -z - 1), new("dbcea9"));
    }

    private void Cloister(float x, float z)
    {
        for (int i = -2; i <= 2; i++)
        {
            Arch(new(i * 4.5f, 0, -z - .9f), new("738485"));
            var arch = Arch(new(-x - .9f, 0, i * 3.8f), new("738485")); arch.RotationDegrees = new(0, 90, 0);
        }
        for (int i = -1; i <= 1; i++) Banner(new(x + .6f, 0, i * 4), new("9787ab"));
        Label("FUNERAL CLOISTER · ANCHOR OF MEMORY", new(0, 4.6f, -z - 1), new("c4d7d1"));
    }

    private void Sanctum(float x, float z, int phase)
    {
        Color stone = phase >= 3 ? new("857486") : new("7d827f");
        for (int i = -2; i <= 2; i++) Arch(new(i * 4.5f, 0, -z - 1), stone);
        foreach (float side in new[] { -1f, 1f })
        {
            Box(new(.55f, 4.8f, .55f), new(side * (x + .6f), 2.4f, -3.5f), stone);
            Box(new(.55f, 4.8f, .55f), new(side * (x + .6f), 2.4f, 3.5f), stone);
        }
        var bell = new Node3D { Position = new(0, 3.6f, -z - .2f) }; _decoration.AddChild(bell);
        AddMesh(new CylinderMesh { TopRadius = .5f, BottomRadius = 1.7f, Height = 2.8f }, Vector3.Zero, new("ad9870"), bell);
        AddMesh(new TorusMesh { InnerRadius = 1.45f, OuterRadius = 1.7f }, new(0, -1.45f, 0), new("dcc38e"), bell);
        if (phase >= 3)
        {
            bell.RotationDegrees = new(0, 0, 25);
            for (int i = 0; i < 5; i++)
                Box(new(.35f, .8f, .2f), new(-3 + i * 1.5f, 1 + i % 2, -z + .7f), new("d6b986"));
        }
        Label(phase switch
        {
            1 => "BELL SAINT · CHAINS & SHOCKWAVES",
            2 => "BELL SAINT · BREAK THE RITUAL ANCHORS",
            3 => "BELL SAINT · THE CREATURE UNBOUND",
            _ => "THE BELL IS SILENT"
        }, new(0, 6f, -z - .5f), new("ead4ac"));
    }

    private Node3D Arch(Vector3 position, Color color)
    {
        var node = new Node3D { Position = position }; _decoration.AddChild(node);
        AddMesh(new BoxMesh { Size = new(.45f, 3.4f, .6f) }, new(-1.6f, 1.7f, 0), color, node);
        AddMesh(new BoxMesh { Size = new(.45f, 3.4f, .6f) }, new(1.6f, 1.7f, 0), color, node);
        AddMesh(new BoxMesh { Size = new(3.65f, .5f, .7f) }, new(0, 3.55f, 0), color, node);
        return node;
    }
    private void Banner(Vector3 position, Color color)
    {
        Box(new(.09f, 3.5f, .09f), position + Vector3.Up * 1.75f, new("aaae9c"));
        Box(new(1.1f, 1.7f, .035f), position + new Vector3(.5f, 2.7f, 0), color);
    }
    private MeshInstance3D Box(Vector3 size, Vector3 position, Color color) => AddMesh(new BoxMesh { Size = size }, position, color, _decoration);
    private static MeshInstance3D AddMesh(Mesh mesh, Vector3 position, Color color, Node parent)
    {
        var actor = new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = .95f } };
        parent.AddChild(actor); return actor;
    }
    private void Label(string text, Vector3 position, Color color, Node? parent = null)
    {
        var label = new Label3D
        {
            Text = text,
            Position = position,
            FontSize = 64,
            PixelSize = .012f,
            OutlineSize = 5,
            Modulate = color,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true
        };
        (parent ?? _decoration).AddChild(label);
    }
}
