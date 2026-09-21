using Godot;

namespace Ashenwake.Client;

/// <summary>Readable props whose outer edges match the Core obstacle rectangle, including authored passage walls.</summary>
public static class OpeningObstacleArt
{
    public static void Build(EnvironmentBuilder b, float width, float depth, Vector3 center, string style)
    {
        void Box(Vector3 size, Vector3 offset, string color, SurfaceKind? surface = null)
            => b.Box(size, center + offset, color, surface: surface);

        if (style == "greyhaven")
        {
            bool forge = center.Z < 0;
            Box(new(width, .18f, depth), new(0, .09f, 0), "555e51");
            Box(new(width * .96f, .65f, depth * .94f), new(0, .48f, 0), forge ? "4b524b" : "68513b",
                forge ? SurfaceKind.Stone : SurfaceKind.Wood);
            Box(new(width, .12f, depth), new(0, .87f, 0), forge ? "757b6c" : "927151",
                forge ? SurfaceKind.Stone : SurfaceKind.Wood);
            if (forge)
            {
                // A squat anvil reads as the workshop's tool bench, with no visual overhang.
                Box(new(width * .42f, .13f, depth * .4f), new(0, .99f, 0), "414e4d", SurfaceKind.Metal);
                Box(new(width * .2f, .19f, depth * .3f), new(0, 1.15f, 0), "5b6761", SurfaceKind.Metal);
                Box(new(width * .62f, .13f, depth * .36f), new(0, 1.3f, 0), "89938a", SurfaceKind.Metal);
            }
            else
            {
                for (int i = 0; i < 4; i++)
                    Box(new(width * .22f, .075f, depth * .7f), new(-width * .36f + i * width * .24f, .965f, 0),
                        i % 2 == 0 ? "6f6f4d" : "8b8159", SurfaceKind.Cloth);
                foreach (float side in new[] { -1f, 1f })
                    Box(new(width * .07f, .6f, depth * .96f), new(side * width * .38f, .5f, 0), "3e4b43", SurfaceKind.Metal);
            }
            return;
        }

        if (style == "road") { RockBank(b, width, depth, center); return; }
        if (style == "sanctum")
        {
            float radius = Math.Min(width, depth) * .40f;
            Box(new(width, .25f, depth), new(0, .125f, 0), "53666a");
            b.Cylinder(radius, radius * .85f, 1.35f, center + Vector3.Up * .94f, "87928b");
            b.Cylinder(radius * 1.10f, radius * 1.04f, .17f, center + Vector3.Up * 1.70f, "b4b59e");
            b.Cylinder(radius * .78f, radius * .46f, .39f, center + Vector3.Up * 1.98f, "6f8180");
            return;
        }
        if (style == "crypt")
        {
            Box(new(width, .20f, depth), new(0, .10f, 0), "354b54");
            Box(new(width * .90f, .64f, depth * .91f), new(0, .52f, 0), "697b7c");
            Box(new(width * .94f, .12f, depth * .95f), new(0, .90f, 0), "a0aaa0");
            Box(new(width * .70f, .19f, depth * .73f), new(0, 1.055f, 0), "748684");
            Box(new(width * .12f, .022f, depth * .56f), new(0, 1.161f, 0), "b0ad94");
            Box(new(width * .42f, .022f, depth * .10f), new(0, 1.161f, -depth * .12f), "b0ad94");
            // The sealed casket shape makes the entire collision footprint visibly occupied.
            foreach (float side in new[] { -1f, 1f })
                Box(new(width * .10f, .42f, depth * .95f), new(side * width * .33f, .49f, 0), "485e65");
            return;
        }

        bool alongX = width >= depth;
        float length = Math.Max(width, depth), thickness = Math.Min(width, depth);
        int columns = Math.Clamp(Mathf.CeilToInt(length / .95f), 2, 18);
        float unit = length / columns;
        Vector3 At(float along, float height) => center + (alongX ? new Vector3(along, height, 0) : new Vector3(0, height, along));
        Vector3 Size(float along, float height, float across) => alongX ? new(along, height, across) : new(across, height, along);
        b.Box(new(width, .18f, depth), center + Vector3.Up * .09f, "536258");
        // Interleaved courses describe the actual wall lengths. A lower broken crown
        // near the camera preserves an unobstructed view of the narrow passage mouths.
        for (int column = 0; column < columns; column++)
        {
            float along = -length * .5f + (column + .5f) * unit;
            float top = center.Z < 0 ? 1.55f + column % 3 * .12f : .94f + column % 3 * .12f;
            for (int row = 0; row < 3; row++)
            {
                float height = (top - .18f) / 3;
                b.Box(Size(unit - .025f, height - .022f, thickness * (row == 2 ? .87f : .96f)),
                    At(along, .18f + row * height + height * .5f), (row + column) % 3 == 0 ? "87928b" : "657775");
            }
            b.Box(Size(unit * .91f, .09f, thickness), At(along, top + .045f), "a0aa98");
            if (column % 4 == 1)
                b.Box(Size(unit * .34f, .11f, thickness * .97f), At(along, .48f), "485e61");
        }
    }

    private static void RockBank(EnvironmentBuilder b, float width, float depth, Vector3 center)
    {
        bool alongX = width >= depth;
        float length = Math.Max(width, depth), thickness = Math.Min(width, depth);
        int count = Math.Clamp(Mathf.CeilToInt(length / 1.28f), 2, 12);
        float segment = length / count;
        Vector3 At(float along, float height) => center + (alongX ? new Vector3(along, height, 0) : new Vector3(0, height, along));
        Vector3 Size(float along, float height, float across) => alongX ? new(along, height, across) : new(across, height, along);
        b.Box(new(width, .24f, depth), center + Vector3.Up * .12f, "4d6058");
        for (int rock = 0; rock < count; rock++)
        {
            float along = -length * .5f + (rock + .5f) * segment;
            float height = (center.Z < 0 ? 1.05f : .63f) + rock % 3 * .17f;
            // Tapered shelves stay inside the bank's exact rectangular base, with no
            // overhanging corners that could suggest a blocked route through a gap.
            b.Box(Size(segment - .02f, height * .62f, thickness * .96f), At(along, .24f + height * .31f), rock % 2 == 0 ? "64766a" : "738073");
            b.Box(Size(segment * .86f, height * .27f, thickness * .77f), At(along, .24f + height * .755f), "879080");
            b.Box(Size(segment * .57f, height * .11f, thickness * .53f), At(along, .24f + height * .945f), "738073");
            b.Box(Size(segment * .70f, .02f, thickness * .51f), At(along, .255f + height), "4b6048", surface: SurfaceKind.Earth);
        }
    }
}
