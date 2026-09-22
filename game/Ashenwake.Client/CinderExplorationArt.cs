using Godot;

namespace Ashenwake.Client;

/// <summary>A shuttered forge and its workers' refuge. Tall structures remain beyond
/// the playable rectangle; only the low, usable testament occupies the fighting floor.</summary>
public static class CinderExplorationArt
{
    private const string Iron = "41454b", Edge = "727477", Copper = "a07753", Dark = "20282e", Basalt = "47424a", Ash = "82746b", Cloth = "74625c";

    public static void BuildFoundry(EnvironmentBuilder b, float x, float z)
    {
        Vector3 furnace = new(0, 0, -z - 2.15f);
        b.Box(new(7.2f, .34f, 2.7f), furnace + Vector3.Up * .17f, Basalt);
        b.Box(new(6.4f, 3.6f, 1.8f), furnace + Vector3.Up * 2.08f, Iron);
        b.Box(new(6.8f, .34f, 2.05f), furnace + Vector3.Up * 3.95f, Edge);
        // The forge was deliberately shut: crossed locking bars and cold, dark doors.
        foreach (float side in new[] { -1f, 1f })
        {
            b.Box(new(2.56f, 2.6f, .12f), furnace + new Vector3(side * 1.35f, 1.85f, .97f), Dark);
            b.Box(new(.17f, 2.8f, .16f), furnace + new Vector3(side * 2.69f, 1.85f, 1.02f), Copper);
            b.Box(new(.15f, 2.7f, .1f), furnace + new Vector3(side * .10f, 1.85f, 1.05f), Edge);
            b.Beam(furnace + new Vector3(side * 2.52f, .70f, 1.12f), furnace + new Vector3(-side * 2.52f, 3.05f, 1.12f), .17f, Copper);
            b.Cylinder(.13f, .13f, .12f, furnace + new Vector3(side * 2.57f, 2.8f, 1.22f), Edge, new(90, 0, 0));
            // Uneven patches and substantial locking shoes belong to the cold forge;
            // the family roster above the doors stays clean and readable.
            b.Box(new(.56f, .52f, .11f), furnace + new Vector3(side * 2.48f, .73f, 1.12f), Iron);
            b.Box(new(.56f, .52f, .11f), furnace + new Vector3(side * 2.48f, 3.03f, 1.12f), Iron);
            foreach (float lockHeight in new[] { .73f, 3.03f })
                foreach (float bolt in new[] { -1f, 1f })
                    CinderReachArt.Rivet(b, furnace + new Vector3(side * 2.48f + bolt * .18f, lockHeight, 1.20f), Vector3.Back, .05f);
            b.Box(new(.33f, .98f, .025f), furnace + new Vector3(side * 2.04f, 1.9f, 1.041f), Basalt);
            b.Box(new(.17f, .64f, .025f), furnace + new Vector3(side * 1.84f, 2.05f, 1.044f), Iron);
            b.Box(new(.34f, .75f, .017f), furnace + new Vector3(side * 2.90f, 3.24f, .913f), Dark);
            // Exhaust stacks and tool frames stand outside the movement boundary.
            var stack = new Vector3(side * (x + 1.4f), 0, -z * .66f);
            b.Box(new(1.25f, 3.9f, 1.35f), stack + Vector3.Up * 1.95f, Basalt);
            b.Box(new(1.48f, .22f, 1.55f), stack + Vector3.Up * 4.01f, Edge);
            for (int band = 0; band < 3; band++)
            {
                b.Box(new(1.28f, .13f, 1.38f), stack + Vector3.Up * (.92f + band * 1.15f), Copper);
                CinderReachArt.Rivet(b, stack + new Vector3(-.39f, .92f + band * 1.15f, .716f), Vector3.Back, .043f);
                CinderReachArt.Rivet(b, stack + new Vector3(.39f, .92f + band * 1.15f, .716f), Vector3.Back, .043f);
            }
            b.Box(new(.24f, 1.58f, .018f), stack + new Vector3(-.11f, 3.10f, .685f), Dark);
            b.Box(new(.11f, 1.02f, .019f), stack + new Vector3(.13f, 3.37f, .686f), Iron);
            CinderReachArt.Pipe(b, new(side * (x + .90f), .38f, -z + .8f), new(side * (x + .90f), .38f, z * .69f), .12f, Iron, 5);
            // Bedrolls beneath metal shields remember the families the furnace sheltered.
            for (int bed = 0; bed < 3; bed++)
            {
                var refuge = new Vector3(side * (x + .86f), 0, -z * .19f + bed * 2.55f);
                b.Box(new(.98f, .16f, 1.66f), refuge + Vector3.Up * .08f, Dark);
                b.Box(new(.76f, .12f, 1.23f), refuge + Vector3.Up * .22f, Cloth);
                b.Box(new(.73f, .22f, .28f), refuge + new Vector3(0, .27f, -.57f), Ash);
                b.Box(new(.23f, .35f, .23f), refuge + new Vector3(side * .65f, .18f, .5f), Copper);
            }
        }
        b.Box(new(2.2f, .76f, .13f), furnace + new Vector3(0, 3.35f, 1.08f), Basalt);
        for (int line = 0; line < 5; line++)
            b.Box(new(1.62f - line % 3 * .19f, .027f, .023f), furnace + new Vector3(0, 3.12f + line * .10f, 1.16f), Ash);
        // A broken crane hook hangs behind the refuge, never over a walkable route.
        var crane = new Vector3(-x * .54f, 0, -z - 2.3f);
        b.Beam(crane, crane + Vector3.Up * 5.8f, .27f, Iron);
        b.Beam(crane + Vector3.Up * 5.8f, crane + new Vector3(2.1f, 5.8f, 0), .25f, Edge);
        b.Branch(crane + new Vector3(2.1f, 5.7f, 0), crane + new Vector3(2.1f, 4.4f, 0), .032f, .032f, Copper, SurfaceKind.Metal);
        b.Beam(crane + new Vector3(0, 4.58f, 0), crane + new Vector3(1.25f, 5.8f, 0), .16f, Iron);
        b.Box(new(.54f, .57f, .09f), crane + new Vector3(.10f, 5.68f, .19f), Copper);
        foreach (float bolt in new[] { -1f, 1f })
            CinderReachArt.Rivet(b, crane + new Vector3(.10f + bolt * .16f, 5.68f, .259f), Vector3.Back, .052f);
        CinderReachArt.Cable(b, crane + new Vector3(.13f, 5.67f, -.20f), crane + new Vector3(1.91f, 5.70f, -.20f), .44f, .025f);
        b.Torus(.16f, .24f, crane + new Vector3(2.1f, 4.17f, 0), Edge, new(90, 0, 0));
    }

    public static bool SupportsMarker(string id) => id.StartsWith("cinder.", StringComparison.Ordinal);

    public static string MarkerLabel(string id, string fallback) => id switch
    {
        "cinder.foundry.enter" => "SEALED FOUNDRY · OPTIONAL",
        "cinder.foundry.return" => "RETURN TO THE CINDER FIELDS",
        "cinder.foundry.treasure" => "THE FOUNDRY TESTAMENT",
        "cinder.storm.enter" => fallback.StartsWith("Revisit", StringComparison.Ordinal) ? "STORM COLLECTORS · CLEARED" : "BURNING RAIN · TIMED STORM",
        "cinder.storm.return" => fallback.Split('·')[0].Trim().ToUpperInvariant(),
        "cinder.back.fields" => "BACK TO THE CINDER FIELDS",
        "cinder.back.floor" => "BACK TO THE EXTRACTION FLOOR",
        "cinder.forward.floor" => "EXTRACTION FLOOR",
        "cinder.forward.spindle" => "FURNACE SPINDLE",
        _ => fallback
    };

    public static void BuildMarker(Node3D marker, string id)
    {
        bool treasure = id == "cinder.foundry.treasure", storm = id.StartsWith("cinder.storm.", StringComparison.Ordinal);
        string signal = storm ? "b4c7ce" : "d1b789";
        var b = new EnvironmentBuilder(marker, treasure ? "FoundryTestament" : "CinderThreshold");
        if (treasure)
        {
            b.Box(new(.94f, .13f, .73f), new(0, .065f, 0), Basalt);
            b.Box(new(.39f, .47f, .45f), new(0, .365f, 0), Iron);
            b.Box(new(.97f, .16f, .54f), new(0, .68f, 0), Edge);
            b.Box(new(.29f, .10f, .3f), new(-.53f, .665f, 0), Copper);
            // Engraved roster rests next to a saber laid across the cooled anvil.
            b.Box(new(.37f, .035f, .42f), new(.15f, .787f, .03f), Ash);
            for (int line = 0; line < 4; line++)
                b.Box(new(.24f - line % 2 * .05f, .009f, .014f), new(.15f, .811f, -.09f + line * .073f), Dark);
            b.Box(new(.78f, .04f, .07f), new(-.14f, .79f, -.22f), Edge);
            b.Box(new(.04f, .06f, .28f), new(-.55f, .81f, -.22f), Copper);
            b.Box(new(.18f, .04f, .075f), new(-.66f, .79f, -.22f), Dark);
            b.Torus(.13f, .2f, new(0, 1.04f, 0), signal, new(90, 0, 0), glow: true);
        }
        else
        {
            // Recessed crossing plates between low markers leave the center visibly open.
            b.Box(new(1.36f, .014f, 1.52f), new(0, -.022f, 0), Iron);
            for (int bar = 0; bar < 5; bar++)
                b.Box(new(1.24f, .009f, .06f), new(0, -.010f, (bar - 2) * .28f), Edge);
            foreach (float side in new[] { -1f, 1f })
            {
                b.Box(new(.18f, .37f, .24f), new(side * .79f, .185f, 0), Basalt);
                b.Box(new(.20f, .06f, .26f), new(side * .79f, .40f, 0), Copper);
                b.Box(new(.10f, .10f, .035f), new(side * .79f, .26f, .135f), signal, glow: true);
            }
            b.Torus(.40f, .47f, new(0, .034f, 0), signal, glow: true);
        }
        b.Flush();
    }
}
