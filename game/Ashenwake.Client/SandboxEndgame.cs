using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private string? CampaignActorLabel(CombatActorView actor)
    {
        if (actor.DefinitionId == "enemy.ritual_anchor") return "DESTROY";
        if (actor.DefinitionId == "enemy.feeding_root") return "SEVER";
        if (actor.DefinitionId == "boss.rootheart") return _view.Actors.Count(a => a.DefinitionId == "enemy.feeding_root" && a.Health > 0) >= 3 ? "PROTECTED" : "EXPOSED";
        if (actor.DefinitionId == "boss.furnace_spindle") return actor.Guarded ? "CORE GUARDED" : "CORE EXPOSED";
        if (actor.DefinitionId == "boss.covenant_warden") return actor.Guarded ? "OATH GUARDED" : "WARDEN EXPOSED";
        if (actor.DefinitionId == "enemy.seal_channel") return "BREAK SEAL";
        if (actor.DefinitionId == "boss.breach_heart")
        {
            if (_view.Actors.Any(a => a.DefinitionId == actor.DefinitionId && a.Id < actor.Id)) return "BREACH ECHO";
            return actor.Shielded ? "SEALED · 3 CHANNELS" : "BREACH EXPOSED";
        }
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
    private string _presentedGroundStyle = "";
    private (int Width, int Depth) _authoredBounds;

    /// <summary>Uses the authoritative arena footprint and an attempt identity, preserving input during same-room build projections.</summary>
    public void PresentAuthoredRoom(RoomDefinition room, string context, string visualStyle = "default")
    {
        bool resized = _authoredBounds != (room.HalfWidth, room.HalfDepth);
        if (resized)
        { _authoredBounds = (room.HalfWidth, room.HalfDepth); _camera.Size = DefaultCameraSize(room.HalfWidth, room.HalfDepth); }
        SetEnvironmentStyle(visualStyle);
        // A resize can keep the same style; its motes still need the new perimeter immediately.
        if (resized) UpdateEnvironmentAtmosphere(0);
        if (_presentationContext == context && _presentedGroundStyle == visualStyle && !resized) return;
        _presentationContext = context;
        _presentedGroundStyle = visualStyle;
        _pending.Clear(); ClearPresentation(); _lootSignature = ""; _inspectedLoot = 0; _target = 0; _moveX = _moveZ = int.MinValue;
        if (_authoredGeometry is null)
        {
            // The initial Sandbox room consists of direct box meshes. Actors/effects have their own roots or other mesh shapes.
            foreach (var mesh in GetChildren().OfType<MeshInstance3D>().Where(m => m.Mesh is BoxMesh).ToArray())
            { RemoveChild(mesh); mesh.QueueFree(); }
            _authoredGeometry = new Node3D(); AddChild(_authoredGeometry);
        }
        foreach (var child in _authoredGeometry.GetChildren()) { _authoredGeometry.RemoveChild(child); child.QueueFree(); }
        _occluders.Clear(); _occluderCenters.Clear();
        EnvironmentGround.Build(_authoredGeometry, room, visualStyle);
        int index = 0;
        foreach (var obstacle in room.Obstacles)
        {
            float width = (obstacle.MaxX - obstacle.MinX) * .001f, depth = (obstacle.MaxZ - obstacle.MinZ) * .001f;
            Vector3 center = new((obstacle.MinX + obstacle.MaxX) * .0005f, 0, (obstacle.MinZ + obstacle.MaxZ) * .0005f);
            var builder = new EnvironmentBuilder(_authoredGeometry, "AuthoritativeObstacle_" + index++);
            if (visualStyle is "verdant_ruins" or "verdant_village" or "verdant_heart" or "verdant_hunt")
                VerdantObstacleArt.Build(builder, width, depth, center, visualStyle);
            else if (CinderAmbience.CueForStyle(visualStyle).Length != 0)
                CinderObstacleArt.Build(builder, width, depth, center, visualStyle);
            else if (SpineAmbience.CueForStyle(visualStyle).Length != 0)
                SpineObstacleArt.Build(builder, width, depth, center, visualStyle);
            else if (HollowAmbience.CueForStyle(visualStyle).Length != 0)
                HollowObstacleArt.Build(builder, width, depth, center, visualStyle);
            else
            {
                builder.Box(new(width, 1.08f, depth), center + Vector3.Up * .54f, visualStyle == "greyhaven" ? "5c665b" : "536367");
                builder.Box(new(width, .12f, depth), center + Vector3.Up * 1.12f, "90978a");
                builder.Box(new(width * .86f, .075f, depth * .82f), center + Vector3.Up * 1.215f, "727d73");
                for (int seam = 1; seam < 4; seam++)
                    builder.Box(new(width, .018f, depth), center + Vector3.Up * (seam * .26f), "384849");
            }
            builder.Flush();
            foreach (var mesh in _authoredGeometry.GetChildren().Last().GetChildren().OfType<MeshInstance3D>())
            {
                // Each obstacle keeps an independent opacity material, including its cap and seams.
                mesh.MaterialOverride = mesh.Mesh.SurfaceGetMaterial(0).Duplicate() as Material;
                _occluders.Add(mesh);
                _occluderCenters[mesh] = center + Vector3.Up * .63f;
            }
        }
        // A room can change while a modal keeps the simulation paused. Recreate its current
        // actors and drops now instead of leaving the cleared presentation empty until a tick.
        _view = _session.View; SynchronizeWorld();
    }
}
