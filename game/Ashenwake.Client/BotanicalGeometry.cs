using Godot;

namespace Ashenwake.Client;

/// <summary>Indexed botanical surfaces with separate front/back normals. Callers own the mesh;
/// environment builders batch and release it, while moving foliage keeps one shared room mesh.</summary>
public static class BotanicalGeometry
{
    public static ArrayMesh Leaf(float length, float width, float curl)
    {
        if (!float.IsFinite(length) || length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (!float.IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (!float.IsFinite(curl)) throw new ArgumentOutOfRangeException(nameof(curl));
        using var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        float[] widths = [.025f, .62f, 1, .84f, .47f, .015f];
        void Vertex(int column, int row)
        {
            float t = row / 5f, across = column - 1;
            surface.SetUV(new(column * .5f, t));
            surface.AddVertex(new(across * width * widths[row] * .5f + width * .035f * MathF.Sin(t * Mathf.Pi),
                t * length, curl * t * t + width * .13f * Math.Abs(across) * MathF.Sin(t * Mathf.Pi)));
        }
        void Triangle((int X, int Y) a, (int X, int Y) b, (int X, int Y) c)
        {
            surface.SetSmoothGroup(0); Vertex(a.X, a.Y); Vertex(b.X, b.Y); Vertex(c.X, c.Y);
            surface.SetSmoothGroup(1); Vertex(c.X, c.Y); Vertex(b.X, b.Y); Vertex(a.X, a.Y);
        }
        for (int row = 0; row < 5; row++)
            for (int column = 0; column < 2; column++)
            {
                Triangle((column, row), (column, row + 1), (column + 1, row + 1));
                Triangle((column, row), (column + 1, row + 1), (column + 1, row));
            }
        surface.GenerateNormals(); surface.GenerateTangents(); surface.Index();
        return surface.Commit();
    }
}
