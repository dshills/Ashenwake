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
            // Small scars interrupt the dressed plinth rather than decorating the arena.
            Box(new(width * .28f, .018f, depth * .055f), new(-width * .21f, .263f, depth * .37f), "394d51");
            Box(new(width * .13f, .012f, depth * .09f), new(width * .25f, .260f, -depth * .32f), "728379");
            foreach (float side in new[] { -1f, 1f })
                Box(new(width * .17f, .009f, depth * .07f), new(side * width * .28f, .259f, side * depth * .25f), "4e6458", SurfaceKind.Earth);
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
            // The remaining gold inscription stays legible; a broken corner and damp
            // streaks age the lid around it, wholly inside the closed casket footprint.
            Box(new(width * .15f, .009f, depth * .08f), new(width * .29f, 1.154f, depth * .28f), "455c60");
            Box(new(width * .08f, .010f, depth * .16f), new(width * .30f, 1.154f, depth * .18f), "455c60");
            Box(new(width * .19f, .014f, depth * .07f), new(-width * .23f, .968f, depth * .43f), "52685e", SurfaceKind.Earth);
            Box(new(width * .07f, .21f, .009f), new(-width * .24f, .75f, depth * .457f), "455c60");
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
            int weather = Variation(center, column);
            float top = (center.Z < 0 ? 1.55f : .94f) + weather % 7 * .041f;
            for (int row = 0; row < 3; row++)
            {
                float height = (top - .18f) / 3;
                b.Box(Size(unit - .025f, height - .022f, thickness * (row == 2 ? .87f : .96f)),
                    At(along, .18f + row * height + height * .5f), (row + weather) % 3 == 0 ? "87928b" : "657775");
            }
            if (weather % 5 == 2)
            {
                // Two offset crown fragments expose the broken seam; neither extends
                // beyond its own wall segment or narrows the Core passage beside it.
                b.Box(Size(unit * .55f, .09f, thickness * .96f), At(along - unit * .17f, top + .045f), "a0aa98");
                b.Box(Size(unit * .26f, .055f, thickness * .79f), At(along + unit * .29f, top + .0275f), "87928b");
            }
            else b.Box(Size(unit * .91f, .09f, thickness), At(along, top + .045f), "a0aa98");
            if (column % 4 == 1)
                b.Box(Size(unit * .34f, .11f, thickness * .97f), At(along, .48f), "485e61");
            if (weather % 3 == 1)
            {
                Vector3 Across(float amount) => alongX ? Vector3.Back * amount : Vector3.Right * amount;
                float across = thickness * .46f;
                b.Box(Size(unit * .38f, .012f, thickness * .065f), At(along - unit * .1f, .192f) + Across(across), "52685a", surface: SurfaceKind.Earth);
                b.Box(Size(unit * .065f, .17f, .009f), At(along + unit * .18f, .32f) + Across(thickness * .483f), "4b605a", surface: SurfaceKind.Earth);
            }
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
            int weather = Variation(center, rock);
            float height = (center.Z < 0 ? 1.05f : .63f) + weather % 7 * .071f;
            // Tapered facets and staggered ledges replace identical square green caps.
            // Each rock remains within one inscribed segment of the exact bank base.
            float radius = Math.Min(segment, thickness) * .455f;
            b.Box(Size(segment * .95f, height * .30f, thickness * .94f), At(along, .24f + height * .15f), weather % 2 == 0 ? "64766a" : "738073");
            b.Cylinder(radius, radius * (.62f + weather % 3 * .08f), height * .73f,
                At(along, .24f + height * .665f), weather % 3 == 0 ? "879080" : "738073");
            float shelf = (weather % 3 - 1) * segment * .07f;
            b.Box(Size(segment * .46f, .04f, thickness * .39f), At(along + shelf, .26f + height), "879080");
            if (weather % 3 != 0)
            {
                b.Box(Size(segment * .23f, .012f, thickness * .20f), At(along + shelf, .288f + height), "4b6048", surface: SurfaceKind.Earth);
                b.Box(Size(segment * .14f, .014f, thickness * .14f), At(along + shelf - segment * .10f, .289f + height), "5b6c50", surface: SurfaceKind.Earth);
            }
            if (weather % 4 == 1)
            {
                Vector3 across = alongX ? Vector3.Back * thickness * .30f : Vector3.Right * thickness * .30f;
                b.Cylinder(radius * .23f, radius * .16f, height * .21f, At(along + segment * .25f, .24f + height * .38f) + across, "64766a");
            }
        }
    }

    private static int Variation(Vector3 center, int index)
    {
        uint value = unchecked((uint)(int)(center.X * 1000) * 0x9E3779B9u ^ (uint)(int)(center.Z * 1000) * 0x85EBCA6Bu ^ (uint)(index + 1) * 0xC2B2AE35u);
        value ^= value >> 16; value *= 0x7FEB352Du; value ^= value >> 15;
        return (int)(value & 0x7FFFFFFF);
    }
}
