using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Godot;

namespace Ashenwake.Client;

/// <summary>Quiet stone and bone paving; all vertices stay below the combat plane and use no simulation state.</summary>
public static class SpineGround
{
    public static void Build(Node3D parent, RoomDefinition room, string style)
    {
        var b = new EnvironmentBuilder(parent, "AuthoredGround");
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        bool memory = style == "spine_memory", hall = style is "spine_hall" or "spine_archive", warden = style == "spine_warden";
        string baseColor = memory ? "424d46" : "45525b";
        string line = memory ? "899281" : "7d8a8f";
        string inset = memory ? "48574f" : "4b5b64";
        string engraving = memory ? "6c7769" : "697b82";
        b.Box(new(x * 2 + 11, .36f, z * 2 + 11), new(0, -.26f, 0), baseColor);
        if (hall) Hall(b, x, z, inset, engraving);
        else if (warden) Court(b, x, z, inset, engraving);
        else if (memory) Memory(b, x, z, inset, engraving);
        else Causeway(b, x, z, inset, engraving);
        RoutePaving(b, room, style);
        // A thin, interrupted stone seam marks the actual boundary without competing with oath and fault warnings.
        for (int i = 0; i < 20; i++)
        {
            float px = -x + (i + .5f) * x * 2 / 20, pz = -z + (i + .5f) * z * 2 / 20;
            b.Box(new(x * 2 / 20 - .07f, .01f, .105f), new(px, -.014f, -z), line);
            b.Box(new(x * 2 / 20 - .07f, .01f, .105f), new(px, -.014f, z), line);
            b.Box(new(.105f, .01f, z * 2 / 20 - .07f), new(-x, -.014f, pz), line);
            b.Box(new(.105f, .01f, z * 2 / 20 - .07f), new(x, -.014f, pz), line);
        }
        b.Flush();
        CourtMasonry(parent.GetNode<Node3D>("AuthoredGround"), room, style);
    }

    public static Vector2[][] Routes(string style)
    {
        string encounter = style switch
        {
            "spine_hall" => "campaign.contract_hall",
            "spine_warden" => "campaign.covenant_warden",
            "spine_memory" => "exploration.first_oath",
            "spine_archive" => "exploration.oathkeeper_archive",
            _ => "campaign.bone_causeway"
        };
        var entrance = style is "spine_memory" or "spine_archive" ? SpineCampaignLayout.BranchReturn : SpineCampaignLayout.BackExit;
        var main = SpineCampaignLayout.Route(encounter).Prepend(entrance).Select(p => new Vector2(p.X * .001f, p.Z * .001f)).ToArray();
        if (style is not ("spine_causeway" or "spine_hall")) return [main];
        var branch = style == "spine_causeway" ? SpineCampaignLayout.ArchiveEntrance : SpineCampaignLayout.MemoryEntrance;
        return [main, [new(6, 0), new(branch.X * .001f, branch.Z * .001f)]];
    }

    private static void RoutePaving(EnvironmentBuilder b, RoomDefinition room, string style)
    {
        var routes = Routes(style);
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        bool memory = style == "spine_memory", archive = style == "spine_archive";
        int row = 0;
        for (float pz = -z + .45f; pz < z - .3f; pz += .84f, row++)
        {
            int column = 0;
            for (float px = -x + .5f + row % 2 * .12f; px < x - .3f; px += .94f, column++)
            {
                var point = new Vector2(px, pz);
                if (!routes.Any(route => DistanceToRoute(point, route) < 1.18f) || !ClearFloor(room, px, pz, .45f, .4f)) continue;
                uint wear = SurfaceHash(row, column);
                string color = memory ? wear % 5 == 0 ? "858776" : "747c70" : wear % 5 == 0 ? "7c898c" : "6e7d84";
                RouteSlab(b, point, color, memory, !memory && RouteDistance(point, routes) > .70f && wear % 9 == 0);
                // Small, subdued witness marks belong to the paving; glowing lines and
                // complete numbered bands remain exclusive to announced fault attacks.
                if (RouteDistance(point, routes) > .45f && wear % (archive ? 4 : 11) == 0)
                {
                    b.Box(new(.21f, .001f, .018f), new(px, -.002f, pz - .17f), memory ? "626c5c" : "52636c");
                    b.Box(new(.018f, .001f, .13f), new(px + .06f, -.002f, pz - .11f), memory ? "626c5c" : "52636c");
                }
            }
        }
    }

    private static void RouteSlab(EnvironmentBuilder b, Vector2 point, string color, bool memory, bool chipped)
    {
        void Slab(Vector2 offset, float width, float depth)
        {
            Vector2 at = point + offset;
            b.Box(new(width, .025f, depth), new(at.X, -.025f, at.Y), memory ? "566153" : "42545f", surface: SurfaceKind.Stone);
            b.Box(new(width - .034f, .010f, depth - .034f), new(at.X, -.009f, at.Y), color, surface: SurfaceKind.Stone);
        }
        if (!chipped) { Slab(Vector2.Zero, .85f, .76f); return; }
        // A missing outer corner reveals the mortar; the flat walkable Core plane is unchanged.
        Slab(new(-.125f, 0), .60f, .76f);
        Slab(new(.303f, .08f), .232f, .60f);
    }

    private static void CourtMasonry(Node3D root, RoomDefinition room, string style)
    {
        bool memory = style == "spine_memory";
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        int columns = Math.Clamp((int)(x * 1.03f), 6, 26), rows = Math.Clamp((int)(z * 1.04f), 6, 26);
        float width = x * 2 / columns, depth = z * 2 / rows;
        var routes = Routes(style);
        Color paving = new(memory ? "666e61" : "596872"), light = new(memory ? "747b6b" : "63717a"), dark = new(memory ? "606a5d" : "53616b");
        Color boneDust = new("a3a494");
        using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        int slabs = 0;
        void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Color color, bool dust)
        {
            if ((b - a).Cross(c - a).Dot(outward) > 0) (b, c) = (c, b);
            Vector3 normal = -(b - a).Cross(c - a).Normalized();
            Vector3 guide = Math.Abs(normal.Y) < .9f ? Vector3.Up : Vector3.Right;
            Vector3 u = guide.Cross(normal).Normalized(), v = normal.Cross(u);
            foreach (var point in new[] { a, b, c })
            {
                var tint = dust ? color.Lerp(boneDust, SettledDust(new(point.X, point.Z), room, routes, style) * .15f) : color;
                // Per-face projection also gives the vertical mortar sides valid tangents.
                surface.SetColor(tint); surface.SetNormal(normal); surface.SetUV(new(point.Dot(u) * .45f, point.Dot(v) * .45f)); surface.AddVertex(point);
            }
        }
        for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                Vector2 center = new(-x + (column + .5f) * width, -z + (row + .5f) * depth);
                float slabWidth = width - .052f, slabDepth = depth - .052f;
                if (!ClearFloor(room, center.X, center.Y, slabWidth * .5f + .012f, slabDepth * .5f + .012f)) continue;
                uint wear = SurfaceHash(row, column);
                Color color = wear % 5 == 0 ? light : wear % 3 == 0 ? dark : paving;
                bool shoulder = RouteDistance(center, routes) > 1.7f && (style != "spine_warden" || center.Length() > 5.4f);
                bool chipped = !memory && shoulder && wear % 4 == 0;
                float halfX = slabWidth * .5f, halfZ = slabDepth * .5f;
                float bevel = memory ? .032f : .042f + ((wear >> 8) & 3) * .008f;
                float corner = Math.Min(halfX, halfZ) * (chipped ? .28f : memory ? .055f : .085f);
                // The court retains coherent broad slabs. Cut corners and chamfered edges
                // supply depth, while only a few outer corners are lost to weathering.
                Vector2[] outline = [new(-halfX + corner, -halfZ), new(halfX - corner, -halfZ),
                    new(halfX, -halfZ + corner), new(halfX, halfZ - corner), new(halfX - corner, halfZ),
                    new(-halfX + corner, halfZ), new(-halfX, halfZ - corner), new(-halfX, -halfZ + corner)];
                if (chipped) outline[(int)((wear >> 12) % 8)] *= .83f;
                Vector3 Upper(Vector2 p) => new(center.X + p.X * (1 - bevel / halfX), -.037f, center.Y + p.Y * (1 - bevel / halfZ));
                Vector3 Rim(Vector2 p) => new(center.X + p.X, -.049f, center.Y + p.Y);
                Vector3 Lower(Vector2 p) => new(center.X + p.X, -.077f, center.Y + p.Y);
                var middle = new Vector3(center.X, -.037f, center.Y);
                for (int edge = 0; edge < outline.Length; edge++)
                {
                    Vector2 a = outline[edge], b = outline[(edge + 1) % outline.Length];
                    Vector3 outward = new((a.X + b.X) * .5f, 0, (a.Y + b.Y) * .5f);
                    Triangle(middle, Upper(a), Upper(b), Vector3.Up, color, !memory);
                    Triangle(Upper(a), Rim(a), Rim(b), Vector3.Up + outward, color.Darkened(memory ? .035f : .07f), !memory);
                    Triangle(Upper(a), Rim(b), Upper(b), Vector3.Up + outward, color.Darkened(memory ? .035f : .07f), !memory);
                    Triangle(Rim(a), Lower(a), Lower(b), outward, color.Darkened(.23f), false);
                    Triangle(Rim(a), Lower(b), Rim(b), outward, color.Darkened(.23f), false);
                }
                slabs++;
            }
        if (slabs == 0) return;
        surface.Index(); surface.GenerateTangents();
        var material = SurfaceMaterials.Create("ffffff", SurfaceKind.Stone, worldScale: true);
        material.VertexColorUseAsAlbedo = true; material.VertexColorIsSrgb = true;
        surface.SetMaterial(material);
        root.AddChild(new MeshInstance3D { Name = memory ? "IntactIvoryCourt" : "WeatheredMountainCourt", Mesh = surface.Commit() });
    }

    private static float SettledDust(Vector2 point, RoomDefinition room, Vector2[][] routes, string style)
    {
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        float distance = Math.Min(x - Math.Abs(point.X), z - Math.Abs(point.Y));
        foreach (var obstacle in room.Obstacles)
        {
            float dx = Math.Max(obstacle.MinX * .001f - point.X, Math.Max(0, point.X - obstacle.MaxX * .001f));
            float dz = Math.Max(obstacle.MinZ * .001f - point.Y, Math.Max(0, point.Y - obstacle.MaxZ * .001f));
            distance = Math.Min(distance, MathF.Sqrt(dx * dx + dz * dz));
        }
        float quiet = Mathf.SmoothStep(.95f, 2.2f, RouteDistance(point, routes));
        if (style == "spine_warden") quiet *= Mathf.SmoothStep(4.8f, 6.8f, point.Length());
        if (style == "spine_archive")
            quiet *= Mathf.SmoothStep(1.8f, 3, point.DistanceTo(new(SpineCampaignLayout.ArchiveTreasure.X * .001f, SpineCampaignLayout.ArchiveTreasure.Z * .001f)));
        return (1 - Mathf.SmoothStep(.10f, 1.55f, distance)) * quiet;
    }

    private static float RouteDistance(Vector2 point, Vector2[][] routes)
    {
        float distance = float.PositiveInfinity;
        foreach (var route in routes) distance = Math.Min(distance, DistanceToRoute(point, route));
        return distance;
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

    private static void Causeway(EnvironmentBuilder b, float x, float z, string inset, string engraving)
    {
        // Broad longitudinal inlays read as a causeway, never as a transverse fault sequence.
        foreach (float side in new[] { -1f, 1f })
        {
            b.Box(new(x * .18f, .013f, z * 1.88f), new(side * x * .73f, -.029f, 0), inset);
            b.Box(new(.055f, .01f, z * 1.84f), new(side * x * .62f, -.017f, 0), engraving);
            for (int i = 0; i < 11; i++)
            {
                float pz = -z * .85f + i * z * .17f;
                b.Box(new(x * .13f, .008f, .1f), new(side * x * .73f, -.018f, pz), engraving);
            }
        }
        for (int i = 0; i < 17; i++)
        {
            float pz = -z * .88f + i * z * 1.76f / 16;
            b.Box(new(.09f, .008f, .29f), new(0, -.022f, pz), engraving);
        }
    }

    private static void Hall(EnvironmentBuilder b, float x, float z, string inset, string engraving)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            float px = side * x * .75f;
            b.Box(new(x * .26f, .012f, z * 1.86f), new(px, -.029f, 0), inset);
            for (int row = 0; row < 9; row++)
            {
                float pz = -z * .8f + row * z * .2f;
                for (int col = 0; col < 3; col++)
                {
                    float dx = (col - 1) * x * .057f;
                    b.Box(new(x * .041f, .008f, .04f), new(px + dx, -.019f, pz), engraving);
                    b.Box(new(.035f, .008f, .17f), new(px + dx, -.019f, pz + .085f), engraving);
                }
            }
        }
        // The central aisle has no closed medallions or glowing regions that could be mistaken for oath zones.
        for (int i = 0; i < 12; i++)
        {
            float pz = -z * .88f + i * z * .16f;
            b.Box(new(.06f, .008f, z * .12f), new(-x * .38f, -.022f, pz), engraving);
            b.Box(new(.06f, .008f, z * .12f), new(x * .38f, -.022f, pz), engraving);
        }
    }

    private static void Court(EnvironmentBuilder b, float x, float z, string inset, string engraving)
    {
        // Only the outer border is carved. The Warden's marks and announced faults own the arena center.
        foreach (float side in new[] { -1f, 1f })
        {
            b.Box(new(x * 1.84f, .012f, .32f), new(0, -.029f, side * z * .9f), inset);
            b.Box(new(.32f, .012f, z * 1.84f), new(side * x * .9f, -.029f, 0), inset);
            for (int i = 0; i < 15; i++)
            {
                float px = -x * .79f + i * x * 1.58f / 14, pz = -z * .78f + i * z * 1.56f / 14;
                b.Box(new(.045f, .008f, .17f), new(px, -.018f, side * z * .9f), engraving);
                b.Box(new(.17f, .008f, .045f), new(side * x * .9f, -.018f, pz), engraving);
            }
        }
    }

    private static void Memory(EnvironmentBuilder b, float x, float z, string inset, string engraving)
    {
        // Undamaged ceremonial paving uses warm, subdued values so the reversed fault order remains legible.
        foreach (float side in new[] { -1f, 1f })
        {
            float px = side * x * .69f;
            b.Box(new(x * .2f, .012f, z * 1.86f), new(px, -.029f, 0), inset);
            for (int i = 0; i < 12; i++)
            {
                float pz = -z * .84f + i * z * 1.68f / 11;
                b.Box(new(x * .13f, .008f, .06f), new(px, -.018f, pz), engraving);
                b.Box(new(.055f, .008f, .23f), new(px, -.018f, pz), engraving);
            }
            b.Box(new(.05f, .01f, z * 1.86f), new(side * x * .55f, -.018f, 0), engraving);
            b.Box(new(.05f, .01f, z * 1.86f), new(side * x * .83f, -.018f, 0), engraving);
        }
    }
}
