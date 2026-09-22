using Godot;

namespace Ashenwake.Client;

/// <summary>The hidden testimony of Orrun's first covenant. Tall records remain outside
/// the simulation boundary; only low thresholds and the usable testament occupy the floor.</summary>
public static class SpineExplorationArt
{
    private const string Stone = "475560", Bone = "9eabaf", Edge = "c2c8bc", Shadow = "2e3d49", Brass = "9c9070", Cloth = "566878", Ivory = "c8bd9b";

    public static void BuildArchive(EnvironmentBuilder b, float x, float z)
    {
        // An excavated wall of public testimony: several generations of tablets, with
        // a missing central record whose fragments the keeper saved on the desk below.
        var wall = new Vector3(0, 0, -z - 2.2f);
        b.Box(new(x * 1.76f, .32f, 2.1f), wall + Vector3.Up * .16f, Stone);
        for (int bay = -3; bay <= 3; bay++)
        {
            float px = bay * 2.55f;
            float height = 4.1f - Math.Abs(bay) * .27f;
            b.Box(new(2.23f, height, .65f), wall + new Vector3(px, height * .5f + .28f, 0), Bone);
            b.Box(new(1.88f, .15f, .86f), wall + new Vector3(px, height + .32f, 0), Edge);
            ShatteredSpineArt.Vertebra(b, wall + new Vector3(px, height + .43f, -.08f), 1.8f, .23f, false);
            for (int shelf = 0; shelf < 3; shelf++)
            {
                float py = .85f + shelf * .96f;
                b.Box(new(1.97f, .13f, .64f), wall + new Vector3(px, py - .34f, .54f), Edge);
                b.Box(new(1.78f, .68f, .055f), wall + new Vector3(px, py, .36f), Shadow);
                if (bay == 0 && shelf == 1) continue;
                for (int tablet = -1; tablet <= 1; tablet++)
                {
                    var p = wall + new Vector3(px + tablet * .56f, py, .47f);
                    string face = (bay + shelf + tablet) % 3 == 0 ? Ivory : Edge;
                    b.Box(new(.42f, .58f, .10f), p + Vector3.Forward * .02f, Bone);
                    b.Box(new(.32f, .47f, .035f), p + new Vector3(0, 0, .045f), face);
                    foreach (float edge in new[] { -1f, 1f })
                    {
                        b.Box(new(.044f, .58f, .095f), p + new Vector3(edge * .188f, 0, .04f), face);
                        b.Box(new(.35f, .044f, .095f), p + new Vector3(0, edge * .268f, .04f), face);
                    }
                    for (int line = 0; line < 4; line++)
                        b.Box(new(.26f - line % 2 * .04f, .025f, .009f), p + new Vector3(0, -.17f + line * .11f, .070f), Stone);
                }
            }
            b.Box(new(.075f, height * .83f, .065f), wall + new Vector3(px - 1.01f, height * .5f + .3f, .37f), Brass);
            if (bay != 0)
                ShatteredSpineArt.Weathering(b, wall + new Vector3(px + .99f, height * .68f, .341f), height * .23f);
        }
        foreach (float side in new[] { -1f, 1f })
        {
            // Record vaults and a keeper's bench stay entirely beyond the side coping.
            for (int rack = 0; rack < 3; rack++)
            {
                var p = new Vector3(side * (x + 1.3f), 0, -z * .58f + rack * z * .52f);
                b.Box(new(1.2f, .24f, 2.25f), p + Vector3.Up * .12f, Stone);
                b.Box(new(.88f, 2.05f, 1.93f), p + Vector3.Up * 1.22f, Bone);
                b.Box(new(1.13f, .2f, 2.12f), p + Vector3.Up * 2.35f, Edge);
                foreach (float end in new[] { -1f, 1f })
                    b.Branch(p + new Vector3(-side * .49f, .35f, end * .77f), p + new Vector3(-side * .47f, 2.23f, end * .73f),
                        .075f, .048f, Edge, SurfaceKind.Bone);
                ShatteredSpineArt.Vertebra(b, p + new Vector3(0, 2.47f, .65f), .86f, .16f, false);
                for (int seal = -1; seal <= 1; seal++)
                {
                    b.Box(new(.035f, 1.7f, .1f), p + new Vector3(-side * .46f, 1.22f, seal * .55f), Brass);
                    b.Cylinder(.115f, .115f, .045f, p + new Vector3(-side * .49f, 1.2f, seal * .55f), Ivory, new(0, 0, 90));
                }
                b.Box(new(.76f, .17f, .48f), p + new Vector3(-side * .04f, 2.53f, -.48f), Cloth);
                b.Box(new(.6f, .10f, .44f), p + new Vector3(-side * .04f, 2.67f, -.48f), Ivory);
            }
            var desk = new Vector3(side * (x * .64f), 0, -z - .92f);
            b.Box(new(2.5f, .68f, .57f), desk + Vector3.Up * .34f, Stone);
            b.Box(new(2.75f, .14f, .81f), desk + Vector3.Up * .77f, Edge);
            for (int page = 0; page < 3; page++)
                b.Box(new(.47f, .08f, .44f), desk + new Vector3((page - 1) * .68f, .88f, 0), Ivory, new(0, (page - 1) * 9, 0));
            ShatteredSpineArt.FoldedCloth(b, new(side * x * .91f, 2.95f, -z - 1.85f), .54f, 1.5f, false);
        }
    }

    public static bool SupportsMarker(string id) => id.StartsWith("spine.", StringComparison.Ordinal);

    public static string MarkerLabel(string id, string fallback) => id switch
    {
        "spine.archive.enter" => "OATHKEEPER'S ARCHIVE · OPTIONAL",
        "spine.archive.return" => "RETURN TO THE BONE CAUSEWAY",
        "spine.archive.treasure" => "THE ARCHIVE TESTAMENT",
        "spine.memory.enter" => fallback.StartsWith("Revisit", StringComparison.Ordinal) ? "THE FIRST PROMISE · REMEMBERED" : "THE PROMISE BEFORE STONE · DIVINE MEMORY",
        "spine.memory.return" => fallback.Split('·')[0].Trim().ToUpperInvariant(),
        "spine.back.causeway" => "BACK TO THE BONE CAUSEWAY",
        "spine.back.hall" => "BACK TO THE CONTRACT HALL",
        "spine.forward.hall" => "CONTRACT HALL",
        "spine.forward.warden" => "COVENANT WARDEN",
        _ => fallback
    };

    public static void BuildMarker(Node3D marker, string id)
    {
        bool treasure = id == "spine.archive.treasure", memory = id.StartsWith("spine.memory.", StringComparison.Ordinal);
        string signal = memory ? "e5d7b5" : "b9cbd5";
        var b = new EnvironmentBuilder(marker, treasure ? "ArchiveTestament" : "SpineThreshold");
        if (treasure)
        {
            b.Box(new(1.13f, .16f, .87f), new(0, .08f, 0), Stone);
            b.Box(new(.55f, .44f, .6f), new(0, .38f, 0), Bone);
            b.Box(new(1.28f, .15f, .85f), new(0, .675f, 0), Edge);
            // Two broken tablets reunite beside the keeper's folded breastplate.
            foreach (float side in new[] { -1f, 1f })
            {
                b.Box(new(.21f, .04f, .43f), new(-.32f + side * .115f, .78f, .09f), Ivory);
                for (int line = 0; line < 4; line++)
                    b.Box(new(.16f, .008f, .015f), new(-.32f + side * .115f, .805f, -.05f + line * .08f), Shadow);
            }
            b.Box(new(.43f, .12f, .51f), new(.29f, .81f, 0), Brass);
            b.Box(new(.33f, .06f, .37f), new(.29f, .9f, -.02f), Bone);
            b.Box(new(.07f, .017f, .3f), new(.29f, .94f, -.01f), Ivory);
            b.Box(new(.26f, .017f, .06f), new(.29f, .94f, -.08f), Ivory);
            b.Torus(.12f, .18f, new(0, 1.1f, 0), signal, new(90, 0, 0), glow: true);
        }
        else
        {
            // A low crossing seam and paired witness stones keep the passage center open.
            b.Box(new(1.38f, .018f, 1.5f), new(0, -.025f, 0), Stone);
            for (int slab = 0; slab < 3; slab++)
                b.Box(new(1.18f, .013f, .35f), new(0, -.009f, (slab - 1) * .46f), memory ? Ivory : Edge);
            foreach (float side in new[] { -1f, 1f })
            {
                b.Box(new(.2f, .36f, .3f), new(side * .82f, .18f, 0), Bone);
                b.Box(new(.24f, .07f, .34f), new(side * .82f, .395f, 0), memory ? Ivory : Edge);
                b.Box(new(.05f, .2f, .015f), new(side * .82f, .215f, .158f), signal, glow: true);
                b.Box(new(.13f, .035f, .02f), new(side * .82f, .25f, .161f), Brass);
            }
            b.Torus(.4f, .47f, new(0, .034f, 0), signal, glow: true);
        }
        b.Flush();
    }
}
