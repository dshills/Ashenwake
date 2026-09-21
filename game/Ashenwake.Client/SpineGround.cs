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
        string paving = memory ? "596158" : "596872";
        string lightPaving = memory ? "666b60" : "63717a";
        string darkPaving = memory ? "515b54" : "53616b";
        string line = memory ? "899281" : "7d8a8f";
        string inset = memory ? "48574f" : "4b5b64";
        string engraving = memory ? "6c7769" : "697b82";
        b.Box(new(x * 2 + 11, .36f, z * 2 + 11), new(0, -.26f, 0), baseColor);
        int columns = Math.Clamp((int)(x * 1.03f), 6, 26), rows = Math.Clamp((int)(z * 1.04f), 6, 26);
        float width = x * 2 / columns, depth = z * 2 / rows;
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
            {
                int pattern = (row * 11 + col * 17 + row * col * 3) % 19;
                float px = -x + (col + .5f) * width, pz = -z + (row + .5f) * depth;
                if (!ClearFloor(room, px, pz, width * .5f, depth * .5f)) continue;
                string color = pattern % 5 == 0 ? lightPaving : pattern % 3 == 0 ? darkPaving : paving;
                b.Box(new(width - .037f, .025f, depth - .037f), new(px, -.051f, pz), color);
                if (!memory && pattern % 6 == 0)
                {
                    // Small surface wear is local and quiet; full-width faults remain exclusive to active mechanics.
                    b.Box(new(width * .27f, .008f, .029f), new(px + width * .13f, -.029f, pz - depth * .17f), inset, new(0, 23, 0));
                    b.Box(new(.035f, .008f, depth * .15f), new(px + width * .03f, -.029f, pz - depth * .12f), inset, new(0, -16, 0));
                }
            }
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
                int pattern = (row * 13 + column * 7) % 5;
                string color = memory ? pattern == 0 ? "858776" : "747c70" : pattern == 0 ? "829092" : "71838b";
                b.Box(new(.85f, .012f, .76f), new(px, -.008f, pz), color);
                // Small, subdued witness marks belong to the paving; glowing lines and
                // complete numbered bands remain exclusive to announced fault attacks.
                if (archive || pattern == 0)
                {
                    b.Box(new(.21f, .0015f, .025f), new(px, -.001f, pz - .17f), memory ? "9b9982" : "a0a694");
                    b.Box(new(.025f, .0015f, .13f), new(px + .06f, -.001f, pz - .11f), memory ? "9b9982" : "a0a694");
                }
            }
        }
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
