using Ashenwake.Core.Content;
using Godot;

namespace Ashenwake.Client;

/// <summary>Muted, deterministic industrial floor detail. Every generated vertex lies below Y=0.</summary>
public static class CinderGround
{
    public static void Build(Node3D parent, RoomDefinition room, string style)
    {
        var b = new EnvironmentBuilder(parent, "AuthoredGround");
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        bool extraction = style == "cinder_extraction", furnace = style == "cinder_furnace", storm = style == "cinder_storm";
        b.Box(new(x * 2 + 11, .36f, z * 2 + 11), new(0, -.26f, 0), "34343a", surface: SurfaceKind.Earth);
        // Offset paving creates volcanic streets, with enough value separation for mint destinations and warm warnings.
        int columns = Math.Clamp((int)(x * 1.15f), 6, 28), rows = Math.Clamp((int)(z * 1.13f), 6, 28);
        float width = x * 2 / columns, depth = z * 2 / rows;
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
            {
                int pattern = (row * 19 + col * 11 + row * col * 3) % 17;
                float px = -x + (col + .5f) * width, pz = -z + (row + .5f) * depth;
                string color = pattern % 4 == 0 ? "4a4447" : pattern % 3 == 0 ? "454147" : "403e44";
                b.Box(new(width - .045f, .022f, depth - .045f), new(px, -.052f, pz), color);
                if (pattern % 5 == 0)
                {
                    b.Box(new(width * .28f, .008f, depth * .035f), new(px - width * .1f, -.034f, pz), "575051", new(0, 21, 0));
                    b.Box(new(width * .12f, .008f, depth * .04f), new(px + width * .05f, -.034f, pz - depth * .035f), "575051", new(0, -31, 0));
                }
            }
        if (extraction) ExtractionFloor(b, x, z);
        else if (furnace) FurnaceFloor(b, x, z);
        else if (storm) StormFloor(b, x, z);
        else StreetFloor(b, x, z);
        // A subdued kerb seam conveys the true bounds without adding a glowing rectangle beneath hazards.
        for (int i = 0; i < 18; i++)
        {
            float px = -x + (i + .5f) * x * 2 / 18, pz = -z + (i + .5f) * z * 2 / 18;
            b.Box(new(x * 2 / 18 - .08f, .014f, .11f), new(px, -.017f, -z), "76685f");
            b.Box(new(x * 2 / 18 - .08f, .014f, .11f), new(px, -.017f, z), "76685f");
            b.Box(new(.11f, .014f, z * 2 / 18 - .08f), new(-x, -.017f, pz), "76685f");
            b.Box(new(.11f, .014f, z * 2 / 18 - .08f), new(x, -.017f, pz), "76685f");
        }
        b.Flush();
    }

    private static void StreetFloor(EnvironmentBuilder b, float x, float z)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            float px = side * x * .53f;
            b.Box(new(.21f, .012f, z * 1.85f), new(px, -.03f, 0), "32373d");
            b.Box(new(.07f, .014f, z * 1.85f), new(px + side * .18f, -.025f, 0), "675e58", surface: SurfaceKind.Metal);
            for (int i = 0; i < 11; i++)
                b.Box(new(.43f, .01f, .035f), new(px, -.018f, -z * .85f + i * z * .17f), "575659", surface: SurfaceKind.Metal);
        }
        for (int row = 0; row < 13; row++)
        {
            float pz = -z * .87f + row * z * .145f;
            b.Box(new(x * .62f, .01f, .15f), new(row % 2 == 0 ? -.09f : .09f, -.029f, pz), "514b4d");
        }
    }

    private static void ExtractionFloor(EnvironmentBuilder b, float x, float z)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            float px = side * x * .55f;
            b.Box(new(x * .43f, .012f, z * 1.82f), new(px, -.033f, 0), "363a3f", surface: SurfaceKind.Metal);
            for (int i = 0; i < 19; i++)
                b.Box(new(x * .41f, .012f, .09f), new(px, -.023f, -z * .86f + i * z * .095f), "575659", surface: SurfaceKind.Metal);
            foreach (float rail in new[] { -1f, 1f })
                b.Box(new(.085f, .012f, z * 1.86f), new(px + rail * x * .225f, -.019f, 0), "675e58", surface: SurfaceKind.Metal);
        }
        // Static maintenance hatches have square seams; no arrow or illuminated conveyor implies extra mechanics.
        foreach (float pz in new[] { -z * .55f, z * .55f })
        {
            b.Box(new(2.1f, .012f, 1.45f), new(0, -.028f, pz), "575659", surface: SurfaceKind.Metal);
            b.Box(new(1.92f, .012f, 1.27f), new(0, -.019f, pz), "403e44");
            for (int i = 0; i < 5; i++) b.Box(new(1.7f, .006f, .045f), new(0, -.009f, pz + (i - 2) * .21f), "514b4d");
        }
    }

    private static void FurnaceFloor(EnvironmentBuilder b, float x, float z)
    {
        float radius = Math.Min(x, z) * .76f;
        for (int ring = 0; ring < 3; ring++)
        {
            float r = radius * (.43f + ring * .26f);
            for (int segment = 0; segment < 32; segment++)
            {
                // Broad gaps at the cardinal lanes keep actual rotating vent warnings visually dominant.
                if (segment % 8 <= 1) continue;
                float a = segment * Mathf.Tau / 32;
                b.Box(new(r * .16f, .01f, ring == 1 ? .1f : .06f), new(Mathf.Sin(a) * r, -.024f, Mathf.Cos(a) * r), "575051", new(0, segment * 11.25f, 0));
            }
        }
        foreach (float side in new[] { -1f, 1f })
        {
            b.Box(new(.38f, .014f, z * 1.7f), new(side * x * .9f, -.026f, 0), "363a3f", surface: SurfaceKind.Metal);
            b.Box(new(x * 1.7f, .014f, .38f), new(0, -.026f, side * z * .9f), "363a3f", surface: SurfaceKind.Metal);
            for (int i = 0; i < 16; i++)
            {
                b.Box(new(.31f, .008f, .05f), new(side * x * .9f, -.013f, -z * .77f + i * z * 1.54f / 15), "675e58", surface: SurfaceKind.Metal);
                b.Box(new(.05f, .008f, .31f), new(-x * .77f + i * x * 1.54f / 15, -.013f, side * z * .9f), "675e58", surface: SurfaceKind.Metal);
            }
        }
    }

    private static void StormFloor(EnvironmentBuilder b, float x, float z)
    {
        // Directional ash drifts follow the wind, not the simulation's storm-strike locations.
        for (int i = 0; i < 33; i++)
        {
            float px = -x * .86f + (i * 7 % 17) * x * 1.72f / 16;
            float pz = -z * .8f + (i * 11 % 19) * z * 1.6f / 18;
            float length = .75f + i % 4 * .25f;
            b.Box(new(length, .008f, .13f + i % 3 * .04f), new(px, -.027f, pz), i % 3 == 0 ? "675e58" : "514b4d", new(0, -23, 0));
            if (i % 4 == 0) b.Box(new(length * .52f, .008f, .06f), new(px + .19f, -.017f, pz + .15f), "575659", new(0, -23, 0));
        }
        foreach (float side in new[] { -1f, 1f })
            for (int i = 0; i < 6; i++)
            {
                float pz = -z * .75f + i * z * .29f;
                b.Box(new(.07f, .01f, z * .19f), new(side * x * .82f, -.019f, pz), "675e58");
                b.Box(new(.34f, .012f, .31f), new(side * x * .82f, -.024f, pz + z * .08f), "363a3f");
            }
    }
}
