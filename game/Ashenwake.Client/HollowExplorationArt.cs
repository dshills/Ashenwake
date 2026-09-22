using Godot;

namespace Ashenwake.Client;

/// <summary>A vault for erased witnesses. Sealed shadow facades stay outside the room;
/// the small, paired anchor glyph belongs exclusively to registered passage markers.</summary>
public static class HollowExplorationArt
{
    private const string Void = "242833", Slate = "4e586a", Stone = "737e91", Edge = "a0a8bc", Memory = "786f8e", Parchment = "b0a6bf", Inlay = "5d627b", Anchor = "a4d5cf";

    public static void BuildVault(EnvironmentBuilder b, float x, float z)
    {
        // A repeated memorial has gaps where names have been erased. These sealed
        // niches face the room from beyond its boundary, with no path or anchor glyph.
        for (int bay = -3; bay <= 3; bay++)
        {
            var p = new Vector3(bay * 2.48f, 0, -z - 2.14f - Math.Abs(bay % 2) * .24f);
            float height = 4.65f - Math.Abs(bay) * .24f;
            b.Box(new(2.25f, .28f, 1.25f), p + Vector3.Up * .14f, Slate);
            b.Box(new(1.96f, height, .42f), p + new Vector3(0, height * .5f + .24f, -.04f), Stone);
            b.Box(new(1.58f, height * .79f, .075f), p + new Vector3(0, height * .5f + .24f, .215f), Void);
            b.Box(new(2.1f, .22f, .90f), p + Vector3.Up * (height + .35f), Edge);
            foreach (float side in new[] { -1f, 1f })
            {
                b.Box(new(.15f, height * .88f, .25f), p + new Vector3(side * .86f, height * .5f + .25f, .31f), Parchment);
                b.Box(new(.045f, height * .82f, .08f), p + new Vector3(side * .735f, height * .5f + .25f, .285f), Inlay);
                // Repeated shadow edges are incomplete; they never carry the anchor.
                b.Box(new(.08f, height * .60f, .03f), p + new Vector3(side * .6f + .15f, height * .54f, .32f), Inlay);
            }
            HollowNightArt.ArchCurve(b, p, 0, .86f, height * .78f + .25f, height + .30f, .49f, .078f, Parchment);
            HollowNightArt.ArchCurve(b, p, 0, .735f, height * .76f + .25f, height + .12f, .32f, .042f, Memory);
            for (int row = 0; row < 4; row++)
            {
                float py = .82f + row * .79f;
                b.Box(new(1.42f, .075f, .34f), p + new Vector3(0, py - .24f, .42f), Slate);
                if ((bay + row * 2 + 7) % 4 == 0) continue;
                float offset = (row % 2 - .5f) * .12f;
                b.Box(new(.94f, .44f, .075f), p + new Vector3(offset, py, .343f), Memory);
                b.Box(new(.79f, .32f, .035f), p + new Vector3(offset, py, .3975f), Inlay);
                foreach (float edge in new[] { -1f, 1f })
                {
                    b.Box(new(.055f, .44f, .065f), p + new Vector3(offset + edge * .44f, py, .411f), Memory);
                    b.Box(new(.9f, .038f, .07f), p + new Vector3(offset, py + edge * .196f, .408f), Memory);
                }
                for (int line = 0; line < 3; line++)
                    b.Box(new(.63f - line % 2 * .12f, .019f, .012f), p + new Vector3(offset, py - .12f + line * .12f, .422f), Parchment);
            }
        }
        foreach (float side in new[] { -1f, 1f })
        {
            for (int urn = 0; urn < 4; urn++)
            {
                var p = new Vector3(side * (x + 1.22f), 0, -z * .68f + urn * z * .44f);
                b.Box(new(1.22f, .3f, 1.35f), p + Vector3.Up * .15f, Slate);
                b.Cylinder(.29f, .45f, .43f, p + Vector3.Up * .515f, Memory);
                b.Cylinder(.45f, .30f, .67f, p + Vector3.Up * 1.065f, Memory);
                b.Cylinder(.3f, .38f, .16f, p + Vector3.Up * 1.48f, Parchment);
                b.Cylinder(.39f, .17f, .15f, p + Vector3.Up * 1.635f, Stone);
                foreach (float handle in new[] { -1f, 1f })
                {
                    var shoulder = p + new Vector3(0, 1.29f, handle * .31f);
                    var outer = p + new Vector3(0, 1.16f, handle * .60f);
                    var lower = p + new Vector3(0, .90f, handle * .54f);
                    b.Branch(shoulder, outer, .04f, .034f, Parchment, SurfaceKind.Stone);
                    b.Branch(outer, lower, .034f, .03f, Parchment, SurfaceKind.Stone);
                    b.Branch(lower, p + new Vector3(0, .82f, handle * .34f), .03f, .035f, Parchment, SurfaceKind.Stone);
                }
                // The empty nameplate is part of the urn, below the combat skyline.
                b.Box(new(.025f, .38f, .25f), p + new Vector3(-side * .45f, .95f, 0), Void);
                b.Box(new(.03f, .024f, .17f), p + new Vector3(-side * .47f, .99f, 0), Edge);
                b.Box(new(.03f, .024f, .17f), p + new Vector3(-side * .47f, .87f, 0), Edge);
            }
            var bench = new Vector3(side * x * .56f, 0, -z - .84f);
            b.Box(new(3.2f, .46f, .46f), bench + Vector3.Up * .23f, Slate);
            b.Box(new(3.42f, .14f, .68f), bench + Vector3.Up * .53f, Edge);
            for (int record = 0; record < 4; record++)
                b.Box(new(.43f, .065f, .35f), bench + new Vector3((record - 1.5f) * .7f, .64f, 0), Parchment, new(0, record % 2 == 0 ? -7 : 7, 0));
        }
        // A shut coffer contains the fragments the repeating galleries have lost.
        var coffer = new Vector3(0, 0, -z - 1.25f);
        b.Box(new(2.45f, .84f, .86f), coffer + Vector3.Up * .42f, Memory);
        b.Box(new(2.59f, .17f, .96f), coffer + Vector3.Up * .925f, Parchment);
        b.Box(new(2.13f, .48f, .035f), coffer + new Vector3(0, .45f, .45f), Void);
        b.Box(new(1.93f, .32f, .035f), coffer + new Vector3(0, .45f, .483f), Inlay);
        foreach (float side in new[] { -1f, 1f })
        {
            b.Box(new(.11f, .8f, .06f), coffer + new Vector3(side * .84f, .48f, .47f), Inlay);
            b.Box(new(.065f, .44f, .09f), coffer + new Vector3(side * 1.06f, .45f, .48f), Edge);
        }
    }

    public static bool SupportsMarker(string id) => id.StartsWith("hollow.", StringComparison.Ordinal);

    public static string MarkerLabel(string id, string fallback) => id switch
    {
        "hollow.vault.enter" => "ANCHORED PASSAGE · THE UNREMEMBERED VAULT",
        "hollow.vault.return" => "ANCHORED PASSAGE · RETURN TO THE REPEATING ROOMS",
        "hollow.vault.treasure" => "THE VAULT TESTAMENT",
        "hollow.back.rooms" => "ANCHORED PASSAGE · BACK TO THE REPEATING ROOMS",
        "hollow.back.memory" => "ANCHORED PASSAGE · BACK TO IDENTITY MEMORY",
        "hollow.forward.memory" => "ANCHORED PASSAGE · IDENTITY MEMORY",
        "hollow.forward.breach" => "ANCHORED PASSAGE · THE BREACH HEART",
        _ => fallback
    };

    public static void BuildMarker(Node3D marker, string id)
    {
        bool treasure = id == "hollow.vault.treasure";
        var b = new EnvironmentBuilder(marker, treasure ? "VaultTestament" : "HollowThreshold");
        if (treasure)
        {
            b.Box(new(1.03f, .14f, .83f), new(0, .07f, 0), Slate);
            b.Box(new(.48f, .47f, .56f), new(0, .375f, 0), Memory);
            b.Box(new(1.22f, .13f, .83f), new(0, .675f, 0), Parchment);
            // A recovered name ledger and its silver ring make the reward legible at rest.
            b.Box(new(.47f, .04f, .5f), new(-.25f, .76f, 0), Stone);
            for (int line = 0; line < 4; line++)
                b.Box(new(.33f - line % 2 * .07f, .009f, .019f), new(-.25f, .785f, -.17f + line * .105f), Parchment);
            b.Box(new(.32f, .07f, .35f), new(.3f, .78f, 0), Void);
            b.Torus(.09f, .14f, new(.3f, .84f, 0), Edge);
            b.Box(new(.1f, .05f, .1f), new(.3f, .89f, -.11f), Parchment);
            b.Torus(.13f, .19f, new(0, 1.08f, 0), Anchor, new(90, 0, 0), glow: true);
        }
        else
        {
            b.Box(new(1.42f, .017f, 1.5f), new(0, -.024f, 0), Slate);
            for (int tile = 0; tile < 3; tile++)
                b.Box(new(1.21f, .012f, .37f), new(0, -.009f, (tile - 1) * .47f), Stone);
            foreach (float side in new[] { -1f, 1f })
            {
                b.Box(new(.31f, .43f, .27f), new(side * .84f, .215f, 0), Slate);
                b.Box(new(.36f, .055f, .31f), new(side * .84f, .457f, 0), Edge);
                AnchorGlyph(b, new(side * .84f, .26f, .149f));
            }
            b.Torus(.4f, .47f, new(0, .034f, 0), Anchor, glow: true);
        }
        b.Flush();
    }

    private static void AnchorGlyph(EnvironmentBuilder b, Vector3 p)
    {
        // Identical stem, crossbar and upturned arms on every real threshold. The
        // glyph is small and vertical, distinct from the Breach's large floor seals.
        b.Box(new(.031f, .24f, .014f), p, Anchor, glow: true);
        b.Box(new(.14f, .027f, .014f), p + new Vector3(0, .04f, 0), Anchor, glow: true);
        b.Box(new(.18f, .027f, .014f), p + new Vector3(0, -.11f, 0), Anchor, glow: true);
        foreach (float side in new[] { -1f, 1f })
            b.Box(new(.027f, .075f, .014f), p + new Vector3(side * .079f, -.082f, 0), Anchor, glow: true);
    }
}
