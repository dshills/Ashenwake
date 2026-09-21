using Godot;

namespace Ashenwake.Client;

/// <summary>Industrial skins contained within the existing authoritative obstacle rectangles.</summary>
public static class CinderObstacleArt
{
    public static void Build(EnvironmentBuilder b, float width, float depth, Vector3 center, string style)
    {
        bool extraction = style is "cinder_extraction" or "cinder_foundry", furnace = style == "cinder_furnace", storm = style == "cinder_storm";
        b.Box(new(width, .91f, depth), center + Vector3.Up * .455f, extraction ? "41454b" : "47424a");
        b.Box(new(width * .96f, .15f, depth * .96f), center + Vector3.Up * .985f, "61575a");
        b.Box(new(width * .86f, .025f, depth * .85f), center + Vector3.Up * 1.074f, "302f35");
        if (extraction || furnace)
        {
            // Recessed top grilles and structural bands leave every navigable edge exactly where the base is.
            for (int i = 0; i < 5; i++)
                b.Box(new(width * .78f, .035f, depth * .045f), center + new Vector3(0, 1.099f, (i - 2) * depth * .16f), "727477");
            foreach (float side in new[] { -1f, 1f })
            {
                b.Box(new(width * .075f, .84f, depth * .96f), center + new Vector3(side * width * .36f, .56f, 0), furnace ? "785440" : "a07753");
                b.Box(new(width * .075f, .15f, depth * .075f), center + new Vector3(side * width * .36f, 1.08f, -depth * .36f), "727477");
                b.Box(new(width * .075f, .15f, depth * .075f), center + new Vector3(side * width * .36f, 1.08f, depth * .36f), "727477");
            }
            // These are cooled buffers. Glowing metal is reserved for active combat warnings and the Spindle.
            b.Box(new(width * .25f, .19f, depth * .035f), center + new Vector3(0, .65f, depth * .475f), "82746b");
        }
        else
        {
            // Unequal basalt courses make a low volcanic outcrop; all offsets and widths are proportional.
            for (int i = 0; i < 3; i++)
            {
                float height = .12f + i * .08f;
                b.Box(new(width * .22f, height, depth * (.53f + i * .09f)), center + new Vector3((i - 1) * width * .27f, 1.075f + height * .5f, (i - 1) * depth * .025f), i % 2 == 0 ? "47424a" : "61575a");
                if (storm) b.Box(new(width * .18f, .02f, depth * .12f), center + new Vector3((i - 1) * width * .27f, 1.085f + height, (i - 1) * depth * .025f), "82746b");
            }
        }
    }
}
