using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Godot;

namespace Ashenwake.Client;

/// <summary>Deterministic organic floor art. Every surface lies below Y=0 and introduces no collision or game state.</summary>
public static class VerdantGround
{
    public static void Build(Node3D parent, RoomDefinition room, string style)
    {
        var b = new EnvironmentBuilder(parent, "AuthoredGround");
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        bool village = style == "verdant_village", heart = style == "verdant_heart";
        var routes = Routes(style);
        b.Box(new(x * 2 + 9, .36f, z * 2 + 9), new(0, -.26f, 0), heart ? "3d4236" : "344338", surface: SurfaceKind.Earth);
        if (village) Boardwalk(b, room, routes);
        else RuinPaving(b, room, routes, style);
        if (heart) Tissue(b, x, z);
        else RootLines(b, x, z, routes);
        // An interrupted pale root seam marks the actual boundary without a bright rectangular arena stripe.
        for (int i = 0; i < 16; i++)
        {
            float tx = -x + (i + .5f) * x * 2 / 16;
            float tz = -z + (i + .5f) * z * 2 / 16;
            b.Box(new(x * 2 / 16 - .15f, .012f, .075f), new(tx, -.025f, -z), "606c51", surface: SurfaceKind.Earth);
            b.Box(new(x * 2 / 16 - .15f, .012f, .075f), new(tx, -.025f, z), "606c51", surface: SurfaceKind.Earth);
            b.Box(new(.075f, .012f, z * 2 / 16 - .15f), new(-x, -.025f, tz), "606c51", surface: SurfaceKind.Earth);
            b.Box(new(.075f, .012f, z * 2 / 16 - .15f), new(x, -.025f, tz), "606c51", surface: SurfaceKind.Earth);
        }
        b.Flush();
        var root = parent.GetNode<Node3D>("AuthoredGround");
        GroundField(root, x, z, routes, style);
        LeafLitter(root, room, routes, style);
    }

    private static void GroundField(Node3D root, float x, float z, Vector2[][] routes, string style)
    {
        // One connected surface replaces hundreds of isolated moss discs. Its only
        // variation is deterministic vertex color, so there are no decal or transparency layers.
        const int columns = 72, rows = 60;
        bool heart = style == "verdant_heart", village = style == "verdant_village";
        Color soil = new(heart ? "4e5140" : village ? "54503d" : "4e5140");
        Color moss = new(heart ? "3b5140" : "405941"), damp = new("344637");
        using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        for (int row = 0; row <= rows; row++)
            for (int column = 0; column <= columns; column++)
            {
                Vector2 point = new(-x + column * x * 2 / columns, -z + row * z * 2 / rows);
                float broad = .50f + .25f * MathF.Sin(point.X * .47f + MathF.Sin(point.Y * .39f)) +
                    .17f * MathF.Sin(point.Y * .63f - point.X * .21f) + .055f * MathF.Sin(point.X * 1.61f + point.Y * 1.19f);
                float shoulder = Mathf.SmoothStep(.8f, 2.9f, RouteDistance(point, routes));
                float growth = Mathf.SmoothStep(.24f, .76f, broad) * shoulder;
                Color color = soil.Lerp(moss, growth).Lerp(damp, Mathf.SmoothStep(.63f, .88f, broad) * shoulder * .36f);
                // Low-contrast canopy light belongs to the terrain, never the warning layer.
                // Broad shadow masses carry a few soft gaps; clues and arena centers stay calm.
                float canopy = Mathf.SmoothStep(-.35f, .45f, MathF.Sin(point.X * .71f + point.Y * .19f) + .45f * MathF.Sin(point.Y * .83f));
                float gaps = Mathf.SmoothStep(.13f, .72f, MathF.Sin(point.X * 3.7f + MathF.Sin(point.Y * 1.1f)) * MathF.Sin(point.Y * 3.1f - point.X * .63f));
                float quiet = QuietWeight(point, routes, style);
                color = color.Darkened(canopy * quiet * .09f).Lightened(gaps * canopy * quiet * .045f);
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
        AddColoredSurface(root, surface, "ConnectedEarthAndMoss");
    }

    private static void Boardwalk(EnvironmentBuilder b, RoomDefinition room, Vector2[][] routes)
    {
        var occupied = new List<Vector2>(); int board = 0;
        foreach (var route in routes)
            for (int segment = 1; segment < route.Length; segment++)
            {
                Vector2 span = route[segment] - route[segment - 1];
                if (span.LengthSquared() < .001f) continue;
                Vector2 along = span.Normalized(), across = new(along.Y, -along.X);
                int count = Math.Max(1, (int)MathF.Ceiling(span.Length() / .34f));
                float step = span.Length() / count, angle = Mathf.RadToDeg(Mathf.Atan2(along.X, along.Y));
                for (int i = 0; i < count; i++, board++)
                {
                    Vector2 point = route[segment - 1] + along * ((i + .5f) * step);
                    uint wear = SurfaceHash(board, segment + 17);
                    float width = 1.82f - (wear % 5) * .045f, depth = step - .025f;
                    float halfX = Math.Abs(across.X) * width * .5f + Math.Abs(along.X) * depth * .5f;
                    float halfZ = Math.Abs(across.Y) * width * .5f + Math.Abs(along.Y) * depth * .5f;
                    if (!ClearFloor(room, point.X, point.Y, halfX + .025f, halfZ + .025f) || occupied.Any(p => p.DistanceSquaredTo(point) < .075f)) continue;
                    occupied.Add(point);
                    string color = wear % 3 == 0 ? "78775d" : wear % 3 == 1 ? "6c7056" : "72745a";
                    b.Box(new(width, .035f, depth), new(point.X, -.037f, point.Y), "4d5341", new(0, angle, 0), surface: SurfaceKind.Wood);
                    b.Box(new(width - .035f, .012f, Math.Max(.03f, depth - .024f)), new(point.X, -.016f, point.Y), color, new(0, angle, 0), surface: SurfaceKind.Wood);
                    if (wear % 4 != 0) continue;
                    // Wear follows the boards' grain; tiny marks never span the whole walkway.
                    var start = point - across * (width * .34f) + along * .055f;
                    var end = point - across * (width * .08f) + along * .055f;
                    FlatLine(b, new(start.X, -.007f, start.Y), new(end.X, -.007f, end.Y), .009f, "555c45", SurfaceKind.Wood);
                }
            }
    }

    private static void RuinPaving(EnvironmentBuilder b, RoomDefinition room, Vector2[][] routes, string style)
    {
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        bool hunt = style == "verdant_hunt", shrine = style == "verdant_shrine";
        int row = 0;
        for (float pz = -z + .5f; pz < z - .3f; pz += .83f, row++)
        {
            int column = 0;
            for (float px = -x + .55f + row % 2 * .49f; px < x - .3f; px += 1.02f, column++)
            {
                Vector2 point = new(px, pz); float distance = RouteDistance(point, routes);
                uint wear = SurfaceHash(row, column);
                // Hunting ground is mostly worn earth. Ruin paths retain coherent paving
                // with a few missing shoulder stones, not a carpet of independent tiles.
                if (distance > 1.18f || hunt && (wear % 5 != 0 || distance < .55f) || distance > .8f && wear % 5 == 0) continue;
                if (hunt && VerdantCampaignLayout.HuntClues.Any(clue => point.DistanceSquaredTo(new(clue.X * .001f, clue.Z * .001f)) < 2.4f)) continue;
                if (!ClearFloor(room, px, pz, .52f, .44f)) continue;
                float width = .90f + (wear % 4) * .017f, depth = .70f + ((wear >> 4) % 3) * .02f;
                string color = shrine ? wear % 3 == 0 ? "76806b" : "697660" : wear % 3 == 0 ? "6c765f" : "606e56";
                bool chipped = distance > .65f && wear % 7 == 0;
                void Slab(float offsetX, float offsetZ, float slabWidth, float slabDepth)
                {
                    b.Box(new(slabWidth, .033f, slabDepth), new(px + offsetX, -.048f, pz + offsetZ), "414f3c", surface: SurfaceKind.Stone);
                    b.Box(new(slabWidth - .03f, .011f, slabDepth - .03f), new(px + offsetX, -.026f, pz + offsetZ), color, surface: SurfaceKind.Stone);
                }
                if (!chipped) Slab(0, 0, width, depth);
                else
                {
                    Slab(-width * .16f, 0, width * .68f - .01f, depth);
                    Slab(width * .34f, depth * .13f, width * .32f - .015f, depth * .74f);
                }
                if (distance > .75f && wear % 13 == 3)
                    FlatLine(b, new(px - width * .3f, -.017f, pz - depth * .2f), new(px + width * .12f, -.017f, pz + depth * .15f), .012f, "4f5d45", SurfaceKind.Stone);
            }
        }
    }

    public static Vector2[][] Routes(string style)
    {
        string encounter = style switch
        {
            "verdant_village" => "campaign.plague_village",
            "verdant_heart" => "campaign.rootheart",
            "verdant_hunt" => "exploration.antler_hunt",
            "verdant_shrine" => "exploration.briar_shrine",
            _ => "campaign.living_ruins"
        };
        var entrance = style is "verdant_hunt" or "verdant_shrine" ? VerdantCampaignLayout.BranchReturn : VerdantCampaignLayout.BackExit;
        var main = VerdantCampaignLayout.Route(encounter).Prepend(entrance).Select(p => new Vector2(p.X * .001f, p.Z * .001f)).ToArray();
        if (style is not ("verdant_ruins" or "verdant_village")) return [main];
        var branch = style == "verdant_ruins" ? VerdantCampaignLayout.ShrineEntrance : VerdantCampaignLayout.HuntEntrance;
        return [main, [new(6, 0), new(branch.X * .001f, branch.Z * .001f)]];
    }

    private static bool ClearFloor(RoomDefinition room, float x, float z, float halfWidth, float halfDepth)
        => Math.Abs(x) + halfWidth < room.HalfWidth * .001f && Math.Abs(z) + halfDepth < room.HalfDepth * .001f &&
            !room.Obstacles.Any(obstacle => x + halfWidth > obstacle.MinX * .001f && x - halfWidth < obstacle.MaxX * .001f &&
                z + halfDepth > obstacle.MinZ * .001f && z - halfDepth < obstacle.MaxZ * .001f);

    private static float RouteDistance(Vector2 point, Vector2[][] routes)
    {
        float distance = float.PositiveInfinity;
        foreach (var route in routes) distance = Math.Min(distance, DistanceToRoute(point, route));
        return distance;
    }

    private static float QuietWeight(Vector2 point, Vector2[][] routes, string style)
    {
        float weight = Mathf.SmoothStep(1.25f, 3.3f, RouteDistance(point, routes));
        if (style == "verdant_heart") weight *= Mathf.SmoothStep(4.4f, 7, point.Length());
        if (style == "verdant_hunt")
            foreach (var clue in VerdantCampaignLayout.HuntClues)
                weight *= Mathf.SmoothStep(1.7f, 2.8f, point.DistanceTo(new(clue.X * .001f, clue.Z * .001f)));
        if (style == "verdant_shrine")
            weight *= Mathf.SmoothStep(1.7f, 2.8f, point.DistanceTo(new(VerdantCampaignLayout.ShrineTreasure.X * .001f, VerdantCampaignLayout.ShrineTreasure.Z * .001f)));
        return weight;
    }

    private static void LeafLitter(Node3D root, RoomDefinition room, Vector2[][] routes, string style)
    {
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        int leaves = 0;
        for (int pocket = 0; pocket < 56; pocket++)
        {
            uint hash = SurfaceHash(pocket + 37, 113);
            Vector2 center = new(-x + .8f + (hash & 1023) / 1023f * (x * 2 - 1.6f),
                -z + .8f + ((hash >> 10) & 1023) / 1023f * (z * 2 - 1.6f));
            if (QuietWeight(center, routes, style) < .5f || !ClearFloor(room, center.X, center.Y, .72f, .72f)) continue;
            // Three folded, pointed leaves form a restrained pocket, with no raised reeds
            // or rectangular litter across the player's route and tracking clues.
            for (int leaf = 0; leaf < 3; leaf++)
            {
                uint shape = SurfaceHash(pocket, leaf + 53);
                float angle = (shape % 360) * Mathf.Pi / 180, width = .13f + leaf * .035f, length = .32f + leaf * .095f;
                Vector2 at = center + new Vector2(.15f * leaf, .09f * leaf).Rotated(angle + 1);
                Color color = new(leaf == 0 ? "646c4b" : leaf == 1 ? "596547" : "6d6748");
                Vector2[] outline = [new(0, -.5f), new(.48f, -.13f), new(.32f, .25f), new(0, .5f), new(-.32f, .25f), new(-.48f, -.13f)];
                Vector3 Point(Vector2 p, bool ridge = false)
                {
                    Vector2 flat = new Vector2(p.X * width, p.Y * length).Rotated(angle) + at;
                    return new(flat.X, ridge ? -.017f : -.026f, flat.Y);
                }
                void Vertex(Vector3 point, Vector2 uv, Vector3 normal, Color tint)
                { surface.SetColor(tint); surface.SetNormal(normal); surface.SetUV(uv); surface.AddVertex(point); }
                for (int edge = 0; edge < outline.Length; edge++)
                {
                    Vector2 first = outline[edge], second = outline[(edge + 1) % outline.Length];
                    Vector3 a = Point(Vector2.Zero, true), b = Point(first), c = Point(second);
                    Vector3 normal = -(b - a).Cross(c - a).Normalized();
                    Vertex(a, new(.5f, .5f), normal, color.Lightened(.035f));
                    Vertex(b, first + new Vector2(.5f, .5f), normal, color);
                    Vertex(c, second + new Vector2(.5f, .5f), normal, color);
                }
                leaves++;
            }
        }
        if (leaves > 0) { surface.Index(); AddColoredSurface(root, surface, "FallenCanopyLeaves"); }
    }

    private static void AddColoredSurface(Node3D root, SurfaceTool surface, string name)
    {
        var material = SurfaceMaterials.Create("ffffff", SurfaceKind.Earth, worldScale: true);
        material.VertexColorUseAsAlbedo = true; material.VertexColorIsSrgb = true;
        surface.GenerateTangents(); surface.SetMaterial(material);
        root.AddChild(new MeshInstance3D { Name = name, Mesh = surface.Commit() });
    }

    private static uint SurfaceHash(int row, int column)
    {
        uint hash = unchecked((uint)row * 0x9E3779B9u ^ (uint)column * 0x85EBCA6Bu ^ 0xC2B2AE35u);
        hash ^= hash >> 16; hash *= 0x7FEB352Du; hash ^= hash >> 15; hash *= 0x846CA68Bu;
        return hash ^ (hash >> 16);
    }

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

    private static void Tissue(EnvironmentBuilder b, float x, float z)
    {
        // Unequal cells suggest exposed growth rings, kept muted and wholly beneath spell warnings.
        float radius = Math.Min(x, z) * .74f;
        for (int ring = 0; ring < 3; ring++)
        {
            float r = radius * (.46f + ring * .25f);
            for (int segment = 0; segment < 24; segment++)
            {
                if ((segment + ring * 3) % 7 == 0) continue;
                float angle = segment * Mathf.Tau / 24;
                float irregular = 1 + .045f * MathF.Sin(segment * 2.3f + ring);
                b.Box(new(r * .255f, .012f, .1f + ring * .025f), new(Mathf.Sin(angle) * r * irregular, -.021f, Mathf.Cos(angle) * r * irregular), ring == 1 ? "59624b" : "505b45", new(0, segment * 15, 0), surface: SurfaceKind.Earth);
            }
        }
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Tau / 8;
            var from = new Vector3(Mathf.Sin(a) * radius * .22f, -.03f, Mathf.Cos(a) * radius * .22f);
            var to = new Vector3(Mathf.Sin(a + .12f) * radius * .82f, -.03f, Mathf.Cos(a + .12f) * radius * .82f);
            FlatLine(b, from, to, .06f, "566148", SurfaceKind.Earth);
        }
    }

    private static void RootLines(EnvironmentBuilder b, float x, float z, Vector2[][] routes)
    {
        foreach (float side in new[] { -1f, 1f })
            for (int i = 0; i < 5; i++)
            {
                float pz = -z * .75f + i * z * .34f;
                var from = new Vector3(side * x * .94f, -.022f, pz);
                var middle = new Vector3(side * x * .73f, -.022f, pz + .5f);
                var tip = new Vector3(side * x * .60f, -.022f, pz + .21f);
                if (RouteDistance(new(middle.X, middle.Z), routes) < 1.9f || RouteDistance(new(tip.X, tip.Z), routes) < 1.9f) continue;
                FlatLine(b, from, middle, .07f, "566148", SurfaceKind.Earth);
                FlatLine(b, middle, tip, .045f, "505b45", SurfaceKind.Earth);
            }
    }

    private static void FlatLine(EnvironmentBuilder b, Vector3 from, Vector3 to, float width, string color, SurfaceKind surface = SurfaceKind.Stone)
    {
        var d = to - from;
        b.Box(new(width, .008f, d.Length()), (from + to) * .5f, color, new(0, Mathf.RadToDeg(Mathf.Atan2(d.X, d.Z)), 0), surface: surface);
    }
}
