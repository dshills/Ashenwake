using Godot;

namespace Ashenwake.Client;

/// <summary>Orrun's inhabited skeleton frames the simulation rectangle without occupying its walkable floor.</summary>
public static class ShatteredSpineArt
{
    private const string Mountain = "475560";
    private const string MountainEdge = "697581";
    private const string Shadow = "2e3d49";
    private const string Bone = "9eabaf";
    private const string BoneEdge = "c2c8bc";
    private const string BoneShade = "7f908f";
    private const string OldIvory = "c8bd9b";
    private const string IvoryEdge = "e5d7b5";
    private const string Brass = "9c9070";
    private const string DarkBrass = "6d6c5d";
    private const string Cloth = "566878";
    private const string OldCloth = "b6a57a";
    private const string Hearth = "b58b52";

    public static void Build(Node3D parent, float halfWidth, float halfDepth, string encounterId)
    {
        var b = new EnvironmentBuilder(parent, "ShatteredSpineArchitecture");
        bool memory = encounterId == "exploration.first_oath";
        switch (encounterId)
        {
            case "campaign.contract_hall": ContractHall(b, halfWidth, halfDepth); break;
            case "campaign.covenant_warden": SealCourt(b, halfWidth, halfDepth); break;
            case "exploration.first_oath": FirstOath(b, halfWidth, halfDepth); break;
            default: Causeway(b, halfWidth, halfDepth); break;
        }
        Perimeter(b, halfWidth, halfDepth, memory);
        b.Flush();
    }

    private static void Causeway(EnvironmentBuilder b, float x, float z)
    {
        // A city excavated into vertebrae: lit dwellings and hanging cloth give the colossal bones human scale.
        foreach (float side in new[] { -1f, 1f })
        {
            BoneDwelling(b, new(side * x * .64f, 0, -z - 3.6f), 3.7f, side < 0 ? 4.7f : 5.3f);
            SideRib(b, side, x, -z * .56f, 4.6f, false);
            SideRib(b, side, x, z * .16f, 3.5f, false);
            MountainShard(b, new(side * (x + 3.9f), 0, -z * .62f), 5.5f);
            var p = new Vector3(side * (x + 1.04f), 0, z * .65f);
            b.Box(new(.9f, .37f, 1.2f), p + Vector3.Up * .185f, Mountain);
            b.Box(new(.62f, 1.85f, .75f), p + Vector3.Up * 1.29f, BoneShade);
            b.Box(new(.75f, .2f, .87f), p + Vector3.Up * 2.28f, BoneEdge);
            // The suspended oath chain belongs outside the bridge, never across a walkable path.
            for (int i = 0; i < 9; i++)
            {
                float pz = -z * .64f + i * z * 1.24f / 8;
                b.Torus(.105f, .15f, new(side * (x + .79f), .69f + MathF.Abs(i - 4) * .044f, pz), DarkBrass, new(0, 0, 90));
                if (i < 8) b.Beam(new(side * (x + .79f), .74f, pz), new(side * (x + .79f), .74f, pz + z * 1.24f / 8), .035f, DarkBrass);
            }
        }
        var law = new Vector3(0, 0, -z - 2.25f);
        b.Box(new(3.2f, .3f, 1.5f), law + Vector3.Up * .15f, Mountain);
        LawTablet(b, law + Vector3.Up * .3f, 2.42f, 3.2f, false);
        b.Box(new(2.8f, .21f, .85f), law + Vector3.Up * 3.59f, BoneEdge);
        b.Box(new(.16f, .67f, .12f), law + new Vector3(0, 3.9f, .37f), Brass);
        b.Box(new(.54f, .12f, .13f), law + new Vector3(0, 4.02f, .37f), Brass);
    }

    private static void BoneDwelling(EnvironmentBuilder b, Vector3 p, float width, float height)
    {
        b.Box(new(width + .45f, .4f, 2.8f), p + Vector3.Up * .2f, Mountain);
        b.Box(new(width, height, 2.35f), p + Vector3.Up * (height * .5f + .37f), BoneShade);
        b.Box(new(width * .8f, .45f, 2.6f), p + Vector3.Up * (height + .5f), Bone);
        b.Box(new(width * .48f, .24f, 2.25f), p + Vector3.Up * (height + .84f), BoneEdge);
        foreach (float side in new[] { -1f, 1f })
        {
            b.Cylinder(.43f, .33f, height + .3f, p + new Vector3(side * width * .44f, height * .5f + .45f, .99f), Bone);
            for (int floor = 0; floor < 3; floor++)
            {
                float py = 1.07f + floor * 1.27f;
                var window = p + new Vector3(side * width * .235f, py, 1.2f);
                b.Box(new(.64f, .78f, .08f), window, Shadow);
                b.Box(new(.42f, .54f, .035f), window + Vector3.Forward * -.06f, Hearth, glow: true);
                b.Box(new(.075f, .78f, .1f), window + Vector3.Forward * -.095f, DarkBrass);
                b.Box(new(.78f, .13f, .3f), window + new Vector3(0, -.46f, .06f), BoneEdge);
            }
        }
        for (int level = 0; level < 2; level++)
        {
            float py = .51f + level * 2.53f;
            b.Box(new(width * .92f, .2f, .72f), p + new Vector3(0, py, 1.37f), Bone);
            b.Box(new(width * .88f, .1f, .1f), p + new Vector3(0, py + .62f, 1.7f), DarkBrass);
            for (int rail = 0; rail < 7; rail++)
                b.Box(new(.065f, .62f, .07f), p + new Vector3((rail - 3) * width * .13f, py + .31f, 1.7f), DarkBrass);
        }
        b.Box(new(.57f, 1.42f, .06f), p + new Vector3(width * .13f, height - .33f, 1.34f), Cloth);
        b.Box(new(.13f, 1.15f, .025f), p + new Vector3(width * .13f, height - .33f, 1.383f), Brass);
        // A rib curves over the rear facade, its feet wholly behind the playable edge.
        RearRib(b, p + new Vector3(0, 0, -.2f), width * .71f, height + 1.45f, BoneEdge);
    }

    private static void ContractHall(EnvironmentBuilder b, float x, float z)
    {
        // An open tribunal with tiered stone records. Nothing in the facade suggests another usable exit.
        var p = new Vector3(0, 0, -z - 3.6f);
        b.Box(new(x * 1.64f, .38f, 3.9f), p + Vector3.Up * .19f, Mountain);
        b.Box(new(x * 1.5f, .28f, 3.35f), p + Vector3.Up * .52f, BoneShade);
        LawTablet(b, p + new Vector3(0, .65f, -.55f), 3.5f, 5.4f, false);
        b.Box(new(4.14f, .3f, .96f), p + new Vector3(0, 6.2f, -.55f), BoneEdge);
        b.Box(new(2.25f, .18f, .21f), p + new Vector3(0, 6.52f, -.07f), Brass);
        foreach (float side in new[] { -1f, 1f })
        {
            for (int i = 0; i < 3; i++)
            {
                float px = side * (2.96f + i * 2.16f);
                float height = 4.65f - i * .48f;
                LawTablet(b, p + new Vector3(px, .66f, -.32f - i * .12f), 1.35f, height, false);
                b.Box(new(1.82f, .23f, 1.3f), p + new Vector3(px, .8f, .58f), Bone);
                b.Box(new(1.68f, .58f, .6f), p + new Vector3(px, 1.2f, .58f), Mountain);
                b.Box(new(1.8f, .17f, .81f), p + new Vector3(px, 1.57f, .58f), BoneEdge);
            }
            for (int i = 0; i < 3; i++)
            {
                float pz = -z * .64f + i * z * .59f;
                var post = new Vector3(side * (x + 1.23f), 0, pz);
                RibPillar(b, post, 4.35f - i * .63f, false);
                b.Box(new(.06f, 1.65f, .83f), post + new Vector3(-side * .44f, 2.52f - i * .32f, .21f), Cloth);
                b.Box(new(.08f, 1.39f, .11f), post + new Vector3(-side * .49f, 2.52f - i * .32f, .21f), Brass);
            }
        }
        // The witness desk and its shut volumes are scenery beyond the north boundary.
        b.Box(new(3.0f, .83f, .68f), new(0, .415f, -z - 1.01f), Mountain);
        b.Box(new(3.28f, .17f, .94f), new(0, .915f, -z - 1.01f), Bone);
        for (int i = 0; i < 3; i++)
        {
            b.Box(new(.6f, .12f, .47f), new((i - 1) * .87f, 1.06f, -z - 1.03f), DarkBrass);
            b.Box(new(.54f, .09f, .42f), new((i - 1) * .87f, 1.16f, -z - 1.03f), BoneEdge);
        }
    }

    private static void SealCourt(EnvironmentBuilder b, float x, float z)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            // The center seven metres belong exclusively to the animated Covenant seal rig.
            for (int i = 0; i < 3; i++)
            {
                float px = side * (4.4f + i * 2.2f);
                var p = new Vector3(px, 0, -z - 3.7f - i * .3f);
                float height = 5.5f - i * .7f;
                RibPillar(b, p, height, false);
                b.Box(new(.43f, height * .53f, .08f), p + new Vector3(0, height * .55f, .67f), Shadow);
                Carving(b, p + new Vector3(0, height * .56f, .724f), .29f, height * .43f, Brass, 6);
                b.Cylinder(.31f, .26f, .38f, p + Vector3.Up * (height + .59f), Brass);
                if (i < 2)
                {
                    float nextX = side * (4.4f + (i + 1) * 2.2f);
                    b.Beam(p + new Vector3(0, height - .43f, -.14f), new(nextX, height - 1.13f, p.Z - .3f), .27f, BoneShade);
                }
            }
            SideRib(b, side, x, -z * .51f, 5.0f, false);
            SideRib(b, side, x, z * .31f, 3.2f, false);
            var law = new Vector3(side * (x + 1.8f), 0, z * .65f);
            LawTablet(b, law, 1.4f, 1.6f, false);
            MountainShard(b, new(side * (x + 3.6f), 0, -z * .62f), 6.5f);
        }
    }

    private static void FirstOath(EnvironmentBuilder b, float x, float z)
    {
        // The intact founding sanctuary precedes today's coercive contracts: open ivory, woven cloth and an unbroken law.
        var p = new Vector3(0, 0, -z - 3.25f);
        b.Box(new(x * 1.65f, .36f, 3.0f), p + Vector3.Up * .18f, OldIvory);
        b.Box(new(x * 1.5f, .21f, 2.64f), p + Vector3.Up * .465f, IvoryEdge);
        LawTablet(b, p + new Vector3(0, .57f, -.25f), 3.0f, 3.95f, true);
        RearRib(b, p + new Vector3(0, .56f, -.63f), 3.5f, 5.8f, IvoryEdge);
        b.Cylinder(.7f, .7f, .12f, p + new Vector3(0, 5.02f, -.1f), Brass, new(90, 0, 0));
        b.Cylinder(.47f, .47f, .14f, p + new Vector3(0, 5.02f, .0f), OldIvory, new(90, 0, 0));
        b.Box(new(.1f, .83f, .07f), p + new Vector3(0, 5.02f, .11f), Brass);
        b.Box(new(.51f, .1f, .075f), p + new Vector3(0, 5.12f, .115f), Brass);
        foreach (float side in new[] { -1f, 1f })
        {
            for (int i = 0; i < 3; i++)
            {
                var post = new Vector3(side * (x * .52f + i * 1.67f), 0, -z - 3.03f);
                RibPillar(b, post, 4.2f - i * .53f, true);
                b.Box(new(.66f, 1.64f, .04f), post + new Vector3(0, 2.55f - i * .2f, .67f), OldCloth);
                b.Box(new(.12f, 1.35f, .06f), post + new Vector3(0, 2.55f - i * .2f, .71f), IvoryEdge);
            }
            SideRib(b, side, x, -z * .46f, 4.7f, true);
            SideRib(b, side, x, z * .24f, 3.65f, true);
            var bench = new Vector3(side * (x + 1.13f), 0, z * .65f);
            b.Box(new(.8f, .25f, 2.26f), bench + Vector3.Up * .69f, IvoryEdge);
            foreach (float end in new[] { -1f, 1f })
                b.Box(new(.54f, .57f, .35f), bench + new Vector3(0, .285f, end * .8f), OldIvory);
        }
    }

    private static void LawTablet(EnvironmentBuilder b, Vector3 p, float width, float height, bool memory)
    {
        b.Box(new(width, height, .64f), p + Vector3.Up * (height * .5f), memory ? OldIvory : Bone);
        b.Box(new(width * .83f, height * .83f, .04f), p + new Vector3(0, height * .51f, .341f), memory ? IvoryEdge : BoneShade);
        foreach (float side in new[] { -1f, 1f })
            b.Box(new(width * .045f, height * .89f, .055f), p + new Vector3(side * width * .43f, height * .51f, .365f), memory ? Brass : BoneEdge);
        Carving(b, p + new Vector3(0, height * .51f, .379f), width * .64f, height * .72f, memory ? DarkBrass : Shadow, 9);
        b.Box(new(width * .8f, .055f, .04f), p + new Vector3(0, height * .89f, .39f), Brass);
    }

    private static void Carving(EnvironmentBuilder b, Vector3 p, float width, float height, string color, int rows)
    {
        // Broken strokes suggest inscribed law at game distance without pretending to be readable interface text.
        for (int row = 0; row < rows; row++)
        {
            float py = -height * .45f + row * height * .9f / (rows - 1);
            for (int col = 0; col < 4; col++)
            {
                float px = (col - 1.5f) * width * .25f;
                float length = width * (.125f + (row + col * 3) % 3 * .025f);
                b.Box(new(length, .035f, .018f), p + new Vector3(px, py, 0), color);
                if ((row + col) % 3 != 0) b.Box(new(.03f, height / rows * .32f, .019f), p + new Vector3(px + length * .33f, py + height / rows * .12f, .003f), color);
            }
        }
    }

    private static void RibPillar(EnvironmentBuilder b, Vector3 p, float height, bool memory)
    {
        string bone = memory ? OldIvory : Bone, edge = memory ? IvoryEdge : BoneEdge;
        b.Box(new(1.34f, .35f, 1.4f), p + Vector3.Up * .175f, memory ? OldIvory : Mountain);
        b.Cylinder(.49f, .37f, height, p + Vector3.Up * (height * .5f + .3f), bone);
        b.Box(new(.36f, height * .81f, .16f), p + new Vector3(0, height * .5f + .38f, .42f), edge);
        for (int i = 0; i < 3; i++)
            b.Box(new(.86f - i * .08f, .16f, .89f - i * .08f), p + Vector3.Up * (.63f + i * height * .36f), edge);
        b.Box(new(1.06f, .29f, 1.13f), p + Vector3.Up * (height + .39f), edge);
    }

    private static void SideRib(EnvironmentBuilder b, float side, float x, float pz, float height, bool memory)
    {
        string bone = memory ? OldIvory : Bone, edge = memory ? IvoryEdge : BoneEdge;
        var foot = new Vector3(side * (x + 1.0f), .2f, pz);
        b.Box(new(1.14f, .4f, 1.8f), foot, memory ? OldIvory : Mountain);
        // These ribs curve outward from the bridge, so the whole silhouette stays beyond its boundary.
        var points = new[]
        {
            foot + new Vector3(0, .04f, 0),
            foot + new Vector3(side * .12f, height * .27f, 0),
            foot + new Vector3(side * .62f, height * .56f, -.1f),
            foot + new Vector3(side * 1.45f, height * .8f, -.21f),
            foot + new Vector3(side * 2.5f, height * .95f, -.35f)
        };
        for (int i = 0; i < points.Length - 1; i++)
        {
            b.Beam(points[i], points[i + 1], .55f - i * .08f, bone);
            b.Beam(points[i] + new Vector3(-side * .1f, 0, .24f), points[i + 1] + new Vector3(-side * .1f, 0, .24f), .12f, edge);
        }
        b.Box(new(.66f, .2f, .87f), foot + new Vector3(side * .11f, height * .24f, 0), Brass);
    }

    private static void RearRib(EnvironmentBuilder b, Vector3 p, float halfSpan, float height, string color)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            var points = new[]
            {
                p + new Vector3(side * halfSpan, .25f, 0),
                p + new Vector3(side * halfSpan * .98f, height * .42f, 0),
                p + new Vector3(side * halfSpan * .74f, height * .76f, 0),
                p + new Vector3(side * halfSpan * .33f, height * .94f, 0),
                p + new Vector3(0, height, 0)
            };
            for (int i = 0; i < points.Length - 1; i++) b.Beam(points[i], points[i + 1], .4f - i * .035f, color);
        }
    }

    private static void MountainShard(EnvironmentBuilder b, Vector3 p, float height)
    {
        b.Cylinder(1.55f, .21f, height, p + Vector3.Up * (height * .5f), Mountain);
        b.Cylinder(.93f, .18f, height * .7f, p + new Vector3(.67f, height * .35f, .6f), MountainEdge);
        b.Cylinder(.76f, .07f, height * .58f, p + new Vector3(-.76f, height * .29f, .81f), Mountain);
    }

    private static void Perimeter(EnvironmentBuilder b, float x, float z, bool memory)
    {
        // Low bridge copings frame the true boundary; the foreground remains short for the isometric camera.
        foreach (float side in new[] { -1f, 1f })
        {
            b.Box(new(.39f, .22f, z * 2 + .5f), new(side * (x + .5f), .11f, 0), memory ? OldIvory : BoneShade);
            for (int i = 0; i < 10; i++)
            {
                float pz = -z * .94f + i * z * 1.88f / 9;
                b.Box(new(.48f, .31f, .28f), new(side * (x + .5f), .155f, pz), memory ? IvoryEdge : Bone);
            }
            for (int i = 0; i < 15; i++)
            {
                float px = -x * .96f + i * x * 1.92f / 14;
                b.Box(new(x * 1.92f / 15 - .055f, .14f, .31f), new(px, .07f, side * (z + .52f)), memory ? OldIvory : BoneShade);
            }
        }
    }
}
