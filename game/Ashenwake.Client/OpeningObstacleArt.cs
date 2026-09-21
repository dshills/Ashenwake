using Godot;

namespace Ashenwake.Client;

/// <summary>Readable props whose outer edges match the existing Core obstacle rectangle.</summary>
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

        Box(new(width, .22f, depth), new(0, .11f, 0), "536258");
        // Uneven courses stay inside the collision footprint, including the broken cap.
        for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
            {
                float height = row == 2 ? column == 0 ? .13f : column == 1 ? .28f : .21f : .26f;
                Box(new(width / 3 - .025f, height, depth * (row == 2 ? .76f : .96f)),
                    new((column - 1) * width / 3, .22f + row * .28f + height * .5f, 0),
                    (row + column) % 3 == 0 ? "7c8975" : "657364");
            }
        Box(new(width * .8f, .015f, depth * .25f), new(0, .227f, depth * .33f), "4c6042", SurfaceKind.Earth);
    }
}
