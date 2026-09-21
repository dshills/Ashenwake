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
            b.Beam(root, middle, .24f, Root);
            b.Beam(middle, foot + new Vector3(-side * .2f, columnHeight + .55f, .4f), .18f, BarkLight);
            LeafSpray(b, foot + new Vector3(side * .17f, columnHeight * .66f, .42f), side * .75f, .9f);
        }
        if (!broken)
        {
            b.Box(new(width + 1f, .48f, 1.1f), p + Vector3.Up * (height + .64f), Stone);
            b.Box(new(width + 1.15f, .12f, 1.24f), p + Vector3.Up * (height + .92f), StoneLight);
            b.Beam(p + new Vector3(-width * .57f, height + 1.02f, .4f), p + new Vector3(width * .45f, height + .94f, .42f), .22f, Bark);
            Fern(b, p + new Vector3(-.5f, height + 1.06f, -.12f), .98f);
        }
        else
        {
            b.Box(new(width * .52f, .43f, 1.05f), p + new Vector3(-width * .23f, height + .55f, 0), Stone, new(0, 0, -5));
            b.Box(new(1.35f, .52f, .9f), p + new Vector3(.6f, .27f, .22f), StoneLight, new(0, 18, 0));
            b.Beam(p + new Vector3(-width * .5f, height + .9f, -.3f), p + new Vector3(.6f, height + 1.7f, -.2f), .28f, Bark);
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
            b.Beam(p + new Vector3(side * width * .44f, 0, .8f), p + new Vector3(side * width * .44f, height + .08f, .8f), .18f, Root);
            b.Beam(p + new Vector3(side * width * .44f, 0, -.8f), p + new Vector3(side * width * .44f, height + .08f, -.8f), .19f, Bark);
            b.Beam(p + new Vector3(side * width * .44f, .45f, .81f), p + new Vector3(0, 1.03f, .81f), .12f, Wood);
        }
        b.Box(new(width, .19f, depth), p + Vector3.Up * 1.03f, Wood);
        b.Box(new(width, height - 1.1f, .11f), p + new Vector3(0, (height + 1.1f) * .5f, -.95f), LeafDark);
        b.Box(new(.1f, height - 1.1f, depth - .25f), p + new Vector3(-width * .48f, (height + 1.1f) * .5f, -.03f), Wood);
        b.Box(new(.1f, height - 1.1f, depth - .25f), p + new Vector3(width * .48f, (height + 1.1f) * .5f, -.03f), Wood);
        for (int i = 0; i < 9; i++)
        {
            float px = -width * .44f + i * width * .11f;
            float rise = (1f - Math.Abs(px) / (width * .6f)) * .65f;
            b.Box(new(width * .13f, .12f, depth + .56f), p + new Vector3(px, height + rise, 0), i % 3 == 0 ? LeafLight : Leaf, new(0, 0, -Math.Sign(px) * 22));
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
                b.Beam(a, c, .51f - i * .04f, BoneShade);
                b.Beam(c, tip, .39f - i * .04f, Bone);
                b.Beam(tip, tip + new Vector3(side * .3f, .23f, -.75f), .25f, Bone);
                b.Beam(a + new Vector3(-side * .13f, 0, .38f), c + new Vector3(.11f, -.21f, .21f), .2f, Bark);
                LeafSpray(b, c + new Vector3(side * .14f, .1f, -.15f), side * .9f, .95f);
                Fern(b, a + new Vector3(side * .3f, .1f, .15f), 1.15f);
            }
            Tree(b, new(side * (x + 2.9f), 0, -z * .59f), 4.4f, side);
            Flower(b, new(side * (x + 1.2f), 0, -z * .1f), .8f);
            // Low tendons continue along the outer arena edge without implying a gameplay obstruction.
            b.Beam(new(side * (x + .65f), .11f, -z + .5f), new(side * (x + .65f), .11f, z * .54f), .2f, Root);
        }
    }

    private static void AntlerGrove(EnvironmentBuilder b, float x, float z)
    {
        // A many-tined bone tree marks the grove without placing false track clues on the route.
        var p = new Vector3(.15f, 0, -z - 3.1f);
        b.Cylinder(.72f, .4f, 3.5f, p + Vector3.Up * 1.75f, Bark);
        b.Beam(p + new Vector3(0, .1f, .5f), p + new Vector3(.26f, 4.2f, .12f), .24f, BoneShade);
        foreach (float side in new[] { -1f, 1f })
        {
            var joint = p + new Vector3(side * 1.18f, 3.66f, -.2f);
            var end = p + new Vector3(side * 3.2f, 4.83f, -.36f);
            b.Beam(p + new Vector3(0, 2.1f, 0), joint, .37f, BoneShade);
            b.Beam(joint, end, .24f, Bone);
            for (int tine = 0; tine < 4; tine++)
            {
                float t = tine / 3f;
                var foot = joint.Lerp(end, t);
                var tip = foot + new Vector3(side * (.05f + t * .2f), 1.35f - t * .4f, -.32f);
                b.Beam(foot, tip, .14f - t * .04f, Bone);
                b.Beam(tip, tip + new Vector3(side * .23f, .37f, .01f), .07f, Bone);
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
                b.Beam(p + new Vector3(-.7f, .07f, .15f), p + new Vector3(.63f, .11f, -.12f), .16f, Root);
                b.Box(new(.53f, .04f, .29f), p + new Vector3(0, .09f, -.02f), Moss, new(0, 29 + i * 13, 0));
            }
        }
    }

    private static void Tree(EnvironmentBuilder b, Vector3 p, float height, float lean)
    {
        var middle = p + new Vector3(lean * .23f, height * .53f, -.25f);
        var crown = p + new Vector3(lean * .54f, height, -.53f);
        b.Cylinder(.63f, .33f, height * .56f, p + new Vector3(0, height * .28f, -.05f), Bark);
        b.Beam(middle, crown, .42f, BarkLight);
        b.Beam(p + new Vector3(-.63f, .04f, .46f), middle, .23f, Root);
        b.Beam(p + new Vector3(.79f, .04f, .28f), middle, .26f, Root);
        foreach (float side in new[] { -1f, 1f })
        {
            var branch = crown + new Vector3(side * 1.17f, .42f - side * .12f, -.22f);
            b.Beam(middle + Vector3.Up * .75f, branch, .19f, Bark);
            LeafSpray(b, branch, side * 1.17f, 1.02f);
            LeafSpray(b, middle + new Vector3(side * .29f, .3f, .1f), side * .98f, .75f);
        }
        LeafSpray(b, crown + Vector3.Up * .08f, lean * .8f, 1.12f);
    }

    private static void LeafSpray(EnvironmentBuilder b, Vector3 p, float spread, float scale)
    {
        // Layered blades make a botanical silhouette rather than a sphere sitting on a trunk.
        var tip = p + new Vector3(spread, .2f * scale, -.12f);
        b.Beam(p, tip, .045f * scale, BarkLight);
        for (int leaf = 0; leaf < 4; leaf++)
        {
            float t = .18f + leaf * .22f;
            var basePoint = p.Lerp(tip, t);
            float size = scale * (.72f - leaf * .1f);
            float yaw = Math.Sign(spread) * (22 + leaf * 8);
            b.Box(new(size * .7f, .055f, size), basePoint + new Vector3(0, .02f, -.18f), leaf % 2 == 0 ? Leaf : LeafLight, new(-12, yaw, Math.Sign(spread) * 9));
            b.Box(new(size * .32f, .045f, size * .47f), basePoint + new Vector3(Math.Sign(spread) * .06f, .1f, -.63f * size), LeafDark, new(-12, yaw, Math.Sign(spread) * 9));
        }
    }

    private static void Fern(EnvironmentBuilder b, Vector3 p, float scale)
    {
        b.Cylinder(.18f * scale, .085f * scale, .47f * scale, p + Vector3.Up * (.24f * scale), BarkLight);
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.Tau / 6;
            var direction = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            var end = p + direction * (.72f * scale) + Vector3.Up * (.32f * scale);
            b.Beam(p + Vector3.Up * (.4f * scale), end, .034f * scale, Root);
            for (int leaf = 0; leaf < 3; leaf++)
            {
                float t = .3f + leaf * .24f;
                var center = p.Lerp(end, t) + Vector3.Up * (.28f * scale * (1 - t));
                b.Box(new((.37f - leaf * .075f) * scale, .05f * scale, .32f * scale), center, i % 2 == 0 ? Leaf : LeafLight, new(0, Mathf.RadToDeg(a), 0));
            }
        }
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
