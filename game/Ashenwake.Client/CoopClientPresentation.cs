using Ashenwake.Core.Content;
using Ashenwake.Core.Coop;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Cosmetic interpolation and prediction only; every gameplay value comes from the received world.</summary>
public partial class CoopClientPresentation : Node3D
{
    private sealed record ActorMesh(Node3D Root, CharacterVisual Body, MeshInstance3D Tell, Label3D Name);
    private readonly Dictionary<int, ActorMesh> actors = [];
    private readonly Dictionary<long, Node3D> warnings = [], projectiles = [];
    private Node3D arena = null!;
    private Camera3D camera = null!;
    private MeshInstance3D targetRing = null!, destinationRing = null!;
    private string context = "";
    public double CorrectionMeters { get; private set; }
    public override void _Ready()
    {
        var environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new("0d1621"), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new("8ca7bc"), AmbientLightEnergy = .85f };
        AddChild(new WorldEnvironment { Environment = environment });
        AddChild(new DirectionalLight3D { RotationDegrees = new(-55, -25, 0), LightEnergy = 1.3f, ShadowEnabled = true });
        camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 25, Position = new(22, 25, 25), Current = true };
        AddChild(camera); camera.LookAt(Vector3.Zero);
        arena = new Node3D(); AddChild(arena);
        targetRing = Mesh(new TorusMesh { InnerRadius = .55f, OuterRadius = .65f }, Vector3.Zero, new("fbd88a"), this); targetRing.Visible = false;
        destinationRing = Mesh(new TorusMesh { InnerRadius = .24f, OuterRadius = .32f }, Vector3.Zero, new("8fe4d0"), this);
        destinationRing.Name = "MouseDestination"; destinationRing.Visible = false;
    }
    public void Zoom(float step) => camera.Size = Math.Clamp(camera.Size + step, 20, 52);
    public int TargetAt(Vector2 point, CoopView view)
    {
        int selected = 0;
        float closest = float.MaxValue;
        foreach (var actor in view.Actors.Where(a => a.PlayerId == 0 && a.Health > 0))
        {
            bool presented = actors.TryGetValue(actor.Id, out var mesh);
            var foot = presented ? mesh!.Root.Position : Point(actor.Position);
            float height = presented ? mesh!.Body.Height : 1.5f;
            if (camera.IsPositionBehind(foot)) continue;
            var bottom = camera.UnprojectPosition(foot + Vector3.Up * .2f);
            var top = camera.UnprojectPosition(foot + Vector3.Up * Math.Max(.25f, height * .85f));
            var segment = top - bottom;
            float along = segment.LengthSquared() > .01f ? Math.Clamp((point - bottom).Dot(segment) / segment.LengthSquared(), 0, 1) : 0;
            float distance = point.DistanceSquaredTo(bottom + segment * along);
            // Follow the visible body instead of treating a large circle around its feet as an attack.
            float radius = Math.Clamp(bottom.DistanceTo(camera.UnprojectPosition(foot + Vector3.Up * .2f + camera.GlobalBasis.X * .45f)), 9, 24);
            if (distance <= radius * radius && distance < closest) { selected = actor.Id; closest = distance; }
        }
        return selected;
    }
    public CorePosition? GroundAt(Vector2 point, RoomDefinition room)
    {
        if (!GetViewport().GetVisibleRect().HasPoint(point)) return null;
        var origin = camera.ProjectRayOrigin(point);
        var ray = camera.ProjectRayNormal(point);
        if (Math.Abs(ray.Y) < .0001f) return null;
        float distance = -origin.Y / ray.Y;
        if (!float.IsFinite(distance) || distance < 0) return null;
        var ground = origin + ray * distance;
        if (!float.IsFinite(ground.X) || !float.IsFinite(ground.Z)) return null;
        return new((int)MathF.Round(Math.Clamp(ground.X * 1000, -room.HalfWidth, room.HalfWidth)),
            (int)MathF.Round(Math.Clamp(ground.Z * 1000, -room.HalfDepth, room.HalfDepth)));
    }
    public void SetDestination(CorePosition? destination)
    {
        if (destinationRing is null) return;
        destinationRing.Visible = destination is not null;
        if (destination is { } position) destinationRing.Position = Point(position) + Vector3.Up * .06f;
    }
    public void Render(CoopView view, CoopView? previous, int localPlayer, int target, Vector2 movement, double age, double delta)
    {
        bool changed = context != view.ContextKey;
        if (changed) { context = view.ContextKey; Rebuild(view.Room); previous = null; }
        float blend = Math.Clamp((float)(age * 30), 0, 1);
        var live = view.Actors.Select(a => a.Id).ToHashSet();
        foreach (int id in actors.Keys.Where(id => !live.Contains(id)).ToArray()) { actors[id].Root.QueueFree(); actors.Remove(id); }
        foreach (var actor in view.Actors)
        {
            if (!actors.TryGetValue(actor.Id, out var mesh))
            {
                var root = new Node3D(); AddChild(root);
                Color color = actor.PlayerId == 1 ? new("73d9c3") : actor.PlayerId == 2 ? new("9abaff") : actor.Role.Contains("Anchor", StringComparison.Ordinal) ? new("d0a9ed") : new("d9967d");
                var body = CharacterVisual.Create(actor.DefinitionId, actor.Role, actor.PlayerId > 0 ? "Vanguard" : "");
                if (actor.PlayerId > 0) body.SetAccent(color);
                root.AddChild(body);
                var tell = Mesh(new TorusMesh { InnerRadius = .65f, OuterRadius = .76f }, new(0, .05f, 0), new("ffb855"), root);
                var label = Label("", new(actor.PlayerId == 1 ? -.7f : actor.PlayerId == 2 ? .7f : 0, body.Height + (actor.PlayerId == 2 ? .8f : .4f), 0), color); root.AddChild(label);
                mesh = new(root, body, tell, label); actors.Add(actor.Id, mesh); mesh.Root.Position = Point(actor.Position);
            }
            var authoritative = Point(actor.Position);
            var before = previous?.Actors.FirstOrDefault(a => a.Id == actor.Id)?.Position ?? actor.Position;
            var display = Point(before).Lerp(authoritative, blend);
            if (actor.PlayerId == localPlayer && actor.Health > 0 && actor.State != "Windup")
            {
                var intent = new Vector3(movement.X, 0, movement.Y); if (intent.LengthSquared() > 1) intent = intent.Normalized();
                display = authoritative + intent * (float)(4.5 * Math.Min(.2, age));
                display.X = Math.Clamp(display.X, -view.Room.HalfWidth * .001f + .28f, view.Room.HalfWidth * .001f - .28f);
                display.Z = Math.Clamp(display.Z, -view.Room.HalfDepth * .001f + .28f, view.Room.HalfDepth * .001f - .28f);
                CorrectionMeters = mesh.Root.Position.DistanceTo(authoritative);
                if (!changed && mesh.Root.Position.DistanceTo(display) < 2) display = mesh.Root.Position.Lerp(display, 1 - MathF.Exp((float)-delta * 24));
            }
            var motion = changed ? Vector3.Zero : display - mesh.Root.Position;
            Vector3? facing = null;
            if (actor.TelegraphTicks > 0)
            {
                var opponent = actor.PlayerId > 0 ? view.Actors.FirstOrDefault(a => a.Id == target) :
                    view.Actors.Where(a => a.PlayerId > 0 && a.Health > 0).OrderBy(a => CorePosition.DistanceSquared(a.Position, actor.Position)).FirstOrDefault();
                if (opponent is not null) facing = Point(opponent.Position) - display;
            }
            // This adapter measures rendered displacement; the rig consumes displacement per Core tick.
            var tickMotion = delta > 0 ? motion * (float)(Ashenwake.Core.Simulation.FixedStepClock.SecondsPerTick / delta) : Vector3.Zero;
            mesh.Body.Animate(delta, tickMotion, actor.TelegraphTicks > 0, actor.State, facing: facing);
            mesh.Root.Position = display; mesh.Root.Visible = actor.Health > 0; mesh.Tell.Visible = actor.TelegraphTicks > 0;
            string name = actor.PlayerId > 0 ? $"P{actor.PlayerId}{(actor.PlayerId == localPlayer ? " · YOU" : "")}" : Readable(actor.DefinitionId);
            mesh.Name.Text = name + (actor.Shielded ? "\nSHIELDED" : actor.Role.Contains("Anchor", StringComparison.Ordinal) ? "\nBREAK SHIELD" : "");
            mesh.Name.Visible = actor.PlayerId > 0 || actor.Id == target || actor.Shielded || actor.Role.Contains("Anchor", StringComparison.Ordinal);
        }
        targetRing.Visible = actors.TryGetValue(target, out var selected) && selected.Root.Visible;
        if (targetRing.Visible) targetRing.Position = selected!.Root.Position + Vector3.Up * .03f;
        var localActor = view.Actors.FirstOrDefault(a => a.PlayerId == localPlayer);
        if (localActor is not null && actors.TryGetValue(localActor.Id, out var localMesh))
        {
            var destination = new Vector3(22, 25, 25) + localMesh.Root.Position;
            camera.Position = changed ? destination : camera.Position.Lerp(destination, 1 - MathF.Exp(-(float)delta * 8));
        }
        RenderWarnings(view);
        var shotIds = view.Projectiles.Select(p => p.Id).ToHashSet();
        foreach (long id in projectiles.Keys.Where(id => !shotIds.Contains(id)).ToArray()) { projectiles[id].QueueFree(); projectiles.Remove(id); }
        foreach (var shot in view.Projectiles)
        {
            if (!projectiles.TryGetValue(shot.Id, out var node))
            { node = Mesh(new SphereMesh { Radius = .16f, Height = .32f }, Vector3.Zero, new("ffcf82"), this); projectiles.Add(shot.Id, node); }
            node.Position = Point(shot.Position) + Vector3.Up * .5f;
        }
    }
    private void RenderWarnings(CoopView view)
    {
        var ids = view.Warnings.Select(w => w.Id).ToHashSet();
        foreach (long id in warnings.Keys.Where(id => !ids.Contains(id)).ToArray()) { warnings[id].QueueFree(); warnings.Remove(id); }
        foreach (var warning in view.Warnings)
        {
            if (!warnings.TryGetValue(warning.Id, out var node))
            {
                node = new Node3D(); AddChild(node); warnings.Add(warning.Id, node);
                var start = Point(warning.Position); var end = Point(warning.End); float radius = warning.Radius * .001f;
                Color fill = new(1, .48f, .12f, .28f);
                if (warning.Shape == "Line")
                {
                    var line = Mesh(new BoxMesh { Size = new(radius * 2, .035f, start.DistanceTo(end)) }, (start + end) * .5f + Vector3.Up * .07f, fill, node);
                    line.Rotation = new(0, MathF.Atan2(end.X - start.X, end.Z - start.Z), 0);
                    foreach (var cap in new[] { start, end }) Mesh(new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = .035f }, cap + Vector3.Up * .07f, fill, node);
                }
                else
                {
                    Mesh(new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = .035f }, start + Vector3.Up * .07f, fill, node);
                    Mesh(new TorusMesh { InnerRadius = Math.Max(.02f, radius - .06f), OuterRadius = radius }, start + Vector3.Up * .1f, new("ffd37a"), node);
                }
                node.AddChild(Label("", start + Vector3.Up * .6f, new("ffe4aa")));
            }
            node.GetChildren().OfType<Label3D>().Single().Text = $"{Math.Max(0, warning.ResolveTick - view.Tick) / 30d:F1}s";
        }
    }
    private void Rebuild(RoomDefinition room)
    {
        foreach (var actor in actors.Values) actor.Root.QueueFree(); actors.Clear();
        foreach (var node in warnings.Values.Concat(projectiles.Values)) node.QueueFree(); warnings.Clear(); projectiles.Clear();
        foreach (var child in arena.GetChildren()) { arena.RemoveChild(child); child.QueueFree(); }
        float width = room.HalfWidth * .002f, depth = room.HalfDepth * .002f;
        Mesh(new BoxMesh { Size = new(width, .3f, depth) }, new(0, -.2f, 0), new("24343f"), arena);
        for (int x = -room.HalfWidth / 1000; x <= room.HalfWidth / 1000; x += 2) Mesh(new BoxMesh { Size = new(.025f, .01f, depth) }, new(x, -.04f, 0), new("607078"), arena);
        for (int z = -room.HalfDepth / 1000; z <= room.HalfDepth / 1000; z += 2) Mesh(new BoxMesh { Size = new(width, .01f, .025f) }, new(0, -.04f, z), new("607078"), arena);
        foreach (var obstacle in room.Obstacles) Mesh(new BoxMesh { Size = new((obstacle.MaxX - obstacle.MinX) * .001f, .8f, (obstacle.MaxZ - obstacle.MinZ) * .001f) },
            new((obstacle.MaxX + obstacle.MinX) * .0005f, .4f, (obstacle.MaxZ + obstacle.MinZ) * .0005f), new("869593"), arena);
    }
    private static Vector3 Point(CorePosition at) => new(at.X * .001f, 0, at.Z * .001f);
    private static string Readable(string id) => id.Split('.').Last().Replace('_', ' ').ToUpperInvariant();
    private static Label3D Label(string text, Vector3 at, Color color) => new() { Text = text, Position = at, FontSize = 32, PixelSize = .014f, OutlineSize = 4, Modulate = color, NoDepthTest = true, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled };
    private static MeshInstance3D Mesh(Mesh shape, Vector3 at, Color color, Node parent)
    {
        var mesh = new MeshInstance3D { Mesh = shape, Position = at, MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = .9f, Transparency = color.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled } };
        parent.AddChild(mesh); return mesh;
    }
}
