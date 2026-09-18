using Godot;

namespace Ashenwake.Client;

/// <summary>Low, displaced record blocks remain inside the exact authoritative collision rectangles.</summary>
public static class HollowObstacleArt
{
    public static void Build(EnvironmentBuilder b, float width, float depth, Vector3 center, string style)
    {
        bool memory = style == "hollow_memory", breach = style == "hollow_breach";
        string baseColor = memory ? "565167" : "4e586a";
        string stone = memory ? "786f8e" : "737e91";
        string edge = memory ? "b0a6bf" : "a0a8bc";
        const string shadow = "333846", inlay = "5d627b";
        b.Box(new(width, .76f, depth), center + Vector3.Up * .38f, baseColor);
        b.Box(new(width * .94f, .17f, depth * .94f), center + Vector3.Up * .845f, stone);
        b.Box(new(width * .83f, .11f, depth * .83f), center + new Vector3(-width * .035f, .985f, depth * .035f), edge);
        b.Box(new(width * .64f, .025f, depth * .63f), center + new Vector3(-width * .035f, 1.053f, depth * .035f), shadow);
        for (int i = 0; i < 3; i++)
        {
            float offset = memory && i == 1 ? width * .11f : 0;
            b.Box(new(width * .38f, .014f, depth * .026f), center + new Vector3(offset, 1.073f, (i - 1) * depth * .16f), inlay);
            b.Box(new(width * .027f, .014f, depth * .105f), center + new Vector3(offset - width * .177f, 1.073f, (i - 1) * depth * .16f + depth * .04f), stone);
        }
        foreach (float side in new[] { -1f, 1f })
        {
            b.Box(new(width * .065f, .67f, depth * .96f), center + new Vector3(side * width * .385f, .49f, 0), stone);
            if (breach)
                b.Box(new(width * .088f, .12f, depth * .97f), center + new Vector3(side * width * .385f, .49f, 0), edge);
            else
                b.Box(new(width * .044f, .49f, depth * .975f), center + new Vector3(side * width * .285f, .39f, 0), inlay);
        }
    }
}
