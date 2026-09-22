using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Godot;

namespace Ashenwake.Client;

/// <summary>Quiet, repeated paving lies strictly below the simulation plane; active warnings own the floor.</summary>
public static class HollowGround
{
    public static void Build(Node3D parent, RoomDefinition room, string style)
    {
        var b = new EnvironmentBuilder(parent, "AuthoredGround");
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        bool memory = style is "hollow_memory" or "hollow_vault", breach = style == "hollow_breach", vault = style == "hollow_vault";
        string foundation = vault ? "303240" : memory ? "343343" : "303742";
        string seam = memory ? "686477" : "626e7d";
        string inset = memory ? "414051" : "3c4453";
        string engraving = memory ? "575468" : "535f70";
        b.Box(new(x * 2 + 11, .36f, z * 2 + 11), new(0, -.26f, 0), foundation);
        RepeatedInlays(b, room, style);
        foreach (float side in new[] { -1f, 1f })
        {
            // The center remains plain in the final chamber so seal channels and returning echoes are unmistakable.
            float band = breach ? .91f : .82f;
            b.Box(new(x * .11f, .01f, z * 1.86f), new(side * x * band, -.029f, 0), inset);
            b.Box(new(x * 1.86f, .01f, z * .11f), new(0, -.029f, side * z * band), inset);
            for (int i = 0; i < 12; i++)
            {
                float px = -x * .78f + i * x * 1.56f / 11, pz = -z * .78f + i * z * 1.56f / 11;
                b.Box(new(.08f, .008f, .23f), new(px, -.018f, side * z * band), engraving);
                b.Box(new(.23f, .008f, .08f), new(side * x * band, -.018f, pz), engraving);
                if (!breach)
                {
                    b.Box(new(.19f, .008f, .04f), new(px + .06f, -.018f, side * z * band + .095f), engraving);
                    b.Box(new(.04f, .008f, .19f), new(side * x * band + .095f, -.018f, pz + .06f), engraving);
                }
            }
            for (int i = 0; i < 20; i++)
            {
                float px = -x + (i + .5f) * x * 2 / 20, pz = -z + (i + .5f) * z * 2 / 20;
                b.Box(new(x * 2 / 20 - .065f, .01f, .085f), new(px, -.014f, side * z), seam);
                b.Box(new(.085f, .01f, z * 2 / 20 - .065f), new(side * x, -.014f, pz), seam);
            }
        }
        RoutePaving(b, room, style);
        b.Flush();
        SlateFloor(parent.GetNode<Node3D>("AuthoredGround"), room, style);
    }
    public static Vector2[][] Routes(string style)
    {
        string encounter = style switch
        {
            "hollow_memory" => "campaign.identity_memory",
            "hollow_breach" => "campaign.breach_heart",
            "hollow_vault" => "exploration.unremembered_vault",
            _ => "campaign.repeating_rooms"
        };
        var entrance = style == "hollow_vault" ? HollowCampaignLayout.BranchReturn : HollowCampaignLayout.BackExit;
        var main = HollowCampaignLayout.Route(encounter).Prepend(entrance).Select(p => new Vector2(p.X * .001f, p.Z * .001f)).ToArray();
        return style == "hollow_rooms" ? [main, [new(6, 0), new(HollowCampaignLayout.VaultEntrance.X * .001f, HollowCampaignLayout.VaultEntrance.Z * .001f)]] : [main];
    }

    private static void RoutePaving(EnvironmentBuilder b, RoomDefinition room, string style)
    {
        var routes = Routes(style);
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        bool memory = style is "hollow_memory" or "hollow_vault", breach = style == "hollow_breach", vault = style == "hollow_vault";
        int row = 0;
        for (float pz = -z + .45f; pz < z - .3f; pz += .84f, row++)
        {
            int column = 0;
            for (float px = -x + .5f + row % 2 * .12f; px < x - .3f; px += .94f, column++)
            {
                var point = new Vector2(px, pz);
                if (!routes.Any(route => DistanceToRoute(point, route) < 1.18f) || !ClearFloor(room, px, pz, .45f, .4f)) continue;
                // Navigation stays muted in the finale: seals, channels and echoes
                // own the strong shapes and glowing colors throughout the fight.
                bool alternate = (row + column) % 4 == 0;
                string color = breach ? alternate ? "5b6575" : "556171" : vault ? alternate ? "74788d" : "676d81" :
                    memory ? alternate ? "79718b" : "69667c" : alternate ? "71818f" : "637384";
                b.Box(new(.85f, .027f, .76f), new(px, -.027f, pz), memory ? "3d3d4f" : "354252", surface: SurfaceKind.Stone);
                b.Box(new(.812f, .010f, .722f), new(px, -.009f, pz), color, surface: SurfaceKind.Stone);
                if (!breach && alternate && RouteDistance(point, routes) > .6f)
                    b.Box(new(.15f, .001f, .016f), new(px, -.002f, pz - .17f), memory ? "515166" : "4b5d70");
            }
        }
    }

    private static void RepeatedInlays(EnvironmentBuilder b, RoomDefinition room, string style)
    {
        if (style == "hollow_breach") return;
        bool memory = style == "hollow_memory", vault = style == "hollow_vault";
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        int columns = Math.Clamp((int)(x * .84f), 6, 24), rows = Math.Clamp((int)(z * .87f), 6, 24);
        float width = x * 2 / columns, depth = z * 2 / rows;
        var routes = Routes(style);
        for (int row = 1; row < rows; row += 3)
            for (int column = 1; column < columns; column += 3)
            {
                Vector2 point = new(-x + (column + .5f) * width, -z + (row + .5f) * depth);
                if (!ClearFloor(room, point.X, point.Y, width * .5f, depth * .5f) || RouteDistance(point, routes) < 1.55f || NearVaultTestament(point, style)) continue;
                // Repeated corners drift by a hand's breadth, as if successive copies
                // remembered different positions. No closed seal, arrow or luminous line.
                float offset = ((row / 3 + column / 3) % 2 == 0 ? 1 : -1) * width * .10f;
                string shadow = memory ? "3c394b" : vault ? "363b4d" : "354150";
                string lip = memory ? "646071" : vault ? "596173" : "586a7b";
                float px = point.X + offset, pz = point.Y + depth * .19f;
                b.Box(new(width * .22f, .002f, .027f), new(px, -.029f, pz), shadow);
                b.Box(new(.027f, .002f, depth * .15f), new(px - width * .097f, -.029f, pz - depth * .067f), shadow);
                b.Box(new(width * .18f, .0015f, .010f), new(px + width * .015f, -.028f, pz + .023f), lip);
                if (vault)
                    b.Box(new(.014f, .0015f, depth * .10f), new(px - width * .065f, -.028f, pz - depth * .065f), lip);
            }
    }

    private static void SlateFloor(Node3D root, RoomDefinition room, string style)
    {
        bool memory = style == "hollow_memory", vault = style == "hollow_vault", breach = style == "hollow_breach";
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        int columns = Math.Clamp((int)(x * .84f), 6, 24), rows = Math.Clamp((int)(z * .87f), 6, 24);
        float width = x * 2 / columns, depth = z * 2 / rows;
        var routes = Routes(style);
        Color paving = new(memory ? "4f4b60" : vault ? "474a5e" : "454e5e");
        Color pale = new(memory ? "5a566a" : vault ? "52576c" : "4e5867");
        Color dark = new(memory ? "464357" : vault ? "404556" : "3e4655");
        using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        int slabs = 0;
        void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Color color)
        {
            if ((b - a).Cross(c - a).Dot(outward) > 0) (b, c) = (c, b);
            Vector3 normal = -(b - a).Cross(c - a).Normalized();
            Vector3 guide = Math.Abs(normal.Y) < .9f ? Vector3.Up : Vector3.Right;
            Vector3 u = guide.Cross(normal).Normalized(), v = normal.Cross(u);
            foreach (var point in new[] { a, b, c })
            {
                surface.SetColor(color); surface.SetNormal(normal);
                surface.SetUV(new(point.Dot(u) * .45f, point.Dot(v) * .45f)); surface.AddVertex(point);
            }
        }
        for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                Vector2 center = new(-x + (column + .5f) * width, -z + (row + .5f) * depth);
                float slabWidth = width - .049f, slabDepth = depth - .049f;
                if (!ClearFloor(room, center.X, center.Y, slabWidth * .5f + .012f, slabDepth * .5f + .012f)) continue;
                int repeat = row % 3 * 3 + column % 3;
                uint wear = SurfaceHash(row, column);
                bool quiet = RouteDistance(center, routes) < 1.65f || breach && center.Length() < 6 || NearVaultTestament(center, style);
                Color color = breach && quiet ? paving : repeat == 0 ? pale : repeat is 4 or 7 ? dark : paving;
                bool chipped = !memory && !quiet && wear % 5 == 0;
                float halfX = slabWidth * .5f, halfZ = slabDepth * .5f;
                float bevel = memory ? .032f : quiet ? .038f : .046f + ((wear >> 8) & 3) * .007f;
                float corner = Math.Min(halfX, halfZ) * (chipped ? .23f : .055f);
                Vector2[] outline = [new(-halfX + corner, -halfZ), new(halfX - corner, -halfZ),
                    new(halfX, -halfZ + corner), new(halfX, halfZ - corner), new(halfX - corner, halfZ),
                    new(-halfX + corner, halfZ), new(-halfX, halfZ - corner), new(-halfX, -halfZ + corner)];
                if (chipped) outline[(int)((wear >> 12) % 8)] *= .86f;
                Vector3 Top(Vector2 point) => new(center.X + point.X * (1 - bevel / halfX), -.038f, center.Y + point.Y * (1 - bevel / halfZ));
                Vector3 Rim(Vector2 point) => new(center.X + point.X, -.050f, center.Y + point.Y);
                Vector3 Bottom(Vector2 point) => new(center.X + point.X, -.077f, center.Y + point.Y);
                var middle = new Vector3(center.X, -.038f, center.Y);
                for (int edge = 0; edge < outline.Length; edge++)
                {
                    Vector2 a = outline[edge], b = outline[(edge + 1) % outline.Length];
                    Vector3 outward = new((a.X + b.X) * .5f, 0, (a.Y + b.Y) * .5f);
                    Triangle(middle, Top(a), Top(b), Vector3.Up, color);
                    Triangle(Top(a), Rim(a), Rim(b), Vector3.Up + outward, color.Darkened(quiet ? .035f : .075f));
                    Triangle(Top(a), Rim(b), Top(b), Vector3.Up + outward, color.Darkened(quiet ? .035f : .075f));
                    Triangle(Rim(a), Bottom(a), Bottom(b), outward, color.Darkened(.24f));
                    Triangle(Rim(a), Bottom(b), Rim(b), outward, color.Darkened(.24f));
                }
                slabs++;
            }
        if (slabs == 0) return;
        surface.Index(); surface.GenerateTangents();
        var material = SurfaceMaterials.Create("ffffff", SurfaceKind.Stone, worldScale: true);
        material.VertexColorUseAsAlbedo = true; material.VertexColorIsSrgb = true;
        surface.SetMaterial(material);
        root.AddChild(new MeshInstance3D { Name = memory ? "RememberedSlate" : vault ? "UnrememberedSlate" : breach ? "BreachCourtSlate" : "RepeatingSlate", Mesh = surface.Commit() });
    }

    private static bool NearVaultTestament(Vector2 point, string style) => style == "hollow_vault" &&
        point.DistanceSquaredTo(new(HollowCampaignLayout.VaultTreasure.X * .001f, HollowCampaignLayout.VaultTreasure.Z * .001f)) < 5.3f;

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

}
