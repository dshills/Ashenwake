using Godot;

namespace Ashenwake.Client;

/// <summary>A roofless burial vault. Walls on playable ground belong to the authored obstacle presenter;
/// these outer niches and arches frame the chamber without hiding its fights or return threshold.</summary>
public static class GreyMarchExplorationArt
{
    private const string Stone = "697b7c", Pale = "a0aaa0", Dark = "354b54", Bone = "aea68b", Silk = "a8c9cd";

    public static void Build(Node3D parent, float x, float z)
    {
        var b = new EnvironmentBuilder(parent, "GreyMarchArchitecture");
        // A stepped vault on the far edge exposes the floor to the isometric camera.
        for (int bay = 0; bay < 5; bay++)
        {
            float px = (bay - 2) * x * .36f;
            Vector3 p = new(px, 0, -z - 1.12f);
            b.Box(new(x * .33f, 2.7f, .82f), p + new Vector3(0, 1.35f, -.18f), Dark);
            b.Box(new(x * .34f, .18f, 1.12f), p + new Vector3(0, 2.79f, -.18f), Stone);
            b.Box(new(.33f, 3.2f, .66f), p + new Vector3(-x * .16f, 1.6f, .23f), Stone);
            for (int row = 0; row < 2; row++)
            {
                Vector3 niche = p + new Vector3(0, .67f + row * 1.03f, .26f);
                b.Box(new(x * .21f, .58f, .11f), niche, "243943");
                b.Box(new(x * .23f, .11f, .41f), niche + new Vector3(0, -.34f, .08f), Pale);
                for (int skull = 0; skull < 3; skull++)
                {
                    Vector3 head = niche + new Vector3((skull - 1) * .30f, -.15f, .09f);
                    b.Cylinder(.105f, .13f, .2f, head, Bone, surface: SurfaceKind.Bone);
                    b.Box(new(.16f, .045f, .028f), head + new Vector3(0, .035f, .11f), Dark);
                }
            }
            if (bay % 2 == 0) Candle(b, p + new Vector3(x * .13f, 0, .62f));
        }
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 p = new(side * x * .68f, 0, -z - 2.45f);
            b.Box(new(.7f, 3.75f, .82f), p + Vector3.Up * 1.875f, Stone);
            b.Box(new(.91f, .2f, 1.03f), p + Vector3.Up * 3.85f, Pale);
            b.Beam(p + Vector3.Up * 3.85f, new(0, 5.0f, -z - 2.45f), .28f, Stone);
        }
        // Burial shelves stop beyond the west limit; the nearer edges remain ankle high.
        for (int bay = 0; bay < 4; bay++)
        {
            Vector3 p = new(-x - 1.08f, 0, -z * .66f + bay * z * .43f);
            b.Box(new(.78f, 1.22f, 1.85f), p + Vector3.Up * .61f, Dark);
            b.Box(new(.92f, .13f, 1.98f), p + Vector3.Up * 1.285f, Stone);
            b.Box(new(.13f, .32f, 1.57f), p + new Vector3(.43f, .72f, 0), Pale);
            if (bay % 2 == 0) Candle(b, p + new Vector3(.08f, 1.35f, .56f));
            b.Box(new(.63f, .25f, 1.2f), new(x + .63f, .125f, p.Z), Stone);
        }
        for (int fragment = 0; fragment < 7; fragment++)
            b.Box(new(1.05f, .13f + fragment % 3 * .035f, .72f),
                new(-x * .83f + fragment * x * .27f, .06f, z + .66f), fragment % 2 == 0 ? Stone : Dark, new(0, fragment * 13, 0));
        b.Flush();
        ScenicTerrain.Build(parent.GetNode<Node3D>("GreyMarchArchitecture"),
        [
            new(new(-x * .63f, -.3f, -z - 5.2f), new(x * .85f, 3.9f, 5.1f), -5, 2),
            new(new(x * .56f, -.3f, -z - 5.4f), new(x * .96f, 4.6f, 5.2f), 6, 4),
            new(new(-x - 3.1f, -.3f, -z * .17f), new(4.1f, 2.7f, z * 1.7f), 0, 3)
        ], Dark, Stone, Pale,
        [
            new(new(0, -.88f, -z - 1.68f), new(x * 2 + 2.4f, .82f, 3.25f)),
            new(new(-x - 1.12f, -.88f, 0), new(2.15f, .82f, z * 2 + 2.5f)),
            new(new(x + .69f, -.58f, 0), new(1.31f, .52f, z * 2 + 1.2f)),
            new(new(0, -.58f, z + .68f), new(x * 2 + 1.9f, .52f, 1.28f))
        ]);
    }

    public static bool SupportsMarker(string id) =>
        id.StartsWith("opening.crypt.", StringComparison.Ordinal) || id.StartsWith("opening.back.", StringComparison.Ordinal) ||
        id.StartsWith("opening.forward.", StringComparison.Ordinal);

    public static string MarkerLabel(string id, string fallback) => id switch
    {
        "opening.crypt.enter" => "WIDOW'S CRYPT · OPTIONAL",
        "opening.crypt.return" => "RETURN TO THE ROAD",
        "opening.crypt.treasure" => "WIDOW'S TESTAMENT",
        "opening.back.road" => "BACK TO THE ROAD",
        "opening.back.monastery" => "BACK TO THE MONASTERY",
        "opening.forward.monastery" => "MONASTERY COURTYARD",
        "opening.forward.sanctum" => "BELL SANCTUARY",
        _ => fallback
    };

    /// <summary>The marker already has the authoritative target position. Door steps are recessed;
    /// their open outline and small silk-colored ring never suggest a closed walking barrier.</summary>
    public static void BuildMarker(Node3D marker, string id, bool inCrypt = false)
    {
        var b = new EnvironmentBuilder(marker, id == "opening.crypt.treasure" ? "WidowReliquary" : "OpeningThreshold");
        if (id == "opening.crypt.treasure")
        {
            b.Cylinder(.46f, .36f, .10f, new(0, .05f, 0), Dark);
            b.Box(new(.62f, .33f, .44f), new(0, .265f, 0), Stone);
            b.Box(new(.72f, .09f, .53f), new(0, .475f, 0), Pale);
            foreach (float side in new[] { -1f, 1f })
                b.Box(new(.047f, .39f, .49f), new(side * .21f, .295f, 0), "ad8b57", surface: SurfaceKind.Metal);
            b.Torus(.12f, .17f, new(0, .74f, 0), Silk, new(90, 0, 0), glow: true);
            b.Box(new(.035f, .24f, .035f), new(0, .75f, 0), Silk, glow: true);
        }
        else
        {
            bool enter = id == "opening.crypt.enter";
            b.Box(new(1.24f, .025f, 1.64f), new(0, -.025f, 0), Dark);
            for (int step = 0; step < 5; step++)
                b.Box(new(1.1f, .01f, .27f), new(0, -.009f - step * .001f, -.54f + step * .27f),
                    enter ? step < 2 ? "87928b" : step < 4 ? "647877" : Dark : "87928b");
            foreach (float side in new[] { -1f, 1f })
                b.Box(new(.11f, .08f, 1.8f), new(side * .69f, 0, 0), Pale);
            b.Box(new(1.48f, .08f, .11f), new(0, 0, -.87f), Pale);
            // An incomplete floor ring distinguishes a usable threshold from a solid door.
            b.Torus(.49f, .54f, new(0, .057f, .12f), enter || inCrypt ? Silk : "ccb487", glow: true);
            b.Box(new(.07f, .018f, .32f), new(0, .06f, .64f), enter || inCrypt ? Silk : "ccb487", glow: true);
        }
        b.Flush();
    }

    private static void Candle(EnvironmentBuilder b, Vector3 p)
    {
        b.Cylinder(.15f, .12f, .12f, p + Vector3.Up * .06f, Dark);
        b.Cylinder(.065f, .052f, .26f, p + Vector3.Up * .25f, Bone);
        b.Cylinder(.058f, 0, .14f, p + Vector3.Up * .45f, "e9c38b", glow: true);
    }
}
