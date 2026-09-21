using Godot;

namespace Ashenwake.Client;

/// <summary>Organic coverings for existing obstacle rectangles; never creates a new collision footprint.</summary>
public static class VerdantObstacleArt
{
    public static void Build(EnvironmentBuilder b, float width, float depth, Vector3 center, string style)
    {
        bool village = style == "verdant_village", grove = style == "verdant_hunt", shrine = style == "verdant_shrine";
        b.Box(new(width, .90f, depth), center + Vector3.Up * .45f, grove ? "51473a" : village ? "50584b" : "647465");
        b.Box(new(width * .96f, .16f, depth * .94f), center + Vector3.Up * .98f, village ? "767b57" : "758566");
        // Low mats sit on the obstacle instead of obscuring the navigable lanes beside it.
        b.Box(new(width * .76f, .035f, depth * .74f), center + new Vector3(width * .02f, 1.08f, 0), "4c6545");
        if (shrine)
        {
            // Reused refuge stones carry names beneath the growth, all inside the solid bank.
            for (int line = 0; line < 4; line++)
                b.Box(new(width * (.45f - line % 2 * .09f), .012f, depth * .018f),
                    center + new Vector3(0, 1.11f, (line - 1.5f) * depth * .12f), "b1b58e");
        }
        if (village)
        {
            for (int i = 0; i < 3; i++)
                b.Box(new(width * .94f, .035f, depth * .07f), center + new Vector3(0, 1.105f, (i - 1) * depth * .29f), "a9a177");
            b.Box(new(width * .18f, .30f, depth * .025f), center + new Vector3(0, .66f, -depth * .48f), "c2b77c");
            return;
        }
        for (int i = -1; i <= 1; i++)
        {
            float px = i * width * .25f;
            float thickness = Math.Min(.08f, Math.Min(width, depth) * .06f);
            b.Beam(center + new Vector3(px - width * .08f, .90f, -depth * .4f), center + new Vector3(px + width * .08f, 1.13f, 0), thickness, "827b53");
            b.Beam(center + new Vector3(px + width * .08f, 1.13f, 0), center + new Vector3(px - width * .04f, .93f, depth * .4f), thickness, "827b53");
        }
        b.Box(new(width * .18f, .013f, depth * .17f), center + new Vector3(width * .26f, 1.14f, depth * .19f), "bdab71");
    }
}
