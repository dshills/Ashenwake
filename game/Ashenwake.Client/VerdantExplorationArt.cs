using Godot;

namespace Ashenwake.Client;

/// <summary>A roofless refuge reclaimed by roots. Tall scenery stays beyond the arena;
/// the usable oathstone and open passages remain low enough to leave combat readable.</summary>
public static class VerdantExplorationArt
{
    private const string Stone = "718173", Pale = "aab298", Root = "746c4c", Dark = "394d43", Oath = "bad4b0", Gold = "bca576";

    public static void BuildShrine(EnvironmentBuilder b, float x, float z)
    {
        // The original shelter oath is carved into a split monolith on the far edge.
        Vector3 center = new(0, 0, -z - 2.35f);
        b.Box(new(5.6f, .28f, 2.1f), center + Vector3.Up * .14f, Dark);
        foreach (float side in new[] { -1f, 1f })
        {
            var foot = center + new Vector3(side * 1.12f, 0, 0);
            b.Box(new(1.9f, 3.8f, .74f), foot + Vector3.Up * 2.04f, Stone, new(0, 0, side * -3));
            b.Box(new(2.12f, .23f, .93f), foot + Vector3.Up * 4.05f, Pale, new(0, 0, side * -3));
            b.Beam(foot + new Vector3(side * 1.1f, .1f, .49f), foot + new Vector3(-side * .26f, 2.4f, .49f), .22f, Root);
            b.Beam(foot + new Vector3(-side * .26f, 2.4f, .49f), foot + new Vector3(side * .71f, 4.24f, .42f), .16f, Root);
            for (int inscription = 0; inscription < 6; inscription++)
                b.Box(new(.85f - inscription % 3 * .13f, .045f, .025f), foot + new Vector3(0, 1.07f + inscription * .27f, .405f), Pale);
            // Folded bedrolls and votives remember the people who were sheltered here.
            for (int bed = 0; bed < 3; bed++)
            {
                var p = new Vector3(side * (x + .82f), 0, -z * .60f + bed * 2.65f);
                b.Box(new(.98f, .14f, 1.62f), p + Vector3.Up * .07f, Dark);
                b.Box(new(.73f, .16f, .75f), p + new Vector3(0, .22f, .29f), "789181");
                b.Cylinder(.13f, .09f, .28f, p + new Vector3(0, .14f, -.64f), Pale);
            }
            // Low roots face the camera, leaving the treasure and returning player visible.
            for (int segment = 0; segment < 4; segment++)
                b.Beam(new(side * (1.1f + segment * 2.1f), .10f, z + .76f),
                    new(side * (2.9f + segment * 2.1f), .13f, z + 1.01f), .15f, Root);
        }
        b.Torus(.45f, .59f, center + new Vector3(0, 3.22f, .55f), Oath, new(90, 0, 0));
        b.Box(new(.1f, 1.01f, .05f), center + new Vector3(0, 3.25f, .59f), Gold);
    }

    public static bool SupportsMarker(string id) => id.StartsWith("verdant.", StringComparison.Ordinal);

    public static string MarkerLabel(string id, string fallback) => id switch
    {
        "verdant.shrine.enter" => "BRIARHEART SHRINE · OPTIONAL",
        "verdant.shrine.return" => "RETURN TO THE LIVING RUINS",
        "verdant.shrine.treasure" => "THE REFUGE OATHSTONE",
        "verdant.hunt.enter" => "ANTLER GROVE · HUNT",
        "verdant.hunt.return" => "RETURN TO PLAGUE VILLAGE",
        "verdant.back.ruins" => "BACK TO THE LIVING RUINS",
        "verdant.back.village" => "BACK TO PLAGUE VILLAGE",
        "verdant.forward.village" => "PLAGUE VILLAGE",
        "verdant.forward.rootheart" => "ROOTHEART SANCTUARY",
        _ => fallback
    };

    public static void BuildMarker(Node3D marker, string id)
    {
        bool treasure = id == "verdant.shrine.treasure", hunt = id.StartsWith("verdant.hunt.", StringComparison.Ordinal);
        var b = new EnvironmentBuilder(marker, treasure ? "RefugeOathstone" : "VerdantThreshold");
        if (treasure)
        {
            b.Cylinder(.56f, .45f, .12f, new(0, .06f, 0), Dark);
            b.Box(new(.76f, .42f, .65f), new(0, .33f, 0), Stone);
            b.Box(new(.9f, .09f, .76f), new(0, .585f, 0), Pale);
            foreach (float side in new[] { -1f, 1f })
                b.Beam(new(side * .43f, .09f, -.24f), new(side * .24f, .66f, .25f), .065f, Root);
            for (int line = 0; line < 3; line++)
                b.Box(new(.40f - line * .065f, .012f, .022f), new(0, .638f, -.16f + line * .10f), Gold);
            b.Torus(.13f, .2f, new(0, .80f, .02f), Oath, new(90, 0, 0), glow: true);
        }
        else
        {
            // Recessed planks and interrupted root edges signal an open crossing.
            for (int plank = 0; plank < 5; plank++)
                b.Box(new(1.25f, .018f, .26f), new(0, -.018f, (plank - 2) * .29f), hunt ? "8b8967" : "84927a");
            foreach (float side in new[] { -1f, 1f })
                b.Beam(new(side * .76f, .05f, -.74f), new(side * .68f, .09f, .69f), .075f, Root);
            b.Torus(.43f, .49f, new(0, .038f, 0), hunt ? Gold : Oath, glow: true);
            b.Box(new(.07f, .018f, .35f), new(0, .053f, .57f), hunt ? Gold : Oath, glow: true);
        }
        b.Flush();
    }
}
