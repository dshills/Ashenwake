using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual : Node3D
{
    private void BuildHumanoid(string discipline, string npcId = "")
    {
        var kind = string.IsNullOrEmpty(npcId) ? discipline.ToLowerInvariant() : HumanNpcKind(npcId);
        switch (kind)
        {
            case "vanguard":
                Palette("354d57", "79e0cb", "17232d", "a5afb6", "eadcc1", "bc9980", "d4fff5");
                break;
            case "veilwalker":
                Palette("34334c", "b6a0ef", "171b2a", "a6b3bf", "d4c9b3", "b79a90", "e3cdff");
                break;
            case "arcanist":
                Palette("344e79", "91dbf3", "1e273b", "b59b65", "e6d8b4", "c7a58d", "bceeff");
                break;
            case "gravecaller":
                Palette("3e4851", "88caba", "1a232d", "8c8575", "e9dfc5", "bfa894", "b5ffdd");
                break;
            case "warden":
                Palette("64513a", "9dc56c", "283b31", "b6a783", "ded1ab", "c7a189", "d7f0a6");
                break;
            case "mara":
                Palette("566e73", "b76457", "273b42", "a7b6b1", "e5dcc7", "bd9278", "d1f6d7");
                break;
            case "torren":
                Palette("795a3e", "c99a5c", "302c29", "a5a9a8", "d9c39b", "c58c66", "ffcf8d");
                break;
            case "cael":
                Palette("63618a", "ba9ad2", "36374d", "c8b576", "ebe2cd", "c5a18e", "fff0bd");
                break;
            case "oris":
                Palette("48626b", "d2a477", "283c45", "c5ad78", "e6d6b1", "bda98c", "e6e6b2");
                break;
            case "kesh":
                Palette("805445", "d3a763", "3b3435", "bea77a", "d8ccb2", "b07e61", "ffd18f");
                break;
            default:
                Palette("536777", "79e0cb", "263440", "a3b1b9", "e7dbc2", "c39c83", "caffed");
                break;
        }

        if (_appearance is not null) { BuildEquippedBody(); return; }

        Height = 2.3f;
        var plate = kind == "vanguard";
        var robe = kind is "arcanist" or "gravecaller" or "cael";
        var torso = plate ? Metal : Main;

        // Feet stay at the model origin. Only these cosmetic joints move; the Core owns locomotion.
        HumanLeg(-1, plate);
        HumanLeg(1, plate);
        TaperedBox(BodyRoot, new Vector3(0, .96f, 0), new Vector3(.48f, .25f, .3f), Dark, .88f);
        var chest = Cone(BodyRoot, new Vector3(0, 1.34f, 0), .27f, .37f, .64f, torso);
        chest.Scale = new Vector3(1, 1, .67f);
        Box(BodyRoot, new Vector3(0, 1.03f, -.025f), new Vector3(.6f, .11f, .38f), Dark);
        Box(BodyRoot, new Vector3(0, 1.03f, -.225f), new Vector3(.13f, .12f, .045f), Metal);
        Cone(BodyRoot, new Vector3(0, 1.77f, 0), .105f, .105f, .16f, Skin);

        if (robe)
        {
            EquippedRobeSkirt(BodyRoot, .46f, Main, Metal);
            Box(BodyRoot, new Vector3(0, .86f, -.30f), new Vector3(.12f, 1.1f, .045f), Accent);
        }

        var left = HumanArm(-1, plate);
        var right = HumanArm(1, plate);
        switch (kind)
        {
            case "vanguard":
                HumanVanguard(left, right);
                break;
            case "veilwalker":
                HumanVeilwalker(left, right);
                break;
            case "arcanist":
                HumanArcanist(left, right);
                break;
            case "gravecaller":
                HumanGravecaller(left, right);
                break;
            case "warden":
                HumanWarden(left, right);
                break;
            case "mara":
                HumanMara(left, right);
                break;
            case "torren":
                HumanTorren(left, right);
                break;
            case "cael":
                HumanCael(left, right);
                break;
            case "oris":
                HumanOris(left, right);
                break;
            case "kesh":
                HumanKesh(left, right);
                break;
            default:
                HumanFace();
                HumanCape(.75f, Accent);
                break;
        }
    }

    private static string HumanNpcKind(string id)
    {
        var separator = id.LastIndexOf('.');
        return (separator < 0 ? id : id[(separator + 1)..]).ToLowerInvariant();
    }

    private Node3D HumanLeg(int side, bool plate, bool underclothes = false)
    {
        var leg = Joint(BodyRoot, new Vector3(side * .18f, .92f, 0), side < 0 ? "left_leg" : "right_leg");
        Cone(leg, new Vector3(0, -.18f, 0), .12f, .145f, .34f, Dark);
        Orb(leg, new Vector3(0, -.37f, -.045f), new Vector3(.25f, .22f, .24f), underclothes ? Dark : plate ? Metal : Main);
        Cone(leg, new Vector3(0, -.57f, 0), .105f, .13f, .32f, underclothes ? Dark : plate ? Metal : Main);
        Box(leg, new Vector3(0, underclothes ? -.865f : -.82f, -.075f), underclothes ? new(.20f, .10f, .29f) : new(.24f, .19f, .39f), Dark);
        return leg;
    }

    private Node3D HumanArm(int side, bool plate)
    {
        var arm = Joint(BodyRoot, new Vector3(side * .4f, 1.64f, 0), side < 0 ? "left_arm" : "right_arm", .7f);
        arm.RotationDegrees = new Vector3(0, 0, side * 7);
        Cone(arm, new Vector3(side * .025f, -.17f, 0), .11f, .14f, .35f, plate ? Metal : Main);
        Cone(arm, new Vector3(side * .04f, -.46f, -.015f), .085f, .11f, .28f, plate ? Metal : Dark);
        Orb(arm, new(side * .04f, -.64f, -.035f), new(.16f, .20f, .15f), plate ? Dark : Skin);
        Orb(arm, new(side * -.04f, -.60f, -.065f), new(.065f, .11f, .075f), plate ? Dark : Skin);
        return arm;
    }

    private void HumanFace(bool hair = true)
    {
        // Rounded cheeks and jaw keep the face organic at the close inventory camera.
        // The brow and nose retain the small planes that make the profile readable.
        Orb(BodyRoot, new(0, 2.025f, .008f), new(.38f, .40f, .34f), Skin);
        Orb(BodyRoot, new(0, 1.885f, -.015f), new(.265f, .21f, .255f), Skin);
        if (hair)
        {
            Orb(BodyRoot, new(0, 2.16f, .025f), new(.415f, .20f, .355f), Dark);
            for (int lockIndex = -1; lockIndex <= 1; lockIndex++)
                TaperedBox(BodyRoot, new(lockIndex * .083f, 2.10f - lockIndex * .012f, -.125f), new(.09f, .15f, .06f), Dark, .48f, new(0, 0, -14));
        }
        for (int side = -1; side <= 1; side += 2)
        {
            Orb(BodyRoot, new(side * .19f, 1.985f, .015f), new(.060f, .11f, .055f), Skin);
            Rod(BodyRoot, new(side * .035f, 2.05f, -.177f), new(side * .12f, 2.065f, -.159f), .014f, Dark);
            Orb(BodyRoot, new(side * .073f, 2.028f, -.173f), new(.046f, .025f, .024f), Dark);
            Orb(BodyRoot, new(side * .069f, 2.032f, -.184f), new(.012f, .012f, .008f), Bone);
            Orb(BodyRoot, new(side * .103f, 1.96f, -.137f), new(.10f, .10f, .046f), Skin);
        }
        TaperedBox(BodyRoot, new(0, 1.99f, -.175f), new(.05f, .115f, .065f), Skin, .8f);
        Box(BodyRoot, new(0, 1.91f, -.149f), new(.075f, .012f, .016f), Dark);
    }

    private void HumanHood(Material fabric, Material face)
    {
        Orb(BodyRoot, new Vector3(0, 2.02f, .025f), new Vector3(.56f, .6f, .48f), fabric);
        Orb(BodyRoot, new Vector3(0, 1.99f, -.185f), new Vector3(.34f, .37f, .15f), face);
        Box(BodyRoot, new Vector3(-.073f, 2.025f, -.265f), new Vector3(.05f, .03f, .018f), face == Dark ? Glow : Dark);
        Box(BodyRoot, new Vector3(.073f, 2.025f, -.265f), new Vector3(.05f, .03f, .018f), face == Dark ? Glow : Dark);
        Cone(BodyRoot, new Vector3(0, 1.735f, 0), .32f, .19f, .22f, fabric);
        // A broad folded rim frames the face instead of reading as a second round head.
        for (int side = -1; side <= 1; side += 2)
        {
            TaperedBox(BodyRoot, new(side * .175f, 2.035f, -.187f), new(.10f, .39f, .09f), fabric, .65f, new(0, 0, side * -14));
            Rod(BodyRoot, new(side * .02f, 2.245f, -.095f), new(side * .16f, 2.185f, -.175f), .036f, fabric);
        }
    }

    private void HumanCape(float length, Material fabric)
    {
        var cape = Joint(BodyRoot, new Vector3(0, 1.68f, .19f), "sway", .4f);
        TailoredCape(cape, new(0, 0, .035f), .61f, length, fabric);
        Box(BodyRoot, new Vector3(0, 1.61f, -.26f), new Vector3(.3f, .14f, .08f), fabric);
        Orb(BodyRoot, new Vector3(.13f, 1.63f, -.315f), new Vector3(.09f, .09f, .05f), Metal);
    }

    private static void TailoredCape(Node parent, Vector3 top, float width, float length, Material fabric)
    {
        // Four broad folds and a lifted center hem remain readable from the gameplay camera.
        // Back faces are authored explicitly so this never changes a shared material's culling mode.
        using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetSmoothGroup(uint.MaxValue);
        Vector3 Point(int column, int row)
        {
            float down = row * .5f;
            float hem = row == 2 && column == 2 ? length * .10f : 0;
            return top + new Vector3((column * .25f - .5f) * width * Mathf.Lerp(.86f, 1.10f, down),
                -length * down + hem, down * length * .16f + (column % 2 == 0 ? 0 : .045f) * MathF.Sin(down * Mathf.Pi * .5f));
        }
        void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            void Vertex(Vector3 point)
            {
                surface.SetUV(new((point.X - top.X) / width + .5f, (top.Y - point.Y) / length));
                surface.AddVertex(point);
            }
            Vertex(a); Vertex(b); Vertex(c); Vertex(c); Vertex(b); Vertex(a);
        }
        for (int row = 0; row < 2; row++)
            for (int column = 0; column < 4; column++)
            {
                Triangle(Point(column, row), Point(column, row + 1), Point(column + 1, row + 1));
                Triangle(Point(column, row), Point(column + 1, row + 1), Point(column + 1, row));
            }
        surface.GenerateNormals(); surface.GenerateTangents(); surface.Index();
        parent.AddChild(new MeshInstance3D { Mesh = surface.Commit(), MaterialOverride = fabric });
    }

    private void HumanVanguard(Node3D left, Node3D right)
    {
        HumanCape(.78f, Accent);
        TaperedBox(BodyRoot, new Vector3(0, 1.39f, -.275f), new Vector3(.42f, .46f, .08f), Main);
        Rod(BodyRoot, new Vector3(0, 1.6f, -.33f), new Vector3(0, 1.2f, -.33f), .035f, Accent);
        Orb(left, new Vector3(-.02f, -.025f, 0), new Vector3(.43f, .25f, .44f), Metal);
        Orb(right, new Vector3(.02f, -.025f, 0), new Vector3(.43f, .25f, .44f), Metal);
        Orb(BodyRoot, new Vector3(0, 2.01f, 0), new Vector3(.47f, .5f, .44f), Metal);
        Box(BodyRoot, new Vector3(0, 2.025f, -.225f), new Vector3(.34f, .065f, .035f), Dark);
        Box(BodyRoot, new Vector3(0, 1.93f, -.235f), new Vector3(.055f, .2f, .055f), Main);
        Box(BodyRoot, new Vector3(0, 2.25f, .015f), new Vector3(.095f, .15f, .34f), Accent);
        Height = 2.45f;

        TaperedBox(left, new Vector3(-.08f, -.41f, -.245f), new Vector3(.6f, .78f, .10f), Metal, .58f, new Vector3(0, -10, 0));
        TaperedBox(left, new Vector3(-.08f, -.4f, -.31f), new Vector3(.47f, .62f, .055f), Main, .58f, new Vector3(0, -10, 0));
        Box(left, new Vector3(-.08f, -.4f, -.348f), new Vector3(.065f, .52f, .025f), Accent);
        Box(left, new Vector3(-.08f, -.4f, -.348f), new Vector3(.36f, .065f, .025f), Accent);
        Orb(left, new Vector3(-.08f, -.4f, -.385f), new Vector3(.16f, .16f, .075f), Metal);
        HumanSword(right, .11f, .82f);
    }

    private void HumanSword(Node3D hand, float x, float length)
    {
        Rod(hand, new Vector3(x, -.73f, -.055f), new Vector3(x, -.51f, -.055f), .045f, Dark);
        Box(hand, new Vector3(x, -.50f, -.055f), new Vector3(.29f, .07f, .09f), Metal);
        Box(hand, new Vector3(x, -.47f + length * .5f, -.055f), new Vector3(.105f, length, .04f), Metal);
        Cone(hand, new Vector3(x, -.47f + length + .08f, -.055f), .058f, 0, .16f, Metal);
    }

    private void HumanVeilwalker(Node3D left, Node3D right)
    {
        HumanHood(Main, Dark);
        HumanCape(1.2f, Main);
        Box(BodyRoot, new Vector3(0, 1.9f, -.272f), new Vector3(.32f, .11f, .03f), Accent);
        Rod(BodyRoot, new Vector3(-.26f, 1.62f, -.22f), new Vector3(.23f, 1.1f, -.23f), .048f, Accent);
        Cone(BodyRoot, new Vector3(0, .93f, .015f), .35f, .27f, .4f, Main);
        HumanSword(left, -.10f, .39f);
        HumanSword(right, .10f, .39f);
        Box(BodyRoot, new Vector3(-.29f, 1.03f, 0), new Vector3(.15f, .23f, .21f), Metal);
        Box(BodyRoot, new Vector3(.29f, 1.03f, 0), new Vector3(.15f, .23f, .21f), Metal);
    }

    private void HumanArcanist(Node3D left, Node3D right)
    {
        HumanFace();
        HumanCape(1.21f, Main);
        Cone(BodyRoot, new Vector3(0, 2.19f, .02f), .24f, .18f, .16f, Main);
        Box(BodyRoot, new Vector3(0, 2.17f, -.225f), new Vector3(.11f, .15f, .07f), Metal);
        Orb(BodyRoot, new Vector3(0, 2.17f, -.27f), new Vector3(.065f, .09f, .045f), Glow);
        Orb(left, new Vector3(-.02f, -.025f, 0), new Vector3(.38f, .23f, .4f), Metal);
        Orb(right, new Vector3(.02f, -.025f, 0), new Vector3(.38f, .23f, .4f), Metal);
        Rod(right, new Vector3(.075f, -1.53f, -.075f), new Vector3(.075f, .72f, -.075f), .041f, Dark);
        Ring(right, new Vector3(.075f, .84f, -.075f), .14f, .19f, Metal, new Vector3(90, 0, 0));
        Orb(right, new Vector3(.075f, .84f, -.075f), new Vector3(.19f, .24f, .19f), Glow);
        Cone(right, new Vector3(.075f, .54f, -.075f), .08f, .08f, .18f, Metal);
        Orb(left, new Vector3(-.06f, -.51f, -.2f), new Vector3(.20f, .20f, .20f), Glow);
        Height = 2.75f;
    }

    private void HumanGravecaller(Node3D left, Node3D right)
    {
        HumanHood(Dark, Bone);
        HumanCape(1.28f, Main);
        Box(BodyRoot, new Vector3(0, 1.915f, -.275f), new Vector3(.16f, .07f, .025f), Dark);
        for (var side = -1; side <= 1; side += 2)
        {
            for (var rib = 0; rib < 3; rib++)
                Rod(BodyRoot, new Vector3(side * .055f, 1.54f - rib * .115f, -.29f),
                    new Vector3(side * (.235f - rib * .027f), 1.49f - rib * .115f, -.23f), .026f, Bone);
        }
        Cone(left, new Vector3(-.02f, .055f, 0), .135f, 0, .37f, Bone, new Vector3(0, 0, 35));
        Cone(right, new Vector3(.02f, .055f, 0), .135f, 0, .37f, Bone, new Vector3(0, 0, -35));
        Rod(right, new Vector3(.075f, -1.53f, -.075f), new Vector3(.075f, .72f, -.075f), .043f, Bone);
        Ring(right, new Vector3(.17f, .72f, -.075f), .14f, .19f, Bone, new Vector3(90, 0, 0));
        Orb(right, new Vector3(.17f, .7f, -.075f), new Vector3(.14f, .17f, .14f), Glow);
        Orb(left, new Vector3(-.06f, -.62f, -.16f), new Vector3(.25f, .28f, .24f), Bone);
        Box(left, new Vector3(-.06f, -.60f, -.285f), new Vector3(.17f, .055f, .025f), Dark);
        Height = 2.7f;
    }

    private void HumanWarden(Node3D left, Node3D right)
    {
        HumanHood(Dark, Skin);
        HumanCape(.95f, Accent);
        TaperedBox(BodyRoot, new Vector3(0, 1.35f, -.28f), new Vector3(.43f, .47f, .06f), Main);
        Orb(left, new Vector3(-.04f, -.025f, 0), new Vector3(.42f, .27f, .39f), Accent);
        Orb(right, new Vector3(.04f, -.025f, 0), new Vector3(.42f, .27f, .39f), Accent);
        for (var side = -1; side <= 1; side += 2)
        {
            Rod(BodyRoot, new Vector3(side * .15f, 2.21f, .01f), new Vector3(side * .30f, 2.49f, .035f), .046f, Bone);
            Rod(BodyRoot, new Vector3(side * .30f, 2.49f, .035f), new Vector3(side * .38f, 2.67f, -.055f), .025f, Bone);
            Rod(BodyRoot, new Vector3(side * .26f, 2.4f, .03f), new Vector3(side * .43f, 2.48f, -.065f), .024f, Bone);
        }
        Rod(right, new Vector3(.075f, -1.53f, -.075f), new Vector3(.075f, .94f, -.075f), .036f, Dark);
        Cone(right, new Vector3(.075f, 1.03f, -.075f), .105f, 0, .37f, Metal);
        Box(right, new Vector3(.19f, .72f, -.075f), new Vector3(.22f, .26f, .035f), Accent);
        Height = 3.0f;
    }

    private void HumanMara(Node3D left, Node3D right)
    {
        HumanFace();
        Orb(BodyRoot, new Vector3(0, 2.08f, .20f), new Vector3(.27f, .24f, .25f), Dark);
        Box(BodyRoot, new Vector3(0, 1.13f, -.28f), new Vector3(.42f, .77f, .065f), Bone);
        Box(BodyRoot, new Vector3(0, 1.45f, -.322f), new Vector3(.09f, .22f, .025f), Accent);
        Box(BodyRoot, new Vector3(0, 1.45f, -.322f), new Vector3(.22f, .08f, .025f), Accent);
        Rod(BodyRoot, new Vector3(-.23f, 1.65f, -.2f), new Vector3(.27f, 1.01f, -.24f), .04f, Dark);
        Box(BodyRoot, new Vector3(.36f, .98f, -.045f), new Vector3(.27f, .34f, .23f), Main);
        Cone(left, new Vector3(-.065f, -.59f, -.15f), .072f, .072f, .24f, Glow);
        Cone(left, new Vector3(-.065f, -.42f, -.15f), .04f, .04f, .11f, Metal);
        Box(right, new Vector3(.075f, -.59f, -.1f), new Vector3(.24f, .07f, .32f), Bone);
    }

    private void HumanTorren(Node3D left, Node3D right)
    {
        HumanFace(false);
        Cone(BodyRoot, new Vector3(0, 1.83f, -.10f), .07f, .18f, .29f, Dark);
        Box(BodyRoot, new Vector3(0, 1.15f, -.275f), new Vector3(.5f, .78f, .08f), Accent);
        Rod(BodyRoot, new Vector3(-.25f, 1.65f, -.19f), new Vector3(-.19f, 1.39f, -.31f), .04f, Dark);
        Rod(BodyRoot, new Vector3(.25f, 1.65f, -.19f), new Vector3(.19f, 1.39f, -.31f), .04f, Dark);
        Orb(left, new Vector3(-.015f, -.12f, 0), new Vector3(.33f, .39f, .34f), Skin);
        Orb(right, new Vector3(.015f, -.12f, 0), new Vector3(.33f, .39f, .34f), Skin);
        Rod(right, new Vector3(.08f, -.76f, -.075f), new Vector3(.08f, -.12f, -.075f), .043f, Dark);
        Box(right, new Vector3(.08f, -.10f, -.075f), new Vector3(.42f, .19f, .23f), Metal);
        Box(BodyRoot, new Vector3(-.27f, .98f, -.2f), new Vector3(.14f, .25f, .08f), Dark);
    }

    private void HumanCael(Node3D left, Node3D right)
    {
        HumanHood(Bone, Skin);
        HumanCape(1.2f, Main);
        Box(BodyRoot, new Vector3(-.16f, 1.28f, -.29f), new Vector3(.11f, .73f, .04f), Bone);
        Box(BodyRoot, new Vector3(.16f, 1.28f, -.29f), new Vector3(.11f, .73f, .04f), Bone);
        Orb(BodyRoot, new Vector3(0, 1.46f, -.29f), new Vector3(.12f, .16f, .06f), Metal);
        Box(left, new Vector3(-.045f, -.53f, -.19f), new Vector3(.28f, .37f, .09f), Bone, new Vector3(0, 0, -10));
        Box(left, new Vector3(-.045f, -.53f, -.249f), new Vector3(.3f, .4f, .035f), Main, new Vector3(0, 0, -10));
        Box(left, new Vector3(-.045f, -.53f, -.272f), new Vector3(.055f, .21f, .018f), Metal);
        Rod(right, new Vector3(.07f, -.63f, -.075f), new Vector3(.07f, -.19f, -.075f), .026f, Metal);
        Orb(right, new Vector3(.07f, -.14f, -.075f), new Vector3(.12f, .17f, .12f), Glow);
    }

    private void HumanOris(Node3D left, Node3D right)
    {
        HumanFace();
        HumanCape(.71f, Main);
        Cone(BodyRoot, new Vector3(0, 2.22f, .015f), .29f, .13f, .17f, Main);
        Ring(BodyRoot, new Vector3(-.079f, 2.038f, -.223f), .044f, .061f, Metal, new Vector3(90, 0, 0));
        Ring(BodyRoot, new Vector3(.079f, 2.038f, -.223f), .044f, .061f, Metal, new Vector3(90, 0, 0));
        Box(BodyRoot, new Vector3(0, 2.038f, -.225f), new Vector3(.043f, .019f, .019f), Metal);
        Rod(BodyRoot, new Vector3(-.26f, 1.64f, -.2f), new Vector3(.26f, .95f, -.21f), .041f, Accent);
        Box(BodyRoot, new Vector3(.35f, .97f, .025f), new Vector3(.3f, .37f, .23f), Dark);
        Cone(left, new Vector3(-.07f, -.59f, -.15f), .08f, .08f, .45f, Bone, new Vector3(0, 0, 65));
        Box(right, new Vector3(.09f, -.6f, -.10f), new Vector3(.19f, .07f, .22f), Metal);
        Orb(right, new Vector3(.09f, -.555f, -.10f), new Vector3(.10f, .025f, .10f), Glow);
        Height = 2.45f;
    }

    private void HumanKesh(Node3D left, Node3D right)
    {
        HumanFace();
        HumanCape(.66f, Accent);
        Cone(BodyRoot, new Vector3(0, 2.2f, .025f), .25f, .13f, .16f, Accent);
        Box(BodyRoot, new Vector3(0, 1.71f, -.12f), new Vector3(.49f, .21f, .36f), Accent);
        Box(BodyRoot, new Vector3(0, 1.38f, .34f), new Vector3(.57f, .72f, .33f), Dark);
        Box(BodyRoot, new Vector3(0, 1.38f, .52f), new Vector3(.43f, .55f, .065f), Main);
        Cone(BodyRoot, new Vector3(0, 1.79f, .35f), .115f, .115f, .66f, Bone, new Vector3(0, 0, 90));
        Rod(BodyRoot, new Vector3(-.23f, 1.62f, -.21f), new Vector3(-.23f, 1.1f, -.24f), .038f, Dark);
        Rod(BodyRoot, new Vector3(.23f, 1.62f, -.21f), new Vector3(.23f, 1.1f, -.24f), .038f, Dark);
        Box(BodyRoot, new Vector3(-.33f, 1.0f, 0), new Vector3(.19f, .28f, .22f), Bone);
        Orb(left, new Vector3(-.065f, -.70f, -.10f), new Vector3(.25f, .32f, .21f), Main);
        Ring(right, new Vector3(.075f, -.76f, -.075f), .085f, .115f, Metal, new Vector3(90, 0, 0));
        Orb(right, new Vector3(.075f, -.9f, -.075f), new Vector3(.17f, .19f, .17f), Glow);
        Height = 2.4f;
    }
}
