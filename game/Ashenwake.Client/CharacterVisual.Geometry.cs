using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    private const int ShapeTemplateLimit = 128;
    private const int BevelSegments = 3;
    private static readonly Dictionary<(Vector3 Size, float Waist), ArrayMesh> ShapeTemplates = [];
    private static readonly Dictionary<(float Bottom, float Top, float Height), CylinderMesh> RoundTemplates = [];
    private static readonly Dictionary<(float Inner, float Outer), TorusMesh> RingTemplates = [];
    internal static int CachedShapeResourceCount => ShapeTemplates.Count;
    // Godot adds one latitude interval: eleven rings place an equator exactly at radius .5.
    internal static SphereMesh OrganicSphereMesh { get; } = new() { Radius = .5f, Height = 1, RadialSegments = 24, Rings = 11 };

    internal static CylinderMesh RoundedConeMesh(float bottomRadius, float topRadius, float height)
    {
        var key = (bottomRadius, topRadius, height);
        if (RoundTemplates.TryGetValue(key, out var cached)) return cached;
        var mesh = new CylinderMesh { BottomRadius = bottomRadius, TopRadius = topRadius, Height = height, RadialSegments = 20, Rings = 1 };
        if (RoundTemplates.Count < ShapeTemplateLimit) RoundTemplates.Add(key, mesh);
        return mesh;
    }

    private static TorusMesh RoundedRingMesh(float inner, float outer)
    {
        var key = (inner, outer);
        if (RingTemplates.TryGetValue(key, out var cached)) return cached;
        var mesh = new TorusMesh { InnerRadius = inner, OuterRadius = outer, Rings = 24, RingSegments = 12 };
        if (RingTemplates.Count < ShapeTemplateLimit) RingTemplates.Add(key, mesh);
        return mesh;
    }

    // Broad planar faces preserve armor and weapon silhouettes; three rounded edge
    // segments and spherical corners catch light smoothly within the authored bounds.
    internal static ArrayMesh PolishedBoxMesh(Vector3 size, float waist = 1)
    {
        var key = (size, waist);
        if (ShapeTemplates.TryGetValue(key, out var cached)) return cached;
        Vector3 half = size * .5f;
        float bevel = Math.Min(.04f, Math.Min(size.X, Math.Min(size.Y, size.Z)) * .18f);
        Vector3 inner = half - Vector3.One * bevel;
        float span = Math.Max(size.X, Math.Max(size.Y, size.Z));
        using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Taper(Vector3 point) => new(point.X * Mathf.Lerp(waist, 1, (point.Y + half.Y) / size.Y), point.Y, point.Z);
        void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector3 outward)
        {
            // Godot front faces wind clockwise when viewed from outside.
            if ((b - a).Cross(c - a).Dot(outward) > 0) { (b, c) = (c, b); (nb, nc) = (nc, nb); }
            Vector3 up = Math.Abs(outward.Y) < .9f ? Vector3.Up : Vector3.Right;
            Vector3 u = up.Cross(outward).Normalized(), v = outward.Cross(u).Normalized();
            void Vertex(Vector3 point, Vector3 normal)
            {
                float scale = Mathf.Lerp(waist, 1, (point.Y + half.Y) / size.Y);
                // Inverse-transpose of the taper Jacobian keeps the lighting normal
                // perpendicular to the surface, including curved breastplate edges.
                Vector3 taperedNormal = new(normal.X / scale,
                    normal.Y - normal.X * point.X * (1 - waist) / (size.Y * scale), normal.Z);
                surface.SetNormal(taperedNormal.Normalized());
                surface.SetUV(new(point.Dot(u) / span + .5f, point.Dot(v) / span + .5f));
                surface.AddVertex(Taper(point));
            }
            Vertex(a, na); Vertex(b, nb); Vertex(c, nc);
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        { Triangle(a, b, c, outward, outward, outward, outward); Triangle(a, c, d, outward, outward, outward, outward); }
        for (int side = -1; side <= 1; side += 2)
        {
            Quad(new(side * half.X, -inner.Y, -inner.Z), new(side * half.X, inner.Y, -inner.Z),
                new(side * half.X, inner.Y, inner.Z), new(side * half.X, -inner.Y, inner.Z), new(side, 0, 0));
            Quad(new(-inner.X, side * half.Y, -inner.Z), new(inner.X, side * half.Y, -inner.Z),
                new(inner.X, side * half.Y, inner.Z), new(-inner.X, side * half.Y, inner.Z), new(0, side, 0));
            Quad(new(-inner.X, -inner.Y, side * half.Z), new(inner.X, -inner.Y, side * half.Z),
                new(inner.X, inner.Y, side * half.Z), new(-inner.X, inner.Y, side * half.Z), new(0, 0, side));
        }
        void Edge(Vector3 start, Vector3 end, Vector3 firstNormal, Vector3 lastNormal)
        {
            Vector3 outward = (firstNormal + lastNormal).Normalized();
            for (int step = 0; step < BevelSegments; step++)
            {
                Vector3 n0 = firstNormal.Lerp(lastNormal, (float)step / BevelSegments).Normalized();
                Vector3 n1 = firstNormal.Lerp(lastNormal, (float)(step + 1) / BevelSegments).Normalized();
                Vector3 a = start + n0 * bevel, b = start + n1 * bevel, c = end + n1 * bevel, d = end + n0 * bevel;
                Triangle(a, b, c, n0, n1, n1, outward); Triangle(a, c, d, n0, n1, n0, outward);
            }
        }
        for (int a = -1; a <= 1; a += 2)
            for (int b = -1; b <= 1; b += 2)
            {
                Edge(new(a * inner.X, b * inner.Y, -inner.Z), new(a * inner.X, b * inner.Y, inner.Z), new(a, 0, 0), new(0, b, 0));
                Edge(new(a * inner.X, -inner.Y, b * inner.Z), new(a * inner.X, inner.Y, b * inner.Z), new(a, 0, 0), new(0, 0, b));
                Edge(new(-inner.X, a * inner.Y, b * inner.Z), new(inner.X, a * inner.Y, b * inner.Z), new(0, a, 0), new(0, 0, b));
                for (int c = -1; c <= 1; c += 2)
                {
                    Vector3 center = new(a * inner.X, b * inner.Y, c * inner.Z), outward = new Vector3(a, b, c).Normalized();
                    Vector3 Normal(int x, int y) => new Vector3(a * x, b * y, c * (BevelSegments - x - y)).Normalized();
                    void Corner(Vector3 n0, Vector3 n1, Vector3 n2) => Triangle(center + n0 * bevel, center + n1 * bevel,
                        center + n2 * bevel, n0, n1, n2, outward);
                    for (int x = 0; x < BevelSegments; x++)
                        for (int y = 0; y < BevelSegments - x; y++)
                        {
                            Corner(Normal(x, y), Normal(x + 1, y), Normal(x, y + 1));
                            if (x + y < BevelSegments - 1) Corner(Normal(x + 1, y), Normal(x + 1, y + 1), Normal(x, y + 1));
                        }
                }
            }
        surface.GenerateTangents();
        // Every shape is indexed before material batching with native primitives.
        surface.Index();
        var mesh = surface.Commit();
        if (ShapeTemplates.Count < ShapeTemplateLimit) ShapeTemplates.Add(key, mesh);
        return mesh;
    }

    private static MeshInstance3D TaperedBox(Node parent, Vector3 at, Vector3 size, Material material, float waist = .78f, Vector3? rotationDegrees = null)
        => Part(parent, PolishedBoxMesh(size, waist), at, material, rotationDegrees);
}
