using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private string? CampaignActorLabel(CombatActorView actor)
    {
        if (actor.DefinitionId == "enemy.ritual_anchor") return "DESTROY";
        if (actor.DefinitionId != "boss.bell_saint" || _view.BossPhase != 2) return null;
        return _view.Actors.Any(a => a.DefinitionId == "enemy.ritual_anchor" && a.Health > 0)
            ? "PROTECTED" : "RITUAL BROKEN · ATTACK NOW";
    }

    public bool IsPaused => _clock.Paused;
    private readonly HashSet<int> _mechanicLabels = [];
    private static string? EndgameActorLabel(string state) => state switch
    {
        "MarkedEcho" => "◆ MARKED ECHO · BREAK THIS",
        "FalseEcho" => "FALSE ECHO · REFORMS ONCE",
        "RebuildingLimb" => "REBUILDING LIMB · INTERRUPT",
        "BroodChannel" => "BROOD CHANNEL · CLOSE THIS",
        "SeedGuard" => "SEED GUARD · BREAK SHIELD",
        "ContractSeal" => "CONTRACT SEAL · BREAK SHIELD",
        "AbsenceAnchor" => "ABSENCE ANCHOR · BREAK SHIELD",
        _ => null
    };
    private Node3D? _authoredGeometry;
    private string _presentationContext = "";
    private (int Width, int Depth) _authoredBounds;

    /// <summary>Uses the authoritative arena footprint and an attempt identity, preserving input during same-room build projections.</summary>
    public void PresentAuthoredRoom(RoomDefinition room, string context)
    {
        if (_presentationContext == context) return;
        _presentationContext = context;
        _pending.Clear(); ClearPresentation(); _lootSignature = ""; _inspectedLoot = 0; _target = 0; _moveX = _moveZ = int.MinValue;
        if (_authoredGeometry is null)
        {
            // The initial Sandbox room consists of direct box meshes. Actors/effects have their own roots or other mesh shapes.
            foreach (var mesh in GetChildren().OfType<MeshInstance3D>().Where(m => m.Mesh is BoxMesh).ToArray())
            { RemoveChild(mesh); mesh.QueueFree(); }
            _authoredGeometry = new Node3D(); AddChild(_authoredGeometry);
        }
        foreach (var child in _authoredGeometry.GetChildren()) { _authoredGeometry.RemoveChild(child); child.QueueFree(); }
        _occluders.Clear();
        float width = room.HalfWidth * .002f, depth = room.HalfDepth * .002f;
        ArenaBox(new(width, .3f, depth), new(0, -.2f, 0), new("14202d"));
        for (int x = -room.HalfWidth / 1000; x <= room.HalfWidth / 1000; x += 2)
            ArenaBox(new(.024f, .012f, depth), new(x, -.042f, 0), new("43515c"));
        for (int z = -room.HalfDepth / 1000; z <= room.HalfDepth / 1000; z += 2)
            ArenaBox(new(width, .012f, .024f), new(0, -.042f, z), new("43515c"));
        ArenaBox(new(width, .28f, .2f), new(0, .03f, -depth / 2), new("69858c"));
        ArenaBox(new(.2f, .28f, depth), new(-width / 2, .03f, 0), new("69858c"));
        ArenaBox(new(width, .1f, .16f), new(0, .01f, depth / 2), new("78816f"));
        ArenaBox(new(.16f, .1f, depth), new(width / 2, .01f, 0), new("78816f"));
        foreach (var obstacle in room.Obstacles)
        {
            var mesh = ArenaBox(new((obstacle.MaxX - obstacle.MinX) * .001f, 1.5f, (obstacle.MaxZ - obstacle.MinZ) * .001f),
                new((obstacle.MinX + obstacle.MaxX) * .0005f, .75f, (obstacle.MinZ + obstacle.MaxZ) * .0005f), new("677578"));
            _occluders.Add(mesh);
            ArenaBox(new((obstacle.MaxX - obstacle.MinX) * .001f + .12f, .13f, (obstacle.MaxZ - obstacle.MinZ) * .001f + .12f), mesh.Position + Vector3.Up * .76f, new("a89b7c"));
        }
        if (_authoredBounds != (room.HalfWidth, room.HalfDepth))
        { _camera.Size = Math.Max(room.HalfWidth, room.HalfDepth) * .0023f + 10; _authoredBounds = (room.HalfWidth, room.HalfDepth); }
    }
    private MeshInstance3D ArenaBox(Vector3 size, Vector3 position, Color color)
    {
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = position, MaterialOverride = Material(color) };
        _authoredGeometry!.AddChild(mesh); return mesh;
    }
}
