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
            case "exploration.oathkeeper_archive": SpineExplorationArt.BuildArchive(b, halfWidth, halfDepth); break;
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
            var column = p + new Vector3(side * width * .44f, .30f, .99f);
            b.Branch(column, column + new Vector3(-side * .08f, height * .57f, .045f), .43f, .32f, Bone, SurfaceKind.Bone);
            b.Branch(column + new Vector3(-side * .08f, height * .57f, .045f), column + new Vector3(-side * .04f, height + .3f, -.025f), .32f, .24f, Bone, SurfaceKind.Bone);
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
        FoldedCloth(b, p + new Vector3(width * .13f, height - .33f, 1.34f), .57f, 1.42f, false);
        // The dwelling's crown retains the transverse processes of an excavated
        // vertebra instead of looking like a stack of conventional roof tiles.
        foreach (float side in new[] { -1f, 1f })
            b.Branch(p + new Vector3(side * width * .14f, height + .60f, .1f),
                p + new Vector3(side * width * .45f, height + .76f, .25f), .31f, .12f, BoneEdge, SurfaceKind.Bone);
        b.Branch(p + new Vector3(0, height + .59f, -.61f), p + new Vector3(0, height + 1.18f, -.12f), .27f, .045f, Bone, SurfaceKind.Bone);
        Weathering(b, p + new Vector3(-width * .39f, height * .67f, 1.212f), height * .23f);
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
                FoldedCloth(b, post + new Vector3(-side * .44f, 2.52f - i * .32f, .21f), .83f, 1.65f, false, -side * 90);
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
                    var joint = p + new Vector3(0, height - .43f, -.14f);
                    var end = new Vector3(nextX, height - 1.13f, p.Z - .3f);
                    var middle = joint.Lerp(end, .5f) + Vector3.Down * .18f;
                    b.Branch(joint, middle, .19f, .14f, BoneShade, SurfaceKind.Bone);
                    b.Branch(middle, end, .14f, .11f, BoneShade, SurfaceKind.Bone);
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
                FoldedCloth(b, post + new Vector3(0, 2.55f - i * .2f, .67f), .66f, 1.64f, true);
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
        // The broad face sits behind a substantial lip. The cast shadow supplies
        // depth to the inscriptions without giving them an interface-like glow.
        b.Box(new(width, height, .50f), p + new Vector3(0, height * .5f, -.07f), memory ? OldIvory : Bone);
        b.Box(new(width * .83f, height * .83f, .065f), p + new Vector3(0, height * .51f, .2225f), memory ? IvoryEdge : BoneShade);
        foreach (float side in new[] { -1f, 1f })
        {
            if (memory)
                b.Box(new(width * .075f, height * .89f, .19f), p + new Vector3(side * width * .455f, height * .51f, .31f), OldIvory);
            else
            {
                b.Box(new(width * .075f, height * .54f, .19f), p + new Vector3(side * width * .455f, height * .335f, .31f), BoneEdge);
                b.Box(new(width * .071f, height * .33f, .18f), p + new Vector3(side * (width * .455f + .013f), height * .79f, .31f), BoneEdge, new(0, 0, -side));
            }
        }
        foreach (float end in new[] { -1f, 1f })
            b.Box(new(width * .91f, height * .055f, .20f), p + new Vector3(0, height * (.51f + end * .43f), .31f), memory ? OldIvory : BoneEdge);
        Carving(b, p + new Vector3(0, height * .51f, .266f), width * .64f, height * .72f, memory ? DarkBrass : Shadow, 9);
        b.Box(new(width * .8f, .055f, .022f), p + new Vector3(0, height * .89f, .275f), Brass);
        if (!memory) Weathering(b, p + new Vector3(width * .39f, height * .32f, .268f), height * .21f);
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
        var basePoint = p + Vector3.Up * .30f;
        var shoulder = p + new Vector3(.025f, height * .56f + .3f, -.06f);
        var crown = p + new Vector3(0, height + .3f, -.015f);
        b.Branch(basePoint, shoulder, .47f, .34f, bone, SurfaceKind.Bone);
        b.Branch(shoulder, crown, .34f, .275f, bone, SurfaceKind.Bone);
        b.Branch(basePoint + new Vector3(0, .08f, .39f), shoulder + new Vector3(0, 0, .30f), .10f, .07f, edge, SurfaceKind.Bone);
        b.Branch(shoulder + new Vector3(0, 0, .30f), crown + new Vector3(0, -.13f, .245f), .07f, .04f, edge, SurfaceKind.Bone);
        for (int i = 0; i < 3; i++)
            Vertebra(b, p + Vector3.Up * (.63f + i * height * .36f), 1.0f - i * .08f, .20f, memory);
        Vertebra(b, p + Vector3.Up * (height + .39f), 1.1f, .30f, memory);
        if (!memory) Weathering(b, p + new Vector3(-.15f, height * .40f, .39f), height * .20f);
    }

    private static void SideRib(EnvironmentBuilder b, float side, float x, float pz, float height, bool memory)
    {
        string bone = memory ? OldIvory : Bone, edge = memory ? IvoryEdge : BoneEdge;
        var foot = new Vector3(side * (x + 1.0f), .2f, pz);
        b.Box(new(1.14f, .4f, 1.8f), foot, memory ? OldIvory : Mountain);
        // These ribs curve outward from the bridge, so the whole silhouette stays beyond its boundary.
        var start = foot + new Vector3(0, .04f, 0);
        var control = foot + new Vector3(side * .12f, height * .70f, -.05f);
        var end = foot + new Vector3(side * 2.5f, height * .95f, -.35f);
        Vector3 Point(float t) => start.Lerp(control, t).Lerp(control.Lerp(end, t), t);
        const int segments = 8;
        float extent = memory ? 1 : .93f;
        for (int i = 0; i < segments; i++)
        {
            float t0 = i * extent / segments, t1 = (i + 1) * extent / segments;
            float r0 = Mathf.Lerp(.34f, .045f, t0), r1 = Mathf.Lerp(.34f, .045f, t1);
            b.Branch(Point(t0), Point(t1), r0, r1, bone, SurfaceKind.Bone);
            b.Branch(Point(t0) + new Vector3(-side * .06f, 0, r0 * .85f), Point(t1) + new Vector3(-side * .06f, 0, r1 * .85f),
                r0 * .21f, r1 * .21f, edge, SurfaceKind.Bone);
        }
        if (!memory)
        {
            var tip = Point(extent);
            var direction = (tip - Point(extent - .04f)).Normalized();
            b.Branch(tip - direction * .015f, tip + direction * .018f, .071f, .061f, BoneShade, SurfaceKind.Bone);
            Weathering(b, Point(.34f) + new Vector3(0, 0, .24f), height * .13f);
        }
        b.Box(new(.66f, .2f, .87f), foot + new Vector3(side * .11f, height * .24f, 0), Brass);
    }

    private static void RearRib(EnvironmentBuilder b, Vector3 p, float halfSpan, float height, string color)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            var start = p + new Vector3(side * halfSpan, .25f, 0);
            var control = p + new Vector3(side * halfSpan, height * .93f, -.045f);
            var end = p + new Vector3(0, height, 0);
            Vector3 Point(float t) => start.Lerp(control, t).Lerp(control.Lerp(end, t), t);
            for (int i = 0; i < 8; i++)
            {
                float t0 = i / 8f, t1 = (i + 1) / 8f;
                b.Branch(Point(t0), Point(t1), Mathf.Lerp(.27f, .10f, t0), Mathf.Lerp(.27f, .10f, t1), color, SurfaceKind.Bone);
            }
        }
    }

    internal static void Vertebra(EnvironmentBuilder b, Vector3 p, float width, float thickness, bool memory)
    {
        string bone = memory ? OldIvory : Bone, edge = memory ? IvoryEdge : BoneEdge;
        b.Cylinder(width * .43f, width * .35f, thickness, p, edge);
        foreach (float side in new[] { -1f, 1f })
        {
            float reach = !memory && side < 0 ? .51f : .64f;
            b.Branch(p + new Vector3(side * width * .27f, 0, -.025f),
                p + new Vector3(side * width * reach, thickness * .22f, -.11f), thickness * .46f, thickness * .17f, bone, SurfaceKind.Bone);
        }
        b.Branch(p + new Vector3(0, 0, width * .23f), p + new Vector3(0, thickness * .17f, width * .52f),
            thickness * .50f, thickness * .13f, edge, SurfaceKind.Bone);
    }

    internal static void FoldedCloth(EnvironmentBuilder b, Vector3 center, float width, float height, bool memory, float yawDegrees = 0)
    {
        const int folds = 6;
        var basis = Basis.FromEuler(Vector3.Up * Mathf.DegToRad(yawDegrees));
        for (int strip = 0; strip < folds; strip++)
        {
            float u0 = strip / (float)folds, u1 = (strip + 1) / (float)folds;
            float z0 = .045f * Mathf.Sin(u0 * Mathf.Tau * 2 + .35f), z1 = .045f * Mathf.Sin(u1 * Mathf.Tau * 2 + .35f);
            var across = new Vector3(width / folds, 0, z1 - z0);
            float stripHeight = memory ? height : height * (1 - strip % 3 * .025f);
            var local = new Vector3((u0 + u1 - 1) * width * .5f, (height - stripHeight) * .5f, (z0 + z1) * .5f);
            float angle = yawDegrees - Mathf.RadToDeg(Mathf.Atan2(across.Z, across.X));
            b.Box(new(across.Length() + .003f, stripHeight, .022f), center + basis * local, memory ? OldCloth : Cloth, new(0, angle, 0));
            if (strip == 2)
                b.Box(new(Math.Min(across.Length() * .55f, .10f), height * .82f, .012f),
                    center + basis * (local + new Vector3(0, .025f, .025f)), memory ? IvoryEdge : Brass, new(0, angle, 0));
        }
    }

    internal static void Weathering(EnvironmentBuilder b, Vector3 p, float height)
    {
        // A few broad, broken seams read as age from the gameplay camera. The
        // original ivory in the First Oath never receives these present-day scars.
        var split = p + new Vector3(.055f, height * .12f, .004f);
        b.Branch(p + new Vector3(-.025f, -height * .40f, 0), split, .018f, .013f, Shadow, SurfaceKind.Stone);
        b.Branch(split, p + new Vector3(-.027f, height * .47f, 0), .013f, .006f, Shadow, SurfaceKind.Stone);
        b.Branch(split, split + new Vector3(.12f, height * .17f, 0), .012f, .003f, Shadow, SurfaceKind.Stone);
    }

    private static void MountainShard(EnvironmentBuilder b, Vector3 p, float height)
    {
        var split = p + new Vector3(.14f, height * .60f, -.10f);
        b.Branch(p, split, 1.55f, .65f, Mountain, SurfaceKind.Stone);
        b.Branch(split + Vector3.Up * .065f, p + new Vector3(.28f, height, -.23f), .59f, .18f, Mountain, SurfaceKind.Stone);
        b.Cylinder(.93f, .18f, height * .7f, p + new Vector3(.67f, height * .35f, .6f), MountainEdge, new(0, 0, -7));
        b.Cylinder(.76f, .07f, height * .58f, p + new Vector3(-.76f, height * .29f, .81f), Mountain);
        b.Cylinder(.39f, .11f, .65f, p + new Vector3(.68f, .30f, 1.20f), MountainEdge, new(0, 17, -12));
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
                float wear = memory ? 0 : i * 7 % 5 * .012f;
                b.Box(new(x * 1.92f / 15 - .055f - wear, .14f - wear * .5f, .31f),
                    new(px, .07f - wear * .25f, side * (z + .52f)), memory ? OldIvory : BoneShade,
                    new(0, 0, memory ? 0 : (i % 3 - 1) * 2));
            }
        }
    }
}
