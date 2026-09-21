using Ashenwake.Core.Content;
using Godot;

namespace Ashenwake.Client;

/// <summary>Walkable courts and connecting paths, all below the authoritative floor and its warnings.</summary>
public static class OpeningGround
{
    public static bool Supports(string style) => style is "greyhaven" or "road" or "monastery";

    public static void Build(Node3D parent, RoomDefinition room, string style)
    {
        var b = new EnvironmentBuilder(parent, "AuthoredGround");
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        bool hub = style == "greyhaven", road = style == "road";
        // The inhabited floor meets a narrow weathered shoulder; distant terrain supplies
        // the silhouette instead of a single oversized rectangular slab.
        b.Box(new(x * 2 + .8f, .5f, z * 2 + .8f), new(0, -.35f, 0), hub ? "303f36" : "303c37", surface: SurfaceKind.Earth);
        var route = Route(x, style);
        float stepX = hub ? .72f : 1.03f, stepZ = hub ? .57f : .76f;
        int row = 0;
        for (float pz = -z + .42f; pz < z - .25f; pz += stepZ, row++)
        {
            int column = 0;
            for (float px = -x + .5f + row % 2 * stepX * .48f; px < x - .3f; px += stepX, column++)
            {
                int variation = (row * 31 + column * 17) % 23;
                Vector2 point = new(px, pz);
                float pathDistance = DistanceToRoute(point, route);
                int court = hub ? Court(point) : 0;
                bool courtyard = !hub && !road && Math.Abs(px) < x * .65f && Math.Abs(pz) < z * .65f;
                bool paved = court != 0 || pathDistance < (hub ? 1.15f : road ? 1.65f : 1.9f) || courtyard;
                if (!paved)
                {
                    // Isolated stones sit near the worn shoulders, not across a patterned lawn.
                    if (pathDistance > 2.6f || variation != 7) continue;
                }
                // A few missing stones create wear without removing walkable ground.
                if (!hub && variation == 1 && pathDistance > 1.1f) continue;
                string color = court switch
                {
                    1 => variation % 3 == 0 ? "71807b" : "5d7069",
                    2 => variation % 3 == 0 ? "877663" : "6c6254",
                    3 => variation % 3 == 0 ? "837e67" : "6d715c",
                    _ => variation % 3 == 0 ? "798079" : variation % 3 == 1 ? "616f68" : "56635d"
                };
                float width = Math.Min(stepX - .05f, (x - px) * 2 - .05f);
                float depth = Math.Min(stepZ - .055f, (z - pz) * 2 - .05f);
                if (!paved) { width *= .55f; depth *= .7f; color = "4b5c51"; }
                b.Box(new(width, .055f, depth), new(px, -.051f, pz), color,
                    new(0, paved && hub ? 0 : variation % 5 - 2, 0));
            }
        }
        if (hub)
        {
            CourtInlay(b, new(-4.5f, 0), new(2.2f, 3.7f), "8b9a86");
            CourtInlay(b, new(2, -2.2f), new(2.8f, 2.15f), "ac9170");
            CourtInlay(b, new(2.2f, 3.5f), new(3.9f, 2.05f), "9b9478");
            // Shallow runoff channels separate the courts; stone crossings remain flush.
            Drain(b, -1.45f, -z + .4f, -2.2f);
            Drain(b, -1.45f, 2.1f, z - .4f);
        }
        else if (!road)
        {
            // The cloister is a broken courtyard with planted outer aisles and a clear center.
            float hx = x * .67f, hz = z * .67f;
            foreach (float side in new[] { -1f, 1f })
            {
                b.Box(new(.16f, .015f, hz * 2), new(side * hx, -.017f, 0), "909b87");
                b.Box(new(hx * 2, .015f, .16f), new(0, -.017f, side * hz), "909b87");
                for (int i = 0; i < 7; i++)
                    b.Box(new(.12f, .008f, .46f), new(-hx + .8f + i * (hx * 2 - 1.6f) / 6, -.011f, side * (hz - .45f)), "75816f");
            }
        }
        Boundary(b, x, z, hub);
        b.Flush();
        GroundField(parent.GetNode<Node3D>("AuthoredGround"), x, z, route, hub);
    }

    public static Vector2[] Route(float halfWidth, string style)
    {
        float end = Math.Max(.5f, halfWidth - 1.4f);
        return style == "road"
            ? [new(-end, 0), new(-end * .57f, 0), new(-end * .19f, -1.15f), new(end * .19f, 1.9f), new(end * .57f, 0), new(end, 0)]
            : [new(-end, 0), new(-end * .43f, 0), new(0, 0), new(end * .62f, 0), new(end, 0)];
    }

    private static void GroundField(Node3D root, float x, float z, Vector2[] route, bool hub)
    {
        // A single softly varying earth surface connects the courts and the scenery.
        // Vertex tinting avoids tiled patch props and keeps the material/texture count fixed.
        const int columns = 48, rows = 40;
        using var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        Color soil = new(hub ? "4c4b3c" : "4e5144"), grass = new(hub ? "3e4d39" : "394d40");
        for (int row = 0; row <= rows; row++)
            for (int column = 0; column <= columns; column++)
            {
                Vector2 p = new(-x + column * x * 2 / columns, -z + row * z * 2 / rows);
                float broad = .5f + Mathf.Sin(p.X * .53f + Mathf.Sin(p.Y * .4f)) * .25f +
                    Mathf.Sin(p.Y * .71f - p.X * .21f) * .17f + Mathf.Sin(p.X * 1.73f + p.Y * 1.31f) * .06f;
                float shoulder = Mathf.SmoothStep(1.3f, 3.4f, DistanceToRoute(p, route));
                surface.SetColor(soil.Lerp(grass, Math.Clamp(broad * shoulder, 0, 1)));
                surface.SetNormal(Vector3.Up);
                surface.SetUV(p * .2f);
                surface.AddVertex(new(p.X, -.079f, p.Y));
            }
        for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                int a = row * (columns + 1) + column, b = a + 1, c = a + columns + 1, d = c + 1;
                foreach (int index in new[] { a, b, c, b, d, c }) surface.AddIndex(index);
            }
        var material = SurfaceMaterials.Create("ffffff", SurfaceKind.Earth, worldScale: true);
        material.VertexColorUseAsAlbedo = true;
        material.VertexColorIsSrgb = true;
        surface.GenerateTangents(); surface.SetMaterial(material);
        root.AddChild(new MeshInstance3D { Name = "EarthAndGrass", Mesh = surface.Commit() });
    }

    private static int Court(Vector2 point)
    {
        if (Inside(point, new(-4.5f, 0), new(2.3f, 3.8f))) return 1;
        if (Inside(point, new(2, -2.2f), new(2.9f, 2.25f))) return 2;
        if (Inside(point, new(2.2f, 3.5f), new(4, 2.15f))) return 3;
        return 0;
    }
    private static bool Inside(Vector2 point, Vector2 center, Vector2 extent)
        => Math.Abs(point.X - center.X) <= extent.X && Math.Abs(point.Y - center.Y) <= extent.Y;

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

    private static void CourtInlay(EnvironmentBuilder b, Vector2 center, Vector2 extent, string color)
    {
        // Corner stones suggest an inhabited district without outlining a closed barrier.
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                Vector3 corner = new(center.X + sx * extent.X, -.014f, center.Y + sz * extent.Y);
                b.Box(new(.75f, .012f, .08f), corner - new Vector3(sx * .34f, 0, 0), color);
                b.Box(new(.08f, .012f, .75f), corner - new Vector3(0, 0, sz * .34f), color);
            }
    }

    private static void Drain(EnvironmentBuilder b, float x, float from, float to)
    {
        if (to <= from) return;
        b.Box(new(.16f, .012f, to - from), new(x, -.082f, (from + to) * .5f), "1f302e");
        foreach (float side in new[] { -1f, 1f })
            b.Box(new(.11f, .025f, to - from), new(x + side * .15f, -.041f, (from + to) * .5f), "53665b");
    }

    private static void Boundary(EnvironmentBuilder b, float x, float z, bool hub)
    {
        // Recessed edging marks the real collision limit. East/west crossing stones
        // align with the approach markers; the border never masquerades as an open map.
        string edge = hub ? "697667" : "657468";
        b.Box(new(x * 2, .032f, .12f), new(0, -.043f, -z), edge);
        b.Box(new(x * 2, .032f, .12f), new(0, -.043f, z), edge);
        b.Box(new(.12f, .032f, z * 2), new(-x, -.043f, 0), edge);
        b.Box(new(.12f, .032f, z * 2), new(x, -.043f, 0), edge);
    }
}
