using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;
using Godot;

namespace Ashenwake.Client;

/// <summary>Procedural slice environment kit. Decoration never participates in authoritative collision.</summary>
public partial class AdventureStage : Node3D
{
    private readonly Dictionary<string, Node3D> _points = [];
    private readonly List<CharacterVisual> _residents = [];
    private Sandbox? _sandbox;
    private Node3D _decoration = null!;
    private string _signature = "";
    private string _pointSignature = "";
    private Node3D _interactionRoot = null!;
    private BellSanctuaryVisual? _bell;

    public override void _Ready()
    {
        for (Node? parent = GetParent(); parent is not null && _sandbox is null; parent = parent.GetParent())
            _sandbox = parent.GetChildren().OfType<Sandbox>().FirstOrDefault();
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        _bell?.Animate(delta, _sandbox?.IsPaused == true, _sandbox?.ReducedEffects == true);
        if (_sandbox?.IsPaused == true) return;
        foreach (var resident in _residents) resident.Animate(delta, Vector3.Zero);
    }

    public void ShowRoom(string roomId, int bellPhase, RoomDefinition room, IReadOnlyList<string> manifestations,
        IReadOnlyDictionary<string, Position> interactions, IReadOnlySet<string> spentInteractions, int hubStage = 0, bool bossDefeated = false)
    {
        string signature = roomId + ":" + hubStage + ":" + room.HalfWidth + ":" + room.HalfDepth;
        float halfWidth = room.HalfWidth * .001f, halfDepth = room.HalfDepth * .001f;
        if (signature != _signature)
        {
            _signature = signature; _pointSignature = "";
            if (_decoration is not null) { RemoveChild(_decoration); _decoration.QueueFree(); }
            _decoration = new Node3D(); AddChild(_decoration); _points.Clear(); _residents.Clear(); _bell = null;
            _interactionRoot = new Node3D { Name = "InteractionMarkers" }; _decoration.AddChild(_interactionRoot);
            switch (roomId)
            {
                case "room.greyhaven": GreyhavenArt.Build(_decoration, halfWidth, halfDepth, hubStage); break;
                case "room.ossuary":
                case "room.cloister":
                case "room.bell_sanctum": _bell = GreyMarchArt.Build(_decoration, halfWidth, halfDepth, roomId, bellPhase, bossDefeated); break;
            }
        }
        _bell?.SetPhase(bellPhase, bossDefeated);
        string pointSignature = string.Join('|', manifestations) + ":" + string.Join('|', spentInteractions.Order()) + ":" +
            string.Join('|', interactions.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value.X}:{pair.Value.Z}"));
        if (pointSignature == _pointSignature) return;
        _pointSignature = pointSignature;
        foreach (var child in _interactionRoot.GetChildren()) { _interactionRoot.RemoveChild(child); child.QueueFree(); }
        _points.Clear(); _residents.Clear();
        if (roomId == "room.greyhaven") AddServiceMats(interactions);
        var occupiedMarkers = new HashSet<Position>();
        foreach (var pair in interactions)
        {
            if (!occupiedMarkers.Add(pair.Value)) continue;
            bool spent = spentInteractions.Contains(pair.Key);
            var point = new Node3D { Position = new(pair.Value.X * .001f, 0, pair.Value.Z * .001f) };
            _interactionRoot.AddChild(point); _points[pair.Key] = point;
            Color color = spent ? new("46515b") : pair.Key.StartsWith("ritual", StringComparison.Ordinal) ? new("e3a1ef") : new("85dfc7");
            bool specialist = pair.Key.StartsWith("npc.", StringComparison.Ordinal) || pair.Key.StartsWith("service.", StringComparison.Ordinal);
            float labelHeight = 1.5f;
            if (specialist)
            {
                var character = CharacterVisual.CreateNpc(pair.Key.Replace("service.", "npc.", StringComparison.Ordinal));
                point.AddChild(character); _residents.Add(character); labelHeight = character.Height + .4f;
            }
            else AddMesh(new CylinderMesh { TopRadius = .52f, BottomRadius = .65f, Height = .5f }, new(0, .25f, 0), color, point);
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
            }, new(0, labelHeight, 0), color, point);
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

    private void AddServiceMats(IReadOnlyDictionary<string, Position> interactions)
    {
        var builder = new EnvironmentBuilder(_interactionRoot, "ServiceMats");
        var occupied = new HashSet<Position>();
        foreach (var (id, point) in interactions)
        {
            if (!occupied.Add(point)) continue;
            string color = id.Contains("torren", StringComparison.Ordinal) ? "705443" : id == "dungeon.replay" ? "747764" : "395e5c";
            Vector3 position = new(point.X * .001f, -.007f, point.Z * .001f);
            builder.Box(new(2.25f, .008f, 1.95f), position, color);
            foreach (float side in new[] { -1f, 1f })
                builder.Box(new(.045f, .009f, 1.85f), position + new Vector3(side * 1.03f, .001f, 0), "9d9680");
        }
        builder.Flush();
    }

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
            FontSize = 38,
            PixelSize = .008f,
            OutlineSize = 5,
            Modulate = color,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true
        };
        (parent ?? _interactionRoot).AddChild(label);
    }
}
