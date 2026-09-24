using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Simulation;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Noncombat followers and rescue landmarks. No actor, collision body or reward authority.</summary>
public partial class PetPresentation : Node3D
{
    private Sandbox _sandbox = null!;
    private readonly Dictionary<string, Node3D> _markers = [];
    private readonly Queue<CorePosition> _trail = [];
    private PetVisual? _follower;
    private Label3D? _name;
    private RoomDefinition? _room;
    private SpatialWorld? _space;
    private ExpeditionInteraction[] _markerTargets = [];
    private CorePosition _player, _lastTrail;
    private string _selection = "", _context = "", _markerContext = "";
    private double _stuck;
    public bool FollowerVisible => Visible && _follower?.IsVisibleInTree() == true;
    public string SelectedPetId { get; private set; } = "";
    public Vector3 FollowerPosition => _follower?.Position ?? Vector3.Zero;
    public void Attach(Sandbox sandbox) => _sandbox = sandbox;
    public Node3D? GetInteractionVisual(string id) => _markers.GetValueOrDefault(id);

    public void Present(PetsView view, RoomDefinition room, CorePosition player, string context, ExpeditionInteraction[] interactions, bool available)
    {
        if (!ReferenceEquals(_room, room)) { _room = room; _space = new SpatialWorld(room); }
        Visible = available; _player = player;
        var selected = view.Entries.FirstOrDefault(e => e.Rescued && e.Selected);
        string selection = selected is null ? "" : selected.Id + ":" + selected.AppearanceId;
        bool changedRoom = context != _context; _context = context;
        if (selection != _selection)
        {
            if (_follower is not null) { RemoveChild(_follower); _follower.QueueFree(); _follower = null; }
            _name = null; _selection = selection; SelectedPetId = selected?.Id ?? "";
            if (selected is not null)
            {
                _follower = PetVisual.Create(selected.Id, selected.AppearanceId); _follower.Name = "EquippedPet"; AddChild(_follower);
                _name = Caption(_follower, selected.Name, new(0, 1.5f, 0));
            }
            changedRoom = true;
        }
        if (_name is not null && selected is not null) _name.Text = selected.Name;
        if (changedRoom) Recall();
        if (_trail.Count == 0 || CorePosition.DistanceSquared(_lastTrail, player) > 350L * 350)
        { _trail.Enqueue(player); _lastTrail = player; while (_trail.Count > 48) _trail.Dequeue(); }
        var targets = interactions.Where(i => i.ActionId.StartsWith("pet.", StringComparison.Ordinal)).ToArray();
        if (context == _markerContext && targets.SequenceEqual(_markerTargets)) return;
        _markerContext = context; _markerTargets = targets;
        foreach (var marker in _markers.Values) { RemoveChild(marker); marker.QueueFree(); }
        _markers.Clear();
        foreach (var target in targets)
        {
            var marker = new Node3D { Name = "PetMarker_" + target.ActionId.Replace('.', '_'), Position = World(target.Position) };
            AddChild(marker); _markers.Add(target.ActionId, marker);
            if (target.ActionId.EndsWith(".rescue", StringComparison.Ordinal))
            {
                var pet = PetCatalog.Definitions.Single(d => target.ActionId == d.Id + ".rescue");
                marker.AddChild(PetVisual.Create(pet.Id, pet.Appearances[0].Id));
                var props = new EnvironmentBuilder(marker, "RescueNest");
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * Mathf.Tau / 6;
                    var at = new Vector3(Mathf.Cos(angle), .03f, Mathf.Sin(angle)) * .8f;
                    props.Branch(at, at + new Vector3(.3f, .1f, -.12f), .04f, .015f, "756451");
                }
                props.Flush(); Caption(marker, "RESCUE · " + pet.Species, new(0, 1.9f, 0));
            }
            else
            {
                var props = new EnvironmentBuilder(marker, "MaterialSatchel");
                marker.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = .4f, Height = .8f, RadialSegments = 24, Rings = 12 }, Position = new(0, .3f, 0), Scale = new(1, .7f, .8f), MaterialOverride = new StandardMaterial3D { AlbedoColor = new("887459"), Roughness = .95f } });
                props.Box(new(.09f, .15f, .49f), new(0, .48f, 0), "d2b77f");
                props.Flush(); Caption(marker, "SALVAGED MATERIALS", new(0, 1.05f, 0));
            }
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible || _sandbox is null || _sandbox.IsPaused) return;
        double dt = Math.Clamp(delta, 0, .1);
        foreach (var marker in _markers.Values)
            foreach (var pet in marker.GetChildren().OfType<PetVisual>()) pet.Animate(dt, false, _sandbox.ReducedEffects);
        if (_follower is null || _space is null) return;
        var at = Logical(_follower.Position);
        if (CorePosition.DistanceSquared(at, _player) > 10000L * 10000) { Recall(); at = Logical(_follower.Position); }
        while (_trail.Count > 1 && CorePosition.DistanceSquared(at, _trail.Peek()) < 650L * 650) _trail.Dequeue();
        var target = _trail.Count > 0 ? _trail.Peek() : _player;
        bool moving = CorePosition.DistanceSquared(at, _player) > 1250L * 1250;
        if (moving)
        {
            var offset = World(target) - _follower.Position; float distance = offset.Length();
            if (distance > .01f)
            {
                var proposed = _follower.Position + offset.Normalized() * Math.Min(distance, (float)dt * 5.5f);
                var next = _space.Move(at, Logical(proposed), 180);
                _follower.Position = World(next);
                if (CorePosition.DistanceSquared(at, next) < 4) _stuck += dt; else _stuck = 0;
                var direction = _follower.Position - World(at);
                if (direction.LengthSquared() > .000001f) _follower.Rotation = new(0, Mathf.Atan2(direction.X, direction.Z), 0);
            }
            if (_stuck > 1.2) Recall();
        }
        _follower.Animate(dt, moving, _sandbox.ReducedEffects);
    }
    public void Reset()
    {
        if (_follower is not null) { RemoveChild(_follower); _follower.QueueFree(); }
        _follower = null; _name = null; _selection = _context = _markerContext = SelectedPetId = ""; _trail.Clear(); _stuck = 0;
        _room = null; _space = null; _markerTargets = [];
        foreach (var marker in _markers.Values) { RemoveChild(marker); marker.QueueFree(); }
        _markers.Clear();
    }
    private void Recall()
    {
        _trail.Clear(); _lastTrail = _player; _stuck = 0;
        if (_follower is null || _space is null) return;
        var nearby = new[] { new CorePosition(_player.X - 950, _player.Z + 600), new CorePosition(_player.X + 950, _player.Z + 600), _player };
        _follower.Position = World(nearby.FirstOrDefault(p => _space.CanOccupy(p, 180) && _space.HasLineOfSight(_player, p), _player));
    }
    private static Vector3 World(CorePosition p) => new(p.X * .001f, 0, p.Z * .001f);
    private static CorePosition Logical(Vector3 p) => new((int)Math.Round(p.X * 1000), (int)Math.Round(p.Z * 1000));
    private static Label3D Caption(Node3D parent, string text, Vector3 at)
    {
        var label = new Label3D { Text = text, Position = at, FontSize = 23, PixelSize = .006f, OutlineSize = 4, Modulate = new("e1cd9e"), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled };
        parent.AddChild(label); return label;
    }
}
