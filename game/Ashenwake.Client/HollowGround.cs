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
        bool memory = style is "hollow_memory" or "hollow_vault", breach = style == "hollow_breach";
        string foundation = memory ? "343343" : "303742";
        string paving = memory ? "4c495d" : "454e5e";
        string pale = memory ? "555266" : "4e5867";
        string dark = memory ? "454355" : "3e4655";
        string seam = memory ? "686477" : "626e7d";
        string inset = memory ? "414051" : "3c4453";
        string engraving = memory ? "575468" : "535f70";
        b.Box(new(x * 2 + 11, .36f, z * 2 + 11), new(0, -.26f, 0), foundation);
        int columns = Math.Clamp((int)(x * .84f), 6, 24), rows = Math.Clamp((int)(z * .87f), 6, 24);
        float width = x * 2 / columns, depth = z * 2 / rows;
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
            {
                int pattern = (row % 3) * 3 + col % 3;
                float px = -x + (col + .5f) * width, pz = -z + (row + .5f) * depth;
                if (!ClearFloor(room, px, pz, width * .5f, depth * .5f)) continue;
                string color = pattern == 0 ? pale : pattern is 4 or 7 ? dark : paving;
                b.Box(new(width - .035f, .025f, depth - .035f), new(px, -.051f, pz), color);
                if (memory && pattern == 4)
                {
                    // The same short corner carving is displaced in every other copy, without resembling an attack line.
                    float offset = row % 2 == 0 ? width * .13f : -width * .13f;
                    b.Box(new(width * .24f, .006f, .035f), new(px + offset, -.032f, pz + depth * .22f), engraving);
                    b.Box(new(.035f, .006f, depth * .17f), new(px + offset - width * .1f, -.032f, pz + depth * .145f), engraving);
                }
            }
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
        bool memory = style is "hollow_memory" or "hollow_vault", breach = style == "hollow_breach";
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
                string color = breach ? alternate ? "626d7d" : "576474" : memory ? alternate ? "79718b" : "69667c" : alternate ? "778795" : "687889";
                b.Box(new(.85f, .012f, .76f), new(px, -.008f, pz), color);
                if (!breach && alternate)
                    b.Box(new(.15f, .0015f, .024f), new(px, -.001f, pz - .17f), memory ? "9790a5" : "96a4b0");
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

}
