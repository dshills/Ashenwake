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
        bool village = style == "verdant_village", heart = style == "verdant_heart", hunt = style == "verdant_hunt";
        b.Box(new(x * 2 + 9, .36f, z * 2 + 9), new(0, -.26f, 0), heart ? "3d4236" : "344338", surface: SurfaceKind.Earth);
        // A bounded grid only seeds irregular islands. It never appears as floor tiles and uses no simulation RNG.
        int columns = Math.Clamp((int)(x * 1.6f), 6, 32), rows = Math.Clamp((int)(z * 1.5f), 6, 28);
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
            {
                int pattern = (row * 31 + col * 17 + row * col * 7) % 19;
                float px = -x + (col + .48f + (pattern % 3 - 1) * .16f) * x * 2 / columns;
                float pz = -z + (row + .48f + (pattern % 5 - 2) * .09f) * z * 2 / rows;
                float pathCenter = MathF.Sin(pz * .29f) * (hunt ? 1.25f : .72f);
                bool path = Math.Abs(px - pathCenter) < (village ? 1.6f : 1.24f) || !hunt && !heart && Math.Abs(pz + .3f) < 1.14f;
                if (path)
                {
                    b.Box(new(.83f + pattern * .011f, .019f, .92f), new(px, -.05f, pz), village ? "5b5d46" : "565c43", new(0, pattern - 9, 0));
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
        else if (village) VillageFloor(b, x, z);
        else if (!hunt) RuinFloor(b, x, z);
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

    private static void RuinFloor(EnvironmentBuilder b, float x, float z)
    {
        foreach (float side in new[] { -1f, 1f })
            for (int row = 0; row < 6; row++)
                for (int col = 0; col < 3; col++)
                {
                    int pattern = row * 5 + col * 3;
                    if (pattern % 4 == 0) continue;
                    float px = side * (x * .53f + (col - 1) * .82f), pz = -z * .7f + row * z * .21f;
                    b.Box(new(.74f, .026f, z * .185f), new(px, -.028f, pz), pattern % 3 == 0 ? "666f59" : "59664f", new(0, pattern % 5 - 2, 0));
                    if (row % 2 == 0) b.Box(new(.12f, .009f, .64f), new(px + .17f, -.01f, pz), "41533c", new(0, 37, 0));
                }
    }

    private static void VillageFloor(EnvironmentBuilder b, float x, float z)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            float px = side * x * .58f;
            b.Box(new(.24f, .022f, z * 1.74f), new(px - side * .85f, -.027f, -z * .04f), "30423a");
            for (int plank = 0; plank < 26; plank++)
            {
                float pz = -z * .86f + plank * z * 1.7f / 26;
                b.Box(new(1.12f, .025f, z * 1.7f / 26 - .04f), new(px, -.027f, pz), plank % 4 == 0 ? "74725a" : "686950", new(0, plank % 3 - 1, 0), surface: SurfaceKind.Wood);
                b.Box(new(.025f, .006f, .07f), new(px + side * .44f, -.01f, pz), "434d3d");
            }
        }
        // Two little transverse stepping bridges over the channels are painted at ground height.
        foreach (float pz in new[] { -z * .45f, z * .33f })
            for (int i = 0; i < 7; i++)
                b.Box(new(x * 1.3f, .018f, .16f), new(0, -.021f, pz + (i - 3) * .185f), i % 2 == 0 ? "686950" : "74725a", surface: SurfaceKind.Wood);
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
