using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Godot;

namespace Ashenwake.Client;

/// <summary>Walkable courts and connecting paths, all below the authoritative floor and its warnings.</summary>
public static class OpeningGround
{
    public static bool Supports(string style) => style is "greyhaven" or "road" or "monastery" or "sanctum" or "crypt";

    public static void Build(Node3D parent, RoomDefinition room, string style)
    {
        var b = new EnvironmentBuilder(parent, "AuthoredGround");
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        bool hub = style == "greyhaven", road = style == "road", crypt = style == "crypt", sanctum = style == "sanctum";
        // The inhabited floor meets a narrow weathered shoulder; distant terrain supplies
        // the silhouette instead of a single oversized rectangular slab.
        b.Box(new(x * 2 + .8f, .5f, z * 2 + .8f), new(0, -.35f, 0), crypt ? "303e45" : hub ? "303f36" : "303c37", surface: SurfaceKind.Earth);
        var route = Route(room, style);
        var cryptEntrance = OpeningCampaignLayout.CryptEntrance;
        Vector2[] cryptApproach = road ? [new(4, 0), new(cryptEntrance.X * .001f, -2.4f), new(cryptEntrance.X * .001f, cryptEntrance.Z * .001f)] : [];
        float stepX = hub ? .72f : crypt ? .91f : 1.03f, stepZ = hub ? .57f : crypt ? .64f : .76f;
        int row = 0;
        for (float pz = -z + .42f; pz < z - .25f; pz += stepZ, row++)
        {
            int column = 0;
            for (float px = -x + .5f + row % 2 * stepX * .48f; px < x - .3f; px += stepX, column++)
            {
                uint wear = SurfaceHash(row, column);
                int variation = (int)(wear % 23);
                Vector2 point = new(px, pz);
                float pathDistance = Math.Min(DistanceToRoute(point, route), DistanceToRoute(point, cryptApproach));
                int court = hub ? Court(point) : 0;
                bool courtyard = style == "monastery" && px > -x + 1.6f && px < .6f && Math.Abs(pz) < z * .65f;
                bool paved = crypt || sanctum || court != 0 || pathDistance < (hub ? 1.15f : road ? 1.25f : 1.35f) || courtyard;
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
                if (crypt) color = variation % 3 == 0 ? "66777d" : variation % 3 == 1 ? "536670" : "4d5d65";
                if (sanctum) color = variation % 3 == 0 ? "77817d" : variation % 3 == 1 ? "5c6d6c" : "536465";
                // Paving ends at actual masonry and rock banks, making openings read as passages.
                // Include the tiny rotated corners when testing the cell against the obstacle.
                if (room.Obstacles.Any(obstacle => px + width * .5f + .035f > obstacle.MinX * .001f &&
                    px - width * .5f - .035f < obstacle.MaxX * .001f && pz + depth * .5f + .035f > obstacle.MinZ * .001f &&
                    pz - depth * .5f - .035f < obstacle.MaxZ * .001f)) continue;
                // Keep the travelled middle calm. Wear accumulates along shoulders and
                // the outside of courts rather than forming a repeating diagonal grid.
                bool quiet = pathDistance < .85f || sanctum && point.Length() < 5.3f || crypt && Math.Abs(px) < 3.6f;
                width *= .94f + ((wear >> 8) & 7) * .008f;
                depth *= .94f + ((wear >> 12) & 7) * .008f;
                float angle = paved && hub ? 0 : (int)((wear >> 16) % 5) - 2;
                Paver(b, point, width, depth, angle, color, !quiet && wear % 13 == 4);
                if (!quiet && paved && wear % 19 == 7)
                    Hairline(b, point, width, depth, angle, (wear & 128) != 0);
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
        else if (style == "monastery")
        {
            // The open west court feeds the true gaps in the east cloister walls.
            CourtInlay(b, new(-x * .40f, 0), new(x * .38f, z * .60f), "909b87");
        }
        else if (sanctum) SanctuaryInlay(b);
        else if (crypt) CryptInlay(b, x, z);
        FootingWear(b, room, route, cryptApproach, style);
        Boundary(b, x, z, hub);
        b.Flush();
        GroundField(parent.GetNode<Node3D>("AuthoredGround"), x, z, route, cryptApproach, style);
    }

    public static Vector2[] Route(RoomDefinition room, string style) => Route(room.HalfWidth * .001f, style);

    public static Vector2[] Route(float halfWidth, string style)
    {
        float end = Math.Max(.5f, halfWidth - 1.4f);
        string encounter = style switch
        {
            "road" => "campaign.road",
            "monastery" => "campaign.monastery",
            "sanctum" => "campaign.bell_saint",
            "crypt" => "exploration.widow_crypt",
            _ => ""
        };
        if (encounter.Length != 0)
        {
            var entrance = style == "crypt" ? OpeningCampaignLayout.CryptReturn : OpeningCampaignLayout.BackExit;
            return OpeningCampaignLayout.Route(encounter).Prepend(entrance)
                .Select(p => new Vector2(Math.Clamp(p.X * .001f, -end, end), p.Z * .001f)).ToArray();
        }
        return [new(-end, 0), new(-end * .43f, 0), new(0, 0), new(end * .62f, 0), new(end, 0)];
    }

    private static void GroundField(Node3D root, float x, float z, Vector2[] route, Vector2[] branchRoute, string style)
    {
        // A single softly varying earth surface connects the courts and the scenery.
        // Vertex tinting avoids tiled patch props and keeps the material/texture count fixed.
        const int columns = 48, rows = 40;
        using var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        bool hub = style == "greyhaven", stone = style is "crypt" or "sanctum";
        Color soil = new(stone ? "43545a" : hub ? "4c4b3c" : "4e5144"), grass = new(stone ? "35464e" : hub ? "3e4d39" : "394d40");
        for (int row = 0; row <= rows; row++)
            for (int column = 0; column <= columns; column++)
            {
                Vector2 p = new(-x + column * x * 2 / columns, -z + row * z * 2 / rows);
                float broad = .5f + Mathf.Sin(p.X * .53f + Mathf.Sin(p.Y * .4f)) * .25f +
                    Mathf.Sin(p.Y * .71f - p.X * .21f) * .17f + Mathf.Sin(p.X * 1.73f + p.Y * 1.31f) * .06f;
                float distance = Math.Min(DistanceToRoute(p, route), DistanceToRoute(p, branchRoute));
                float shoulder = Mathf.SmoothStep(1.3f, 3.4f, distance);
                // Broad, connected damp patches read as terrain; tiny independent spots
                // would only turn the otherwise readable fighting floor into noise.
                float damp = Mathf.SmoothStep(.43f, .78f, broad) * shoulder;
                var tint = soil.Lerp(grass, Math.Clamp(broad * shoulder, 0, 1));
                surface.SetColor(tint.Lerp(new Color(stone ? "30434a" : "354534"), damp * .36f));
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
        var material = SurfaceMaterials.Create("ffffff", stone ? SurfaceKind.Stone : SurfaceKind.Earth, worldScale: true);
        material.VertexColorUseAsAlbedo = true;
        material.VertexColorIsSrgb = true;
        surface.GenerateTangents(); surface.SetMaterial(material);
        root.AddChild(new MeshInstance3D { Name = stone ? "BurialStone" : "EarthAndGrass", Mesh = surface.Commit() });
    }

    private static void Paver(EnvironmentBuilder b, Vector2 point, float width, float depth, float angle, string color, bool chipped)
    {
        string edge = new Color(color).Darkened(.16f).ToHtml(false);
        void Slab(Vector2 offset, float w, float d)
        {
            Vector2 rotated = offset.Rotated(-angle * Mathf.Pi / 180);
            var position = new Vector3(point.X + rotated.X, -.056f, point.Y + rotated.Y);
            b.Box(new(w, .043f, d), position, edge, new(0, angle, 0), surface: SurfaceKind.Stone);
            b.Box(new(Math.Max(.04f, w - .032f), .014f, Math.Max(.04f, d - .032f)),
                position + Vector3.Up * .027f, color, new(0, angle, 0), surface: SurfaceKind.Stone);
        }
        if (!chipped) { Slab(Vector2.Zero, width, depth); return; }
        // Two neighbouring fragments leave a small missing corner and a genuine recessed
        // fracture, while the earth underneath remains the same authoritative flat floor.
        Slab(new(-width * .14f, 0), width * .72f - .012f, depth);
        Slab(new(width * .36f, depth * .105f), width * .28f - .012f, depth * .79f);
    }

    private static void Hairline(EnvironmentBuilder b, Vector2 point, float width, float depth, float angle, bool mirrored)
    {
        float sign = mirrored ? -1 : 1;
        Vector3 At(float x, float z)
        {
            var p = new Vector2(x * width, z * depth).Rotated(-angle * Mathf.Pi / 180) + point;
            return new(p.X, -.018f, p.Y);
        }
        var first = At(-.38f, -.23f * sign); var middle = At(-.05f, .05f * sign); var last = At(.25f, .23f * sign);
        b.Beam(first, middle, .008f, "43524f", SurfaceKind.Stone);
        b.Beam(middle, last, .007f, "43524f", SurfaceKind.Stone);
        b.Beam(middle, At(.02f, -.19f * sign), .006f, "43524f", SurfaceKind.Stone);
    }

    private static void FootingWear(EnvironmentBuilder b, RoomDefinition room, Vector2[] route, Vector2[] branchRoute, string style)
    {
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        var sites = new List<Vector2>();
        // Author detail at real masonry feet and a few perimeter pockets, not throughout
        // the traversable arena. No cluster projects above the floor or blocks a route.
        foreach (var obstacle in room.Obstacles)
        {
            float left = obstacle.MinX * .001f, right = obstacle.MaxX * .001f;
            float back = obstacle.MinZ * .001f, front = obstacle.MaxZ * .001f;
            sites.Add(new(left - .28f, back + (front - back) * .23f));
            sites.Add(new(right + .28f, back + (front - back) * .73f));
            sites.Add(new(left + (right - left) * .31f, front + .25f));
        }
        foreach (float side in new[] { -1f, 1f })
            for (int pocket = 0; pocket < 4; pocket++)
                sites.Add(new(side * (x - .7f), -z * .72f + pocket * z * .46f));
        for (int site = 0; site < sites.Count; site++)
        {
            var center = sites[site];
            if (Math.Abs(center.X) > x - .55f || Math.Abs(center.Y) > z - .55f ||
                Math.Min(DistanceToRoute(center, route), DistanceToRoute(center, branchRoute)) < 1.8f) continue;
            uint shape = SurfaceHash(site + 83, 47);
            for (int part = 0; part < 4; part++)
            {
                float angle = ((shape >> (part * 4)) & 15) * 24f;
                float radius = .11f + part * .09f;
                var offset = Vector2.Right.Rotated(angle * Mathf.Pi / 180) * radius;
                float size = .25f - part * .035f;
                string moss = style is "crypt" or "sanctum" ? "425651" : part % 2 == 0 ? "3d503a" : "46563e";
                // Overlapping low facets give moss a soft, irregular outline rather than
                // making it look like more paving or square confetti at camera distance.
                b.Cylinder(size * .68f, size * .57f, .006f, new(center.X + offset.X, -.015f, center.Y + offset.Y),
                    moss, new(0, angle, 0), surface: SurfaceKind.Earth);
                if (part % 2 == 0)
                    b.Box(new(.075f, .014f, .10f), new(center.X - offset.X, -.011f, center.Y - offset.Y),
                        "657468", new(0, angle + 17, 0), surface: SurfaceKind.Stone);
            }
        }
    }

    private static uint SurfaceHash(int row, int column)
    {
        // Integer coordinate hash: stable cosmetic variation, independent of all Core RNG.
        uint value = unchecked((uint)row * 0x9E3779B9u ^ (uint)column * 0x85EBCA6Bu ^ 0xC2B2AE35u);
        value ^= value >> 16; value *= 0x7FEB352Du; value ^= value >> 15; value *= 0x846CA68Bu;
        return value ^ (value >> 16);
    }

    private static void SanctuaryInlay(EnvironmentBuilder b)
    {
        // Faded burial geometry stays subordinate to the Saint's live area warnings.
        for (int i = 0; i < 48; i++)
        {
            float angle = i * Mathf.Tau / 48;
            b.Box(new(.44f, .008f, .055f), new(Mathf.Sin(angle) * 3.5f, -.012f, Mathf.Cos(angle) * 3.5f), "7e8b82", new(0, i * 7.5f, 0));
        }
    }

    private static void CryptInlay(EnvironmentBuilder b, float x, float z)
    {
        // Quiet rectangular grave inscriptions leave the chamber's central fighting aisle empty.
        foreach (float side in new[] { -1f, 1f })
            for (int grave = 0; grave < 4; grave++)
            {
                Vector2 center = new(side * x * .77f, -z * .55f + grave * z * .37f);
                CourtInlay(b, center, new(.63f, 1.05f), "8b9991");
                for (int line = 0; line < 3; line++)
                    b.Box(new(.46f - line * .07f, .008f, .025f), new(center.X, -.012f, center.Y + line * .15f), "728986");
            }
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
