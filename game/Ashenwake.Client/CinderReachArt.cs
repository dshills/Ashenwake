using Godot;

namespace Ashenwake.Client;

/// <summary>Obsidian industry around the simulation rectangle; tall silhouettes never occupy its walkable floor.</summary>
public static class CinderReachArt
{
    private const string Obsidian = "302f35";
    private const string Basalt = "47424a";
    private const string CutStone = "61575a";
    private const string Ash = "82746b";
    private const string Iron = "41454b";
    private const string IronEdge = "727477";
    private const string Rust = "785440";
    private const string Copper = "a07753";
    private const string Dark = "20282e";
    private const string Coal = "3c3436";
    private const string Ember = "b85c36";
    private const string Heat = "dc8751";
    private const string Cloth = "74625c";
    private const string Lava = "a43a19";
    private const string CoolingLava = "81301f";

    public static void Build(Node3D parent, float halfWidth, float halfDepth, string encounterId)
    {
        var b = new EnvironmentBuilder(parent, "CinderReachArchitecture");
        switch (encounterId)
        {
            case "campaign.extraction_floor": Extraction(b, halfWidth, halfDepth); break;
            case "campaign.furnace_spindle": Furnace(b, halfWidth, halfDepth); break;
            case "exploration.burning_rain": Storm(b, halfWidth, halfDepth); break;
            default: City(b, halfWidth, halfDepth); break;
        }
        Perimeter(b, halfWidth, halfDepth, encounterId == "exploration.burning_rain");
        b.Flush();
        // Lava has its own low-energy, unshaded surface so warm arena lighting cannot bleach it into a warning-yellow strip.
        var architecture = parent.GetNode<Node3D>("CinderReachArchitecture");
        foreach (var mesh in architecture.GetChildren().OfType<MeshInstance3D>())
        {
            if (mesh.Mesh?.SurfaceGetMaterial(0) is not StandardMaterial3D material || !material.EmissionEnabled) continue;
            if (material.AlbedoColor != new Color(Lava) && material.AlbedoColor != new Color(CoolingLava)) continue;
            material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            material.EmissionEnergyMultiplier = .28f;
        }
    }

    private static void City(EnvironmentBuilder b, float x, float z)
    {
        // Working chimney houses flank a closed city gate. The safe floor remains an uncluttered street.
        FoundryHouse(b, new(-x * .57f, 0, -z - 3.25f), 4.3f, 4.6f, false);
        FoundryHouse(b, new(x * .57f, 0, -z - 3.75f), 4.5f, 5.2f, true);
        var gate = new Vector3(0, 0, -z - 2.45f);
        foreach (float side in new[] { -1f, 1f })
        {
            b.Box(new(.64f, 3.7f, .88f), gate + new Vector3(side * 1.15f, 1.85f, 0), CutStone);
            b.Box(new(.86f, .22f, 1.05f), gate + new Vector3(side * 1.15f, 3.8f, 0), Ash);
            b.Box(new(.65f, .34f, .74f), gate + new Vector3(side * 1.15f, .17f, .3f), Obsidian);
        }
        b.Box(new(2.18f, 2.8f, .16f), gate + Vector3.Up * 1.4f, Dark);
        for (int i = 0; i < 7; i++) b.Box(new(.055f, 2.85f, .075f), gate + new Vector3((i - 3) * .29f, 1.43f, .14f), IronEdge);
        b.Box(new(2.85f, .34f, .89f), gate + Vector3.Up * 3.25f, Basalt);
        b.Box(new(1.1f, .3f, .08f), gate + new Vector3(0, 3.6f, .47f), Copper);
        b.Box(new(.09f, .55f, .09f), gate + new Vector3(0, 3.63f, .53f), IronEdge);
        BasaltCluster(b, new(-x - 2.2f, 0, -z * .66f), 2.5f);
        Pump(b, new(x + 2.3f, 0, -z * .49f), 2.4f);
        // Insulated heat pipes explain the city's dependence on the furnace without crossing a player route.
        foreach (float side in new[] { -1f, 1f })
        {
            b.Beam(new(side * (x + .78f), .38f, -z - .7f), new(side * (x + .78f), .38f, z * .61f), .24f, Rust);
            for (int i = 0; i < 5; i++)
                b.Box(new(.48f, .13f, .24f), new(side * (x + .78f), .38f, -z * .75f + i * z * .3f), IronEdge);
        }
    }

    private static void FoundryHouse(EnvironmentBuilder b, Vector3 p, float width, float height, bool openForge)
    {
        const float depth = 2.9f;
        b.Box(new(width + .25f, .35f, depth + .35f), p + Vector3.Up * .175f, Obsidian);
        b.Box(new(width, height, depth), p + Vector3.Up * (height * .5f + .3f), Basalt);
        b.Box(new(width + .25f, .26f, depth + .22f), p + Vector3.Up * (height + .44f), CutStone);
        foreach (float side in new[] { -1f, 1f })
        {
            b.Box(new(.31f, height + .25f, .28f), p + new Vector3(side * width * .42f, height * .5f + .3f, depth * .51f), Obsidian);
            b.Box(new(.72f, .75f, .055f), p + new Vector3(side * width * .24f, height * .74f, depth * .515f), Dark);
            b.Box(new(.48f, .43f, .035f), p + new Vector3(side * width * .24f, height * .74f, depth * .53f), Ember, glow: true);
            b.Box(new(.075f, .76f, .055f), p + new Vector3(side * width * .24f, height * .74f, depth * .55f), Iron);
            b.Box(new(.74f, .065f, .075f), p + new Vector3(side * width * .24f, height * .74f, depth * .55f), Iron);
        }
        b.Box(new(width * .53f, 1.6f, .06f), p + new Vector3(0, 1.08f, depth * .515f), Dark);
        b.Box(new(width * .62f, .25f, .46f), p + new Vector3(0, 1.96f, depth * .53f), Rust);
        if (openForge)
        {
            b.Box(new(width * .39f, .46f, .07f), p + new Vector3(0, .7f, depth * .54f), Heat, glow: true);
            for (int i = 0; i < 5; i++) b.Box(new(.07f, 1.49f, .08f), p + new Vector3((i - 2) * width * .09f, 1.09f, depth * .56f), Iron);
        }
        else
        {
            b.Box(new(width * .38f, 1.46f, .12f), p + new Vector3(0, 1.05f, depth * .545f), Iron);
            b.Box(new(width * .37f, .15f, .05f), p + new Vector3(0, 1.41f, depth * .575f), Copper);
            b.Box(new(.6f, .62f, .075f), p + new Vector3(-width * .31f, 1.45f, depth * .57f), Cloth);
        }
        Chimney(b, p + new Vector3(width * .25f, height + .55f, -.52f), 2.1f);
        Chimney(b, p + new Vector3(-width * .28f, height + .55f, -.45f), 1.25f);
        // Slanted, thick roofs belong to the facade; their nearest edge remains well behind the room.
        b.Box(new(width * .43f, .1f, depth + .38f), p + new Vector3(-width * .21f, height + .78f, .01f), Iron, new(0, 0, 8));
        b.Box(new(width * .43f, .1f, depth + .38f), p + new Vector3(width * .21f, height + .78f, .01f), Iron, new(0, 0, -8));
    }

    private static void Extraction(EnvironmentBuilder b, float x, float z)
    {
        // Two shaft hoists and their rear gantry read as a working mine without adding false floor obstacles.
        foreach (float side in new[] { -1f, 1f })
        {
            var shaft = new Vector3(side * x * .61f, 0, -z - 3.8f);
            b.Cylinder(1.52f, 1.52f, .3f, shaft + Vector3.Up * .15f, Obsidian);
            b.Cylinder(1.18f, 1.18f, .045f, shaft + Vector3.Up * .325f, Dark);
            b.Torus(1.1f, 1.45f, shaft + Vector3.Up * .35f, Copper);
            foreach (float leg in new[] { -1f, 1f })
            {
                var foot = shaft + new Vector3(leg * 1.48f, 0, .5f);
                b.Box(new(.65f, .27f, .83f), foot + Vector3.Up * .135f, CutStone);
                b.Beam(foot, shaft + new Vector3(leg * 1.07f, 5.1f, .1f), .3f, Iron);
                b.Box(new(.18f, .17f, 1.12f), foot + new Vector3(0, 2.42f, -.22f), Copper);
            }
            b.Box(new(3.15f, .45f, .62f), shaft + new Vector3(0, 5.12f, .1f), IronEdge);
            b.Torus(.38f, .62f, shaft + new Vector3(0, 4.62f, .48f), Rust, new(90, 0, 0));
            b.Beam(shaft + new Vector3(0, 4.65f, .47f), shaft + new Vector3(0, 1.22f, .47f), .065f, IronEdge);
            b.Cylinder(.64f, .5f, 1.25f, shaft + new Vector3(0, 1.62f, .48f), Iron);
            for (int band = 0; band < 3; band++)
                b.Torus(.48f, .65f, shaft + new Vector3(0, 1.09f + band * .5f, .48f), Rust);
            Conveyor(b, new(side * (x + 1.92f), 0, -z * .43f), z * .88f);
            Pump(b, new(side * (x + 2.45f), 0, z * .4f), 1.55f);
        }
        b.Box(new(x * 1.5f, .3f, .7f), new(0, 4.07f, -z - 4.4f), Iron);
        for (int i = 0; i < 7; i++)
            b.Beam(new(-x * .66f + i * x * .22f, 4.2f, -z - 4.35f), new(-x * .55f + i * x * .22f, 4.96f, -z - 4.35f), .12f, Rust);
        b.Box(new(x * 1.5f, .16f, .22f), new(0, 5.03f, -z - 4.35f), Copper);
        b.Box(new(2.35f, 1.75f, 1.25f), new(0, .88f, -z - 2.15f), Basalt);
        b.Box(new(2.56f, .16f, 1.46f), new(0, 1.83f, -z - 2.15f), Iron);
        for (int i = 0; i < 3; i++)
        {
            b.Box(new(.48f, .58f, .07f), new((i - 1) * .68f, 1.12f, -z - 1.48f), Dark);
            b.Box(new(.32f, .15f, .05f), new((i - 1) * .68f, 1.22f, -z - 1.42f), Copper);
        }
    }

    private static void Furnace(EnvironmentBuilder b, float x, float z)
    {
        // A distant industrial apse frames the separately animated Spindle. All four arena lanes stay visible.
        var back = new Vector3(0, 0, -z - 4.1f);
        b.Box(new(x * 1.68f, .46f, 2.35f), back + Vector3.Up * .23f, Obsidian);
        foreach (float side in new[] { -1f, 1f })
        {
            for (int i = 0; i < 3; i++)
            {
                var foot = back + new Vector3(side * (4.2f + i * 2.1f), 0, -i * .38f);
                float height = 5.9f - i * .55f;
                b.Box(new(.95f, height, 1.18f), foot + Vector3.Up * (height * .5f), Basalt);
                b.Box(new(.25f, height - .5f, .16f), foot + new Vector3(0, height * .5f, .66f), Copper);
                b.Box(new(1.25f, .3f, 1.38f), foot + Vector3.Up * (height + .13f), Iron);
                b.Beam(foot + new Vector3(side * .58f, .25f, .23f), foot + new Vector3(side * .38f, height * .7f, .23f), .24f, Rust);
                Chimney(b, foot + Vector3.Up * (height + .3f), .78f + i * .18f);
            }
            Pump(b, new(side * (x + 2.55f), 0, -z * .38f), 3.55f);
            b.Beam(new(side * (x + 1.48f), .7f, -z * .62f), new(side * (x + 1.48f), .7f, z * .58f), .48f, Rust);
            for (int i = 0; i < 4; i++)
                b.Box(new(.69f, .23f, .27f), new(side * (x + 1.48f), .7f, -z * .45f + i * z * .28f), IronEdge);
            b.Box(new(5.65f, .3f, .87f), back + new Vector3(side * 6.4f, 4.83f, -.39f), Iron);
            b.Box(new(5.65f, .12f, .91f), back + new Vector3(side * 6.4f, 5.06f, -.39f), Copper);
        }
        // The central seven-metre opening belongs to the animated furnace rig, including its exposed core.
    }

    private static void Storm(EnvironmentBuilder b, float x, float z)
    {
        // Lightning collectors are sculptural, disconnected ruins; no decorative flash resembles an active warning.
        foreach (float side in new[] { -1f, 1f })
        {
            var p = new Vector3(side * x * .64f, 0, -z - 3.2f);
            float height = side < 0 ? 5.75f : 4.9f;
            b.Box(new(2.25f, .48f, 2f), p + Vector3.Up * .24f, Obsidian);
            b.Cylinder(.76f, .38f, height, p + Vector3.Up * (height * .5f + .4f), Iron);
            for (int i = 0; i < 5; i++)
                b.Torus(.41f, .69f - i * .035f, p + Vector3.Up * (1.04f + i * .81f), i % 2 == 0 ? Copper : CutStone);
            b.Beam(p + Vector3.Up * (height + .42f), p + new Vector3(-.86f, height + 1.37f, 0), .14f, IronEdge);
            b.Beam(p + Vector3.Up * (height + .42f), p + new Vector3(.86f, height + 1.37f, 0), .14f, IronEdge);
            b.Beam(p + Vector3.Up * (height + .4f), p + Vector3.Up * (height + 1.67f), .095f, Copper);
            b.Box(new(1.02f, 1.12f, .86f), p + new Vector3(side * 1.08f, .68f, .18f), Basalt);
            b.Box(new(.65f, .54f, .07f), p + new Vector3(side * 1.08f, .94f, .65f), Dark);
            BasaltCluster(b, new(side * (x + 2.35f), 0, -z * .48f), 2.35f);
            b.Box(new(.2f, .22f, z * .95f), new(side * (x + .92f), .11f, -.18f), Copper);
            for (int i = 0; i < 4; i++)
                b.Box(new(.48f, .12f, .42f), new(side * (x + .92f), .07f, -z * .47f + i * z * .31f), Basalt);
        }
        // An evacuated awning and empty supply cart show a place people used before the storm.
        var shelter = new Vector3(0, 0, -z - 2.55f);
        foreach (float side in new[] { -1f, 1f })
            b.Box(new(.18f, 2.8f, .18f), shelter + new Vector3(side * 1.25f, 1.4f, .32f), Iron);
        b.Box(new(2.95f, .09f, 1.45f), shelter + Vector3.Up * 2.78f, Cloth, new(-8, 0, 0));
        b.Box(new(1.44f, .38f, .85f), shelter + Vector3.Up * .6f, Rust);
        foreach (float side in new[] { -1f, 1f })
            b.Cylinder(.28f, .28f, .12f, shelter + new Vector3(side * .77f, .3f, 0), Dark, new(0, 0, 90));
        b.Box(new(.65f, .58f, .58f), shelter + new Vector3(-.2f, 1.08f, 0), Iron);
    }

    private static void Conveyor(EnvironmentBuilder b, Vector3 p, float length)
    {
        b.Box(new(1.38f, .52f, length), p + Vector3.Up * .44f, Iron);
        b.Box(new(1.06f, .06f, length - .1f), p + Vector3.Up * .73f, Dark);
        for (int i = 0; i < 15; i++)
        {
            float pz = -length * .46f + i * length * .92f / 14;
            b.Box(new(1.08f, .045f, .09f), p + new Vector3(0, .78f, pz), IronEdge);
            if (i % 4 == 0) b.Cylinder(.31f, .2f, .29f, p + new Vector3(.07f, .92f, pz), Coal);
        }
        foreach (float side in new[] { -1f, 1f })
        {
            b.Box(new(.11f, .2f, length + .05f), p + new Vector3(side * .65f, .86f, 0), Rust);
            for (int i = 0; i < 3; i++)
                b.Box(new(.18f, .55f, .26f), p + new Vector3(side * .58f, .28f, (i - 1) * length * .37f), Basalt);
        }
    }

    private static void Pump(EnvironmentBuilder b, Vector3 p, float height)
    {
        b.Box(new(1.95f, .24f, 1.86f), p + Vector3.Up * .12f, Obsidian);
        b.Cylinder(.74f, .66f, height, p + Vector3.Up * (height * .5f + .2f), Iron);
        for (int i = 0; i < 3; i++) b.Torus(.62f, .81f, p + Vector3.Up * (.4f + i * height * .36f), Rust);
        b.Cylinder(.69f, .47f, .28f, p + Vector3.Up * (height + .32f), IronEdge);
        b.Cylinder(.22f, .22f, .68f, p + Vector3.Up * (height + .8f), Rust);
        b.Box(new(.14f, height * .62f, .095f), p + new Vector3(0, height * .56f, .74f), Copper);
        b.Torus(.24f, .36f, p + new Vector3(0, height * .61f, .87f), Copper, new(90, 0, 0));
        b.Box(new(.06f, .54f, .055f), p + new Vector3(0, height * .61f, .91f), IronEdge);
        b.Box(new(.54f, .06f, .055f), p + new Vector3(0, height * .61f, .91f), IronEdge);
    }

    private static void Chimney(EnvironmentBuilder b, Vector3 p, float height)
    {
        b.Box(new(.71f, height, .74f), p + Vector3.Up * (height * .5f), Obsidian);
        for (int i = 0; i < 3; i++) b.Box(new(.77f, .12f, .8f), p + Vector3.Up * (.13f + i * height * .39f), CutStone);
        b.Box(new(.96f, .16f, .98f), p + Vector3.Up * (height + .08f), Iron);
        b.Box(new(.48f, .025f, .49f), p + Vector3.Up * (height + .174f), Dark);
    }

    private static void BasaltCluster(EnvironmentBuilder b, Vector3 p, float height)
    {
        for (int i = 0; i < 5; i++)
        {
            float a = i * Mathf.Tau / 5;
            float h = height * (.56f + (i * 7 % 5) * .11f);
            var foot = p + new Vector3(Mathf.Sin(a) * .52f, 0, Mathf.Cos(a) * .62f);
            b.Cylinder(.43f, .28f, h, foot + Vector3.Up * (h * .5f), i % 2 == 0 ? Obsidian : Basalt);
            b.Cylinder(.285f, .26f, .06f, foot + Vector3.Up * (h + .025f), CutStone);
        }
    }

    private static void Perimeter(EnvironmentBuilder b, float x, float z, bool storm)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            // The lava is a distant environmental source, never a decorative hazard inside playable bounds.
            b.Box(new(1.38f, .025f, z * 1.76f), new(side * (x + 3.8f), -.04f, 0), storm ? CoolingLava : Lava, glow: true);
            b.Box(new(.38f, .29f, z * 1.8f), new(side * (x + 2.98f), .025f, 0), Obsidian);
            b.Box(new(.38f, .29f, z * 1.8f), new(side * (x + 4.62f), .025f, 0), Basalt);
            // Broken cooled rafts interrupt the molten surface. Every corner stays within the outer canal, well off the floor.
            for (int i = 0; i < 14; i++)
            {
                int pattern = i * 7 % 5;
                float pz = -z * .8f + i * z * 1.6f / 13;
                float px = side * (x + 3.8f + (pattern - 2) * .055f);
                b.Box(new(.66f + pattern * .035f, .021f, .46f + pattern * .09f), new(px, -.015f, pz), pattern % 2 == 0 ? Obsidian : Coal, new(0, pattern * 6 - 12, 0));
                if (i % 3 == 0)
                    b.Box(new(.23f, .018f, .34f), new(px - side * .39f, -.016f, pz + .28f), Basalt, new(0, -17, 0));
            }
            for (int i = 0; i < 6; i++)
            {
                float pz = -z * .74f + i * z * .3f;
                b.Box(new(.3f, .2f, .62f), new(side * (x + .37f), .06f, pz), CutStone);
            }
            // No camera-facing railing: only ankle-height ash kerbs mark the foreground edge.
            for (int i = 0; i < 5; i++)
                b.Box(new(x * .16f, .16f, .34f), new(side * (x * .09f + i * x * .195f), .025f, z + .62f), i % 2 == 0 ? Basalt : CutStone);
        }
    }
}
