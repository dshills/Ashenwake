using Godot;

namespace Ashenwake.Client;

/// <summary>Low vertebral stonework exactly contained by the existing authoritative obstacle footprints.</summary>
public static class SpineObstacleArt
{
    public static void Build(EnvironmentBuilder b, float width, float depth, Vector3 center, string style)
    {
        bool memory = style == "spine_memory", hall = style is "spine_hall" or "spine_archive", warden = style == "spine_warden";
        string baseColor = memory ? "9a9c87" : "697581";
        string bone = memory ? "c8bd9b" : "9eabaf";
        string edge = memory ? "e5d7b5" : "c2c8bc";
        string shadow = memory ? "6d6c5d" : "475560";
        const string brass = "9c9070";
        b.Box(new(width, .78f, depth), center + Vector3.Up * .39f, baseColor);
        b.Box(new(width * .96f, .2f, depth * .96f), center + Vector3.Up * .88f, bone);
        b.Box(new(width * .88f, .1f, depth * .88f), center + Vector3.Up * 1.03f, edge);
        if (hall || warden || memory)
        {
            // Chiseled record blocks have inset top inscriptions rather than bright active seals.
            b.Box(new(width * .7f, .02f, depth * .68f), center + Vector3.Up * 1.091f, baseColor);
            for (int row = 0; row < 4; row++)
            {
                float pz = (row - 1.5f) * depth * .155f;
                for (int col = 0; col < 3; col++)
                {
                    float px = (col - 1) * width * .19f;
                    b.Box(new(width * .105f, .006f, depth * .025f), center + new Vector3(px, 1.105f, pz), shadow);
                    if ((col + row) % 2 == 0)
                        b.Box(new(width * .022f, .006f, depth * .08f), center + new Vector3(px + width * .035f, 1.105f, pz + depth * .035f), shadow);
                }
            }
            foreach (float side in new[] { -1f, 1f })
            {
                b.Box(new(width * .065f, .64f, depth * .94f), center + new Vector3(side * width * .37f, .5f, 0), bone);
                b.Box(new(width * .055f, .14f, depth * .94f), center + new Vector3(side * width * .37f, .58f, 0), brass);
            }
        }
        else
        {
            // Uneven vertebral fragments stay axis aligned: their extents never overhang the collision body.
            for (int i = 0; i < 3; i++)
            {
                float height = .18f + (i % 2) * .14f;
                b.Box(new(width * .23f, height, depth * (.64f - i * .055f)), center + new Vector3((i - 1) * width * .28f, 1.08f + height * .5f, (i - 1) * depth * .045f), i % 2 == 0 ? bone : edge);
                b.Box(new(width * .08f, .015f, depth * .3f), center + new Vector3((i - 1) * width * .28f, 1.088f + height, (i - 1) * depth * .045f), baseColor);
            }
        }
    }
}
