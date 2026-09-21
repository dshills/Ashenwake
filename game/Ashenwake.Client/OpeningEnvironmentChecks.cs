using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Independent scene evidence for the opening layouts; this never advances or replaces a live character.</summary>
public static class OpeningEnvironmentChecks
{
    public sealed record Evidence(string Style, int ArchitectureMeshes, int ArchitectureMaterials, int ArchitectureTriangles,
        int GroundMeshes, int GroundMaterials, int GroundTriangles, float GroundTop, int Routes, int MaximumRouteTicks,
        int VisibleInteractionTargets, double InspectionMilliseconds);

    /// <summary>Pass the visible static architecture root, actual room, and current service/way-forward targets.
    /// A target may omit its Visual when that resident is not present yet; its approach is still checked.</summary>
    public static Evidence Inspect(Sandbox sandbox, Node3D sceneryRoot, RoomDefinition room, string style,
        IReadOnlyList<WorldInteractionTarget> interactions, Action<string, bool> check)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        string prefix = "opening_" + style + "_";
        string initialState = sandbox.Session.StateHash, roomHash = JsonData.Hash(room);
        var scenery = Meshes(sceneryRoot);
        var groundRoot = Descendants(sandbox).OfType<Node3D>().SingleOrDefault(n => n.Name == "AuthoredGround" && n.IsVisibleInTree());
        Check("authored_ground_exists", groundRoot is not null);
        var ground = groundRoot is null ? [] : Meshes(groundRoot);
        var sceneryFaces = Faces(scenery); var groundFaces = Faces(ground);
        Check("finite_authored_geometry", sceneryFaces.Length > 0 && groundFaces.Length > 0 &&
            sceneryFaces.All(p => p.IsFinite()) && groundFaces.All(p => p.IsFinite()));
        float groundTop = groundFaces.Length == 0 ? float.PositiveInfinity : groundFaces.Max(p => p.Y);
        Check("ground_stays_below_feet_and_combat_tells", float.IsFinite(groundTop) && groundTop < 0);
        int sceneryMaterials = Materials(scenery), groundMaterials = Materials(ground);
        Check("bounded_static_geometry_and_batches", sceneryFaces.Length / 3 <= 40000 && scenery.Length <= 120 && sceneryMaterials is > 0 and <= 120 &&
            groundFaces.Length / 3 <= 40000 && ground.Length <= 48 && groundMaterials is > 0 and <= 48);
        Check("static_meshes_are_material_batched", scenery.All(m => m.Mesh.GetSurfaceCount() <= 2) && ground.All(m => m.Mesh.GetSurfaceCount() <= 2));
        Check("scenery_adds_no_invisible_physics_or_navigation", !WithRoot(sceneryRoot).Concat(groundRoot is null ? [] : WithRoot(groundRoot))
            .Any(n => n is CollisionObject3D or CollisionShape3D or CollisionPolygon3D or NavigationRegion3D or NavigationLink3D));
        Obstacles(sandbox, room, scenery.Concat(ground).ToArray(), Check);

        var space = new SpatialWorld(room);
        var intrusion = FirstActorLaneIntrusion(sceneryFaces, room, space);
        Check("scenery_keeps_walkable_character_corridors_clear" + (intrusion is { } bad ? "_at_" + PointKey(bad) : ""), intrusion is null);
        var starts = new[] { room.PlayerSpawn, room.EnemySpawn }.Concat(sandbox.Session.View.Actors.Select(a => a.Position)).Distinct().ToArray();
        Check("live_actors_and_authored_spawns_fit_core_room", starts.All(p => space.CanOccupy(p, CombatSession.ActorRadius)));
        int routes = 0, maximumRouteTicks = 0;
        foreach (var start in starts)
        {
            var result = Route(room, room.PlayerSpawn, start, 0);
            Check("spawn_route_" + routes, result.Reached); maximumRouteTicks = Math.Max(maximumRouteTicks, result.Ticks); routes++;
        }
        if (OpeningGround.Supports(style))
        {
            var waypoints = OpeningGround.Route(room.HalfWidth * .001f, style)
                .Select(point => new CorePosition((int)Math.Round(point.X * 1000), (int)Math.Round(point.Y * 1000))).ToArray();
            Check("paved_route_waypoints_fit_core_room", waypoints.Length is >= 2 and <= 16 && waypoints.All(p => space.CanOccupy(p, CombatSession.ActorRadius)));
            var position = room.PlayerSpawn;
            foreach (var (direction, points) in new[] { ("forward", waypoints), ("return", waypoints.Reverse().ToArray()) })
                for (int i = 0; i < points.Length; i++)
                {
                    // Carry the actual arrival into each next segment, retaining the planner's digital steering tolerance.
                    var result = Route(room, position, points[i], 0);
                    Check("paved_route_" + direction + "_segment_" + i, result.Reached);
                    position = result.Final; maximumRouteTicks = Math.Max(maximumRouteTicks, result.Ticks); routes++;
                }
        }
        foreach (var target in interactions)
        {
            // Workshop markers can sit at an obstacle edge. The contract is a reachable approach inside their usable radius.
            foreach (var start in new[] { room.PlayerSpawn, room.EnemySpawn }.Distinct())
            {
                var result = Route(room, start, target.Position, target.Range);
                Check("approach_" + SafeName(target.Id) + "_from_" + PointKey(start), result.Reached);
                maximumRouteTicks = Math.Max(maximumRouteTicks, result.Ticks); routes++;
                if (result.Reached)
                {
                    var back = Route(room, result.Final, room.PlayerSpawn, 0);
                    Check("return_from_" + SafeName(target.Id) + "_from_" + PointKey(start), back.Reached);
                    maximumRouteTicks = Math.Max(maximumRouteTicks, back.Ticks); routes++;
                }
            }
        }
        var camera = sandbox.GetChildren().OfType<Camera3D>().Single();
        int visibleTargets = 0;
        foreach (var target in interactions.Where(t => t.Visual is not null && t.Visual.IsVisibleInTree()))
        {
            bool visible = HasUnoccludedBodySample(target.Visual!, sceneryFaces, camera, sandbox.GetViewport().GetVisibleRect());
            Check("click_target_visible_through_scenery_" + SafeName(target.Id), visible);
            if (visible) visibleTargets++;
        }
        Check("inspection_keeps_live_state_and_room_unchanged", sandbox.Session.StateHash == initialState && JsonData.Hash(room) == roomHash);
        return new(style, scenery.Length, sceneryMaterials, sceneryFaces.Length / 3, ground.Length, groundMaterials,
            groundFaces.Length / 3, groundTop, routes, maximumRouteTicks, visibleTargets,
            System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        void Check(string name, bool success) => check(prefix + name, success);
    }

    private static void Obstacles(Sandbox sandbox, RoomDefinition room, MeshInstance3D[] otherScenery, Action<string, bool> check)
    {
        var obstacles = Descendants(sandbox).OfType<Node3D>()
            .Where(n => n.Name.ToString().StartsWith("AuthoritativeObstacle_", StringComparison.Ordinal) && n.IsVisibleInTree()).ToArray();
        check("obstacle_count_matches_authoritative_room", obstacles.Length == room.Obstacles.Length);
        var fadeMaterials = new HashSet<ulong>();
        var otherMaterials = otherScenery.SelectMany(m => Enumerable.Range(0, m.Mesh.GetSurfaceCount()).Select(m.GetActiveMaterial))
            .Where(m => m is not null).Select(m => m.GetInstanceId()).ToHashSet();
        for (int i = 0; i < room.Obstacles.Length; i++)
        {
            var root = obstacles.Single(n => n.Name == "AuthoritativeObstacle_" + i);
            var bounds = room.Obstacles[i]; var meshes = Meshes(root); var vertices = Faces(meshes);
            check("obstacle_" + i + "_finite_bounded_geometry", vertices.Length > 0 && vertices.Length / 3 <= 4000 &&
                vertices.All(p => p.IsFinite()) && meshes.Length is > 0 and <= 24 && Materials(meshes) <= 24);
            check("obstacle_" + i + "_above_ground_vertices_match_core_footprint", vertices.Any(p => p.Y > 0) && vertices.Where(p => p.Y > 0).All(p =>
                p.X >= bounds.MinX * .001f - .005f && p.X <= bounds.MaxX * .001f + .005f &&
                p.Z >= bounds.MinZ * .001f - .005f && p.Z <= bounds.MaxZ * .001f + .005f));
            check("obstacle_" + i + "_adds_no_physics_or_navigation", !WithRoot(root)
                .Any(n => n is CollisionObject3D or CollisionShape3D or CollisionPolygon3D or NavigationRegion3D or NavigationLink3D));
            check("obstacle_" + i + "_owns_independent_fade_materials", meshes.All(mesh => mesh.MaterialOverride is StandardMaterial3D material &&
                fadeMaterials.Add(material.GetInstanceId()) && !otherMaterials.Contains(material.GetInstanceId()) &&
                Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).All(surface => mesh.Mesh.SurfaceGetMaterial(surface)?.GetInstanceId() != material.GetInstanceId())));
        }
    }

    /// <summary>Call on a detached diagnostic copy made with OpeningAtmosphere.Create; direct Animate exercises its public stage contract.
    /// The live frame-loop integration remains covered by JourneySmoke's pause and accessibility actions.</summary>
    public static void Atmosphere(OpeningAtmosphere atmosphere, Action<string, bool> check)
    {
        string prefix = "opening_" + atmosphere.Style + "_atmosphere_";
        var effects = WithRoot(atmosphere).OfType<MultiMeshInstance3D>().ToArray();
        Check("bounded_cosmetic_effects", atmosphere.Capacity is > 0 and <= 16 && atmosphere.ActiveCount is > 0 &&
            atmosphere.ActiveCount <= atmosphere.Capacity && effects.Length == 1 && effects[0].Multimesh.InstanceCount == atmosphere.Capacity &&
            !WithRoot(atmosphere).Any(n => n is CollisionObject3D or CollisionShape3D or NavigationRegion3D or Light3D));
        double initial = atmosphere.MotionTime;
        atmosphere.Animate(.25, paused: false, reducedEffects: false);
        Check("motion_advances_during_play", atmosphere.MotionTime > initial && atmosphere.Visible && atmosphere.ActiveCount > 0);
        double playing = atmosphere.MotionTime; var pose = Pose(effects);
        atmosphere.Animate(1, paused: true, reducedEffects: false);
        Check("pause_freezes_time_and_transforms", atmosphere.MotionTime == playing && SamePose(effects, pose));
        atmosphere.Animate(1, paused: true, reducedEffects: true);
        Check("reduced_effects_hides_even_while_paused", !atmosphere.Visible && atmosphere.ActiveCount == 0 && atmosphere.MotionTime == playing && SamePose(effects, pose));
        atmosphere.Animate(1, paused: false, reducedEffects: true);
        Check("reduced_effects_stops_optional_motion", !atmosphere.Visible && atmosphere.MotionTime == playing && SamePose(effects, pose));
        atmosphere.Animate(0, paused: true, reducedEffects: false);
        Check("restoring_effects_does_not_unpause_motion", atmosphere.Visible && atmosphere.ActiveCount > 0 && atmosphere.MotionTime == playing && SamePose(effects, pose));
        atmosphere.Animate(.25, paused: false, reducedEffects: false);
        Check("resume_advances_from_frozen_time", atmosphere.MotionTime > playing && atmosphere.ActiveCount <= atmosphere.Capacity);
        // Dummy-renderer MultiMesh readback is identity; visual motion is assessed by the rendered scene diagnostic.
        if (DisplayServer.GetName() != "headless")
            Check("rendered_instances_move_after_resume", !SamePose(effects, pose));
        Check("effect_pose_remains_finite", Pose(effects).All(t => t.Origin.IsFinite() && t.Basis.X.IsFinite() && t.Basis.Y.IsFinite() && t.Basis.Z.IsFinite()));

        void Check(string name, bool success) => check(prefix + name, success);
    }

    private sealed record RouteResult(bool Reached, int Ticks, CorePosition Final);
    private static RouteResult Route(RoomDefinition room, CorePosition start, CorePosition target, int range)
    {
        var planner = new ClickMovePlanner(room); var space = new SpatialWorld(room); var position = start;
        bool accepted = range > 0 ? planner.TrySetApproach(start, target, range) : planner.TrySetDestination(start, target);
        if (!accepted) return new(false, 0, position);
        int tick = 0;
        while (planner.Destination is not null && tick < 600)
        {
            var direction = planner.NextDirection(position);
            int step = direction.X != 0 && direction.Z != 0 ? 106 : 150;
            position = space.Move(position, new(position.X + direction.X * step, position.Z + direction.Z * step), CombatSession.ActorRadius);
            if (!space.CanOccupy(position, CombatSession.ActorRadius)) return new(false, tick, position);
            tick++;
        }
        int tolerance = range > 0 ? range : ClickMovePlanner.ArrivalTolerance;
        return new(CorePosition.DistanceSquared(position, target) <= (long)tolerance * tolerance, tick, position);
    }

    private static Vector3? FirstActorLaneIntrusion(Vector3[] faces, RoomDefinition room, SpatialWorld space)
    {
        float x = (room.HalfWidth - CombatSession.ActorRadius) * .001f, z = (room.HalfDepth - CombatSession.ActorRadius) * .001f;
        for (int i = 0; i + 2 < faces.Length; i += 3)
        {
            List<Vector3> polygon = [faces[i], faces[i + 1], faces[i + 2]];
            // Roof overhangs above a character are scenery; solid faces through the body's walking slab are obstacles.
            polygon = Clip(polygon, p => p.Y - .12f); polygon = Clip(polygon, p => 1.8f - p.Y);
            polygon = Clip(polygon, p => p.X + x); polygon = Clip(polygon, p => x - p.X);
            polygon = Clip(polygon, p => p.Z + z); polygon = Clip(polygon, p => z - p.Z);
            if (polygon.Count == 0) continue;
            var center = polygon.Aggregate(Vector3.Zero, (sum, point) => sum + point) / polygon.Count;
            foreach (var point in polygon.Append(center))
                if (space.CanOccupy(new((int)Math.Round(point.X * 1000), (int)Math.Round(point.Z * 1000)), 0)) return point;
        }
        return null;
    }

    private static List<Vector3> Clip(List<Vector3> input, Func<Vector3, float> distance)
    {
        if (input.Count == 0) return input;
        var output = new List<Vector3>(); Vector3 previous = input[^1]; float previousDistance = distance(previous);
        foreach (var current in input)
        {
            float currentDistance = distance(current);
            if ((currentDistance >= 0) != (previousDistance >= 0))
                output.Add(previous.Lerp(current, previousDistance / (previousDistance - currentDistance)));
            if (currentDistance >= 0) output.Add(current);
            previous = current; previousDistance = currentDistance;
        }
        return output;
    }

    private static bool HasUnoccludedBodySample(Node3D target, Vector3[] scenery, Camera3D camera, Rect2 viewport)
    {
        foreach (var mesh in Meshes(target))
        {
            var bounds = mesh.Mesh.GetAabb();
            // Center and face-center samples come from the visible body, not a depth-test-disabled label above it.
            Vector3 center = bounds.GetCenter();
            foreach (var local in new[] { center, center + Vector3.Up * bounds.Size.Y * .35f,
                center + Vector3.Right * bounds.Size.X * .35f, center + Vector3.Left * bounds.Size.X * .35f })
            {
                var world = mesh.GlobalTransform * local; var screen = camera.UnprojectPosition(world);
                if (camera.IsPositionBehind(world) || !viewport.HasPoint(screen)) continue;
                var origin = camera.ProjectRayOrigin(screen); var direction = camera.ProjectRayNormal(screen);
                float targetDistance = (world - origin).Dot(direction);
                bool blocked = false;
                for (int i = 0; i + 2 < scenery.Length; i += 3)
                    if (RayTriangle(origin, direction, scenery[i], scenery[i + 1], scenery[i + 2], targetDistance - .03f)) { blocked = true; break; }
                if (!blocked) return true;
            }
        }
        return false;
    }

    private static bool RayTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, float maximum)
    {
        var edge1 = b - a; var edge2 = c - a; var cross = direction.Cross(edge2); float determinant = edge1.Dot(cross);
        if (Math.Abs(determinant) < .000001f) return false;
        float inverse = 1 / determinant; var offset = origin - a; float u = offset.Dot(cross) * inverse;
        if (u < 0 || u > 1) return false;
        var q = offset.Cross(edge1); float v = direction.Dot(q) * inverse;
        if (v < 0 || u + v > 1) return false;
        float distance = edge2.Dot(q) * inverse;
        return distance >= 0 && distance < maximum;
    }

    private static MeshInstance3D[] Meshes(Node root) => WithRoot(root).OfType<MeshInstance3D>().Where(m => m.Mesh is not null && m.IsVisibleInTree()).ToArray();
    private static Vector3[] Faces(IEnumerable<MeshInstance3D> meshes) => meshes.SelectMany(m => m.Mesh.GetFaces().Select(p => m.GlobalTransform * p)).ToArray();
    private static int Materials(IEnumerable<MeshInstance3D> meshes) => meshes.SelectMany(m => Enumerable.Range(0, m.Mesh.GetSurfaceCount()).Select(m.GetActiveMaterial))
        .Where(m => m is not null).Select(m => m.GetInstanceId()).Distinct().Count();
    private static Transform3D[] Pose(IEnumerable<MultiMeshInstance3D> effects) => effects.SelectMany(m => Enumerable.Range(0, m.Multimesh.InstanceCount).Select(m.Multimesh.GetInstanceTransform)).ToArray();
    private static bool SamePose(IEnumerable<MultiMeshInstance3D> effects, Transform3D[] before)
    { var current = Pose(effects); return current.Length == before.Length && current.Zip(before).All(pair => pair.First.IsEqualApprox(pair.Second)); }
    private static string SafeName(string value) => value.Replace('.', '_').Replace(':', '_');
    private static string PointKey(CorePosition point) => point.X + "_" + point.Z;
    private static string PointKey(Vector3 point) => PointKey(new CorePosition((int)Math.Round(point.X * 1000), (int)Math.Round(point.Z * 1000)));
    private static IEnumerable<Node> WithRoot(Node root) => new[] { root }.Concat(Descendants(root));
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
}
