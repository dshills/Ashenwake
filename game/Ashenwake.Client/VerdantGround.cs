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
        bool village = style == "verdant_village", heart = style == "verdant_heart", hunt = style == "verdant_hunt", shrine = style == "verdant_shrine";
        var routes = Routes(style);
        b.Box(new(x * 2 + 9, .36f, z * 2 + 9), new(0, -.26f, 0), heart ? "3d4236" : "344338", surface: SurfaceKind.Earth);
        // A bounded grid only seeds irregular islands. It never appears as floor tiles and uses no simulation RNG.
        int columns = Math.Clamp((int)(x * 1.6f), 6, 32), rows = Math.Clamp((int)(z * 1.5f), 6, 28);
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
            {
                int pattern = (row * 31 + col * 17 + row * col * 7) % 19;
                float px = -x + (col + .48f + (pattern % 3 - 1) * .16f) * x * 2 / columns;
                float pz = -z + (row + .48f + (pattern % 5 - 2) * .09f) * z * 2 / rows;
                float pathDistance = routes.Min(route => DistanceToRoute(new(px, pz), route));
                bool path = pathDistance < (village ? 1.15f : 1.25f);
                // The floor follows the same passages as movement. Stop every paving cell
                // before actual roots and masonry, including its rotated corners.
                if (!ClearFloor(room, px, pz, .72f, .74f)) continue;
                if (path)
                {
                    if (village)
                    {
                        for (int plank = 0; plank < 3; plank++)
                            b.Box(new(1.0f, .02f, .26f), new(px, -.032f, pz + (plank - 1) * .29f), pattern % 3 == 0 ? "858167" : "6e7157", surface: SurfaceKind.Wood);
                    }
                    else b.Box(new(.83f + pattern * .011f, .019f, .92f), new(px, -.033f, pz),
                        shrine ? "7c8370" : hunt ? "73775b" : "758168", new(0, pattern % 7 - 3, 0));
                    continue;
                }
                if (pattern % 3 != 0)
                {
                    float radius = .29f + pattern * .018f;
                    b.Cylinder(radius, radius, .022f, new(px, -.057f, pz), pattern % 2 == 0 ? "41533c" : "4b5d3e", surface: SurfaceKind.Earth);
                    b.Cylinder(radius * .62f, radius * .62f, .016f, new(px + radius * .57f, -.038f, pz + radius * .36f), "526143", surface: SurfaceKind.Earth);
                }
                if (pattern % 4 == 0 || hunt && pattern % 2 == 0)
                    Litter(b, px, pz, pattern, hunt);
            }
        if (heart) Tissue(b, x, z);
        else RootLines(b, x, z);
        // An interrupted pale root seam marks the actual boundary without a bright rectangular arena stripe.
        for (int i = 0; i < 16; i++)
        {
            float tx = -x + (i + .5f) * x * 2 / 16;
            float tz = -z + (i + .5f) * z * 2 / 16;
            b.Box(new(x * 2 / 16 - .09f, .018f, .11f), new(tx, -.022f, -z), "77795c");
            b.Box(new(x * 2 / 16 - .09f, .018f, .11f), new(tx, -.022f, z), "77795c");
            b.Box(new(.11f, .018f, z * 2 / 16 - .09f), new(-x, -.022f, tz), "77795c");
            b.Box(new(.11f, .018f, z * 2 / 16 - .09f), new(x, -.022f, tz), "77795c");
        }
        b.Flush();
    }

    private static void Litter(EnvironmentBuilder b, float x, float z, int pattern, bool hunt)
    {
        float angle = pattern * 19;
        string color = hunt ? "697052" : "62724b";
        b.Box(new(.3f, .008f, .54f), new(x, -.018f, z), color, new(0, angle, 0));
        b.Box(new(.15f, .008f, .64f), new(x, -.014f, z), color, new(0, angle, 0));
        b.Box(new(.2f, .008f, .32f), new(x + .28f, -.022f, z + .24f), "5b6450", new(0, angle + 70, 0));
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
                b.Box(new(r * .255f, .012f, .1f + ring * .025f), new(Mathf.Sin(angle) * r * irregular, -.021f, Mathf.Cos(angle) * r * irregular), ring == 1 ? "686955" : "5b6450", new(0, segment * 15, 0));
            }
        }
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Tau / 8;
            var from = new Vector3(Mathf.Sin(a) * radius * .22f, -.03f, Mathf.Cos(a) * radius * .22f);
            var to = new Vector3(Mathf.Sin(a + .12f) * radius * .82f, -.03f, Mathf.Cos(a + .12f) * radius * .82f);
            FlatLine(b, from, to, .06f, "61634b");
        }
    }

    private static void RootLines(EnvironmentBuilder b, float x, float z)
    {
        foreach (float side in new[] { -1f, 1f })
            for (int i = 0; i < 5; i++)
            {
                float pz = -z * .75f + i * z * .34f;
                var from = new Vector3(side * x * .94f, -.022f, pz);
                var middle = new Vector3(side * x * .73f, -.022f, pz + .5f);
                var tip = new Vector3(side * x * .49f, -.022f, pz + .21f);
                FlatLine(b, from, middle, .1f, "61634b");
                FlatLine(b, middle, tip, .065f, "5b6450");
            }
    }

    private static void FlatLine(EnvironmentBuilder b, Vector3 from, Vector3 to, float width, string color)
    {
        var d = to - from;
        b.Box(new(width, .008f, d.Length()), (from + to) * .5f, color, new(0, Mathf.RadToDeg(Mathf.Atan2(d.X, d.Z)), 0));
    }
}
