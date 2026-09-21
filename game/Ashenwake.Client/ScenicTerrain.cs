using Godot;

namespace Ashenwake.Client;

internal readonly record struct ScenicRock(Vector3 Base, Vector3 Size, float Yaw = 0, int Variant = 0);
internal readonly record struct ScenicTerrace(Vector3 Base, Vector3 Size);

/// <summary>Small, indexed cliff batches. Callers keep their complete footprints outside the playable floor.</summary>
internal static class ScenicTerrain
{
    public static void Build(Node3D architecture, IEnumerable<ScenicRock> rocks, string dark, string stone, string light,
        IEnumerable<ScenicTerrace>? terraces = null)
    {
        string[] palette = [dark, stone, light];
        var surfaces = palette.Select(_ => new SurfaceTool()).ToArray();
        foreach (var surface in surfaces) surface.Begin(Mesh.PrimitiveType.Triangles);
        try
        {
            foreach (var rock in rocks) AddRock(surfaces, rock);
            if (terraces is not null) foreach (var terrace in terraces) AddTerrace(surfaces, terrace);
            var root = new Node3D { Name = "OutlyingTerrain" }; architecture.AddChild(root);
            for (int i = 0; i < surfaces.Length; i++)
            {
                // Reuse the architecture's immutable stone materials instead of adding a draw palette per rock.
                var material = architecture.GetChildren().OfType<MeshInstance3D>().Select(mesh => mesh.GetActiveMaterial(0))
                    .OfType<StandardMaterial3D>().FirstOrDefault(value => value.AlbedoColor == new Color(palette[i]))
                    ?? SurfaceMaterials.Create(palette[i], SurfaceKind.Stone, worldScale: true);
                surfaces[i].GenerateTangents();
                surfaces[i].Index();
                surfaces[i].SetMaterial(material);
                root.AddChild(new MeshInstance3D { Name = "CliffFacets" + i, Mesh = surfaces[i].Commit() });
            }
        }
        finally { foreach (var surface in surfaces) surface.Dispose(); }
    }

    private static void AddRock(SurfaceTool[] surfaces, ScenicRock rock)
    {
        const int sides = 8;
        Vector3 Point(int side, int ring)
        {
            float angle = side * Mathf.Tau / sides;
            float irregular = .84f + ((side * 5 + rock.Variant * 3) % 7) * .025f;
            float radius = ring == 0 ? 1 : ring == 1 ? .96f : .56f;
            float height = ring == 0 ? -.025f : ring == 1 ? .34f : .80f + ((side + rock.Variant) % 3) * .055f;
            Vector3 local = new(Mathf.Cos(angle) * rock.Size.X * .5f * radius * irregular,
                rock.Size.Y * height, Mathf.Sin(angle) * rock.Size.Z * .5f * radius * irregular);
            return rock.Base + local.Rotated(Vector3.Up, Mathf.DegToRad(rock.Yaw));
        }
        void Triangle(Vector3 a, Vector3 b, Vector3 c)
            => AddTriangle(surfaces, a, b, c, rock.Base + Vector3.Up * rock.Size.Y * .4f);
        for (int side = 0; side < sides; side++)
        {
            int next = (side + 1) % sides;
            for (int ring = 0; ring < 2; ring++)
            {
                Triangle(Point(side, ring), Point(next, ring), Point(next, ring + 1));
                Triangle(Point(side, ring), Point(next, ring + 1), Point(side, ring + 1));
            }
            Triangle(Point(side, 2), Point(next, 2), rock.Base + new Vector3(.03f * rock.Size.X, rock.Size.Y, 0));
            Triangle(Point(next, 0), Point(side, 0), rock.Base - Vector3.Up * rock.Size.Y * .025f);
        }
    }

    private static void AddTerrace(SurfaceTool[] surfaces, ScenicTerrace terrace)
    {
        Vector2[] outline = [new(-1, -.84f), new(-.84f, -1), new(.84f, -1), new(1, -.84f),
            new(1, .84f), new(.84f, 1), new(-.84f, 1), new(-1, .84f)];
        Vector3 Point(int index, bool top) => terrace.Base + new Vector3(outline[index].X * terrace.Size.X * .5f,
            top ? terrace.Size.Y : 0, outline[index].Y * terrace.Size.Z * .5f);
        Vector3 inside = terrace.Base + Vector3.Up * terrace.Size.Y * .5f;
        for (int index = 0; index < outline.Length; index++)
        {
            int next = (index + 1) % outline.Length;
            AddTriangle(surfaces, Point(index, false), Point(next, false), Point(next, true), inside);
            AddTriangle(surfaces, Point(index, false), Point(next, true), Point(index, true), inside);
            AddTriangle(surfaces, Point(index, true), Point(next, true), terrace.Base + Vector3.Up * terrace.Size.Y, inside, 1);
            AddTriangle(surfaces, Point(next, false), Point(index, false), terrace.Base, inside);
        }
    }

    private static void AddTriangle(SurfaceTool[] surfaces, Vector3 a, Vector3 b, Vector3 c, Vector3 inside, int? shade = null)
    {
        Vector3 outward = (a + b + c) / 3 - inside;
        if ((b - a).Cross(c - a).Dot(outward) > 0) (b, c) = (c, b);
        Vector3 normal = -(b - a).Cross(c - a).Normalized();
        var surface = surfaces[shade ?? (normal.Y > .6f ? 2 : normal.Y > .15f ? 1 : 0)];
        Vector3 up = Math.Abs(normal.Y) < .9f ? Vector3.Up : Vector3.Right;
        Vector3 u = up.Cross(normal).Normalized(), v = normal.Cross(u);
        foreach (Vector3 point in new[] { a, b, c })
        {
            surface.SetNormal(normal);
            surface.SetUV(new(point.Dot(u) * .2f, point.Dot(v) * .2f));
            surface.AddVertex(point);
        }
    }
}
