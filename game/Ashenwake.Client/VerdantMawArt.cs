using Godot;

namespace Ashenwake.Client;

/// <summary>Ilyra's inhabited, anatomical jungle. Every raised prop stays outside the simulation rectangle.</summary>
public static class VerdantMawArt
{
    private const string Bark = "414b36";
    private const string BarkLight = "64704a";
    private const string Root = "73664b";
    private const string Moss = "526844";
    private const string Leaf = "466653";
    private const string LeafLight = "6b8058";
    private const string LeafDark = "304d43";
    private const string Bone = "a69d77";
    private const string BoneShade = "7d8161";
    private const string Stone = "647267";
    private const string StoneLight = "8c947d";
    private const string StoneDark = "434e48";
    private const string Wood = "655944";
    private const string Reed = "9a9470";
    private const string Cloth = "698d80";
    private const string Pod = "82665e";
    private const string Sap = "b1b576";

    public static void Build(Node3D parent, float halfWidth, float halfDepth, string encounterId)
    {
        var b = new EnvironmentBuilder(parent, "VerdantMawArchitecture");
        float x = halfWidth, z = halfDepth;
        switch (encounterId)
        {
            case "campaign.plague_village": Village(b, x, z); break;
            case "campaign.rootheart": Rootheart(b, x, z); break;
            case "exploration.antler_hunt": AntlerGrove(b, x, z); break;
            case "exploration.briar_shrine": VerdantExplorationArt.BuildShrine(b, x, z); break;
            default: LivingRuins(b, x, z); break;
        }
        Perimeter(b, x, z, encounterId == "campaign.plague_village");
        b.Flush();
    }

    private static void LivingRuins(EnvironmentBuilder b, float x, float z)
    {
        // The temple has become an organ: roots bind its opened columns and a fern grows through its roof.
        float back = -z - 2.8f;
        RuinArch(b, new(-x * .54f, 0, back), 4.1f, 4.35f, false);
        RuinArch(b, new(x * .56f, 0, back - .6f), 4.8f, 5.2f, true);
        b.Box(new(3.3f, .38f, 1.15f), new(.1f, .19f, back - .25f), StoneDark);
        b.Box(new(2.65f, .22f, 1f), new(.1f, .47f, back - .25f), StoneLight);
        b.Cylinder(.68f, .42f, 1.28f, new(.1f, 1.19f, back - .25f), BoneShade);
        b.Torus(.35f, .52f, new(.1f, 1.85f, back - .25f), Bone);
        Fern(b, new(.1f, 1.88f, back - .25f), 1.55f);
        Tree(b, new(-x - 2.75f, 0, -z * .5f), 4.6f, -1);
        Tree(b, new(x + 2.85f, 0, -z * .53f), 5.15f, 1);
        InsectMolt(b, new(-x - 2.55f, 1.72f, -z * .5f + .45f));
        Flower(b, new(x * .2f, 0, -z - 1.4f), .78f);
        b.Box(new(1.25f, .35f, .75f), new(-x * .2f, .18f, -z - 1.2f), Stone, new(0, -14, 0));
        b.Box(new(.7f, .23f, .6f), new(-x * .2f + .32f, .47f, -z - 1.3f), StoneLight, new(0, 22, 0));
    }

    private static void RuinArch(EnvironmentBuilder b, Vector3 p, float width, float height, bool broken)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            var foot = p + new Vector3(side * width * .43f, 0, 0);
            b.Box(new(1.15f, .34f, 1.24f), foot + Vector3.Up * .17f, StoneDark);
            float columnHeight = broken && side > 0 ? height * .68f : height;
            b.Cylinder(.44f, .33f, columnHeight, foot + Vector3.Up * (columnHeight * .5f + .3f), Stone);
            for (int course = 0; course < 4; course++)
                b.Box(new(.8f, .13f, .87f), foot + Vector3.Up * (.55f + course * columnHeight * .22f), StoneLight);
            b.Box(new(1.15f, .3f, 1.15f), foot + Vector3.Up * (columnHeight + .35f), StoneLight);
            var root = foot + new Vector3(-side * .3f, .1f, .49f);
            var middle = foot + new Vector3(side * .39f, columnHeight * .5f, .47f);
            b.Branch(root, middle, .16f, .105f, Root);
            b.Branch(middle, foot + new Vector3(-side * .2f, columnHeight + .55f, .4f), .105f, .033f, BarkLight);
            LeafSpray(b, foot + new Vector3(side * .17f, columnHeight * .66f, .42f), side * .75f, .9f);
        }
        if (!broken)
        {
            b.Box(new(width + 1f, .48f, 1.1f), p + Vector3.Up * (height + .64f), Stone);
            b.Box(new(width + 1.15f, .12f, 1.24f), p + Vector3.Up * (height + .92f), StoneLight);
            var fallenRoot = p + new Vector3(-width * .07f, height + 1.13f, .34f);
            b.Branch(p + new Vector3(-width * .57f, height + 1.02f, .4f), fallenRoot, .14f, .09f, Bark);
            b.Branch(fallenRoot, p + new Vector3(width * .45f, height + .94f, .42f), .09f, .026f, BarkLight);
            Fern(b, p + new Vector3(-.5f, height + 1.06f, -.12f), .98f);
        }
        else
        {
            b.Box(new(width * .52f, .43f, 1.05f), p + new Vector3(-width * .23f, height + .55f, 0), Stone, new(0, 0, -5));
            b.Box(new(1.35f, .52f, .9f), p + new Vector3(.6f, .27f, .22f), StoneLight, new(0, 18, 0));
            b.Branch(p + new Vector3(-width * .5f, height + .9f, -.3f), p + new Vector3(.6f, height + 1.7f, -.2f), .18f, .055f, Bark);
            LeafSpray(b, p + new Vector3(.6f, height + 1.7f, -.2f), 1.4f, .82f);
        }
    }

    private static void Village(EnvironmentBuilder b, float x, float z)
    {
        // A settlement adapting to the organism: raised sleeping places, isolated beds and sap collectors.
        Shelter(b, new(-x * .59f, 0, -z - 2.8f), 3.5f, 2.8f, false);
        Shelter(b, new(x * .57f, 0, -z - 3.15f), 4.1f, 3.4f, true);
        QuarantineScreen(b, new(-.8f, 0, -z - 1.35f));
        QuarantineScreen(b, new(1.3f, 0, -z - 1.6f));
        Tree(b, new(-x - 2.8f, 0, -z * .56f), 4.3f, -1);
        Tree(b, new(x + 2.85f, 0, -z * .4f), 4.65f, 1);
        for (int i = 0; i < 3; i++)
        {
            var p = new Vector3(-x * .16f + i * .9f, 0, -z - 3.4f);
            b.Cylinder(.29f, .39f, .59f, p + Vector3.Up * .3f, Wood);
            b.Torus(.31f, .41f, p + Vector3.Up * .6f, Reed);
            b.Cylinder(.31f, .31f, .018f, p + Vector3.Up * .58f, LeafDark);
            b.Beam(p + new Vector3(-.26f, .85f, -.4f), p + new Vector3(.13f, .76f, 0), .065f, Bone);
        }
        b.Box(new(.42f, .18f, z * .8f), new(x + 1.4f, .12f, -z * .14f), Wood);
        b.Box(new(.25f, .2f, z * .8f), new(x + 1.4f, .14f, -z * .14f), LeafDark);
        for (int i = 0; i < 3; i++) Flower(b, new(x + 1.42f, .3f, -z * .38f + i * 1.5f), .4f);
    }

    private static void Shelter(EnvironmentBuilder b, Vector3 p, float width, float height, bool sickroom)
    {
        const float depth = 2.3f;
        foreach (float side in new[] { -1f, 1f })
        {
            b.Branch(p + new Vector3(side * width * .44f, 0, .8f), p + new Vector3(side * width * .44f, height + .08f, .8f), .12f, .068f, Root);
            b.Branch(p + new Vector3(side * width * .44f, 0, -.8f), p + new Vector3(side * width * .44f, height + .08f, -.8f), .13f, .075f, Bark);
            b.Beam(p + new Vector3(side * width * .44f, .45f, .81f), p + new Vector3(0, 1.03f, .81f), .12f, Wood);
        }
        b.Box(new(width, .19f, depth), p + Vector3.Up * 1.03f, Wood);
        b.Box(new(width, height - 1.1f, .11f), p + new Vector3(0, (height + 1.1f) * .5f, -.95f), LeafDark);
        b.Box(new(.1f, height - 1.1f, depth - .25f), p + new Vector3(-width * .48f, (height + 1.1f) * .5f, -.03f), Wood);
        b.Box(new(.1f, height - 1.1f, depth - .25f), p + new Vector3(width * .48f, (height + 1.1f) * .5f, -.03f), Wood);
        // Overlapping palm blades follow the ridge-to-eave fall, leaving individually
        // pointed edges instead of a continuous stack of rectangular green roof boards.
        for (int row = 0; row < 7; row++)
        {
            float along = -.99f + row * .33f;
            foreach (float side in new[] { -1f, 1f })
            {
                var ridge = p + new Vector3(-side * .045f, height + .70f + row % 2 * .015f, along);
                var direction = new Vector3(side, -.29f, (row % 3 - 1) * .025f);
                Blade(b, ridge, direction, width * (.575f + row % 2 * .018f), .66f, .11f,
                    row % 3 == 0 ? LeafLight : row % 3 == 1 ? Leaf : LeafDark, side * 4);
                b.Branch(ridge + Vector3.Up * .012f, ridge + direction.Normalized() * (width * .51f), .022f, .006f, BarkLight);
            }
        }
        b.Beam(p + new Vector3(-width * .6f, height + .15f, 1.38f), p + new Vector3(0, height + .76f, 1.38f), .12f, BarkLight);
        b.Beam(p + new Vector3(width * .6f, height + .15f, 1.38f), p + new Vector3(0, height + .76f, 1.38f), .12f, BarkLight);
        b.Box(new(width * .4f, height - 1.36f, .07f), p + new Vector3(-width * .26f, (height + 1.36f) * .5f, 1.12f), sickroom ? Cloth : Reed);
        b.Box(new(.95f, .12f, 1.65f), p + new Vector3(width * .21f, 1.21f, 0), Reed);
        b.Box(new(.78f, .1f, .35f), p + new Vector3(width * .21f, 1.32f, -.53f), Cloth);
        // Small palm-shaped community mark, distinct from the cross-like quarantine bindings.
        for (int i = -2; i <= 2; i++)
            b.Beam(p + new Vector3(-width * .27f, 1.73f, 1.18f), p + new Vector3(-width * .27f + i * .09f, 1.96f - Math.Abs(i) * .025f, 1.18f), .035f, Bone);
        for (int step = 0; step < 3; step++)
            b.Box(new(.82f, .2f, .29f), p + new Vector3(width * .25f, .22f + step * .27f, 1.73f - step * .26f), Root);
    }

    private static void QuarantineScreen(EnvironmentBuilder b, Vector3 p)
    {
        b.Box(new(1.25f, 1.35f, .065f), p + Vector3.Up * 1.5f, Cloth);
        foreach (float side in new[] { -1f, 1f })
            b.Beam(p + new Vector3(side * .69f, 0, 0), p + new Vector3(side * .69f, 2.35f, 0), .09f, Root);
        b.Beam(p + new Vector3(-.8f, 2.17f, 0), p + new Vector3(.8f, 2.17f, 0), .085f, Reed);
        b.Beam(p + new Vector3(-.23f, 1.23f, .065f), p + new Vector3(.23f, 1.77f, .065f), .055f, Bone);
        b.Beam(p + new Vector3(.23f, 1.23f, .065f), p + new Vector3(-.23f, 1.77f, .065f), .055f, Bone);
    }

    private static void Rootheart(EnvironmentBuilder b, float x, float z)
    {
        // Leave the central four-metre radius for the animated heart. Its apse is a dissected rib cage.
        foreach (float side in new[] { -1f, 1f })
        {
            for (int i = 0; i < 3; i++)
            {
                float px = side * (4.9f + i * 1.8f), back = -z - 3.7f - i * .72f;
                float height = 6.1f - i * .5f;
                Vector3 a = new(px + side * 1.02f, .02f, back + .18f);
                Vector3 c = new(px, height * .57f, back - .05f);
                Vector3 tip = new(px - side * .02f, height, back - 1.25f);
                b.Branch(a, c, .32f - i * .025f, .25f - i * .02f, BoneShade, SurfaceKind.Bone);
                b.Branch(c, tip, .25f - i * .02f, .13f - i * .015f, Bone, SurfaceKind.Bone);
                b.Branch(tip, tip + new Vector3(side * .3f, .23f, -.75f), .13f - i * .015f, .026f, Bone, SurfaceKind.Bone);
                b.Branch(a + new Vector3(-side * .13f, 0, .38f), c + new Vector3(.11f, -.21f, .21f), .14f, .058f, Bark);
                LeafSpray(b, c + new Vector3(side * .14f, .1f, -.15f), side * .9f, .95f);
                Fern(b, a + new Vector3(side * .3f, .1f, .15f), 1.15f);
            }
            Tree(b, new(side * (x + 2.9f), 0, -z * .59f), 4.4f, side);
            Flower(b, new(side * (x + 1.2f), 0, -z * .1f), .8f);
            // Low tendons continue along the outer arena edge without implying a gameplay obstruction.
            var edgeRoot = new Vector3(side * (x + .73f), .15f, -z * .12f);
            b.Branch(new(side * (x + .65f), .11f, -z + .5f), edgeRoot, .12f, .085f, Root);
            b.Branch(edgeRoot, new(side * (x + .65f), .11f, z * .54f), .085f, .025f, Root);
        }
    }

    private static void AntlerGrove(EnvironmentBuilder b, float x, float z)
    {
        // A many-tined bone tree marks the grove without placing false track clues on the route.
        var p = new Vector3(.15f, 0, -z - 3.1f);
        var trunkBend = p + new Vector3(-.16f, 1.9f, .08f);
        b.Branch(p + Vector3.Up * .02f, trunkBend, .72f, .46f, Bark);
        b.Branch(trunkBend, p + new Vector3(.14f, 3.7f, -.13f), .46f, .21f, BarkLight);
        b.Branch(p + new Vector3(0, .1f, .5f), p + new Vector3(.26f, 4.2f, .12f), .16f, .06f, BoneShade, SurfaceKind.Bone);
        foreach (float side in new[] { -1f, 1f })
        {
            var joint = p + new Vector3(side * 1.18f, 3.66f, -.2f);
            var end = p + new Vector3(side * 3.2f, 4.83f, -.36f);
            b.Branch(p + new Vector3(0, 2.1f, 0), joint, .24f, .16f, BoneShade, SurfaceKind.Bone);
            b.Branch(joint, end, .16f, .062f, Bone, SurfaceKind.Bone);
            for (int tine = 0; tine < 4; tine++)
            {
                float t = tine / 3f;
                var foot = joint.Lerp(end, t);
                var tip = foot + new Vector3(side * (.05f + t * .2f), 1.35f - t * .4f, -.32f);
                b.Branch(foot, tip, .095f - t * .023f, .048f - t * .015f, Bone, SurfaceKind.Bone);
                b.Branch(tip, tip + new Vector3(side * .23f, .37f, .01f), .048f - t * .015f, .009f, Bone, SurfaceKind.Bone);
            }
            Fern(b, p + new Vector3(side * .98f, 0, .25f), 1.25f);
            Tree(b, new(side * (x * .69f), 0, -z - 4.1f), 5.4f - (side + 1) * .35f, side);
            Tree(b, new(side * (x + 2.9f), 0, -z * .4f), 4.4f, side);
        }
        InsectMolt(b, new(x + 2.52f, 1.7f, -z * .4f + .46f));
        for (int i = 0; i < 3; i++)
        {
            var shrine = new Vector3(-x * .45f + i * .66f, 0, -z - 1.24f);
            b.Cylinder(.25f, .16f, .7f + i * .16f, shrine + Vector3.Up * (.35f + i * .08f), StoneDark);
            b.Box(new(.33f, .08f, .35f), shrine + Vector3.Up * (.76f + i * .16f), BoneShade);
        }
    }

    private static void Perimeter(EnvironmentBuilder b, float x, float z, bool village)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            for (int i = 0; i < 4; i++)
            {
                float pz = -z * .8f + i * z * .47f;
                var p = new Vector3(side * (x + 1.4f), 0, pz);
                // Ferns remain outward of the edge, including their outermost leaves.
                Fern(b, p, i == 3 ? .62f : .88f);
                if (i % 2 == 0) Flower(b, p + new Vector3(side * .75f, 0, -.66f), village ? .43f : .7f);
            }
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3(side * (1.8f + i * x * .21f), 0, z + 1.2f);
                // Camera-facing roots never rise above ankle height; no front canopy obscures a fight.
                var bend = p + new Vector3(-.04f, .14f, .07f);
                b.Branch(p + new Vector3(-.7f, .07f, .15f), bend, .10f, .07f, Root);
                b.Branch(bend, p + new Vector3(.63f, .11f, -.12f), .07f, .018f, Root);
                b.Box(new(.53f, .04f, .29f), p + new Vector3(0, .09f, -.02f), Moss, new(0, 29 + i * 13, 0));
            }
        }
    }

    private static void Tree(EnvironmentBuilder b, Vector3 p, float height, float lean)
    {
        var lower = p + new Vector3(lean * .07f, height * .25f, -.08f);
        var middle = p + new Vector3(lean * .23f, height * .53f, -.25f);
        var upper = p + new Vector3(lean * .39f, height * .78f, -.46f);
        var crown = p + new Vector3(lean * .54f, height, -.53f);
        b.Branch(p + Vector3.Up * .02f, lower, .61f, .41f, Bark);
        b.Branch(lower, middle, .41f, .28f, Bark);
        b.Branch(middle, upper, .28f, .17f, BarkLight);
        b.Branch(upper, crown, .17f, .063f, BarkLight);
        foreach (float side in new[] { -1f, 1f })
        {
            var rootKnee = p + new Vector3(side * .48f, .25f, .29f);
            b.Branch(lower, rootKnee, .24f, .16f, Root);
            b.Branch(rootKnee, p + new Vector3(side * .88f, .045f, .57f), .16f, .028f, Root);
            var fork = middle + new Vector3(side * .49f, height * .15f, -.28f);
            var branch = crown + new Vector3(side * .9f, .26f - side * .15f, -.22f);
            b.Branch(middle, fork, .17f, .12f, Bark);
            b.Branch(fork, branch, .12f, .027f, BarkLight);
            LeafSpray(b, branch, side * .94f, 1.02f);
            LeafSpray(b, middle + new Vector3(side * .28f, .24f, .08f), side * .82f, .75f);
        }
        b.Branch(lower + Vector3.Back * .05f, p + new Vector3(lean * .18f, .04f, -.89f), .22f, .022f, Root);
        LeafSpray(b, crown + Vector3.Up * .08f, lean * .8f, 1.12f);
    }

    private static void LeafSpray(EnvironmentBuilder b, Vector3 p, float spread, float scale)
    {
        // Alternating pointed leaves expose an actual tapering stem between the blades.
        var middle = p + new Vector3(spread * .52f, .27f * scale, -.085f);
        var tip = p + new Vector3(spread, .17f * scale, -.16f);
        b.Branch(p, middle, .030f * scale, .020f * scale, BarkLight);
        b.Branch(middle, tip, .020f * scale, .005f * scale, BarkLight);
        for (int leaf = 0; leaf < 4; leaf++)
        {
            float t = .17f + leaf * .22f;
            var basePoint = p.Lerp(tip, t) + Vector3.Up * (.09f * scale * Mathf.Sin(t * Mathf.Pi));
            float size = scale * (.62f - leaf * .085f);
            foreach (float side in new[] { -1f, 1f })
            {
                var direction = new Vector3(Math.Sign(spread) * .38f, .08f + leaf * .035f, side * .84f);
                var baseOffset = new Vector3(Math.Sign(spread) * side * .026f * scale, side * .018f * scale, 0);
                Blade(b, basePoint + baseOffset, direction, size, size * .39f, size * .13f,
                    side > 0 ? Leaf : leaf % 2 == 0 ? LeafLight : LeafDark, side * (8 + leaf * 3));
            }
        }
        Blade(b, tip - new Vector3(Math.Sign(spread) * .03f, 0, 0), new(Math.Sign(spread), .17f, -.2f),
            scale * .35f, scale * .13f, scale * .05f, LeafLight, -12);
    }

    private static void Fern(EnvironmentBuilder b, Vector3 p, float scale)
    {
        b.Cylinder(.13f * scale, .065f * scale, .33f * scale, p + Vector3.Up * (.17f * scale), BarkLight);
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.Tau / 6 + (i % 2 == 0 ? .07f : -.04f);
            var direction = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            var lateral = new Vector3(direction.Z, 0, -direction.X);
            var basePoint = p + Vector3.Up * (.27f * scale);
            var rise = p + direction * (.34f * scale) + Vector3.Up * ((.55f + i % 2 * .07f) * scale);
            var end = p + direction * ((.82f - i % 2 * .06f) * scale) + Vector3.Up * (.28f * scale);
            b.Branch(basePoint, rise, .023f * scale, .014f * scale, Root);
            b.Branch(rise, end, .014f * scale, .004f * scale, BarkLight);
            for (int leaf = 0; leaf < 4; leaf++)
            {
                float t = .24f + leaf * .18f;
                var center = t < .5f ? basePoint.Lerp(rise, t * 2) : rise.Lerp(end, (t - .5f) * 2);
                float length = (.35f - leaf * .064f) * scale;
                foreach (float side in new[] { -1f, 1f })
                    Blade(b, center + direction * (side * .017f * scale),
                        lateral * side + direction * .37f + Vector3.Up * (.12f - leaf * .09f), length,
                        length * .36f, length * .14f, (i + leaf) % 3 == 0 ? LeafLight : Leaf, side * 11);
            }
            Blade(b, end - direction * (.12f * scale), direction + Vector3.Down * .12f, .23f * scale, .08f * scale, .03f * scale, LeafLight);
        }
    }

    private static void Blade(EnvironmentBuilder b, Vector3 position, Vector3 direction, float length, float width,
        float curl, string color, float roll = 0)
    {
        // Build a leaf frame with its broad face turned toward the sky. Local Y is the
        // midrib, X is blade width and Z cups upward; a small roll varies neighbouring leaves.
        var axis = direction.Normalized();
        var lateral = axis.Cross(Math.Abs(axis.Y) < .93f ? Vector3.Up : Vector3.Forward).Normalized();
        var normal = lateral.Cross(axis).Normalized();
        var basis = new Basis(lateral, axis, normal) * Basis.FromEuler(new(0, Mathf.DegToRad(roll), 0));
        b.Leaf(length, width, curl, position, color, basis.GetEuler() * (180 / Mathf.Pi));
    }

    private static void Flower(EnvironmentBuilder b, Vector3 p, float scale)
    {
        b.Cylinder(.19f * scale, .1f * scale, .62f * scale, p + Vector3.Up * (.31f * scale), BarkLight);
        b.Cylinder(.14f * scale, .4f * scale, .54f * scale, p + Vector3.Up * (.8f * scale), Pod);
        b.Cylinder(.31f * scale, .31f * scale, .026f * scale, p + Vector3.Up * (1.075f * scale), LeafDark);
        b.Torus(.3f * scale, .43f * scale, p + Vector3.Up * (1.09f * scale), Root);
        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.Tau / 6;
            b.Cylinder(.058f * scale, 0, .21f * scale, p + new Vector3(Mathf.Sin(angle) * .32f, 1.2f, Mathf.Cos(angle) * .32f) * scale, Bone);
        }
        b.Cylinder(.04f * scale, .075f * scale, .19f * scale, p + Vector3.Up * (1.03f * scale), Sap);
    }

    private static void InsectMolt(EnvironmentBuilder b, Vector3 p)
    {
        // A hollow, shed carapace is fixed to a trunk, distinct from the moving enemy silhouettes.
        for (int i = 0; i < 4; i++)
        {
            float radius = .34f - Math.Abs(i - 1.5f) * .055f;
            b.Cylinder(radius, radius * .88f, .31f, p + Vector3.Up * (i * .28f), BoneShade);
            b.Box(new(.035f, .27f, .055f), p + new Vector3(0, i * .28f, radius), Bark);
        }
        foreach (float side in new[] { -1f, 1f })
            for (int i = 0; i < 3; i++)
            {
                var foot = p + new Vector3(side * .2f, .2f + i * .22f, .15f);
                var knee = foot + new Vector3(side * .45f, -.15f + i * .04f, .12f);
                b.Beam(foot, knee, .055f, Bone);
                b.Beam(knee, knee + new Vector3(side * .12f, -.37f, -.24f), .04f, BoneShade);
            }
    }
}
