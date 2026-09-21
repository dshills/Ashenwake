using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    private const int ShapeTemplateLimit = 128;
    private static readonly Dictionary<(Vector3 Size, float Waist), ArrayMesh> ShapeTemplates = [];
    internal static int CachedShapeResourceCount => ShapeTemplates.Count;

    // One chamfer has six broad faces, twelve edge strips and eight corner facets.
    // Its 44 triangles retain the authored extents while catching light along every edge.
    internal static ArrayMesh PolishedBoxMesh(Vector3 size, float waist = 1)
    {
        var key = (size, waist);
        if (ShapeTemplates.TryGetValue(key, out var cached)) return cached;
        Vector3 half = size * .5f;
        float bevel = Math.Min(.04f, Math.Min(size.X, Math.Min(size.Y, size.Z)) * .18f);
        Vector3 inner = half - Vector3.One * bevel;
        using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Taper(Vector3 point) => new(point.X * Mathf.Lerp(waist, 1, (point.Y + half.Y) / size.Y), point.Y, point.Z);
        void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            a = Taper(a); b = Taper(b); c = Taper(c);
            // Godot front faces wind clockwise when viewed from outside.
            if ((b - a).Cross(c - a).Dot(outward) > 0) (b, c) = (c, b);
            Vector3 normal = -(b - a).Cross(c - a).Normalized();
            Vector3 up = Math.Abs(normal.Y) < .9f ? Vector3.Up : Vector3.Right;
            Vector3 u = up.Cross(normal).Normalized(), v = normal.Cross(u);
            float span = Math.Max(size.X, Math.Max(size.Y, size.Z));
            void Vertex(Vector3 point)
            {
                surface.SetNormal(normal);
                surface.SetUV(new(point.Dot(u) / span + .5f, point.Dot(v) / span + .5f));
                surface.AddVertex(point);
            }
            Vertex(a); Vertex(b); Vertex(c);
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        { Triangle(a, b, c, outward); Triangle(a, c, d, outward); }
        for (int side = -1; side <= 1; side += 2)
        {
            Quad(new(side * half.X, -inner.Y, -inner.Z), new(side * half.X, inner.Y, -inner.Z),
                new(side * half.X, inner.Y, inner.Z), new(side * half.X, -inner.Y, inner.Z), new(side, 0, 0));
            Quad(new(-inner.X, side * half.Y, -inner.Z), new(inner.X, side * half.Y, -inner.Z),
                new(inner.X, side * half.Y, inner.Z), new(-inner.X, side * half.Y, inner.Z), new(0, side, 0));
            Quad(new(-inner.X, -inner.Y, side * half.Z), new(inner.X, -inner.Y, side * half.Z),
                new(inner.X, inner.Y, side * half.Z), new(-inner.X, inner.Y, side * half.Z), new(0, 0, side));
        }
        for (int a = -1; a <= 1; a += 2)
            for (int b = -1; b <= 1; b += 2)
            {
                Quad(new(a * half.X, b * inner.Y, -inner.Z), new(a * inner.X, b * half.Y, -inner.Z),
                    new(a * inner.X, b * half.Y, inner.Z), new(a * half.X, b * inner.Y, inner.Z), new(a, b, 0));
                Quad(new(a * half.X, -inner.Y, b * inner.Z), new(a * inner.X, -inner.Y, b * half.Z),
                    new(a * inner.X, inner.Y, b * half.Z), new(a * half.X, inner.Y, b * inner.Z), new(a, 0, b));
                Quad(new(-inner.X, a * half.Y, b * inner.Z), new(-inner.X, a * inner.Y, b * half.Z),
                    new(inner.X, a * inner.Y, b * half.Z), new(inner.X, a * half.Y, b * inner.Z), new(0, a, b));
                for (int c = -1; c <= 1; c += 2)
                    Triangle(new(a * half.X, b * inner.Y, c * inner.Z), new(a * inner.X, b * half.Y, c * inner.Z),
                        new(a * inner.X, b * inner.Y, c * half.Z), new(a, b, c));
            }
        surface.GenerateTangents();
        // Native primitives are indexed. AppendFrom does not create indices for an
        // unindexed source, so every shape must be indexed before material batching.
        surface.Index();
        var mesh = surface.Commit();
        if (ShapeTemplates.Count < ShapeTemplateLimit) ShapeTemplates.Add(key, mesh);
        return mesh;
    }

    private static MeshInstance3D TaperedBox(Node parent, Vector3 at, Vector3 size, Material material, float waist = .78f, Vector3? rotationDegrees = null)
        => Part(parent, PolishedBoxMesh(size, waist), at, material, rotationDegrees);
}
