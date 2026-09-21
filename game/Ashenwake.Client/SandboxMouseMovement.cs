using Ashenwake.Core.Combat;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Mouse destinations are input intent. Core still owns every movement, collision and replay command.</summary>
public partial class Sandbox
{
    private static readonly string[] NavigationInterruptActions = ["aw_localmap", "aw_inventory", "aw_settings", "aw_journey", "aw_endgame", "aw_character", "aw_interact", "aw_pickup", "aw_corpse", "aw_echo"];
    private ClickMovePlanner? _clickMove;
    private MeshInstance3D? _moveDestination;
    private Label? _navigationNotice;
    private double _navigationNoticeAge;
    public CorePosition? ClickMoveDestination => _clickMove?.Destination;
    public bool MouseDestinationVisible => _moveDestination?.Visible == true;

    private void BeginMouseMovement(Vector2 screen)
    {
        var player = _view.Actors.Single(a => a.Id == 1);
        if (player.Health <= 0) return;
        Vector3 origin = _camera.ProjectRayOrigin(screen), ray = _camera.ProjectRayNormal(screen);
        if (ray.Y >= -.0001f) return;
        float distance = -origin.Y / ray.Y;
        Vector3 ground = origin + ray * distance;
        if (distance < 0 || !float.IsFinite(ground.X) || !float.IsFinite(ground.Z) ||
            Math.Abs(ground.X) > 1_000_000 || Math.Abs(ground.Z) > 1_000_000) return;
        var destination = new CorePosition((int)MathF.Round(ground.X * 1000), (int)MathF.Round(ground.Z * 1000));
        TryBeginGroundMovement(destination);
    }

    private bool TryBeginGroundMovement(CorePosition destination)
    {
        var player = _session.View.Actors.Single(a => a.Id == 1);
        if (player.Health <= 0 || IsPaused) return false;
        _clickMove ??= new ClickMovePlanner(_session.Room);
        var occupied = _view.Actors.Where(a => a.Id != 1 && a.Health > 0).Select(a => a.Position).ToArray();
        if (!_clickMove.TrySetDestination(player.Position, destination, occupied))
        {
            CancelMouseMovement(true);
            if (_navigationNotice is not null)
            { _navigationNotice.Text = "No clear route to that spot."; _navigationNoticeAge = 2; }
            return false;
        }
        if (_navigationNotice is not null) _navigationNotice.Text = "";
        ShowMoveDestination(); return true;
    }

    private void UpdateMovementInput()
    {
        var input = ReadReleaseMovement();
        int x = Math.Abs(input.X) < .28f ? 0 : Math.Sign(input.X);
        int z = Math.Abs(input.Y) < .28f ? 0 : Math.Sign(input.Y);
        var view = _session.View;
        var player = view.Actors.Single(a => a.Id == 1);
        if (player.Health <= 0) { CancelMouseMovement(false); x = z = 0; }
        else if (x != 0 || z != 0) CancelMouseMovement(false);
        else
        {
            // Do not count a root, stun or ongoing cast as failed navigation. The simulation
            // decides when movement is legal; no destination is stored in canonical state.
            bool held = player.TelegraphTicks > 0 || player.Statuses.Any(s => s.RemainingTicks > 0 && s.Id is "Rooted" or "Frozen" or "Staggered" or "Terrified");
            UpdateWorldAction(player, held);
            if (!held && _clickMove?.Destination is not null)
            {
                var occupied = view.Actors.Where(a => a.Id != 1 && a.Health > 0).Select(a => a.Position).ToArray();
                var direction = _clickMove.NextDirection(player.Position, occupied);
                x = direction.X; z = direction.Z;
            }
        }
        if (x != _moveX || z != _moveZ)
        { Enqueue(new(CombatCommandKind.Move, X: x, Z: z)); _moveX = x; _moveZ = z; }
        ShowMoveDestination();
    }

    private void CancelMouseMovement(bool stop)
    {
        bool active = _clickMove?.Destination is not null || PendingWorldActionId is not null;
        _clickMove?.Cancel();
        ClearWorldAction();
        if (_moveDestination is not null) _moveDestination.Visible = false;
        if (stop && active)
        {
            _pending.RemoveAll(c => c.Kind == CombatCommandKind.Move);
            Enqueue(new(CombatCommandKind.Stop)); _moveX = _moveZ = 0;
        }
    }

    private void ResetMouseMovement()
    {
        CancelMouseMovement(false); _clickMove = null;
        ResetWorldHover();
        if (_navigationNotice is not null) _navigationNotice.Text = "";
        _navigationNoticeAge = 0;
    }

    private void ShowMoveDestination()
    {
        if (_clickMove?.Destination is not { } goal)
        { if (_moveDestination is not null) _moveDestination.Visible = false; return; }
        if (_moveDestination is null)
        {
            _moveDestination = new MeshInstance3D
            {
                Name = "MouseMoveDestination",
                Mesh = new TorusMesh { InnerRadius = .28f, OuterRadius = .36f, Rings = 16, RingSegments = 8 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new("8ddab6"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Scale = new(1, .2f, 1)
            };
            AddChild(_moveDestination);
        }
        _moveDestination.Position = new(goal.X * .001f, .025f, goal.Z * .001f);
        _moveDestination.Visible = true;
    }

    private void UpdateNavigationNotice(double delta)
    {
        if (_navigationNoticeAge <= 0 || _clock.Paused) return;
        _navigationNoticeAge -= delta;
        if (_navigationNoticeAge <= 0 && _navigationNotice is not null) _navigationNotice.Text = "";
    }

    // Pick the visible body, not the old ninety-pixel proximity bubble. Clicking clear floor
    // beside an enemy should request movement instead of unexpectedly attacking that enemy.
    private bool MouseHitsBody(ActorPresentation actor, Vector2 point)
    {
        if (!actor.Root.IsVisibleInTree()) return false;
        // Reject distant screen positions before inspecting articulated meshes on hover.
        // This deliberately generous box includes weapon swings and boss silhouettes.
        float reach = Math.Max(3, actor.Body.Height);
        var broad = new Aabb(actor.Root.GlobalPosition + new Vector3(-reach, -1, -reach), new(reach * 2, reach * 2 + 1, reach * 2));
        var screenBounds = new Rect2(_camera.UnprojectPosition(broad.Position), Vector2.Zero);
        for (int i = 0; i < 8; i++) screenBounds = screenBounds.Expand(_camera.UnprojectPosition(broad.GetEndpoint(i)));
        if (!screenBounds.HasPoint(point)) return false;
        return Meshes(actor.Body).Any(mesh =>
        {
            if (!mesh.IsVisibleInTree() || mesh.Mesh is null) return false;
            var bounds = mesh.Mesh.GetAabb();
            var first = _camera.UnprojectPosition(mesh.GlobalTransform * bounds.Position);
            var rectangle = new Rect2(first, Vector2.Zero);
            for (int i = 0; i < 8; i++) rectangle = rectangle.Expand(_camera.UnprojectPosition(mesh.GlobalTransform * bounds.GetEndpoint(i)));
            return rectangle.Grow(3).HasPoint(point);
        });
    }
    private static IEnumerable<MeshInstance3D> Meshes(Node parent)
        => parent.GetChildren().SelectMany(child => (child is MeshInstance3D mesh ? new[] { mesh } : []).Concat(Meshes(child)));
}
