using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Godot;

namespace Ashenwake.Client;

/// <summary>Muted, deterministic industrial floor detail. Every generated vertex lies below Y=0.</summary>
public static class CinderGround
{
    public static void Build(Node3D parent, RoomDefinition room, string style)
    {
        var b = new EnvironmentBuilder(parent, "AuthoredGround");
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        bool extraction = style is "cinder_extraction" or "cinder_foundry", furnace = style == "cinder_furnace", storm = style == "cinder_storm";
        var routes = Routes(style);
        b.Box(new(x * 2 + 11, .36f, z * 2 + 11), new(0, -.26f, 0), "34343a", surface: SurfaceKind.Earth);
        if (extraction) ExtractionFloor(b, x, z);
        else if (furnace) FurnaceFloor(b, x, z);
        else if (storm) StormFloor(b, x, z);
        else StreetFloor(b, x, z);
        RoutePaving(b, room, style);
        // A subdued kerb seam conveys the true bounds without adding a glowing rectangle beneath hazards.
        for (int i = 0; i < 18; i++)
        {
            float px = -x + (i + .5f) * x * 2 / 18, pz = -z + (i + .5f) * z * 2 / 18;
            b.Box(new(x * 2 / 18 - .13f, .012f, .08f), new(px, -.020f, -z), "68615d");
            b.Box(new(x * 2 / 18 - .13f, .012f, .08f), new(px, -.020f, z), "68615d");
            b.Box(new(.08f, .012f, z * 2 / 18 - .13f), new(-x, -.020f, pz), "68615d");
            b.Box(new(.08f, .012f, z * 2 / 18 - .13f), new(x, -.020f, pz), "68615d");
        }
        b.Flush();
        var root = parent.GetNode<Node3D>("AuthoredGround");
        GroundField(root, room, routes, style);
        BasaltShoulders(root, room, routes, style);
    }

    public static Vector2[][] Routes(string style)
    {
        string encounter = style switch
        {
            "cinder_extraction" => "campaign.extraction_floor",
            "cinder_furnace" => "campaign.furnace_spindle",
            "cinder_storm" => "exploration.burning_rain",
            "cinder_foundry" => "exploration.sealed_foundry",
            _ => "campaign.cinder_pack"
        };
        var entrance = style is "cinder_storm" or "cinder_foundry" ? CinderCampaignLayout.BranchReturn : CinderCampaignLayout.BackExit;
        var main = CinderCampaignLayout.Route(encounter).Prepend(entrance).Select(p => new Vector2(p.X * .001f, p.Z * .001f)).ToArray();
        if (style is not ("cinder_fields" or "cinder_extraction")) return [main];
        var branch = style == "cinder_fields" ? CinderCampaignLayout.FoundryEntrance : CinderCampaignLayout.StormEntrance;
        return [main, [new(6, 0), new(branch.X * .001f, branch.Z * .001f)]];
    }

    private static void RoutePaving(EnvironmentBuilder b, RoomDefinition room, string style)
    {
        var routes = Routes(style);
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        bool metal = style is "cinder_extraction" or "cinder_furnace" or "cinder_foundry";
        int row = 0;
        for (float pz = -z + .44f; pz < z - .3f; pz += .81f, row++)
        {
            int column = 0;
            for (float px = -x + .48f + row % 2 * .11f; px < x - .3f; px += .91f, column++)
            {
                var point = new Vector2(px, pz);
                if (!routes.Any(route => DistanceToRoute(point, route) < 1.16f) || !ClearFloor(room, px, pz, .44f, .39f)) continue;
                uint wear = SurfaceHash(row, column);
                string color = wear % 5 == 0 ? "716967" : "635e5e";
                if (metal) MetalPanel(b, point, color, wear);
                else StonePanel(b, point, color, wear, RouteDistance(point, routes) > .65f);
            }
        }
    }

    private static void MetalPanel(EnvironmentBuilder b, Vector2 point, string color, uint wear)
    {
        // Recessed steel, worn edges and sparse bolts read as construction rather
        // than hazard stripes. All faces stay below the authoritative ground plane.
        b.Box(new(.84f, .019f, .74f), new(point.X, -.020f, point.Y), "363a3f", surface: SurfaceKind.Metal);
        b.Box(new(.78f, .008f, .68f), new(point.X, -.007f, point.Y), color, surface: SurfaceKind.Metal);
        if (wear % 7 == 0)
        {
            b.Box(new(.56f, .002f, .44f), new(point.X, -.0025f, point.Y), "32373d", surface: SurfaceKind.Metal);
            for (int bar = 0; bar < 5; bar++)
                b.Box(new(.55f, .001f, .022f), new(point.X, -.0009f, point.Y + (bar - 2) * .086f), "727477", surface: SurfaceKind.Metal);
        }
        else if (wear % 4 == 0)
        {
            b.Box(new(.37f, .001f, .012f), new(point.X - .035f, -.0015f, point.Y + .12f), "82746b", new(0, -8, 0), surface: SurfaceKind.Metal);
            b.Box(new(.20f, .001f, .010f), new(point.X + .11f, -.0015f, point.Y + .16f), "514b4d", new(0, -8, 0), surface: SurfaceKind.Metal);
        }
        if (wear % 3 != 0) return;
        foreach (float side in new[] { -1f, 1f })
            b.Cylinder(.023f, .020f, .001f, new(point.X + side * .31f, -.0014f, point.Y - .25f), "82746b", surface: SurfaceKind.Metal);
    }

    private static void StonePanel(EnvironmentBuilder b, Vector2 point, string color, uint wear, bool shoulder)
    {
        void Slab(Vector2 offset, float width, float depth)
        {
            var at = point + offset;
            b.Box(new(width, .026f, depth), new(at.X, -.026f, at.Y), "32373d", surface: SurfaceKind.Stone);
            b.Box(new(width - .025f, .008f, depth - .025f), new(at.X, -.009f, at.Y), color, surface: SurfaceKind.Stone);
        }
        if (shoulder && wear % 6 == 0)
        {
            Slab(new(-.13f, 0), .565f, .74f);
            Slab(new(.29f, .075f), .245f, .59f);
        }
        else Slab(Vector2.Zero, .84f, .74f);
        if (!shoulder || wear % 9 != 0) return;
        FlatSeam(b, new(point.X - .28f, -.003f, point.Y - .20f), new(point.X + .03f, -.003f, point.Y + .025f), .01f);
        FlatSeam(b, new(point.X + .03f, -.003f, point.Y + .025f), new(point.X + .29f, -.003f, point.Y + .11f), .008f);
    }

    private static void FlatSeam(EnvironmentBuilder b, Vector3 from, Vector3 to, float width)
    {
        Vector3 span = to - from;
        b.Box(new(width, .0015f, span.Length()), (from + to) * .5f, "41454b",
            new(0, Mathf.RadToDeg(Mathf.Atan2(span.X, span.Z)), 0), surface: SurfaceKind.Stone);
    }

    private static void GroundField(Node3D root, RoomDefinition room, Vector2[][] routes, string style)
    {
        // One continuous surface carries cooled flows and drifting ash. Color changes
        // are broad and non-emissive; no luminous fissure can be mistaken for a vent tell.
        const int columns = 64, rows = 52;
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        bool storm = style == "cinder_storm";
        Color basalt = new(storm ? "43464c" : "46434a"), ash = new(storm ? "666461" : "5a5352"), soot = new("30353b");
        using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        for (int row = 0; row <= rows; row++)
            for (int column = 0; column <= columns; column++)
            {
                Vector2 point = new(-x + column * x * 2 / columns, -z + row * z * 2 / rows);
                float flow = .5f + .24f * MathF.Sin(point.X * .42f + MathF.Sin(point.Y * .31f)) +
                    .16f * MathF.Sin(point.Y * .64f - point.X * .24f) + .07f * MathF.Sin(point.X * 1.47f + point.Y * 1.13f);
                float drift = .5f + .32f * MathF.Sin(point.Y * 1.06f + point.X * .37f + MathF.Sin(point.X * .34f)) +
                    .14f * MathF.Sin(point.Y * 2.15f + point.X * .78f);
                float detail = QuietWeight(point, routes, style);
                float wallDust = 0;
                foreach (var obstacle in room.Obstacles)
                {
                    float dx = Math.Max(obstacle.MinX * .001f - point.X, Math.Max(0, point.X - obstacle.MaxX * .001f));
                    float dz = Math.Max(obstacle.MinZ * .001f - point.Y, Math.Max(0, point.Y - obstacle.MaxZ * .001f));
                    wallDust = Math.Max(wallDust, 1 - Mathf.SmoothStep(.05f, 1.15f, MathF.Sqrt(dx * dx + dz * dz)));
                }
                float ashAmount = Mathf.SmoothStep(.36f, .78f, drift) * detail * (storm ? .48f : .28f);
                ashAmount = Math.Max(ashAmount, wallDust * .16f);
                Color color = basalt.Lerp(soot, Mathf.SmoothStep(.52f, .84f, flow) * detail * .48f).Lerp(ash, ashAmount);
                surface.SetColor(color); surface.SetNormal(Vector3.Up); surface.SetUV(point * .2f);
                surface.AddVertex(new(point.X, -.079f, point.Y));
            }
        for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                int a = row * (columns + 1) + column, b = a + 1, c = a + columns + 1, d = c + 1;
                surface.AddIndex(a); surface.AddIndex(b); surface.AddIndex(c);
                surface.AddIndex(b); surface.AddIndex(d); surface.AddIndex(c);
            }
        AddColoredSurface(root, surface, "CooledBasaltAndAsh", SurfaceKind.Earth);
    }

    private static void BasaltShoulders(Node3D root, RoomDefinition room, Vector2[][] routes, string style)
    {
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        var occupied = new List<(Vector2 Point, float Radius)>();
        int patches = 0;
        for (int site = 0; site < 42; site++)
        {
            uint shape = SurfaceHash(site + 51, 89);
            Vector2 center = new(-x + 1.25f + (shape & 1023) / 1023f * (x * 2 - 2.5f),
                -z + 1.25f + ((shape >> 10) & 1023) / 1023f * (z * 2 - 2.5f));
            float width = .65f + (shape >> 20 & 7) * .09f, depth = .56f + (shape >> 24 & 7) * .085f;
            if (QuietWeight(center, routes, style) < .36f || !ClearFloor(room, center.X, center.Y, width * .55f, depth * .55f)) continue;
            float radius = Math.Max(width, depth) * .55f;
            if (occupied.Any(p => p.Point.DistanceSquaredTo(center) < (p.Radius + radius) * (p.Radius + radius))) continue;
            occupied.Add((center, radius));
            Color stone = new(site % 3 == 0 ? "514b50" : site % 3 == 1 ? "49464d" : "45454c");
            // Six unequal fracture edges create a low broken plate, without a grid of
            // regular polygons. The bevel falls back into the connected ash surface.
            var edge = new Vector3[6];
            for (int corner = 0; corner < edge.Length; corner++)
            {
                float angle = corner * Mathf.Tau / 6 + (shape % 360) * Mathf.Pi / 180;
                float inset = .76f + ((shape >> (corner * 4)) & 7) * .027f;
                edge[corner] = new(center.X + MathF.Sin(angle) * width * .5f * inset, -.043f,
                    center.Y + MathF.Cos(angle) * depth * .5f * inset);
            }
            void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
            {
                Vector3 normal = -(b - a).Cross(c - a).Normalized();
                foreach (var point in new[] { a, b, c })
                { surface.SetColor(color); surface.SetNormal(normal); surface.SetUV(new(point.X * .6f, point.Z * .6f)); surface.AddVertex(point); }
            }
            var middle = new Vector3(center.X, -.037f, center.Y);
            for (int corner = 0; corner < edge.Length; corner++)
            {
                Vector3 a = edge[corner], b = edge[(corner + 1) % edge.Length];
                Vector3 lowerA = new(center.X + (a.X - center.X) * 1.08f, -.076f, center.Y + (a.Z - center.Y) * 1.08f);
                Vector3 lowerB = new(center.X + (b.X - center.X) * 1.08f, -.076f, center.Y + (b.Z - center.Y) * 1.08f);
                // Ring points run counterclockwise in XZ, so reverse the top fan for Godot's clockwise faces.
                Triangle(middle, b, a, corner % 3 == 0 ? stone.Lightened(.028f) : stone);
                Triangle(a, b, lowerB, stone.Darkened(.18f));
                Triangle(a, lowerB, lowerA, stone.Darkened(.18f));
            }
            patches++;
        }
        if (patches > 0) { surface.Index(); AddColoredSurface(root, surface, "FracturedBasaltShoulders", SurfaceKind.Stone); }
    }

    private static void AddColoredSurface(Node3D root, SurfaceTool surface, string name, SurfaceKind kind)
    {
        var material = SurfaceMaterials.Create("ffffff", kind, worldScale: true);
        material.VertexColorUseAsAlbedo = true; material.VertexColorIsSrgb = true;
        surface.GenerateTangents(); surface.SetMaterial(material);
        root.AddChild(new MeshInstance3D { Name = name, Mesh = surface.Commit() });
    }

    private static float RouteDistance(Vector2 point, Vector2[][] routes)
    {
        float distance = float.PositiveInfinity;
        foreach (var route in routes) distance = Math.Min(distance, DistanceToRoute(point, route));
        return distance;
    }

    private static float QuietWeight(Vector2 point, Vector2[][] routes, string style)
    {
        float detail = Mathf.SmoothStep(1.1f, 3.3f, RouteDistance(point, routes));
        if (style == "cinder_furnace") detail *= Mathf.SmoothStep(4.7f, 7.5f, point.Length());
        if (style == "cinder_foundry")
            detail *= Mathf.SmoothStep(1.8f, 3, point.DistanceTo(new(CinderCampaignLayout.FoundryTreasure.X * .001f, CinderCampaignLayout.FoundryTreasure.Z * .001f)));
        return detail;
    }

    private static uint SurfaceHash(int row, int column)
    {
        uint hash = unchecked((uint)row * 0x9E3779B9u ^ (uint)column * 0x85EBCA6Bu ^ 0xC2B2AE35u);
        hash ^= hash >> 16; hash *= 0x7FEB352Du; hash ^= hash >> 15; hash *= 0x846CA68Bu;
        return hash ^ (hash >> 16);
    }

    private static bool ClearFloor(RoomDefinition room, float x, float z, float halfWidth, float halfDepth)
        => Math.Abs(x) + halfWidth < room.HalfWidth * .001f && Math.Abs(z) + halfDepth < room.HalfDepth * .001f &&
            !room.Obstacles.Any(obstacle => x + halfWidth > obstacle.MinX * .001f && x - halfWidth < obstacle.MaxX * .001f &&
                z + halfDepth > obstacle.MinZ * .001f && z - halfDepth < obstacle.MaxZ * .001f);

    private static float DistanceToRoute(Vector2 point, Vector2[] route)
    {
        float distance = float.PositiveInfinity;
        for (int i = 1; i < route.Length; i++)
        {
            Vector2 segment = route[i] - route[i - 1];
            float t = Math.Clamp((point - route[i - 1]).Dot(segment) / Math.Max(.001f, segment.LengthSquared()), 0, 1);
            distance = Math.Min(distance, point.DistanceTo(route[i - 1] + segment * t));
        }
        return distance;
    }

    private static void StreetFloor(EnvironmentBuilder b, float x, float z)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            float px = side * x * .53f;
            b.Box(new(.21f, .012f, z * 1.85f), new(px, -.03f, 0), "32373d");
            b.Box(new(.07f, .014f, z * 1.85f), new(px + side * .18f, -.025f, 0), "675e58", surface: SurfaceKind.Metal);
            for (int i = 0; i < 11; i++)
                b.Box(new(.43f, .01f, .035f), new(px, -.018f, -z * .85f + i * z * .17f), "575659", surface: SurfaceKind.Metal);
        }
        for (int row = 0; row < 13; row++)
        {
            float pz = -z * .87f + row * z * .145f;
            b.Box(new(x * .62f, .01f, .15f), new(row % 2 == 0 ? -.09f : .09f, -.029f, pz), "514b4d");
        }
    }

    private static void ExtractionFloor(EnvironmentBuilder b, float x, float z)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            float px = side * x * .55f;
            b.Box(new(x * .43f, .012f, z * 1.82f), new(px, -.033f, 0), "363a3f", surface: SurfaceKind.Metal);
            for (int i = 0; i < 19; i++)
                b.Box(new(x * .41f, .012f, .09f), new(px, -.023f, -z * .86f + i * z * .095f), "575659", surface: SurfaceKind.Metal);
            foreach (float rail in new[] { -1f, 1f })
                b.Box(new(.085f, .012f, z * 1.86f), new(px + rail * x * .225f, -.019f, 0), "675e58", surface: SurfaceKind.Metal);
        }
        // Static maintenance hatches have square seams; no arrow or illuminated conveyor implies extra mechanics.
        foreach (float pz in new[] { -z * .55f, z * .55f })
        {
            b.Box(new(2.1f, .012f, 1.45f), new(0, -.028f, pz), "575659", surface: SurfaceKind.Metal);
            b.Box(new(1.92f, .012f, 1.27f), new(0, -.019f, pz), "403e44");
            for (int i = 0; i < 5; i++) b.Box(new(1.7f, .006f, .045f), new(0, -.009f, pz + (i - 2) * .21f), "514b4d");
        }
    }

    private static void FurnaceFloor(EnvironmentBuilder b, float x, float z)
    {
        float radius = Math.Min(x, z) * .76f;
        for (int ring = 0; ring < 3; ring++)
        {
            float r = radius * (.43f + ring * .26f);
            for (int segment = 0; segment < 32; segment++)
            {
                // Broad gaps at the cardinal lanes keep actual rotating vent warnings visually dominant.
                if (segment % 8 <= 1) continue;
                float a = segment * Mathf.Tau / 32;
                b.Box(new(r * .16f, .01f, ring == 1 ? .1f : .06f), new(Mathf.Sin(a) * r, -.024f, Mathf.Cos(a) * r), "575051", new(0, segment * 11.25f, 0));
            }
        }
        foreach (float side in new[] { -1f, 1f })
        {
            b.Box(new(.38f, .014f, z * 1.7f), new(side * x * .9f, -.026f, 0), "363a3f", surface: SurfaceKind.Metal);
            b.Box(new(x * 1.7f, .014f, .38f), new(0, -.026f, side * z * .9f), "363a3f", surface: SurfaceKind.Metal);
            for (int i = 0; i < 16; i++)
            {
                b.Box(new(.31f, .008f, .05f), new(side * x * .9f, -.013f, -z * .77f + i * z * 1.54f / 15), "675e58", surface: SurfaceKind.Metal);
                b.Box(new(.05f, .008f, .31f), new(-x * .77f + i * x * 1.54f / 15, -.013f, side * z * .9f), "675e58", surface: SurfaceKind.Metal);
            }
        }
    }

    private static void StormFloor(EnvironmentBuilder b, float x, float z)
    {
        // Directional ash is now part of GroundField's continuous color variation.
        // Only the old maintenance kerbs remain as geometry beside the storm arena.
        foreach (float side in new[] { -1f, 1f })
            for (int i = 0; i < 6; i++)
            {
                float pz = -z * .75f + i * z * .29f;
                b.Box(new(.07f, .01f, z * .19f), new(side * x * .82f, -.019f, pz), "675e58");
                b.Box(new(.34f, .012f, .31f), new(side * x * .82f, -.024f, pz + z * .08f), "363a3f");
            }
    }
}
